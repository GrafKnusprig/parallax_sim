using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manages loading and applying high-resolution textures, normal maps, bump maps,
/// and atmosphere/cloud layers for planetary bodies.
/// Now supports both Editor and Runtime loading with proper Resources folder usage.
/// </summary>
public class PlanetTextureManager : MonoBehaviour
{
    [System.Serializable]
    public class PlanetTextureSet
    {
        public long naifId;
        public string planetName;
        public Texture2D baseTexture;
        public Texture2D normalMap;
        public Texture2D bumpMap;
        public Texture2D specularMap;
        public Texture2D atmosphereTexture;
        public Texture2D nightLightsTexture;
        
        [Range(0f, 1f)]
        public float atmosphereOpacity = 0.5f;
        
        public bool hasAtmosphere = false;
        public bool isLoading = false;
        public bool loadingFailed = false;
    }

    [Header("Texture Loading Settings")]
    [SerializeField] private bool useAsyncLoading = true;
    [SerializeField] private float loadingDelay = 0.05f; // Delay between texture loads to prevent freezing
    
    [Header("Texture Path (relative to Assets/ or Resources/)")]
    [Tooltip("Path relative to Assets/ folder in Editor, or Resources/ folder in builds")]
    [SerializeField] private string texturesPath = "HighResTextures";
    
    [Header("Material References")]
    [Tooltip("Template material for planets (must be assigned!)")]
    [SerializeField] private Material planetMaterialTemplate;
    
    [Tooltip("Template material for atmospheres")]
    [SerializeField] private Material atmosphereMaterialTemplate;
    
    [Tooltip("Template material for the Sun")]
    [SerializeField] private Material sunMaterialTemplate;
    
    [Header("Atmosphere Settings")]
    [SerializeField] private bool enableAtmospheres = true;
    [SerializeField] private float atmosphereHeightMultiplier = 1.05f; // 5% larger than planet
    
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;
    
    private Dictionary<long, PlanetTextureSet> textureSets = new Dictionary<long, PlanetTextureSet>();
    private Dictionary<long, Material> createdMaterials = new Dictionary<long, Material>();
    private bool isInitialized = false;
    private Coroutine loadingCoroutine = null;
    
    // Texture file mapping based on what we found in HighResTextures
    private static readonly Dictionary<long, TextureInfo> textureDatabase = new Dictionary<long, TextureInfo>
    {
        // Solar System Planets
        { 10, new TextureInfo("8k_sun") },
        { 199, new TextureInfo("8k_mercury") },
        { 299, new TextureInfo("8k_venus_surface", atmosphereFile: "4k_venus_atmosphere") },
        { 399, new TextureInfo("8k_earth_daymap", normalFile: "8k_earth_normal_map", specularFile: "8k_earth_specular_map", 
                               atmosphereFile: "8k_earth_clouds", nightLightsFile: "8k_earth_nightmap") },
        { 301, new TextureInfo("8k_moon") },
        { 499, new TextureInfo("8k_mars") },
        { 599, new TextureInfo("8k_jupiter") },
        { 699, new TextureInfo("8k_saturn") },
        { 799, new TextureInfo("2k_uranus") },
        { 899, new TextureInfo("2k_neptune") },
        { 999, new TextureInfo("plutomap2k", bumpFile: "plutobump2k") },
        
        // Jupiter Moons
        { 501, new TextureInfo("Jupiter_Moons/Io") },
        { 502, new TextureInfo("Jupiter_Moons/Europa") },
        { 503, new TextureInfo("Jupiter_Moons/Ganymede") },
        { 504, new TextureInfo("Jupiter_Moons/Callisto") },
        
        // Saturn Moons
        { 601, new TextureInfo("Saturn_Moons/Mimas") },
        { 602, new TextureInfo("Saturn_Moons/Enceladus") },
        { 603, new TextureInfo("Saturn_Moons/Tethys") },
        { 604, new TextureInfo("Saturn_Moons/Dione") },
        { 605, new TextureInfo("Saturn_Moons/Rhea") },
        { 606, new TextureInfo("Saturn_Moons/TitanSurface", atmosphereFile: "Saturn_Moons/TitanClouds") },
        { 608, new TextureInfo("Saturn_Moons/Iapetus") },
    };
    
    /// <summary>
    /// Helper class to organize texture file information
    /// </summary>
    [System.Serializable]
    public class TextureInfo
    {
        public string baseFile;
        public string normalFile;
        public string bumpFile;
        public string specularFile;
        public string atmosphereFile;
        public string nightLightsFile;
        
        public TextureInfo(string baseFile, string normalFile = null, string bumpFile = null, 
                          string specularFile = null, string atmosphereFile = null, string nightLightsFile = null)
        {
            this.baseFile = baseFile;
            this.normalFile = normalFile;
            this.bumpFile = bumpFile;
            this.specularFile = specularFile;
            this.atmosphereFile = atmosphereFile;
            this.nightLightsFile = nightLightsFile;
        }
    }

    /// <summary>
    /// Initializes the texture manager and starts loading textures
    /// </summary>
    public void Initialize()
    {
        if (isInitialized)
        {
            if (verboseLogging) Debug.LogWarning("PlanetTextureManager already initialized!");
            return;
        }
        
        try
        {
            Debug.Log("=== PlanetTextureManager: Initializing ===");
            
            // Validate material templates
            if (!ValidateMaterialTemplates())
            {
                Debug.LogError("PlanetTextureManager: Material template validation failed! Check Inspector assignments.");
                return;
            }
            
            // Start loading textures
            if (useAsyncLoading)
            {
                loadingCoroutine = StartCoroutine(LoadAllTexturesAsync());
            }
            else
            {
                LoadAllTexturesSync();
            }
            
            isInitialized = true;
            Debug.Log("=== PlanetTextureManager: Initialization started ===");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"PlanetTextureManager: Exception during initialization: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Validates that required material templates are assigned
    /// </summary>
    private bool ValidateMaterialTemplates()
    {
        bool isValid = true;
        
        if (planetMaterialTemplate == null)
        {
            Debug.LogError("PlanetTextureManager: planetMaterialTemplate is NOT ASSIGNED! Please assign it in the Inspector.");
            isValid = false;
        }
        else
        {
            if (verboseLogging)
                Debug.Log($"✓ planetMaterialTemplate: {planetMaterialTemplate.name}, shader: {(planetMaterialTemplate.shader != null ? planetMaterialTemplate.shader.name : "NULL")}");
        }
        
        if (atmosphereMaterialTemplate == null)
        {
            Debug.LogWarning("PlanetTextureManager: atmosphereMaterialTemplate not assigned. Atmosphere layers will not be created.");
        }
        else if (verboseLogging)
        {
            Debug.Log($"✓ atmosphereMaterialTemplate: {atmosphereMaterialTemplate.name}");
        }
        
        if (sunMaterialTemplate == null)
        {
            Debug.LogWarning("PlanetTextureManager: sunMaterialTemplate not assigned. Sun will use fallback material.");
        }
        else if (verboseLogging)
        {
            Debug.Log($"✓ sunMaterialTemplate: {sunMaterialTemplate.name}");
        }
        
        return isValid;
    }
    
    /// <summary>
    /// Loads all available high-resolution textures synchronously (blocking)
    /// </summary>
    private void LoadAllTexturesSync()
    {
        Debug.Log("Loading textures synchronously...");
        int loadedCount = 0;
        
        foreach (var kvp in textureDatabase)
        {
            long naifId = kvp.Key;
            TextureInfo info = kvp.Value;
            
            PlanetTextureSet textureSet = LoadTextureSetForPlanet(naifId, info);
            
            if (textureSet != null && textureSet.baseTexture != null)
            {
                textureSets[naifId] = textureSet;
                loadedCount++;
            }
        }
        
        Debug.Log($"Loaded {loadedCount}/{textureDatabase.Count} texture sets synchronously");
    }
    
    /// <summary>
    /// Loads all available high-resolution textures asynchronously to prevent freezing
    /// </summary>
    private IEnumerator LoadAllTexturesAsync()
    {
        Debug.Log("Loading textures asynchronously...");
        int loadedCount = 0;
        int totalCount = textureDatabase.Count;
        
        foreach (var kvp in textureDatabase)
        {
            long naifId = kvp.Key;
            TextureInfo info = kvp.Value;
            
            // Mark as loading
            PlanetTextureSet textureSet = new PlanetTextureSet
            {
                naifId = naifId,
                planetName = info.baseFile,
                isLoading = true
            };
            textureSets[naifId] = textureSet;
            
            // Load the texture set
            textureSet = LoadTextureSetForPlanet(naifId, info);
            
            if (textureSet != null && textureSet.baseTexture != null)
            {
                textureSet.isLoading = false;
                textureSets[naifId] = textureSet;
                loadedCount++;
                
                if (verboseLogging)
                    Debug.Log($"[{loadedCount}/{totalCount}] Loaded texture set for {info.baseFile} (NAIF {naifId})");
            }
            else
            {
                textureSet.isLoading = false;
                textureSet.loadingFailed = true;
                if (verboseLogging)
                    Debug.LogWarning($"[{loadedCount}/{totalCount}] Failed to load textures for NAIF {naifId}");
            }
            
            // Small delay to prevent freezing
            yield return new WaitForSeconds(loadingDelay);
        }
        
        Debug.Log($"✓ Async texture loading complete: {loadedCount}/{totalCount} texture sets loaded");
    }
    
    /// <summary>
    /// Loads a complete texture set for a specific planet
    /// </summary>
    private PlanetTextureSet LoadTextureSetForPlanet(long naifId, TextureInfo info)
    {
        PlanetTextureSet textureSet = new PlanetTextureSet
        {
            naifId = naifId,
            planetName = info.baseFile
        };
        
        // Load base texture (required)
        textureSet.baseTexture = LoadTexture(info.baseFile);
        if (textureSet.baseTexture == null)
        {
            if (verboseLogging)
                Debug.LogWarning($"Could not load base texture for {info.baseFile}");
            return null;
        }
        
        // Load optional textures
        if (!string.IsNullOrEmpty(info.normalFile))
            textureSet.normalMap = LoadTexture(info.normalFile);
            
        if (!string.IsNullOrEmpty(info.bumpFile))
            textureSet.bumpMap = LoadTexture(info.bumpFile);
            
        if (!string.IsNullOrEmpty(info.specularFile))
            textureSet.specularMap = LoadTexture(info.specularFile);
            
        if (!string.IsNullOrEmpty(info.atmosphereFile))
        {
            textureSet.atmosphereTexture = LoadTexture(info.atmosphereFile);
            textureSet.hasAtmosphere = textureSet.atmosphereTexture != null;
            textureSet.atmosphereOpacity = 0.5f;
        }
        
        if (!string.IsNullOrEmpty(info.nightLightsFile))
            textureSet.nightLightsTexture = LoadTexture(info.nightLightsFile);
        
        return textureSet;
    }
    
    /// <summary>
    /// Unified texture loading method that works in both Editor and Runtime
    /// Tries multiple file extensions and handles both Resources and AssetDatabase
    /// </summary>
    private Texture2D LoadTexture(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;
        
#if UNITY_EDITOR
        // In Editor: Load from Assets folder using AssetDatabase
        return LoadTextureFromAssets(fileName);
#else
        // In Build: Load from Resources folder
        return LoadTextureFromResources(fileName);
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Loads texture from Assets folder (Editor only)
    /// </summary>
    private Texture2D LoadTextureFromAssets(string fileName)
    {
        string basePath = $"Assets/{texturesPath}/{fileName}";
        
        if (verboseLogging)
            Debug.Log($"  Attempting to load from: {basePath}");
        
        // Try different file extensions
        string[] extensions = { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
        
        foreach (string ext in extensions)
        {
            string fullPath = basePath + ext;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            
            if (texture != null)
            {
                if (verboseLogging)
                    Debug.Log($"  ✓ LOADED from HighResTextures: {fullPath} ({texture.width}x{texture.height})");
                return texture;
            }
        }
        
        // Also try loading without extension (AssetDatabase can handle this)
        Texture2D textureNoExt = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
        if (textureNoExt != null)
        {
            if (verboseLogging)
                Debug.Log($"  ✓ LOADED from HighResTextures: {basePath} ({textureNoExt.width}x{textureNoExt.height})");
            return textureNoExt;
        }
        
        if (verboseLogging)
            Debug.LogWarning($"  ✗ NOT FOUND in HighResTextures: {basePath}{{.jpg,.png,.tif}}");
        
        return null;
    }
#endif
    
    /// <summary>
    /// Loads texture from Resources folder (Runtime/Build)
    /// </summary>
    private Texture2D LoadTextureFromResources(string fileName)
    {
        // Construct path relative to Resources folder
        string resourcePath = $"{texturesPath}/{fileName}";
        
        // Resources.Load doesn't need file extensions
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        
        if (texture != null)
        {
            if (verboseLogging)
                Debug.Log($"  ✓ Loaded from Resources: {resourcePath} ({texture.width}x{texture.height})");
            return texture;
        }
        
        // Try without the base path (in case texture is directly in Resources)
        texture = Resources.Load<Texture2D>(fileName);
        if (texture != null)
        {
            if (verboseLogging)
                Debug.Log($"  ✓ Loaded from Resources: {fileName} ({texture.width}x{texture.height})");
            return texture;
        }
        
        if (verboseLogging)
            Debug.LogWarning($"  ✗ Not found in Resources: {resourcePath}");
        
        return null;
    }
    
    /// <summary>
    /// Creates or retrieves a material for a specific planet with high-res textures applied
    /// </summary>
    public Material GetOrCreatePlanetMaterial(long naifId, Material fallbackMaterial = null)
    {
        // Return cached material if already created
        if (createdMaterials.TryGetValue(naifId, out Material cachedMaterial))
        {
            if (cachedMaterial != null)
                return cachedMaterial;
        }
        
        // Check if we have textures for this planet
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
        {
            if (verboseLogging)
                Debug.Log($"No texture set available for NAIF {naifId}, using fallback");
            return fallbackMaterial;
        }
        
        // If still loading, return fallback temporarily
        if (textureSet.isLoading)
        {
            if (verboseLogging)
                Debug.Log($"Textures still loading for NAIF {naifId}, using fallback temporarily");
            return fallbackMaterial;
        }
        
        // If loading failed or no base texture, return fallback
        if (textureSet.loadingFailed || textureSet.baseTexture == null)
        {
            if (verboseLogging)
                Debug.LogWarning($"No base texture for NAIF {naifId}, using fallback");
            return fallbackMaterial;
        }
        
        // Determine which material template to use
        Material templateMaterial = GetMaterialTemplate(naifId);
        
        if (templateMaterial == null)
        {
            Debug.LogError($"No material template available for NAIF {naifId}!");
            return CreateEmergencyMaterial(naifId, fallbackMaterial);
        }
        
        // Verify template has a valid shader
        if (templateMaterial.shader == null)
        {
            Debug.LogError($"Template material {templateMaterial.name} has NULL shader for NAIF {naifId}!");
            return fallbackMaterial;
        }
        
        // Create material instance
        Material material = CreateMaterialFromTemplate(templateMaterial, naifId);
        
        // Apply all textures to the material
        ApplyTexturesToMaterial(material, textureSet);
        
        // Cache and return
        createdMaterials[naifId] = material;
        
        if (verboseLogging)
            Debug.Log($"✓ Created material for NAIF {naifId}: {material.name} with shader {material.shader.name}");
        
        return material;
    }
    
    /// <summary>
    /// Gets the appropriate material template for a given planet
    /// </summary>
    private Material GetMaterialTemplate(long naifId)
    {
        if (naifId == 10 && sunMaterialTemplate != null) // Sun
            return sunMaterialTemplate;
        
        return planetMaterialTemplate;
    }
    
    /// <summary>
    /// Creates a material instance from a template
    /// </summary>
    private Material CreateMaterialFromTemplate(Material template, long naifId)
    {
        Material material = new Material(template);
        material.name = $"Planet_{naifId}_Material";
        return material;
    }
    
    /// <summary>
    /// Creates an emergency fallback material when templates fail
    /// </summary>
    private Material CreateEmergencyMaterial(long naifId, Material fallbackMaterial)
    {
        if (fallbackMaterial != null)
            return fallbackMaterial;
        
        // Last resort: create a basic material with Standard shader
        Shader standardShader = Shader.Find("Standard");
        if (standardShader != null)
        {
            Material emergencyMaterial = new Material(standardShader);
            emergencyMaterial.name = $"Emergency_Material_{naifId}";
            Debug.LogWarning($"Created emergency material for NAIF {naifId}");
            return emergencyMaterial;
        }
        
        Debug.LogError($"Cannot create any material for NAIF {naifId}!");
        return null;
    }
    
    /// <summary>
    /// Applies all available textures to a material
    /// </summary>
    private void ApplyTexturesToMaterial(Material material, PlanetTextureSet textureSet)
    {
        // Apply base texture (main texture)
        if (textureSet.baseTexture != null)
        {
            material.mainTexture = textureSet.baseTexture;
            material.SetTexture("_MainTex", textureSet.baseTexture);
            
            if (verboseLogging)
                Debug.Log($"  Applied base texture: {textureSet.baseTexture.name}");
        }
        
        // Apply normal map
        if (textureSet.normalMap != null)
        {
            material.SetTexture("_BumpMap", textureSet.normalMap);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 1.0f);
            
            if (verboseLogging)
                Debug.Log($"  Applied normal map: {textureSet.normalMap.name}");
        }
        // Use bump map as normal map if no dedicated normal map exists
        else if (textureSet.bumpMap != null)
        {
            material.SetTexture("_BumpMap", textureSet.bumpMap);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 0.5f);
            
            if (verboseLogging)
                Debug.Log($"  Applied bump map as normal: {textureSet.bumpMap.name}");
        }
        
        // Apply specular map
        if (textureSet.specularMap != null)
        {
            material.SetTexture("_MetallicGlossMap", textureSet.specularMap);
            material.SetFloat("_Glossiness", 0.5f);
            material.SetFloat("_GlossMapScale", 1.0f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            
            if (verboseLogging)
                Debug.Log($"  Applied specular map: {textureSet.specularMap.name}");
        }
        
        // Apply night lights as emissive texture
        if (textureSet.nightLightsTexture != null)
        {
            material.SetTexture("_EmissionMap", textureSet.nightLightsTexture);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.white * 0.5f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            
            if (verboseLogging)
                Debug.Log($"  Applied night lights: {textureSet.nightLightsTexture.name}");
        }
    }
    
    /// <summary>
    /// Creates an atmosphere GameObject as a child of the planet
    /// </summary>
    public GameObject CreateAtmosphereLayer(long naifId, GameObject planetObject)
    {
        if (!enableAtmospheres)
        {
            if (verboseLogging)
                Debug.Log($"Atmospheres disabled, skipping for NAIF {naifId}");
            return null;
        }
        
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
        {
            if (verboseLogging)
                Debug.Log($"No texture set for NAIF {naifId}, cannot create atmosphere");
            return null;
        }
        
        if (!textureSet.hasAtmosphere || textureSet.atmosphereTexture == null)
        {
            if (verboseLogging)
                Debug.Log($"No atmosphere texture for NAIF {naifId}");
            return null;
        }
        
        if (atmosphereMaterialTemplate == null)
        {
            Debug.LogError($"Atmosphere material template not assigned! Cannot create atmosphere for NAIF {naifId}");
            return null;
        }
        
        // Create atmosphere sphere
        GameObject atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphere.name = $"{planetObject.name}_Atmosphere";
        atmosphere.transform.SetParent(planetObject.transform, false);
        atmosphere.transform.localPosition = Vector3.zero;
        atmosphere.transform.localScale = Vector3.one * atmosphereHeightMultiplier;
        
        // Remove collider
        Collider collider = atmosphere.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        
        // Create material instance
        Material atmosphereMaterial = new Material(atmosphereMaterialTemplate);
        atmosphereMaterial.name = $"Atmosphere_{naifId}";
        atmosphereMaterial.mainTexture = textureSet.atmosphereTexture;
        
        // Set transparency
        Color atmosphereColor = Color.white;
        atmosphereColor.a = textureSet.atmosphereOpacity;
        atmosphereMaterial.color = atmosphereColor;
        
        // Apply material
        MeshRenderer renderer = atmosphere.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = atmosphereMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        
        if (verboseLogging)
            Debug.Log($"✓ Created atmosphere layer for NAIF {naifId}");
        
        return atmosphere;
    }
    
    /// <summary>
    /// Checks if high-res textures are available for a given planet
    /// </summary>
    public bool HasHighResTextures(long naifId)
    {
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
            return false;
        
        return textureSet.baseTexture != null && !textureSet.loadingFailed;
    }
    
    /// <summary>
    /// Checks if textures are still loading for a planet
    /// </summary>
    public bool IsLoading(long naifId)
    {
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
            return false;
        
        return textureSet.isLoading;
    }
    
    /// <summary>
    /// Gets the texture set for a planet (if available)
    /// </summary>
    public PlanetTextureSet GetTextureSet(long naifId)
    {
        textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet);
        return textureSet;
    }
    
    /// <summary>
    /// Gets the number of successfully loaded texture sets
    /// </summary>
    public int GetLoadedTextureCount()
    {
        int count = 0;
        foreach (var textureSet in textureSets.Values)
        {
            if (textureSet.baseTexture != null && !textureSet.loadingFailed)
                count++;
        }
        return count;
    }
    
    /// <summary>
    /// Toggles atmosphere visibility for all planets
    /// </summary>
    public void SetAtmospheresEnabled(bool enabled)
    {
        enableAtmospheres = enabled;
        if (verboseLogging)
            Debug.Log($"Atmospheres {(enabled ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Force reload all textures (useful for debugging)
    /// </summary>
    public void ReloadAllTextures()
    {
        Debug.Log("Reloading all textures...");
        
        // Clear existing data
        textureSets.Clear();
        createdMaterials.Clear();
        
        // Stop any ongoing loading
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }
        
        isInitialized = false;
        
        // Restart initialization
        Initialize();
    }
}
