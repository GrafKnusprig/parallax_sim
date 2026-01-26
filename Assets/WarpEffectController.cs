using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SolarSystemParallaxManager))]
public class WarpEffectController : MonoBehaviour
{
    [Header("Effect Settings")]
    [Tooltip("Speed in AU/sec at which the effect starts appearing")]
    [SerializeField] private float activationSpeed = 0.01f; // Lowered to start sooner
    
    [Tooltip("Speed in AU/sec at which the effect reaches max intensity")]
    [SerializeField] private float maxEffectSpeed = 2.0f; // Lowered from 50.0f to ramp up much faster
    
    [Tooltip("Radius of the warp tunnel cylinder")]
    [SerializeField] private float tunnelRadius = 8.0f; // Increased from 5.0f
    
    [Tooltip("Length of the warp tunnel cylinder")]
    [SerializeField] private float tunnelLength = 40.0f;
    
    [Tooltip("Number of segments in the cylinder")]
    [SerializeField] private int tunnelSegments = 32;

    [Header("Shader References")]
    [SerializeField] private Shader warpShader;
    
    private GameObject warpTunnel;
    private Material warpMaterial;
    private SolarSystemParallaxManager parallaxManager;
    private Camera currentCamera;
    
    private void Start()
    {
        parallaxManager = GetComponent<SolarSystemParallaxManager>();
        
        if (warpShader == null)
        {
            warpShader = Shader.Find("Custom/WarpEffect");
            if (warpShader == null)
            {
                Debug.LogError("WarpEffectController: Could not find shader 'Custom/WarpEffect'");
                enabled = false;
                return;
            }
        }
        
        CreateWarpTunnel();
    }
    
    private void Update()
    {
        // Handle camera changes (e.g. VR vs Desktop or scene changes)
        Camera cam = Camera.main; // Better to get this from parallaxManager if possible, but Main is usually safe
        
        if (cam != currentCamera)
        {
            currentCamera = cam;
            if (warpTunnel != null && currentCamera != null)
            {
                warpTunnel.transform.SetParent(currentCamera.transform);
                warpTunnel.transform.localPosition = Vector3.zero;
                warpTunnel.transform.localRotation = Quaternion.identity;
                // Rotate cylinder to align with Z axis if needed. 
                // Our procedural generation will align along Z.
            }
        }
        
        if (warpTunnel == null || parallaxManager == null) return;
        
        float currentSpeed = parallaxManager.CurrentSpeedAuPerSec;
        // In autopilot, use ActualSpeedAuPerSec if available primarily
        float actualSpeed = parallaxManager.ActualSpeedAuPerSec;
        
        // Use the larger of the two to catch both manual and autopilot movement
        float effectiveSpeed = Mathf.Max(currentSpeed, actualSpeed);
        
        float speedRatio = Mathf.InverseLerp(activationSpeed, maxEffectSpeed, effectiveSpeed);
        
        // Smooth transition
        float targetAlpha = speedRatio;
        
        // Update shader properties
        if (warpMaterial != null)
        {
            // Speed factor for UV scrolling
            float scrollSpeed = effectiveSpeed * 0.5f; 
            if (scrollSpeed > 10.0f) scrollSpeed = 10.0f; // Cap visual speed
            
            warpMaterial.SetFloat("_Speed", scrollSpeed);
            warpMaterial.SetFloat("_Alpha", targetAlpha);
            
            // Adjust density/streak length based on speed if desired
            warpMaterial.SetFloat("_StreakLength", 1.0f + speedRatio * 5.0f);
        }
        
        // Hide/Show object based on alpha to save fill rate
        if (targetAlpha <= 0.01f)
        {
            if (warpTunnel.activeSelf) warpTunnel.SetActive(false);
        }
        else
        {
            if (!warpTunnel.activeSelf) warpTunnel.SetActive(true);
        }
    }
    
    private void CreateWarpTunnel()
    {
        if (warpTunnel != null) Destroy(warpTunnel);
        
        warpTunnel = new GameObject("WarpTunnelEffect");
        
        // Create Mesh
        Mesh mesh = new Mesh();
        mesh.name = "WarpTunnelMesh";
        
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        
        // Generate Cylinder (Open ended)
        // Oriented along Z axis
        
        float angleStep = 360.0f / tunnelSegments;
        
        // We create a cylinder with just 2 rings (start and end) is enough for simple streaks?
        // No, we want some length. Let's do 2 rings: -Z and +Z.
        // Actually, let's put it IN FRONT of the camera.
        // Camera at 0,0,0. Tunnel from 0 to +Length.
        
        float zStart = -10.0f; // Start behind camera
        float zEnd = tunnelLength; // End far ahead
        
        for (int i = 0; i <= tunnelSegments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * tunnelRadius;
            float y = Mathf.Sin(angle) * tunnelRadius;
            
            // Ring 1 (Start)
            vertices.Add(new Vector3(x, y, zStart));
            uvs.Add(new Vector2((float)i / tunnelSegments, 0.0f));
            
            // Ring 2 (End)
            vertices.Add(new Vector3(x, y, zEnd));
            uvs.Add(new Vector2((float)i / tunnelSegments, 1.0f)); // UV Y is along length
        }
        
        // Triangles
        for (int i = 0; i < tunnelSegments; i++)
        {
            int baseIndex = i * 2;
            
            // Quad
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }
        
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        MeshFilter mf = warpTunnel.AddComponent<MeshFilter>();
        mf.mesh = mesh;
        
        MeshRenderer mr = warpTunnel.AddComponent<MeshRenderer>();
        warpMaterial = new Material(warpShader);
        mr.material = warpMaterial;
        
        // Initial state
        warpTunnel.SetActive(false);
    }
    
    private void OnDestroy()
    {
        if (warpTunnel != null) Destroy(warpTunnel);
        if (warpMaterial != null) Destroy(warpMaterial);
    }
}
