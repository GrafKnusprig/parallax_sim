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
    [Tooltip("Exaggerate parallax effect for visibility (1x = real parallax, >1x = exaggerated)")]
    [SerializeField] private float parallaxExaggeration = 100f;
    [Tooltip("Show parallax motion based on player position")]
    [SerializeField] private bool enableParallax = true;
    
    [Header("Star Rendering")]
    [SerializeField] private Material starMaterial;
    [Tooltip("Base size for all stars (Unity units)")]
    [SerializeField] private float baseStarSize = 0.1f;
    [SerializeField] private Color starColor = Color.white;
    [Tooltip("Overall brightness for all stars (0-5x)")]
    [SerializeField] private float starBrightness = 1.0f;
    [Tooltip("Maximum stars to render per frame (count)")]
    [SerializeField] private int maxStarsPerFrame = 5000;
    
    // Constants
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const float AU_TO_PARSEC = 1f / PARSEC_TO_AU;  // 1 AU = 1/206,264.806 parsecs
    private const int CSV_FILE_COUNT = 6;
    
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
                           (enableParallax && Vector3.Distance(solarSystemManager.playerRealPosAu, Vector3.zero) > 0.001f); // Update for parallax
        
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
        Debug.Log("Loading Gaia GDR1 stellar data...");
        
        allStars.Clear();
        int totalStarsLoaded = 0;
        
        // Load all 6 CSV files
        for (int fileIndex = 1; fileIndex <= CSV_FILE_COUNT; fileIndex++)
        {
            string fileName = $"gaia_gdr1_homogen_subset_part{fileIndex:D3}.csv";
            string filePath = Path.Combine(Application.streamingAssetsPath, "GDR1", fileName);
            
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"GDR1 file not found: {fileName}");
                continue;
            }
            
            yield return StartCoroutine(LoadCSVFile(filePath, fileIndex));
            
            // Yield periodically to prevent frame drops
            if (fileIndex % 2 == 0)
                yield return new WaitForEndOfFrame();
        }
        
        Debug.Log($"Total stars loaded: {allStars.Count}");
        Debug.Log($"Distance range: {minStarDistance:F2} - {maxStarDistance:F2} parsecs");
        
        starsLoaded = true;
        UpdateVisibleStars();
    }
    
    private IEnumerator LoadCSVFile(string filePath, int fileIndex)
    {
        Debug.Log($"Loading file {fileIndex}: {Path.GetFileName(filePath)}");
        
        using (StreamReader reader = new StreamReader(filePath))
        {
            // Skip header line
            if (!reader.EndOfStream)
                reader.ReadLine();
            
            int lineCount = 0;
            string line;
            
            while ((line = reader.ReadLine()) != null)
            {
                if (TryParseGDR1Data(line, allStars.Count, out StarData star))
                {
                    allStars.Add(star);
                    
                    // Update distance range
                    if (star.distance < minStarDistance) minStarDistance = star.distance;
                    if (star.distance > maxStarDistance) maxStarDistance = star.distance;
                }
                
                lineCount++;
                
                // Yield every 1000 lines to prevent frame drops
                if (lineCount % 1000 == 0)
                    yield return null;
            }
        }
        
        Debug.Log($"File {fileIndex} loaded: {allStars.Count} total stars so far");
    }
    
    private bool TryParseGDR1Data(string line, int index, out StarData star)
    {
        star = new StarData();
        
        if (string.IsNullOrEmpty(line?.Trim()))
            return false;
        
        string[] parts = line.Split(',');
        if (parts.Length < 7) // source_id,ra_deg,dec_deg,parallax_mas,distance_pc,phot_g_mean_mag,abs_mag_g
            return false;
        
        try
        {
            // Parse required fields
            float ra_deg, dec_deg, distance_pc, magnitude;
            
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ra_deg) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dec_deg) ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out distance_pc) ||
                !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out magnitude))
            {
                return false;
            }
            
            // Skip stars with invalid data
            if (distance_pc <= 0 || float.IsNaN(distance_pc) || float.IsInfinity(distance_pc))
                return false;
            
            // Convert spherical coordinates (RA, Dec) to Cartesian
            // RA is in degrees (0-360), Dec is in degrees (-90 to +90)
            float ra_rad = ra_deg * Mathf.Deg2Rad;
            float dec_rad = dec_deg * Mathf.Deg2Rad;
            
            // Convert to cartesian coordinates (distance * unit vector)
            float cos_dec = Mathf.Cos(dec_rad);
            float x = distance_pc * cos_dec * Mathf.Cos(ra_rad);
            float y = distance_pc * Mathf.Sin(dec_rad);
            float z = distance_pc * cos_dec * Mathf.Sin(ra_rad);
            
            star.positionParsecs = new Vector3(x, y, z);
            star.distance = distance_pc;
            star.magnitude = magnitude;
            star.originalIndex = index;
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
        Vector3 cameraUp = playerCamera.transform.up;
        Vector3 cameraRight = playerCamera.transform.right;
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        // Calculate effective FOV with generous margin
        float halfFOVWithMargin = playerCamera.fieldOfView * 0.5f + FOV_CULLING_MARGIN;
        
        int starsInRange = 0;
        int starsInFOV = 0;
        
        foreach (StarData star in allStars)
        {
            // Distance culling
            if (star.distance > maxDistance)
                continue;
            
            starsInRange++;
            
            // Calculate world position on the virtual sphere WITH PARALLAX
            Vector3 direction = star.positionParsecs.normalized;
            Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius);
            
            // Improved FOV culling with dot product for better performance
            Vector3 toStar = (worldPos - cameraPos).normalized;
            float dotProduct = Vector3.Dot(cameraForward, toStar);
            float angleToCamera = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;
            
            // Use generous FOV margin for seamless experience
            if (angleToCamera > halfFOVWithMargin)
                continue;
            
            starsInFOV++;
            visibleStars.Add(star);
            
            // Limit stars per frame for performance
            if (visibleStars.Count >= maxStarsPerFrame)
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
        
        // Update positions and matrices
        for (int i = 0; i < starCount; i++)
        {
            StarData star = visibleStars[i];
            Vector3 direction = star.positionParsecs.normalized;
            Vector3 worldPos = CalculateStarWorldPosition(star, direction, horizonRadius);
            
            starPositions[i] = worldPos;
            starMatrices[i] = Matrix4x4.TRS(worldPos, Quaternion.identity, Vector3.one);
        }
    }
    
    private Vector3 CalculateStarWorldPosition(StarData star, Vector3 originalDirection, float horizonRadius)
    {
        if (!enableParallax)
        {
            // No parallax - just place on sphere
            return originalDirection * horizonRadius;
        }
        
        // Get player position from solar system manager (in AU, convert to parsecs)
        Vector3 playerPosAu = solarSystemManager.playerRealPosAu;
        Vector3 playerPosParsecs = playerPosAu * AU_TO_PARSEC;
        
        // Calculate parallax shift
        // Parallax angle = baseline / distance (in radians)
        float distance = Mathf.Max(star.distance, 0.1f); // Distance in parsecs
        
        // Calculate the parallax offset in angular space (both in parsecs now)
        Vector3 parallaxOffset = playerPosParsecs / distance;
        
        // Apply exaggeration for visibility
        parallaxOffset *= parallaxExaggeration;
        
        // Convert parallax offset to apparent direction change
        // Subtract offset because parallax shifts stars opposite to player movement
        Vector3 apparentDirection = originalDirection - parallaxOffset;
        
        // Normalize to keep on unit sphere, then scale to horizon radius
        apparentDirection = apparentDirection.normalized;
        
        return apparentDirection * horizonRadius;
    }
    
    private void RenderStars()
    {
        if (starMatrices == null || starMatrices.Length == 0 || starMaterial == null)
            return;
        
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
        
        // Clamp parallax exaggeration to reasonable range
        parallaxExaggeration = Mathf.Clamp(parallaxExaggeration, 1f, 1000f);
        
        // Clamp brightness setting
        starBrightness = Mathf.Clamp(starBrightness, 0.1f, 5f);
        
        // Clamp max stars per frame
        maxStarsPerFrame = Mathf.Clamp(maxStarsPerFrame, 100, 50000);
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