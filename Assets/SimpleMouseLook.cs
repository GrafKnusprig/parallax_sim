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
        if (vrMode)
        {
            if (vrLookAction != null) vrLookAction.action.Enable();
            if (vrControllerLookAction != null) vrControllerLookAction.action.Enable();
        }
        else if (!vrMode && mouseLookAction != null)
        {
            mouseLookAction.action.Enable();
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

    private void Start()
    {
        Vector3 euler = transform.localEulerAngles;
        yaw = euler.y;
        pitch = euler.x;

        UpdateCursorState();
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
        // Update cursor state based on menu
        UpdateCursorState();
        
        // Skip mouse look when menu is open
        if (SolarSystemUIManager.IsMenuOpen)
        {
            return;
        }
        
        // Skip mouse look when autopilot is controlling the camera or when Orbiting
        if (SolarSystemParallaxManager.IsAutopilotActive || SolarSystemParallaxManager.IsOrbiting)
        {
            // Sync yaw/pitch from current rotation so there's no jump when autopilot ends
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
            if (pitch > 180f) pitch -= 360f; // Normalize pitch
            return;
        }
        
        // In VR mode, head tracking is handled by Tracked Pose Driver.
        // We add manual rotation from the right controller trackpad/stick.
        if (vrMode)
        {
            if (vrControllerLookAction != null)
            {
                Vector2 controllerDelta = vrControllerLookAction.action.ReadValue<Vector2>();
                
                // Only use horizontal for snap/smooth turn if preferred, 
                // but here we implement smooth look as requested.
                yaw += controllerDelta.x * sensitivity * 20f; // Scale sensitivity for controller
                pitch -= controllerDelta.y * sensitivity * 20f;
                // pitch = Mathf.Clamp(pitch, -89f, 89f);

                // Note: In VR, we usually rotate a 'Rig' or parent object to avoid making the user sick.
                // However, since this script is likely on the Camera or a proxy, we'll apply it locally.
                // Head tracking will be relative to this base rotation.
                transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            return;
        }
        
        // Non-VR mode: use mouse look
        if (mouseLookAction == null) return;

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