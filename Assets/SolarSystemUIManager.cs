using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
    
    // Reference to the main manager
    private SolarSystemParallaxManager parallaxManager;
    
    // Internal state
    private bool isVRMode = false;
    private bool wasVRMode = false;
    
    // Public accessors
    public bool EnableLabels => enableLabels;
    public Canvas LabelCanvas => labelCanvas;
    public TMP_FontAsset LabelFont => labelFont;
    public int LabelFontSize => labelFontSize;
    public Color LabelColor => labelColor;
    public float LabelOffsetPixels => labelOffsetPixels;
    public bool IsVRMode => isVRMode;
    
    private void Awake()
    {
        parallaxManager = GetComponent<SolarSystemParallaxManager>();
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
        textComponent.enableWordWrapping = false;
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
}
