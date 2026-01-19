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
    [Header("Gaia GDR1 Data Settings")]
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
    [SerializeField] private int maxStarsPerFrame = 2500000;

    [Header("Distance-Based Brightness")]
    [Tooltip("Enable dimming stars based on distance")]
    [SerializeField] private bool enableDistanceBrightness = true;
    [Tooltip("Distance (parsecs) where stars are at full brightness")]
    [SerializeField] private float brightnessNearDistanceParsecs = 5f;
    [Tooltip("Distance (parsecs) where stars are at minimum brightness")]
    [SerializeField] private float brightnessFarDistanceParsecs = 1000f;
    [Tooltip("Brightness falloff curve (1=linear, 2=quadratic, 0.5=sqrt)")]
    [SerializeField] private float brightnessFalloffExponent = 1.5f;

    [Header("Parallel Processing (Burst/Jobs)")]
    [Tooltip("Use Burst-compiled Jobs for parallel star processing")]
    [SerializeField] private bool useJobs = true;
    [Tooltip("Batch size for parallel job processing")]
    [SerializeField] private int jobBatchSize = 512;

    [Header("Parallax Approximation")]
    [Tooltip("Use fast parallax approximation for distant stars")]
    [SerializeField] private bool enableFastParallaxApprox = true;
    [Tooltip("Distance (parsecs) beyond which fast approximation is used")]
    [SerializeField] private float parallaxApproxDistanceParsecs = 50f;

    [Header("Player-Relative Culling")]
    [Tooltip("Cull stars outside a fixed radius around the player (parsecs)")]
    [SerializeField] private bool enablePlayerRelativeCulling = true;
    [Tooltip("Player-relative culling radius (parsecs)")]
    [SerializeField] private float playerCullingRadiusParsecs = 200f;
    [Tooltip("Increase player-relative culling radius as distance from the Sun grows")]
    [SerializeField] private bool enableAdaptivePlayerCullingRadius = true;
    [Tooltip("Culling radius growth per parsec from Sun")]
    [SerializeField] private float playerCullingRadiusGrowthPerParsec = 0.25f;
    [Tooltip("Maximum adaptive culling radius (parsecs)")]
    [SerializeField] private float maxPlayerCullingRadiusParsecs = 2000f;
    [Tooltip("Ensure at least this many stars are shown by expanding selection if needed")]
    [SerializeField] private int minVisibleStars = 5000;
    [Tooltip("Enable fallback pass to reach minimum visible stars")]
    [SerializeField] private bool enableMinVisibleStarFallback = true;

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
    [Header("Far Distance Culling (Performance)")]
    [Tooltip("Enable gradual far-distance culling to reduce work at large distances")]
    [SerializeField] private bool enableFarDistanceCulling = true;
    [Tooltip("Distance (AU) where far-distance culling starts ramping in")]
    [SerializeField] private float farCullingStartAu = 300000f; // ~1.45 ly
    [Tooltip("Distance (AU) where far-distance culling reaches full effect")]
    [SerializeField] private float farCullingEndAu = 2000000f; // ~9.7 ly
    [Tooltip("Max stars to process when far-distance culling is off")]
    [SerializeField] private int maxStarsToProcessNear = 4000000;
    [Tooltip("Max stars to process when far-distance culling is fully on")]
    [SerializeField] private int maxStarsToProcessFar = 1500000;
    [Tooltip("Player-relative distance factor at far distances (fraction of maxDistance)")]
    [SerializeField] private float farDistancePlayerRangeFactor = 0.6f;
    
    // Constants
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const float AU_TO_PARSEC = 1f / PARSEC_TO_AU;  // 1 AU = 1/206,264.806 parsecs
    
    // Star data structure for GDR1 format
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
    private NativeArray<float> nativeOutputBrightness;       // Per-star brightness (0-1)
    private NativeArray<int> nativeVisibilityFlags;          // 1 = visible, 0 = culled
    private int outputArrayCapacity = 0;
    
    // Reusable managed arrays for rendering (avoid GC allocations)
    private Matrix4x4[] renderBatchBuffer;
    private const int RENDER_BATCH_SIZE = 1023; // Unity's limit for Graphics.DrawMeshInstanced
    
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
        public float3 effectivePlayerPosParsecs;
        public float horizonRadius;
        public float cosHalfFOV;
        public float cosRoughFOV;
        public float maxDistance;
        public float effectivePlayerCullingRadiusSq;
        public float farDistancePlayerRangeFactor;
        public float smoothFarT;
        public float auToParsec;
        public float parallaxApproxDistanceParsecs;
        
        // Brightness parameters
        public float brightnessNearDistance;  // Distance (parsecs) where brightness = 1.0
        public float brightnessFarDistance;   // Distance (parsecs) where brightness approaches 0
        public float brightnessExponent;      // Falloff curve exponent
        
        // Input: Feature flags
        public bool enableParallax;
        public bool enableFastParallaxApprox;
        public bool enablePlayerRelativeCulling;
        
        // Output: Visibility flags and world positions (indexed by star index)
        // Each star writes to its own index, then we compact on main thread
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<float3> outputWorldPositions;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<float> outputBrightness;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> visibilityFlags; // 1 = visible, 0 = culled
        
        public void Execute(int index)
        {
            float distance = distances[index];
            
            // Early distance culling - cheapest check first
            // TESTING: Distance culling disabled - show all stars in FOV
            // if (distance > maxDistance)
            // {
            //     visibilityFlags[index] = 0;
            //     return;
            // }
            
            float3 positionParsecs = positionsParsecs[index];
            
            // TESTING: Player-relative culling disabled
            // if (enablePlayerRelativeCulling)
            // {
            //     float3 starToPlayer = effectivePlayerPosParsecs - positionParsecs;
            //     float sqrDist = math.lengthsq(starToPlayer);
            //     if (sqrDist > effectivePlayerCullingRadiusSq)
            //     {
            //         visibilityFlags[index] = 0;
            //         return;
            //     }
            // }
            
            // TESTING: Far distance culling disabled
            // if (smoothFarT > 0f)
            // {
            //     float3 playerPosParsecsLocal = effectivePlayerPosAu * auToParsec;
            //     float3 starToPlayer = playerPosParsecsLocal - positionParsecs;
            //     float distanceToPlayer = math.length(starToPlayer);
            //     if (distanceToPlayer > maxDistance * farDistancePlayerRangeFactor)
            //     {
            //         visibilityFlags[index] = 0;
            //         return;
            //     }
            // }
            
            float3 direction = directions[index];
            float3 starDir = direction * horizonRadius;
            float3 toStarRough = math.normalize(starDir - cameraPos);
            float roughDot = math.dot(cameraForward, toStarRough);
            
            // Skip if clearly outside FOV
            if (roughDot < cosRoughFOV)
            {
                visibilityFlags[index] = 0;
                return;
            }
            
            // Calculate world position with parallax
            float3 worldPos = CalculateWorldPosition(index, direction, positionParsecs, distance);
            
            // Validate world position
            if (math.any(math.isnan(worldPos)) || math.any(math.isinf(worldPos)))
            {
                visibilityFlags[index] = 0;
                return;
            }
            
            // Final precise FOV culling
            float3 toStar = math.normalize(worldPos - cameraPos);
            float dotProduct = math.dot(cameraForward, toStar);
            
            if (dotProduct < cosHalfFOV)
            {
                visibilityFlags[index] = 0;
                return;
            }
            
            // Calculate brightness based on distance (inverse-square-ish falloff)
            float normalizedDist = math.saturate((distance - brightnessNearDistance) / (brightnessFarDistance - brightnessNearDistance));
            float brightness = math.pow(1.0f - normalizedDist, brightnessExponent);
            brightness = math.clamp(brightness, 0.02f, 1.0f); // Ensure minimum visibility
            
            // Star is visible - write world position, brightness, and mark as visible
            outputWorldPositions[index] = worldPos;
            outputBrightness[index] = brightness;
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
    
    // ============================================================================
    // BURST JOB: Build Matrix4x4 array with brightness encoded in scale
    // ============================================================================
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    private struct BuildMatricesWithBrightnessJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> worldPositions;
        [ReadOnly] public NativeArray<float> brightness;
        [WriteOnly] public NativeArray<Matrix4x4> matrices;
        
        public void Execute(int index)
        {
            float3 pos = worldPositions[index];
            float b = brightness[index];
            // Encode brightness in the scale - shader will extract it
            matrices[index] = Matrix4x4.TRS(
                new Vector3(pos.x, pos.y, pos.z),
                Quaternion.identity,
                new Vector3(b, b, b)
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
        StartCoroutine(LoadGDR1DataAsync());
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
            UpdateStarRendering();
            
            lastCameraPosition = currentCameraPos;
            lastCameraForward = currentCameraForward;
            lastRenderDistance = renderDistanceRange;
        }
        
        RenderStars();
    }
    
    private void CreateStarParent()
    {
        if (starParent != null) return;
        
        starParent = new GameObject("Gaia_GDR1_Stars");
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
    
    private IEnumerator LoadGDR1DataAsync()
    {
        Debug.Log("Loading Gaia GDR1 stellar data from binary file...");
        
        allStars.Clear();
        
        string filePath = Path.Combine(Application.streamingAssetsPath, "GDR1", "gaia_stars.bin");
        
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"Gaia binary file not found: {filePath}");
            yield break;
        }
        
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // Read header: number of stars (uint32)
            uint starCount = reader.ReadUInt32();
            Debug.Log($"Loading {starCount:N0} stars from binary file...");
            
            allStars.Capacity = (int)starCount;
            
            int batchSize = 10000;
            int processed = 0;
            
            for (uint i = 0; i < starCount; i++)
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
        AllocateNativeArraysFromStarData();
        EnsureOutputArrayCapacity(maxStarsPerFrame);
        
        starsLoaded = true;
        UpdateVisibleStars();
    }
    
    private void UpdateVisibleStars()
    {
        if (!starsLoaded) return;
        
        if (useJobs && nativeArraysAllocated)
        {
            UpdateVisibleStarsJobs();
        }
        else
        {
            UpdateVisibleStarsCPU();
        }
    }
    
    // ============================================================================
    // JOBS PATH: Burst-compiled parallel processing
    // ============================================================================
    private int lastJobVisibleCount = 0;
    private NativeArray<Matrix4x4> nativeMatrices;
    private NativeArray<float3> nativeCompactedPositions;  // Compacted visible positions for matrix building
    private NativeArray<float> nativeCompactedBrightness;  // Compacted brightness values
    
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
        Vector3 effectivePlayerPosParsecs = effectivePlayerPosAu * AU_TO_PARSEC;
        float playerDistanceParsecs = effectivePlayerPosParsecs.magnitude;
        
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        float cosHalfFOV = Mathf.Cos(halfFOVWithMargin * Mathf.Deg2Rad);
        float cosRoughFOV = Mathf.Cos((halfFOVWithMargin + 30f) * Mathf.Deg2Rad);
        
        float effectivePlayerCullingRadiusParsecs = playerCullingRadiusParsecs;
        if (enableAdaptivePlayerCullingRadius)
        {
            effectivePlayerCullingRadiusParsecs = Mathf.Clamp(
                playerCullingRadiusParsecs + playerDistanceParsecs * playerCullingRadiusGrowthPerParsec,
                playerCullingRadiusParsecs,
                maxPlayerCullingRadiusParsecs
            );
        }
        
        float playerDistanceFromOrigin = effectivePlayerPosAu.magnitude;
        float farT = enableFarDistanceCulling
            ? Mathf.InverseLerp(farCullingStartAu, farCullingEndAu, playerDistanceFromOrigin)
            : 0f;
        float farT01 = Mathf.Clamp01(farT);
        float smoothFarT = farT01 * farT01 * (3f - 2f * farT01);
        float playerRangeFactor = Mathf.Lerp(1f, farDistancePlayerRangeFactor, smoothFarT);
        
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
            effectivePlayerPosParsecs = new float3(effectivePlayerPosParsecs.x, effectivePlayerPosParsecs.y, effectivePlayerPosParsecs.z),
            horizonRadius = horizonRadius,
            cosHalfFOV = cosHalfFOV,
            cosRoughFOV = cosRoughFOV,
            maxDistance = maxDistance,
            effectivePlayerCullingRadiusSq = effectivePlayerCullingRadiusParsecs * effectivePlayerCullingRadiusParsecs,
            farDistancePlayerRangeFactor = playerRangeFactor,
            smoothFarT = smoothFarT,
            auToParsec = AU_TO_PARSEC,
            parallaxApproxDistanceParsecs = parallaxApproxDistanceParsecs,
            
            brightnessNearDistance = enableDistanceBrightness ? brightnessNearDistanceParsecs : 0f,
            brightnessFarDistance = enableDistanceBrightness ? brightnessFarDistanceParsecs : 10000f,
            brightnessExponent = enableDistanceBrightness ? brightnessFalloffExponent : 0f,
            
            enableParallax = enableParallax,
            enableFastParallaxApprox = enableFastParallaxApprox,
            enablePlayerRelativeCulling = enablePlayerRelativeCulling,
            
            outputWorldPositions = nativeOutputWorldPositions,
            outputBrightness = nativeOutputBrightness,
            visibilityFlags = nativeVisibilityFlags
        };
        
        // Schedule and complete the job
        JobHandle cullingHandle = cullingJob.Schedule(starCount, jobBatchSize);
        cullingHandle.Complete();
        
        // Compact visible stars on main thread (stream compaction)
        // This is fast because we're just reading flags and copying positions
        lastJobVisibleCount = 0;
        
        // Ensure compacted arrays are ready
        if (!nativeCompactedPositions.IsCreated || nativeCompactedPositions.Length < maxStarsPerFrame)
        {
            if (nativeCompactedPositions.IsCreated) nativeCompactedPositions.Dispose();
            nativeCompactedPositions = new NativeArray<float3>(maxStarsPerFrame, Allocator.Persistent);
        }
        if (!nativeCompactedBrightness.IsCreated || nativeCompactedBrightness.Length < maxStarsPerFrame)
        {
            if (nativeCompactedBrightness.IsCreated) nativeCompactedBrightness.Dispose();
            nativeCompactedBrightness = new NativeArray<float>(maxStarsPerFrame, Allocator.Persistent);
        }
        
        // Compact: gather visible star positions and brightness
        for (int i = 0; i < starCount && lastJobVisibleCount < maxStarsPerFrame; i++)
        {
            if (nativeVisibilityFlags[i] == 1)
            {
                nativeCompactedPositions[lastJobVisibleCount] = nativeOutputWorldPositions[i];
                nativeCompactedBrightness[lastJobVisibleCount] = nativeOutputBrightness[i];
                lastJobVisibleCount++;
            }
        }
        
        // Update matrices for rendering (with brightness encoded in scale)
        if (lastJobVisibleCount > 0)
        {
            // Ensure matrices array is properly sized
            if (!nativeMatrices.IsCreated || nativeMatrices.Length < lastJobVisibleCount)
            {
                if (nativeMatrices.IsCreated) nativeMatrices.Dispose();
                nativeMatrices = new NativeArray<Matrix4x4>(Mathf.Max(lastJobVisibleCount, maxStarsPerFrame), Allocator.Persistent);
            }
            
            var matrixJob = new BuildMatricesWithBrightnessJob
            {
                worldPositions = nativeCompactedPositions,
                brightness = nativeCompactedBrightness,
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
        Vector3 playerPosParsecs = playerPosAu * AU_TO_PARSEC;
        Vector3 effectivePlayerPosAu = playerPosAu * starDistanceScale;
        Vector3 effectivePlayerPosParsecs = playerPosParsecs * starDistanceScale;
        float playerDistanceParsecs = effectivePlayerPosParsecs.magnitude;
        
        // Calculate effective FOV with generous margin
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        float cosHalfFOV = Mathf.Cos(halfFOVWithMargin * Mathf.Deg2Rad);
        float cosRoughFOV = Mathf.Cos((halfFOVWithMargin + 30f) * Mathf.Deg2Rad);
        
        float effectivePlayerCullingRadiusParsecs = playerCullingRadiusParsecs;
        if (enableAdaptivePlayerCullingRadius)
        {
            effectivePlayerCullingRadiusParsecs = Mathf.Clamp(
                playerCullingRadiusParsecs + playerDistanceParsecs * playerCullingRadiusGrowthPerParsec,
                playerCullingRadiusParsecs,
                maxPlayerCullingRadiusParsecs
            );
        }

        int processed = 0;
        HashSet<int> visibleIndices = new HashSet<int>();
            float playerDistanceFromOrigin = effectivePlayerPosAu.magnitude;
            float farT = enableFarDistanceCulling
                ? Mathf.InverseLerp(farCullingStartAu, farCullingEndAu, playerDistanceFromOrigin)
                : 0f;
            float farT01 = Mathf.Clamp01(farT);
            float smoothFarT = farT01 * farT01 * (3f - 2f * farT01); // smoothstep
            int maxToProcess = Mathf.RoundToInt(Mathf.Lerp(maxStarsToProcessNear, maxStarsToProcessFar, smoothFarT));
            float playerRangeFactor = Mathf.Lerp(1f, farDistancePlayerRangeFactor, smoothFarT);
        
        foreach (StarData star in allStars)
        {
            // Early distance culling - cheapest check first
            if (star.distance > maxDistance)
            {
                processed++;
                if (processed > maxToProcess) break; // Prevent processing all 2.4M stars when far out
                continue;
            }

            if (enablePlayerRelativeCulling)
            {
                Vector3 starToPlayer = effectivePlayerPosParsecs - star.positionParsecs;
                if (starToPlayer.sqrMagnitude > effectivePlayerCullingRadiusParsecs * effectivePlayerCullingRadiusParsecs)
                {
                    processed++;
                    if (processed > maxToProcess) break;
                    continue;
                }
            }

            // Additional distance culling when player is far from galactic center (ramped)
            if (smoothFarT > 0f)
            {
                Vector3 starToPlayer = (effectivePlayerPosAu * AU_TO_PARSEC) - star.positionParsecs;
                float distanceToPlayer = starToPlayer.magnitude;

                // Only consider stars relatively close to player's position
                if (distanceToPlayer > maxDistance * playerRangeFactor)
                {
                    processed++;
                    if (processed > maxToProcess) break;
                    continue;
                }
            }
            
            // Cheap direction culling before expensive parallax calculation
            Vector3 direction = star.direction;
            Vector3 starDir = direction * horizonRadius;
            Vector3 toStarRough = (starDir - cameraPos).normalized;
            float roughDot = Vector3.Dot(cameraForward, toStarRough);
            
            // Skip expensive parallax calculation if star is clearly outside FOV
            if (roughDot < cosRoughFOV)
            {
                processed++;
                if (processed > maxToProcess) break;
                continue;
            }
            
            // Now do the expensive parallax calculation only for potential candidates
            Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius, effectivePlayerPosAu);
            
            // Skip stars with invalid world positions
            if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.y) || float.IsNaN(worldPos.z) ||
                float.IsInfinity(worldPos.x) || float.IsInfinity(worldPos.y) || float.IsInfinity(worldPos.z))
            {
                processed++;
                if (processed > maxToProcess) break;
                continue;
            }
            
            // Final precise FOV culling with parallax-corrected position
            Vector3 toStar = (worldPos - cameraPos).normalized;
            float dotProduct = Vector3.Dot(cameraForward, toStar);
            
            // Use generous FOV margin for seamless experience
            if (dotProduct < cosHalfFOV)
            {
                processed++;
                if (processed > maxToProcess) break;
                continue;
            }
            
            visibleStars.Add(star);
            visibleStarWorldPositions.Add(worldPos);
            visibleIndices.Add(star.originalIndex);
            processed++;
            
            // Limit stars per frame for performance
            if (visibleStars.Count >= maxStarsPerFrame || processed > maxToProcess)
                break;
        }

        if (enableMinVisibleStarFallback && visibleStars.Count < minVisibleStars)
        {
            int targetCount = Mathf.Min(minVisibleStars, maxStarsPerFrame);
            foreach (StarData star in allStars)
            {
                if (visibleStars.Count >= targetCount)
                    break;

                // Skip stars already visible
                if (visibleIndices.Contains(star.originalIndex))
                    continue;

                // Cheap direction culling before expensive parallax calculation
                Vector3 direction = star.direction;
                Vector3 starDir = direction * horizonRadius;
                Vector3 toStarRough = (starDir - cameraPos).normalized;
                float roughDot = Vector3.Dot(cameraForward, toStarRough);

                if (roughDot < cosRoughFOV)
                    continue;

                Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius, effectivePlayerPosAu);
                if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.y) || float.IsNaN(worldPos.z) ||
                    float.IsInfinity(worldPos.x) || float.IsInfinity(worldPos.y) || float.IsInfinity(worldPos.z))
                    continue;

                Vector3 toStar = (worldPos - cameraPos).normalized;
                float dotProduct = Vector3.Dot(cameraForward, toStar);
                if (dotProduct < cosHalfFOV)
                    continue;

                visibleStars.Add(star);
                visibleStarWorldPositions.Add(worldPos);
                visibleIndices.Add(star.originalIndex);
            }
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
        maxStarsPerFrame = Mathf.Clamp(maxStarsPerFrame, 100, 3000000);

        farCullingStartAu = Mathf.Max(0f, farCullingStartAu);
        farCullingEndAu = Mathf.Max(farCullingStartAu, farCullingEndAu);
        maxStarsToProcessNear = Mathf.Clamp(maxStarsToProcessNear, 100000, 4000000);
        maxStarsToProcessFar = Mathf.Clamp(maxStarsToProcessFar, 100000, maxStarsToProcessNear);
        farDistancePlayerRangeFactor = Mathf.Clamp01(farDistancePlayerRangeFactor);
        playerCullingRadiusParsecs = Mathf.Max(0f, playerCullingRadiusParsecs);
        playerCullingRadiusGrowthPerParsec = Mathf.Max(0f, playerCullingRadiusGrowthPerParsec);
        maxPlayerCullingRadiusParsecs = Mathf.Max(playerCullingRadiusParsecs, maxPlayerCullingRadiusParsecs);
        minVisibleStars = Mathf.Clamp(minVisibleStars, 0, 1000000);

        speedScalingStartAuPerSec = Mathf.Max(0f, speedScalingStartAuPerSec);
        speedScalingStrength = Mathf.Max(0f, speedScalingStrength);
        minStarDistanceScale = Mathf.Clamp(minStarDistanceScale, 0.01f, 1f);
        stoppedSpeedThresholdAuPerSec = Mathf.Max(0f, stoppedSpeedThresholdAuPerSec);
        parallaxApproxDistanceParsecs = Mathf.Max(0f, parallaxApproxDistanceParsecs);
        
        // Brightness settings
        brightnessNearDistanceParsecs = Mathf.Max(0.1f, brightnessNearDistanceParsecs);
        brightnessFarDistanceParsecs = Mathf.Max(brightnessNearDistanceParsecs + 1f, brightnessFarDistanceParsecs);
        brightnessFalloffExponent = Mathf.Clamp(brightnessFalloffExponent, 0.1f, 5f);
        
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
        if (nativeOutputBrightness.IsCreated) nativeOutputBrightness.Dispose();
        if (nativeVisibilityFlags.IsCreated) nativeVisibilityFlags.Dispose();
        if (nativeCompactedPositions.IsCreated) nativeCompactedPositions.Dispose();
        if (nativeCompactedBrightness.IsCreated) nativeCompactedBrightness.Dispose();
        if (nativeMatrices.IsCreated) nativeMatrices.Dispose();
        outputArrayCapacity = 0;
    }
    
    private void AllocateNativeArraysFromStarData()
    {
        if (nativeArraysAllocated)
            DisposeNativeArrays();
        
        int count = allStars.Count;
        if (count == 0) return;
        
        nativePositionsParsecs = new NativeArray<float3>(count, Allocator.Persistent);
        nativeDirections = new NativeArray<float3>(count, Allocator.Persistent);
        nativeDistances = new NativeArray<float>(count, Allocator.Persistent);
        nativeInvDistances = new NativeArray<float>(count, Allocator.Persistent);
        
        for (int i = 0; i < count; i++)
        {
            StarData star = allStars[i];
            nativePositionsParsecs[i] = new float3(star.positionParsecs.x, star.positionParsecs.y, star.positionParsecs.z);
            nativeDirections[i] = new float3(star.direction.x, star.direction.y, star.direction.z);
            nativeDistances[i] = star.distance;
            nativeInvDistances[i] = star.invDistance;
        }
        
        nativeArraysAllocated = true;
        Debug.Log($"[StellarParallaxManager] Allocated NativeArrays for {count:N0} stars (Burst/Jobs ready)");
    }
    
    private void EnsureOutputArrayCapacity(int requiredCapacity)
    {
        if (outputArrayCapacity >= requiredCapacity)
            return;
        
        // Dispose old arrays
        if (nativeOutputWorldPositions.IsCreated) nativeOutputWorldPositions.Dispose();
        if (nativeOutputBrightness.IsCreated) nativeOutputBrightness.Dispose();
        if (nativeVisibilityFlags.IsCreated) nativeVisibilityFlags.Dispose();
        
        // Allocate arrays sized to star count
        outputArrayCapacity = requiredCapacity;
        nativeOutputWorldPositions = new NativeArray<float3>(outputArrayCapacity, Allocator.Persistent);
        nativeOutputBrightness = new NativeArray<float>(outputArrayCapacity, Allocator.Persistent);
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
        return useJobs && nativeArraysAllocated;
    }
}