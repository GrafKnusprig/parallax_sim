using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages fake lighting for planets by updating shader properties with the sun's position.
/// Attach this to the GameObject containing your solar system or scene manager.
/// </summary>
public class PlanetLightingManager : MonoBehaviour
{
    [Header("Sun Settings")]
    [SerializeField] private Color sunColor = Color.white;
    [SerializeField] private float sunIntensity = 1.0f;
    [SerializeField] private float ambientLight = 0.05f;
    
    [Header("Auto-Find Settings")]
    [SerializeField] private bool debugLogging = false;
    
    [Header("References")]
    [SerializeField] private SolarSystemParallaxManager solarSystemManager;
    
    private Dictionary<Material, long> materialToNaifId = new Dictionary<Material, long>();
    private static readonly int SunDirectionID = Shader.PropertyToID("_SunDirection");
    private static readonly int SunColorID = Shader.PropertyToID("_SunColor");
    private static readonly int SunIntensityID = Shader.PropertyToID("_SunIntensity");
    private static readonly int AmbientLightID = Shader.PropertyToID("_AmbientLight");

    void Start()
    {
        // Auto-find solar system manager if needed
        if (solarSystemManager == null)
        {
            solarSystemManager = FindObjectOfType<SolarSystemParallaxManager>();
            if (solarSystemManager == null)
            {
                Debug.LogWarning("PlanetLightingManager: Could not find SolarSystemParallaxManager in scene");
            }
            else if (debugLogging)
            {
                Debug.Log("PlanetLightingManager: Found SolarSystemParallaxManager");
            }
        }
    }
    
    /// <summary>
    /// Register a material with its corresponding NAIF ID and real position
    /// Call this from SolarSystemParallaxManager when creating planets
    /// </summary>
    public void RegisterPlanetMaterial(Material material, long naifId, Vector3 realPosAu)
    {
        Debug.Log($"PlanetLightingManager: RegisterPlanetMaterial called for NAIF {naifId}");
        
        if (material == null)
        {
            Debug.LogError($"PlanetLightingManager: Material is NULL for NAIF {naifId}!");
            return;
        }
        
        if (material.shader == null)
        {
            Debug.LogError($"PlanetLightingManager: Material {material.name} has NULL shader for NAIF {naifId}!");
        }
        
        materialToNaifId[material] = naifId;
        
        // Calculate light direction: from planet to sun (sun is at origin)
        // Normalize the negative of the planet's position
        Vector3 sunDirection = -realPosAu.normalized;
        
        // Set lighting properties (only once - static positions)
        material.SetVector(SunDirectionID, sunDirection);
        material.SetColor(SunColorID, sunColor);
        material.SetFloat(SunIntensityID, sunIntensity);
        material.SetFloat(AmbientLightID, ambientLight);
        
        Debug.Log($"  ✓ Registered NAIF {naifId}: shader={material.shader?.name}, sunDir={sunDirection}, sunColor={sunColor}, intensity={sunIntensity}, ambient={ambientLight}");
    }
    
    /// <summary>
    /// Update sun color and intensity for all materials (can be called at runtime to change appearance)
    /// </summary>
    public void UpdateSunProperties(Color? newSunColor = null, float? newIntensity = null, float? newAmbient = null)
    {
        if (newSunColor.HasValue) sunColor = newSunColor.Value;
        if (newIntensity.HasValue) sunIntensity = newIntensity.Value;
        if (newAmbient.HasValue) ambientLight = newAmbient.Value;
        
        foreach (var kvp in materialToNaifId)
        {
            Material mat = kvp.Key;
            if (mat != null)
            {
                mat.SetColor(SunColorID, sunColor);
                mat.SetFloat(SunIntensityID, sunIntensity);
                mat.SetFloat(AmbientLightID, ambientLight);
            }
        }
    }
    
    /// <summary>
    /// Update sun color at runtime
    /// </summary>
    public void SetSunColor(Color color)
    {
        sunColor = color;
    }
    
    /// <summary>
    /// Update sun intensity at runtime
    /// </summary>
    public void SetSunIntensity(float intensity)
    {
        sunIntensity = intensity;
    }
    
    /// <summary>
    /// Update ambient light at runtime
    /// </summary>
    public void SetAmbientLight(float ambient)
    {
        ambientLight = ambient;
    }
}
