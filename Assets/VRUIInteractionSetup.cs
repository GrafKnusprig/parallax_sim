using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Sets up invisible VR ray interactors for UI interaction.
/// Attach this to the main camera or any active GameObject.
/// </summary>
public class VRUIInteractionSetup : MonoBehaviour
{
    [Header("Hand References")]
    [Tooltip("Left hand transform (with TrackedPoseDriver)")]
    [SerializeField] private Transform leftHand;
    
    [Tooltip("Right hand transform (with TrackedPoseDriver)")]
    [SerializeField] private Transform rightHand;
    
    [Header("Ray Settings")]
    [Tooltip("Enable visual ray line (set to false for invisible ray)")]
    [SerializeField] private bool showRayVisual = false;
    
    [Tooltip("Ray color if visual is enabled")]
    [SerializeField] private Color rayColor = new Color(0.3f, 0.6f, 1f, 0.8f);
    
    [Tooltip("Maximum ray distance for UI interaction")]
    [SerializeField] private float maxRayDistance = 30f;
    
    private XRRayInteractor leftRayInteractor;
    private XRRayInteractor rightRayInteractor;
    
    private void Start()
    {
        SetupRayInteractors();
    }
    
    private void SetupRayInteractors()
    {
        // Setup XR Interaction Manager if not present
        XRInteractionManager interactionManager = FindFirstObjectByType<XRInteractionManager>();
        if (interactionManager == null)
        {
            GameObject managerGO = new GameObject("XR Interaction Manager");
            interactionManager = managerGO.AddComponent<XRInteractionManager>();
            Debug.Log("Created XR Interaction Manager");
        }
        
        // Setup right hand ray interactor
        if (rightHand != null)
        {
            rightRayInteractor = SetupHandRayInteractor(rightHand, "Right Ray Interactor", interactionManager);
            Debug.Log("Right hand ray interactor set up");
        }
        else
        {
            Debug.LogWarning("Right hand transform not assigned - VR UI interaction for right hand disabled");
        }
        
        // Setup left hand ray interactor  
        if (leftHand != null)
        {
            leftRayInteractor = SetupHandRayInteractor(leftHand, "Left Ray Interactor", interactionManager);
            Debug.Log("Left hand ray interactor set up");
        }
        else
        {
            Debug.LogWarning("Left hand transform not assigned - VR UI interaction for left hand disabled");
        }
    }
    
    private XRRayInteractor SetupHandRayInteractor(Transform handTransform, string name, XRInteractionManager manager)
    {
        // Create a child GameObject for the ray interactor
        GameObject rayGO = new GameObject(name);
        rayGO.transform.SetParent(handTransform, false);
        rayGO.transform.localPosition = Vector3.zero;
        rayGO.transform.localRotation = Quaternion.identity;
        
        // Add XR Ray Interactor
        XRRayInteractor rayInteractor = rayGO.AddComponent<XRRayInteractor>();
        rayInteractor.interactionManager = manager;
        rayInteractor.maxRaycastDistance = maxRayDistance;
        
        // Configure for UI interaction
        rayInteractor.enableUIInteraction = true;
        
        // Add Line Renderer for visual ray (optional)
        if (showRayVisual)
        {
            LineRenderer lineRenderer = rayGO.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.002f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = rayColor;
            lineRenderer.endColor = rayColor * 0.5f;
            lineRenderer.positionCount = 2;
            
            // Add XR Interactor Line Visual for proper ray rendering
            UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual lineVisual = rayGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            lineVisual.lineLength = maxRayDistance;
            lineVisual.lineWidth = 0.005f;
            lineVisual.enabled = true;
        }
        
        return rayInteractor;
    }
    
    /// <summary>
    /// Enable or disable the ray visuals at runtime
    /// </summary>
    public void SetRayVisualsEnabled(bool enabled)
    {
        if (leftRayInteractor != null)
        {
            var lineVisual = leftRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            if (lineVisual != null) lineVisual.enabled = enabled;
        }
        
        if (rightRayInteractor != null)
        {
            var lineVisual = rightRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            if (lineVisual != null) lineVisual.enabled = enabled;
        }
    }
}
