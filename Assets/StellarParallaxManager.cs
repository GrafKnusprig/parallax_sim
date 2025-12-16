using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using System.Collections;

[RequireComponent(typeof(SolarSystemParallaxManager))]
public class StellarParallaxManager : MonoBehaviour
{
    [Header("Stellar Data")]
    [Tooltip("GAIA CSV file name inside StreamingAssets")]
    [SerializeField] private string gaiaCsvFileName = "GAIA_DR3_R7882_phi178182_absz0220.csv";
    
    [Header("Star Rendering")]
    [SerializeField] private Material starMaterial;
    [SerializeField] private float baseStarSize = 0.1f;
    [SerializeField] private float maxStarSize = 2.0f;
    [SerializeField] private Color starColor = Color.white;
    [SerializeField] private float starBrightness = 1.0f;
    
    [Header("Performance & LOD")]
    [SerializeField] private bool enableLOD = true;
    [SerializeField] private float maxRenderDistanceParsecs = 50f;  // Only render stars within this distance
    [SerializeField] private int maxStarsToRender = 100000;  // Maximum stars to render at once
    [SerializeField] private float lodNearDistance = 10f;  // Distance where all nearby stars are shown
    [SerializeField] private float lodFarDistance = 30f;   // Distance where star culling becomes aggressive
    
    [Header("Parallax Visualization")]
    [SerializeField] private bool showParallaxMotion = true;
    [SerializeField] private float parallaxExaggeration = 100f;  // Exaggerate parallax effect for visibility
    [SerializeField] private bool useRealParallax = false;  // Use real parallax (very subtle) vs exaggerated
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private int debugStarLimit = 1000;  // Limit stars for debugging
    [SerializeField] private bool spreadClusteredStars = true;  // Artificially spread stars if they're too clustered
    [SerializeField] private float spreadFactor = 10f;  // How much to spread clustered stars

    // Constants
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const float AU_TO_PARSEC = 1f / PARSEC_TO_AU;
    
    // Star data structure
    private struct StarData
    {
        public Vector3 positionParsecs;  // Position in parsecs (heliocentric)
        public float distance;           // Distance from Sun in parsecs
        public int originalIndex;        // Original index in CSV for debugging
    }
    
    // Rendering data
    private List<StarData> allStars = new List<StarData>();
    private List<StarData> visibleStars = new List<StarData>();
    private GameObject starParent;
    private List<GameObject> starInstances = new List<GameObject>();
    
    // References
    private SolarSystemParallaxManager solarSystemManager;
    private Camera playerCamera;
    
    // State
    private Vector3 lastPlayerPosition = Vector3.zero;
    private bool starsLoaded = false;
    private Coroutine loadingCoroutine;
    
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
        // Get camera reference (try to use the same one as solar system manager)
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindObjectOfType<Camera>();
        }
        
        CreateStarParent();
        
        // Start loading stars asynchronously
        loadingCoroutine = StartCoroutine(LoadStarsAsync());
    }
    
    private void CreateStarParent()
    {
        starParent = new GameObject("Stars");
        starParent.transform.SetParent(transform, false);
        starParent.transform.localPosition = Vector3.zero;
        starParent.transform.localRotation = Quaternion.identity;
        starParent.transform.localScale = Vector3.one;
    }
    
    private IEnumerator LoadStarsAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, gaiaCsvFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"GAIA CSV not found at {path}");
            yield break;
        }
        
        Debug.Log($"Loading GAIA stellar data from {path}...");
        
        int totalLines = 0;
        int loadedStars = 0;
        int maxStarsToLoad = showDebugInfo ? debugStarLimit : int.MaxValue;
        
        using (var reader = new StreamReader(path))
        {
            string line;
            bool isFirstLine = true;
            
            while ((line = reader.ReadLine()) != null && loadedStars < maxStarsToLoad)
            {
                totalLines++;
                
                // Skip header if it exists (though GAIA data doesn't seem to have one)
                if (isFirstLine && (line.StartsWith("Z,") || line.Contains("phi")))
                {
                    isFirstLine = false;
                    continue;
                }
                isFirstLine = false;
                
                // Parse every 100th line to avoid frame drops
                if (totalLines % 100 == 0)
                {
                    yield return null; // Allow other systems to update
                }
                
                if (TryParseStellarData(line, loadedStars, out StarData star))
                {
                    // Filter by distance for performance
                    if (star.distance <= maxRenderDistanceParsecs)
                    {
                        allStars.Add(star);
                        loadedStars++;
                    }
                }
            }
        }
        
        Debug.Log($"Loaded {loadedStars} stars from {totalLines} total entries");
        
        // Debug: Show first few star directions and distances
        if (showDebugInfo && allStars.Count > 0)
        {
            Debug.Log("=== STAR POSITIONING DEBUG ===");
            for (int i = 0; i < Mathf.Min(10, allStars.Count); i++)
            {
                StarData star = allStars[i];
                Vector3 direction = star.positionParsecs.normalized;
                Debug.Log($"Star {i}: Raw=({star.positionParsecs.x:F3},{star.positionParsecs.y:F3},{star.positionParsecs.z:F3}), " +
                         $"Direction=({direction.x:F3},{direction.y:F3},{direction.z:F3}), Distance={star.distance:F2}pc");
            }
            
            // Check if stars are too similar
            if (allStars.Count >= 2)
            {
                Vector3 dir1 = allStars[0].positionParsecs.normalized;
                Vector3 dir2 = allStars[1].positionParsecs.normalized;
                float angleDiff = Vector3.Angle(dir1, dir2);
                Debug.Log($"Angle between first two stars: {angleDiff:F2} degrees");
                
                if (angleDiff < 1f)
                {
                    Debug.LogWarning("Stars are very close together! Check coordinate parsing.");
                }
            }
        }
        
        starsLoaded = true;
        
        // Initial star visibility update
        UpdateVisibleStars();
        CreateStarInstances();
    }
    
    private bool TryParseStellarData(string line, int index, out StarData star)
    {
        star = new StarData();
        
        if (string.IsNullOrEmpty(line?.Trim()))
            return false;
        
        string[] parts = line.Split(',');
        if (parts.Length < 5) // Z,X,Y,R,phi
            return false;
        
        try
        {
            float z = float.Parse(parts[0], CultureInfo.InvariantCulture);
            float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float r = float.Parse(parts[3], CultureInfo.InvariantCulture);
            // phi (angle) is in parts[4] but we'll use the Cartesian coordinates
            
            // Convert from GAIA coordinate system to Unity coordinate system
            // GAIA appears to use Galactic coordinates: Z (vertical), X, Y (galactic plane)
            // Unity uses: Y (up), X (right), Z (forward)  
            star.positionParsecs = new Vector3(x, z, y);
            star.distance = r; // Use provided distance
            star.originalIndex = index;
            
            // Debug first few stars
            if (showDebugInfo && index < 3)
            {
                Debug.Log($"Parsed star {index}: Raw({z},{x},{y},{r}) -> Unity({star.positionParsecs.x:F3},{star.positionParsecs.y:F3},{star.positionParsecs.z:F3}), dist={r:F2}");
            }
            
            // Check if this is a very localized dataset
            if (index == 0 && showDebugInfo)
            {
                Debug.Log($"First star data suggests localized dataset. All stars may be clustered in one sky region.");
            }
            
            return true;
        }
        catch (Exception e)
        {
            if (showDebugInfo && index < 10) // Only log first few errors
            {
                Debug.LogWarning($"Failed to parse stellar data line {index}: {line}. Error: {e.Message}");
            }
            return false;
        }
    }
    
    private void Update()
    {
        if (!starsLoaded || solarSystemManager == null)
            return;
        
        // Get player position from solar system manager (in AU)
        Vector3 playerPosAu = GetPlayerPositionAU();
        
        // Convert to parsecs for stellar calculations
        Vector3 playerPosParsecs = playerPosAu * AU_TO_PARSEC;
        
        // Update star positions for parallax if player moved significantly
        if (Vector3.Distance(lastPlayerPosition, playerPosParsecs) > 0.001f) // 0.001 parsecs threshold
        {
            UpdateVisibleStars();
            UpdateStarPositions(playerPosParsecs);
            lastPlayerPosition = playerPosParsecs;
        }
    }
    
    private Vector3 GetPlayerPositionAU()
    {
        // Access the player position from the solar system manager
        if (solarSystemManager != null)
        {
            return solarSystemManager.playerRealPosAu;
        }
        return Vector3.zero;
    }
    
    private void UpdateVisibleStars()
    {
        if (!enableLOD)
        {
            visibleStars = allStars;
            return;
        }
        
        visibleStars.Clear();
        Vector3 playerPos = lastPlayerPosition;
        
        // Sort stars by distance and apply LOD
        var sortedStars = new List<StarData>(allStars);
        sortedStars.Sort((a, b) => 
            Vector3.Distance(a.positionParsecs, playerPos).CompareTo(
            Vector3.Distance(b.positionParsecs, playerPos)));
        
        int starsToShow = Mathf.Min(maxStarsToRender, sortedStars.Count);
        for (int i = 0; i < starsToShow; i++)
        {
            StarData star = sortedStars[i];
            float distanceToPlayer = Vector3.Distance(star.positionParsecs, playerPos);
            
            // Apply distance-based culling
            if (distanceToPlayer <= maxRenderDistanceParsecs)
            {
                visibleStars.Add(star);
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"UpdateVisibleStars: {visibleStars.Count} visible stars from {allStars.Count} total (player at {playerPos})");
        }
    }
    
    private void CreateStarInstances()
    {
        // Clear existing star instances
        foreach (var star in starInstances)
        {
            if (star != null)
                DestroyImmediate(star);
        }
        starInstances.Clear();
        
        // Create new star instances for visible stars
        foreach (var star in visibleStars)
        {
            GameObject starObj = CreateStarInstance(star);
            if (starObj != null)
            {
                starInstances.Add(starObj);
                
                // Set initial position
                Vector3 playerPos = GetPlayerPositionAU() * AU_TO_PARSEC;
                Vector3 worldPos = CalculateStarWorldPosition(star, playerPos);
                starObj.transform.position = worldPos;
                
                if (showDebugInfo && star.originalIndex < 10)
                {
                    Debug.Log($"Star {star.originalIndex}: World position ({worldPos.x:F1},{worldPos.y:F1},{worldPos.z:F1}), " +
                             $"magnitude={worldPos.magnitude:F1}, scale={starObj.transform.localScale.x:F2}");
                }
            }
        }
        
        Debug.Log($"Created {starInstances.Count} star instances from {visibleStars.Count} visible stars");
        
        // Check for potential render artifacts
        if (showDebugInfo)
        {
            int largeStars = 0;
            foreach (var starObj in starInstances)
            {
                if (starObj != null && starObj.transform.localScale.x > 2f)
                {
                    largeStars++;
                }
            }
            if (largeStars > 0)
            {
                Debug.LogWarning($"Found {largeStars} stars with large scale - possible render artifacts!");
            }
        }
    }
    
    private GameObject CreateStarInstance(StarData star)
    {
        // Create a simple quad for the star
        GameObject starObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        starObj.name = $"Star_{star.originalIndex}";
        starObj.transform.SetParent(starParent.transform, false);
        
        // Remove collider for performance
        var collider = starObj.GetComponent<Collider>();
        if (collider) DestroyImmediate(collider);
        
        // Ensure proper initial orientation (face towards center)
        starObj.transform.rotation = Quaternion.identity;
        
        // Set material
        var renderer = starObj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material materialToUse = null;
            
            if (starMaterial != null)
            {
                materialToUse = starMaterial;
                if (showDebugInfo && star.originalIndex < 3)
                {
                    Debug.Log($"Star {star.originalIndex}: Using custom star material {starMaterial.name}");
                }
            }
            else
            {
                // Fallback to Unity's built-in unlit material
                materialToUse = new Material(Shader.Find("Sprites/Default"));
                Debug.LogWarning("Star material is null! Using fallback material. Please assign StarMaterial in inspector.");
            }
            
            renderer.material = materialToUse;
            renderer.material.color = starColor * starBrightness;
            
            // Make it emissive so it's always visible
            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.SetColor("_EmissionColor", starColor * starBrightness * 0.5f);
            }
            
            if (showDebugInfo && star.originalIndex < 3)
            {
                Debug.Log($"Star {star.originalIndex}: Material={materialToUse.name}, Color={starColor * starBrightness}, Shader={materialToUse.shader.name}");
            }
        }
        else
        {
            Debug.LogError($"No MeshRenderer found on star {star.originalIndex}");
        }
        
        // All stars are on horizon sphere, so use uniform scaling
        // Could vary by stellar magnitude if we had that data
        float scale = Mathf.Clamp(baseStarSize, 0.1f, 5f);  // Clamp to reasonable bounds
        
        // Use normal star scaling - no debug enlargement to avoid render artifacts
        // if (showDebugInfo && star.originalIndex < 5)
        // {
        //     scale = maxStarSize * 2f;
        // }
        
        starObj.transform.localScale = Vector3.one * scale;
        
        // Debug bounds
        if (showDebugInfo && star.originalIndex < 3)
        {
            Bounds bounds = renderer.bounds;
            Debug.Log($"Star {star.originalIndex}: Scale={scale}, Bounds={bounds.size}, Center={bounds.center}");
        }
        
        // Always face camera properly
        if (playerCamera != null)
        {
            starObj.transform.LookAt(playerCamera.transform);
            
            // Debug large stars that might cause render artifacts
            if (showDebugInfo && starObj.transform.localScale.x > 2f)
            {
                Debug.LogWarning($"Star {star.originalIndex} has large scale {starObj.transform.localScale.x} - potential render artifact!");
            }
        }
        
        return starObj;
    }
    
    private void UpdateStarPositions(Vector3 playerPosParsecs)
    {
        for (int i = 0; i < starInstances.Count && i < visibleStars.Count; i++)
        {
            if (starInstances[i] == null) continue;
            
            StarData star = visibleStars[i];
            Vector3 starWorldPos = CalculateStarWorldPosition(star, playerPosParsecs);
            
            starInstances[i].transform.position = starWorldPos;
            
            // Face camera (stars should always face the observer)
            if (playerCamera != null)
            {
                starInstances[i].transform.LookAt(playerCamera.transform);
            }
            
            // Debug first few star positions
            if (showDebugInfo && star.originalIndex < 3)
            {
                Debug.Log($"Star {star.originalIndex} updated position: {starWorldPos}");
            }
        }
    }
    
    private Vector3 CalculateStarWorldPosition(StarData star, Vector3 playerPosParsecs)
    {
        // Get the star's original direction from the Sun (normalized)
        Vector3 originalDirection = star.positionParsecs.normalized;
        
        float horizonRadius = solarSystemManager.HorizonRadius;
        
        // Apply artificial spreading if stars are too clustered (for visualization)
        Vector3 workingDirection = originalDirection;
        if (spreadClusteredStars)
        {
            // Add some artificial spread based on the star's index to separate clustered stars
            float spreadX = Mathf.Sin(star.originalIndex * 0.1f) * spreadFactor * 0.001f;
            float spreadY = Mathf.Cos(star.originalIndex * 0.15f) * spreadFactor * 0.001f;
            float spreadZ = Mathf.Sin(star.originalIndex * 0.13f) * spreadFactor * 0.001f;
            
            workingDirection = originalDirection + new Vector3(spreadX, spreadY, spreadZ);
            workingDirection = workingDirection.normalized;
        }
        
        if (showDebugInfo && star.originalIndex < 3)
        {
            Debug.Log($"Star {star.originalIndex}: Horizon radius = {horizonRadius}");
            Debug.Log($"Original direction = {originalDirection}, Working direction = {workingDirection}");
        }
        
        if (showParallaxMotion)
        {
            // Calculate parallax shift: apparent position changes based on observer position
            // Parallax angle = baseline / distance (in radians)
            Vector3 baseline = playerPosParsecs; // Observer position relative to Sun
            float distance = Mathf.Max(star.distance, 0.1f); // Avoid division by zero
            
            // Calculate the parallax offset in angular space
            Vector3 parallaxOffset = baseline / distance;
            
            if (!useRealParallax)
            {
                // Exaggerate parallax for educational visibility
                parallaxOffset *= parallaxExaggeration;
            }
            
            // Apply the angular shift to the star's direction
            Vector3 shiftedDirection = (workingDirection + parallaxOffset).normalized;
            
            // Project onto the horizon sphere
            Vector3 finalPos = shiftedDirection * horizonRadius;
            
            if (showDebugInfo && star.originalIndex < 3)
            {
                Debug.Log($"Star {star.originalIndex}: Parallax enabled, final position = {finalPos}, distance from origin = {finalPos.magnitude}");
            }
            
            return finalPos;
        }
        else
        {
            // No parallax - just place on horizon sphere at working direction
            Vector3 finalPos = workingDirection * horizonRadius;
            
            if (showDebugInfo && star.originalIndex < 3)
            {
                Debug.Log($"Star {star.originalIndex}: No parallax, final position = {finalPos}, distance from origin = {finalPos.magnitude}");
            }
            
            return finalPos;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (!showDebugInfo || !starsLoaded)
            return;
        
        Gizmos.color = Color.cyan;
        
        // Draw a few nearby stars for debugging
        int count = Mathf.Min(10, visibleStars.Count);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = visibleStars[i].positionParsecs * PARSEC_TO_AU;
            Gizmos.DrawWireSphere(pos, 1000f); // 1000 AU radius for visibility
        }
    }
    
    private void OnDestroy()
    {
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
        }
    }
    
    // Public methods for integration with SolarSystemParallaxManager
    public int GetLoadedStarCount() => allStars.Count;
    public int GetVisibleStarCount() => visibleStars.Count;
    public bool IsDataLoaded() => starsLoaded;
    
    // Method to be called by SolarSystemParallaxManager when player position changes
    public void OnPlayerPositionChanged(Vector3 playerRealPosAu)
    {
        Vector3 playerPosParsecs = playerRealPosAu * AU_TO_PARSEC;
        
        if (Vector3.Distance(lastPlayerPosition, playerPosParsecs) > 0.001f)
        {
            UpdateVisibleStars();
            UpdateStarPositions(playerPosParsecs);
            lastPlayerPosition = playerPosParsecs;
        }
    }
}