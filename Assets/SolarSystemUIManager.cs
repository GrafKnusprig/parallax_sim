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
}
