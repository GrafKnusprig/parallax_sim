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
    
    [Tooltip("VR Controller button to toggle orbit mode (e.g., Right Hand Menu/Primary button).")]
    [SerializeField] private InputActionReference orbitAction;
    
    [Header("HUD (Heads-Up Display)")]
    [SerializeField] private bool enableHUD = true;
    [SerializeField] private int hudFontSize = 28;
    [SerializeField] private Color hudColor = Color.cyan;
    [SerializeField] private Vector2 hudPosition = new Vector2(300f, -100f); // offset from top-left corner
    
    // Reference to the main manager
    private SolarSystemParallaxManager parallaxManager;
    
    // Internal state
    private bool wasVRMode = false;
    
    // VR input state tracking
    private bool autopilotTriggerWasPressed = false;
    private bool planetInfoTriggerWasPressed = false;
    private bool orbitTriggerWasPressed = false;
    private bool vrSelectWasPressed = false;
    
    // Public accessors
    public bool EnableLabels => enableLabels;
    public Canvas LabelCanvas => labelCanvas;
    public TMP_FontAsset LabelFont => labelFont;
    public int LabelFontSize => labelFontSize;
    public Color LabelColor => labelColor;
    public float LabelOffsetPixels => labelOffsetPixels;
    public bool IsVRMode => enableVRMode;
    
    // Events for input notifications - ParallaxManager subscribes to respond to input
    public System.Action OnAutopilotTogglePressed;
    public System.Action OnPlanetInfoTogglePressed;
    public System.Action OnOrbitTogglePressed;
    
    private void Awake()
    {
        parallaxManager = GetComponent<SolarSystemParallaxManager>();
    }
    
    private void OnEnable()
    {
        // Only enable VR actions if in VR mode
        if (enableVRMode)
        {
            if (autopilotMenuAction != null) autopilotMenuAction.action.Enable();
            if (planetInfoAction != null) planetInfoAction.action.Enable();
            if (menuScrollAction != null) menuScrollAction.action.Enable();
            if (menuSelectAction != null) menuSelectAction.action.Enable();
            if (orbitAction != null) orbitAction.action.Enable();
        }
    }
    
    private void OnDisable()
    {
        if (autopilotMenuAction != null) autopilotMenuAction.action.Disable();
        if (planetInfoAction != null) planetInfoAction.action.Disable();
        if (menuScrollAction != null) menuScrollAction.action.Disable();
        if (menuSelectAction != null) menuSelectAction.action.Disable();
        if (orbitAction != null) orbitAction.action.Disable();
    }
    
    /// <summary>
    /// Updates input handling for VR controllers and keyboard. Call from Update().
    /// </summary>
    public void UpdateInput()
    {
        // Handle runtime VR mode toggle in Inspector
        if (enableVRMode != wasVRMode)
        {
            SetupLabelCanvas();
        }

        // Handle autopilot toggle with X key or VR controller button
        bool autopilotTogglePressed = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame);
        
        // Check VR controller trigger (only in VR mode)
        if (enableVRMode && autopilotMenuAction != null && autopilotMenuAction.action.enabled)
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
        
        // Check VR controller trigger for planet info (only in VR mode)
        if (enableVRMode && planetInfoAction != null && planetInfoAction.action.enabled)
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
        
        // Handle orbit toggle with O key
        bool orbitTogglePressed = (Keyboard.current != null && Keyboard.current.oKey.wasPressedThisFrame);
        
        // Check VR controller toggle for orbit (only in VR mode)
        if (enableVRMode && orbitAction != null && orbitAction.action.enabled)
        {
            float triggerValue = orbitAction.action.ReadValue<float>();
            bool triggerIsPressed = triggerValue > 0.5f;
            
            // Detect rising edge (button just pressed)
            if (triggerIsPressed && !orbitTriggerWasPressed)
            {
                orbitTogglePressed = true;
            }
            orbitTriggerWasPressed = triggerIsPressed;
        }
        
        if (orbitTogglePressed)
        {
            OnOrbitTogglePressed?.Invoke();
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
        // Only process VR navigation if in VR mode (keyboard fallback remains)
        float scrollInput = 0f;
        bool selectPressed = false;
        
        // VR Controller thumbstick/trackpad (only in VR mode)
        if (enableVRMode && menuScrollAction != null && menuScrollAction.action.enabled)
        {
            Vector2 scrollValue = menuScrollAction.action.ReadValue<Vector2>();
            scrollInput = scrollValue.y;
        }
        
        // Keyboard fallback (always active)
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
        
        // VR controller select button (only in VR mode)
        if (enableVRMode && menuSelectAction != null && menuSelectAction.action.enabled)
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
        // Sync state
        wasVRMode = enableVRMode;
        Debug.Log($"[UIManager] VR Mode enabled: {enableVRMode}");

        // Sync input action states
        if (enableVRMode)
        {
            if (autopilotMenuAction != null) autopilotMenuAction.action.Enable();
            if (planetInfoAction != null) planetInfoAction.action.Enable();
            if (menuScrollAction != null) menuScrollAction.action.Enable();
            if (menuSelectAction != null) menuSelectAction.action.Enable();
        }
        else
        {
            if (autopilotMenuAction != null) autopilotMenuAction.action.Disable();
            if (planetInfoAction != null) planetInfoAction.action.Disable();
            if (menuScrollAction != null) menuScrollAction.action.Disable();
            if (menuSelectAction != null) menuSelectAction.action.Disable();
        }
        
        if (enableLabels && labelCanvas == null)
        {
            // Create a Canvas for labels if one isn't assigned
            GameObject canvasGO = new GameObject("LabelCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            
            // Configure based on VR mode
            if (enableVRMode)
            {
                ConfigureCanvasForVR(canvas);
            }
            else
            {
                ConfigureCanvasForDesktop(canvas);
            }
            
            // Add appropriate raycaster based on VR mode
            if (enableVRMode)
            {
                // Use TrackedDeviceGraphicRaycaster for VR controller interaction
                canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            else
            {
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            labelCanvas = canvas;
            Debug.Log($"[UIManager] Created automatic label canvas ({(enableVRMode ? "World Space for VR with TrackedDeviceGraphicRaycaster" : "Screen Space for Desktop")})");
        }
        else if (labelCanvas != null)
        {
            // Configure existing canvas based on VR mode
            if (enableVRMode)
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
            
            if (enableVRMode)
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
        else if (enableVRMode)
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
    
    private const float VR_CANVAS_SCALE = 0.0006f;
    
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
        
        // Scale down for world space
        canvas.transform.localScale = Vector3.one * VR_CANVAS_SCALE; // Slightly smaller for better fit
        
        // Remove from any existing parent first to be safe
        canvas.transform.SetParent(null);
        
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
    /// Updates the World Space canvas position and scale to follow the VR camera.
    /// Handles INVERSE SCALING to counteract the camera's dynamic scaling.
    /// Call this from Update() when in VR mode.
    /// </summary>
    /// <param name="cam">The active camera</param>
    public void UpdateVRCanvasPosition(Camera cam)
    {
        // Only update canvas position in VR mode
        if (!enableVRMode) return;
        
        if (labelCanvas == null || cam == null) return;
        
        // 1. Ensure parenting (Lock to head)
        if (labelCanvas.transform.parent != cam.transform)
        {
            labelCanvas.transform.SetParent(cam.transform, false);
            labelCanvas.transform.localRotation = Quaternion.identity;
            Debug.Log($"[UIManager] LabelCanvas parented to {cam.name} for locked HUD behavior.");
        }
        
        // 2. Assign world camera if needed
        if (labelCanvas.worldCamera != cam)
        {
            labelCanvas.worldCamera = cam;
        }

        // 3. Apply INVERSE SCALING
        // The camera scales down (e.g. 0.00001) when near planets.
        // We must scale the canvas UP by the inverse amount to keep it physically constant for the user.
        float cameraScale = cam.transform.localScale.x;
        
        // Avoid division by zero
        if (cameraScale < 0.0000001f) cameraScale = 0.0000001f;
        
        float inverseScale = 1.0f / cameraScale;
        
        // Apply scale: Base Scale * Inverse Scale
        labelCanvas.transform.localScale = Vector3.one * VR_CANVAS_SCALE * inverseScale;
        
        // Apply position: Base Distance * Inverse Scale
        // If we didn't scale the position, the canvas would be 0.6 * 0.00001 = 0.000006 units away (inside the eye)
        labelCanvas.transform.localPosition = new Vector3(0, 0, 0.6f * inverseScale);
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
        
        // === BACKGROUND PANEL === (Match HUD style)
        Image panelImage = planetInfoUI.AddComponent<Image>();
        panelImage.color = new Color(0.02f, 0.05f, 0.12f, 0.85f); // Same as HUD background
        
        // === BORDER FRAME === (Match HUD style)
        // Top border
        CreateBorderEdge(planetInfoUI.transform, "BorderTop", new Vector2(0, 1), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(0, -3), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Bottom border
        CreateBorderEdge(planetInfoUI.transform, "BorderBottom", new Vector2(0, 0), new Vector2(1, 0), 
            new Vector2(0, 3), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Left border
        CreateBorderEdge(planetInfoUI.transform, "BorderLeft", new Vector2(0, 0), new Vector2(0, 1), 
            new Vector2(3, 0), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Right border
        CreateBorderEdge(planetInfoUI.transform, "BorderRight", new Vector2(1, 0), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(-3, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        
        // === CORNER BRACKETS === (Match HUD style)
        Color bracketColor = new Color(0.3f, 0.9f, 1f, 1f); // Bright cyan
        float bracketSize = 35f; // Slightly larger for bigger panel
        float bracketThickness = 3f;
        
        // Top-left corner
        CreateCornerBracket(planetInfoUI.transform, "CornerTL", new Vector2(0, 1), bracketSize, bracketThickness, bracketColor, true, true);
        // Top-right corner
        CreateCornerBracket(planetInfoUI.transform, "CornerTR", new Vector2(1, 1), bracketSize, bracketThickness, bracketColor, false, true);
        // Bottom-left corner
        CreateCornerBracket(planetInfoUI.transform, "CornerBL", new Vector2(0, 0), bracketSize, bracketThickness, bracketColor, true, false);
        // Bottom-right corner
        CreateCornerBracket(planetInfoUI.transform, "CornerBR", new Vector2(1, 0), bracketSize, bracketThickness, bracketColor, false, false);
        
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
        planetInfoNameText.color = new Color(0.4f, 0.9f, 1f, 1f); // Bright cyan (match HUD)
        planetInfoNameText.alignment = TextAlignmentOptions.Center;
        planetInfoNameText.characterSpacing = 3f; // Add spacing like HUD header
        
        // === HEADER LINE === (Match HUD style)
        GameObject headerLine = new GameObject("HeaderLine");
        headerLine.transform.SetParent(planetInfoUI.transform, false);
        RectTransform headerLineRect = headerLine.AddComponent<RectTransform>();
        headerLineRect.anchorMin = new Vector2(0, 1);
        headerLineRect.anchorMax = new Vector2(1, 1);
        headerLineRect.pivot = new Vector2(0.5f, 1);
        headerLineRect.anchoredPosition = new Vector2(0, -72);
        headerLineRect.sizeDelta = new Vector2(-30, 1);
        Image headerLineImg = headerLine.AddComponent<Image>();
        headerLineImg.color = new Color(0.2f, 0.6f, 0.8f, 0.5f);
        
        // Create data content section
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(planetInfoUI.transform, false);
        RectTransform contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.offsetMin = new Vector2(25, 50); // More left padding for cleaner look
        contentRect.offsetMax = new Vector2(-25, -85); // Adjusted for header line
        
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
        
        // Hide HUD to prevent overlap
        if (hudUI != null)
        {
            hudUI.SetActive(false);
        }
        
        Debug.Log($"[UIManager] Showing planet info for: {data.Name}");
    }
    
    /// <summary>
    /// Hides the planet info panel.
    /// </summary>
    public void HidePlanetInfo()
    {
        planetInfoVisible = false;
        
        // Show HUD again when closing planet info
        if (hudUI != null && enableHUD)
        {
            hudUI.SetActive(true);
        }
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

    // Structure for navigable items (planets, moons, categories, buttons)
    private struct NavigableItem
    {
        public string name;
        public Button button;
        public System.Action onSelect;
        public bool isCategory;
        public bool isMoon;
    }
    private List<NavigableItem> navigableItems = new List<NavigableItem>();
    
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
        
        // === BACKGROUND PANEL === (Match HUD/Planet Info style)
        Image panelImage = autopilotUI.AddComponent<Image>();
        panelImage.color = new Color(0.02f, 0.05f, 0.12f, 0.85f); // Same as HUD background
        
        // === BORDER FRAME === (Match HUD style)
        // Top border
        CreateBorderEdge(autopilotUI.transform, "BorderTop", new Vector2(0, 1), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(0, -3), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Bottom border
        CreateBorderEdge(autopilotUI.transform, "BorderBottom", new Vector2(0, 0), new Vector2(1, 0), 
            new Vector2(0, 3), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Left border
        CreateBorderEdge(autopilotUI.transform, "BorderLeft", new Vector2(0, 0), new Vector2(0, 1), 
            new Vector2(3, 0), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Right border
        CreateBorderEdge(autopilotUI.transform, "BorderRight", new Vector2(1, 0), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(-3, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        
        // === CORNER BRACKETS === (Match HUD style)
        Color bracketColor = new Color(0.3f, 0.9f, 1f, 1f); // Bright cyan
        float bracketSize = 30f;
        float bracketThickness = 3f;
        
        // Top-left corner
        CreateCornerBracket(autopilotUI.transform, "CornerTL", new Vector2(0, 1), bracketSize, bracketThickness, bracketColor, true, true);
        // Top-right corner
        CreateCornerBracket(autopilotUI.transform, "CornerTR", new Vector2(1, 1), bracketSize, bracketThickness, bracketColor, false, true);
        // Bottom-left corner
        CreateCornerBracket(autopilotUI.transform, "CornerBL", new Vector2(0, 0), bracketSize, bracketThickness, bracketColor, true, false);
        // Bottom-right corner
        CreateCornerBracket(autopilotUI.transform, "CornerBR", new Vector2(1, 0), bracketSize, bracketThickness, bracketColor, false, false);
        
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
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = new Color(0.4f, 0.9f, 1f, 1f); // Match HUD header color
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.characterSpacing = 2f; // Add spacing like HUD header
        
        // === HEADER LINE === (Match HUD/Planet Info style)
        GameObject headerLine = new GameObject("HeaderLine");
        headerLine.transform.SetParent(autopilotUI.transform, false);
        RectTransform headerLineRect = headerLine.AddComponent<RectTransform>();
        headerLineRect.anchorMin = new Vector2(0, 1);
        headerLineRect.anchorMax = new Vector2(1, 1);
        headerLineRect.pivot = new Vector2(0.5f, 1);
        headerLineRect.anchoredPosition = new Vector2(0, -80);
        headerLineRect.sizeDelta = new Vector2(-30, 1);
        Image headerLineImg = headerLine.AddComponent<Image>();
        headerLineImg.color = new Color(0.2f, 0.6f, 0.8f, 0.5f);
        
        // Create scroll view for body list
        GameObject scrollViewGO = new GameObject("ScrollView");
        scrollViewGO.transform.SetParent(autopilotUI.transform, false);
        RectTransform scrollViewRect = scrollViewGO.AddComponent<RectTransform>();
        scrollViewRect.anchorMin = new Vector2(0, 0);
        scrollViewRect.anchorMax = new Vector2(1, 1);
        scrollViewRect.offsetMin = new Vector2(15, 60); // Bottom padding
        scrollViewRect.offsetMax = new Vector2(-15, -90); // Top padding for header line + title
        
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
        
        // Clear buttons and items
        autopilotButtons.Clear();
        
        float buttonHeight = 50f;
        float spacing = 8f;
        int itemIndex = 0;
        
        foreach (var body in mainBodies)
        {
            CreateAutopilotButton(contentGO, body, itemIndex, buttonHeight, spacing, false);
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
        
        // Calculate content size and initial navigation
        RefreshNavigableItems();
        
        // Cancel button
        CreateCancelButton();
        
        // Start hidden
        autopilotUI.SetActive(false);
        
        Debug.Log($"[UIManager] Autopilot menu created: {mainBodies.Count} planets, {moons.Count} moons");
    }

    private void RefreshNavigableItems()
    {
        navigableItems.Clear();
        
        float buttonHeight = 50f;
        float spacing = 8f;
        float itemHeight = buttonHeight + spacing;
        
        // Count how many top-level items we have to correctly position moonsContainer
        int topLevelItemCount = 0;
        
        // Sort autopilotButtons by their sibling index to ensure correct processing order
        // if they share the same parent. 
        List<Button> sortedButtons = new List<Button>(autopilotButtons);
        sortedButtons.RemoveAll(b => b == null);
        
        // Process top-level items (planets and category)
        foreach (var btn in sortedButtons)
        {
            if (!btn.gameObject.activeInHierarchy || btn.transform.parent != autopilotContentRect) continue;
            
            topLevelItemCount++;
            
            if (btn.name == "Moons_Category")
            {
                Button capturedCategory = btn;
                navigableItems.Add(new NavigableItem {
                    name = "Moons Category",
                    button = btn,
                    onSelect = () => ToggleMoonsCategory(),
                    isCategory = true
                });
                
                // If moons are expanded, add the moons from the container
                if (moonsExpanded && moonsContainer != null)
                {
                    // Find moons - they are children of moonsContainer
                    foreach (var moonBtn in sortedButtons)
                    {
                        if (moonBtn.gameObject.activeInHierarchy && moonBtn.transform.parent == moonsContainer.transform)
                        {
                            Button capturedMoon = moonBtn;
                            navigableItems.Add(new NavigableItem {
                                name = moonBtn.name,
                                button = moonBtn,
                                onSelect = () => capturedMoon.onClick.Invoke(),
                                isMoon = true
                            });
                        }
                    }
                }
            }
            else
            {
                Button capturedPlanet = btn;
                navigableItems.Add(new NavigableItem {
                    name = btn.name,
                    button = btn,
                    onSelect = () => capturedPlanet.onClick.Invoke()
                });
            }
        }
        
        // Add cancel button - it's a child of autopilotUI, not content
        foreach (var btn in sortedButtons)
        {
            if (btn != null && btn.name == "CancelButton")
            {
                Button capturedCancel = btn;
                navigableItems.Add(new NavigableItem {
                    name = "Cancel",
                    button = btn,
                    onSelect = () => capturedCancel.onClick.Invoke()
                });
                break;
            }
        }

        // Update content size based on visible items in scroll area
        int visibleInScrollCount = 0;
        foreach (var item in navigableItems)
        {
            if (item.name != "Cancel") visibleInScrollCount++;
        }
        
        if (autopilotContentRect != null)
        {
            autopilotContentRect.sizeDelta = new Vector2(0, visibleInScrollCount * itemHeight + 20f); // Add a small bottom padding
            
            // Reposition moons container correctly below its category button
            if (moonsExpanded && moonsContainer != null)
            {
                // Find index of Moons_Category among top-level buttons to know where it is
                int categoryIndex = 0;
                foreach (Transform child in autopilotContentRect)
                {
                    if (child.name == "Moons_Category") break;
                    // Only count visible planets (activeInHierarchy might not be reliable here if parent is inactive, 
                    // but we know planets are active if we're here)
                    if (child.gameObject.activeSelf) categoryIndex++;
                }
                
                RectTransform moonsRect = moonsContainer.GetComponent<RectTransform>();
                moonsRect.anchoredPosition = new Vector2(0, -(categoryIndex + 1) * itemHeight);
            }
        }
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
        autopilotButtons.Add(button);
        
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
        btnText.text = "[+] Moons";
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
                        txt.text = moonsExpanded ? "[-] Moons" : "[+] Moons";
                    }
                    break;
                }
            }
            
            // Refresh everything through the central logic
            RefreshNavigableItems();
            UpdateMenuSelectionHighlight();
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
        autopilotButtons.Add(cancelBtn);
        
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
            
            // Show HUD again when force closing
            if (hudUI != null && enableHUD)
            {
                hudUI.SetActive(true);
            }
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
        
        // Hide/show HUD to prevent overlap
        if (hudUI != null && enableHUD)
        {
            hudUI.SetActive(!autopilotMenuOpen);
        }
        
        // Reset selection when opening
        if (autopilotMenuOpen)
        {
            RefreshNavigableItems();
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
        RefreshNavigableItems();
        menuSelectedIndex = 0;
        UpdateMenuSelectionHighlight();
        
        // Hide HUD to prevent overlap
        if (hudUI != null && enableHUD)
        {
            hudUI.SetActive(false);
        }
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
        
        // Show HUD again when closing autopilot menu
        if (hudUI != null && enableHUD)
        {
            hudUI.SetActive(true);
        }
    }
    
    private void SelectAutopilotTarget(AutopilotBodyInfo body)
    {
        // Close the menu
        autopilotMenuOpen = false;
        IsMenuOpen = false;
        if (autopilotUI != null)
            autopilotUI.SetActive(false);
        
        // Show HUD again when selecting a target
        if (hudUI != null && enableHUD)
        {
            hudUI.SetActive(true);
        }
        
        // Invoke callback with selected body
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
        if (!autopilotMenuOpen || navigableItems.Count == 0) return;
        
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
                menuSelectedIndex = Mathf.Min(navigableItems.Count - 1, menuSelectedIndex + 1);
            }
            
            if (menuSelectedIndex != previousIndex)
            {
                menuScrollCooldown = MENU_SCROLL_DELAY;
                UpdateMenuSelectionHighlight();
            }
        }
        
        // Confirm selection
        if (selectPressed && menuSelectedIndex >= 0 && menuSelectedIndex < navigableItems.Count)
        {
            navigableItems[menuSelectedIndex].onSelect?.Invoke();
        }
    }
    
    private void UpdateMenuSelectionHighlight()
    {
        // Update visual highlight on all buttons in the navigable list
        for (int i = 0; i < navigableItems.Count; i++)
        {
            Button btn = navigableItems[i].button;
            if (btn == null) continue;
            
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage != null)
            {
                if (i == menuSelectedIndex)
                {
                    // Special colors for categories
                    if (navigableItems[i].isCategory)
                        btnImage.color = new Color(0.45f, 0.35f, 0.7f, 1f);
                    else if (navigableItems[i].isMoon)
                        btnImage.color = new Color(0.25f, 0.5f, 0.9f, 1f);
                    else if (navigableItems[i].name == "Cancel")
                        btnImage.color = new Color(0.8f, 0.3f, 0.3f, 1f);
                    else
                        btnImage.color = new Color(0.3f, 0.6f, 1f, 1f); // Highlighted
                }
                else
                {
                    // Normal colors
                    if (navigableItems[i].isCategory)
                        btnImage.color = new Color(0.2f, 0.15f, 0.3f, 1f);
                    else if (navigableItems[i].isMoon)
                        btnImage.color = new Color(0.12f, 0.2f, 0.35f, 1f);
                    else if (navigableItems[i].name == "Cancel")
                        btnImage.color = new Color(0.5f, 0.2f, 0.2f, 1f);
                    else
                        btnImage.color = new Color(0.15f, 0.25f, 0.4f, 1f); // Normal
                }
            }
        }
        
        // Scroll list to keep selection visible
        if (autopilotContentRect != null && menuSelectedIndex >= 0 && menuSelectedIndex < navigableItems.Count)
        {
            // Don't auto-scroll for the Cancel button as it's outside the scroll view
            if (navigableItems[menuSelectedIndex].name == "Cancel") return;

            float buttonHeight = 50f;
            float spacing = 8f;
            float itemHeight = buttonHeight + spacing;
            
            // Find the index of this item specifically among scrollable items
            int scrollableIndex = 0;
            for (int i = 0; i < menuSelectedIndex; i++)
            {
                if (navigableItems[i].name != "Cancel") scrollableIndex++;
            }

            float scrollPosition = scrollableIndex * itemHeight;
            
            ScrollRect scrollRect = autopilotUI?.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null && scrollRect.viewport != null)
            {
                float contentHeight = autopilotContentRect.sizeDelta.y;
                float viewportHeight = scrollRect.viewport.rect.height;
                
                // Fallback for viewport height if not yet updated by layout system or too small
                if (viewportHeight < 100f) viewportHeight = 550f; 

                float scrollArea = contentHeight - viewportHeight;
                if (scrollArea > 0.001f)
                {
                    // We want to keep the item in view. 
                    // Calculate current normalized scroll position
                    float currentScroll = scrollRect.verticalNormalizedPosition;
                    
                    // Top of item in normalized space
                    float itemTopNormalized = 1f - (scrollPosition / scrollArea);
                    // Bottom of item in normalized space
                    float itemBottomNormalized = 1f - ((scrollPosition + itemHeight) / scrollArea);
                    
                    // Small buffers to prevent jitter and ensure full visibility
                    float buffer = 0.05f; 
                    
                    // If item is below view, scroll down
                    if (currentScroll > itemTopNormalized)
                    {
                        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(itemTopNormalized);
                    }
                    // If item is above view, scroll up
                    else if (currentScroll < itemBottomNormalized)
                    {
                        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(itemBottomNormalized);
                    }
                }
                else
                {
                    // Everything fits, keep it at top
                    scrollRect.verticalNormalizedPosition = 1f;
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
    [SerializeField] private string loadingScreenImagePath = "Assets/TitleScreen.jpg";
    
    // Loading screen UI elements
    private GameObject loadingScreenUI;
    private Image loadingBackground;
    private GameObject loadingCircle;
    private Image progressBarBackground;
    private Image progressBarFill;
    private TextMeshProUGUI progressText;
    private TextMeshProUGUI loadingSubText;
    private bool loadingComplete = false;
    private float loadingFadeProgress = 0f;
    private const float LOADING_FADE_SPEED = 2f;
    private Sprite loadingScreenSprite;
    
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
        
        // Load background image
        if (loadingScreenSprite == null && !string.IsNullOrEmpty(loadingScreenImagePath))
        {
#if UNITY_EDITOR
            loadingScreenSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(loadingScreenImagePath);
            if (loadingScreenSprite == null)
            {
                // Fallback: Try loading as Texture2D and create a sprite (handles images not marked as "Sprite" in Unity)
                Texture2D tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(loadingScreenImagePath);
                if (tex != null)
                {
                    loadingScreenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    Debug.Log($"[UIManager] Created sprite from texture for loading screen: {loadingScreenImagePath}");
                }
                else
                {
                    Debug.LogWarning($"[UIManager] Failed to load loading screen image at: {loadingScreenImagePath}");
                }
            }
#else
            // In build, we assumes the image is in Resources if we want to load it this way,
            string resourcesPath = loadingScreenImagePath.Replace("Assets/", "").Replace(".jpg", "").Replace(".png", "");
            loadingScreenSprite = Resources.Load<Sprite>(resourcesPath);
            if (loadingScreenSprite == null)
            {
                Texture2D tex = Resources.Load<Texture2D>(resourcesPath);
                if (tex != null)
                {
                    loadingScreenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
#endif
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
        
        // Add background
        loadingBackground = loadingScreenUI.AddComponent<Image>();
        if (loadingScreenSprite != null)
        {
            loadingBackground.sprite = loadingScreenSprite;
            loadingBackground.color = Color.white;
            loadingBackground.preserveAspect = true; // Best for title screens
            // Make it fill the screen
            loadingBackground.type = Image.Type.Simple;
        }
        else
        {
            loadingBackground.color = new Color(0.02f, 0.02f, 0.05f, 1f); // Fallback to dark blue-black
        }
        
        // === TOP QUARTER: LOADING CIRCLE ===
        GameObject topContainer = new GameObject("TopContainer");
        topContainer.transform.SetParent(loadingScreenUI.transform, false);
        RectTransform topRect = topContainer.AddComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0.5f, 0.75f);
        topRect.anchorMax = new Vector2(0.5f, 0.75f);
        topRect.pivot = new Vector2(0.5f, 0.5f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(200, 200);

        loadingCircle = new GameObject("LoadingCircle");
        loadingCircle.transform.SetParent(topContainer.transform, false);
        RectTransform circleRect = loadingCircle.AddComponent<RectTransform>();
        circleRect.sizeDelta = new Vector2(100, 100);
        
        Image circleImage = loadingCircle.AddComponent<Image>();
        // Use a simple circle sprite or create a procedural one
        // For now, we'll create a simple ring appearance using a filled image with radial fill
        circleImage.color = new Color(0.3f, 0.6f, 1f, 1f);
        
        // Try to find a default Unity sprite for the circle if possible, or just use a square for now 
        // until we have a proper asset. Actually, we can use the "Knob" sprite which is usually present.
#if UNITY_EDITOR
        circleImage.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
#endif
        circleImage.type = Image.Type.Filled;
        circleImage.fillMethod = Image.FillMethod.Radial360;
        circleImage.fillAmount = 0.25f; // Quarter circle
        
        // === BOTTOM QUARTER: PROGRESS BAR & INFO ===
        GameObject bottomContainer = new GameObject("BottomContainer");
        bottomContainer.transform.SetParent(loadingScreenUI.transform, false);
        RectTransform bottomRect = bottomContainer.AddComponent<RectTransform>();
        bottomRect.anchorMin = new Vector2(0.5f, 0.25f);
        bottomRect.anchorMax = new Vector2(0.5f, 0.25f);
        bottomRect.pivot = new Vector2(0.5f, 0.5f);
        bottomRect.anchoredPosition = Vector2.zero;
        bottomRect.sizeDelta = new Vector2(600, 150);
        
        // Create progress bar container
        GameObject progressContainer = new GameObject("ProgressBarContainer");
        progressContainer.transform.SetParent(bottomContainer.transform, false);
        RectTransform progressContainerRect = progressContainer.AddComponent<RectTransform>();
        progressContainerRect.anchorMin = new Vector2(0.1f, 0.45f);
        progressContainerRect.anchorMax = new Vector2(0.9f, 0.55f);
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
        progressBarBackground.color = new Color(0.1f, 0.12f, 0.18f, 0.8f);
        
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
        progressTextGO.transform.SetParent(bottomContainer.transform, false);
        RectTransform progressTextRect = progressTextGO.AddComponent<RectTransform>();
        progressTextRect.anchorMin = new Vector2(0, 0.25f);
        progressTextRect.anchorMax = new Vector2(1, 0.42f);
        progressTextRect.offsetMin = Vector2.zero;
        progressTextRect.offsetMax = Vector2.zero;
        
        progressText = progressTextGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) progressText.font = labelFont;
        progressText.fontSize = 18;
        progressText.color = new Color(0.8f, 0.9f, 1f, 1f);
        progressText.alignment = TextAlignmentOptions.Center;
        progressText.text = "0% - 0 / 0 stars";
        
        // Sub-text hint
        GameObject hintGO = new GameObject("LoadingHint");
        hintGO.transform.SetParent(bottomContainer.transform, false);
        RectTransform hintRect = hintGO.AddComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0, 0.1f);
        hintRect.anchorMax = new Vector2(1, 0.25f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;
        
        loadingSubText = hintGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) loadingSubText.font = labelFont;
        loadingSubText.fontSize = 14;
        loadingSubText.color = new Color(0.6f, 0.7f, 0.8f, 0.9f);
        loadingSubText.alignment = TextAlignmentOptions.Center;
        loadingSubText.text = "Preparing stellar dataset...";
        
        // Hide HUD during loading
        if (hudReference != null)
        {
            hudReference.SetActive(false);
        }
        
        Debug.Log("[UIManager] Loading screen created with background image and loading circle");
    }
    
    /// <summary>
    /// Updates the loading screen progress. Call from Update().
    /// </summary>
    /// <param name="dataReady">True when stellar data has finished loading</param>
    /// <param name="starCount">Current number of loaded stars</param>
    public void UpdateLoadingScreen(bool dataReady, int starCount, int totalStars, string datasetName)
    {
        if (loadingScreenUI == null) return;
        
        // Animate loading circle
        if (loadingCircle != null)
        {
            loadingCircle.transform.Rotate(0, 0, -200f * Time.deltaTime);
        }

        // Update sub-text with dataset name
        if (loadingSubText != null)
        {
            loadingSubText.text = $"Processing {datasetName} stellar dataset...";
        }

        float progress = totalStars > 0 ? Mathf.Clamp01((float)starCount / totalStars) : 0f;
        
        // Update progress bar fill
        if (progressBarFill != null && !loadingComplete)
        {
            RectTransform fillRect = progressBarFill.GetComponent<RectTransform>();
            fillRect.anchorMax = new Vector2(progress, 1f);
        }
        
        // Update progress text
        if (progressText != null && !loadingComplete)
        {
            progressText.text = $"{(progress * 100f):F1}% - {starCount:N0} / {totalStars:N0} stars";
        }
        
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
                progressText.text = $"100% - {starCount:N0} / {totalStars:N0} stars";
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
                    // If we have a sprite, keep its base color (white) and just fade alpha
                    Color bgCol = loadingScreenSprite != null ? Color.white : new Color(0.02f, 0.02f, 0.05f, 1f);
                    bgCol.a = alpha;
                    loadingBackground.color = bgCol;
                }
                if (loadingCircle != null)
                {
                    Image circleImg = loadingCircle.GetComponent<Image>();
                    if (circleImg != null)
                    {
                        Color c = circleImg.color;
                        c.a = alpha;
                        circleImg.color = c;
                    }
                }
                if (progressBarBackground != null)
                {
                    Color c = progressBarBackground.color;
                    c.a = alpha * 0.8f;
                    progressBarBackground.color = c;
                }
                if (progressBarFill != null)
                {
                    Color c = progressBarFill.color;
                    c.a = alpha;
                    progressBarFill.color = c;
                }
                if (progressText != null)
                {
                    Color c = progressText.color;
                    c.a = alpha;
                    progressText.color = c;
                }
                if (loadingSubText != null)
                {
                    Color c = loadingSubText.color;
                    c.a = alpha * 0.9f;
                    loadingSubText.color = c;
                }
            }
        }
    }
    
    /// <summary>
    /// Shows or hides all labels.
    /// </summary>
    public void SetLabelsVisible(bool visible)
    {
        if (labelCanvas != null)
        {
            foreach (Transform child in labelCanvas.transform)
            {
                if (child.name.EndsWith("_Label"))
                {
                    child.gameObject.SetActive(visible);
                }
            }
        }
    }
    
    // ========================
    // HUD (HEADS-UP DISPLAY)
    // ========================
    
    // HUD UI elements
    private GameObject hudUI;
    private TextMeshProUGUI hudText;
    private GameObject controlsHint; // Controls hint at bottom
    
    public GameObject HudUI => hudUI;
    public bool EnableHUD => enableHUD;
    public TextMeshProUGUI HudText => hudText;
    public GameObject ControlsHint => controlsHint;
    
    /// <summary>
    /// Creates the HUD display with sci-fi styling.
    /// </summary>
    public void CreateHUD()
    {
        if (!enableHUD) return;
        
        if (labelCanvas == null)
        {
            Debug.LogWarning("[UIManager] Cannot create HUD: Label Canvas not available. HUD requires a canvas.");
            return;
        }
        
        // Create main HUD container
        hudUI = new GameObject("HUD");
        hudUI.transform.SetParent(labelCanvas.transform, false);
        
        RectTransform hudRect = hudUI.AddComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0, 1);
        hudRect.anchorMax = new Vector2(0, 1);
        hudRect.pivot = new Vector2(0, 1);
        hudRect.anchoredPosition = hudPosition;
        hudRect.sizeDelta = new Vector2(540, 300); // Compact size
        
        // === BACKGROUND PANEL ===
        GameObject bgPanel = new GameObject("Background");
        bgPanel.transform.SetParent(hudUI.transform, false);
        RectTransform bgRect = bgPanel.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        Image bgImage = bgPanel.AddComponent<Image>();
        bgImage.color = new Color(0.02f, 0.05f, 0.12f, 0.85f); // Dark blue, semi-transparent
        
        // === BORDER FRAME ===
        // Top border
        CreateBorderEdge(hudUI.transform, "BorderTop", new Vector2(0, 1), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(0, -3), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Bottom border
        CreateBorderEdge(hudUI.transform, "BorderBottom", new Vector2(0, 0), new Vector2(1, 0), 
            new Vector2(0, 3), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Left border
        CreateBorderEdge(hudUI.transform, "BorderLeft", new Vector2(0, 0), new Vector2(0, 1), 
            new Vector2(3, 0), new Vector2(0, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        // Right border
        CreateBorderEdge(hudUI.transform, "BorderRight", new Vector2(1, 0), new Vector2(1, 1), 
            new Vector2(0, 0), new Vector2(-3, 0), new Color(0.2f, 0.8f, 1f, 0.9f));
        
        // === CORNER BRACKETS ===
        Color bracketColor = new Color(0.3f, 0.9f, 1f, 1f); // Bright cyan
        float bracketSize = 25f;
        float bracketThickness = 3f;
        
        // Top-left corner
        CreateCornerBracket(hudUI.transform, "CornerTL", new Vector2(0, 1), bracketSize, bracketThickness, bracketColor, true, true);
        // Top-right corner
        CreateCornerBracket(hudUI.transform, "CornerTR", new Vector2(1, 1), bracketSize, bracketThickness, bracketColor, false, true);
        // Bottom-left corner
        CreateCornerBracket(hudUI.transform, "CornerBL", new Vector2(0, 0), bracketSize, bracketThickness, bracketColor, true, false);
        // Bottom-right corner
        CreateCornerBracket(hudUI.transform, "CornerBR", new Vector2(1, 0), bracketSize, bracketThickness, bracketColor, false, false);
        
        // === HEADER LINE ===
        GameObject headerLine = new GameObject("HeaderLine");
        headerLine.transform.SetParent(hudUI.transform, false);
        RectTransform headerLineRect = headerLine.AddComponent<RectTransform>();
        headerLineRect.anchorMin = new Vector2(0, 1);
        headerLineRect.anchorMax = new Vector2(1, 1);
        headerLineRect.pivot = new Vector2(0.5f, 1);
        headerLineRect.anchoredPosition = new Vector2(0, -40);
        headerLineRect.sizeDelta = new Vector2(-20, 1);
        Image headerLineImg = headerLine.AddComponent<Image>();
        headerLineImg.color = new Color(0.2f, 0.6f, 0.8f, 0.5f);
        
        // === HEADER TITLE ===
        GameObject headerGO = new GameObject("Header");
        headerGO.transform.SetParent(hudUI.transform, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.anchoredPosition = new Vector2(0, -8);
        headerRect.sizeDelta = new Vector2(-20, 30);
        
        // Header styling
        
        TextMeshProUGUI headerText = headerGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) headerText.font = labelFont;
        headerText.text = "NAVIGATION SYSTEM";
        headerText.fontSize = 18;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.4f, 0.9f, 1f, 1f);
        headerText.alignment = TextAlignmentOptions.Center;
        headerText.characterSpacing = 3f;
        
        // === MAIN DATA TEXT ===
        GameObject textGO = new GameObject("DataText");
        textGO.transform.SetParent(hudUI.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(15, 60);  // Padding from edges, more space at bottom
        textRect.offsetMax = new Vector2(-15, -48);
        
        hudText = textGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) hudText.font = labelFont;
        hudText.fontSize = hudFontSize;
        hudText.color = new Color(0.85f, 0.95f, 1f, 1f);
        hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.textWrappingMode = TextWrappingModes.NoWrap;
        hudText.overflowMode = TextOverflowModes.Overflow;
        hudText.lineSpacing = 12f; // Slight increase for better readability
        hudText.richText = true;
        
        // Create icon container
        GameObject iconContainer = new GameObject("IconContainer");
        iconContainer.transform.SetParent(hudUI.transform, false);
        RectTransform iconRect = iconContainer.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0, 0);
        iconRect.anchorMax = new Vector2(0, 1);
        iconRect.pivot = new Vector2(0, 0.5f);
        iconRect.anchoredPosition = new Vector2(50, -24); // Offset to align with text (moved right to make space for icons)
        iconRect.sizeDelta = new Vector2(32, 0);
        
        // Initial text - now cleaner without brackets, indented to make room for icons
        hudText.text = "<color=#FFFFFF>VELOCITY</color> <color=#AAAAAA>0 km/s</color>\n" +
                       "    <color=#88AACC>0%c</color>\n" +
                       "<color=#FFFFFF>SOLAR DIST</color> <color=#AAAAAA>0 km</color>\n" +
                       "<color=#FFFFFF>PROXIMITY</color> <color=#AAAAAA>--</color>";
        
        // === CONTROLS HINT AT BOTTOM ===
        GameObject controlsGO = new GameObject("ControlsHint");
        controlsGO.transform.SetParent(hudUI.transform, false);
        RectTransform controlsRect = controlsGO.AddComponent<RectTransform>();
        controlsRect.anchorMin = new Vector2(0, 0);
        controlsRect.anchorMax = new Vector2(1, 0);
        controlsRect.pivot = new Vector2(0.5f, 0);
        controlsRect.anchoredPosition = new Vector2(0, 12);
        controlsRect.sizeDelta = new Vector2(-20, 40);
        
        TextMeshProUGUI controlsText = controlsGO.AddComponent<TextMeshProUGUI>();
        if (labelFont != null) controlsText.font = labelFont;
        controlsText.text = "<color=#4488AA>[ X ] Autopilot  [ O ] Orbit  [ I ] Info</color>";
        controlsText.fontSize = 15;
        controlsText.color = new Color(0.4f, 0.6f, 0.7f, 0.8f);
        controlsText.alignment = TextAlignmentOptions.Center;
        
        // Store reference for visibility toggling
        controlsHint = controlsGO;
        
        Debug.Log("[UIManager] Sci-Fi HUD created successfully");
    }
    
    /// <summary>
    /// Helper to create a border edge line.
    /// </summary>
    private void CreateBorderEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject border = new GameObject(name);
        border.transform.SetParent(parent, false);
        RectTransform rect = border.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image img = border.AddComponent<Image>();
        img.color = color;
    }
    
    /// <summary>
    /// Helper to create an L-shaped corner bracket.
    /// </summary>
    private void CreateCornerBracket(Transform parent, string name, Vector2 anchor, 
        float size, float thickness, Color color, bool left, bool top)
    {
        GameObject corner = new GameObject(name);
        corner.transform.SetParent(parent, false);
        RectTransform cornerRect = corner.AddComponent<RectTransform>();
        cornerRect.anchorMin = anchor;
        cornerRect.anchorMax = anchor;
        cornerRect.pivot = anchor;
        cornerRect.anchoredPosition = Vector2.zero;
        cornerRect.sizeDelta = new Vector2(size, size);
        
        // Horizontal part of L
        GameObject hBar = new GameObject("H");
        hBar.transform.SetParent(corner.transform, false);
        RectTransform hRect = hBar.AddComponent<RectTransform>();
        hRect.anchorMin = new Vector2(left ? 0 : 1, top ? 1 : 0);
        hRect.anchorMax = new Vector2(left ? 0 : 1, top ? 1 : 0);
        hRect.pivot = new Vector2(left ? 0 : 1, top ? 1 : 0);
        hRect.anchoredPosition = Vector2.zero;
        hRect.sizeDelta = new Vector2(size, thickness);
        Image hImg = hBar.AddComponent<Image>();
        hImg.color = color;
        
        // Vertical part of L
        GameObject vBar = new GameObject("V");
        vBar.transform.SetParent(corner.transform, false);
        RectTransform vRect = vBar.AddComponent<RectTransform>();
        vRect.anchorMin = new Vector2(left ? 0 : 1, top ? 1 : 0);
        vRect.anchorMax = new Vector2(left ? 0 : 1, top ? 1 : 0);
        vRect.pivot = new Vector2(left ? 0 : 1, top ? 1 : 0);
        vRect.anchoredPosition = Vector2.zero;
        vRect.sizeDelta = new Vector2(thickness, size);
        Image vImg = vBar.AddComponent<Image>();
        vImg.color = color;
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
    /// <summary>
    /// Shows or hides the label text using transparency, keeping the label GameObject active.
    /// This allows children (like HUD info boxes) to remain visible even if the name label is hidden.
    /// </summary>
    public void SetLabelTextVisible(GameObject labelGO, bool visible)
    {
        if (labelGO == null) return;
        
        var textComp = labelGO.GetComponent<TextMeshProUGUI>();
        if (textComp != null)
        {
            // Use alpha for visibility instead of enabling/disabling component
            // This ensures the component remains active which might be needed for layout/children
            // or if the "Box" is somehow dependent on the TextMeshPro component being enabled.
            Color c = textComp.color;
            c.a = visible ? 1f : 0f;
            textComp.color = c;
            
            // Also ensure the component is actually enabled
            if (!textComp.enabled) textComp.enabled = true;
        }
    }
}
