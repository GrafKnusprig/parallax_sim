using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

[RequireComponent(typeof(SolarSystemParallaxManager))]
public class StellarParallaxManager : MonoBehaviour
{
    [Header("Gaia GDR3 Data Settings")]
    [Tooltip("Virtual render distance from nearest to farthest star (%)")]
    [SerializeField] private float renderDistanceRange = 50f;  // Distance in parsecs
    
    [Header("Parallax Settings")]
    [Tooltip("Show parallax motion based on player position (real-world parallax only)")]
    [SerializeField] private bool enableParallax = true;
    
    // Fixed to real-world parallax - no exaggeration for realistic stellar parallax
    private const float PARALLAX_EXAGGERATION = 1.0f;
    
    [Header("Star Rendering")]
    [SerializeField] private Material starMaterial; 
    [Tooltip("Base size for all stars (Unity units)")]
    [SerializeField] private float baseStarSize = 0.1f;
    [SerializeField] private Color starColor = Color.white;
    [Tooltip("Overall brightness for all stars (0-5x)")]
    [SerializeField] private float starBrightness = 1.0f;
    [Tooltip("Maximum stars to render per frame (count)")]
    [Range(0, 10000000)]
    [SerializeField] private int maxStarsPerFrame = 10000000;

    [Header("GPU Processing (Compute Shader)")]
    [Tooltip("Use GPU compute shader for star processing (fastest)")]
    [SerializeField] private bool useComputeShader = true;
    [Tooltip("Compute shader for star culling")]
    [SerializeField] private ComputeShader starCullingShader;
    [Tooltip("GPU-optimized star material (uses StructuredBuffer)")]
    [SerializeField] private Material starMaterialGPU;

    [Header("Parallel Processing (Burst/Jobs)")]
    [Tooltip("Use Burst-compiled Jobs for parallel star processing (fallback if no compute shader)")]
    [SerializeField] private bool useJobs = true;
    [Tooltip("Batch size for parallel job processing")]
    [SerializeField] private int jobBatchSize = 512;

    [Header("Parallax Approximation")]
    [Tooltip("Use fast parallax approximation for distant stars")]
    [SerializeField] private bool enableFastParallaxApprox = true;
    [Tooltip("Distance (parsecs) beyond which fast approximation is used")]
    [SerializeField] private float parallaxApproxDistanceParsecs = 50f;



    [Header("Speed-Based Star Scaling")]
    [Tooltip("Scale star distances down when player speed exceeds the threshold")]
    [SerializeField] private bool enableSpeedBasedStarScaling = true;
    [Tooltip("Speed (AU/s) where star scaling starts")]
    [SerializeField] private float speedScalingStartAuPerSec = 5f;
    [Tooltip("Scaling strength (higher = stronger compression)")]
    [SerializeField] private float speedScalingStrength = 0.05f;
    [Tooltip("Minimum star distance scale factor")]
    [SerializeField] private float minStarDistanceScale = 0.1f;
    [Tooltip("Keep the moving-scale look when speed drops to (near) zero")]
    [SerializeField] private bool holdScaleWhenStopped = true;
    [Tooltip("Speed (AU/s) considered stopped for holding scale")]
    [SerializeField] private float stoppedSpeedThresholdAuPerSec = 0.05f;

    
    // Constants
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const float AU_TO_PARSEC = 1f / PARSEC_TO_AU;  // 1 AU = 1/206,264.806 parsecs
    
    // Star data structure for GDR3 format
    private struct StarData
    {
        public Vector3 positionParsecs;  // 3D position in parsecs (galactic coordinates)
        public Vector3 direction;         // Unit direction from Sun
        public float distance;           // Distance from Sun in parsecs
        public float invDistance;         // 1 / distance
        public float magnitude;          // G magnitude for brightness
        public int originalIndex;        // Original index for debugging
    }
    
    // SoA NativeArrays for Burst/Jobs processing (persistent allocations)
    private NativeArray<float3> nativePositionsParsecs;
    private NativeArray<float3> nativeDirections;
    private NativeArray<float> nativeDistances;
    private NativeArray<float> nativeInvDistances;
    private bool nativeArraysAllocated = false;
    
    // Per-frame output arrays (persistent allocations, sized to star count)
    private NativeArray<float3> nativeOutputWorldPositions; // Indexed by star index
    private NativeArray<int> nativeVisibilityFlags;          // 1 = visible, 0 = culled
    private int outputArrayCapacity = 0;
    
    // Reusable managed arrays for rendering (avoid GC allocations)
    private Matrix4x4[] renderBatchBuffer;
    private const int RENDER_BATCH_SIZE = 1023; // Unity's limit for Graphics.DrawMeshInstanced
    
    // GPU Compute Shader resources
    private ComputeBuffer starInputBuffer;       // All stars (uploaded once)
    private ComputeBuffer visibleStarsBuffer;    // Visible stars output (AppendBuffer)
    private ComputeBuffer indirectArgsBuffer;    // For DrawMeshInstancedIndirect
    private bool computeBuffersAllocated = false;
    private int computeKernelCull;
    private uint[] indirectArgs = new uint[5];   // Reusable args array
    
    // GPU input struct (must match compute shader)
    private struct StarInputGPU
    {
        public Vector4 posAndDist;     // xyz = positionParsecs, w = distance
        public Vector4 dirAndInvDist;  // xyz = direction,       w = invDistance
        
        public static int Size => sizeof(float) * 8; // 4 + 4 = 8 floats (32 bytes)
    }
    
    // Data storage
    private List<StarData> allStars = new List<StarData>();
    private List<StarData> visibleStars = new List<StarData>();
    private List<Vector3> visibleStarWorldPositions = new List<Vector3>();
    private Dictionary<int, StarData> nearestStars = new Dictionary<int, StarData>();
    
    // Rendering components
    private Mesh starMesh;
    private Vector3[] starPositions;
    private Matrix4x4[] starMatrices;
    private MaterialPropertyBlock materialPropertyBlock;
    private GameObject starParent;
    
    // References
    private SolarSystemParallaxManager solarSystemManager;
    private Camera playerCamera;
    
    // State
    private bool starsLoaded = false;
    private float minStarDistance = float.MaxValue;
    private float maxStarDistance = 0f;
    private Vector3 lastCameraPosition;
    private Vector3 lastCameraForward;
    private float lastRenderDistance = -1f;
    private float lastStarDistanceScale = 1f;
    private uint totalStars = 0;
    
    // Performance optimization
    private const float FOV_CULLING_MARGIN = 45f; // Large margin for seamless rendering
    
    // ============================================================================
    // BURST JOB: Parallel star culling and world position calculation
    // ============================================================================
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    private struct StarCullingJob : IJobParallelFor
    {
        // Input: Star data (SoA layout for cache efficiency)
        [ReadOnly] public NativeArray<float3> positionsParsecs;
        [ReadOnly] public NativeArray<float3> directions;
        [ReadOnly] public NativeArray<float> distances;
        [ReadOnly] public NativeArray<float> invDistances;
        
        // Input: Culling parameters
        public float3 cameraPos;
        public float3 cameraForward;
        public float3 effectivePlayerPosAu;
        public float horizonRadius;
        public float cosHalfFOV;
        public float maxDistance;
        public float auToParsec;
        public float parallaxApproxDistanceParsecs;
        public int starCount;
        public int maxStarsPerFrame;
        
        // Input: Feature flags
        public bool enableParallax;
        public bool enableFastParallaxApprox;
        
        // Output: Visibility flags and world positions (indexed by star index)
        // Each star writes to its own index, then we compact on main thread
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<float3> outputWorldPositions;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> visibilityFlags; // 1 = visible, 0 = culled
        
        public void Execute(int index)
        {
            // Stride-based sampling: skip stars uniformly to reduce density
            // This matches the compute shader's approach (lines 54-59 in StarCulling.compute)
            if (maxStarsPerFrame > 0 && starCount > maxStarsPerFrame)
            {
                int stride = starCount / maxStarsPerFrame;
                if (stride > 1 && (index % stride) != 0)
                {
                    visibilityFlags[index] = 0;
                    return;
                }
            }
            
            float distance = distances[index];
            
            float3 positionParsecs = positionsParsecs[index];
            
            // Fast FOV culling (matches compute shader logic)
            float3 worldPos;
            float3 direction = directions[index];
            
            if (!enableParallax)
            {
                // No parallax: simple direction check (matches compute shader lines 71-72)
                if (math.dot(direction, cameraForward) < cosHalfFOV)
                {
                    visibilityFlags[index] = 0;
                    return;
                }
                
                worldPos = direction * horizonRadius;
            }
            else
            {
                // Parallax enabled: Calculate vector P from player to star
                // P = StarPos - PlayerPos (All in Parsecs)
                // Matches compute shader lines 78-95
                float3 P = positionParsecs - (effectivePlayerPosAu * auToParsec);
                
                // Fast Squared FOV Check
                // We defer costly sqrt/normalize until after culling
                float dotF = math.dot(P, cameraForward);
                
                // 1. Cull if behind camera (assuming FOV < 180)
                if (dotF <= 0)
                {
                    visibilityFlags[index] = 0;
                    return;
                }
                
                // 2. Cull if outside field of view cone
                // Check: dot(P, F) < cos(theta) * length(P)
                // Optimization: dotF^2 < cos^2 * dot(P, P)
                float distSq = math.dot(P, P);
                if (dotF * dotF < (cosHalfFOV * cosHalfFOV) * distSq)
                {
                    visibilityFlags[index] = 0;
                    return;
                }
                
                // Star is visible: Compute final world position
                // rsqrt is fast on CPU with Burst as well
                float invDist = math.rsqrt(distSq);
                float3 dir = P * invDist;
                
                worldPos = dir * horizonRadius;
            }
            
            // Validate world position
            if (math.any(math.isnan(worldPos)) || math.any(math.isinf(worldPos)))
            {
                visibilityFlags[index] = 0;
                return;
            }
            
            // Star is visible - write world position and mark as visible
            outputWorldPositions[index] = worldPos;
            visibilityFlags[index] = 1;
        }
        
        private float3 CalculateWorldPosition(int index, float3 originalDirection, float3 positionParsecs, float distance)
        {
            if (!enableParallax)
            {
                return originalDirection * horizonRadius;
            }
            
            float invDist = invDistances[index];
            
            // Fast approximation for distant stars
            if (enableFastParallaxApprox && distance >= parallaxApproxDistanceParsecs)
            {
                float3 playerPosParsecsLocal = effectivePlayerPosAu * auToParsec;
                float3 approxDir = math.normalize(originalDirection - (playerPosParsecsLocal * invDist));
                if (math.any(math.isnan(approxDir)) || math.any(math.isinf(approxDir)))
                    approxDir = originalDirection;
                return approxDir * horizonRadius;
            }
            
            // Full precision parallax calculation
            float3 playerPosParsecs = effectivePlayerPosAu * auToParsec;
            float3 playerToStar = positionParsecs - playerPosParsecs;
            float actualDistance = math.length(playerToStar);
            
            if (actualDistance < 0.001f)
                return originalDirection * horizonRadius;
            
            float3 apparentDirection = playerToStar / actualDistance;
            
            if (math.any(math.isnan(apparentDirection)) || math.any(math.isinf(apparentDirection)))
                apparentDirection = originalDirection;
            
            return apparentDirection * horizonRadius;
        }
    }
    
    // ============================================================================
    // BURST JOB: Build Matrix4x4 array from world positions (for rendering)
    // ============================================================================
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    private struct BuildMatricesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> worldPositions;
        [WriteOnly] public NativeArray<Matrix4x4> matrices;
        
        public void Execute(int index)
        {
            float3 pos = worldPositions[index];
            matrices[index] = Matrix4x4.TRS(
                new Vector3(pos.x, pos.y, pos.z),
                Quaternion.identity,
                Vector3.one
            );
        }
    }
    


    private void Awake()
    {
        solarSystemManager = GetComponent<SolarSystemParallaxManager>();
        if (solarSystemManager == null)
        {
            Debug.LogError("StellarParallaxManager requires SolarSystemParallaxManager component!");
            enabled = false;
            return;
        }
    }
    
    private void Start()
    {
        playerCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
        
        CreateStarParent();
        InitializeStarMesh();
        StartCoroutine(LoadGDR3DataAsync());
    }
    
    private void Update()
    {
        if (!starsLoaded) return;
        
        Vector3 currentCameraPos = playerCamera.transform.position;
        Vector3 currentCameraForward = playerCamera.transform.forward;
        
        // Update only if camera moved significantly or render distance changed
        bool shouldUpdate = Vector3.Distance(currentCameraPos, lastCameraPosition) > 0.5f ||
                           Vector3.Angle(currentCameraForward, lastCameraForward) > 2f ||
                           Mathf.Abs(renderDistanceRange - lastRenderDistance) > 0.1f ||
                           (enableParallax && solarSystemManager.playerRealPosAu.localOffset.magnitude > 0.001); // Update for parallax
        
        if (shouldUpdate)
        {
            UpdateVisibleStars();
            
            // GPU path handles rendering differently - no need for UpdateStarRendering
            if (!IsUsingComputeShader())
            {
                UpdateStarRendering();
            }
            
            lastCameraPosition = currentCameraPos;
            lastCameraForward = currentCameraForward;
            lastRenderDistance = renderDistanceRange;
        }
        
        RenderStars();
    }
    
    private void CreateStarParent()
    {
        if (starParent != null) return;
        
        starParent = new GameObject("Gaia_GDR3_Stars");
        starParent.transform.SetParent(transform, false);
        starParent.transform.localPosition = Vector3.zero;
        starParent.transform.localRotation = Quaternion.identity;
        starParent.transform.localScale = Vector3.one;
    }
    
    private void InitializeStarMesh()
    {
        starMesh = new Mesh();
        starMesh.name = "StarPointMesh";
        
        // Create simple quad for point sprite rendering
        Vector3[] vertices = {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3( 0.5f, -0.5f, 0),
            new Vector3( 0.5f,  0.5f, 0),
            new Vector3(-0.5f,  0.5f, 0)
        };
        
        Vector2[] uv = {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        
        int[] triangles = { 0, 1, 2, 2, 3, 0 };
        
        starMesh.vertices = vertices;
        starMesh.uv = uv;
        starMesh.triangles = triangles;
        starMesh.RecalculateNormals();
        starMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
    }
    
    private IEnumerator LoadGDR3DataAsync()
    {
        string datasetPath = solarSystemManager.StarDatasetPath;
        string datasetName = solarSystemManager.StarDatasetName;
        Debug.Log($"Loading {datasetName} stellar data from binary file: {datasetPath}");
        
        allStars.Clear();
        
        string filePath = Path.Combine(Application.streamingAssetsPath, datasetPath);
        
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"Gaia binary file not found: {filePath}");
            yield break;
        }
        
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // Read header: number of stars (uint32)
            totalStars = reader.ReadUInt32();
            Debug.Log($"Loading {totalStars:N0} stars from binary file...");
            
            allStars.Capacity = (int)totalStars;
            
            int batchSize = 10000;
            int processed = 0;
            
            for (uint i = 0; i < totalStars; i++)
            {
                // Read binary record: RA, DEC, Distance, Magnitude (all float32)
                float ra_deg = reader.ReadSingle();
                float dec_deg = reader.ReadSingle();
                float distance_pc = reader.ReadSingle();
                float magnitude = reader.ReadSingle();
                
                // Skip invalid data
                if (distance_pc <= 0 || float.IsNaN(distance_pc) || float.IsInfinity(distance_pc))
                    continue;
                
                // Convert spherical coordinates (RA, Dec) to Cartesian
                float ra_rad = ra_deg * Mathf.Deg2Rad;
                float dec_rad = dec_deg * Mathf.Deg2Rad;
                
                // Convert to cartesian coordinates (distance * unit vector)
                float cos_dec = Mathf.Cos(dec_rad);
                float x = distance_pc * cos_dec * Mathf.Cos(ra_rad);
                float y = distance_pc * Mathf.Sin(dec_rad);
                float z = distance_pc * cos_dec * Mathf.Sin(ra_rad);
                
                StarData star = new StarData
                {
                    positionParsecs = new Vector3(x, y, z),
                    direction = new Vector3(x, y, z).normalized,
                    distance = distance_pc,
                    invDistance = 1f / distance_pc,
                    magnitude = magnitude,
                    originalIndex = (int)i
                };
                
                allStars.Add(star);
                
                // Update distance range
                if (star.distance < minStarDistance) minStarDistance = star.distance;
                if (star.distance > maxStarDistance) maxStarDistance = star.distance;
                
                processed++;
                
                // Yield periodically for smooth loading
                if (processed % batchSize == 0)
                    yield return null;
            }
        }
        
        Debug.Log($"Total stars loaded: {allStars.Count:N0}");
        Debug.Log($"Distance range: {minStarDistance:F2} - {maxStarDistance:F2} parsecs");
        
        // Allocate NativeArrays for Burst/Jobs processing
        // We do this in chunks to avoid blocking main thread
        yield return StartCoroutine(AllocateNativeArraysFromStarDataAsync());
        EnsureOutputArrayCapacity(maxStarsPerFrame);
        
        // Allocate ComputeBuffers for GPU processing
        yield return StartCoroutine(AllocateComputeBuffersAsync());
        
        starsLoaded = true;
        UpdateVisibleStars();
    }
    
    private void UpdateVisibleStars()
    {
        if (!starsLoaded) return;
        
        if (useComputeShader && computeBuffersAllocated && starCullingShader != null)
        {
            UpdateVisibleStarsGPU();
        }
        else if (useJobs && nativeArraysAllocated)
        {
            UpdateVisibleStarsJobs();
        }
        else
        {
            UpdateVisibleStarsCPU();
        }
    }
    
    // ============================================================================
    // GPU PATH: Compute shader processing (fastest)
    // ============================================================================
    private void UpdateVisibleStarsGPU()
    {
        float starDistanceScale = GetStarDistanceScaleFactor();
        
        Vector3 cameraPos = playerCamera.transform.position;
        Vector3 cameraForward = playerCamera.transform.forward;
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        Vector3d playerPosRelativeToSunAu = solarSystemManager.GetPlayerPositionRelativeToSun();
        Vector3 playerPosAu = (Vector3)playerPosRelativeToSunAu;
        Vector3 effectivePlayerPosAu = playerPosAu * starDistanceScale;
        
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        float cosHalfFOV = Mathf.Cos(halfFOVWithMargin * Mathf.Deg2Rad);
        
        // Reset append buffer
        visibleStarsBuffer.SetCounterValue(0);
        
        // Set compute shader uniforms
        starCullingShader.SetBuffer(computeKernelCull, "_AllStars", starInputBuffer);
        starCullingShader.SetBuffer(computeKernelCull, "_VisibleStars", visibleStarsBuffer);
        
        starCullingShader.SetVector("_CameraPos", cameraPos);
        starCullingShader.SetVector("_CameraForward", cameraForward);
        starCullingShader.SetVector("_PlayerPosAu", effectivePlayerPosAu);
        starCullingShader.SetFloat("_HorizonRadius", horizonRadius);
        starCullingShader.SetFloat("_CosHalfFOV", cosHalfFOV);
        starCullingShader.SetFloat("_AuToParsec", AU_TO_PARSEC);
        
        starCullingShader.SetInt("_EnableParallax", enableParallax ? 1 : 0);
        starCullingShader.SetInt("_EnableFastParallaxApprox", enableFastParallaxApprox ? 1 : 0);
        starCullingShader.SetFloat("_ParallaxApproxDistanceParsecs", parallaxApproxDistanceParsecs);
        
        starCullingShader.SetInt("_StarCount", allStars.Count);
        starCullingShader.SetInt("_MaxStarsPerFrame", maxStarsPerFrame);
        
        // Dispatch compute shader (256 threads per group)
        int threadGroups = Mathf.CeilToInt(allStars.Count / 256f);
        starCullingShader.Dispatch(computeKernelCull, threadGroups, 1, 1);
        
        // Copy visible count to indirect args buffer (GPU-side, no CPU readback!)
        ComputeBuffer.CopyCount(visibleStarsBuffer, indirectArgsBuffer, sizeof(uint));
    }
    
    private void RenderStarsGPU()
    {
        if (!computeBuffersAllocated || starMaterialGPU == null) return;
        
        // Set the visible stars buffer on the material
        starMaterialGPU.SetBuffer("_VisibleStars", visibleStarsBuffer);
        starMaterialGPU.SetColor("_Color", starColor);
        starMaterialGPU.SetFloat("_Brightness", starBrightness);
        starMaterialGPU.SetFloat("_Size", baseStarSize);
        
        // Render using indirect args (GPU determines instance count)
        Graphics.DrawMeshInstancedIndirect(
            starMesh,
            0,
            starMaterialGPU,
            new Bounds(Vector3.zero, Vector3.one * 100000f), // Large bounds to always render
            indirectArgsBuffer
        );
    }
    
    // ============================================================================
    // JOBS PATH: Burst-compiled parallel processing
    // ============================================================================
    private int lastJobVisibleCount = 0;
    private NativeArray<Matrix4x4> nativeMatrices;
    private NativeArray<float3> nativeCompactedPositions;  // Compacted visible positions for matrix building
    
    private void UpdateVisibleStarsJobs()
    {
        float starDistanceScale = GetStarDistanceScaleFactor();
        float maxDistance = Mathf.Lerp(minStarDistance, maxStarDistance, renderDistanceRange / 100f);
        
        Vector3 cameraPos = playerCamera.transform.position;
        Vector3 cameraForward = playerCamera.transform.forward;
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        Vector3d playerPosRelativeToSunAu = solarSystemManager.GetPlayerPositionRelativeToSun();
        Vector3 playerPosAu = (Vector3)playerPosRelativeToSunAu;
        Vector3 effectivePlayerPosAu = playerPosAu * starDistanceScale;

        
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        float cosHalfFOV = Mathf.Cos(halfFOVWithMargin * Mathf.Deg2Rad);
        


        
        int starCount = allStars.Count;
        
        // Ensure output arrays are ready (sized to star count)
        EnsureOutputArrayCapacity(starCount);
        
        // Create and schedule the culling job
        var cullingJob = new StarCullingJob
        {
            positionsParsecs = nativePositionsParsecs,
            directions = nativeDirections,
            distances = nativeDistances,
            invDistances = nativeInvDistances,
            
            cameraPos = new float3(cameraPos.x, cameraPos.y, cameraPos.z),
            cameraForward = new float3(cameraForward.x, cameraForward.y, cameraForward.z),
            effectivePlayerPosAu = new float3(effectivePlayerPosAu.x, effectivePlayerPosAu.y, effectivePlayerPosAu.z),
            horizonRadius = horizonRadius,
            cosHalfFOV = cosHalfFOV,
            maxDistance = maxDistance,
            auToParsec = AU_TO_PARSEC,
            parallaxApproxDistanceParsecs = parallaxApproxDistanceParsecs,
            starCount = starCount,
            maxStarsPerFrame = maxStarsPerFrame,
            
            enableParallax = enableParallax,
            enableFastParallaxApprox = enableFastParallaxApprox,
            
            outputWorldPositions = nativeOutputWorldPositions,
            visibilityFlags = nativeVisibilityFlags
        };
        
        // Schedule and complete the job
        JobHandle cullingHandle = cullingJob.Schedule(starCount, jobBatchSize);
        cullingHandle.Complete();
        
        // Compact visible stars on main thread (stream compaction)
        // This is fast because we're just reading flags and copying positions
        lastJobVisibleCount = 0;
        
        // Ensure compacted array is ready
        if (!nativeCompactedPositions.IsCreated || nativeCompactedPositions.Length < maxStarsPerFrame)
        {
            if (nativeCompactedPositions.IsCreated) nativeCompactedPositions.Dispose();
            nativeCompactedPositions = new NativeArray<float3>(maxStarsPerFrame, Allocator.Persistent);
        }
        
        // Compact: gather visible star positions
        for (int i = 0; i < starCount && lastJobVisibleCount < maxStarsPerFrame; i++)
        {
            if (nativeVisibilityFlags[i] == 1)
            {
                nativeCompactedPositions[lastJobVisibleCount] = nativeOutputWorldPositions[i];
                lastJobVisibleCount++;
            }
        }
        
        // Update matrices for rendering
        if (lastJobVisibleCount > 0)
        {
            // Ensure matrices array is properly sized
            if (!nativeMatrices.IsCreated || nativeMatrices.Length < lastJobVisibleCount)
            {
                if (nativeMatrices.IsCreated) nativeMatrices.Dispose();
                nativeMatrices = new NativeArray<Matrix4x4>(Mathf.Max(lastJobVisibleCount, maxStarsPerFrame), Allocator.Persistent);
            }
            
            var matrixJob = new BuildMatricesJob
            {
                worldPositions = nativeCompactedPositions,
                matrices = nativeMatrices
            };
            
            JobHandle matrixHandle = matrixJob.Schedule(lastJobVisibleCount, jobBatchSize);
            matrixHandle.Complete();
            
            // Copy to managed array for rendering (reuse existing array to avoid GC)
            if (starMatrices == null || starMatrices.Length < lastJobVisibleCount)
            {
                starMatrices = new Matrix4x4[Mathf.Max(lastJobVisibleCount, maxStarsPerFrame)];
            }
            
            NativeArray<Matrix4x4>.Copy(nativeMatrices, 0, starMatrices, 0, lastJobVisibleCount);
        }
    }
    
    // ============================================================================
    // CPU PATH: Original single-threaded implementation
    // ============================================================================
    private void UpdateVisibleStarsCPU()
    {
        visibleStars.Clear();
        visibleStarWorldPositions.Clear();
        
        float starDistanceScale = GetStarDistanceScaleFactor();

        // Calculate render distance limits
        float maxDistance = Mathf.Lerp(minStarDistance, maxStarDistance, renderDistanceRange / 100f);
        
        // Get camera frustum for culling
        Vector3 cameraPos = playerCamera.transform.position;
        Vector3 cameraForward = playerCamera.transform.forward;
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        // Get player position RELATIVE TO SUN for stellar coordinates
        // Stars are positioned with Sun at origin (0,0,0), so we need player offset from Sun
        Vector3d playerPosRelativeToSunAu = solarSystemManager.GetPlayerPositionRelativeToSun();
        Vector3 playerPosAu = (Vector3)playerPosRelativeToSunAu;
        Vector3 effectivePlayerPosAu = playerPosAu * starDistanceScale;
        
        // Calculate effective FOV with generous margin
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        float cosHalfFOV = Mathf.Cos(halfFOVWithMargin * Mathf.Deg2Rad);
        
        int processed = 0;
        HashSet<int> visibleIndices = new HashSet<int>();
        
        // Calculate stride for uniform sampling (matches compute shader logic)
        int starCount = allStars.Count;
        int stride = 1;
        if (maxStarsPerFrame > 0 && starCount > maxStarsPerFrame)
        {
            stride = starCount / maxStarsPerFrame;
        }
        
        int starIndex = 0;
        foreach (StarData star in allStars)
        {
            // Stride-based sampling: skip stars uniformly to reduce density
            // This matches the compute shader's approach (lines 54-59 in StarCulling.compute)
            if (stride > 1 && (starIndex % stride) != 0)
            {
                starIndex++;
                continue;
            }
            
            // Early distance culling - cheapest check first
            if (star.distance > maxDistance)
            {
                starIndex++;
                continue;
            }




            
            // Fast FOV culling (matches compute shader logic)
            Vector3 worldPos;
            if (!enableParallax)
            {
                // No parallax: simple direction check
                Vector3 dir = star.direction;
                
                // Fast Pre-Check (No Parallax) - matches compute shader lines 71-72
                if (Vector3.Dot(dir, cameraForward) < cosHalfFOV)
                {
                    starIndex++;
                    continue;
                }
                
                worldPos = dir * horizonRadius;
            }
            else
            {
                // Parallax enabled: Calculate vector P from player to star
                // P = StarPos - PlayerPos (All in Parsecs)
                // Matches compute shader lines 78-95
                Vector3 P = star.positionParsecs - (effectivePlayerPosAu * AU_TO_PARSEC);
                
                // Fast Squared FOV Check
                // We defer costly sqrt/normalize until after culling
                float dotF = Vector3.Dot(P, cameraForward);
                
                // 1. Cull if behind camera (assuming FOV < 180)
                if (dotF <= 0)
                {
                    starIndex++;
                    continue;
                }
                
                // 2. Cull if outside field of view cone
                // Check: dot(P, F) < cos(theta) * length(P)
                // Optimization: dotF^2 < cos^2 * dot(P, P)
                float distSq = Vector3.Dot(P, P);
                if (dotF * dotF < (cosHalfFOV * cosHalfFOV) * distSq)
                {
                    starIndex++;
                    continue;
                }
                
                // Star is visible: Compute final world position
                float invDist = 1f / Mathf.Sqrt(distSq);
                Vector3 dir = P * invDist;
                
                worldPos = dir * horizonRadius;
            }
            
            // Skip stars with invalid world positions
            if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.y) || float.IsNaN(worldPos.z) ||
                float.IsInfinity(worldPos.x) || float.IsInfinity(worldPos.y) || float.IsInfinity(worldPos.z))
            {
                starIndex++;
                continue;
            }
            
            visibleStars.Add(star);
            visibleStarWorldPositions.Add(worldPos);
            visibleIndices.Add(star.originalIndex);
            starIndex++;
            
            // Limit stars per frame for performance
            if (visibleStars.Count >= maxStarsPerFrame)
                break;
        }


    }
    
    private void UpdateStarRendering()
    {
        // Skip for Jobs path - matrices are built in UpdateVisibleStarsJobs
        if (useJobs && nativeArraysAllocated)
            return;
        
        int starCount = visibleStars.Count;
        
        // Initialize or resize arrays if needed
        if (starPositions == null || starPositions.Length != starCount)
        {
            starPositions = new Vector3[starCount];
            starMatrices = new Matrix4x4[starCount];
        }
        
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        // Get player position RELATIVE TO SUN for stellar coordinates
        // Stars are positioned with Sun at origin (0,0,0), so we need player offset from Sun
        Vector3d playerPosRelativeToSunAu = solarSystemManager.GetPlayerPositionRelativeToSun();
        Vector3 playerPosAu = (Vector3)playerPosRelativeToSunAu;
        
        // Update positions and matrices
        for (int i = 0; i < starCount; i++)
        {
            Vector3 worldPos = visibleStarWorldPositions[i];
            starPositions[i] = worldPos;
            starMatrices[i] = Matrix4x4.TRS(worldPos, Quaternion.identity, Vector3.one);
        }
    }
    
    private Vector3 CalculateStarWorldPosition(StarData star, Vector3 originalDirection, float horizonRadius, Vector3 playerPosAu)
    {
        if (!enableParallax)
        {
            // No parallax - just place on sphere
            return originalDirection * horizonRadius;
        }

        // Fast small-angle parallax approximation for distant stars
        if (enableFastParallaxApprox && star.distance >= parallaxApproxDistanceParsecs)
        {
            Vector3 playerPosParsecsLocal = playerPosAu * AU_TO_PARSEC;
            Vector3 approxDir = (star.direction - (playerPosParsecsLocal * star.invDistance)).normalized;
            if (float.IsNaN(approxDir.x) || float.IsNaN(approxDir.y) || float.IsNaN(approxDir.z) ||
                float.IsInfinity(approxDir.x) || float.IsInfinity(approxDir.y) || float.IsInfinity(approxDir.z))
            {
                approxDir = originalDirection;
            }
            return approxDir * horizonRadius;
        }
        
        // Use double precision for accurate parallax calculations
        // Star position is in parsecs, player position is in AU
        // Convert player position to parsecs for consistent units
        Vector3d playerPosParsecs = new Vector3d(
            playerPosAu.x * AU_TO_PARSEC,
            playerPosAu.y * AU_TO_PARSEC,
            playerPosAu.z * AU_TO_PARSEC
        );
        
        Vector3d starPosParsecs64 = new Vector3d(
            star.positionParsecs.x,
            star.positionParsecs.y,
            star.positionParsecs.z
        );
        
        // Calculate vector from PLAYER to star (not from Sun to star)
        Vector3d playerToStar = starPosParsecs64 - playerPosParsecs;
        
        // Calculate actual distance from player to star
        double actualDistance = playerToStar.magnitude;
        
        // Bounds checking for extreme values
        if (actualDistance < 0.001)
        {
            // If too close or invalid, fallback to original direction
            return originalDirection * horizonRadius;
        }
        
        // Calculate the direction from player to star
        Vector3d apparentDir64 = playerToStar.normalized;
        
        // Convert back to Vector3 with precision check
        Vector3 apparentDirection = new Vector3(
            (float)apparentDir64.x,
            (float)apparentDir64.y,
            (float)apparentDir64.z
        );
        
        // Final bounds check for NaN/Infinity
        if (float.IsNaN(apparentDirection.x) || float.IsNaN(apparentDirection.y) || float.IsNaN(apparentDirection.z) ||
            float.IsInfinity(apparentDirection.x) || float.IsInfinity(apparentDirection.y) || float.IsInfinity(apparentDirection.z))
        {
            // Fallback to original direction if calculation failed
            apparentDirection = originalDirection;
        }
        
        return apparentDirection * horizonRadius;
    }

    private float GetStarDistanceScaleFactor()
    {
        if (!enableSpeedBasedStarScaling || solarSystemManager == null)
            return 1f;

        float speed = solarSystemManager.ActualSpeedAuPerSec;
        if (holdScaleWhenStopped && speed <= stoppedSpeedThresholdAuPerSec)
            return lastStarDistanceScale;
        if (speed <= speedScalingStartAuPerSec)
            return 1f;

        float excess = speed - speedScalingStartAuPerSec;
        float scale = 1f / (1f + excess * speedScalingStrength);
        lastStarDistanceScale = Mathf.Clamp(scale, minStarDistanceScale, 1f);
        return lastStarDistanceScale;
    }
    
    private void RenderStars()
    {
        // Use GPU path if available
        if (useComputeShader && computeBuffersAllocated && starCullingShader != null && starMaterialGPU != null)
        {
            RenderStarsGPU();
            return;
        }
        
        // Fallback to CPU/Jobs path
        int starCount;
        if (useJobs && nativeArraysAllocated)
        {
            starCount = lastJobVisibleCount;
        }
        else
        {
            starCount = visibleStars.Count;
        }
        
        if (starCount == 0 || starMatrices == null || starMaterial == null)
            return;
        
        // Ensure materialPropertyBlock is initialized
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
        
        // Set material properties
        materialPropertyBlock.SetColor("_Color", starColor);
        materialPropertyBlock.SetFloat("_Size", baseStarSize);
        materialPropertyBlock.SetFloat("_Brightness", starBrightness);
        
        // Ensure render batch buffer exists
        if (renderBatchBuffer == null || renderBatchBuffer.Length < RENDER_BATCH_SIZE)
            renderBatchBuffer = new Matrix4x4[RENDER_BATCH_SIZE];
        
        // Render stars in batches (Unity has limits on instanced rendering)
        for (int startIndex = 0; startIndex < starCount; startIndex += RENDER_BATCH_SIZE)
        {
            int count = Mathf.Min(RENDER_BATCH_SIZE, starCount - startIndex);
            
            // Copy to reusable batch buffer (avoids GC allocation per frame)
            Array.Copy(starMatrices, startIndex, renderBatchBuffer, 0, count);
            
            Graphics.DrawMeshInstanced(
                starMesh,
                0,
                starMaterial,
                renderBatchBuffer,
                count,
                materialPropertyBlock
            );
        }
    }
    
    private void OnValidate()
    {
        // Clamp render distance to valid range
        renderDistanceRange = Mathf.Clamp(renderDistanceRange, 0f, 100f);
        
        // Clamp brightness setting
        starBrightness = Mathf.Clamp(starBrightness, 0.1f, 5f);
        
        // Clamp max stars per frame
        // Allow up to 20M for full dataset rendering
        maxStarsPerFrame = Mathf.Clamp(maxStarsPerFrame, 100, 20000000);



        speedScalingStartAuPerSec = Mathf.Max(0f, speedScalingStartAuPerSec);
        speedScalingStrength = Mathf.Max(0f, speedScalingStrength);
        minStarDistanceScale = Mathf.Clamp(minStarDistanceScale, 0.01f, 1f);
        stoppedSpeedThresholdAuPerSec = Mathf.Max(0f, stoppedSpeedThresholdAuPerSec);
        parallaxApproxDistanceParsecs = Mathf.Max(0f, parallaxApproxDistanceParsecs);
        
        // Job settings
        jobBatchSize = Mathf.Clamp(jobBatchSize, 32, 2048);
    }
    
    private void OnDestroy()
    {
        if (starMesh != null)
        {
            DestroyImmediate(starMesh);
        }
        
        DisposeNativeArrays();
        DisposeComputeBuffers();
    }
    
    private void DisposeNativeArrays()
    {
        if (nativeArraysAllocated)
        {
            if (nativePositionsParsecs.IsCreated) nativePositionsParsecs.Dispose();
            if (nativeDirections.IsCreated) nativeDirections.Dispose();
            if (nativeDistances.IsCreated) nativeDistances.Dispose();
            if (nativeInvDistances.IsCreated) nativeInvDistances.Dispose();
            nativeArraysAllocated = false;
        }
        
        if (nativeOutputWorldPositions.IsCreated) nativeOutputWorldPositions.Dispose();
        if (nativeVisibilityFlags.IsCreated) nativeVisibilityFlags.Dispose();
        if (nativeCompactedPositions.IsCreated) nativeCompactedPositions.Dispose();
        if (nativeMatrices.IsCreated) nativeMatrices.Dispose();
        outputArrayCapacity = 0;
    }
    
    private IEnumerator AllocateNativeArraysFromStarDataAsync()
    {
        if (nativeArraysAllocated)
            DisposeNativeArrays();
        
        int count = allStars.Count;
        if (count == 0) yield break;
        
        nativePositionsParsecs = new NativeArray<float3>(count, Allocator.Persistent);
        nativeDirections = new NativeArray<float3>(count, Allocator.Persistent);
        nativeDistances = new NativeArray<float>(count, Allocator.Persistent);
        nativeInvDistances = new NativeArray<float>(count, Allocator.Persistent);
        
        int batchSize = 100000;
        int processed = 0;

        for (int i = 0; i < count; i++)
        {
            StarData star = allStars[i];
            nativePositionsParsecs[i] = new float3(star.positionParsecs.x, star.positionParsecs.y, star.positionParsecs.z);
            nativeDirections[i] = new float3(star.direction.x, star.direction.y, star.direction.z);
            nativeDistances[i] = star.distance;
            nativeInvDistances[i] = star.invDistance;

            processed++;
            if (processed % batchSize == 0)
                yield return null;
        }
        
        nativeArraysAllocated = true;
        Debug.Log($"[StellarParallaxManager] Allocated NativeArrays for {count:N0} stars (Burst/Jobs ready)");
    }
    
    private IEnumerator AllocateComputeBuffersAsync()
    {
        if (starCullingShader == null)
        {
            Debug.LogWarning("[StellarParallaxManager] No compute shader assigned, GPU path disabled");
            yield break;
        }
        
        DisposeComputeBuffers();
        
        int count = allStars.Count;
        if (count == 0) yield break;
        
        // Get kernel
        computeKernelCull = starCullingShader.FindKernel("CullStars");
        
        yield return null; // Yield before big allocation

        // Create input buffer (uploaded once with all star data)
        starInputBuffer = new ComputeBuffer(count, StarInputGPU.Size);
        
        // Fill input buffer in chunks
        StarInputGPU[] inputData = new StarInputGPU[count];
        int batchSize = 100000;
        int processed = 0;

        for (int i = 0; i < count; i++)
        {
            StarData star = allStars[i];
            inputData[i] = new StarInputGPU
            {
                posAndDist = new Vector4(star.positionParsecs.x, star.positionParsecs.y, star.positionParsecs.z, star.distance),
                dirAndInvDist = new Vector4(star.direction.x, star.direction.y, star.direction.z, star.invDistance)
            };

            processed++;
            if (processed % batchSize == 0)
                yield return null;
        }

        // Upload to GPU
        starInputBuffer.SetData(inputData);
        
        // Discard managed array to free memory
        inputData = null;
        GC.Collect();
        
        yield return null;
        
        // Create visible stars output buffer (AppendBuffer)
        // Size for worst case: all stars visible (capped at maxStarsPerFrame)
        int maxVisible = Mathf.Min(count, maxStarsPerFrame);
        visibleStarsBuffer = new ComputeBuffer(maxVisible, sizeof(float) * 3, ComputeBufferType.Append); // float3 worldPosition
        
        // Create indirect args buffer for DrawMeshInstancedIndirect
        indirectArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        
        // Initialize indirect args
        // [0] = vertex count per instance (6 for quad: 2 triangles)
        // [1] = instance count (will be set by GPU)
        // [2] = start vertex location
        // [3] = start instance location
        indirectArgs[0] = starMesh != null ? starMesh.GetIndexCount(0) : 6;
        indirectArgs[1] = 0; // Will be overwritten by CopyCount
        indirectArgs[2] = 0;
        indirectArgs[3] = 0;
        indirectArgs[4] = 0;
        indirectArgsBuffer.SetData(indirectArgs);
        
        computeBuffersAllocated = true;
        Debug.Log($"[StellarParallaxManager] Allocated ComputeBuffers for {count:N0} stars (GPU path ready)");
    }
    
    private void DisposeComputeBuffers()
    {
        if (starInputBuffer != null)
        {
            starInputBuffer.Release();
            starInputBuffer = null;
        }
        if (visibleStarsBuffer != null)
        {
            visibleStarsBuffer.Release();
            visibleStarsBuffer = null;
        }
        if (indirectArgsBuffer != null)
        {
            indirectArgsBuffer.Release();
            indirectArgsBuffer = null;
        }
        computeBuffersAllocated = false;
    }
    
    private void EnsureOutputArrayCapacity(int requiredCapacity)
    {
        if (outputArrayCapacity >= requiredCapacity)
            return;
        
        // Dispose old arrays
        if (nativeOutputWorldPositions.IsCreated) nativeOutputWorldPositions.Dispose();
        if (nativeVisibilityFlags.IsCreated) nativeVisibilityFlags.Dispose();
        
        // Allocate arrays sized to star count
        outputArrayCapacity = requiredCapacity;
        nativeOutputWorldPositions = new NativeArray<float3>(outputArrayCapacity, Allocator.Persistent);
        nativeVisibilityFlags = new NativeArray<int>(outputArrayCapacity, Allocator.Persistent);
        
        // Allocate render batch buffer
        if (renderBatchBuffer == null || renderBatchBuffer.Length < RENDER_BATCH_SIZE)
            renderBatchBuffer = new Matrix4x4[RENDER_BATCH_SIZE];
    }
    
    // Public interface methods for compatibility with SolarSystemParallaxManager
    public void OnPlayerPositionChanged(Vector3 newPosition)
    {
        // The Update method already handles position changes automatically
        // This method is kept for compatibility but doesn't need to do anything
    }
    
    public int GetLoadedStarCount()
    {
        return allStars?.Count ?? 0;
    }

    public int GetTotalStarCount()
    {
        return (int)totalStars;
    }
    
    public int GetVisibleStarCount()
    {
        if (useJobs && nativeArraysAllocated)
            return lastJobVisibleCount;
        return visibleStars?.Count ?? 0;
    }
    
    public bool IsDataLoaded()
    {
        return starsLoaded;
    }
    
    public bool IsUsingJobs()
    {
        return useJobs && nativeArraysAllocated && !IsUsingComputeShader();
    }
    
    public bool IsUsingComputeShader()
    {
        return useComputeShader && computeBuffersAllocated && starCullingShader != null;
    }
    
    public string GetProcessingMode()
    {
        if (IsUsingComputeShader()) return "GPU (Compute Shader)";
        if (IsUsingJobs()) return "CPU (Burst/Jobs)";
        return "CPU (Single-threaded)";
    }
}