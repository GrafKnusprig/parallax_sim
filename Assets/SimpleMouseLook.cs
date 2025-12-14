using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleMouseLook : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private bool vrMode = true;
    [Tooltip("When enabled, uses VR head tracking. When disabled, uses mouse look.")]
    
    [Header("Input")]
    [SerializeField] private InputActionReference vrLookAction; // VR head tracking (for VR mode)
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
        if (vrMode && vrLookAction != null)
            vrLookAction.action.Enable();
        else if (!vrMode && mouseLookAction != null)
            mouseLookAction.action.Enable();
    }
    
    private void DisableAllLookActions()
    {
        if (vrLookAction != null)
            vrLookAction.action.Disable();
        if (mouseLookAction != null)
            mouseLookAction.action.Disable();
    }

    private void Start()
    {
        Vector3 euler = transform.localEulerAngles;
        yaw = euler.y;
        pitch = euler.x;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        // In VR mode, VR head tracking handles rotation, so we skip manual rotation
        if (vrMode)
        {
            // VR head tracking is handled by the Tracked Pose Driver component
            // No manual rotation needed here
            return;
        }
        
        // Non-VR mode: use mouse look
        if (mouseLookAction == null) return;

        // Mouse delta / right stick etc.
        Vector2 delta = mouseLookAction.action.ReadValue<Vector2>();

        // Horizontal: yaw, Vertical: pitch
        yaw   += delta.x * sensitivity;
        pitch -= delta.y * sensitivity;

        // Clamp vertical look so you don't break your neck
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}