using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// Manages UI elements for the Solar System visualization including labels.
/// Extracted from SolarSystemParallaxManager for better code organization.
/// </summary>
public class SolarSystemUIManager : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private bool enableLabels = true;
    [SerializeField] private Canvas labelCanvas;
    [SerializeField] private TMP_FontAsset labelFont;
    [Tooltip("Font size for planet labels (pt)")]
    [SerializeField] private int labelFontSize = 24;
    [SerializeField] private Color labelColor = Color.white;
    [Tooltip("Offset from planet center (pixels)")]
    [SerializeField] private float labelOffsetPixels = 20f;
    
    [Header("VR Mode")]
    [Tooltip("Enable VR mode for headset display. When disabled, uses standard screen with mouse support.")]
    [SerializeField] private bool enableVRMode = false;
    
    [Tooltip("VR Controller button to toggle the autopilot menu (e.g., menu button or Y/B button).")]
    [SerializeField] private InputActionReference autopilotMenuAction;
    
    [Tooltip("VR Controller button to toggle planet info display.")]
    [SerializeField] private InputActionReference planetInfoAction;
    
    [Tooltip("VR Controller thumbstick/trackpad for menu scrolling (2D axis).")]
    [SerializeField] private InputActionReference menuScrollAction;
    
    [Tooltip("VR Controller button to confirm selection in menu (e.g., trigger or trackpad press).")]
    [SerializeField] private InputActionReference menuSelectAction;
    
    [Header("HUD (Heads-Up Display)")]
    [SerializeField] private bool enableHUD = true;
    [Tooltip("Font size for HUD text (pt)")]
    [SerializeField] private int hudFontSize = 28;
    [SerializeField] private Color hudColor = Color.cyan;
    [SerializeField] private Vector2 hudPosition = new Vector2(300f, -100f); // offset from top-left corner
    
    // Reference to the main manager
    private SolarSystemParallaxManager parallaxManager;
    
    // Internal state
    private bool isVRMode = false;
    private bool wasVRMode = false;
    
    // VR input state tracking
    private bool autopilotTriggerWasPressed = false;
    private bool planetInfoTriggerWasPressed = false;
    private bool vrSelectWasPressed = false;
    
    // Public accessors
    public bool EnableLabels => enableLabels;
    public Canvas LabelCanvas => labelCanvas;
    public TMP_FontAsset LabelFont => labelFont;
    public int LabelFontSize => labelFontSize;
    public Color LabelColor => labelColor;
    public float LabelOffsetPixels => labelOffsetPixels;
    public bool IsVRMode => isVRMode;
    
    // Events for input notifications - ParallaxManager subscribes to respond to input
    public System.Action OnAutopilotTogglePressed;
    public System.Action OnPlanetInfoTogglePressed;
    
    private void Awake()
    {
        parallaxManager = GetComponent<SolarSystemParallaxManager>();
    }
    
    private void OnEnable()
    {
        if (autopilotMenuAction != null) autopilotMenuAction.action.Enable();
        if (planetInfoAction != null) planetInfoAction.action.Enable();
        if (menuScrollAction != null) menuScrollAction.action.Enable();
        if (menuSelectAction != null) menuSelectAction.action.Enable();
    }
    
    private void OnDisable()
    {
        if (autopilotMenuAction != null) autopilotMenuAction.action.Disable();
        if (planetInfoAction != null) planetInfoAction.action.Disable();
        if (menuScrollAction != null) menuScrollAction.action.Disable();
        if (menuSelectAction != null) menuSelectAction.action.Disable();
    }
    
    /// <summary>
    /// Updates input handling for VR controllers and keyboard. Call from Update().
    /// </summary>
    public void UpdateInput()
    {
        // Handle autopilot toggle with X key or VR controller button
        bool autopilotTogglePressed = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame);
        
        // Check VR controller trigger (it's an axis, so we need to detect edge)
        if (autopilotMenuAction != null && autopilotMenuAction.action.enabled)
        {
            float triggerValue = autopilotMenuAction.action.ReadValue<float>();
            bool triggerIsPressed = triggerValue > 0.5f;
            
            // Detect rising edge (trigger just pressed)
            if (triggerIsPressed && !autopilotTriggerWasPressed)
            {
                autopilotTogglePressed = true;
            }
            autopilotTriggerWasPressed = triggerIsPressed;
        }
        
        if (autopilotTogglePressed)
        {
            OnAutopilotTogglePressed?.Invoke();
        }
        
        // Handle planet info toggle with I key or VR controller button
        bool planetInfoTogglePressed = (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame);
        
        // Check VR controller trigger for planet info
        if (planetInfoAction != null && planetInfoAction.action.enabled)
        {
            float triggerValue = planetInfoAction.action.ReadValue<float>();
            bool triggerIsPressed = triggerValue > 0.5f;
            
            // Detect rising edge (trigger just pressed)
            if (triggerIsPressed && !planetInfoTriggerWasPressed)
            {
                planetInfoTogglePressed = true;
            }
            planetInfoTriggerWasPressed = triggerIsPressed;
        }
        
        if (planetInfoTogglePressed)
        {
            OnPlanetInfoTogglePressed?.Invoke();
        }
        
        // Handle VR menu navigation when autopilot menu is open
        if (autopilotMenuOpen)
        {
            HandleVRMenuNavigation();
        }
    }
    
    /// <summary>
    /// Handles VR controller and keyboard input for menu navigation.
    /// </summary>
    private void HandleVRMenuNavigation()
    {
        float scrollInput = 0f;
        bool selectPressed = false;
        
        // VR Controller thumbstick/trackpad (2D axis - use Y component)
        if (menuScrollAction != null && menuScrollAction.action.enabled)
        {
            Vector2 scrollValue = menuScrollAction.action.ReadValue<Vector2>();
            scrollInput = scrollValue.y;
        }
        
        // Keyboard fallback (arrow keys)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame)
            {
                scrollInput = 1f;
            }
            else if (Keyboard.current.downArrowKey.wasPressedThisFrame)
            {
                scrollInput = -1f;
            }
            
            if (Keyboard.current.enterKey.wasPressedThisFrame)
            {
                selectPressed = true;
            }
        }
        
        // VR controller select button (trigger/trackpad press)
        if (menuSelectAction != null && menuSelectAction.action.enabled)
        {
            float selectValue = menuSelectAction.action.ReadValue<float>();
            bool selectIsPressed = selectValue > 0.5f;
            
            // Edge detection for select press
            if (selectIsPressed && !vrSelectWasPressed)
            {
                selectPressed = true;
            }
            vrSelectWasPressed = selectIsPressed;
        }
        
        // Process the input
        UpdateVRMenuNavigation(scrollInput, selectPressed);
    }
    
    /// <summary>
    /// Initialize the UI manager. Call this from SolarSystemParallaxManager.Start().
    /// </summary>
    public void Initialize()
    {
        SetupLabelCanvas();
    }
    
    /// <summary>
    /// Sets up the label canvas based on VR or Desktop mode.
    /// </summary>
    public void SetupLabelCanvas()
    {
        // Use manual VR mode setting from Inspector
        isVRMode = enableVRMode;
        wasVRMode = isVRMode;
        Debug.Log($"[UIManager] VR Mode enabled: {isVRMode}");
        
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
            
            // Add appropriate raycaster based on VR mode
            if (isVRMode)
            {
                // Use TrackedDeviceGraphicRaycaster for VR controller interaction
                canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            else
            {
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            labelCanvas = canvas;
            Debug.Log($"[UIManager] Created automatic label canvas ({(isVRMode ? "World Space for VR with TrackedDeviceGraphicRaycaster" : "Screen Space for Desktop")})");
        }
        else if (labelCanvas != null)
        {
            // Configure existing canvas based on VR mode
            if (isVRMode)
            {
                ConfigureCanvasForVR(labelCanvas);
                
                // Ensure TrackedDeviceGraphicRaycaster exists for VR controller interaction
                if (labelCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    // Remove standard GraphicRaycaster if present (they conflict)
                    var oldRaycaster = labelCanvas.GetComponent<GraphicRaycaster>();
                    if (oldRaycaster != null && !(oldRaycaster is TrackedDeviceGraphicRaycaster))
                    {
                        Destroy(oldRaycaster);
                    }
                    labelCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                    Debug.Log("[UIManager] Added TrackedDeviceGraphicRaycaster to existing canvas for VR controller interaction");
                }
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
            
            if (isVRMode)
            {
                // Use XRUIInputModule for VR controller input
                eventSystemGO.AddComponent<XRUIInputModule>();
                Debug.Log("[UIManager] Created EventSystem with XRUIInputModule for VR controller interaction");
            }
            else
            {
                eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                Debug.Log("[UIManager] Created EventSystem for UI interaction");
            }
        }
        else if (isVRMode)
        {
            // Ensure VR UI input module is present if EventSystem already exists
            var existingEventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (existingEventSystem.GetComponent<XRUIInputModule>() == null)
            {
                // Remove existing input module if present
                var existingInputModule = existingEventSystem.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                if (existingInputModule != null)
                {
                    Destroy(existingInputModule);
                }
                existingEventSystem.gameObject.AddComponent<XRUIInputModule>();
                Debug.Log("[UIManager] Added XRUIInputModule to existing EventSystem for VR controller interaction");
            }
        }
    }
    
    /// <summary>
    /// Configures a Canvas for VR (World Space).
    /// </summary>
    public void ConfigureCanvasForVR(Canvas canvas)
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
        
        Debug.Log("[UIManager] Canvas configured for VR (World Space)");
    }
    
    /// <summary>
    /// Configures a Canvas for Desktop (Screen Space Overlay).
    /// </summary>
    public void ConfigureCanvasForDesktop(Canvas canvas)
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
        
        Debug.Log("[UIManager] Canvas configured for Desktop (Screen Space Overlay)");
    }
    
    /// <summary>
    /// Creates a UI label for a body. Returns the label GameObject.
    /// </summary>
    /// <param name="bodyName">Name to display on the label</param>
    /// <returns>The created label GameObject, or null if labels are disabled</returns>
    public GameObject CreateLabelForBody(string bodyName)
    {
        if (!enableLabels)
        {
            return null;
        }
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Label Canvas not assigned. Please assign a Canvas for labels.");
            return null;
        }

        // Create UI GameObject
        GameObject labelGO = new GameObject(bodyName + "_Label");
        labelGO.transform.SetParent(labelCanvas.transform, false);

        // Add RectTransform
        RectTransform rectTransform = labelGO.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(200, 30);

        // Add Text component
        TextMeshProUGUI textComponent = labelGO.AddComponent<TextMeshProUGUI>();
        textComponent.text = bodyName;
        if (labelFont != null) textComponent.font = labelFont;
        textComponent.fontSize = labelFontSize;
        textComponent.color = labelColor;
        textComponent.alignment = TextAlignmentOptions.Left;
        textComponent.textWrappingMode = TextWrappingModes.NoWrap;
        textComponent.overflowMode = TextOverflowModes.Overflow;

        return labelGO;
    }
    
    /// <summary>
    /// Updates the World Space canvas position to follow the VR camera.
    /// Call this from Update() when in VR mode.
    /// </summary>
    /// <param name="cam">The active camera</param>
    /// previously UpdateVRCanvas
    public void UpdateVRCanvasPosition(Camera cam)
    {
        // Only update canvas position in VR mode
        if (!isVRMode) return;
        
        if (labelCanvas == null || cam == null) return;
        
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
    
    /// <summary>
    /// Data structure for label visibility calculations.
    /// </summary>
    public struct LabelVisibilityData
    {
        public GameObject labelUI;
        public Vector2 screenPos;
        public float distanceAu;
        public Rect labelBounds;
        public bool isPriority; // e.g., Proxima system bodies
    }
    
    /// <summary>
    /// Updates label positions with collision detection to prevent overlapping labels.
    /// </summary>
    /// <param name="visibleLabels">List of labels with their visibility data</param>
    public void UpdateLabelPositions(List<LabelVisibilityData> visibleLabels)
    {
        if (!enableLabels || labelCanvas == null) return;
        
        // Sort by priority: Priority items first, then by distance (closest first)
        visibleLabels.Sort((a, b) => 
        {
            // Priority items always come first
            if (a.isPriority && !b.isPriority) return -1;
            if (!a.isPriority && b.isPriority) return 1;
            
            // Otherwise sort by distance (closest first)
            return a.distanceAu.CompareTo(b.distanceAu);
        });
        
        List<Rect> occupiedRects = new List<Rect>();
        
        foreach (var labelData in visibleLabels)
        {
            if (labelData.labelUI == null) continue;
            
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
                    RectTransform labelRect = labelData.labelUI.GetComponent<RectTransform>();
                    labelRect.localPosition = offsetPos;
                    labelData.labelUI.SetActive(true);
                    occupiedRects.Add(offsetBounds);
                }
                else
                {
                    // No spot found - check if this is a priority object
                    if (labelData.isPriority)
                    {
                        // Priority labels are always shown, even if overlapping
                        RectTransform labelRect = labelData.labelUI.GetComponent<RectTransform>();
                        labelRect.localPosition = labelData.screenPos;
                        labelData.labelUI.SetActive(true);
                        occupiedRects.Add(labelData.labelBounds);
                    }
                    else
                    {
                        // No spot found - hide this label
                        labelData.labelUI.SetActive(false);
                    }
                }
            }
            else
            {
                // No overlap - show label at original position
                RectTransform labelRect = labelData.labelUI.GetComponent<RectTransform>();
                labelRect.localPosition = labelData.screenPos;
                labelData.labelUI.SetActive(true);
                occupiedRects.Add(labelData.labelBounds);
            }
        }
    }
    
    // ========================
    // PLANET INFO PANEL
    // ========================
    
    [Header("Planet Info Panel")]
    [Tooltip("Animation speed for planet info panel slide")]
    [SerializeField] private float planetInfoAnimSpeed = 8f;
    
    // Planet info UI elements
    private GameObject planetInfoUI;
    private TextMeshProUGUI planetInfoNameText;
    private TextMeshProUGUI planetInfoDataText;
    private float planetInfoAnimProgress = 0f;
    private bool planetInfoVisible = false;
    
    // Public accessor for visibility state
    public bool IsPlanetInfoVisible => planetInfoVisible;
    
    /// <summary>
    /// Data structure for planet information display.
    /// </summary>
    public class PlanetInfoData
    {
        public string Name;
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
        public float RadiusKm; // Fallback if no data
    }
    
    /// <summary>
    /// Creates the planet info panel UI. Call from Start().
    /// </summary>
    public void CreatePlanetInfoPanel()
    {
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Cannot create Planet Info Panel: Label Canvas not available.");
            return;
        }
        
        // Create main panel - positioned off-screen to the bottom initially
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
        
        Debug.Log("[UIManager] Planet info panel created successfully");
    }
    
    /// <summary>
    /// Shows the planet info panel with the specified data.
    /// </summary>
    public void ShowPlanetInfo(PlanetInfoData data)
    {
        if (planetInfoUI == null) return;
        
        planetInfoVisible = true;
        planetInfoUI.SetActive(true);
        PopulatePlanetInfo(data);
        
        Debug.Log($"[UIManager] Showing planet info for: {data.Name}");
    }
    
    /// <summary>
    /// Hides the planet info panel.
    /// </summary>
    public void HidePlanetInfo()
    {
        planetInfoVisible = false;
    }
    
    /// <summary>
    /// Toggles the planet info panel visibility.
    /// </summary>
    /// <returns>The new visibility state</returns>
    public bool TogglePlanetInfo()
    {
        planetInfoVisible = !planetInfoVisible;
        
        if (planetInfoVisible && planetInfoUI != null)
        {
            planetInfoUI.SetActive(true);
        }
        
        return planetInfoVisible;
    }
    
    /// <summary>
    /// Populates the planet info panel with data.
    /// </summary>
    private void PopulatePlanetInfo(PlanetInfoData data)
    {
        if (planetInfoNameText == null || planetInfoDataText == null) return;
        
        planetInfoNameText.text = data.Name.ToUpper();
        
        if (!string.IsNullOrEmpty(data.Diameter))
        {
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
                $"<color=#88CCFF>Radius:</color>  {data.RadiusKm:N0} km";
        }
    }
    
    /// <summary>
    /// Updates the planet info panel animation. Call from Update().
    /// </summary>
    public void UpdatePlanetInfoPanel()
    {
        if (planetInfoUI == null) return;
        
        // Animate panel position
        float targetProgress = planetInfoVisible ? 1f : 0f;
        planetInfoAnimProgress = Mathf.MoveTowards(planetInfoAnimProgress, targetProgress, Time.deltaTime * planetInfoAnimSpeed);
        
        UpdatePlanetInfoPanelPosition();
        
        // Hide GameObject if fully closed
        if (!planetInfoVisible && planetInfoAnimProgress <= 0.01f)
        {
            if (planetInfoUI.activeSelf) 
                planetInfoUI.SetActive(false);
        }
    }
    
    /// <summary>
    /// Updates the planet info panel position based on animation progress.
    /// </summary>
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
    
    // ========================
    // AUTOPILOT MENU
    // ========================
    
    // Autopilot menu UI elements
    private GameObject autopilotUI;
    private List<Button> autopilotButtons = new List<Button>();
    private bool autopilotMenuOpen = false;
    private bool moonsExpanded = false;
    private GameObject moonsContainer;
    private RectTransform autopilotContentRect;
    
    // VR menu navigation
    private int menuSelectedIndex = 0;
    private float menuScrollCooldown = 0f;
    private const float MENU_SCROLL_DELAY = 0.2f;
    
    // Static properties for other scripts
    public static bool IsMenuOpen { get; private set; } = false;
    
    // Public accessors
    public bool AutopilotMenuOpen => autopilotMenuOpen;
    
    // Events for menu interactions - ParallaxManager subscribes to these
    public System.Action<AutopilotBodyInfo> OnAutopilotTargetSelected;
    public System.Action OnAutopilotMenuToggled;
    public System.Action OnAutopilotCancelled;
    
    /// <summary>
    /// Data structure for body information passed to the menu.
    /// </summary>
    public class AutopilotBodyInfo
    {
        public string name;
        public long naifId;
        public bool isMoon;
        public bool isProximaSystem;
        public object bodyReference; // Reference back to BodyInstance
    }
    
    // Cached body info for the menu
    private List<AutopilotBodyInfo> menuBodies = new List<AutopilotBodyInfo>();
    private List<AutopilotBodyInfo> menuSelectableBodies = new List<AutopilotBodyInfo>();
    
    /// <summary>
    /// Creates the autopilot menu UI. Call from Start() after the body list is loaded.
    /// </summary>
    /// <param name="bodies">List of bodies to populate the menu with</param>
    public void CreateAutopilotMenu(List<AutopilotBodyInfo> bodies)
    {
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Cannot create Autopilot Menu: Label Canvas not available.");
            return;
        }
        
        menuBodies = bodies;
        
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
        
        // Create scroll view for body list
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
        
        // Viewport - leave room for scrollbar
        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollViewGO.transform, false);
        RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = new Vector2(-15, 0);
        
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
        CreateScrollbar(scrollViewGO, scroll);
        
        // Separate bodies into main bodies and moons
        List<AutopilotBodyInfo> mainBodies = new List<AutopilotBodyInfo>();
        List<AutopilotBodyInfo> moons = new List<AutopilotBodyInfo>();
        
        foreach (var body in bodies)
        {
            if (body.isMoon)
                moons.Add(body);
            else
                mainBodies.Add(body);
        }
        
        // Sort main bodies: Proxima system first, then Sun, then by position (already sorted by caller)
        mainBodies.Sort((a, b) =>
        {
            if (a.isProximaSystem && !b.isProximaSystem) return -1;
            if (!a.isProximaSystem && b.isProximaSystem) return 1;
            bool aIsSun = a.naifId == 10;
            bool bIsSun = b.naifId == 10;
            if (aIsSun && !bIsSun) return -1;
            if (!aIsSun && bIsSun) return 1;
            return 0;
        });
        
        // Add buttons for main bodies
        float buttonHeight = 50f;
        float spacing = 8f;
        int itemIndex = 0;
        
        menuSelectableBodies.Clear();
        autopilotButtons.Clear();
        
        foreach (var body in mainBodies)
        {
            CreateAutopilotButton(contentGO, body, itemIndex, buttonHeight, spacing, false);
            menuSelectableBodies.Add(body);
            itemIndex++;
        }
        
        // Moons category header
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
        
        // Calculate content size
        int visibleCount = mainBodies.Count + (moons.Count > 0 ? 1 : 0);
        autopilotContentRect.sizeDelta = new Vector2(0, visibleCount * (buttonHeight + spacing));
        
        // Cancel button
        CreateCancelButton();
        
        // Start hidden
        autopilotUI.SetActive(false);
        
        Debug.Log($"[UIManager] Autopilot menu created: {mainBodies.Count} planets, {moons.Count} moons");
    }
    
    private void CreateScrollbar(GameObject scrollViewGO, ScrollRect scroll)
    {
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
        
        // Handle area
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
    }
    
    private void CreateAutopilotButton(GameObject parent, AutopilotBodyInfo body, int index, float buttonHeight, float spacing, bool isMoon)
    {
        GameObject buttonGO = new GameObject(body.name + "_Button");
        buttonGO.transform.SetParent(parent.transform, false);
        
        RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0, 1);
        buttonRect.anchorMax = new Vector2(1, 1);
        buttonRect.pivot = new Vector2(0.5f, 1);
        buttonRect.anchoredPosition = new Vector2(0, -index * (buttonHeight + spacing));
        buttonRect.sizeDelta = new Vector2(isMoon ? -40 : -20, buttonHeight);
        
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
        
        AutopilotBodyInfo capturedBody = body;
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
        buttonImage.color = new Color(0.2f, 0.15f, 0.3f, 1f);
        
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
        
        // Button text with arrow
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
        btnText.color = new Color(0.9f, 0.8f, 1f, 1f);
        btnText.alignment = TextAlignmentOptions.Left;
    }
    
    private void ToggleMoonsCategory()
    {
        moonsExpanded = !moonsExpanded;
        
        if (moonsContainer != null)
        {
            moonsContainer.SetActive(moonsExpanded);
            
            // Update arrow in category button
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
            
            // Recalculate content size
            float buttonHeight = 50f;
            float spacing = 8f;
            
            int mainCount = 0;
            int moonCount = 0;
            foreach (var body in menuBodies)
            {
                if (body.isMoon) moonCount++;
                else mainCount++;
            }
            
            int visibleCount = mainCount + 1; // +1 for moons category header
            if (moonsExpanded)
            {
                visibleCount += moonCount;
            }
            
            autopilotContentRect.sizeDelta = new Vector2(0, visibleCount * (buttonHeight + spacing));
            
            // Reposition moons container
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
    
    /// <summary>
    /// Toggles the autopilot menu visibility.
    /// </summary>
    /// <param name="forceClose">If true, force close the menu (used when autopilot is cancelled)</param>
    public void ToggleAutopilotMenu(bool forceClose = false)
    {
        if (autopilotUI == null) return;
        
        if (forceClose)
        {
            autopilotMenuOpen = false;
            autopilotUI.SetActive(false);
            IsMenuOpen = false;
            OnAutopilotCancelled?.Invoke();
            return;
        }
        
        // Mutual exclusion: Close Planet Info if open
        if (planetInfoVisible)
        {
            HidePlanetInfo();
        }
        
        autopilotMenuOpen = !autopilotMenuOpen;
        autopilotUI.SetActive(autopilotMenuOpen);
        IsMenuOpen = autopilotMenuOpen;
        
        // Reset selection when opening
        if (autopilotMenuOpen)
        {
            menuSelectedIndex = 0;
            UpdateMenuSelectionHighlight();
        }
        
        OnAutopilotMenuToggled?.Invoke();
    }
    
    /// <summary>
    /// Shows the autopilot menu.
    /// </summary>
    public void ShowAutopilotMenu()
    {
        if (autopilotUI == null) return;
        
        if (planetInfoVisible)
        {
            HidePlanetInfo();
        }
        
        autopilotMenuOpen = true;
        autopilotUI.SetActive(true);
        IsMenuOpen = true;
        menuSelectedIndex = 0;
        UpdateMenuSelectionHighlight();
    }
    
    /// <summary>
    /// Hides the autopilot menu.
    /// </summary>
    public void HideAutopilotMenu()
    {
        if (autopilotUI == null) return;
        
        autopilotMenuOpen = false;
        autopilotUI.SetActive(false);
        IsMenuOpen = false;
    }
    
    private void SelectAutopilotTarget(AutopilotBodyInfo body)
    {
        // Close the menu
        autopilotMenuOpen = false;
        IsMenuOpen = false;
        if (autopilotUI != null)
            autopilotUI.SetActive(false);
        
        // Notify listeners
        OnAutopilotTargetSelected?.Invoke(body);
        
        Debug.Log($"[UIManager] Autopilot destination selected: {body.name}");
    }
    
    /// <summary>
    /// Updates VR menu navigation. Call from Update() when menu is open.
    /// </summary>
    /// <param name="scrollInput">Scroll input value (-1 to 1)</param>
    /// <param name="selectPressed">Whether the select button was pressed this frame</param>
    public void UpdateVRMenuNavigation(float scrollInput, bool selectPressed)
    {
        if (!autopilotMenuOpen || menuSelectableBodies.Count == 0) return;
        
        // Decrease scroll cooldown
        if (menuScrollCooldown > 0)
        {
            menuScrollCooldown -= Time.deltaTime;
        }
        
        // Process scroll input
        if (menuScrollCooldown <= 0 && Mathf.Abs(scrollInput) > 0.5f)
        {
            int previousIndex = menuSelectedIndex;
            
            if (scrollInput > 0.5f)
            {
                menuSelectedIndex = Mathf.Max(0, menuSelectedIndex - 1);
            }
            else if (scrollInput < -0.5f)
            {
                menuSelectedIndex = Mathf.Min(menuSelectableBodies.Count - 1, menuSelectedIndex + 1);
            }
            
            if (menuSelectedIndex != previousIndex)
            {
                menuScrollCooldown = MENU_SCROLL_DELAY;
                UpdateMenuSelectionHighlight();
            }
        }
        
        // Confirm selection
        if (selectPressed && menuSelectedIndex >= 0 && menuSelectedIndex < menuSelectableBodies.Count)
        {
            SelectAutopilotTarget(menuSelectableBodies[menuSelectedIndex]);
        }
    }
    
    private void UpdateMenuSelectionHighlight()
    {
        // Update visual highlight on all buttons
        for (int i = 0; i < autopilotButtons.Count && i < menuSelectableBodies.Count; i++)
        {
            Button btn = autopilotButtons[i];
            if (btn == null) continue;
            
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage != null)
            {
                if (i == menuSelectedIndex)
                {
                    btnImage.color = new Color(0.3f, 0.6f, 1f, 1f); // Highlighted
                }
                else
                {
                    btnImage.color = new Color(0.15f, 0.25f, 0.4f, 1f); // Normal
                }
            }
        }
        
        // Scroll list to keep selection visible
        if (autopilotContentRect != null && menuSelectedIndex >= 0)
        {
            float buttonHeight = 50f;
            float spacing = 8f;
            float itemHeight = buttonHeight + spacing;
            float scrollPosition = menuSelectedIndex * itemHeight;
            
            ScrollRect scrollRect = autopilotUI?.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null)
            {
                float contentHeight = autopilotContentRect.sizeDelta.y;
                float viewportHeight = scrollRect.viewport.rect.height;
                
                if (contentHeight > viewportHeight)
                {
                    float normalizedPosition = 1f - (scrollPosition / (contentHeight - viewportHeight));
                    scrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
                }
            }
        }
    }
    
    // ========================
    // LOADING SCREEN
    // ========================
    
    [Header("Loading Screen")]
    [Tooltip("Show loading screen while stellar data is being loaded")]
    [SerializeField] private bool enableLoadingScreen = true;
    
    // Loading screen UI elements
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
    
    // Reference to HUD (for hiding during loading)
    private GameObject hudReference;
    
    // Public accessors
    public bool IsLoadingComplete => loadingComplete;
    public bool EnableLoadingScreen => enableLoadingScreen;
    
    // Events
    public System.Action OnLoadingComplete;
    
    /// <summary>
    /// Creates the loading screen UI.
    /// </summary>
    /// <param name="hudUI">Reference to HUD to hide during loading</param>
    public void CreateLoadingScreen(GameObject hudUI)
    {
        hudReference = hudUI;
        
        if (!enableLoadingScreen)
        {
            loadingComplete = true;
            OnLoadingComplete?.Invoke();
            return;
        }
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Cannot create loading screen: Label Canvas not available.");
            loadingComplete = true;
            OnLoadingComplete?.Invoke();
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
        loadingText.color = new Color(0.8f, 0.9f, 1f, 1f);
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
        
        // Progress bar background
        GameObject progressBgGO = new GameObject("ProgressBarBackground");
        progressBgGO.transform.SetParent(progressContainer.transform, false);
        RectTransform progressBgRect = progressBgGO.AddComponent<RectTransform>();
        progressBgRect.anchorMin = Vector2.zero;
        progressBgRect.anchorMax = Vector2.one;
        progressBgRect.offsetMin = Vector2.zero;
        progressBgRect.offsetMax = Vector2.zero;
        
        progressBarBackground = progressBgGO.AddComponent<Image>();
        progressBarBackground.color = new Color(0.1f, 0.12f, 0.18f, 1f);
        
        // Progress bar fill
        GameObject progressFillGO = new GameObject("ProgressBarFill");
        progressFillGO.transform.SetParent(progressContainer.transform, false);
        RectTransform progressFillRect = progressFillGO.AddComponent<RectTransform>();
        progressFillRect.anchorMin = Vector2.zero;
        progressFillRect.anchorMax = new Vector2(0f, 1f);
        progressFillRect.pivot = new Vector2(0f, 0.5f);
        progressFillRect.offsetMin = new Vector2(2, 2);
        progressFillRect.offsetMax = new Vector2(-2, -2);
        
        progressBarFill = progressFillGO.AddComponent<Image>();
        progressBarFill.color = new Color(0.3f, 0.6f, 1f, 1f);
        
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
        if (hudReference != null)
        {
            hudReference.SetActive(false);
        }
        
        Debug.Log("[UIManager] Loading screen created with progress bar");
    }
    
    /// <summary>
    /// Updates the loading screen progress. Call from Update().
    /// </summary>
    /// <param name="dataReady">True when stellar data has finished loading</param>
    /// <param name="starCount">Current number of loaded stars</param>
    public void UpdateLoadingScreen(bool dataReady, int starCount)
    {
        if (loadingScreenUI == null) return;
        
        float progress = Mathf.Clamp01((float)starCount / ESTIMATED_TOTAL_STARS);
        
        if (dataReady && !loadingComplete)
        {
            // Data is ready, start fading out
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
                if (hudReference != null)
                {
                    hudReference.SetActive(true);
                }
                
                OnLoadingComplete?.Invoke();
                Debug.Log("[UIManager] Loading complete - stars ready!");
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
        else if (!dataReady)
        {
            // Animate loading text with dots
            int dotCount = (int)(Time.time * 2f) % 4;
            string dots = new string('.', dotCount);
            loadingText.text = $"Loading Stellar Data{dots}";
            
            // Update progress bar fill
            if (progressBarFill != null)
            {
                RectTransform fillRect = progressBarFill.GetComponent<RectTransform>();
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
    
    // ========================
    // HUD (HEADS-UP DISPLAY)
    // ========================
    
    // HUD UI elements
    private GameObject hudUI;
    private TextMeshProUGUI hudText;
    
    // Public accessors
    public GameObject HudUI => hudUI;
    public bool EnableHUD => enableHUD;
    public TextMeshProUGUI HudText => hudText;
    
    /// <summary>
    /// Creates the HUD display.
    /// </summary>
    public void CreateHUD()
    {
        if (!enableHUD) return;
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Cannot create HUD: Label Canvas not available. HUD requires a canvas.");
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
        rectTransform.anchoredPosition = hudPosition;
        rectTransform.sizeDelta = new Vector2(800, 400);
        
        // Add Text component
        hudText = hudUI.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) hudText.font = labelFont;
        hudText.fontSize = hudFontSize;
        hudText.color = hudColor;
        hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.textWrappingMode = TextWrappingModes.NoWrap;
        hudText.overflowMode = TextOverflowModes.Overflow;
        
        // Initial text
        hudText.text = "Speed: 0 km/s (0% lightspeed)\nDistance from Sun: 0 km\nMode: DISTANCE-BASED";
        
        Debug.Log("[UIManager] HUD created successfully");
    }
    
    /// <summary>
    /// Sets the HUD display text.
    /// </summary>
    /// <param name="text">Text to display</param>
    public void SetHUDText(string text)
    {
        if (hudText != null)
        {
            hudText.text = text;
        }
    }
    
    /// <summary>
    /// Shows or hides the HUD.
    /// </summary>
    /// <param name="visible">True to show, false to hide</param>
    public void SetHUDVisible(bool visible)
    {
        if (hudUI != null)
        {
            hudUI.SetActive(visible);
        }
    }
}
