using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple helper script to toggle atmosphere visibility at runtime
/// Attach to the same GameObject as SolarSystemParallaxManager
/// </summary>
public class AtmosphereToggle : MonoBehaviour
{
    [Header("Controls")]
    [Tooltip("Key to toggle atmospheres on/off")]
    [SerializeField] private Key toggleKey = Key.A;
    
    [Header("UI Display")]
    [SerializeField] private bool showToggleMessage = true;
    
    private PlanetTextureManager textureManager;
    private bool atmospheresEnabled = true;
    private float messageDisplayTime = 0f;
    private const float MESSAGE_DURATION = 2f;
    
    private void Start()
    {
        textureManager = GetComponent<PlanetTextureManager>();
        if (textureManager == null)
        {
            Debug.LogWarning("AtmosphereToggle: PlanetTextureManager not found on this GameObject");
        }
    }
    
    private void Update()
    {
        if (textureManager == null) return;
        
        // Check for toggle key press
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            ToggleAtmospheres();
        }
        
        // Update message display timer
        if (messageDisplayTime > 0f)
        {
            messageDisplayTime -= Time.deltaTime;
        }
    }
    
    private void ToggleAtmospheres()
    {
        atmospheresEnabled = !atmospheresEnabled;
        textureManager.SetAtmospheresEnabled(atmospheresEnabled);
        
        if (showToggleMessage)
        {
            messageDisplayTime = MESSAGE_DURATION;
            Debug.Log($"Atmospheres {(atmospheresEnabled ? "ENABLED" : "DISABLED")}");
        }
    }
    
    private void OnGUI()
    {
        if (!showToggleMessage || messageDisplayTime <= 0f) return;
        
        // Display toggle message
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 24;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = atmospheresEnabled ? Color.green : Color.red;
        style.alignment = TextAnchor.MiddleCenter;
        
        string message = $"Atmospheres: {(atmospheresEnabled ? "ON" : "OFF")}";
        float alpha = Mathf.Clamp01(messageDisplayTime / 0.5f);
        Color color = style.normal.textColor;
        color.a = alpha;
        style.normal.textColor = color;
        
        GUI.Label(new Rect(Screen.width / 2 - 150, 100, 300, 40), message, style);
    }
}
