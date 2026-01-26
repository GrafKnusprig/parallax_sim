using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

public class SolarSystemParallaxManager : MonoBehaviour
{
    [Header("Datasets")]
    [Tooltip("Path to planet CSV dataset relative to StreamingAssets (e.g., PlanetDatasetPlus/solar_dataset_plus.csv)")]
    [SerializeField] private string csvFileName = "PlanetDatasetPlus/solar_dataset_plus.csv";

    [Tooltip("Path to star dataset relative to StreamingAssets (e.g., GDR3/gaia3_10M.bin)")]
    [SerializeField] private string starDatasetPath = "GDR3/gaia3_10M.bin";

    [Tooltip("Display name of the star dataset for the loading screen")]
    [SerializeField] private string starDatasetName = "Gaia GDR3";

    [Tooltip("Path to planet materials JSON relative to StreamingAssets")]
    [SerializeField] private string planetMaterialsJsonFileName = "planet_materials.json";
    
    [Tooltip("Path to object names JSON relative to StreamingAssets")]
    [SerializeField] private string objectNamesJsonFileName = "PlanetDatasetPlus/stellar_object_names.json";

    [Header("Horizon bubble")]
    [Tooltip("Radius of the virtual horizon sphere (Unity units)")]
    [SerializeField] private float horizonRadius = 4000f;
    [SerializeField] private Material horizonMaterial;
    [SerializeField] private bool showHorizonSphere = false;  // Make sure this is FALSE
    
    // Public accessor for StellarParallaxManager
    public float HorizonRadius => horizonRadius;

    [Header("Planets / Bodies")]
    [SerializeField] private Material planetMaterial;
    [Tooltip("Minimum planet proxy radius (Unity units)")]
    [SerializeField] private float minProxyRadius = 1f;
    [Tooltip("Maximum planet proxy radius (Unity units)")]
    [SerializeField] private float maxProxyRadius = 4000f;
    [SerializeField] private bool useHighQualitySpheres = true;
    [Tooltip("Higher = more detailed (0-4 recommended)")]
    [SerializeField] private int sphereSubdivisions = 3;  // Higher = more detailed (0-4 recommended)
    
    [Header("Saturn Rings")]
    [Tooltip("Material for Saturn's rings (should use alpha transparency)")]
    [SerializeField] private Material saturnRingMaterial;
    [Tooltip("Inner radius of Saturn's rings (in Saturn radii)")]
    [SerializeField] private float saturnRingInnerRadius = 1.2f;
    [Tooltip("Outer radius of Saturn's rings (in Saturn radii)")]
    [SerializeField] private float saturnRingOuterRadius = 2.3f;
    
    [Header("Black Hole Accretion Disc")]
    [Tooltip("Material for black hole accretion disc")]
    [SerializeField] private Material accretionDiscMaterial;
    [Tooltip("Inner radius of accretion disc (in black hole radii)")]
    [SerializeField] private float accretionDiscInnerRadius = 2.5f;
    [Tooltip("Outer radius of accretion disc (in black hole radii)")]
    [SerializeField] private float accretionDiscOuterRadius = 7.0f;
    [Tooltip("Enable gravitational lensing effect (creates secondary lensed images)")]
    [SerializeField] private bool enableGravitationalLensing = true;
    [Tooltip("Vertical offset for lensed disc images (in black hole radii)")]
    [SerializeField] private float lensingVerticalOffset = 0.8f;
    [Tooltip("Opacity multiplier for lensed images")]
    [SerializeField] private float lensingImageOpacity = 0.6f;
    [Header("Black Hole Lensing Torus")]
    [SerializeField] private Material lensingRefractionMaterial;

    [Header("Asteroids")]
    [SerializeField] private bool enableAsteroids = true;
    [SerializeField] private Material asteroidMaterial;
    [Tooltip("Base size for all asteroids (Unity units)")]
    [SerializeField] private float baseAsteroidSize = 0.08f;
    [SerializeField] private Color asteroidColor = new Color(1f, 0.4f, 0.2f);
    [Tooltip("Overall brightness for all asteroids (0-5x)")]
    [SerializeField] private float asteroidBrightness = 1.2f;
    [Tooltip("Maximum asteroids to render per frame (count)")]
    [SerializeField] private int maxAsteroidsPerFrame = 100000;
    
    [Header("Player (real space)")]
    [Tooltip("Angular velocity for orbit mode (radians/sec)")]
    [SerializeField] private float orbitAngularVelocity = 0.2f;

    [Tooltip("Max travel distance in lightyears")]
    [SerializeField] private double maxDistanceFromSunLy = 600000.0;
    
    [Header("Camera")]
    [Tooltip("The camera to use for rendering and movement calculations. If not set, will use Camera.main.")]
    [SerializeField] private Camera targetCamera;
    
    [Tooltip("New Input System: 2D move (x: strafe, y: forward).")]
    [SerializeField] private InputActionReference moveAction;

    [Tooltip("New Input System: vertical move (float axis).")]
    [SerializeField] private InputActionReference verticalAction;

    // NOTE: Label settings (enableLabels, labelCanvas, labelFont, labelFontSize, labelColor, labelOffsetPixels)
    // have been moved to SolarSystemUIManager. Configure labels there.
    
    // NOTE: HUD settings (enableHUD, hudFontSize, hudColor, hudPosition) have been moved to SolarSystemUIManager.
    
    // NOTE: VR input actions (autopilotMenuAction, planetInfoAction, menuScrollAction, menuSelectAction)
    // have been moved to SolarSystemUIManager. Configure VR inputs there.
    
    // NOTE: Loading screen settings (enableLoadingScreen) have been moved to SolarSystemUIManager.

    private const double AU_KM = 149_597_870.7;
    private const double SPEED_OF_LIGHT_KM_S = 299_792_458.0; // km/s (exact value)
    private const double LIGHTYEAR_KM = 9_460_730_472_580.8; // km in 1 lightyear
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU
    private const double SECTOR_SIZE_AU = 1_000_000.0; // Each sector is 1 million AU (approx 5 parsecs)

    [System.NonSerialized]
    public HierarchicalPosition playerRealPosAu; // player position in AU (real space) - public for StellarParallaxManager

    private GameObject horizonSphere;

    private readonly List<BodyInstance> bodies = new List<BodyInstance>();
    
    // Asteroid data
    private struct AsteroidData
    {
        public HierarchicalPosition positionAU;  // 3D position in AU (hierarchical coordinates)
        public float distance;      // Distance from Sun in AU
        public float magnitude;     // H magnitude for brightness
        public int originalIndex;   // Original index for debugging
    }
    
    private List<AsteroidData> allAsteroids = new List<AsteroidData>();
    private List<AsteroidData> visibleAsteroids = new List<AsteroidData>();
    private Vector3[] asteroidPositions;
    private Matrix4x4[] asteroidMatrices;
    private MaterialPropertyBlock asteroidPropertyBlock;
    private Mesh asteroidMesh;
    private bool asteroidsLoaded = false;
    
    // Dynamic scaling and speed
    private BodyInstance nearestPlanet;
    private float currentScale;
    private float currentSpeed;
    private float actualSpeed; // Actual movement speed (0 when standing still)
    private float distanceToNearestPlanet;
    
    // Planet-specific textures and materials
    private Dictionary<long, Texture2D> planetTextures = new Dictionary<long, Texture2D>();
    private Dictionary<long, Texture2D> planetAtmosphereTextures = new Dictionary<long, Texture2D>();
    private Dictionary<long, Texture2D> planetNightTextures = new Dictionary<long, Texture2D>();
    private Dictionary<long, Material> planetMaterials = new Dictionary<long, Material>(); // For stars with custom materials
    private Dictionary<long, bool> bodyEmitting = new Dictionary<long, bool>(); // Whether body emits light
    private Dictionary<long, long?> bodyIlluminatedBy = new Dictionary<long, long?>(); // Which body illuminates this one
    
    // NOTE: HUD elements (hudUI, hudText) have been moved to SolarSystemUIManager.
    
    // Stellar parallax integration
    private StellarParallaxManager stellarManager;
    
    // UI Manager for labels and other UI elements
    private SolarSystemUIManager uiManager;
    
    // Autopilot system - UI managed by SolarSystemUIManager
    private bool autopilotActive = false;
    private BodyInstance autopilotTarget = null;
    
    // Static property for other scripts to check autopilot state
    public static bool IsAutopilotActive { get; private set; } = false;
    public static bool IsOrbiting { get; private set; } = false;

    public float CurrentSpeedAuPerSec => currentSpeed;
    public float ActualSpeedAuPerSec => actualSpeed;

    public string StarDatasetPath => starDatasetPath;
    public string StarDatasetName => starDatasetName;
    
    // Planet info system - NOTE: UI elements now managed by SolarSystemUIManager
    private Dictionary<string, PlanetData> planetInfoData = new Dictionary<string, PlanetData>();
    
    // NOTE: Loading screen UI elements have been moved to SolarSystemUIManager.
    
    // NOTE: VR input state tracking (autopilotTriggerWasPressed, planetInfoTriggerWasPressed, vrSelectWasPressed)
    // has been moved to SolarSystemUIManager.

    // Stencil Property IDs
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
    private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");
    private MaterialPropertyBlock planetPropertyBlock;
    
    // Orbit system
    private BodyInstance orbitTargetBody = null;
    private float orbitAngle = 0f;
    private double orbitDistanceAu = 0.0;
    private double orbitHeightAu = 0.0;

    private class BodyInstance
    {
        public string name;
        public long naifId;
        public string objectType; // sun, planet, moon, dwarf_planet, star, asteroid, black_hole
        public HierarchicalPosition realPosAu;
        public float radiusKm;

        public Transform proxy;
        public Renderer renderer;
        public Renderer ringRenderer;

        // UI-based labels
        public GameObject labelUI;
        
        // Link to planet data
        public PlanetData planetData;
        
        // Ring system (for Saturn, etc.)
        public GameObject ringObject;
        
        // Lensing Torus (for Black Hole)
        public GameObject lensingTorus;
    }
    
    // Planet data from CSV dataset
    private class PlanetData
    {
        public string Color;
        public string Mass;
        public string Diameter;
        public string Density;
        public string Gravity;
        public string LengthOfDay;
        public string DistanceFromSun;
        public string MeanTemperature;
        public string NumberOfMoons;
        public string RingSystem;
        public string AtmosphericComposition;
        public string SurfaceFeatures;
        public string Composition;
    }
    
    // NAIF ID to name mapping loaded from JSON
    private Dictionary<long, string> naifIdToName = new Dictionary<long, string>();

    // Radii for main bodies (km), keyed by NAIF ID used in your file
    private static readonly Dictionary<long, float> BodyRadiiKm = new Dictionary<long, float>
    {
        { 10, 696_340f },   // SUN

        { 199, 2_439.7f },  // MERCURY
        { 299, 6_051.8f },  // VENUS
        { 399, 6_371.0f },  // EARTH
        { 301, 1_737.4f },  // MOON
        
        { 4, 3_389.5f },    // MARS (barycenter - only barycenter ID 4 is in the CSV)

        // Jupiter: only barycenter available (5) in your CSV
        { 5, 69_911f },     // JUPITER (barycenter as proxy)

        { 699, 58_232f },   // SATURN
        { 7, 25_362f },     // URANUS (barycenter)
        { 8, 24_622f },     // NEPTUNE (barycenter)
        { 999, 1_188.3f },  // PLUTO

        // You can add moons etc. if you care about correct sizes
    };

    private void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (verticalAction != null) verticalAction.action.Enable();
        // VR input actions are now handled by UIManager
    }

    private void OnDisable()
    {
        if (moveAction != null) moveAction.action.Disable();
        if (verticalAction != null) verticalAction.action.Disable();
        // VR input actions are now handled by UIManager
    }

    private Camera GetActiveCamera()
    {
        if (targetCamera != null)
            return targetCamera;
        
        return Camera.main;
    }
    
    private void Start()
    {
        // Get reference to stellar manager if present
        stellarManager = GetComponent<StellarParallaxManager>();
        
        // Get or add UI manager
        uiManager = GetComponent<SolarSystemUIManager>();
        if (uiManager == null)
        {
            uiManager = gameObject.AddComponent<SolarSystemUIManager>();
        }
        
        CreateHorizonSphere();
        uiManager.Initialize(); // Setup label canvas via UI manager
        uiManager.CreateHUD(); // Create HUD via UI manager
        LoadObjectNamesFromJson();
        LoadPlanetMaterials();

        LoadShadowVectors();
        LoadBodiesFromCsv();
        if (bodies.Count == 0)
        {
            Debug.LogWarning("No bodies loaded from dataset");
        }
        
        LoadPlanetInfoData();
        CreateAutopilotMenuViaUIManager(); // Create autopilot menu via UI manager
        uiManager.CreatePlanetInfoPanel(); // Create planet info panel via UI manager
        
        // Subscribe to UIManager input events
        uiManager.OnAutopilotTogglePressed += HandleAutopilotToggle;
        uiManager.OnPlanetInfoTogglePressed += TogglePlanetInfo;
        uiManager.OnOrbitTogglePressed += ToggleOrbit;
        
        // Create loading screen via UI manager (passing HUD reference for hiding during load)
        uiManager.CreateLoadingScreen(uiManager.HudUI);
        
        // Initialize asteroid rendering
        if (asteroidPropertyBlock == null)
            asteroidPropertyBlock = new MaterialPropertyBlock();
        InitializeAsteroidMesh();
        if (enableAsteroids)
            StartCoroutine(LoadAsteroidDataAsync());
        
        // Initialize dynamic behavior
        currentScale = 1f; // Use hardcoded base scale
        currentSpeed = 0.001f; // Start with minimal speed

        // Set initial camera scale
        Camera cam = GetActiveCamera();
        if (cam != null)
        {
            cam.transform.localScale = Vector3.one * currentScale;
        }
        else
        {
            Debug.LogError("No camera found! Please assign a camera in the Inspector or tag a camera as MainCamera.");
        }
        
        Debug.Log($"Player spawn position: {playerRealPosAu} AU");
    }
    
    // Get Sun's current position (for stellar parallax calculations)
    public HierarchicalPosition GetSunPosition()
    {
        foreach (var body in bodies)
        {
            if (body.naifId == 10) // Sun
                return body.realPosAu;
        }
        return HierarchicalPosition.zero; // Fallback if Sun not found
    }
    
    // Get player position relative to Sun (for stellar coordinates)
    public Vector3d GetPlayerPositionRelativeToSun()
    {
        HierarchicalPosition sunPos = GetSunPosition();
        return sunPos.OffsetTo(playerRealPosAu, SECTOR_SIZE_AU);
    }
    
    // Helper property to get label canvas from UI manager
    private Canvas LabelCanvas => uiManager != null ? uiManager.LabelCanvas : null;
    
    // Helper property to get label font from UI manager
    private TMP_FontAsset LabelFont => uiManager != null ? uiManager.LabelFont : null;
    
    // NOTE: CreateHUD() has been moved to SolarSystemUIManager.
    
    // Origin shifting system - adaptive based on proximity to celestial bodies
    // Only shifts when near planets for precision; allows free travel at extreme distances
    private void CheckAndPerformOriginShift()
    {
        const double SHIFT_THRESHOLD = SECTOR_SIZE_AU * 0.5; // 500,000 AU threshold
        const double PLANET_PROXIMITY_THRESHOLD = 100000.0; // Only shift if within 100,000 AU of any body
        
        // Check if player's local offset is getting large
        double offsetMagnitude = playerRealPosAu.localOffset.magnitude;
        
        if (offsetMagnitude > SHIFT_THRESHOLD)
        {
            // Check if we're near any celestial body
            bool nearAnyBody = false;
            double closestBodyDistance = double.PositiveInfinity;
            
            foreach (var body in bodies)
            {
                Vector3d offset = playerRealPosAu.OffsetTo(body.realPosAu, SECTOR_SIZE_AU);
                double distance = offset.magnitude;
                
                if (distance < closestBodyDistance)
                    closestBodyDistance = distance;
                
                if (distance < PLANET_PROXIMITY_THRESHOLD)
                {
                    nearAnyBody = true;
                    break; // Found at least one nearby body
                }
            }
            
            // Only perform origin shift if we're near a celestial body
            if (nearAnyBody)
            {
                Debug.Log($"Origin shift triggered: player offset {offsetMagnitude:F2} AU, nearest body {closestBodyDistance:F2} AU");
                
                // Calculate the shift amount (move player back to near origin of their sector)
                Vector3d shiftAmount = playerRealPosAu.localOffset;
                
                // Shift player position (this updates sector and resets local offset)
                playerRealPosAu = new HierarchicalPosition(playerRealPosAu.sector, Vector3d.zero);
                
                // Shift all bodies by the same amount (relative to player)
                for (int i = 0; i < bodies.Count; i++)
                {
                    bodies[i].realPosAu = bodies[i].realPosAu.Subtract(shiftAmount, SECTOR_SIZE_AU);
                }
                
                // Shift all asteroids by the same amount
                for (int i = 0; i < allAsteroids.Count; i++)
                {
                    AsteroidData asteroid = allAsteroids[i];
                    asteroid.positionAU = asteroid.positionAU.Subtract(shiftAmount, SECTOR_SIZE_AU);
                    allAsteroids[i] = asteroid;
                }
                
                // Note: Stars are handled by StellarParallaxManager which calculates relative to player
                
                Debug.Log($"Origin shift complete: player now at {playerRealPosAu}");
            }
            else
            {
                // Far from all bodies - allow offset to grow without shifting
                // This prevents jumps during high-speed interstellar travel
                // Double precision can handle offsets up to ~10^15 AU with millimeter precision
                if (offsetMagnitude > SECTOR_SIZE_AU * 10.0) // Log warning at 10M AU
                {
                    Debug.LogWarning($"Player very far from all bodies: {offsetMagnitude:F0} AU from sector origin. Precision may degrade at extreme distances.");
                }
            }
        }
    }

    private void Update()
    {
        // Handle loading screen via UIManager
        if (uiManager != null)
        {
            bool starsReady = stellarManager == null || stellarManager.IsDataLoaded();
            int starCount = stellarManager != null ? stellarManager.GetLoadedStarCount() : 0;
            int totalStars = stellarManager != null ? stellarManager.GetTotalStarCount() : 0;
            uiManager.UpdateLoadingScreen(starsReady, starCount, totalStars, starDatasetName);
        }
        
        // Always update VR canvas position (needed for loading screen visibility in VR)
        UpdateVRCanvas();
        
        // Don't allow gameplay until loading is complete
        if (uiManager != null && !uiManager.IsLoadingComplete)
        {
            return;
        }
        
        // Delegate input handling to UIManager (handles keyboard X/I keys, VR controller buttons, menu navigation)
        if (uiManager != null)
        {
            uiManager.UpdateInput();
        }
        
        UpdateDynamicBehavior();
        
        // Use autopilot or manual movement
        if (autopilotActive)
        {
            UpdateAutopilot();
        }
        else if (IsOrbiting)
        {
            UpdateOrbitMovement();
        }
        else if (uiManager != null && !uiManager.AutopilotMenuOpen && !uiManager.IsPlanetInfoVisible)
        {
            UpdatePlayerMovement();
        }
        else if (uiManager == null)
        {
            UpdatePlayerMovement();
        }
        
        // Check for origin shift (keep player near sector origin for precision)
        CheckAndPerformOriginShift();
        
        UpdateBodyProxies();
        UpdatePlanetInfoPanel();
        
        // Update and render asteroids
        if (asteroidsLoaded && enableAsteroids)
        {
            UpdateVisibleAsteroids();
            UpdateAsteroidRendering();
            RenderAsteroids();
        }
        
        // Stellar manager handles its own update based on playerRealPosAu
        
        UpdateBlackHoleLensing();
        
        UpdatePlanetStencilMaterials();

        // Update camera look at AFTER body proxies are positioned
        if (IsOrbiting)
        {
            UpdateOrbitCamera();
        }
    }
    
    private void UpdateBlackHoleLensing()
    {
        Camera cam = GetActiveCamera();
        if (cam == null) return;
        
        foreach (var body in bodies)
        {
            if (body.lensingTorus != null)
            {
                // Billboard: Look at camera
                body.lensingTorus.transform.LookAt(cam.transform);
                // Adjust rotation if needed (Torus usually lies on XZ plane, LookAt aligns Z axis)
                // If Torus is flat on XZ, LookAt makes top face camera. That's usually what we want for a "ring" facing us.
                // Actually, LookAt aligns +Z to face target.
                // If Torus is built in XZ plane, we want its "face" (Y axis) to point at camera?
                // Standard Unity LookAt makes +Z point at target.
                // If Torus is XZ, we want to rotate 90 deg around X? 
                
                // Let's assume Torus is created in XZ plane.
                // We want the circle to be perpendicular to the view vector.
                body.lensingTorus.transform.LookAt(cam.transform);
            }
        }
    }
    
    private void UpdatePlanetStencilMaterials()
    {
        if (bodies.Count == 0) return;

        // Create a list of bodies paired with their distance for sorting
        // We do this every frame to handle moving player/planets
        var sortedBodies = new List<(BodyInstance body, double distance)>(bodies.Count);

        foreach (var body in bodies)
        {
            double dist = playerRealPosAu.OffsetTo(body.realPosAu, SECTOR_SIZE_AU).magnitude;
            sortedBodies.Add((body, dist));
        }

        // Sort by distance ascending (Closest First)
        sortedBodies.Sort((a, b) => a.distance.CompareTo(b.distance));

        // Apply stencil properties hierarchically
        // Front-to-Back rendering with Stencil Masking
        for (int i = 0; i < sortedBodies.Count; i++)
        {
            var body = sortedBodies[i].body;
            
            // Generate unique stencil ID based on sort order.
            // Closest body gets 255, furthest gets lower values.
            // This assumes < 255 bodies, which is true for solar system.
            int stencilRef = Mathf.Clamp(255 - i, 1, 255);
            
            // Stencil Logic Corrected used GreaterEqual:
            // 1. Ref = Unique ID (decreasing with distance, e.g. Closest=255, Next=254)
            // 2. Comp = GreaterEqual (7).
            //    - If we draw a pixel, we check if current Stencil Buffer value <= our Ref (Ref >= Buffer).
            //    - Default buffer is 0. 255 >= 0 is TRUE.
            //    - Body A (Ref 255) draws: writes 255.
            //    - Body A's Ring (Ref 255) draws over Body A: 255 >= 255 is TRUE. Allowed.
            //    - Body B (Ref 254) draws BEHIND Body A:
            //      - Pixel has 255. Ref is 254.
            //      - Check: 254 >= 255? FALSE. Occluded.
            // 3. Pass = Replace (2). If we pass Test and Depth, write our Ref to buffer.
            
            // Base queue for this body index. Using stride of 10 to allow space for layers.
            int baseQueue = 2000 + (i * 10);

            if (body.renderer != null)
            {
                Material mat = body.renderer.material;
                if (mat != null)
                {
                    // Render Planet FIRST (opaque, writes Z)
                    mat.renderQueue = baseQueue; 

                    if (mat.HasProperty(StencilRefId))
                    {
                        mat.SetFloat(StencilRefId, stencilRef);
                        mat.SetFloat(StencilCompId, 7); // GreaterEqual
                        mat.SetFloat(StencilPassId, 2); // Replace
                    }
                }
            }

            // Apply same logic to Ring Renderer if present
            if (body.ringRenderer != null)
            {
                Material ringMat = body.ringRenderer.material;
                if (ringMat != null)
                {
                    // Render Ring SECOND (transparent, usually ZWrite Off)
                    // This allows it to pass ZTest where it is in front of planet,
                    // and fail ZTest where it is behind planet (since planet wrote Z).
                    ringMat.renderQueue = baseQueue + 1;

                    if (ringMat.HasProperty(StencilRefId))
                    {
                        ringMat.SetFloat(StencilRefId, stencilRef);
                        ringMat.SetFloat(StencilCompId, 7); // GreaterEqual
                        ringMat.SetFloat(StencilPassId, 2); // Replace
                    }
                }
            }
        }
    }
    
    // NOTE: CreateLoadingScreen() and UpdateLoadingScreen() have been moved to SolarSystemUIManager.
    
    /// <summary>
    /// Updates the World Space canvas position to follow the VR camera.
    /// This ensures UI elements are visible in VR headsets.
    /// Only runs when in VR mode - desktop mode uses Screen Space Overlay.
    /// </summary>
    private void UpdateVRCanvas()
    {
        // Delegate VR canvas positioning to UI manager
        if (uiManager != null)
        {
            Camera cam = GetActiveCamera();
            uiManager.UpdateVRCanvasPosition(cam);
        }
    }

    // --- Setup ---

    private void CreateHorizonSphere()
    {
        horizonSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        horizonSphere.name = "HorizonSphere";
        horizonSphere.transform.SetParent(transform, false);
        horizonSphere.transform.localPosition = Vector3.zero;
        horizonSphere.transform.localScale = Vector3.one * horizonRadius * 2f;

        var col = horizonSphere.GetComponent<Collider>();
        if (col) Destroy(col);

        var renderer = horizonSphere.GetComponent<MeshRenderer>();
        if (!showHorizonSphere && renderer != null)
        {
            renderer.enabled = false;
        }
        else if (renderer != null && horizonMaterial != null)
        {
            renderer.sharedMaterial = horizonMaterial;
        }
    }

    private void LoadBodiesFromCsv()
    {
        bool earthFound = false;
        
        // Load solar system dataset
        earthFound = LoadBodiesFromFile(csvFileName, earthFound) || earthFound;
        
        // Load Alpha Centauri system dataset
        LoadBodiesFromFile("centauri_system.csv", earthFound);
        
        if (!earthFound)
        {
            Debug.LogWarning("Earth (naifId 399) not found in dataset. Player starts at origin in real space.");
            playerRealPosAu = HierarchicalPosition.zero;
        }
    }
    
    private bool LoadBodiesFromFile(string fileName, bool earthFound)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"CSV not found at {path}");
            return earthFound;
        }
        
        // Shadow Vector Index Tracking
        bool useShadowVectors = fileName == csvFileName;
        int shadowVectorIndex = 0;

        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1)
        {
            Debug.LogWarning($"CSV {fileName} seems empty or header-only.");
            return earthFound;
        }
        
        Debug.Log($"Loading {fileName}: {lines.Length - 1} entries");
        bool localEarthFound = earthFound;

        // Parse new Gaia-format CSV: source_id,object_type,ra_deg,dec_deg,parallax_mas,distance_pc,phot_g_mean_mag,abs_mag_g,size_km,vx_au_d,vy_au_d,vz_au_d,speed_km_s,gm_km3_s2,mass_kg,density_g_cm3,mean_radius_km,albedo,rot_per_hr,H
        // object_type can be: sun, planet, moon, dwarf_planet, star, black_hole
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 17) continue; // Need at least up to mean_radius_km

            // Parse fields from new format
            if (!long.TryParse(parts[0], out long naifId)) continue; // source_id is the NAIF ID
            
            string objectType = parts[1]; // object_type (sun, planet, moon, dwarf_planet, star, black_hole)
            
            // Parse RA, Dec, Distance in parsecs
            float ra_deg = 0, dec_deg = 0, distance_pc = 0;
            bool hasPosition = true;
            
            if (!string.IsNullOrEmpty(parts[2]) && !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out ra_deg))
                hasPosition = false;
            if (!string.IsNullOrEmpty(parts[3]) && !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out dec_deg))
                hasPosition = false;
            if (!string.IsNullOrEmpty(parts[5]) && !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out distance_pc))
                hasPosition = false;
            
            // For the Sun or bodies with empty position data, set to origin
            HierarchicalPosition realPosAu;
            if (naifId == 10 || !hasPosition) // Sun or invalid position
            {
                realPosAu = HierarchicalPosition.zero;
            }
            else
            {
                // Convert RA/Dec/Distance from parsecs to Cartesian AU coordinates
                double ra_rad = ra_deg * Math.PI / 180.0;
                double dec_rad = dec_deg * Math.PI / 180.0;
                
                double cos_dec = Math.Cos(dec_rad);
                double distance_au = distance_pc * PARSEC_TO_AU;
                
                double x = distance_au * cos_dec * Math.Cos(ra_rad);
                double y = distance_au * Math.Sin(dec_rad);
                double z = distance_au * cos_dec * Math.Sin(ra_rad);
                
                Vector3d absolutePos = new Vector3d(x, y, z);
                realPosAu = new HierarchicalPosition(absolutePos, SECTOR_SIZE_AU);
            }
            
            // Parse mean radius (field index 16)
            float radiusKm = 1000f; // default fallback
            if (parts.Length > 16 && !string.IsNullOrEmpty(parts[16]))
            {
                if (float.TryParse(parts[16], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                {
                    radiusKm = radius;
                    if (naifId == 9000000000) // Sagittarius A* debug
                    {
                        Debug.Log($"Sagittarius A* parsed radius: {radiusKm:F2} km from string '{parts[16]}'");
                    }
                }
                else
                {
                    // Fallback to BodyRadiiKm dictionary if parsing fails
                    radiusKm = BodyRadiiKm.TryGetValue(naifId, out float r) ? r : 1_000f;
                    if (naifId == 9000000000)
                    {
                        Debug.LogWarning($"Sagittarius A* FAILED to parse radius from '{parts[16]}', using fallback: {radiusKm}");
                    }
                }
            }
            else
            {
                // Fallback to BodyRadiiKm dictionary if field is empty
                radiusKm = BodyRadiiKm.TryGetValue(naifId, out float r) ? r : 1_000f;
                if (naifId == 9000000000)
                {
                    Debug.LogWarning($"Sagittarius A* radius field empty or missing, using fallback: {radiusKm}");
                }
            }
            
            // Create name from NAIF ID
            string name = GetBodyName(naifId, objectType);

            // Create proxy sphere
            GameObject proxy;
            if (useHighQualitySpheres)
            {
                proxy = CreateHighQualitySphere(name, sphereSubdivisions);
            }
            else
            {
                proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                proxy.name = name;
                var col = proxy.GetComponent<Collider>();
                if (col) Destroy(col);
            }
            
            proxy.transform.SetParent(transform, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localScale = Vector3.one; // will be updated in UpdateBodyProxies

            var renderer = proxy.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material materialToUse = null;
                bool isEmitting = bodyEmitting.TryGetValue(naifId, out bool emitting) && emitting;
                
                // Check if this body has a custom material (e.g., stars with procedural shaders)
                if (planetMaterials.TryGetValue(naifId, out Material customMaterial))
                {
                    materialToUse = customMaterial;
                }
                // Otherwise, use the base material with texture applied
                else if (planetMaterial != null)
                {
                    // Duplicate the material so each body has its own instance
                    materialToUse = new Material(planetMaterial);
                    
                    // Try to load body-specific texture, fall back to default (ID 0)
                    Texture2D textureToUse = null;
                    if (planetTextures.TryGetValue(naifId, out Texture2D specificTexture))
                    {
                        textureToUse = specificTexture;
                    }
                    else if (planetTextures.TryGetValue(0, out Texture2D defaultTexture))
                    {
                        textureToUse = defaultTexture;
                        Debug.Log($"Using default texture for {name} (NAIF ID {naifId})");
                    }
                    
                    // Apply texture to the duplicated material
                    if (textureToUse != null)
                    {
                        materialToUse.SetTexture("_BaseMap", textureToUse);
                        materialToUse.SetTexture("_MainTex", textureToUse); // Legacy shader support

                        // Apply atmosphere texture if available
                        if (planetAtmosphereTextures.TryGetValue(naifId, out Texture2D atmTexture))
                        {
                            materialToUse.SetTexture("_SecondTex", atmTexture);
                            Debug.Log($"Applied atmosphere to {name}");
                        }

                        // Apply night texture if available
                        if (planetNightTextures.TryGetValue(naifId, out Texture2D nightTexture))
                        {
                            materialToUse.SetTexture("_NightTex", nightTexture);
                            Debug.Log($"Applied night texture to {name}");
                        }
                        
                        // If this body emits light, make it glow
                        if (isEmitting)
                        {
                            materialToUse.EnableKeyword("_EMISSION");
                            materialToUse.SetTexture("_EmissionMap", textureToUse);
                            materialToUse.SetColor("_EmissionColor", Color.white * 2f); // Bright emission
                            Debug.Log($"Enabled emission for {name} (NAIF ID {naifId})");
                        }
                    }
                    
                }
                
                // Apply Shadow Direction (Sun Direction) to any material (custom or standard)
                if (materialToUse != null && useShadowVectors && shadowVectorIndex < shadowVectors.Count)
                {
                    Vector4 sunDir = shadowVectors[shadowVectorIndex];
                    if (sunDir.w > 0.5f) // Check validity
                    {
                         materialToUse.SetVector("_SunDirection", sunDir);
                    }
                }

                if (materialToUse != null)
                {
                    renderer.material = materialToUse; // Use .material (not .sharedMaterial) since we duplicated it
                }
            }

            // Cache renderer
            BodyInstance bodyInst = new BodyInstance
            {
                name = name,
                naifId = naifId,
                objectType = objectType,
                realPosAu = realPosAu,
                radiusKm = radiusKm,
                proxy = proxy.transform,
                renderer = proxy.GetComponent<Renderer>()
            };
            
            // Create rings for Saturn (NAIF ID 699)
            if (naifId == 699)
            {
                // Tilt Saturn 27 degrees on Z axis
                proxy.transform.localRotation = Quaternion.Euler(0, 0, 27f);
                
                if (saturnRingMaterial != null)
                {
                    bodyInst.ringObject = CreateSaturnRings(proxy.transform, radiusKm);
                    if (bodyInst.ringObject != null)
                        bodyInst.ringRenderer = bodyInst.ringObject.GetComponent<Renderer>();
                }
            }
            
            // Create accretion disc for black holes
            if (objectType == "black_hole")
            {
                if (accretionDiscMaterial != null)
                {
                    bodyInst.ringObject = CreateAccretionDisc(proxy.transform, radiusKm);
                    if (bodyInst.ringObject != null)
                        bodyInst.ringRenderer = bodyInst.ringObject.GetComponent<Renderer>();
                    Debug.Log($"Created accretion disc for {name} (NAIF ID {naifId})");
                }
                
                // Create Lensing Torus
                if (lensingRefractionMaterial != null)
                {
                    CreateLensingTorus(bodyInst, radiusKm);
                    Debug.Log($"Created lensing torus for {name}");
                }
            }

            if (uiManager != null && uiManager.EnableLabels)
            {
                CreateLabelForBody(bodyInst);
                Debug.Log($"Created label for {bodyInst.name}");
            }

            bodies.Add(bodyInst);

            if (naifId == 399 && !localEarthFound)
            {
                // Position player at a good distance in front of Earth for viewing
                Vector3d earthOffset = new Vector3d(0, 0, -0.1); // 0.1 AU in front of Earth along -Z axis
                playerRealPosAu = realPosAu.Add(earthOffset, SECTOR_SIZE_AU);
                localEarthFound = true;
                Debug.Log($"Player positioned in front of Earth at: {playerRealPosAu}");
            }
            
            // Increment shadow vector index for every valid body row processed
            if (useShadowVectors)
            {
                shadowVectorIndex++;
            }
        }

        return localEarthFound;
    }

    private List<Vector4> shadowVectors = new List<Vector4>();
    
    private void LoadShadowVectors()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "PlanetDatasetPlus/shadow_vectors.bytes");
        if (!File.Exists(path))
        {
            Debug.LogWarning("shadow_vectors.bytes not found. Shadows may be incorrect.");
            return;
        }
        
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            int vectorCount = bytes.Length / 16; // 4 floats * 4 bytes
            
            shadowVectors.Clear();
            
            for (int i = 0; i < vectorCount; i++)
            {
                int offset = i * 16;
                float x = System.BitConverter.ToSingle(bytes, offset);
                float y = System.BitConverter.ToSingle(bytes, offset + 4);
                float z = System.BitConverter.ToSingle(bytes, offset + 8);
                float w = System.BitConverter.ToSingle(bytes, offset + 12);
                
                shadowVectors.Add(new Vector4(x, y, z, w));
            }
            
            Debug.Log($"Loaded {shadowVectors.Count} shadow vectors.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load shadow vectors: {e.Message}");
        }
    }

    private void CreateLabelForBody(BodyInstance body)
    {
        if (uiManager == null)
        {
            Debug.LogWarning("UI Manager not available. Cannot create label.");
            return;
        }
        
        // Delegate label creation to UI manager
        body.labelUI = uiManager.CreateLabelForBody(body.name);
        
        if (body.labelUI != null)
        {
            Debug.Log($"Created label for {body.name} via UI Manager");
        }
    }

    private float ParseFloat(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    private void LoadObjectNamesFromJson()
    {
        string jsonPath = Path.Combine(Application.streamingAssetsPath, objectNamesJsonFileName);
        
        if (!File.Exists(jsonPath))
        {
            Debug.LogWarning($"Object names JSON not found at {jsonPath}. Using fallback names.");
            return;
        }
        
        try
        {
            string jsonText = File.ReadAllText(jsonPath);
            
            // Parse JSON manually (Unity's JsonUtility doesn't support dictionaries directly)
            // Simple JSON parser for string:string dictionary
            jsonText = jsonText.Trim();
            if (jsonText.StartsWith("{") && jsonText.EndsWith("}"))
            {
                jsonText = jsonText.Substring(1, jsonText.Length - 2); // Remove outer braces
                string[] entries = jsonText.Split(',');
                
                foreach (string entry in entries)
                {
                    string trimmedEntry = entry.Trim();
                    if (string.IsNullOrEmpty(trimmedEntry)) continue;
                    
                    // Split by first colon
                    int colonIndex = trimmedEntry.IndexOf(':');
                    if (colonIndex < 0) continue;
                    
                    string keyPart = trimmedEntry.Substring(0, colonIndex).Trim();
                    string valuePart = trimmedEntry.Substring(colonIndex + 1).Trim();
                    
                    // Remove quotes
                    keyPart = keyPart.Trim('"');
                    valuePart = valuePart.Trim('"');
                    
                    // Parse key as long to support large Gaia source IDs
                    if (long.TryParse(keyPart, out long naifId))
                    {
                        naifIdToName[naifId] = valuePart;
                    }
                }
            }
            
            Debug.Log($"Loaded {naifIdToName.Count} object names from {objectNamesJsonFileName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load object names from JSON: {e.Message}");
        }
    }
    
    private string GetBodyName(long naifId, string objectType)
    {
        // First check if we have a name in the loaded JSON mapping
        if (naifIdToName.ContainsKey(naifId))
        {
            return naifIdToName[naifId];
        }
        
        // Fallback for unknown IDs
        return $"{objectType} {naifId}";
    }

    private string CleanBodyName(string rawName)
    {
        // Remove NAIF ID prefix (e.g., "399 EARTH" -> "EARTH")
        string cleaned = rawName;
        
        // Find the first space and take everything after it
        int spaceIndex = cleaned.IndexOf(' ');
        if (spaceIndex >= 0 && spaceIndex < cleaned.Length - 1)
        {
            cleaned = cleaned.Substring(spaceIndex + 1);
        }
        
        // Remove "BARYCENTER" suffix for cleaner names
        cleaned = cleaned.Replace(" BARYCENTER", "");
        
        // Convert to title case for better readability
        cleaned = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLower());
        
        return cleaned;
    }
    
    private GameObject CreateHighQualitySphere(string name, int subdivisions)
    {
        GameObject sphere = new GameObject(name);
        MeshFilter meshFilter = sphere.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = sphere.AddComponent<MeshRenderer>();
        
        // Create icosphere mesh for better quality
        Mesh mesh = CreateIcosphereMesh(subdivisions);
        meshFilter.mesh = mesh;
        
        return sphere;
    }
    
    private Mesh CreateIcosphereMesh(int subdivisions)
    {
        // Create an icosphere (geodesic sphere) which has much better triangle distribution than UV sphere
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        
        // Golden ratio for icosahedron
        float phi = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;
        float invPhi = 1.0f / phi;
        
        // Create initial icosahedron vertices
        vertices.AddRange(new Vector3[] {
            new Vector3(-invPhi, phi, 0).normalized,
            new Vector3(invPhi, phi, 0).normalized,
            new Vector3(-invPhi, -phi, 0).normalized,
            new Vector3(invPhi, -phi, 0).normalized,
            new Vector3(0, -invPhi, phi).normalized,
            new Vector3(0, invPhi, phi).normalized,
            new Vector3(0, -invPhi, -phi).normalized,
            new Vector3(0, invPhi, -phi).normalized,
            new Vector3(phi, 0, -invPhi).normalized,
            new Vector3(phi, 0, invPhi).normalized,
            new Vector3(-phi, 0, -invPhi).normalized,
            new Vector3(-phi, 0, invPhi).normalized
        });
        
        // Create initial icosahedron triangles
        int[] initialTriangles = {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };
        
        triangles.AddRange(initialTriangles);
        
        // Subdivide
        Dictionary<string, int> midpointCache = new Dictionary<string, int>();
        
        for (int i = 0; i < subdivisions; i++)
        {
            List<int> newTriangles = new List<int>();
            
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int v1 = triangles[t];
                int v2 = triangles[t + 1];
                int v3 = triangles[t + 2];
                
                int m1 = GetMidpoint(v1, v2, vertices, midpointCache);
                int m2 = GetMidpoint(v2, v3, vertices, midpointCache);
                int m3 = GetMidpoint(v3, v1, vertices, midpointCache);
                
                newTriangles.AddRange(new int[] { v1, m1, m3, v2, m2, m1, v3, m3, m2, m1, m2, m3 });
            }
            
            triangles = newTriangles;
        }
        
        // Generate UVs with proper seam handling
        // We need to duplicate vertices at the seam where U wraps around
        List<Vector3> finalVertices = new List<Vector3>();
        List<Vector2> finalUvs = new List<Vector2>();
        List<int> finalTriangles = new List<int>();
        
        // Process each triangle independently to handle seam correctly
        for (int t = 0; t < triangles.Count; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];
            
            Vector3 p0 = vertices[i0];
            Vector3 p1 = vertices[i1];
            Vector3 p2 = vertices[i2];
            
            // Calculate UVs for each vertex
            Vector2 uv0 = CalculateSphericalUV(p0);
            Vector2 uv1 = CalculateSphericalUV(p1);
            Vector2 uv2 = CalculateSphericalUV(p2);
            
            // Fix UV seam - if any UV.x difference is > 0.5, we have a seam crossing
            FixUVSeam(ref uv0, ref uv1, ref uv2);
            
            // Add vertices (duplicated per triangle for correct UVs)
            int baseIndex = finalVertices.Count;
            finalVertices.Add(p0);
            finalVertices.Add(p1);
            finalVertices.Add(p2);
            
            finalUvs.Add(uv0);
            finalUvs.Add(uv1);
            finalUvs.Add(uv2);
            
            finalTriangles.Add(baseIndex);
            finalTriangles.Add(baseIndex + 1);
            finalTriangles.Add(baseIndex + 2);
        }
        
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Support more than 65k vertices
        mesh.vertices = finalVertices.ToArray();
        mesh.triangles = finalTriangles.ToArray();
        mesh.uv = finalUvs.ToArray();
        
        // For a unit sphere, the normal at each vertex IS the vertex position (normalized)
        // This gives smooth shading even with duplicated vertices
        Vector3[] normals = new Vector3[finalVertices.Count];
        for (int i = 0; i < finalVertices.Count; i++)
        {
            normals[i] = finalVertices[i].normalized;
        }
        mesh.normals = normals;
        
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    private Vector2 CalculateSphericalUV(Vector3 p)
    {
        // Spherical to UV mapping
        // Use (z, x) instead of (x, z) to match standard equirectangular projection
        // This prevents texture mirroring
        float u = Mathf.Atan2(p.z, p.x) / (2f * Mathf.PI) + 0.5f;
        float v = Mathf.Asin(Mathf.Clamp(p.y, -1f, 1f)) / Mathf.PI + 0.5f;
        return new Vector2(u, v);
    }
    
    private void FixUVSeam(ref Vector2 uv0, ref Vector2 uv1, ref Vector2 uv2)
    {
        // Detect if this triangle crosses the UV seam (U wraps from ~0 to ~1)
        // If any two vertices have U difference > 0.5, adjust the lower one by +1
        
        float threshold = 0.5f;
        
        // Check each pair and adjust
        if (Mathf.Abs(uv0.x - uv1.x) > threshold)
        {
            if (uv0.x < uv1.x) uv0.x += 1f;
            else uv1.x += 1f;
        }
        if (Mathf.Abs(uv1.x - uv2.x) > threshold)
        {
            if (uv1.x < uv2.x) uv1.x += 1f;
            else uv2.x += 1f;
        }
        if (Mathf.Abs(uv0.x - uv2.x) > threshold)
        {
            if (uv0.x < uv2.x) uv0.x += 1f;
            else uv2.x += 1f;
        }
    }
    
    private int GetMidpoint(int v1, int v2, List<Vector3> vertices, Dictionary<string, int> cache)
    {
        string key = v1 < v2 ? $"{v1},{v2}" : $"{v2},{v1}";
        
        if (cache.ContainsKey(key))
        {
            return cache[key];
        }
        
        Vector3 midpoint = ((vertices[v1] + vertices[v2]) * 0.5f).normalized;
        vertices.Add(midpoint);
        int index = vertices.Count - 1;
        cache[key] = index;
        
        return index;
    }
    
    private GameObject CreateSaturnRings(Transform parentPlanet, float planetRadiusKm)
    {
        GameObject ringObject = new GameObject("SaturnRings");
        ringObject.transform.SetParent(parentPlanet, false);
        ringObject.transform.localPosition = Vector3.zero;
        
        // Create ring mesh
        MeshFilter meshFilter = ringObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = ringObject.AddComponent<MeshRenderer>();
        
        // Create the ring geometry
        Mesh ringMesh = CreateRingMesh(saturnRingInnerRadius, saturnRingOuterRadius, 128);
        meshFilter.mesh = ringMesh;
        
        // Apply material with transparency
        if (saturnRingMaterial != null)
        {
            meshRenderer.sharedMaterial = saturnRingMaterial;
        }
        
        // No additional rotation needed - rings inherit Saturn's tilt from parent
        // The planet is already tilted 27 degrees on Z axis
        ringObject.transform.localRotation = Quaternion.identity;
        
        // The scale will be set in UpdateBodyProxies along with the planet
        ringObject.transform.localScale = Vector3.one;
        
        return ringObject;
    }
    
    private GameObject CreateAccretionDisc(Transform parentBlackHole, float blackHoleRadiusKm)
    {
        GameObject discObject = new GameObject("AccretionDisc");
        discObject.transform.SetParent(parentBlackHole, false);
        discObject.transform.localPosition = Vector3.zero;
        
        // Create disc mesh
        MeshFilter meshFilter = discObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = discObject.AddComponent<MeshRenderer>();
        
        // Create the disc geometry (reuse ring mesh creation)
        Mesh discMesh = CreateRingMesh(accretionDiscInnerRadius, accretionDiscOuterRadius, 256);
        meshFilter.mesh = discMesh;
        
        // Apply accretion disc material
        if (accretionDiscMaterial != null)
        {
            meshRenderer.sharedMaterial = accretionDiscMaterial;
        }
        
        // No rotation - disc is horizontal by default
        discObject.transform.localRotation = Quaternion.identity;
        discObject.transform.localScale = Vector3.one;
        
        return discObject;
    }
    
    private void CreateGravitationalLensingRing(Transform parent, Mesh baseMesh)
    {
        // Create a lensing ring at the photon sphere radius (1.5 Rs)
        // This represents light from the disc that's been bent around the black hole
        
        GameObject lensingRing = new GameObject("LensingRing");
        lensingRing.transform.SetParent(parent, false);
        lensingRing.transform.localPosition = Vector3.zero;
        
        MeshFilter meshFilter = lensingRing.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = lensingRing.AddComponent<MeshRenderer>();
        
        // Create a toroidal/curved mesh around the photon sphere
        Mesh bentMesh = CreateBentLensingMesh(1.5f, accretionDiscInnerRadius, accretionDiscOuterRadius);
        meshFilter.mesh = bentMesh;
        
        // Create dimmer material for lensed light
        Material lensedMaterial = new Material(accretionDiscMaterial);
        if (lensedMaterial.HasProperty("_Intensity"))
        {
            float originalIntensity = lensedMaterial.GetFloat("_Intensity");
            lensedMaterial.SetFloat("_Intensity", originalIntensity * lensingImageOpacity);
        }
        
        meshRenderer.material = lensedMaterial;
        lensingRing.transform.localRotation = Quaternion.identity;
        lensingRing.transform.localScale = Vector3.one;
    }
    
    private Mesh CreateBentLensingMesh(float photonSphereRadius, float discInner, float discOuter)
    {
        Mesh mesh = new Mesh();
        mesh.name = "BentLensingMesh";
        
        int angularSegments = 256; // Around the ring
        int radialSegments = 32;   // From inner to outer
        int verticalSegments = 16; // Curve segments
        
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        
        // Create a curved surface that wraps around the photon sphere
        for (int r = 0; r <= radialSegments; r++)
        {
            float radialT = (float)r / radialSegments;
            float radius = Mathf.Lerp(discInner, discOuter, radialT);
            
            for (int v = 0; v <= verticalSegments; v++)
            {
                // Vertical curve parameter (0 to 1, where 0.5 is the equator)
                float verticalT = (float)v / verticalSegments;
                
                // Create curvature: arc from -90° to +90° around photon sphere
                float curveAngle = (verticalT - 0.5f) * Mathf.PI; // -PI/2 to +PI/2
                float verticalOffset = photonSphereRadius * Mathf.Sin(curveAngle);
                float radialOffset = photonSphereRadius * (1.0f - Mathf.Cos(curveAngle));
                
                float adjustedRadius = radius - radialOffset;
                
                for (int a = 0; a <= angularSegments; a++)
                {
                    float angle = (float)a / angularSegments * 2f * Mathf.PI;
                    
                    Vector3 pos = new Vector3(
                        Mathf.Cos(angle) * adjustedRadius,
                        verticalOffset,
                        Mathf.Sin(angle) * adjustedRadius
                    );
                    
                    vertices.Add(pos);
                    
                    // UV mapping: x = radial, y = angular (for shader rotation)
                    uvs.Add(new Vector2(radialT, (float)a / angularSegments));
                }
            }
        }
        
        // Generate triangles
        for (int r = 0; r < radialSegments; r++)
        {
            for (int v = 0; v < verticalSegments; v++)
            {
                int angularCount = angularSegments + 1;
                int verticalCount = verticalSegments + 1;
                
                for (int a = 0; a < angularSegments; a++)
                {
                    int i0 = r * verticalCount * angularCount + v * angularCount + a;
                    int i1 = r * verticalCount * angularCount + v * angularCount + (a + 1);
                    int i2 = r * verticalCount * angularCount + (v + 1) * angularCount + a;
                    int i3 = r * verticalCount * angularCount + (v + 1) * angularCount + (a + 1);
                    
                    int i4 = (r + 1) * verticalCount * angularCount + v * angularCount + a;
                    int i5 = (r + 1) * verticalCount * angularCount + v * angularCount + (a + 1);
                    int i6 = (r + 1) * verticalCount * angularCount + (v + 1) * angularCount + a;
                    int i7 = (r + 1) * verticalCount * angularCount + (v + 1) * angularCount + (a + 1);
                    
                    // First triangle
                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i4);
                    
                    triangles.Add(i2);
                    triangles.Add(i6);
                    triangles.Add(i4);
                    
                    // Second triangle
                    triangles.Add(i1);
                    triangles.Add(i5);
                    triangles.Add(i3);
                    
                    triangles.Add(i3);
                    triangles.Add(i5);
                    triangles.Add(i7);
                }
            }
        }
        
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    private void CreateLensedDiscImage(Transform parent, Mesh discMesh, string name, 
                                       Vector3 localPosition, float opacityMultiplier)
    {
        GameObject lensedDisc = new GameObject(name);
        lensedDisc.transform.SetParent(parent, false);
        lensedDisc.transform.localPosition = localPosition;
        lensedDisc.transform.localRotation = Quaternion.identity;
        lensedDisc.transform.localScale = Vector3.one;
        
        MeshFilter meshFilter = lensedDisc.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = lensedDisc.AddComponent<MeshRenderer>();
        
        meshFilter.mesh = discMesh; // Reuse same mesh
        
        // Create material instance with reduced opacity
        Material lensedMaterial = new Material(accretionDiscMaterial);
        
        // Reduce intensity for lensed images (they appear dimmer)
        if (lensedMaterial.HasProperty("_Intensity"))
        {
            float originalIntensity = lensedMaterial.GetFloat("_Intensity");
            lensedMaterial.SetFloat("_Intensity", originalIntensity * opacityMultiplier);
        }
        
        meshRenderer.material = lensedMaterial;
    }
    
    private Mesh CreateRingMesh(float innerRadius, float outerRadius, int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "RingMesh";
        
        int vertexCount = (segments + 1) * 2;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        
        // Create vertices in a ring pattern
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)i / segments * 2f * Mathf.PI;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            
            // Inner vertex
            int innerIndex = i * 2;
            vertices[innerIndex] = new Vector3(cos * innerRadius, 0, sin * innerRadius);
            uvs[innerIndex] = new Vector2(0, (float)i / segments);  // Swapped: U=radial position, V=angle
            normals[innerIndex] = Vector3.up;
            
            // Outer vertex
            int outerIndex = i * 2 + 1;
            vertices[outerIndex] = new Vector3(cos * outerRadius, 0, sin * outerRadius);
            uvs[outerIndex] = new Vector2(1, (float)i / segments);  // Swapped: U=radial position, V=angle
            normals[outerIndex] = Vector3.up;
        }
        
        // Create triangles
        int[] triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 6;
            int vertexIndex = i * 2;
            
            // First triangle
            triangles[baseIndex] = vertexIndex;
            triangles[baseIndex + 1] = vertexIndex + 1;
            triangles[baseIndex + 2] = vertexIndex + 2;
            
            // Second triangle
            triangles[baseIndex + 3] = vertexIndex + 1;
            triangles[baseIndex + 4] = vertexIndex + 3;
            triangles[baseIndex + 5] = vertexIndex + 2;
        }
        
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = triangles;
        
        return mesh;
    }
    
    private void CreateLensingTorus(BodyInstance body, float planetRadiusKm)
    {
        GameObject torus = new GameObject("LensingTorus");
        torus.transform.SetParent(body.proxy, false);
        torus.transform.localPosition = Vector3.zero;
        
        MeshFilter mf = torus.AddComponent<MeshFilter>();
        MeshRenderer mr = torus.AddComponent<MeshRenderer>();
        
        // Dimensions per user request:
        // "inner radius should be the radius of the black hole" -> 1.0
        // "z: radius of accretion disc" -> Thickness = accretionDiscOuterRadius
        // "x and y 1,5 times that size" -> Outer Radius = 1.5 * accretionDiscOuterRadius
        
        float innerRadius = 1.0f;
        float outerRadius = accretionDiscOuterRadius * 1.5f;
        float thickness = accretionDiscOuterRadius;
        
        Mesh mesh = CreateLensTorusMesh(innerRadius, outerRadius, thickness, 64, 32);
        mf.mesh = mesh;
        mr.sharedMaterial = lensingRefractionMaterial;
        
        // No scaling needed, mesh is generated to size
        torus.transform.localScale = Vector3.one;
        
        body.lensingTorus = torus;
    }
    
    private Mesh CreateLensTorusMesh(float innerRadius, float outerRadius, float thickness, int radialSegments, int tubularSegments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "LensTorus";

        Vector3[] vertices = new Vector3[(radialSegments + 1) * (tubularSegments + 1)];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[radialSegments * tubularSegments * 6];

        float mainRadius = (innerRadius + outerRadius) * 0.5f;
        float tubeWidth = (outerRadius - innerRadius) * 0.5f;
        float tubeHeight = thickness * 0.5f;

        for (int j = 0; j <= radialSegments; j++)
        {
            for (int i = 0; i <= tubularSegments; i++)
            {
                float u = (float)i / tubularSegments * Mathf.PI * 2.0f;
                float v = (float)j / radialSegments * Mathf.PI * 2.0f;

                // Elliptical Cross Section:
                // Width derived from tubeWidth
                // Height derived from tubeHeight
                
                float cx = Mathf.Cos(v);
                float cy = Mathf.Sin(v);
                
                // Tube Offset from main radius
                float tubeXOffset = tubeWidth * Mathf.Cos(u);
                float tubeZOffset = tubeHeight * Mathf.Sin(u);
                
                // Vertex Position
                float x = (mainRadius + tubeXOffset) * cx;
                float y = (mainRadius + tubeXOffset) * cy;
                float z = tubeZOffset;

                int index = j * (tubularSegments + 1) + i;
                vertices[index] = new Vector3(x, y, z);
                
                // Normal Calculation (Ellipsoidal Normal)
                // Tangent vector along tube ring (dT/du)
                float dxdu = -tubeWidth * Mathf.Sin(u) * cx;
                float dydu = -tubeWidth * Mathf.Sin(u) * cy;
                float dzdu = tubeHeight * Mathf.Cos(u);
                Vector3 tangentU = new Vector3(dxdu, dydu, dzdu).normalized;
                
                // Tangent vector along main ring (dT/dv)
                float dxdv = -(mainRadius + tubeXOffset) * cy;
                float dydv = (mainRadius + tubeXOffset) * cx;
                float dzdv = 0;
                Vector3 tangentV = new Vector3(dxdv, dydv, dzdv).normalized;
                
                // Normal is cross product
                normals[index] = Vector3.Cross(tangentV, tangentU).normalized; // Or U x V? Standard is U corresponds to "wrapping"
                // Check orientation: U goes 0..2PI. V goes 0..2PI.
                // Standard torus normals point OUT.
                
                uvs[index] = new Vector2((float)j / radialSegments, (float)i / tubularSegments);
            }
        }

        int t = 0;
        for (int j = 0; j < radialSegments; j++)
        {
            for (int i = 0; i < tubularSegments; i++)
            {
                int nextI = i + 1;
                int nextJ = j + 1;

                int a = j * (tubularSegments + 1) + i;
                int b = j * (tubularSegments + 1) + nextI;
                int c = nextJ * (tubularSegments + 1) + i;
                int d = nextJ * (tubularSegments + 1) + nextI;

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;

                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    private void LoadPlanetMaterials()
    {
        string path = Path.Combine(Application.streamingAssetsPath, planetMaterialsJsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Planet materials JSON not found at {path}. Using default materials.");
            return;
        }
        
        try
        {
            string jsonText = File.ReadAllText(path);
            
            // Parse JSON manually - now each entry is an object with path, emitting, illuminated_by
            // Simple JSON parser for our specific structure
            jsonText = jsonText.Trim();
            if (jsonText.StartsWith("{") && jsonText.EndsWith("}"))
            {
                // Remove outer braces and split by closing brace followed by comma
                jsonText = jsonText.Substring(1, jsonText.Length - 2);
                
                // Split entries more carefully to handle nested objects
                List<string> entries = new List<string>();
                int braceDepth = 0;
                int startIndex = 0;
                
                for (int i = 0; i < jsonText.Length; i++)
                {
                    if (jsonText[i] == '{') braceDepth++;
                    else if (jsonText[i] == '}') braceDepth--;
                    else if (jsonText[i] == ',' && braceDepth == 0)
                    {
                        entries.Add(jsonText.Substring(startIndex, i - startIndex));
                        startIndex = i + 1;
                    }
                }
                // Add last entry
                if (startIndex < jsonText.Length)
                    entries.Add(jsonText.Substring(startIndex));
                
                foreach (string entry in entries)
                {
                    string trimmedEntry = entry.Trim();
                    if (string.IsNullOrEmpty(trimmedEntry)) continue;
                    
                    // Parse "ID": { ... }
                    int firstColon = trimmedEntry.IndexOf(':');
                    if (firstColon < 0) continue;
                    
                    string keyPart = trimmedEntry.Substring(0, firstColon).Trim().Trim('"');
                    string valuePart = trimmedEntry.Substring(firstColon + 1).Trim();
                    
                    if (!long.TryParse(keyPart, out long naifId)) continue;
                    
                    // Parse the object { "path": "...", "emitting": ..., "illuminated_by": ... }
                    if (!valuePart.StartsWith("{") || !valuePart.EndsWith("}")) continue;
                    
                    valuePart = valuePart.Substring(1, valuePart.Length - 2); // Remove braces
                    
                    // Extract properties
                    string assetPath = ExtractJsonStringValue(valuePart, "path");
                    string atmospherePath = ExtractJsonStringValue(valuePart, "atmosphere");
                    string nightTexturePath = ExtractJsonStringValue(valuePart, "night-texture");
                    bool emitting = ExtractJsonBoolValue(valuePart, "emitting");
                    long? illuminatedBy = ExtractJsonLongValue(valuePart, "illuminated_by");
                    
                    // Store emitting and illuminated_by properties
                    bodyEmitting[naifId] = emitting;
                    bodyIlluminatedBy[naifId] = illuminatedBy;
                    
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    
                    // Check if this is a material (.mat) or texture (.jpg, .png, .tif, etc.)
                    if (assetPath.EndsWith(".mat"))
                    {
                        // Load material (for stars with procedural shaders)
                        Material material = null;
                        
#if UNITY_EDITOR
                        material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
#else
                        string resourcesPath = assetPath.Replace("Assets/", "").Replace(".mat", "");
                        material = Resources.Load<Material>(resourcesPath);
#endif
                        
                        if (material != null)
                        {
                            planetMaterials[naifId] = material;
                            Debug.Log($"Loaded material for NAIF ID {naifId}: {assetPath} (emitting: {emitting})");
                        }
                        else
                        {
                            Debug.LogWarning($"Could not load material at path: {assetPath} for NAIF ID {naifId}");
                        }
                    }
                    else
                    {
                        // Load texture (for planets/moons)
                        Texture2D texture = null;
                        
#if UNITY_EDITOR
                        texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
#else
                        string resourcesPath = assetPath.Replace("Assets/", "");
                        // Remove extension for Resources.Load
                        int lastDot = resourcesPath.LastIndexOf('.');
                        if (lastDot > 0) resourcesPath = resourcesPath.Substring(0, lastDot);
                        texture = Resources.Load<Texture2D>(resourcesPath);
#endif
                        
                        if (texture != null)
                        {
                            planetTextures[naifId] = texture;
                            Debug.Log($"Loaded texture for NAIF ID {naifId}: {assetPath} (emitting: {emitting}, illuminated_by: {illuminatedBy})");
                        }
                        else
                        {
                            Debug.LogWarning($"Could not load texture at path: {assetPath} for NAIF ID {naifId}");
                        }
                    }

                    // Load atmosphere texture if present
                    if (!string.IsNullOrEmpty(atmospherePath))
                    {
                        Texture2D atmTexture = null;
#if UNITY_EDITOR
                        atmTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(atmospherePath);
#else
                        string atmResourcesPath = atmospherePath.Replace("Assets/", "");
                        int lastDotAtm = atmResourcesPath.LastIndexOf('.');
                        if (lastDotAtm > 0) atmResourcesPath = atmResourcesPath.Substring(0, lastDotAtm);
                        atmTexture = Resources.Load<Texture2D>(atmResourcesPath);
#endif
                        if (atmTexture != null)
                        {
                            planetAtmosphereTextures[naifId] = atmTexture;
                            Debug.Log($"Loaded atmosphere texture for NAIF ID {naifId}: {atmospherePath}");
                        }
                        else
                        {
                            Debug.LogWarning($"Could not load atmosphere texture at path: {atmospherePath} for NAIF ID {naifId}");
                        }
                    }

                    // Load night texture if present
                    if (!string.IsNullOrEmpty(nightTexturePath))
                    {
                        Texture2D nightTexture = null;
#if UNITY_EDITOR
                        nightTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(nightTexturePath);
#else
                        string nightResourcesPath = nightTexturePath.Replace("Assets/", "");
                        int lastDotNight = nightResourcesPath.LastIndexOf('.');
                        if (lastDotNight > 0) nightResourcesPath = nightResourcesPath.Substring(0, lastDotNight);
                        nightTexture = Resources.Load<Texture2D>(nightResourcesPath);
#endif
                        if (nightTexture != null)
                        {
                            planetNightTextures[naifId] = nightTexture;
                            Debug.Log($"Loaded night texture for NAIF ID {naifId}: {nightTexturePath}");
                        }
                        else
                        {
                            Debug.LogWarning($"Could not load night texture at path: {nightTexturePath} for NAIF ID {naifId}");
                        }
                    }
                }
            }
            
            Debug.Log($"Loaded {planetTextures.Count} planet textures and {planetMaterials.Count} custom materials.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading planet materials JSON: {e.Message}");
        }
    }
    
    private string ExtractJsonStringValue(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIndex = json.IndexOf(pattern);
        if (keyIndex < 0) return null;
        
        int colonIndex = json.IndexOf(':', keyIndex);
        if (colonIndex < 0) return null;
        
        int startQuote = json.IndexOf('"', colonIndex);
        if (startQuote < 0) return null;
        
        int endQuote = json.IndexOf('"', startQuote + 1);
        if (endQuote < 0) return null;
        
        return json.Substring(startQuote + 1, endQuote - startQuote - 1);
    }
    
    private bool ExtractJsonBoolValue(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIndex = json.IndexOf(pattern);
        if (keyIndex < 0) return false;
        
        int colonIndex = json.IndexOf(':', keyIndex);
        if (colonIndex < 0) return false;
        
        int startValue = colonIndex + 1;
        while (startValue < json.Length && char.IsWhiteSpace(json[startValue])) startValue++;
        
        if (startValue >= json.Length) return false;
        
        // Find end of value (comma or closing brace)
        int endValue = startValue;
        while (endValue < json.Length && json[endValue] != ',' && json[endValue] != '}') endValue++;
        
        string value = json.Substring(startValue, endValue - startValue).Trim();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
    
    private long? ExtractJsonLongValue(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIndex = json.IndexOf(pattern);
        if (keyIndex < 0) return null;
        
        int colonIndex = json.IndexOf(':', keyIndex);
        if (colonIndex < 0) return null;
        
        int startValue = colonIndex + 1;
        while (startValue < json.Length && char.IsWhiteSpace(json[startValue])) startValue++;
        
        if (startValue >= json.Length) return null;
        
        // Check for null value
        if (json.Substring(startValue).StartsWith("null")) return null;
        
        // Find end of value (comma or closing brace)
        int endValue = startValue;
        while (endValue < json.Length && json[endValue] != ',' && json[endValue] != '}') endValue++;
        
        string value = json.Substring(startValue, endValue - startValue).Trim();
        if (long.TryParse(value, out long result))
            return result;
        
        return null;
    }

    // --- Dynamic scaling and speed ---
    
    private void UpdateDynamicBehavior()
    {
        // Find nearest planet more frequently for better responsiveness
        if (Time.frameCount % 5 == 0) // Check every 5 frames for quicker response
        {
            FindNearestPlanet();
        }
        
        if (nearestPlanet == null) return;
        
        // When autopilot is active, use autopilot target for speed zones instead of nearest planet
        BodyInstance targetBody = (autopilotActive && autopilotTarget != null) ? autopilotTarget : nearestPlanet;
        
        // Calculate distance to target planet surface
        Vector3d offsetAu = playerRealPosAu.OffsetTo(targetBody.realPosAu, SECTOR_SIZE_AU);
        double distanceAu = offsetAu.magnitude;
        
        // Convert planet radius from km to AU for comparison
        double planetRadiusAu = targetBody.radiusKm / AU_KM;
        
        // Distance to planet surface (not center) - prevent negative distance
        distanceToNearestPlanet = (float)Math.Max(0.000000001, distanceAu - planetRadiusAu);
        
        // IMPROVED SCALING AND SPEED SYSTEM:
        // Fixed issues with flying through planets and speed decreasing when flying away
        
        float zoneDistance = (float)planetRadiusAu * 100f; // Single zone distance
        
        // Hardcoded scale values for consistent behavior
        float baseScale = 1f;      // Normal scale
        float minScale = 0.00001f;  // Minimum scale for dramatic zoom
        
        // === SCALING CALCULATION ===
        float targetScale;
        
        if (distanceToNearestPlanet < zoneDistance)
        {
            // Within zone: scaling based on distance to planet surface
            float normalizedDistance = Mathf.Clamp01(distanceToNearestPlanet / zoneDistance);
            // Use smooth curve from minimum scale to full scale
            targetScale = Mathf.Lerp(minScale, baseScale, normalizedDistance * normalizedDistance);
        }
        else
        {
            // Beyond zone: normal scale
            targetScale = baseScale;
        }
        
        // === SPEED CALCULATION ===
        // Flying away is faster than flying towards for intuitive escape mechanics
        float targetSpeed;
        
        // Determine movement direction relative to planet (use target body when autopilot active)
        Vector3d toPlanetVec = playerRealPosAu.OffsetTo(targetBody.realPosAu, SECTOR_SIZE_AU);
        Vector3 toPlanet = (Vector3)toPlanetVec.normalized;
        Vector3 lastMovement = Vector3.zero;
        
        // Get current movement direction from input
        if (moveAction != null)
        {
            Vector2 move = moveAction.action.ReadValue<Vector2>();
            float vertical = verticalAction != null ? verticalAction.action.ReadValue<float>() : 0f;
            
            if (move.sqrMagnitude > 0.01f || Mathf.Abs(vertical) > 0.01f)
            {
                Camera inputCamera = GetActiveCamera();
                if (inputCamera != null)
                {
                    lastMovement = (inputCamera.transform.right * move.x + inputCamera.transform.forward * move.y + inputCamera.transform.up * vertical).normalized;
                }
            }
        }
        
        // Calculate if moving towards or away from planet
        float movementDot = Vector3.Dot(lastMovement, toPlanet);
        bool movingTowardsPlanet = movementDot > 0.1f;
        bool movingAwayFromPlanet = movementDot < -0.1f;
        
        if (distanceToNearestPlanet < zoneDistance)
        {
            // Within zone: directional speed with asymptotic approach
            float normalizedDistance = distanceToNearestPlanet / zoneDistance; // 0 to 1
            float speedMultiplier = Mathf.Pow(normalizedDistance, 1.8f); // Moderate exponent for balanced curve
            float baseSpeed = zoneDistance * 1.2f; // Higher base speed for faster start
            float baseTargetSpeed = Mathf.Max(0.000001f, baseSpeed * speedMultiplier);
            
            if (movingAwayFromPlanet)
            {
                // Flying away: 3x faster for quick escape
                targetSpeed = baseTargetSpeed * 3f;
            }
            else
            {
                // Flying towards or neutral: normal asymptotic speed
                targetSpeed = baseTargetSpeed;
            }
        }
        else
        {
            // Beyond zone: normal distance-based speed
            targetSpeed = Mathf.Max(distanceToNearestPlanet, 0.01f); // Direct distance speed in AU/s
        }
        
        // Smooth transitions
        float scaleLerpSpeed = 20f;
        float speedLerpSpeed = 25f;
        
        currentScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * scaleLerpSpeed);
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * speedLerpSpeed);
        
        // Apply scale to camera
        Camera cam = GetActiveCamera();
        if (cam != null)
        {
            cam.transform.localScale = Vector3.one * currentScale;
            
            // Extra debug for scale application
            if (Time.frameCount % 60 == 0) // Every 60 frames (about 1 second)
            {
                Debug.Log($"SCALE DEBUG - Target: {targetScale:F5}, Current: {currentScale:F5}, Applied to: {cam.name}, LocalScale: {cam.transform.localScale}");
            }
        }
        
        // Update HUD
        UpdateHUD();
        
        // Debug info (remove or comment out when not needed)
        if (Time.frameCount % 30 == 0) // Every 30 frames
        {
            string planetInfo = autopilotActive && autopilotTarget != null ? $"{targetBody.name} (autopilot target)" : targetBody.name;
            Debug.Log($"Distance-Based Speed - Distance: {distanceToNearestPlanet:F6} AU, Scale: {currentScale:F5}, Speed: {currentSpeed:F6} AU/s, Planet: {planetInfo}");
        }
    }
    
    private void UpdateHUD()
    {
        if (uiManager == null || !uiManager.EnableHUD || uiManager.HudText == null) return;
        
        // Calculate speed in real units
        double speedKmPerSecond = actualSpeed * AU_KM; // Convert AU/s to km/s
        double lightSpeedPercentage = (speedKmPerSecond / SPEED_OF_LIGHT_KM_S) * 100.0;
        
        // Format speed display
        string speedDisplay;
        if (speedKmPerSecond >= 1_000_000) // Millions km/s
            speedDisplay = $"{speedKmPerSecond / 1_000_000:F2}M km/s";
        else if (speedKmPerSecond >= 1_000) // Thousands km/s
            speedDisplay = $"{speedKmPerSecond / 1_000:F2}k km/s";
        else if (speedKmPerSecond >= 1) 
            speedDisplay = $"{speedKmPerSecond:F1} km/s";
        else
            speedDisplay = $"{speedKmPerSecond:F3} km/s";
        
        // Format lightspeed percentage
        string lightSpeedDisplay;
        if (lightSpeedPercentage >= 100)
            lightSpeedDisplay = $"{lightSpeedPercentage:F1}% lightspeed";
        else if (lightSpeedPercentage >= 1)
            lightSpeedDisplay = $"{lightSpeedPercentage:F2}% lightspeed";
        else if (lightSpeedPercentage >= 0.01)
            lightSpeedDisplay = $"{lightSpeedPercentage:F4}% lightspeed";
        else if (lightSpeedPercentage > 0)
            lightSpeedDisplay = $"{lightSpeedPercentage:F6}% lightspeed";
        else
            lightSpeedDisplay = "0% lightspeed";
        
        // Calculate distance from Sun using hierarchical position system
        Vector3d offsetFromSun = GetPlayerPositionRelativeToSun();
        double distanceFromSunAu = offsetFromSun.magnitude;
        double distanceFromSunKm = distanceFromSunAu * AU_KM;
        
        // Format distance display
        string distanceDisplay;
        if (distanceFromSunKm >= 1_000_000_000) // >= 1 billion km, switch to lightyears
        {
            double distanceLightyears = distanceFromSunKm / LIGHTYEAR_KM;
            if (distanceLightyears >= 1000)
                distanceDisplay = $"{distanceLightyears / 1000:F2}k lightyears";
            else if (distanceLightyears >= 1)
                distanceDisplay = $"{distanceLightyears:F2} lightyears";
            else
                distanceDisplay = $"{distanceLightyears:F4} lightyears";
        }
        else
        {
            if (distanceFromSunKm >= 1_000_000) // Millions km
                distanceDisplay = $"{distanceFromSunKm / 1_000_000:F2}M km";
            else if (distanceFromSunKm >= 1_000) // Thousands km
                distanceDisplay = $"{distanceFromSunKm / 1_000:F1}k km";
            else
                distanceDisplay = $"{distanceFromSunKm:F0} km";
        }
        
        // Build HUD text - simplified for distance-based speed
        uiManager.HudText.text = $"Speed: {speedDisplay} ({lightSpeedDisplay})\n" +
                      $"Distance from Sun: {distanceDisplay}\n" +
                      $"Mode: DISTANCE-BASED";
        
        if (nearestPlanet != null)
        {
            double distanceToPlanetKm = distanceToNearestPlanet * AU_KM;
            string planetDistanceDisplay;
            
            // Show both AU and km when distance is reasonable (< 10 million km)
            if (distanceToPlanetKm < 10_000_000)
            {
                string kmDisplay;
                if (distanceToPlanetKm >= 1_000_000)
                    kmDisplay = $"{distanceToPlanetKm / 1_000_000:F2}M km";
                else if (distanceToPlanetKm >= 1_000)
                    kmDisplay = $"{distanceToPlanetKm / 1_000:F1}k km";
                else
                    kmDisplay = $"{distanceToPlanetKm:F0} km";
                
                planetDistanceDisplay = $"{distanceToNearestPlanet:F6} AU ({kmDisplay})";
            }
            else
            {
                planetDistanceDisplay = $"{distanceToNearestPlanet:F6} AU";
            }
            
            uiManager.HudText.text += $"\nNearest: {nearestPlanet.name}\n" +
                           $"Distance: {planetDistanceDisplay}";
        }
        
        // Add stellar manager info
        if (stellarManager != null)
        {
            uiManager.HudText.text += $"\nStars Visible: {stellarManager.GetVisibleStarCount()}";
            if (!stellarManager.IsDataLoaded())
            {
                uiManager.HudText.text += " (Loading...)";
            }
        }
        
        // Add autopilot status
        if (IsOrbiting && orbitTargetBody != null)
        {
            uiManager.HudText.text += $"\n\n[ORBIT] ⟳ Orbiting {orbitTargetBody.name}\nRadius: {orbitDistanceAu:F6} AU\nPress O to disengage";
        }
        else if (autopilotActive && autopilotTarget != null)
        {
            Vector3d toTarget = playerRealPosAu.OffsetTo(autopilotTarget.realPosAu, SECTOR_SIZE_AU);
            double distanceAu = toTarget.magnitude;
            double distKm = distanceAu * AU_KM;
            
            string autopilotDistDisplay;
            if (distKm >= 1_000_000)
                autopilotDistDisplay = $"{distKm / 1_000_000:F2}M km";
            else if (distKm >= 1_000)
                autopilotDistDisplay = $"{distKm / 1_000:F1}k km";
            else
                autopilotDistDisplay = $"{distKm:F0} km";
            
            uiManager.HudText.text += $"\n\n[AUTOPILOT] → {autopilotTarget.name}\nDistance: {autopilotDistDisplay}\nPress X to cancel";
        }
        else if (!autopilotActive && (uiManager == null || !uiManager.AutopilotMenuOpen))
        {
            uiManager.HudText.text += "\n\nPress X for Autopilot";
            if (nearestPlanet != null)
            {
                uiManager.HudText.text += $" | Press O to Orbit {nearestPlanet.name}";
                if (nearestPlanet.planetData != null)
                {
                  uiManager.HudText.text += $" | Press I for info on {nearestPlanet.name}";
                }
            }
        }
    }
    
    private void FindNearestPlanet()
    {
        double closestDistanceSqr = double.PositiveInfinity;
        BodyInstance closest = null;
        
        foreach (var body in bodies)
        {
            Vector3d offset = playerRealPosAu.OffsetTo(body.realPosAu, SECTOR_SIZE_AU);
            double distanceSqr = offset.sqrMagnitude;
            
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closest = body;
            }
        }
        
        nearestPlanet = closest;
    }

    // --- Runtime updates ---

    private void ToggleOrbit()
    {
        // Mutual exclusion: Cannot orbit while autopilot is active
        if (autopilotActive) return;

        if (IsOrbiting)
        {
            IsOrbiting = false;
            orbitTargetBody = null;
            Debug.Log("Orbit disengaged.");
        }
        else
        {
            if (nearestPlanet != null)
            {
                IsOrbiting = true;
                orbitTargetBody = nearestPlanet;

                // Calculate initial orbit parameters relative to planet
                Vector3d relPos = orbitTargetBody.realPosAu.OffsetTo(playerRealPosAu, SECTOR_SIZE_AU);

                // orbit in XZ plane relative to planet
                orbitDistanceAu = Math.Sqrt(relPos.x * relPos.x + relPos.z * relPos.z);
                orbitHeightAu = relPos.y;

                orbitAngle = Mathf.Atan2((float)relPos.z, (float)relPos.x);

                Debug.Log($"Orbit engaged around {orbitTargetBody.name}");
            }
            else
            {
                Debug.Log("Cannot orbit: nearest planet is null.");
            }
        }
    }

    private void UpdateOrbitMovement()
    {
        if (orbitTargetBody == null)
        {
            IsOrbiting = false;
            return;
        }

        // Update angle
        orbitAngle += orbitAngularVelocity * Time.deltaTime;
        
        // Calculate new relative position (XZ circle)
        double newX = orbitDistanceAu * Math.Cos(orbitAngle);
        double newZ = orbitDistanceAu * Math.Sin(orbitAngle);
        
        Vector3d newRelPos = new Vector3d(newX, orbitHeightAu, newZ);
        
        // Update player position
        playerRealPosAu = orbitTargetBody.realPosAu.Add(newRelPos, SECTOR_SIZE_AU);
    }
    
    private void UpdateOrbitCamera()
    {
        if (orbitTargetBody == null || orbitTargetBody.proxy == null) return;
        
        Camera cam = GetActiveCamera();
        if (cam != null)
        {
            cam.transform.LookAt(orbitTargetBody.proxy.position);
        }
    }

    private void UpdatePlayerMovement()
    {
        if (moveAction == null) return;

        Vector2 move = moveAction.action.ReadValue<Vector2>(); // x: strafe, y: forward
        float vertical = verticalAction != null ? verticalAction.action.ReadValue<float>() : 0f;

        // Movement is expressed in camera space, but we do NOT move the camera in Unity world.
        // We only move the player in REAL space (AU).
        Camera cam = GetActiveCamera();
        if (cam == null) return;

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        Vector3 camUp = cam.transform.up;

        // Full 3D movement based on camera orientation
        Vector3 moveDir =
            camRight * move.x +        // strafe left/right
            camForward * move.y +      // move forward/backward in camera direction
            camUp * vertical;          // move up/down relative to camera

        if (moveDir.sqrMagnitude < 1e-6f)
        {
            actualSpeed = 0f; // Standing still
            return;
        }

        moveDir.Normalize();

        // Check if nearest planet is at maximum size and prevent movement towards it
        if (nearestPlanet != null)
        {
            // Calculate if nearest planet would be at maximum size
            Vector3d offsetToNearestAu = playerRealPosAu.OffsetTo(nearestPlanet.realPosAu, SECTOR_SIZE_AU);
            double distAu = offsetToNearestAu.magnitude;
            double distKm = distAu * AU_KM;
            double angularRadius = Math.Atan(nearestPlanet.radiusKm / distKm);
            double proxyRadius = Math.Tan(angularRadius) * horizonRadius;
            bool planetAtMaxSize = proxyRadius >= maxProxyRadius;
            
            if (planetAtMaxSize)
            {
                // Check if movement is towards the planet
                Vector3 toPlanet = (Vector3)offsetToNearestAu.normalized;
                float movementDot = Vector3.Dot(moveDir, toPlanet);
                
                if (movementDot > 0.1f) // Moving towards planet
                {
                    // Block movement towards planet by projecting movement perpendicular to planet direction
                    moveDir = Vector3.ProjectOnPlane(moveDir, toPlanet).normalized;
                    
                    // If the resulting movement is too small, don't move at all
                    if (moveDir.sqrMagnitude < 0.1f)
                    {
                        actualSpeed = 0f;
                        return;
                    }
                }
            }
        }


        Vector3d moveDirDouble = new Vector3d(moveDir.x, moveDir.y, moveDir.z);
        // Calculate tentative position
        HierarchicalPosition tentativePos = playerRealPosAu.Add(moveDirDouble * (currentSpeed * Time.deltaTime), SECTOR_SIZE_AU);
        
        // Check for maximum distance from Sun (300,000 lightyears)
        // Refined approach: Math clamp on the final position vector
        HierarchicalPosition sunPos = GetSunPosition();
        Vector3d sunToTentative = sunPos.OffsetTo(tentativePos, SECTOR_SIZE_AU);
        double currentDistAu = sunToTentative.magnitude;
        double maxDistAu = (maxDistanceFromSunLy * LIGHTYEAR_KM) / AU_KM;
        
        if (currentDistAu > maxDistAu)
        {
             // Clamp position to the shell of the sphere
             Vector3d clampedOffset = sunToTentative * (maxDistAu / currentDistAu);
             tentativePos = sunPos.Add(clampedOffset, SECTOR_SIZE_AU);
        }
        
        playerRealPosAu = tentativePos;

        
        // Track actual movement speed
        actualSpeed = currentSpeed;
    }

    private void UpdateBodyProxies()
    {
        Camera cam = GetActiveCamera();
        Canvas labelCanvasRef = uiManager != null ? uiManager.LabelCanvas : null;
        RectTransform canvasRect = labelCanvasRef != null ? labelCanvasRef.GetComponent<RectTransform>() : null;
        bool isVRMode = uiManager != null && uiManager.IsVRMode;
        float labelOffsetPx = uiManager != null ? uiManager.LabelOffsetPixels : 20f;
        bool labelsEnabled = uiManager != null && uiManager.EnableLabels;
        
        // Collect label visibility data for UI manager
        List<SolarSystemUIManager.LabelVisibilityData> visibleLabels = 
            new List<SolarSystemUIManager.LabelVisibilityData>();
        
        foreach (var body in bodies)
        {
            // Calculate offset using hierarchical coordinates
            Vector3d offsetAu = playerRealPosAu.OffsetTo(body.realPosAu, SECTOR_SIZE_AU);
            double distAu = offsetAu.magnitude;

            if (distAu < 1e-6)
            {
                body.proxy.gameObject.SetActive(false);
                if (body.labelUI != null)
                    body.labelUI.SetActive(false);
                continue;
            }

            body.proxy.gameObject.SetActive(true);

            Vector3d dir = offsetAu / distAu;

            // Position on horizon sphere
            Vector3 proxyPos = (Vector3)(dir * horizonRadius);
            body.proxy.position = proxyPos;

            // Apparent angular radius (radians)
            double distKm = distAu * AU_KM;
            double angularRadius = Math.Atan(body.radiusKm / distKm);

            // Proxy radius at distance horizonRadius
            double proxyRadius = Math.Tan(angularRadius) * horizonRadius;

            float r = (float)proxyRadius;

            // Clamp to keep things sane in Unity
            r = Mathf.Clamp(r, minProxyRadius, maxProxyRadius);

            float diameter = r * 2f;
            body.proxy.localScale = new Vector3(diameter, diameter, diameter);

            // --- UI label position calculation ---
            if (labelsEnabled && body.labelUI != null && cam != null && canvasRect != null)
            {
                // Convert world position to viewport position (0-1 range, works better for VR)
                Vector3 viewportPos = cam.WorldToViewportPoint(body.proxy.position);
                
                // Check if object is in front of camera and within view (using viewport 0-1 range)
                bool isVisible = viewportPos.z > 0 && 
                               viewportPos.x >= 0 && viewportPos.x <= 1 &&
                               viewportPos.y >= 0 && viewportPos.y <= 1;
                
                if (isVisible)
                {
                    // Convert viewport to screen position for the canvas calculation
                    Vector3 screenPos = new Vector3(
                        viewportPos.x * Screen.width,
                        viewportPos.y * Screen.height,
                        viewportPos.z);
                    
                    // For Screen Space Overlay (desktop), use null camera
                    // For World Space (VR), use the active camera
                    Camera canvasCam = isVRMode ? labelCanvasRef.worldCamera : null;
                    
                    Vector2 canvasPos;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, screenPos, canvasCam, out canvasPos);
                    
                    // Add offset to position label to the right of the body
                    canvasPos.x += labelOffsetPx;
                    
                    // Calculate label bounds (approximate based on text size)
                    float labelWidth = 100f;
                    float labelHeight = 25f;
                    Rect bounds = new Rect(canvasPos.x - labelWidth / 2, canvasPos.y - labelHeight / 2, labelWidth, labelHeight);
                    
                    // Add to visible labels for UI manager to process
                    visibleLabels.Add(new SolarSystemUIManager.LabelVisibilityData
                    {
                        labelUI = body.labelUI,
                        screenPos = canvasPos,
                        distanceAu = (float)distAu,
                        labelBounds = bounds,
                        isPriority = IsProximaSystem(body)
                    });
                }
                else
                {
                    body.labelUI.SetActive(false);
                }
            }
        }
        
        // Delegate label collision detection and positioning to UI manager
        if (uiManager != null)
        {
            uiManager.UpdateLabelPositions(visibleLabels);
        }
    }
    
    // --- Autopilot System ---
    
    /// <summary>
    /// Creates the autopilot menu via UIManager and subscribes to events.
    /// </summary>
    private void CreateAutopilotMenuViaUIManager()
    {
        if (uiManager == null)
        {
            Debug.LogWarning("Cannot create Autopilot Menu: UIManager not available.");
            return;
        }
        
        // Convert bodies to AutopilotBodyInfo list
        var bodyInfoList = new List<SolarSystemUIManager.AutopilotBodyInfo>();
        
        foreach (var body in bodies)
        {
            var info = new SolarSystemUIManager.AutopilotBodyInfo
            {
                name = body.name,
                naifId = body.naifId,
                isMoon = IsMoon(body.naifId),
                isProximaSystem = IsProximaSystem(body),
                bodyReference = body
            };
            bodyInfoList.Add(info);
        }
        
        // Create the menu via UIManager
        uiManager.CreateAutopilotMenu(bodyInfoList);
        
        // Subscribe to events
        uiManager.OnAutopilotTargetSelected += OnAutopilotTargetSelected;
        uiManager.OnAutopilotCancelled += OnAutopilotCancelled;
        
        Debug.Log("Autopilot menu created via UIManager");
    }
    
    /// <summary>
    /// Called when a target is selected in the autopilot menu.
    /// </summary>
    private void OnAutopilotTargetSelected(SolarSystemUIManager.AutopilotBodyInfo bodyInfo)
    {
        // Find the corresponding BodyInstance
        BodyInstance target = bodyInfo.bodyReference as BodyInstance;
        
        if (target != null)
        {
            autopilotTarget = target;
            autopilotActive = true;
            IsAutopilotActive = true;
            Debug.Log($"Autopilot: Traveling to {target.name}");
        }
    }
    
    /// <summary>
    /// Called when the autopilot menu is cancelled.
    /// </summary>
    private void OnAutopilotCancelled()
    {
        // Nothing special needed here, menu is already closed by UIManager
    }
    
    /// <summary>
    /// Handles the autopilot toggle button press.
    /// </summary>
    private void HandleAutopilotToggle()
    {
        if (autopilotActive)
        {
            // If autopilot is active, pressing X cancels it
            StopAutopilot();
            return;
        }
        
        // Otherwise toggle the menu via UIManager
        if (uiManager != null)
        {
            uiManager.ToggleAutopilotMenu();
        }
    }
    
    // NOTE: UpdateVRMenuNavigationInput() has been moved to SolarSystemUIManager.HandleVRMenuNavigation()
    
    private bool IsMoon(long naifId)
    {
        // Moons have NAIF IDs: 3xx (Earth), 4xx (Mars), 5xx (Jupiter), 6xx (Saturn), 7xx (Uranus), 8xx (Neptune), 9xx (Pluto)
        // But not the parent planets: 399 (Earth), 499 (Mars), 599 (Jupiter), 699 (Saturn), 799 (Uranus), 899 (Neptune), 999 (Pluto)
        if (naifId >= 301 && naifId <= 399 && naifId != 399) return true; // Earth's moons
        if (naifId >= 401 && naifId <= 499 && naifId != 499) return true; // Mars' moons
        if (naifId >= 501 && naifId <= 599 && naifId != 599) return true; // Jupiter's moons
        if (naifId >= 601 && naifId <= 699 && naifId != 699) return true; // Saturn's moons
        if (naifId >= 701 && naifId <= 799 && naifId != 799) return true; // Uranus' moons
        if (naifId >= 801 && naifId <= 899 && naifId != 899) return true; // Neptune's moons
        if (naifId >= 901 && naifId <= 999 && naifId != 999) return true; // Pluto's moons
        return false;
    }
    
    private bool IsProximaSystem(long naifId)
    {
        // Check if body is part of the Proxima Centauri / Alpha Centauri system
        // These have very large NAIF IDs (Gaia source IDs)
        long id = naifId;
        return id == 4472832130942575872L || // Alpha Centauri A
               id == 4472832130942575873L || // Alpha Centauri B
               id == 4472832130942575874L;   // Proxima Centauri
    }
    
    private bool IsProximaSystem(BodyInstance body)
    {
        if (body == null) return false;
        return IsProximaSystem(body.naifId) || 
               body.name.Contains("Proxima") || 
               body.name.Contains("Alpha Centauri");
    }
    
    private void UpdateAutopilot()
    {
        if (!autopilotActive || autopilotTarget == null) return;
        
        // Calculate direction and distance to target
        Vector3d toTarget = playerRealPosAu.OffsetTo(autopilotTarget.realPosAu, SECTOR_SIZE_AU);
        double distanceAu = toTarget.magnitude;
        
        // Convert target radius to AU for stopping distance
        double targetRadiusAu = autopilotTarget.radiusKm / AU_KM;
        double stopDistanceAu = targetRadiusAu * 10.0; // Stop at 10x planet radius
        
        // Check if we've arrived (with small tolerance for floating-point precision)
        double arrivalTolerance = 1e-4; // Tolerance in AU (~15,000 km) - generous to ensure reliable exit
        double remainingDistance = distanceAu - stopDistanceAu;
        if (distanceAu <= stopDistanceAu || remainingDistance < arrivalTolerance)
        {
            Debug.Log($"Autopilot: Arrived at {autopilotTarget.name}");
            StopAutopilot();
            return;
        }
        
        // Smooth camera rotation towards target
        Camera cam = GetActiveCamera();
        if (cam != null && autopilotTarget.proxy != null)
        {
            // Look at the target's proxy position (where it appears on the horizon sphere)
            Vector3 targetDirection = autopilotTarget.proxy.position - cam.transform.position;
            
            if (targetDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                float rotationSpeed = 2f; // Smooth rotation speed
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            }
        }
        
        // Calculate travel speed (use the dynamic currentSpeed from UpdateDynamicBehavior)
        Vector3d moveDir = toTarget.normalized;
        
        // Move towards target
        double moveAmount = currentSpeed * Time.deltaTime * 1.5; // 1.5x normal speed for autopilot
        
        // Don't overshoot
        if (moveAmount > distanceAu - stopDistanceAu)
        {
            moveAmount = distanceAu - stopDistanceAu;
        }
        
        playerRealPosAu = playerRealPosAu.Add(moveDir * moveAmount, SECTOR_SIZE_AU);
        actualSpeed = currentSpeed * 1.5f;
    }
    
    private void StopAutopilot()
    {
        autopilotActive = false;
        IsAutopilotActive = false; // Static property for other scripts
        autopilotTarget = null;
        actualSpeed = 0f;
        Debug.Log("Autopilot: Stopped");
    }
    
    // --- Planet Info System ---
    
    private void LoadPlanetInfoData()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "PlanetDataset/planets_updated.csv");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Planet info CSV not found at {path}. Planet info feature disabled.");
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= 1)
            {
                Debug.LogWarning("Planet info CSV seems empty or header-only.");
                return;
            }
            
            // Parse header to get column indices
            string[] headers = ParseCsvLine(lines[0]);
            Dictionary<string, int> columnIndex = new Dictionary<string, int>();
            for (int i = 0; i < headers.Length; i++)
            {
                columnIndex[headers[i].Trim()] = i;
            }
            
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                string[] parts = ParseCsvLine(line);
                if (parts.Length < 2) continue;
                
                string planetName = parts[0].Trim();
                
                PlanetData data = new PlanetData
                {
                    Color = GetCsvValue(parts, columnIndex, "Color"),
                    Mass = GetCsvValue(parts, columnIndex, "Mass (10^24kg)"),
                    Diameter = GetCsvValue(parts, columnIndex, "Diameter (km)"),
                    Density = GetCsvValue(parts, columnIndex, "Density (kg/m^3)"),
                    Gravity = GetCsvValue(parts, columnIndex, "Surface Gravity(m/s^2)"),
                    LengthOfDay = GetCsvValue(parts, columnIndex, "Length of Day (hours)"),
                    DistanceFromSun = GetCsvValue(parts, columnIndex, "Distance from Sun (10^6 km)"),
                    MeanTemperature = GetCsvValue(parts, columnIndex, "Mean Temperature (C)"),
                    NumberOfMoons = GetCsvValue(parts, columnIndex, "Number of Moons"),
                    RingSystem = GetCsvValue(parts, columnIndex, "Ring System?"),
                    AtmosphericComposition = GetCsvValue(parts, columnIndex, "Atmospheric Composition"),
                    SurfaceFeatures = GetCsvValue(parts, columnIndex, "Surface Features"),
                    Composition = GetCsvValue(parts, columnIndex, "Composition")
                };
                
                planetInfoData[planetName.ToLower()] = data;
                Debug.Log($"Loaded planet info for: {planetName}");
            }
            
            // Link planet data to bodies
            foreach (var body in bodies)
            {
                string lookupName = body.name.ToLower();
                if (planetInfoData.TryGetValue(lookupName, out PlanetData data))
                {
                    body.planetData = data;
                    Debug.Log($"Linked planet data for {body.name}");
                }
            }
            
            Debug.Log($"Loaded {planetInfoData.Count} planet info entries.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading planet info CSV: {e.Message}");
        }
    }
    
    private string[] ParseCsvLine(string line)
    {
        // Handle CSV with quoted fields containing commas
        List<string> result = new List<string>();
        bool inQuotes = false;
        string current = "";
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else
            {
                current += c;
            }
        }
        result.Add(current);
        
        return result.ToArray();
    }
    
    private string GetCsvValue(string[] parts, Dictionary<string, int> columnIndex, string columnName)
    {
        if (columnIndex.TryGetValue(columnName, out int index) && index < parts.Length)
        {
            return parts[index].Trim();
        }
        return "Unknown";
    }
    
    // NOTE: CreatePlanetInfoPanel() has been moved to SolarSystemUIManager
    
    private void TogglePlanetInfo()
    {
        if (uiManager == null) return;
        
        // Mutual exclusion: Close Autopilot Menu if open
        if (uiManager.AutopilotMenuOpen)
        {
            uiManager.HideAutopilotMenu();
        }

        bool wasVisible = uiManager.IsPlanetInfoVisible;
        
        if (!wasVisible)
        {
            // Find the nearest body within range that has info data
            BodyInstance infoTarget = FindNearestBodyWithinRange();
            
            if (infoTarget != null)
            {
                // Convert to UI manager's data format
                var infoData = ConvertToPlanetInfoData(infoTarget);
                uiManager.ShowPlanetInfo(infoData);
                Debug.Log($"Showing planet info for: {infoTarget.name}");
            }
            else
            {
                Debug.Log("No body with planet info data found within range");
            }
        }
        else
        {
            uiManager.HidePlanetInfo();
        }
    }
    
    /// <summary>
    /// Converts a BodyInstance to PlanetInfoData for the UI manager.
    /// </summary>
    private SolarSystemUIManager.PlanetInfoData ConvertToPlanetInfoData(BodyInstance body)
    {
        var infoData = new SolarSystemUIManager.PlanetInfoData
        {
            Name = body.name,
            RadiusKm = body.radiusKm
        };
        
        if (body.planetData != null)
        {
            infoData.Color = body.planetData.Color;
            infoData.Mass = body.planetData.Mass;
            infoData.Diameter = body.planetData.Diameter;
            infoData.Density = body.planetData.Density;
            infoData.Gravity = body.planetData.Gravity;
            infoData.LengthOfDay = body.planetData.LengthOfDay;
            infoData.DistanceFromSun = body.planetData.DistanceFromSun;
            infoData.MeanTemperature = body.planetData.MeanTemperature;
            infoData.NumberOfMoons = body.planetData.NumberOfMoons;
            infoData.RingSystem = body.planetData.RingSystem;
            infoData.AtmosphericComposition = body.planetData.AtmosphericComposition;
            infoData.SurfaceFeatures = body.planetData.SurfaceFeatures;
            infoData.Composition = body.planetData.Composition;
        }
        
        return infoData;
    }
    
    /// <summary>
    /// Finds the nearest body that has planet info data and is within range.
    /// Range is based on body radius (500x radius).
    /// Includes Sun and Moon as special cases even without CSV data.
    /// </summary>
    private BodyInstance FindNearestBodyWithinRange()
    {
        // Max distance multiplier - body must be within this many radii to show info
        const float MAX_DISTANCE_RADII = 500f;
        
        BodyInstance nearest = null;
        float nearestDist = float.MaxValue;
        
        foreach (var body in bodies)
        {
            // Check if this body has planet info data, or is Sun/Earth's Moon/Black Hole (special cases)
            bool hasInfo = body.planetData != null || body.naifId == 301 || body.naifId == 10 || body.objectType == "black_hole";
            if (!hasInfo) continue;
            
            // Calculate distance in AU
            Vector3d offset = playerRealPosAu.OffsetTo(body.realPosAu, SECTOR_SIZE_AU);
            float distanceAu = (float)offset.magnitude;
            
            // Calculate max allowed distance based on body radius
            float bodyRadiusAu = body.radiusKm / (float)AU_KM;
            float maxDistanceAu = bodyRadiusAu * MAX_DISTANCE_RADII;
            
            // Skip if too far from this body
            if (distanceAu > maxDistanceAu) continue;
            
            // Find the nearest body among those in range
            if (distanceAu < nearestDist)
            {
                nearestDist = distanceAu;
                nearest = body;
            }
        }
        
        return nearest;
    }
    
    // NOTE: Planet info panel creation, population, and animation are now handled by SolarSystemUIManager.
    // The following methods delegate to the UI manager:
    
    private void UpdatePlanetInfoPanel()
    {
        if (uiManager != null)
        {
            uiManager.UpdatePlanetInfoPanel();
        }
    }
    
    // ========================
    // ASTEROID RENDERING
    // ========================
    
    private void InitializeAsteroidMesh()
    {
        asteroidMesh = new Mesh();
        asteroidMesh.name = "AsteroidPointMesh";
        
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
        
        asteroidMesh.vertices = vertices;
        asteroidMesh.uv = uv;
        asteroidMesh.triangles = triangles;
        asteroidMesh.RecalculateNormals();
        asteroidMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
    }
    
    private IEnumerator LoadAsteroidDataAsync()
    {
        Debug.Log("Loading asteroid data from binary file...");
        
        allAsteroids.Clear();
        
        string filePath = Path.Combine(Application.streamingAssetsPath, "AsteroidDataset", "asteroids_orbital.bin");
        
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"Asteroid data file not found: {filePath}");
            yield break;
        }
        
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // Read header: number of asteroids (uint32)
            uint asteroidCount = reader.ReadUInt32();
            Debug.Log($"Loading {asteroidCount} asteroids from binary file...");
            
            allAsteroids.Capacity = (int)asteroidCount;
            
            int batchSize = 10000;
            int processed = 0;
            
            for (uint i = 0; i < asteroidCount; i++)
            {
                // Read binary record: RA, DEC, Distance_AU, H (all float32)
                float ra_deg = reader.ReadSingle();
                float dec_deg = reader.ReadSingle();
                float distance_au = reader.ReadSingle();
                float magnitude = reader.ReadSingle();
                
                // Skip invalid data
                if (distance_au <= 0 || float.IsNaN(distance_au) || float.IsInfinity(distance_au))
                    continue;
                
                // Convert RA/DEC to Cartesian coordinates in AU using double precision
                double ra_rad = ra_deg * Math.PI / 180.0;
                double dec_rad = dec_deg * Math.PI / 180.0;
                
                double cos_dec = Math.Cos(dec_rad);
                double x = distance_au * cos_dec * Math.Cos(ra_rad);
                double y = distance_au * Math.Sin(dec_rad);
                double z = distance_au * cos_dec * Math.Sin(ra_rad);
                
                Vector3d absolutePos = new Vector3d(x, y, z);
                HierarchicalPosition hierarchicalPos = new HierarchicalPosition(absolutePos, SECTOR_SIZE_AU);
                
                AsteroidData asteroid = new AsteroidData
                {
                    positionAU = hierarchicalPos,
                    distance = distance_au,
                    magnitude = magnitude,
                    originalIndex = (int)i
                };
                
                allAsteroids.Add(asteroid);
                processed++;
                
                // Yield periodically for smooth loading
                if (processed % batchSize == 0)
                    yield return null;
            }
        }
        
        Debug.Log($"Total asteroids loaded: {allAsteroids.Count}");
        asteroidsLoaded = true;
        
        // Initial update
        UpdateVisibleAsteroids();
    }
    
    private void UpdateVisibleAsteroids()
    {
        if (!asteroidsLoaded) return;
        
        visibleAsteroids.Clear();
        
        Camera cam = GetActiveCamera();
        if (cam == null) return;
        
        Vector3 cameraPos = cam.transform.position;
        Vector3 cameraForward = cam.transform.forward;
        
        // Calculate effective FOV with generous margin
        float halfFOVWithMargin = cam.fieldOfView * 0.5f + 45f; // 45 degree margin
        
        foreach (AsteroidData asteroid in allAsteroids)
        {
            // Calculate asteroid position on virtual horizon with parallax
            Vector3 asteroidWorldPos = CalculateAsteroidWorldPosition(asteroid);
            
            // FOV culling
            Vector3 toAsteroid = (asteroidWorldPos - cameraPos).normalized;
            float dotProduct = Vector3.Dot(cameraForward, toAsteroid);
            float angleToCamera = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;
            
            if (angleToCamera > halfFOVWithMargin)
                continue;
            
            visibleAsteroids.Add(asteroid);
            
            // Limit asteroids per frame for performance
            if (visibleAsteroids.Count >= maxAsteroidsPerFrame)
                break;
        }
    }
    
    private Vector3 CalculateAsteroidWorldPosition(AsteroidData asteroid)
    {
        // Calculate position relative to player using hierarchical coordinates
        Vector3d relativePos = playerRealPosAu.OffsetTo(asteroid.positionAU, SECTOR_SIZE_AU);
        
        // Get direction to asteroid from player
        Vector3d direction = relativePos.normalized;
        
        // Project onto virtual horizon sphere
        return (Vector3)(direction * horizonRadius);
    }
    
    private void UpdateAsteroidRendering()
    {
        int asteroidCount = visibleAsteroids.Count;
        
        // Initialize or resize arrays if needed
        if (asteroidPositions == null || asteroidPositions.Length != asteroidCount)
        {
            asteroidPositions = new Vector3[asteroidCount];
            asteroidMatrices = new Matrix4x4[asteroidCount];
        }
        
        // Update positions and matrices
        for (int i = 0; i < asteroidCount; i++)
        {
            AsteroidData asteroid = visibleAsteroids[i];
            Vector3 worldPos = CalculateAsteroidWorldPosition(asteroid);
            
            asteroidPositions[i] = worldPos;
            asteroidMatrices[i] = Matrix4x4.TRS(worldPos, Quaternion.identity, Vector3.one);
        }
    }
    
    private void RenderAsteroids()
    {
        if (asteroidMatrices == null || asteroidMatrices.Length == 0 || asteroidMaterial == null)
            return;
        
        // Ensure asteroidPropertyBlock is initialized
        if (asteroidPropertyBlock == null)
            asteroidPropertyBlock = new MaterialPropertyBlock();
        
        // Set material properties for asteroids
        asteroidPropertyBlock.SetColor("_Color", asteroidColor);
        asteroidPropertyBlock.SetFloat("_Size", baseAsteroidSize);
        asteroidPropertyBlock.SetFloat("_Brightness", asteroidBrightness);
        
        // Render asteroids in batches (Unity has limits on instanced rendering)
        const int BATCH_SIZE = 1023; // Unity's limit for Graphics.DrawMeshInstanced
        
        for (int startIndex = 0; startIndex < asteroidMatrices.Length; startIndex += BATCH_SIZE)
        {
            int count = Mathf.Min(BATCH_SIZE, asteroidMatrices.Length - startIndex);
            Matrix4x4[] batch = new Matrix4x4[count];
            
            Array.Copy(asteroidMatrices, startIndex, batch, 0, count);
            
            Graphics.DrawMeshInstanced(
                asteroidMesh,
                0,
                asteroidMaterial,
                batch,
                count,
                asteroidPropertyBlock
            );
        }
    }
}

// Double precision 3D vector for accurate calculations
[System.Serializable]
public struct Vector3d
{
    public double x, y, z;
    
    public Vector3d(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    
    public Vector3d(Vector3 v)
    {
        this.x = v.x;
        this.y = v.y;
        this.z = v.z;
    }
    
    public double magnitude => System.Math.Sqrt(x * x + y * y + z * z);
    
    public double sqrMagnitude => x * x + y * y + z * z;
    
    public Vector3d normalized
    {
        get
        {
            double mag = magnitude;
            if (mag < 1e-15) return new Vector3d(1, 0, 0);
            return new Vector3d(x / mag, y / mag, z / mag);
        }
    }
    
    public static Vector3d zero => new Vector3d(0, 0, 0);
    
    public static Vector3d operator +(Vector3d a, Vector3d b)
    {
        return new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
    }
    
    public static Vector3d operator -(Vector3d a, Vector3d b)
    {
        return new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    
    public static Vector3d operator *(Vector3d a, double scalar)
    {
        return new Vector3d(a.x * scalar, a.y * scalar, a.z * scalar);
    }
    
    public static Vector3d operator *(double scalar, Vector3d a)
    {
        return new Vector3d(a.x * scalar, a.y * scalar, a.z * scalar);
    }
    
    public static Vector3d operator /(Vector3d a, double scalar)
    {
        return new Vector3d(a.x / scalar, a.y / scalar, a.z / scalar);
    }
    
    public static Vector3d operator -(Vector3d a)
    {
        return new Vector3d(-a.x, -a.y, -a.z);
    }
    
    public static explicit operator Vector3(Vector3d v)
    {
        return new Vector3((float)v.x, (float)v.y, (float)v.z);
    }
    
    public override string ToString()
    {
        return $"({x:F6}, {y:F6}, {z:F6})";
    }
}

// Hierarchical position system for unlimited scale with high precision
[System.Serializable]
public struct HierarchicalPosition
{
    public Vector3Int sector;      // Which sector (each sector is SECTOR_SIZE_AU)
    public Vector3d localOffset;   // Precise position within sector (in AU)
    
    public HierarchicalPosition(Vector3Int sector, Vector3d localOffset)
    {
        this.sector = sector;
        this.localOffset = localOffset;
    }
    
    // Create from absolute AU position
    public HierarchicalPosition(Vector3d absolutePositionAu, double sectorSizeAu)
    {
        int sectorX = (int)System.Math.Floor(absolutePositionAu.x / sectorSizeAu);
        int sectorY = (int)System.Math.Floor(absolutePositionAu.y / sectorSizeAu);
        int sectorZ = (int)System.Math.Floor(absolutePositionAu.z / sectorSizeAu);
        
        sector = new Vector3Int(sectorX, sectorY, sectorZ);
        localOffset = new Vector3d(
            absolutePositionAu.x - sectorX * sectorSizeAu,
            absolutePositionAu.y - sectorY * sectorSizeAu,
            absolutePositionAu.z - sectorZ * sectorSizeAu
        );
    }
    
    // Convert from Vector3 (for legacy compatibility)
    public HierarchicalPosition(Vector3 positionAu, double sectorSizeAu)
    {
        Vector3d pos = new Vector3d(positionAu);
        int sectorX = (int)System.Math.Floor(pos.x / sectorSizeAu);
        int sectorY = (int)System.Math.Floor(pos.y / sectorSizeAu);
        int sectorZ = (int)System.Math.Floor(pos.z / sectorSizeAu);
        
        sector = new Vector3Int(sectorX, sectorY, sectorZ);
        localOffset = new Vector3d(
            pos.x - sectorX * sectorSizeAu,
            pos.y - sectorY * sectorSizeAu,
            pos.z - sectorZ * sectorSizeAu
        );
    }
    
    // Convert to absolute AU position (for debugging/display)
    public Vector3d ToAbsolutePosition(double sectorSizeAu)
    {
        return new Vector3d(
            sector.x * sectorSizeAu + localOffset.x,
            sector.y * sectorSizeAu + localOffset.y,
            sector.z * sectorSizeAu + localOffset.z
        );
    }
    
    // Calculate offset from this position to another (player-relative coordinates)
    public Vector3d OffsetTo(HierarchicalPosition other, double sectorSizeAu)
    {
        // Calculate sector difference
        Vector3Int sectorDiff = other.sector - this.sector;
        
        // Convert to AU offset
        Vector3d sectorOffsetAu = new Vector3d(
            sectorDiff.x * sectorSizeAu,
            sectorDiff.y * sectorSizeAu,
            sectorDiff.z * sectorSizeAu
        );
        
        // Add local offset difference
        return sectorOffsetAu + (other.localOffset - this.localOffset);
    }
    
    // Add a Vector3d offset (for movement)
    public HierarchicalPosition Add(Vector3d offsetAu, double sectorSizeAu)
    {
        Vector3d newLocal = localOffset + offsetAu;
        Vector3Int newSector = sector;
        
        // Handle sector overflow in X
        while (newLocal.x >= sectorSizeAu)
        {
            newLocal.x -= sectorSizeAu;
            newSector.x++;
        }
        while (newLocal.x < 0)
        {
            newLocal.x += sectorSizeAu;
            newSector.x--;
        }
        
        // Handle sector overflow in Y
        while (newLocal.y >= sectorSizeAu)
        {
            newLocal.y -= sectorSizeAu;
            newSector.y++;
        }
        while (newLocal.y < 0)
        {
            newLocal.y += sectorSizeAu;
            newSector.y--;
        }
        
        // Handle sector overflow in Z
        while (newLocal.z >= sectorSizeAu)
        {
            newLocal.z -= sectorSizeAu;
            newSector.z++;
        }
        while (newLocal.z < 0)
        {
            newLocal.z += sectorSizeAu;
            newSector.z--;
        }
        
        return new HierarchicalPosition(newSector, newLocal);
    }
    
    // Subtract (shift) all positions by an offset (for origin shifting)
    public HierarchicalPosition Subtract(Vector3d offsetAu, double sectorSizeAu)
    {
        return Add(-offsetAu, sectorSizeAu);
    }
    
    public static HierarchicalPosition zero => new HierarchicalPosition(Vector3Int.zero, Vector3d.zero);
    
    public override string ToString()
    {
        return $"Sector{sector} + {localOffset}";
    }
}
