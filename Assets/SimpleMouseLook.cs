using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleMouseLook : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference lookAction; // Player/Look (Vector2)

    [Header("Settings")]
    [SerializeField] private float sensitivity = 0.1f;
    [SerializeField] private bool lockCursor = true;

    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        if (lookAction != null)
            lookAction.action.Enable();
    }

    private void OnDisable()
    {
        if (lookAction != null)
            lookAction.action.Disable();
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
        if (lookAction == null) return;

        // Mouse delta / right stick etc.
        Vector2 delta = lookAction.action.ReadValue<Vector2>();

        // Horizontal: yaw, Vertical: pitch
        yaw   += delta.x * sensitivity;
        pitch -= delta.y * sensitivity;

        // Clamp vertical look so you don't break your neck
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}