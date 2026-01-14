using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Helper component for testing and debugging the texture loading system
/// Attach to the same GameObject as PlanetTextureManager for quick testing
/// </summary>
public class PlanetTextureDebugger : MonoBehaviour
{
    [Header("Debug Controls")]
    [SerializeField] private bool showTextureStats = true;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
    [SerializeField] private KeyCode statsKey = KeyCode.T;
    
    private PlanetTextureManager textureManager;
    
    private void Start()
    {
        textureManager = GetComponent<PlanetTextureManager>();
        if (textureManager == null)
        {
            Debug.LogError("PlanetTextureDebugger: No PlanetTextureManager found on this GameObject!");
        }
    }
    
    private void Update()
    {
        if (textureManager == null)
            return;
        
        // Press R to reload all textures
        if (Input.GetKeyDown(reloadKey))
        {
            Debug.Log("Manually reloading all textures...");
            textureManager.ReloadAllTextures();
        }
        
        // Press T to show texture stats
        if (Input.GetKeyDown(statsKey))
        {
            ShowTextureStatistics();
        }
    }
    
    private void OnGUI()
    {
        if (!showTextureStats || textureManager == null)
            return;
        
        // Display texture loading statistics in top-right corner
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 14;
        style.normal.textColor = Color.cyan;
        
        int loadedCount = textureManager.GetLoadedTextureCount();
        string statsText = $"Textures Loaded: {loadedCount}\n" +
                          $"Press [{reloadKey}] to reload\n" +
                          $"Press [{statsKey}] for details";
        
        GUI.Box(new Rect(Screen.width - 250, 10, 240, 70), statsText, style);
    }
    
    /// <summary>
    /// Logs detailed statistics about loaded textures
    /// </summary>
    private void ShowTextureStatistics()
    {
        Debug.Log("=== Planet Texture Statistics ===");
        
        int totalPlanets = 0;
        int loadedPlanets = 0;
        int withNormals = 0;
        int withSpecular = 0;
        int withAtmosphere = 0;
        int withNightLights = 0;
        long totalMemoryMB = 0;
        
        // Test common NAIF IDs
        long[] testIds = { 10, 199, 299, 399, 301, 499, 599, 699, 799, 899, 999,
                          501, 502, 503, 504, 601, 602, 603, 604, 605, 606, 608 };
        
        foreach (long naifId in testIds)
        {
            totalPlanets++;
            var textureSet = textureManager.GetTextureSet(naifId);
            
            if (textureSet != null && textureSet.baseTexture != null)
            {
                loadedPlanets++;
                
                if (textureSet.normalMap != null) withNormals++;
                if (textureSet.specularMap != null) withSpecular++;
                if (textureSet.atmosphereTexture != null) withAtmosphere++;
                if (textureSet.nightLightsTexture != null) withNightLights++;
                
                // Estimate memory usage (rough approximation)
                totalMemoryMB += EstimateTextureSizeMB(textureSet);
                
                Debug.Log($"  ✓ NAIF {naifId}: {textureSet.planetName} " +
                         $"({textureSet.baseTexture.width}x{textureSet.baseTexture.height})");
            }
        }
        
        Debug.Log($"\nSummary:");
        Debug.Log($"  Total Planets: {totalPlanets}");
        Debug.Log($"  Loaded Successfully: {loadedPlanets}");
        Debug.Log($"  With Normal Maps: {withNormals}");
        Debug.Log($"  With Specular Maps: {withSpecular}");
        Debug.Log($"  With Atmospheres: {withAtmosphere}");
        Debug.Log($"  With Night Lights: {withNightLights}");
        Debug.Log($"  Estimated Memory: ~{totalMemoryMB} MB");
        Debug.Log("=================================");
    }
    
    /// <summary>
    /// Estimates memory usage for a texture set (rough approximation)
    /// </summary>
    private long EstimateTextureSizeMB(PlanetTextureManager.PlanetTextureSet textureSet)
    {
        long totalBytes = 0;
        
        if (textureSet.baseTexture != null)
            totalBytes += EstimateTextureMB(textureSet.baseTexture);
        
        if (textureSet.normalMap != null)
            totalBytes += EstimateTextureMB(textureSet.normalMap);
        
        if (textureSet.specularMap != null)
            totalBytes += EstimateTextureMB(textureSet.specularMap);
        
        if (textureSet.atmosphereTexture != null)
            totalBytes += EstimateTextureMB(textureSet.atmosphereTexture);
        
        if (textureSet.nightLightsTexture != null)
            totalBytes += EstimateTextureMB(textureSet.nightLightsTexture);
        
        if (textureSet.bumpMap != null)
            totalBytes += EstimateTextureMB(textureSet.bumpMap);
        
        return totalBytes;
    }
    
    /// <summary>
    /// Estimates memory usage for a single texture in MB
    /// </summary>
    private long EstimateTextureMB(Texture2D texture)
    {
        if (texture == null)
            return 0;
        
        // Very rough estimation: width * height * 4 bytes per pixel (RGBA)
        // In reality, this depends on format, compression, mipmaps, etc.
        long bytes = texture.width * texture.height * 4L;
        return bytes / (1024 * 1024); // Convert to MB
    }
}

#if UNITY_EDITOR
/// <summary>
/// Custom editor for PlanetTextureDebugger with helpful buttons
/// </summary>
[CustomEditor(typeof(PlanetTextureDebugger))]
public class PlanetTextureDebuggerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        PlanetTextureDebugger debugger = (PlanetTextureDebugger)target;
        PlanetTextureManager manager = debugger.GetComponent<PlanetTextureManager>();
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Actions", EditorStyles.boldLabel);
        
        if (manager == null)
        {
            EditorGUILayout.HelpBox("No PlanetTextureManager found on this GameObject!", MessageType.Error);
            return;
        }
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Show Statistics"))
        {
            debugger.SendMessage("ShowTextureStatistics");
        }
        
        if (GUILayout.Button("Reload All Textures"))
        {
            if (EditorUtility.DisplayDialog("Reload Textures", 
                "This will clear and reload all textures. Continue?", "Yes", "Cancel"))
            {
                manager.ReloadAllTextures();
            }
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Copy Textures to Resources"))
        {
            EditorApplication.ExecuteMenuItem("Tools/Planet Textures/Copy to Resources Folder");
        }
        
        if (GUILayout.Button("Open Documentation"))
        {
            string docPath = "Assets/PLANET_TEXTURE_SYSTEM_README.md";
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(docPath, 1);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Runtime Controls:\n" +
            "• Press R to reload textures\n" +
            "• Press T to show statistics in console\n" +
            "• Stats overlay shown in top-right corner during play",
            MessageType.Info);
    }
}
#endif
