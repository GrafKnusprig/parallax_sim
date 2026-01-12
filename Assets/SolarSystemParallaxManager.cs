using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class SolarSystemParallaxManager : MonoBehaviour
{
    [Header("CSV")]
    [Tooltip("File name inside StreamingAssets/PlanetDatasetPlus")]
    [SerializeField] private string csvFileName = "solar_dataset_plus.csv";
    
    [Tooltip("Planet materials CSV file inside StreamingAssets")]
    [SerializeField] private string planetMaterialsCsvFileName = "planet_materials.csv";

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

    [Header("Player (real space)")]
    
    [Header("Camera")]
    [Tooltip("The camera to use for rendering and movement calculations. If not set, will use Camera.main.")]
    [SerializeField] private Camera targetCamera;
    
    [Tooltip("New Input System: 2D move (x: strafe, y: forward).")]
    [SerializeField] private InputActionReference moveAction;

    [Tooltip("New Input System: vertical move (float axis).")]
    [SerializeField] private InputActionReference verticalAction;

    [Header("Labels (optional)")]
    [SerializeField] private bool enableLabels = true;
    [SerializeField] private Canvas labelCanvas;
    [SerializeField] private TMP_FontAsset labelFont;
    [Tooltip("Font size for planet labels (pt)")]
    [SerializeField] private int labelFontSize = 24;
    [SerializeField] private Color labelColor = Color.white;
    [Tooltip("Offset from planet center (pixels)")]
    [SerializeField] private float labelOffsetPixels = 20f; // offset from planet center in pixels
    
    [Header("HUD (Heads-Up Display)")]
    [SerializeField] private bool enableHUD = true;
    [Tooltip("Font size for HUD text (pt)")]
    [SerializeField] private int hudFontSize = 28;
    [SerializeField] private Color hudColor = Color.cyan;
    [SerializeField] private Vector2 hudPosition = new Vector2(300f, -100f); // offset from top-left corner
    
    [Header("VR Mode")]
    [Tooltip("Enable VR mode for headset display. When disabled, uses standard screen with mouse support.")]
    [SerializeField] private bool enableVRMode = false;
    
    [Header("Loading Screen")]
    [Tooltip("Show loading screen while stellar data is being loaded")]
    [SerializeField] private bool enableLoadingScreen = true;

    private const double AU_KM = 149_597_870.7;
    private const double SPEED_OF_LIGHT_KM_S = 299_792_458.0; // km/s (exact value)
    private const double LIGHTYEAR_KM = 9_460_730_472_580.8; // km in 1 lightyear
    private const float PARSEC_TO_AU = 206264.806f;  // 1 parsec = 206,264.806 AU

    [System.NonSerialized]
    public Vector3 playerRealPosAu; // player position in AU (real space) - public for StellarParallaxManager

    private GameObject horizonSphere;

    private readonly List<BodyInstance> bodies = new List<BodyInstance>();
    
    // Dynamic scaling and speed
    private BodyInstance nearestPlanet;
    private float currentScale;
    private float currentSpeed;
    private float actualSpeed; // Actual movement speed (0 when standing still)
    private float distanceToNearestPlanet;
    
    // Planet-specific materials
    private Dictionary<int, Material> planetMaterials = new Dictionary<int, Material>();
    
    // HUD elements
    private GameObject hudUI;
    private TextMeshProUGUI hudText;
    
    // Stellar parallax integration
    private StellarParallaxManager stellarManager;
    
    // Autopilot system
    private bool autopilotMenuOpen = false;
    private bool autopilotActive = false;
    private BodyInstance autopilotTarget = null;
    private GameObject autopilotUI;
    private List<Button> autopilotButtons = new List<Button>();
    
    // Static property for other scripts to check menu state
    public static bool IsMenuOpen { get; private set; } = false;
    public static bool IsAutopilotActive { get; private set; } = false;
    
    // Planet info system
    private Dictionary<string, PlanetData> planetInfoData = new Dictionary<string, PlanetData>();
    private bool planetInfoVisible = false;
    private GameObject planetInfoUI;
    private TextMeshProUGUI planetInfoNameText;
    private TextMeshProUGUI planetInfoDataText;
    private float planetInfoAnimProgress = 0f;
    private const float PLANET_INFO_ANIM_SPEED = 8f;
    
    // Loading screen
    private GameObject loadingScreenUI;
    private TextMeshProUGUI loadingText;
    private Image loadingBackground;
    private Image progressBarBackground;
    private Image progressBarFill;
    private TextMeshProUGUI progressText;
    private bool loadingComplete = false;
    private float loadingFadeProgress = 0f;
    private const float LOADING_FADE_SPEED = 2f;
    private const int ESTIMATED_TOTAL_STARS = 2400000; // Approximate total stars in GDR1 dataset
    
    // VR/Desktop adaptive mode
    private bool isVRMode = false;
    private bool wasVRMode = false; // Track if mode changed

    private class BodyInstance
    {
        public string name;
        public int naifId;
        public Vector3 realPosAu;
        public float radiusKm;
        public Transform proxy;

        // UI-based labels
        public GameObject labelUI;
        
        // Link to planet data
        public PlanetData planetData;
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

    // Radii for main bodies (km), keyed by NAIF ID used in your file
    private static readonly Dictionary<int, float> BodyRadiiKm = new Dictionary<int, float>
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
    }

    private void OnDisable()
    {
        if (moveAction != null) moveAction.action.Disable();
        if (verticalAction != null) verticalAction.action.Disable();
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
        
        CreateHorizonSphere();
        SetupLabelCanvas();
        CreateHUD();
        LoadPlanetMaterials();
        LoadBodiesFromCsv();
        if (bodies.Count == 0)
        {
            Debug.LogWarning("No bodies loaded from dataset");
        }
        
        LoadPlanetInfoData();
        CreateAutopilotMenu();
        CreatePlanetInfoPanel();
        CreateLoadingScreen();
        
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

    private void SetupLabelCanvas()
    {
        // Use manual VR mode setting from Inspector
        isVRMode = enableVRMode;
        wasVRMode = isVRMode;
        Debug.Log($"VR Mode enabled: {isVRMode}");
        
        if (enableLabels && labelCanvas == null)
        {
            // Create a Canvas for labels if one isn't assigned
            GameObject canvasGO = new GameObject("LabelCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            
            // Configure based on VR mode
            if (isVRMode)
            {
                ConfigureCanvasForVR(canvas);
            }
            else
            {
                ConfigureCanvasForDesktop(canvas);
            }
            
            canvasGO.AddComponent<GraphicRaycaster>();
            labelCanvas = canvas;
            Debug.Log($"Created automatic label canvas ({(isVRMode ? "World Space for VR" : "Screen Space for Desktop")})");
        }
        else if (labelCanvas != null)
        {
            // Configure existing canvas based on VR mode
            if (isVRMode)
            {
                ConfigureCanvasForVR(labelCanvas);
            }
            else
            {
                ConfigureCanvasForDesktop(labelCanvas);
            }
        }
        
        // Ensure EventSystem exists for UI interaction
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            GameObject eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("Created EventSystem for UI interaction");
        }
    }
    
    private void ConfigureCanvasForVR(Canvas canvas)
    {
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            canvasRect.sizeDelta = new Vector2(1920, 1080);
        }
        
        // Scale down for world space (2 meters wide approximately)
        canvas.transform.localScale = Vector3.one * 0.001f;
        
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;
        
        Debug.Log("Canvas configured for VR (World Space)");
    }
    
    private void ConfigureCanvasForDesktop(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        
        // Reset scale for screen space
        canvas.transform.localScale = Vector3.one;
        
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        Debug.Log("Canvas configured for Desktop (Screen Space Overlay)");
    }
    
    private void CreateHUD()
    {
        if (!enableHUD) return;
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("Cannot create HUD: Label Canvas not available. HUD requires a canvas.");
            return;
        }
        
        // Create HUD GameObject
        hudUI = new GameObject("HUD");
        hudUI.transform.SetParent(labelCanvas.transform, false);
        
        // Add RectTransform
        RectTransform rectTransform = hudUI.AddComponent<RectTransform>();
        
        // Position at top-left corner
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(0, 1);
        rectTransform.pivot = new Vector2(0, 1);
        rectTransform.anchoredPosition = hudPosition; // Use configurable position
        rectTransform.sizeDelta = new Vector2(800, 400); // 200% bigger than previous
        
        // Add Text component
        // Add Text component
        hudText = hudUI.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) hudText.font = labelFont;
        hudText.fontSize = hudFontSize;
        hudText.color = hudColor;
        hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.enableWordWrapping = false;
        hudText.overflowMode = TextOverflowModes.Overflow;
        
        // Initial text
        hudText.text = "Speed: 0 km/s (0% lightspeed)\nDistance from Sun: 0 km\nMode: DISTANCE-BASED";
        
        Debug.Log("HUD created successfully");
    }

    private void Update()
    {
        // Handle loading screen
        UpdateLoadingScreen();
        
        // Always update VR canvas position (needed for loading screen visibility in VR)
        UpdateVRCanvas();
        
        // Don't allow gameplay until loading is complete
        if (!loadingComplete)
        {
            return;
        }
        
        // Handle autopilot toggle with X key
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            ToggleAutopilotMenu();
        }
        
        // Handle planet info toggle with I key
        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
        {
            TogglePlanetInfo();
        }
        
        UpdateDynamicBehavior();
        
        // Use autopilot or manual movement
        if (autopilotActive)
        {
            UpdateAutopilot();
        }
        else if (!autopilotMenuOpen && !planetInfoVisible)
        {
            UpdatePlayerMovement();
        }
        
        UpdateBodyProxies();
        UpdatePlanetInfoPanel();
        
        // Notify stellar manager of position change
        if (stellarManager != null)
        {
            stellarManager.OnPlayerPositionChanged(playerRealPosAu);
        }
    }
    
    private void CreateLoadingScreen()
    {
        if (!enableLoadingScreen)
        {
            loadingComplete = true; // Mark as complete so gameplay isn't blocked
            return;
        }
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("Cannot create loading screen: Label Canvas not available.");
            loadingComplete = true;
            return;
        }
        
        // Create loading screen container
        loadingScreenUI = new GameObject("LoadingScreen");
        loadingScreenUI.transform.SetParent(labelCanvas.transform, false);
        
        // Set transform to cover full screen
        RectTransform rectTransform = loadingScreenUI.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        // Add dark background
        loadingBackground = loadingScreenUI.AddComponent<Image>();
        loadingBackground.color = new Color(0.02f, 0.02f, 0.05f, 1f); // Very dark blue-black
        
        // Create center container for loading content
        GameObject centerContainer = new GameObject("CenterContainer");
        centerContainer.transform.SetParent(loadingScreenUI.transform, false);
        RectTransform centerRect = centerContainer.AddComponent<RectTransform>();
        centerRect.anchorMin = new Vector2(0.5f, 0.5f);
        centerRect.anchorMax = new Vector2(0.5f, 0.5f);
        centerRect.pivot = new Vector2(0.5f, 0.5f);
        centerRect.anchoredPosition = Vector2.zero;
        centerRect.sizeDelta = new Vector2(600, 250);
        
        // Create loading text
        GameObject textGO = new GameObject("LoadingText");
        textGO.transform.SetParent(centerContainer.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0.6f);
        textRect.anchorMax = new Vector2(1, 1f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        loadingText = textGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) loadingText.font = labelFont;
        loadingText.fontSize = 32;
        loadingText.color = new Color(0.8f, 0.9f, 1f, 1f); // Light blue-white
        loadingText.alignment = TextAlignmentOptions.Center;
        loadingText.text = "Loading Stellar Data...";
        
        // Create progress bar container
        GameObject progressContainer = new GameObject("ProgressBarContainer");
        progressContainer.transform.SetParent(centerContainer.transform, false);
        RectTransform progressContainerRect = progressContainer.AddComponent<RectTransform>();
        progressContainerRect.anchorMin = new Vector2(0.1f, 0.35f);
        progressContainerRect.anchorMax = new Vector2(0.9f, 0.45f);
        progressContainerRect.offsetMin = Vector2.zero;
        progressContainerRect.offsetMax = Vector2.zero;
        
        // Progress bar background (dark)
        GameObject progressBgGO = new GameObject("ProgressBarBackground");
        progressBgGO.transform.SetParent(progressContainer.transform, false);
        RectTransform progressBgRect = progressBgGO.AddComponent<RectTransform>();
        progressBgRect.anchorMin = Vector2.zero;
        progressBgRect.anchorMax = Vector2.one;
        progressBgRect.offsetMin = Vector2.zero;
        progressBgRect.offsetMax = Vector2.zero;
        
        progressBarBackground = progressBgGO.AddComponent<Image>();
        progressBarBackground.color = new Color(0.1f, 0.12f, 0.18f, 1f); // Dark blue-gray
        
        // Progress bar fill (bright gradient-like effect)
        GameObject progressFillGO = new GameObject("ProgressBarFill");
        progressFillGO.transform.SetParent(progressContainer.transform, false);
        RectTransform progressFillRect = progressFillGO.AddComponent<RectTransform>();
        progressFillRect.anchorMin = Vector2.zero;
        progressFillRect.anchorMax = new Vector2(0f, 1f); // Start with 0 width
        progressFillRect.pivot = new Vector2(0f, 0.5f);
        progressFillRect.offsetMin = new Vector2(2, 2);
        progressFillRect.offsetMax = new Vector2(-2, -2);
        
        progressBarFill = progressFillGO.AddComponent<Image>();
        progressBarFill.color = new Color(0.3f, 0.6f, 1f, 1f); // Bright blue
        
        // Progress percentage text
        GameObject progressTextGO = new GameObject("ProgressText");
        progressTextGO.transform.SetParent(centerContainer.transform, false);
        RectTransform progressTextRect = progressTextGO.AddComponent<RectTransform>();
        progressTextRect.anchorMin = new Vector2(0, 0.15f);
        progressTextRect.anchorMax = new Vector2(1, 0.32f);
        progressTextRect.offsetMin = Vector2.zero;
        progressTextRect.offsetMax = Vector2.zero;
        
        progressText = progressTextGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) progressText.font = labelFont;
        progressText.fontSize = 16;
        progressText.color = new Color(0.6f, 0.7f, 0.8f, 0.9f);
        progressText.alignment = TextAlignmentOptions.Center;
        progressText.text = "0% - 0 / 2,400,000 stars";
        
        // Sub-text hint
        GameObject hintGO = new GameObject("LoadingHint");
        hintGO.transform.SetParent(centerContainer.transform, false);
        RectTransform hintRect = hintGO.AddComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0, 0f);
        hintRect.anchorMax = new Vector2(1, 0.15f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;
        
        TextMeshProUGUI hintText = hintGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) hintText.font = labelFont;
        hintText.fontSize = 14;
        hintText.color = new Color(0.4f, 0.5f, 0.6f, 0.7f);
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.text = "Processing Gaia GDR1 stellar catalog...";
        
        // Hide HUD during loading
        if (hudUI != null)
        {
            hudUI.SetActive(false);
        }
        
        Debug.Log("Loading screen created with progress bar");
    }
    
    private void UpdateLoadingScreen()
    {
        if (loadingScreenUI == null) return;
        
        bool starsReady = stellarManager == null || stellarManager.IsDataLoaded();
        int starCount = stellarManager != null ? stellarManager.GetLoadedStarCount() : 0;
        float progress = Mathf.Clamp01((float)starCount / ESTIMATED_TOTAL_STARS);
        
        if (starsReady && !loadingComplete)
        {
            // Stars are ready, start fading out
            loadingFadeProgress += Time.deltaTime * LOADING_FADE_SPEED;
            
            // Ensure progress bar shows 100% when complete
            if (progressBarFill != null)
            {
                RectTransform fillRect = progressBarFill.GetComponent<RectTransform>();
                fillRect.anchorMax = new Vector2(1f, 1f);
            }
            if (progressText != null)
            {
                progressText.text = $"100% - {starCount:N0} / {ESTIMATED_TOTAL_STARS:N0} stars";
            }
            
            if (loadingFadeProgress >= 1f)
            {
                loadingComplete = true;
                loadingScreenUI.SetActive(false);
                
                // Show HUD now that loading is complete
                if (hudUI != null)
                {
                    hudUI.SetActive(true);
                }
                
                Debug.Log("Loading complete - stars ready!");
            }
            else
            {
                // Fade out the loading screen
                float alpha = 1f - loadingFadeProgress;
                if (loadingBackground != null)
                {
                    loadingBackground.color = new Color(0.02f, 0.02f, 0.05f, alpha);
                }
                if (loadingText != null)
                {
                    loadingText.color = new Color(0.8f, 0.9f, 1f, alpha);
                }
                if (progressBarBackground != null)
                {
                    progressBarBackground.color = new Color(0.1f, 0.12f, 0.18f, alpha);
                }
                if (progressBarFill != null)
                {
                    progressBarFill.color = new Color(0.3f, 0.6f, 1f, alpha);
                }
                if (progressText != null)
                {
                    progressText.color = new Color(0.6f, 0.7f, 0.8f, alpha * 0.9f);
                }
            }
        }
        else if (!starsReady)
        {
            // Animate loading text with dots
            int dotCount = (int)(Time.time * 2f) % 4;
            string dots = new string('.', dotCount);
            loadingText.text = $"Loading Stellar Data{dots}";
            
            // Update progress bar fill
            if (progressBarFill != null)
            {
                RectTransform fillRect = progressBarFill.GetComponent<RectTransform>();
                // Smoothly animate the progress bar
                float currentProgress = fillRect.anchorMax.x;
                float targetProgress = progress;
                float smoothProgress = Mathf.Lerp(currentProgress, targetProgress, Time.deltaTime * 5f);
                fillRect.anchorMax = new Vector2(smoothProgress, 1f);
            }
            
            // Update progress text
            if (progressText != null)
            {
                int percentage = Mathf.RoundToInt(progress * 100f);
                progressText.text = $"{percentage}% - {starCount:N0} / {ESTIMATED_TOTAL_STARS:N0} stars";
            }
            
            // Subtle pulsing effect on progress bar color
            float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 3f);
            if (progressBarFill != null)
            {
                progressBarFill.color = new Color(0.3f * pulse, 0.6f * pulse, 1f, 1f);
            }
        }
    }
    
    /// <summary>
    /// Updates the World Space canvas position to follow the VR camera.
    /// This ensures UI elements are visible in VR headsets.
    /// Only runs when in VR mode - desktop mode uses Screen Space Overlay.
    /// </summary>
    private void UpdateVRCanvas()
    {
        // Only update canvas position in VR mode
        // In desktop mode, Screen Space Overlay handles positioning automatically
        if (!isVRMode) return;
        
        if (labelCanvas == null) return;
        
        Camera cam = GetActiveCamera();
        if (cam == null) return;
        
        // Position the canvas in front of the camera
        // Distance of 2 meters is comfortable for VR viewing
        float canvasDistance = 2f;
        
        Transform canvasTransform = labelCanvas.transform;
        
        // Position canvas in front of camera
        canvasTransform.position = cam.transform.position + cam.transform.forward * canvasDistance;
        
        // Make canvas face the camera
        canvasTransform.rotation = cam.transform.rotation;
        
        // Assign the world camera for proper raycasting in VR
        if (labelCanvas.worldCamera != cam)
        {
            labelCanvas.worldCamera = cam;
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
        earthFound = LoadBodiesFromFile("solar_dataset_plus.csv", earthFound) || earthFound;
        
        // Load Alpha Centauri system dataset
        LoadBodiesFromFile("centauri_system.csv", earthFound);
        
        if (!earthFound)
        {
            Debug.LogWarning("Earth (naifId 399) not found in dataset. Player starts at origin in real space.");
            playerRealPosAu = Vector3.zero;
        }
    }
    
    private bool LoadBodiesFromFile(string fileName, bool earthFound)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "PlanetDatasetPlus", fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"CSV not found at {path}");
            return earthFound;
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1)
        {
            Debug.LogWarning($"CSV {fileName} seems empty or header-only.");
            return earthFound;
        }
        
        Debug.Log($"Loading {fileName}: {lines.Length - 1} entries");
        bool localEarthFound = earthFound;

        // Parse new Gaia-format CSV: source_id,object_type,ra_deg,dec_deg,parallax_mas,distance_pc,phot_g_mean_mag,abs_mag_g,size_km,vx_au_d,vy_au_d,vz_au_d,speed_km_s,gm_km3_s2,mass_kg,density_g_cm3,mean_radius_km,albedo,rot_per_hr,H
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 17) continue; // Need at least up to mean_radius_km

            // Parse fields from new format
            if (!int.TryParse(parts[0], out int naifId)) continue; // source_id is the NAIF ID
            
            string objectType = parts[1]; // object_type (sun, planet, moon, dwarf_planet, star)
            
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
            Vector3 realPosAu;
            if (naifId == 10 || !hasPosition) // Sun or invalid position
            {
                realPosAu = Vector3.zero;
            }
            else
            {
                // Convert RA/Dec/Distance from parsecs to Cartesian AU coordinates
                float ra_rad = ra_deg * Mathf.Deg2Rad;
                float dec_rad = dec_deg * Mathf.Deg2Rad;
                
                float cos_dec = Mathf.Cos(dec_rad);
                float distance_au = distance_pc * PARSEC_TO_AU;
                
                float x = distance_au * cos_dec * Mathf.Cos(ra_rad);
                float y = distance_au * Mathf.Sin(dec_rad);
                float z = distance_au * cos_dec * Mathf.Sin(ra_rad);
                
                realPosAu = new Vector3(x, y, z);
            }
            
            // Parse mean radius (field index 16)
            float radiusKm = 1000f; // default fallback
            if (parts.Length > 16 && !string.IsNullOrEmpty(parts[16]))
            {
                if (float.TryParse(parts[16], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                {
                    radiusKm = radius;
                }
                else
                {
                    // Fallback to BodyRadiiKm dictionary if parsing fails
                    radiusKm = BodyRadiiKm.TryGetValue(naifId, out float r) ? r : 1_000f;
                }
            }
            else
            {
                // Fallback to BodyRadiiKm dictionary if field is empty
                radiusKm = BodyRadiiKm.TryGetValue(naifId, out float r) ? r : 1_000f;
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
                // Try to use planet-specific material first, fall back to generic material
                Material materialToUse = planetMaterials.TryGetValue(naifId, out Material specificMaterial) ? specificMaterial : planetMaterial;
                if (materialToUse != null)
                {
                    renderer.sharedMaterial = materialToUse;
                }
            }

            var body = new BodyInstance
            {
                name = name,
                naifId = naifId,
                realPosAu = realPosAu,
                radiusKm = radiusKm,
                proxy = proxy.transform
            };

            // NEW: optional label setup
            if (enableLabels)
            {
                CreateLabelForBody(body);
                Debug.Log($"Created label for {body.name}");
            }

            bodies.Add(body);

            // Player positioned in front of Earth for good viewing
            if (naifId == 399 && !localEarthFound)
            {
                // Position player at a good distance in front of Earth for viewing
                Vector3 earthOffset = new Vector3(0, 0, -0.1f); // 0.1 AU in front of Earth along -Z axis
                playerRealPosAu = realPosAu + earthOffset;
                localEarthFound = true;
                Debug.Log($"Player positioned in front of Earth at: {playerRealPosAu} AU");
            }
        }

        return localEarthFound;
    }

    private void CreateLabelForBody(BodyInstance body)
    {
        if (labelCanvas == null)
        {
            Debug.LogWarning("Label Canvas not assigned. Please assign a Canvas for labels.");
            return;
        }

        // Create UI GameObject
        GameObject labelGO = new GameObject(body.name + "_Label");
        labelGO.transform.SetParent(labelCanvas.transform, false);

        // Add RectTransform
        RectTransform rectTransform = labelGO.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(200, 30);

        // Add Text component
        // Add Text component
        TextMeshProUGUI textComponent = labelGO.AddComponent<TextMeshProUGUI>();
        textComponent.text = body.name;
        if (labelFont != null) textComponent.font = labelFont;
        textComponent.fontSize = labelFontSize;
        textComponent.color = labelColor;
        textComponent.alignment = TextAlignmentOptions.Left;
        textComponent.enableWordWrapping = false;
        textComponent.overflowMode = TextOverflowModes.Overflow;

        // Store references
        body.labelUI = labelGO;
    }

    private float ParseFloat(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    private string GetBodyName(int naifId, string objectType)
    {
        // Map NAIF IDs to common names
        switch (naifId)
        {
            case 10: return "Sun";
            case 199: return "Mercury";
            case 299: return "Venus";
            case 399: return "Earth";
            case 301: return "Moon";
            case 401: return "Phobos";
            case 402: return "Deimos";
            case 499: return "Mars";
            case 501: return "Io";
            case 502: return "Europa";
            case 503: return "Ganymede";
            case 504: return "Callisto";
            case 599: return "Jupiter";
            case 601: return "Mimas";
            case 602: return "Enceladus";
            case 603: return "Tethys";
            case 604: return "Dione";
            case 605: return "Rhea";
            case 606: return "Titan";
            case 699: return "Saturn";
            case 799: return "Uranus";
            case 899: return "Neptune";
            case 999: return "Pluto";
            default:
                // For unknown IDs, use the object type and ID
                return $"{objectType} {naifId}";
        }
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
        float u = Mathf.Atan2(p.x, p.z) / (2f * Mathf.PI) + 0.5f;
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
    
    private void LoadPlanetMaterials()
    {
        string path = Path.Combine(Application.streamingAssetsPath, planetMaterialsCsvFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Planet materials CSV not found at {path}. Using default materials.");
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= 1)
            {
                Debug.LogWarning("Planet materials CSV seems empty or header-only.");
                return;
            }
            
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                string[] parts = line.Split(',');
                if (parts.Length < 2) continue;
                
                if (int.TryParse(parts[0], out int naifId))
                {
                    string materialPath = parts[1].Trim();
                    
                    // Load material from asset path (works in editor and build)
                    Material material = null;
                    
#if UNITY_EDITOR
                    // In editor, use AssetDatabase
                    material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(materialPath);
#else
                    // In build, try to find material by converting path to Resources path
                    string resourcesPath = materialPath.Replace("Assets/", "").Replace(".mat", "");
                    material = Resources.Load<Material>(resourcesPath);
#endif
                    
                    if (material != null)
                    {
                        planetMaterials[naifId] = material;
                        Debug.Log($"Loaded material for NAIF ID {naifId}: {materialPath}");
                    }
                    else
                    {
                        Debug.LogWarning($"Could not load material at path: {materialPath} for NAIF ID {naifId}");
                    }
                }
            }
            
            Debug.Log($"Loaded {planetMaterials.Count} planet-specific materials.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading planet materials CSV: {e.Message}");
        }
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
        
        // Calculate distance to nearest planet surface
        Vector3 offsetAu = nearestPlanet.realPosAu - playerRealPosAu;
        float distanceAu = offsetAu.magnitude;
        
        // Convert planet radius from km to AU for comparison
        float planetRadiusAu = nearestPlanet.radiusKm / (float)AU_KM;
        
        // Distance to planet surface (not center) - prevent negative distance
        distanceToNearestPlanet = Mathf.Max(0.000000001f, distanceAu - planetRadiusAu);
        
        // IMPROVED SCALING AND SPEED SYSTEM:
        // Fixed issues with flying through planets and speed decreasing when flying away
        
        float zoneDistance = planetRadiusAu * 100f; // Single zone distance
        
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
        
        // Determine movement direction relative to planet
        Vector3 toPlanet = (nearestPlanet.realPosAu - playerRealPosAu).normalized;
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
            Debug.Log($"Distance-Based Speed - Distance: {distanceToNearestPlanet:F6} AU, Scale: {currentScale:F5}, Speed: {currentSpeed:F6} AU/s, Planet: {nearestPlanet.name}");
        }
    }
    
    private void UpdateHUD()
    {
        if (!enableHUD || hudText == null) return;
        
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
        
        // Calculate distance from Sun (origin)
        double distanceFromSunAu = playerRealPosAu.magnitude;
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
        hudText.text = $"Speed: {speedDisplay} ({lightSpeedDisplay})\n" +
                      $"Distance from Sun: {distanceDisplay}\n" +
                      $"Mode: DISTANCE-BASED";
        
        if (nearestPlanet != null)
        {
            hudText.text += $"\nNearest: {nearestPlanet.name}\n" +
                           $"Distance: {distanceToNearestPlanet:F6} AU";
        }
        
        // Add stellar manager info
        if (stellarManager != null)
        {
            hudText.text += $"\nStars Visible: {stellarManager.GetVisibleStarCount()}";
            if (!stellarManager.IsDataLoaded())
            {
                hudText.text += " (Loading...)";
            }
        }
        
        // Add autopilot status
        if (autopilotActive && autopilotTarget != null)
        {
            Vector3 toTarget = autopilotTarget.realPosAu - playerRealPosAu;
            float distanceAu = toTarget.magnitude;
            double distKm = distanceAu * AU_KM;
            
            string autopilotDistDisplay;
            if (distKm >= 1_000_000)
                autopilotDistDisplay = $"{distKm / 1_000_000:F2}M km";
            else if (distKm >= 1_000)
                autopilotDistDisplay = $"{distKm / 1_000:F1}k km";
            else
                autopilotDistDisplay = $"{distKm:F0} km";
            
            hudText.text += $"\n\n[AUTOPILOT] → {autopilotTarget.name}\nDistance: {autopilotDistDisplay}\nPress X to cancel";
        }
        else if (!autopilotActive && !autopilotMenuOpen)
        {
            hudText.text += "\n\nPress X for Autopilot";
            if (nearestPlanet != null && nearestPlanet.planetData != null)
            {
                hudText.text += $" | Press I for info on {nearestPlanet.name}";
            }
        }
    }
    
    private void FindNearestPlanet()
    {
        float closestDistanceSqr = Mathf.Infinity;
        BodyInstance closest = null;
        
        foreach (var body in bodies)
        {
            Vector3 offset = body.realPosAu - playerRealPosAu;
            float distanceSqr = offset.sqrMagnitude;
            
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closest = body;
            }
        }
        
        nearestPlanet = closest;
    }

    // --- Runtime updates ---

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
            Vector3 offsetToNearestAu = nearestPlanet.realPosAu - playerRealPosAu;
            float distAu = offsetToNearestAu.magnitude;
            double distKm = distAu * AU_KM;
            double angularRadius = Math.Atan(nearestPlanet.radiusKm / distKm);
            double proxyRadius = Math.Tan(angularRadius) * horizonRadius;
            bool planetAtMaxSize = proxyRadius >= maxProxyRadius;
            
            if (planetAtMaxSize)
            {
                // Check if movement is towards the planet
                Vector3 toPlanet = offsetToNearestAu.normalized;
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

        playerRealPosAu += moveDir * (currentSpeed * Time.deltaTime);
        
        // Track actual movement speed
        actualSpeed = currentSpeed;
    }

    private void UpdateBodyProxies()
    {
        Camera cam = GetActiveCamera();
        RectTransform canvasRect = labelCanvas != null ? labelCanvas.GetComponent<RectTransform>() : null;
        
        // First pass: calculate positions and store label data for collision detection
        List<(BodyInstance body, Vector2 screenPos, float distAu, Rect labelBounds)> visibleLabels = 
            new List<(BodyInstance, Vector2, float, Rect)>();
        
        foreach (var body in bodies)
        {
            Vector3 offsetAu = body.realPosAu - playerRealPosAu;
            float distAu = offsetAu.magnitude;

            if (distAu < 1e-6f)
            {
                body.proxy.gameObject.SetActive(false);
                if (body.labelUI != null)
                    body.labelUI.SetActive(false);
                continue;
            }

            body.proxy.gameObject.SetActive(true);

            Vector3 dir = offsetAu / distAu;

            // Position on horizon sphere
            Vector3 proxyPos = dir * horizonRadius;
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
            if (enableLabels && body.labelUI != null && cam != null && canvasRect != null)
            {
                // Convert world position to viewport position (0-1 range, works better for VR)
                Vector3 viewportPos = cam.WorldToViewportPoint(body.proxy.position);
                
                // Check if object is in front of camera and within view (using viewport 0-1 range)
                bool isVisible = viewportPos.z > 0 && 
                               viewportPos.x >= 0 && viewportPos.x <= 1 &&
                               viewportPos.y >= 0 && viewportPos.y <= 1;
                
                if (isVisible)
                {
                    RectTransform labelRect = body.labelUI.GetComponent<RectTransform>();
                    
                    // Convert viewport to screen position for the canvas calculation
                    Vector3 screenPos = new Vector3(
                        viewportPos.x * Screen.width,
                        viewportPos.y * Screen.height,
                        viewportPos.z);
                    
                    // For Screen Space Overlay (desktop), use null camera
                    // For World Space (VR), use the active camera
                    Camera canvasCam = isVRMode ? labelCanvas.worldCamera : null;
                    
                    Vector2 canvasPos;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, screenPos, canvasCam, out canvasPos);
                    
                    // Add offset to position label to the right of the planet
                    canvasPos.x += labelOffsetPixels;
                    
                    // Calculate label bounds (approximate based on text size)
                    float labelWidth = 100f; // Approximate label width
                    float labelHeight = 25f; // Approximate label height
                    Rect bounds = new Rect(canvasPos.x, canvasPos.y - labelHeight / 2, labelWidth, labelHeight);
                    
                    visibleLabels.Add((body, canvasPos, distAu, bounds));
                }
                else
                {
                    body.labelUI.SetActive(false);
                }
            }
        }
        
        // Second pass: detect overlaps and hide overlapping labels
        // Sort by distance (closest first) - closer labels have priority
        visibleLabels.Sort((a, b) => a.distAu.CompareTo(b.distAu));
        
        List<Rect> occupiedRects = new List<Rect>();
        
        foreach (var labelData in visibleLabels)
        {
            bool hasOverlap = false;
            
            // Check against all already-placed labels
            foreach (var occupied in occupiedRects)
            {
                if (labelData.labelBounds.Overlaps(occupied))
                {
                    hasOverlap = true;
                    break;
                }
            }
            
            if (hasOverlap)
            {
                // Try to offset the label vertically
                Vector2 offsetPos = labelData.screenPos;
                Rect offsetBounds = labelData.labelBounds;
                bool foundSpot = false;
                
                // Try offsetting up first, then down
                float[] offsets = { 30f, -30f, 60f, -60f };
                foreach (float yOffset in offsets)
                {
                    offsetBounds = new Rect(
                        labelData.labelBounds.x, 
                        labelData.labelBounds.y + yOffset, 
                        labelData.labelBounds.width, 
                        labelData.labelBounds.height);
                    
                    bool stillOverlaps = false;
                    foreach (var occupied in occupiedRects)
                    {
                        if (offsetBounds.Overlaps(occupied))
                        {
                            stillOverlaps = true;
                            break;
                        }
                    }
                    
                    if (!stillOverlaps)
                    {
                        offsetPos.y += yOffset;
                        foundSpot = true;
                        break;
                    }
                }
                
                if (foundSpot)
                {
                    // Place with offset
                    RectTransform labelRect = labelData.body.labelUI.GetComponent<RectTransform>();
                    labelRect.localPosition = offsetPos;
                    labelData.body.labelUI.SetActive(true);
                    occupiedRects.Add(offsetBounds);
                }
                else
                {
                    // No spot found - hide this label
                    labelData.body.labelUI.SetActive(false);
                }
            }
            else
            {
                // No overlap - show label at original position
                RectTransform labelRect = labelData.body.labelUI.GetComponent<RectTransform>();
                labelRect.localPosition = labelData.screenPos;
                labelData.body.labelUI.SetActive(true);
                occupiedRects.Add(labelData.labelBounds);
            }
        }
    }
    
    // --- Autopilot System ---
    
    private bool moonsExpanded = false;
    private GameObject moonsContainer;
    private RectTransform autopilotContentRect;
    
    private void CreateAutopilotMenu()
    {
        if (labelCanvas == null)
        {
            Debug.LogWarning("Cannot create Autopilot Menu: Label Canvas not available.");
            return;
        }
        
        // Create main panel
        autopilotUI = new GameObject("AutopilotMenu");
        autopilotUI.transform.SetParent(labelCanvas.transform, false);
        
        RectTransform panelRect = autopilotUI.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(500, 700);
        
        // Add semi-transparent background
        Image panelImage = autopilotUI.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.2f, 0.95f);
        
        // Add title
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(autopilotUI.transform, false);
        RectTransform titleRect = titleGO.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -15);
        titleRect.sizeDelta = new Vector2(0, 60);
        
        TextMeshProUGUI titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = "AUTOPILOT - Select Destination";
        if (labelFont != null) titleText.font = labelFont;
        titleText.fontSize = 32;
        titleText.color = Color.cyan;
        titleText.alignment = TextAlignmentOptions.Center;
        
        // Create scroll view for body list - leave room for scrollbar
        GameObject scrollViewGO = new GameObject("ScrollView");
        scrollViewGO.transform.SetParent(autopilotUI.transform, false);
        RectTransform scrollViewRect = scrollViewGO.AddComponent<RectTransform>();
        scrollViewRect.anchorMin = new Vector2(0, 0);
        scrollViewRect.anchorMax = new Vector2(1, 1);
        scrollViewRect.offsetMin = new Vector2(15, 80);
        scrollViewRect.offsetMax = new Vector2(-15, -70);
        
        ScrollRect scroll = scrollViewGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 30f;
        
        // Viewport - leave room for scrollbar on right
        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollViewGO.transform, false);
        RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = new Vector2(-15, 0); // Room for scrollbar
        
        Image viewportMask = viewportGO.AddComponent<Image>();
        viewportMask.color = new Color(1, 1, 1, 0.01f);
        Mask mask = viewportGO.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        
        scroll.viewport = viewportRect;
        
        // Content container
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        autopilotContentRect = contentGO.AddComponent<RectTransform>();
        autopilotContentRect.anchorMin = new Vector2(0, 1);
        autopilotContentRect.anchorMax = new Vector2(1, 1);
        autopilotContentRect.pivot = new Vector2(0.5f, 1);
        autopilotContentRect.anchoredPosition = Vector2.zero;
        
        scroll.content = autopilotContentRect;
        
        // Create visible scrollbar
        GameObject scrollbarGO = new GameObject("Scrollbar");
        scrollbarGO.transform.SetParent(scrollViewGO.transform, false);
        RectTransform scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.pivot = new Vector2(1, 0.5f);
        scrollbarRect.anchoredPosition = Vector2.zero;
        scrollbarRect.sizeDelta = new Vector2(20, 0);
        
        Image scrollbarBg = scrollbarGO.AddComponent<Image>();
        scrollbarBg.color = new Color(0.15f, 0.15f, 0.25f, 0.8f);
        
        Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        
        // Scrollbar handle
        GameObject handleAreaGO = new GameObject("Handle Slide Area");
        handleAreaGO.transform.SetParent(scrollbarGO.transform, false);
        RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(2, 2);
        handleAreaRect.offsetMax = new Vector2(-2, -2);
        
        GameObject handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaGO.transform, false);
        RectTransform handleRect = handleGO.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.sizeDelta = Vector2.zero;
        
        Image handleImage = handleGO.AddComponent<Image>();
        handleImage.color = new Color(0.4f, 0.6f, 0.9f, 0.9f);
        
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        
        ColorBlock scrollColors = scrollbar.colors;
        scrollColors.normalColor = new Color(0.4f, 0.6f, 0.9f, 0.9f);
        scrollColors.highlightedColor = new Color(0.5f, 0.7f, 1f, 1f);
        scrollColors.pressedColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        scrollbar.colors = scrollColors;
        
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        
        // Separate bodies into planets/sun and moons
        List<BodyInstance> mainBodies = new List<BodyInstance>();
        List<BodyInstance> moons = new List<BodyInstance>();
        
        foreach (var body in bodies)
        {
            if (IsMoon(body.naifId))
            {
                moons.Add(body);
            }
            else
            {
                mainBodies.Add(body);
            }
        }
        
        // Add buttons for main bodies
        float buttonHeight = 50f;
        float spacing = 8f;
        int itemIndex = 0;
        
        // Main bodies (Sun, planets)
        foreach (var body in mainBodies)
        {
            CreateAutopilotButton(contentGO, body, itemIndex, buttonHeight, spacing, false);
            itemIndex++;
        }
        
        // Moons category header (only if there are moons)
        if (moons.Count > 0)
        {
            CreateMoonsCategoryButton(contentGO, itemIndex, buttonHeight, spacing);
            itemIndex++;
            
            // Create moons container (initially hidden)
            moonsContainer = new GameObject("MoonsContainer");
            moonsContainer.transform.SetParent(contentGO.transform, false);
            RectTransform moonsContainerRect = moonsContainer.AddComponent<RectTransform>();
            moonsContainerRect.anchorMin = new Vector2(0, 1);
            moonsContainerRect.anchorMax = new Vector2(1, 1);
            moonsContainerRect.pivot = new Vector2(0.5f, 1);
            moonsContainerRect.anchoredPosition = new Vector2(0, -itemIndex * (buttonHeight + spacing));
            moonsContainerRect.sizeDelta = new Vector2(0, moons.Count * (buttonHeight + spacing));
            
            int moonIndex = 0;
            foreach (var moon in moons)
            {
                CreateAutopilotButton(moonsContainer, moon, moonIndex, buttonHeight, spacing, true);
                moonIndex++;
            }
            
            moonsContainer.SetActive(false);
        }
        
        // Calculate initial content size (without moons expanded)
        int visibleCount = mainBodies.Count + (moons.Count > 0 ? 1 : 0);
        autopilotContentRect.sizeDelta = new Vector2(0, visibleCount * (buttonHeight + spacing));
        
        // Cancel button at bottom
        CreateCancelButton();
        
        // Start hidden
        autopilotUI.SetActive(false);
        
        Debug.Log($"Autopilot menu created: {mainBodies.Count} planets, {moons.Count} moons");
    }
    
    private bool IsMoon(int naifId)
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
    
    private void CreateAutopilotButton(GameObject parent, BodyInstance body, int index, float buttonHeight, float spacing, bool isMoon)
    {
        GameObject buttonGO = new GameObject(body.name + "_Button");
        buttonGO.transform.SetParent(parent.transform, false);
        
        RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0, 1);
        buttonRect.anchorMax = new Vector2(1, 1);
        buttonRect.pivot = new Vector2(0.5f, 1);
        buttonRect.anchoredPosition = new Vector2(0, -index * (buttonHeight + spacing));
        buttonRect.sizeDelta = new Vector2(isMoon ? -40 : -20, buttonHeight); // Indent moons
        
        Image buttonImage = buttonGO.AddComponent<Image>();
        Color btnColor = isMoon ? new Color(0.12f, 0.2f, 0.35f, 1f) : new Color(0.15f, 0.25f, 0.4f, 1f);
        buttonImage.color = btnColor;
        
        Button button = buttonGO.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.normalColor = btnColor;
        colors.highlightedColor = new Color(0.3f, 0.6f, 1f, 1f);
        colors.pressedColor = new Color(0.1f, 0.3f, 0.6f, 1f);
        colors.selectedColor = new Color(0.25f, 0.45f, 0.7f, 1f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.15f;
        button.colors = colors;
        
        BodyInstance capturedBody = body;
        button.onClick.AddListener(() => SelectAutopilotTarget(capturedBody));
        
        autopilotButtons.Add(button);
        
        // Button text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(10, 0);
        textRect.offsetMax = Vector2.zero;
        
        TextMeshProUGUI btnText = textGO.AddComponent<TextMeshProUGUI>();
        btnText.text = (isMoon ? "  " : "") + body.name;
        if (labelFont != null) btnText.font = labelFont;
        btnText.fontSize = isMoon ? 20 : 24;
        btnText.color = isMoon ? new Color(0.8f, 0.9f, 1f, 1f) : Color.white;
        btnText.alignment = TextAlignmentOptions.Left;
    }
    
    private void CreateMoonsCategoryButton(GameObject parent, int index, float buttonHeight, float spacing)
    {
        GameObject buttonGO = new GameObject("Moons_Category");
        buttonGO.transform.SetParent(parent.transform, false);
        
        RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0, 1);
        buttonRect.anchorMax = new Vector2(1, 1);
        buttonRect.pivot = new Vector2(0.5f, 1);
        buttonRect.anchoredPosition = new Vector2(0, -index * (buttonHeight + spacing));
        buttonRect.sizeDelta = new Vector2(-20, buttonHeight);
        
        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.15f, 0.3f, 1f); // Purple tint for category
        
        Button button = buttonGO.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.15f, 0.3f, 1f);
        colors.highlightedColor = new Color(0.35f, 0.25f, 0.5f, 1f);
        colors.pressedColor = new Color(0.15f, 0.1f, 0.25f, 1f);
        colors.selectedColor = new Color(0.25f, 0.2f, 0.4f, 1f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.15f;
        button.colors = colors;
        
        button.onClick.AddListener(ToggleMoonsCategory);
        
        // Button text with arrow indicator
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(10, 0);
        textRect.offsetMax = Vector2.zero;
        
        TextMeshProUGUI btnText = textGO.AddComponent<TextMeshProUGUI>();
        btnText.text = "▸ Moons";
        if (labelFont != null) btnText.font = labelFont;
        btnText.fontSize = 24;
        btnText.color = new Color(0.9f, 0.8f, 1f, 1f); // Light purple text
        btnText.alignment = TextAlignmentOptions.Left;
    }
    
    private void ToggleMoonsCategory()
    {
        moonsExpanded = !moonsExpanded;
        
        if (moonsContainer != null)
        {
            moonsContainer.SetActive(moonsExpanded);
            
            // Update arrow in category button
            Transform categoryBtn = autopilotUI.transform.Find("AutopilotMenu/ScrollView/Viewport/Content/Moons_Category");
            if (categoryBtn == null)
            {
                // Try to find it differently
                foreach (Transform child in autopilotContentRect)
                {
                    if (child.name == "Moons_Category")
                    {
                        TextMeshProUGUI txt = child.GetComponentInChildren<TextMeshProUGUI>();
                        if (txt != null)
                        {
                            txt.text = moonsExpanded ? "▾ Moons" : "▸ Moons";
                        }
                        break;
                    }
                }
            }
            
            // Recalculate content size
            float buttonHeight = 50f;
            float spacing = 8f;
            
            int mainCount = 0;
            int moonCount = 0;
            foreach (var body in bodies)
            {
                if (IsMoon(body.naifId)) moonCount++;
                else mainCount++;
            }
            
            int visibleCount = mainCount + 1; // +1 for moons category header
            if (moonsExpanded)
            {
                visibleCount += moonCount;
            }
            
            autopilotContentRect.sizeDelta = new Vector2(0, visibleCount * (buttonHeight + spacing));
            
            // Reposition moons container if expanded
            if (moonsExpanded && moonsContainer != null)
            {
                RectTransform moonsRect = moonsContainer.GetComponent<RectTransform>();
                moonsRect.anchoredPosition = new Vector2(0, -(mainCount + 1) * (buttonHeight + spacing));
            }
        }
    }
    
    private void CreateCancelButton()
    {
        GameObject cancelGO = new GameObject("CancelButton");
        cancelGO.transform.SetParent(autopilotUI.transform, false);
        RectTransform cancelRect = cancelGO.AddComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(0.5f, 0);
        cancelRect.anchorMax = new Vector2(0.5f, 0);
        cancelRect.pivot = new Vector2(0.5f, 0);
        cancelRect.anchoredPosition = new Vector2(0, 15);
        cancelRect.sizeDelta = new Vector2(180, 50);
        
        Image cancelImage = cancelGO.AddComponent<Image>();
        cancelImage.color = new Color(0.5f, 0.2f, 0.2f, 1f);
        
        Button cancelBtn = cancelGO.AddComponent<Button>();
        cancelBtn.targetGraphic = cancelImage;
        ColorBlock cancelColors = cancelBtn.colors;
        cancelColors.normalColor = new Color(0.5f, 0.2f, 0.2f, 1f);
        cancelColors.highlightedColor = new Color(0.8f, 0.3f, 0.3f, 1f);
        cancelColors.pressedColor = new Color(0.3f, 0.1f, 0.1f, 1f);
        cancelColors.selectedColor = new Color(0.6f, 0.25f, 0.25f, 1f);
        cancelColors.fadeDuration = 0.15f;
        cancelBtn.colors = cancelColors;
        cancelBtn.onClick.AddListener(() => ToggleAutopilotMenu());
        
        GameObject cancelTextGO = new GameObject("Text");
        cancelTextGO.transform.SetParent(cancelGO.transform, false);
        RectTransform cancelTextRect = cancelTextGO.AddComponent<RectTransform>();
        cancelTextRect.anchorMin = Vector2.zero;
        cancelTextRect.anchorMax = Vector2.one;
        cancelTextRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI cancelText = cancelTextGO.AddComponent<TextMeshProUGUI>();
        cancelText.text = "Cancel";
        if (labelFont != null) cancelText.font = labelFont;
        cancelText.fontSize = 24;
        cancelText.color = Color.white;
        cancelText.alignment = TextAlignmentOptions.Center;
    }
    
    private void ToggleAutopilotMenu()
    {
        if (autopilotUI == null) return;
        
        if (autopilotActive)
        {
            // If autopilot is traveling, pressing X cancels it
            StopAutopilot();
            return;
        }
        // Mutual exclusion: Close Planet Info if open
        if (planetInfoVisible)
        {
            planetInfoVisible = false;
            UpdatePlanetInfoPanelPosition(); // Immediate hide or let update loop handle it
        }
        
        autopilotMenuOpen = !autopilotMenuOpen;
        autopilotUI.SetActive(autopilotMenuOpen);
        IsMenuOpen = autopilotMenuOpen; // Update static property for other scripts
    }
    
    private void SelectAutopilotTarget(BodyInstance body)
    {
        autopilotTarget = body;
        autopilotActive = true;
        IsAutopilotActive = true; // Static property for other scripts
        
        // Close the menu
        autopilotMenuOpen = false;
        IsMenuOpen = false; // Update static property
        if (autopilotUI != null)
            autopilotUI.SetActive(false);
        
        Debug.Log($"Autopilot: Traveling to {body.name}");
    }
    
    private void UpdateAutopilot()
    {
        if (!autopilotActive || autopilotTarget == null) return;
        
        // Calculate direction and distance to target
        Vector3 toTarget = autopilotTarget.realPosAu - playerRealPosAu;
        float distanceAu = toTarget.magnitude;
        
        // Convert target radius to AU for stopping distance
        float targetRadiusAu = autopilotTarget.radiusKm / (float)AU_KM;
        float stopDistanceAu = targetRadiusAu * 10f; // Stop at 10x planet radius
        
        // Check if we've arrived
        if (distanceAu <= stopDistanceAu)
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
        Vector3 moveDir = toTarget.normalized;
        
        // Move towards target
        float moveAmount = currentSpeed * Time.deltaTime * 1.5f; // 1.5x normal speed for autopilot
        
        // Don't overshoot
        if (moveAmount > distanceAu - stopDistanceAu)
        {
            moveAmount = distanceAu - stopDistanceAu;
        }
        
        playerRealPosAu += moveDir * moveAmount;
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
    
    private void CreatePlanetInfoPanel()
    {
        if (labelCanvas == null)
        {
            Debug.LogWarning("Cannot create Planet Info Panel: Label Canvas not available.");
            return;
        }
        
        // Create main panel - positioned off-screen to the right initially
        planetInfoUI = new GameObject("PlanetInfoPanel");
        planetInfoUI.transform.SetParent(labelCanvas.transform, false);
        
        RectTransform panelRect = planetInfoUI.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0, -1200); // Start off-screen (bottom)
        panelRect.sizeDelta = new Vector2(600, 800);
        
        // Add gradient-like background (semi-transparent dark blue/purple)
        Image panelImage = planetInfoUI.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.08f, 0.18f, 0.92f);
        
        // Add left border accent
        GameObject borderGO = new GameObject("LeftBorder");
        borderGO.transform.SetParent(planetInfoUI.transform, false);
        RectTransform borderRect = borderGO.AddComponent<RectTransform>();
        borderRect.anchorMin = new Vector2(0, 0);
        borderRect.anchorMax = new Vector2(0, 1);
        borderRect.pivot = new Vector2(0, 0.5f);
        borderRect.anchoredPosition = Vector2.zero;
        borderRect.sizeDelta = new Vector2(4, 0);
        Image borderImage = borderGO.AddComponent<Image>();
        borderImage.color = new Color(0.3f, 0.8f, 1f, 0.9f); // Cyan accent
        
        // Create title/header section
        GameObject headerGO = new GameObject("Header");
        headerGO.transform.SetParent(planetInfoUI.transform, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.anchoredPosition = new Vector2(0, -15);
        headerRect.sizeDelta = new Vector2(-30, 50);
        
        planetInfoNameText = headerGO.AddComponent<TextMeshProUGUI>();
        planetInfoNameText.text = "PLANET INFO";
        if (labelFont != null) planetInfoNameText.font = labelFont;
        planetInfoNameText.fontSize = 42;
        planetInfoNameText.fontStyle = FontStyles.Bold;
        planetInfoNameText.color = new Color(0.3f, 0.9f, 1f, 1f); // Bright cyan
        planetInfoNameText.alignment = TextAlignmentOptions.Center;
        
        // Create data content section
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(planetInfoUI.transform, false);
        RectTransform contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.offsetMin = new Vector2(20, 50);
        contentRect.offsetMax = new Vector2(-15, -75);
        
        planetInfoDataText = contentGO.AddComponent<TextMeshProUGUI>();
        planetInfoDataText.text = "";
        if (labelFont != null) planetInfoDataText.font = labelFont;
        planetInfoDataText.fontSize = 24;
        planetInfoDataText.color = new Color(0.85f, 0.9f, 1f, 1f); // Soft white-blue
        planetInfoDataText.alignment = TextAlignmentOptions.TopLeft;

        
        // Add close hint at bottom
        GameObject hintGO = new GameObject("CloseHint");
        hintGO.transform.SetParent(planetInfoUI.transform, false);
        RectTransform hintRect = hintGO.AddComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0, 0);
        hintRect.anchorMax = new Vector2(1, 0);
        hintRect.pivot = new Vector2(0.5f, 0);
        hintRect.anchoredPosition = new Vector2(0, 15);
        hintRect.sizeDelta = new Vector2(0, 30);
        
        TextMeshProUGUI hintText = hintGO.AddComponent<TextMeshProUGUI>();
        hintText.text = "Press I to close";
        if (labelFont != null) hintText.font = labelFont;
        hintText.fontSize = 14;
        hintText.fontStyle = FontStyles.Italic;
        hintText.color = new Color(0.5f, 0.6f, 0.7f, 0.8f);
        hintText.alignment = TextAlignmentOptions.Center;
        
        // Start hidden (off-screen)
        planetInfoAnimProgress = 0f;
        UpdatePlanetInfoPanelPosition();
        
        Debug.Log("Planet info panel created successfully");
    }
    
    private void TogglePlanetInfo()
    {
        // Mutual exclusion: Close Autopilot Menu if open
        if (autopilotMenuOpen)
        {
            autopilotMenuOpen = false;
            if (autopilotUI != null) autopilotUI.SetActive(false);
            IsMenuOpen = false;
        }

        planetInfoVisible = !planetInfoVisible;
        
        if (planetInfoVisible && nearestPlanet != null)
        {
            if (planetInfoUI != null) planetInfoUI.SetActive(true);
            PopulatePlanetInfo(nearestPlanet);
            Debug.Log($"Showing planet info for: {nearestPlanet.name}");
        }
    }
    
    private void PopulatePlanetInfo(BodyInstance body)
    {
        if (planetInfoNameText == null || planetInfoDataText == null) return;
        
        planetInfoNameText.text = body.name.ToUpper();
        
        if (body.planetData != null)
        {
            PlanetData data = body.planetData;
            planetInfoDataText.text = 
                $"<color=#88CCFF>Color:</color>  {data.Color}\n\n" +
                $"<color=#88CCFF>Diameter:</color>  {data.Diameter} km\n\n" +
                $"<color=#88CCFF>Density:</color>  {data.Density} kg/m³\n\n" +
                $"<color=#88CCFF>Surface Gravity:</color>  {data.Gravity} m/s²\n\n" +
                $"<color=#88CCFF>Length of Day:</color>  {data.LengthOfDay} hours\n\n" +
                $"<color=#88CCFF>Distance from Sun:</color>  {data.DistanceFromSun} M km\n\n" +
                $"<color=#88CCFF>Mean Temperature:</color>  {data.MeanTemperature}°C\n\n" +
                $"<color=#88CCFF>Moons:</color>  {data.NumberOfMoons}\n\n" +
                $"<color=#88CCFF>Ring System:</color>  {data.RingSystem}\n\n" +
                $"<color=#88CCFF>Atmosphere:</color>  {data.AtmosphericComposition}";
        }
        else
        {
            planetInfoDataText.text = "No detailed data available for this body.\n\n" +
                $"<color=#88CCFF>Radius:</color>  {body.radiusKm:N0} km";
        }
    }
    
    private void UpdatePlanetInfoPanel()
    {
        if (planetInfoUI == null) return;
        
        // Animate panel position
        float targetProgress = planetInfoVisible ? 1f : 0f;
        planetInfoAnimProgress = Mathf.MoveTowards(planetInfoAnimProgress, targetProgress, Time.deltaTime * PLANET_INFO_ANIM_SPEED);
        
        UpdatePlanetInfoPanelPosition();
        
        // Hide GameObject if fully closed
        if (!planetInfoVisible && planetInfoAnimProgress <= 0.01f)
        {
            if (planetInfoUI.activeSelf) 
                planetInfoUI.SetActive(false);
        }
    }
    
    private void UpdatePlanetInfoPanelPosition()
    {
        if (planetInfoUI == null) return;
        
        RectTransform panelRect = planetInfoUI.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            // Smooth easing curve
            float easedProgress = 1f - Mathf.Pow(1f - planetInfoAnimProgress, 3f);
            
            // Slide in from bottom: -1200 (off-screen) to 0 (center)
            float yPos = Mathf.Lerp(-1200, 0, easedProgress);
            panelRect.anchoredPosition = new Vector2(0, yPos);
        }
    }
}