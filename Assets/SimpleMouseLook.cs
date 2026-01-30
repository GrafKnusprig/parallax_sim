using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleMouseLook : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private bool vrMode = true;
    [Tooltip("When enabled, uses VR head tracking. When disabled, uses mouse look.")]
    
    [Header("Input")]
    [SerializeField] private InputActionReference vrLookAction; // VR head tracking (for VR mode)
    [SerializeField] private InputActionReference vrControllerLookAction; // VR controller trackpad/stick look
    [SerializeField] private InputActionReference mouseLookAction; // Mouse look (for non-VR mode)

    [Header("Settings")]
    [SerializeField] private float sensitivity = 0.1f;
    [SerializeField] private bool lockCursor = true;

    private float yaw;
    private float pitch;

    private Transform artificialLookPivot; // Intermediate pivot for VR controller rotation 
    public Transform ArtificialLookPivot => GetArtificialLookPivot();

    private void OnEnable()
    {
        EnableCurrentLookAction();
    }

    private void OnDisable()
    {
        DisableAllLookActions();
    }
    
    private void EnableCurrentLookAction()
    {
        // Manage TrackedPoseDrivers (disable in desktop mode to allow mouse control)
        // We find all in scene to prevent any VR headset from overriding rotation
        var poseDrivers = FindObjectsByType<UnityEngine.InputSystem.XR.TrackedPoseDriver>(FindObjectsSortMode.None);
        foreach (var pd in poseDrivers)
        {
            pd.enabled = vrMode;
        }

        if (vrMode)
        {
            if (vrLookAction != null) vrLookAction.action.Enable();
            if (vrControllerLookAction != null) vrControllerLookAction.action.Enable();
            
            // Explicitly disable mouse look action in VR mode
            if (mouseLookAction != null) mouseLookAction.action.Disable();
        }
        else if (!vrMode)
        {
            if (mouseLookAction != null) mouseLookAction.action.Enable();
            
            // Explicitly disable VR actions in non-VR mode
            if (vrLookAction != null) vrLookAction.action.Disable();
            if (vrControllerLookAction != null) vrControllerLookAction.action.Disable();
        }
    }
    
    private void DisableAllLookActions()
    {
        if (vrLookAction != null)
            vrLookAction.action.Disable();
        if (vrControllerLookAction != null)
            vrControllerLookAction.action.Disable();
        if (mouseLookAction != null)
            mouseLookAction.action.Disable();
    }

    private Transform GetCameraRig()
    {
        // In VR, the Rig should be the stable parent.
        // We find the parent of the pivot (if it exists) or the current parent.
        Transform pivot = GetArtificialLookPivot();
        if (pivot != null && pivot.parent != null)
            return pivot.parent;

        if (transform.parent != null)
            return transform.parent;
        
        return transform;
    }

    private Transform GetArtificialLookPivot()
    {
        if (!vrMode) return null;
        if (artificialLookPivot != null) return artificialLookPivot;

        // Try to find an existing pivot first
        if (transform.parent != null && transform.parent.name == "ArtificialLookPivot")
        {
            artificialLookPivot = transform.parent;
            return artificialLookPivot;
        }

        // Create a new pivot at runtime
        GameObject pivotObj = new GameObject("ArtificialLookPivot");
        artificialLookPivot = pivotObj.transform;
        
        // Sync position/rotation with camera's current state relative to rig
        artificialLookPivot.position = transform.position;
        artificialLookPivot.rotation = transform.rotation;
        
        // Insert into hierarchy: Rig -> Pivot -> Camera
        if (transform.parent != null)
        {
            artificialLookPivot.SetParent(transform.parent, true);
        }
        transform.SetParent(artificialLookPivot, true);

        Debug.Log("[SimpleMouseLook] Created and inserted ArtificialLookPivot into hierarchy.");
        return artificialLookPivot;
    }

    private void Start()
    {
        // For VR mode, the state is held by the Pivot's transform.
        // For Desktop mode, we initialize our yaw/pitch.
        if (vrMode)
        {
            GetArtificialLookPivot(); // Ensure pivot exists
        }
        else
        {
            Vector3 euler = transform.localEulerAngles;
            yaw = euler.y;
            pitch = euler.x;
            if (pitch > 180f) pitch -= 360f;
        }

        UpdateCursorState();
        
        Debug.Log($"[SimpleMouseLook] Initialized. VR Mode: {vrMode}, Target: {(vrMode ? "ArtificialLookPivot" : transform.name)}");
    }
    
    private void UpdateCursorState()
    {
        // Check if a UI menu is open
        bool menuOpen = SolarSystemUIManager.IsMenuOpen;
        
        if (lockCursor && !menuOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (menuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        // Sync VR mode from UI manager if available to ensure one source of truth
        SolarSystemUIManager uiManager = FindFirstObjectByType<SolarSystemUIManager>();
        if (uiManager != null)
        {
            if (vrMode != uiManager.IsVRMode)
            {
                vrMode = uiManager.IsVRMode;
                DisableAllLookActions();
                EnableCurrentLookAction();
            }
        }

        // Update cursor state based on menu
        UpdateCursorState();
        
        // Skip mouse look when menu is open
        if (SolarSystemUIManager.IsMenuOpen)
        {
            return;
        }
        
        // Skip manual look when autopilot is controlling the camera or when Orbiting.
        // During these modes, we sync our yaw/pitch from the RIG's rotation to avoid a jump when they end.
        // UNLESS we are in VR Mode and using the controller to look around.
        if (SolarSystemParallaxManager.IsAutopilotActive || SolarSystemParallaxManager.IsOrbiting)
        {
            if (!vrMode)
            {
                Vector3 euler = transform.localEulerAngles;
                yaw = euler.y;
                pitch = euler.x;
                if (pitch > 180f) pitch -= 360f; // Normalize pitch
                return;
            }
        }
        
        // In VR mode, head tracking is handled by Tracked Pose Driver on the camera itself.
        // We apply ARTIFICIAL rotation from the controller to the ArtificialLookPivot.
        if (vrMode)
        {
            InputAction actionToUse = vrControllerLookAction != null ? vrControllerLookAction.action : null;
            
            // Fallback: If reference is null, search globally for "VRLook" action
            if (actionToUse == null)
            {
                actionToUse = InputSystem.actions?.FindAction("VRLook");
            }

            Vector2 controllerDelta = Vector2.zero;
            if (actionToUse != null)
            {
                if (!actionToUse.enabled) actionToUse.Enable();
                controllerDelta = actionToUse.ReadValue<Vector2>();
            }

            Transform pivot = GetArtificialLookPivot();
            if (pivot != null && controllerDelta.sqrMagnitude > 0.001f)
            {
                // Logic: Rig (stable) -> Pivot (Controller Rotation) -> Camera (Head Tracking)
                
                // Calculate incremental rotation based on head orientation (transform.right/up)
                // This makes the rotation "relative from where the user is looking".
                float rotSpeed = sensitivity * 20f * Time.deltaTime * 60f;
                float rotX = controllerDelta.y * -rotSpeed; // Pitch
                float rotY = controllerDelta.x * rotSpeed;  // Yaw

                // Rotate around Camera's axes in world space
                pivot.Rotate(transform.right, rotX, Space.World);
                pivot.Rotate(transform.up, rotY, Space.World);
                
                // Note: We no longer need 'yaw' and 'pitch' variables or clamping for VR.
                // The Transform's rotation handles the state and allows infinite 360-degree look.
            }
            return;
        }
        
        if (mouseLookAction == null || !mouseLookAction.action.enabled) return;

        // Mouse delta / right stick etc.
        Vector2 delta = mouseLookAction.action.ReadValue<Vector2>();

        if (transform.up.y < 0)
        {
            delta.x = -delta.x;
        }

        // Horizontal: yaw, Vertical: pitch
        yaw   += delta.x * sensitivity;
        pitch -= delta.y * sensitivity;

        // Clamp vertical look so you don't break your neck
        // pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}