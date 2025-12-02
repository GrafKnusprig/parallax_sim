using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SolarSystemParallaxManager : MonoBehaviour
{
    [Header("CSV")]
    [Tooltip("File name inside StreamingAssets")]
    [SerializeField] private string csvFileName = "solar_system_positions_with_velocity.csv";

    [Tooltip("We use a single snapshot for this date (YYYY-MM-DD).")]
    [SerializeField] private string targetDate = "2023-07-10";

    [Header("Horizon bubble")]
    [SerializeField] private float horizonRadius = 1000f;
    [SerializeField] private Material horizonMaterial;
    [SerializeField] private bool showHorizonSphere = false;

    [Header("Planets / Bodies")]
    [SerializeField] private Material planetMaterial;
    [SerializeField] private float minProxyRadius = 2f;
    [SerializeField] private float maxProxyRadius = 200f;

    [Header("Player (real space)")]
    [SerializeField] private float moveSpeedAuPerSecond = 0.01f;

    [Header("Dynamic Scaling & Speed")]
    [SerializeField] private bool enableDynamicBehavior = true;
    [SerializeField] private float baseScale = 1f;
    [SerializeField] private float minScale = 0.001f;  // Much smaller for massive planet effect
    [SerializeField] private float maxScale = 5f;
    [SerializeField] private float scaleTransitionDistanceAu = 0.1f;   // Larger transition zone
    [SerializeField] private float baseSpeed = 0.05f;
    [SerializeField] private float minSpeed = 0.0001f;  // Very slow for precise control
    [SerializeField] private float maxSpeed = 0.3f;     // More moderate normal speed
    [SerializeField] private float speedTransitionDistanceAu = 0.05f;  // Larger slow zone
    [SerializeField] private float hyperSpeed = 5.0f;   // More reasonable hyperspeed
    [SerializeField] private float hyperSpeedTransitionDistanceAu = 2.0f;  // Further hyperspeed activation
    
    [Header("Super Near Zone - Breathtaking Flybys")]
    [SerializeField] private float superNearSpeed = 0.00001f;  // Ultra-slow for dramatic flybys
    [SerializeField] private float superNearScale = 0.0001f;   // Massive planet effect
    [SerializeField] private float superNearTransitionDistanceAu = 0.01f;  // Very close encounters

    [Tooltip("New Input System: 2D move (x: strafe, y: forward).")]
    [SerializeField] private InputActionReference moveAction;

    [Tooltip("New Input System: vertical move (float axis).")]
    [SerializeField] private InputActionReference verticalAction;

    [Header("Labels (optional)")]
    [SerializeField] private bool enableLabels = true;
    [SerializeField] private Canvas labelCanvas;
    [SerializeField] private Font labelFont;
    [SerializeField] private int labelFontSize = 14;
    [SerializeField] private Color labelColor = Color.white;
    [SerializeField] private float labelOffsetPixels = 20f; // offset from planet center in pixels

    private const double AU_KM = 149_597_870.7;

    private Vector3 playerRealPosAu; // player position in AU (real space)

    private GameObject horizonSphere;

    private readonly List<BodyInstance> bodies = new List<BodyInstance>();
    
    // Dynamic scaling and speed
    private BodyInstance nearestPlanet;
    private float currentScale;
    private float currentSpeed;
    private float distanceToNearestPlanet;

    private class BodyInstance
    {
        public string name;
        public int naifId;
        public Vector3 realPosAu;
        public float radiusKm;
        public Transform proxy;

        // UI-based labels
        public GameObject labelUI;
        public Text labelText;
    }

    // Radii for main bodies (km), keyed by NAIF ID used in your file
    private static readonly Dictionary<int, float> BodyRadiiKm = new Dictionary<int, float>
    {
        { 10, 696_340f },   // SUN

        { 199, 2_439.7f },  // MERCURY
        { 299, 6_051.8f },  // VENUS
        { 399, 6_371.0f },  // EARTH
        { 301, 1_737.4f },  // MOON

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

    private void Start()
    {
        CreateHorizonSphere();
        SetupLabelCanvas();
        LoadBodiesFromCsv();
        if (bodies.Count == 0)
        {
            Debug.LogWarning("No bodies loaded for date " + targetDate);
        }
        
        // Initialize dynamic behavior
        currentScale = baseScale;
        currentSpeed = baseSpeed;
        
        // Set initial camera scale
        Camera.main.transform.localScale = Vector3.one * currentScale;
    }

    private void SetupLabelCanvas()
    {
        if (enableLabels && labelCanvas == null)
        {
            // Create a Canvas for labels if one isn't assigned
            GameObject canvasGO = new GameObject("LabelCanvas");
            canvasGO.transform.SetParent(transform, false);
            
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();
            
            labelCanvas = canvas;
            Debug.Log("Created automatic label canvas");
        }
    }

    private void Update()
    {
        if (enableDynamicBehavior)
        {
            UpdateDynamicBehavior();
        }
        UpdatePlayerMovement();
        UpdateBodyProxies();
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
        string path = Path.Combine(Application.streamingAssetsPath, csvFileName);
        if (!File.Exists(path))
        {
            Debug.LogError("CSV not found at " + path);
            return;
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1)
        {
            Debug.LogError("CSV seems empty or header-only.");
            return;
        }

        bool earthFound = false;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 9) continue;

            string dateStr = parts[0];
            if (dateStr != targetDate) continue; // only our snapshot day

            string rawName = parts[1];
            if (!int.TryParse(parts[2], out int naifId)) continue;
            
            // Clean up the name by removing NAIF ID prefix and extra words
            string name = CleanBodyName(rawName);

            // We keep:
            // - Sun (10)
            // - Everything with ID >= 100 (planets, moons, some satellites)
            // - Plus barycenter giants with IDs 1..9 if they have a radius defined
            bool keep =
                naifId == 10 ||
                naifId >= 100 ||
                BodyRadiiKm.ContainsKey(naifId);

            if (!keep)
                continue;

            float xAu = ParseFloat(parts[3]);
            float yAu = ParseFloat(parts[4]);
            float zAu = ParseFloat(parts[5]);

            Vector3 realPosAu = new Vector3(xAu, yAu, zAu);

            // Radius look-up; fallback if not known
            float radiusKm = BodyRadiiKm.TryGetValue(naifId, out float r) ? r : 1_000f;

            // Create proxy sphere
            GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            proxy.name = name;
            proxy.transform.SetParent(transform, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localScale = Vector3.one; // will be updated in UpdateBodyProxies

            var col = proxy.GetComponent<Collider>();
            if (col) Destroy(col);

            var renderer = proxy.GetComponent<MeshRenderer>();
            if (renderer != null && planetMaterial != null)
            {
                renderer.sharedMaterial = planetMaterial;
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

            // Player start at Earth (399) in real coordinates
            if (naifId == 399 && !earthFound)
            {
                playerRealPosAu = realPosAu;
                earthFound = true;
            }
        }

        if (!earthFound)
        {
            Debug.LogWarning("Earth (naifId 399) not found on " + targetDate + ". Player starts at origin in real space.");
            playerRealPosAu = Vector3.zero;
        }
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
        Text textComponent = labelGO.AddComponent<Text>();
        textComponent.text = body.name;
        textComponent.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        textComponent.fontSize = labelFontSize;
        textComponent.color = labelColor;
        textComponent.alignment = TextAnchor.MiddleLeft;
        textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
        textComponent.verticalOverflow = VerticalWrapMode.Overflow;

        // Store references
        body.labelUI = labelGO;
        body.labelText = textComponent;
    }

    private float ParseFloat(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
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
        distanceToNearestPlanet = Mathf.Max(0.00001f, distanceAu - planetRadiusAu);
        
        // Calculate scale based on distance with super near zone for breathtaking flybys
        float targetScale;
        
        if (distanceToNearestPlanet < superNearTransitionDistanceAu)
        {
            // Super near zone: transition from superNearScale to minScale
            float superNearFactor = distanceToNearestPlanet / superNearTransitionDistanceAu;
            superNearFactor = superNearFactor * superNearFactor; // Exponential for dramatic effect
            targetScale = Mathf.Lerp(superNearScale, minScale, superNearFactor);
        }
        else
        {
            // Normal scale transition from minScale to maxScale
            float scaleFactor = Mathf.Clamp01((distanceToNearestPlanet - superNearTransitionDistanceAu) / (scaleTransitionDistanceAu - superNearTransitionDistanceAu));
            targetScale = Mathf.Lerp(minScale, maxScale, scaleFactor);
        }
        
        // Calculate speed based on distance with four zones: super near, close, normal, and hyperspeed
        float targetSpeed;
        
        if (distanceToNearestPlanet < superNearTransitionDistanceAu)
        {
            // Super near zone: transition from superNearSpeed to minSpeed for breathtaking flybys
            float superNearFactor = distanceToNearestPlanet / superNearTransitionDistanceAu;
            superNearFactor = superNearFactor * superNearFactor * superNearFactor; // Cubic for ultra-dramatic slowdown
            targetSpeed = Mathf.Lerp(superNearSpeed, minSpeed, superNearFactor);
        }
        else if (distanceToNearestPlanet < speedTransitionDistanceAu)
        {
            // Close zone: exponential curve from minSpeed to maxSpeed
            float speedFactor = (distanceToNearestPlanet - superNearTransitionDistanceAu) / (speedTransitionDistanceAu - superNearTransitionDistanceAu);
            speedFactor = speedFactor * speedFactor; // Square for exponential curve
            targetSpeed = Mathf.Lerp(minSpeed, maxSpeed, speedFactor);
        }
        else if (distanceToNearestPlanet < hyperSpeedTransitionDistanceAu)
        {
            // Normal zone: smoother transition from maxSpeed to hyperSpeed
            float normalizedDistance = (distanceToNearestPlanet - speedTransitionDistanceAu) / (hyperSpeedTransitionDistanceAu - speedTransitionDistanceAu);
            normalizedDistance = Mathf.Clamp01(normalizedDistance);
            // Use smoothstep for even smoother transition
            float smoothFactor = normalizedDistance * normalizedDistance * (3.0f - 2.0f * normalizedDistance);
            targetSpeed = Mathf.Lerp(maxSpeed, hyperSpeed, smoothFactor);
        }
        else
        {
            // Far zone: full hyperspeed
            targetSpeed = hyperSpeed;
        }
        
        // Ultra-responsive transitions with emergency braking for close approaches
        float scaleLerpSpeed = 20f;
        float speedLerpSpeed = 25f;
        
        // Emergency braking: if we're moving too fast and getting close, brake harder
        if (distanceToNearestPlanet < superNearTransitionDistanceAu * 3f && currentSpeed > targetSpeed)
        {
            speedLerpSpeed = 100f; // Ultra-fast braking for super near encounters
        }
        else if (distanceToNearestPlanet < speedTransitionDistanceAu * 2f && currentSpeed > targetSpeed)
        {
            speedLerpSpeed = 50f; // Fast braking for close encounters
        }
        
        currentScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * scaleLerpSpeed);
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * speedLerpSpeed);
        
        // Apply scale to camera
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.localScale = Vector3.one * currentScale;
        }
        
        // Debug info (remove or comment out when not needed)
        if (Time.frameCount % 30 == 0) // Every 30 frames
        {
            string zone = distanceToNearestPlanet < superNearTransitionDistanceAu ? "SUPER NEAR" :
                         distanceToNearestPlanet < speedTransitionDistanceAu ? "CLOSE" :
                         distanceToNearestPlanet < hyperSpeedTransitionDistanceAu ? "NORMAL" : "HYPERSPEED";
            Debug.Log($"Zone: {zone}, Distance: {distanceToNearestPlanet:F6} AU, Scale: {currentScale:F5}, Speed: {currentSpeed:F6} AU/s, Planet: {nearestPlanet.name}");
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
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        Vector3 camUp = cam.transform.up;

        // Full 3D movement based on camera orientation
        Vector3 moveDir =
            camRight * move.x +        // strafe left/right
            camForward * move.y +      // move forward/backward in camera direction
            camUp * vertical;          // move up/down relative to camera

        if (moveDir.sqrMagnitude < 1e-6f) return;

        moveDir.Normalize();
        float effectiveSpeed = enableDynamicBehavior ? currentSpeed : moveSpeedAuPerSecond;
        playerRealPosAu += moveDir * (effectiveSpeed * Time.deltaTime);
    }

    private void UpdateBodyProxies()
    {
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

            // --- UI label update ---
            if (enableLabels && body.labelUI != null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    // Convert world position to screen position
                    Vector3 screenPos = cam.WorldToScreenPoint(body.proxy.position);
                    
                    // Check if object is in front of camera and on screen
                    bool isVisible = screenPos.z > 0 && 
                                   screenPos.x >= 0 && screenPos.x <= Screen.width &&
                                   screenPos.y >= 0 && screenPos.y <= Screen.height;
                    
                    body.labelUI.SetActive(isVisible);
                    
                    if (isVisible)
                    {
                        // Convert screen position to canvas position
                        RectTransform canvasRect = labelCanvas.GetComponent<RectTransform>();
                        RectTransform labelRect = body.labelUI.GetComponent<RectTransform>();
                        
                        Vector2 canvasPos;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            canvasRect, screenPos, labelCanvas.worldCamera, out canvasPos);
                        
                        // Add offset to position label to the right of the planet
                        canvasPos.x += labelOffsetPixels;
                        
                        labelRect.localPosition = canvasPos;
                    }
                }
            }
        }
    }
}