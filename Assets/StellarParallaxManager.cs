using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

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
    [SerializeField] private int maxStarsPerFrame = 100000;
    
    // Constants
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const float AU_TO_PARSEC = 1f / PARSEC_TO_AU;  // 1 AU = 1/206,264.806 parsecs
    
    // Star data structure for GDR1 format
    private struct StarData
    {
        public Vector3 positionParsecs;  // 3D position in parsecs (galactic coordinates)
        public float distance;           // Distance from Sun in parsecs
        public float magnitude;          // G magnitude for brightness
        public int originalIndex;        // Original index for debugging
    }
    
    // Data storage
    private List<StarData> allStars = new List<StarData>();
    private List<StarData> visibleStars = new List<StarData>();
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
    
    // Performance optimization
    private const float FOV_CULLING_MARGIN = 45f; // Large margin for seamless rendering
    
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
                    distance = distance_pc,
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
        
        starsLoaded = true;
        UpdateVisibleStars();
    }
    
    private void UpdateVisibleStars()
    {
        if (!starsLoaded) return;
        
        visibleStars.Clear();
        
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
        
        // Calculate effective FOV with generous margin
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        
        // Early exit optimization: if player is extremely far, drastically reduce search space
        float playerDistanceFromOrigin = playerPosAu.magnitude;
        bool isExtremeDistance = playerDistanceFromOrigin > 206265f; // ~1 parsec in AU = very far
        
        int processed = 0;
        int maxToProcess = isExtremeDistance ? 100000 : 2500000; // Limit search when far out
        
        foreach (StarData star in allStars)
        {
            // Early distance culling - cheapest check first
            if (star.distance > maxDistance)
            {
                processed++;
                if (processed > maxToProcess) break; // Prevent processing all 2.4M stars when far out
                continue;
            }
            
            // Additional distance culling when player is far from galactic center
            if (isExtremeDistance)
            {
                Vector3 starToPlayer = (playerPosAu * AU_TO_PARSEC) - star.positionParsecs;
                float distanceToPlayer = starToPlayer.magnitude;
                
                // Only consider stars relatively close to player's position
                if (distanceToPlayer > maxDistance * 0.5f)
                {
                    processed++;
                    if (processed > maxToProcess) break;
                    continue;
                }
            }
            
            // Cheap direction culling before expensive parallax calculation
            Vector3 direction = star.positionParsecs.normalized;
            Vector3 starDir = direction * horizonRadius;
            Vector3 toStarRough = (starDir - cameraPos).normalized;
            float roughDot = Vector3.Dot(cameraForward, toStarRough);
            float roughAngle = Mathf.Acos(Mathf.Clamp(roughDot, -1f, 1f)) * Mathf.Rad2Deg;
            
            // Skip expensive parallax calculation if star is clearly outside FOV
            if (roughAngle > halfFOVWithMargin + 30f) // Extra margin for parallax shift
            {
                processed++;
                if (processed > maxToProcess) break;
                continue;
            }
            
            // Now do the expensive parallax calculation only for potential candidates
            Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius, playerPosAu);
            
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
            float angleToCamera = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;
            
            // Use generous FOV margin for seamless experience
            if (angleToCamera > halfFOVWithMargin)
            {
                processed++;
                if (processed > maxToProcess) break;
                continue;
            }
            
            visibleStars.Add(star);
            processed++;
            
            // Limit stars per frame for performance
            if (visibleStars.Count >= maxStarsPerFrame || processed > maxToProcess)
                break;
        }
    }
    
    private void UpdateStarRendering()
    {
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
            StarData star = visibleStars[i];
            Vector3 direction = star.positionParsecs.normalized;
            Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius, playerPosAu);
            
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
        
        // Use double precision for accurate parallax calculations
        // Star position is in parsecs, player position is in AU
        // Convert player position to parsecs for consistent units
        Vector3d playerPosParsecs = new Vector3d(
            playerPosAu.x * AU_TO_PARSEC,
            playerPosAu.y * AU_TO_PARSEC,
            playerPosAu.z * AU_TO_PARSEC
        );
        
        Vector3d starPosParsecs = new Vector3d(
            star.positionParsecs.x,
            star.positionParsecs.y,
            star.positionParsecs.z
        );
        
        // Calculate vector from PLAYER to star (not from Sun to star)
        Vector3d playerToStar = starPosParsecs - playerPosParsecs;
        
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
    
    private void RenderStars()
    {
        if (starMatrices == null || starMatrices.Length == 0 || starMaterial == null)
            return;
        
        // Ensure materialPropertyBlock is initialized
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
        
        // Set material properties
        materialPropertyBlock.SetColor("_Color", starColor);
        materialPropertyBlock.SetFloat("_Size", baseStarSize);
        materialPropertyBlock.SetFloat("_Brightness", starBrightness);
        
        // Render stars in batches (Unity has limits on instanced rendering)
        const int BATCH_SIZE = 1023; // Unity's limit for Graphics.DrawMeshInstanced
        
        for (int startIndex = 0; startIndex < starMatrices.Length; startIndex += BATCH_SIZE)
        {
            int count = Mathf.Min(BATCH_SIZE, starMatrices.Length - startIndex);
            Matrix4x4[] batch = new Matrix4x4[count];
            
            Array.Copy(starMatrices, startIndex, batch, 0, count);
            
            Graphics.DrawMeshInstanced(
                starMesh,
                0,
                starMaterial,
                batch,
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
        maxStarsPerFrame = Mathf.Clamp(maxStarsPerFrame, 100, 500000);
    }
    
    private void OnDestroy()
    {
        if (starMesh != null)
        {
            DestroyImmediate(starMesh);
        }
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
        return visibleStars?.Count ?? 0;
    }
    
    public bool IsDataLoaded()
    {
        return starsLoaded;
    }
}