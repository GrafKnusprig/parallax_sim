using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Manages loading and applying high-resolution textures, normal maps, bump maps,
/// and atmosphere/cloud layers for planetary bodies.
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
    }

    [Header("Texture Settings")]
    [SerializeField] private string highResTexturesPath = "HighResTextures";
    
    [Header("Material References")]
    [SerializeField] private Material planetMaterialTemplate;
    [SerializeField] private Material atmosphereMaterialTemplate;
    [SerializeField] private Material sunMaterialTemplate;
    
    [Header("Atmosphere Settings")]
    [SerializeField] private bool enableAtmospheres = true;
    [SerializeField] private float atmosphereHeightMultiplier = 1.05f; // 5% larger than planet
    
    private Dictionary<long, PlanetTextureSet> textureSets = new Dictionary<long, PlanetTextureSet>();
    private Dictionary<long, Material> createdMaterials = new Dictionary<long, Material>();
    
    // Texture file mapping based on what we found in HighResTextures
    private static readonly Dictionary<long, string> textureFileNames = new Dictionary<long, string>
    {
        // Solar System Planets
        { 10, "8k_sun" },
        { 199, "8k_mercury" },
        { 299, "8k_venus_surface" },
        { 399, "8k_earth_daymap" },
        { 301, "8k_moon" },
        { 499, "8k_mars" },
        { 599, "8k_jupiter" },
        { 699, "8k_saturn" },
        { 799, "2k_uranus" },
        { 899, "2k_neptune" },
        { 999, "plutomap2k" },
        
        // Jupiter Moons
        { 501, "Jupiter_Moons/Io" },
        { 502, "Jupiter_Moons/Europa" },
        { 503, "Jupiter_Moons/Ganymede" },
        { 504, "Jupiter_Moons/Callisto" },
        
        // Saturn Moons
        { 601, "Saturn_Moons/Mimas" },
        { 602, "Saturn_Moons/Enceladus" },
        { 603, "Saturn_Moons/Tethys" },
        { 604, "Saturn_Moons/Dione" },
        { 605, "Saturn_Moons/Rhea" },
        { 606, "Saturn_Moons/TitanSurface" },
        { 608, "Saturn_Moons/Iapetus" },
    };
    
    // Planets with atmosphere/cloud layers
    private static readonly Dictionary<long, string> atmosphereFileNames = new Dictionary<long, string>
    {
        { 399, "8k_earth_clouds" },
        { 299, "4k_venus_atmosphere" },
        { 606, "Saturn_Moons/TitanClouds" },
    };
    
    // Planets with normal maps
    private static readonly Dictionary<long, string> normalMapFileNames = new Dictionary<long, string>
    {
        { 399, "8k_earth_normal_map" },
    };
    
    // Planets with bump maps
    private static readonly Dictionary<long, string> bumpMapFileNames = new Dictionary<long, string>
    {
        { 999, "plutobump2k" },
    };
    
    // Planets with specular maps
    private static readonly Dictionary<long, string> specularMapFileNames = new Dictionary<long, string>
    {
        { 399, "8k_earth_specular_map" },
    };
    
    // Planets with night lights
    private static readonly Dictionary<long, string> nightLightsFileNames = new Dictionary<long, string>
    {
        { 399, "8k_earth_nightmap" },
    };

    public void Initialize()
    {
        LoadAllTextures();
    }
    
    /// <summary>
    /// Loads all available high-resolution textures from the Assets folder
    /// </summary>
    private void LoadAllTextures()
    {
        foreach (var kvp in textureFileNames)
        {
            long naifId = kvp.Key;
            string baseName = kvp.Value;
            
            PlanetTextureSet textureSet = new PlanetTextureSet
            {
                naifId = naifId,
                planetName = baseName
            };
            
            // Load base texture
            textureSet.baseTexture = LoadTextureFromResources(baseName);
            
            // Load normal map if available
            if (normalMapFileNames.ContainsKey(naifId))
            {
                textureSet.normalMap = LoadTextureFromResources(normalMapFileNames[naifId]);
            }
            
            // Load bump map if available
            if (bumpMapFileNames.ContainsKey(naifId))
            {
                textureSet.bumpMap = LoadTextureFromResources(bumpMapFileNames[naifId]);
            }
            
            // Load specular map if available
            if (specularMapFileNames.ContainsKey(naifId))
            {
                textureSet.specularMap = LoadTextureFromResources(specularMapFileNames[naifId]);
            }
            
            // Load atmosphere/cloud layer if available
            if (atmosphereFileNames.ContainsKey(naifId))
            {
                textureSet.atmosphereTexture = LoadTextureFromResources(atmosphereFileNames[naifId]);
                textureSet.hasAtmosphere = true;
                textureSet.atmosphereOpacity = 0.5f; // Default opacity
            }
            
            // Load night lights if available
            if (nightLightsFileNames.ContainsKey(naifId))
            {
                textureSet.nightLightsTexture = LoadTextureFromResources(nightLightsFileNames[naifId]);
            }
            
            if (textureSet.baseTexture != null)
            {
                textureSets[naifId] = textureSet;
                Debug.Log($"Loaded texture set for {baseName} (ID: {naifId})");
            }
        }
        
        Debug.Log($"Loaded {textureSets.Count} high-resolution texture sets");
    }
    
    /// <summary>
    /// Loads a texture from the Assets folder (editor) or Resources folder (build)
    /// </summary>
    private Texture2D LoadTextureFromResources(string fileName)
    {
#if UNITY_EDITOR
        // In editor, load from Assets folder directly using AssetDatabase
        string assetPath = $"Assets/{highResTexturesPath}/{fileName}";
        
        // Try different extensions
        string[] extensions = { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
        foreach (string ext in extensions)
        {
            string fullPath = assetPath + ext;
            Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            if (texture != null)
            {
                return texture;
            }
        }
        
        // If not found, return null
        return null;
#else
        // In build, load from Resources folder
        string resourcePath = Path.Combine(highResTexturesPath, fileName);
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        
        if (texture == null)
        {
            // Try without extension
            string pathWithoutExt = Path.Combine(highResTexturesPath, Path.GetFileNameWithoutExtension(fileName));
            texture = Resources.Load<Texture2D>(pathWithoutExt);
        }
        
        return texture;
#endif
    }
    
    /// <summary>
    /// Creates or retrieves a material for a specific planet with high-res textures applied
    /// </summary>
    public Material GetOrCreatePlanetMaterial(long naifId, Material fallbackMaterial = null)
    {
        // Return cached material if already created
        if (createdMaterials.TryGetValue(naifId, out Material cachedMaterial))
        {
            return cachedMaterial;
        }
        
        // Check if we have textures for this planet
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
        {
            return fallbackMaterial;
        }
        
        // Determine which material template to use
        Material templateMaterial;
        if (naifId == 10) // Sun
        {
            templateMaterial = sunMaterialTemplate;
        }
        else
        {
            templateMaterial = planetMaterialTemplate;
        }
        
        // Check if template material is assigned
        if (templateMaterial == null)
        {
            Debug.LogError($"Material template not assigned in PlanetTextureManager! Please assign planetMaterialTemplate or sunMaterialTemplate in the Inspector.");
            return fallbackMaterial;
        }
        
        // Create a new material instance from the template
        Material material = new Material(templateMaterial);
        material.name = $"Planet_{naifId}_Material";
        
        // Apply base texture
        if (textureSet.baseTexture != null)
        {
            material.mainTexture = textureSet.baseTexture;
            material.SetTexture("_MainTex", textureSet.baseTexture);
        }
        
        // Apply normal map
        if (textureSet.normalMap != null)
        {
            material.SetTexture("_BumpMap", textureSet.normalMap);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 1.0f);
        }
        
        // Apply bump map (as normal map if no dedicated normal map exists)
        if (textureSet.bumpMap != null && textureSet.normalMap == null)
        {
            material.SetTexture("_BumpMap", textureSet.bumpMap);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 0.5f);
        }
        
        // Apply specular map
        if (textureSet.specularMap != null)
        {
            material.SetTexture("_MetallicGlossMap", textureSet.specularMap);
            material.SetFloat("_Glossiness", 0.5f);
            material.SetFloat("_GlossMapScale", 1.0f);
            material.EnableKeyword("_METALLICGLOSSMAP");
        }
        
        // Apply emissive for night lights (Earth)
        if (textureSet.nightLightsTexture != null)
        {
            material.SetTexture("_EmissionMap", textureSet.nightLightsTexture);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.white * 0.5f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        
        createdMaterials[naifId] = material;
        return material;
    }
    
    /// <summary>
    /// Creates an atmosphere GameObject as a child of the planet
    /// </summary>
    public GameObject CreateAtmosphereLayer(long naifId, GameObject planetObject)
    {
        if (!enableAtmospheres)
            return null;
            
        if (!textureSets.TryGetValue(naifId, out PlanetTextureSet textureSet))
            return null;
            
        if (!textureSet.hasAtmosphere || textureSet.atmosphereTexture == null)
            return null;
        
        // Check if atmosphere material template is assigned
        if (atmosphereMaterialTemplate == null)
        {
            Debug.LogError("Atmosphere material template not assigned in PlanetTextureManager! Please assign atmosphereMaterialTemplate in the Inspector.");
            return null;
        }
        
        // Create atmosphere sphere slightly larger than the planet
        GameObject atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphere.name = $"{planetObject.name}_Atmosphere";
        atmosphere.transform.SetParent(planetObject.transform, false);
        atmosphere.transform.localPosition = Vector3.zero;
        atmosphere.transform.localScale = Vector3.one * atmosphereHeightMultiplier;
        
        // Remove collider
        Collider collider = atmosphere.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        
        // Create material instance from template
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
        
        return atmosphere;
    }
    
    /// <summary>
    /// Checks if high-res textures are available for a given planet
    /// </summary>
    public bool HasHighResTextures(long naifId)
    {
        return textureSets.ContainsKey(naifId);
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
    /// Toggles atmosphere visibility for all planets
    /// </summary>
    public void SetAtmospheresEnabled(bool enabled)
    {
        enableAtmospheres = enabled;
    }
}
