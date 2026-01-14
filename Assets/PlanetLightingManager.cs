using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages fake lighting for planets by updating shader properties with the sun's position.
/// Attach this to the GameObject containing your solar system or scene manager.
/// </summary>
public class PlanetLightingManager : MonoBehaviour
{
    [Header("Sun Settings")]
    [SerializeField] private Transform sunTransform;
    [SerializeField] private Color sunColor = Color.white;
    [SerializeField] private float sunIntensity = 1.0f;
    [SerializeField] private float ambientLight = 0.1f;
    
    [Header("Auto-Find Settings")]
    [SerializeField] private bool autoFindSun = true;
    [SerializeField] private long sunNaifId = 10; // NAIF ID for the Sun
    
    [Header("Material Management")]
    [SerializeField] private bool autoFindMaterials = true;
    [SerializeField] private Material[] planetMaterials;
    
    [Header("References")]
    [SerializeField] private SolarSystemParallaxManager solarSystemManager;
    
    private List<Material> managedMaterials = new List<Material>();
    private static readonly int SunPositionID = Shader.PropertyToID("_SunPosition");
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
        }
        
        // Auto-find sun if needed
        if (autoFindSun && sunTransform == null)
        {
            FindSunTransform();
        }
        
        // Gather materials
        GatherMaterials();
        
        // Initial update
        UpdateLighting();
    }

    void Update()
    {
        // Re-find sun if it was lost (e.g., scene reload)
        if (sunTransform == null && autoFindSun)
        {
            FindSunTransform();
        }
        
        UpdateLighting();
    }
    
    /// <summary>
    /// Finds the sun GameObject by searching for the object with NAIF ID 10
    /// </summary>
    private void FindSunTransform()
    {
        // Search all transforms with "ID: 10" in the name (NAIF ID for Sun)
        Transform[] allTransforms = FindObjectsOfType<Transform>();
        foreach (Transform t in allTransforms)
        {
            // The solar system manager creates objects with names like "Sun (ID: 10)"
            if (t.name.Contains($"ID: {sunNaifId}") || t.name.Contains($"ID:{sunNaifId}"))
            {
                sunTransform = t;
                Debug.Log($"PlanetLightingManager: Found sun at {t.name}");
                return;
            }
        }
    }
    
    /// <summary>
    /// Finds all materials that use the EnhancedPlanet shader
    /// </summary>
    private void GatherMaterials()
    {
        managedMaterials.Clear();
        
        // Add manually assigned materials
        if (planetMaterials != null)
        {
            foreach (Material mat in planetMaterials)
            {
                if (mat != null && !managedMaterials.Contains(mat))
                {
                    managedMaterials.Add(mat);
                }
            }
        }
        
        // Auto-find materials if enabled
        if (autoFindMaterials)
        {
            MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null && mat.shader != null && 
                        mat.shader.name == "Custom/EnhancedPlanet" &&
                        !managedMaterials.Contains(mat))
                    {
                        managedMaterials.Add(mat);
                    }
                }
            }
        }
        
        Debug.Log($"PlanetLightingManager: Managing {managedMaterials.Count} materials");
    }
    
    /// <summary>
    /// Updates all managed materials with current sun position and properties
    /// </summary>
    private void UpdateLighting()
    {
        if (sunTransform == null)
            return;
            
        Vector3 sunPosition = sunTransform.position;
        
        foreach (Material mat in managedMaterials)
        {
            if (mat != null)
            {
                mat.SetVector(SunPositionID, sunPosition);
                mat.SetColor(SunColorID, sunColor);
                mat.SetFloat(SunIntensityID, sunIntensity);
                mat.SetFloat(AmbientLightID, ambientLight);
            }
        }
    }
    
    /// <summary>
    /// Call this to refresh the material list (e.g., after creating new planets)
    /// </summary>
    public void RefreshMaterials()
    {
        GatherMaterials();
    }
    
    /// <summary>
    /// Manually set the sun transform
    /// </summary>
    public void SetSunTransform(Transform sun)
    {
        sunTransform = sun;
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
