# Accretion Disc Gravitational Lensing - Implementation Guide

## Key Findings from Real-Time Black Hole Shader Analysis

Based on the shader structure from the Real-Time Black Hole v2 project, here's what you need to implement gravitational lensing for an accretion disc (not a skybox).

## Critical Difference: Skybox vs Accretion Disc

**Skybox Lensing** (what the reference shader does):
- Samples a distant environment cubemap
- Only needs to calculate the deflected direction
- Uses: `SampleCubemap(deflectedDirection)`

**Accretion Disc Lensing** (what you need):
- Must trace ray to find intersection with disc plane
- Calculate where in disc space the ray hits
- Sample disc texture/properties at that location
- Apply local physical effects (temperature, velocity, etc.)

## Implementation Steps

### 1. Ray Setup and Impact Parameter

```hlsl
// In your shader, convert screen UV to world space ray
float2 screenPos = i.uv * 2.0 - 1.0; // Range [-1, 1]
screenPos.y *= _AspectRatio;

// Calculate impact parameter (perpendicular distance to black hole)
float b = length(screenPos) * _CameraDistance;
float viewAngle = atan2(screenPos.y, screenPos.x);
```

### 2. Deflection Angle Calculation

The shader uses a complex subgraph system. Here's a simplified approach:

```hlsl
// Simplified deflection angle (you'll need the full elliptic integral version)
float deflectionAngle = CalculateDeflectionAngle(b, _BlackHoleMass);

// The reference shader has this in DeflectionAngle.shadersubgraph
// It uses elliptic integrals for exact GR calculation
// For initial testing, you can use weak-field approximation:
// deflectionAngle = 4 * M / b (in geometric units where c=G=1)
```

### 3. Ray Tracing to Disc Plane

**This is the key difference:**

```hlsl
// Calculate the deflected ray direction
float3 deflectedDir = CalculateDeflectedDirection(viewAngle, deflectionAngle);

// Trace ray to equatorial plane (where accretion disc lives)
// Disc is typically at z=0 (equatorial plane)
float3 rayOrigin = _CameraPosition;
float tHit = -rayOrigin.z / deflectedDir.z; // Intersection parameter

if (tHit > 0) {
    float3 hitPoint = rayOrigin + deflectedDir * tHit;
    float discRadius = length(hitPoint.xy); // Distance from black hole center
    float discAngle = atan2(hitPoint.y, hitPoint.x);
    
    // Check if hit is within disc bounds
    if (discRadius > _DiscInnerRadius && discRadius < _DiscOuterRadius) {
        // We hit the disc! Now calculate color
        return CalculateDiscColor(discRadius, discAngle);
    }
}

// No disc hit - return black or background
return float4(0, 0, 0, 1);
```

### 4. Disc Properties Calculation

Based on the `Disk Temperature` and `Disk Color` subgraphs:

```hlsl
float3 CalculateDiscColor(float r, float phi) {
    // Temperature decreases with radius (Shakura-Sunyaev model)
    // Inner disc is hottest (white/blue), outer disc is cooler (red)
    float temp = CalculateTemperature(r);
    
    // Keplerian velocity
    float v = sqrt(_BlackHoleMass / r);
    
    // Doppler shift from rotation
    // Disc rotates, so material on one side approaches, other recedes
    float velocityAngle = phi; // Angle in disc
    float dopplerShift = CalculateDopplerEffect(v, velocityAngle);
    
    // Gravitational redshift
    float grav_redshift = sqrt(1.0 - 2.0 * _BlackHoleMass / r);
    
    // Blackbody radiation color based on temperature
    float3 baseColor = BlackbodyColor(temp);
    
    // Apply relativistic effects
    float3 finalColor = baseColor * dopplerShift * grav_redshift;
    
    // Add intensity based on viewing angle and relativistic beaming
    float intensity = CalculateIntensity(r, phi);
    
    return finalColor * intensity;
}
```

### 5. Temperature Profile

```hlsl
float CalculateTemperature(float r) {
    // Shakura-Sunyaev thin disc model
    // T ∝ r^(-3/4) for standard disc
    float r_inner = _DiscInnerRadius; // Typically 3-6 M
    
    // Normalized radius
    float r_norm = r / r_inner;
    
    // Temperature profile
    float temp = _MaxTemperature * pow(r_norm, -0.75);
    temp *= pow(1.0 - sqrt(r_inner / r), 0.25); // Goes to zero at inner edge
    
    return clamp(temp, 1000, 100000); // In Kelvin
}
```

### 6. Blackbody Color

Based on the `BlackbodyColor.shadersubgraph`:

```hlsl
float3 BlackbodyColor(float temperature) {
    // Simplified blackbody radiation
    // Full implementation would use Planck's law
    
    // Very hot: blue-white (> 10000K)
    // Hot: white (5000-10000K)  
    // Cool: orange-red (< 5000K)
    
    float t_norm = temperature / 10000.0;
    
    float3 color;
    if (t_norm > 1.0) {
        // Blue-white for very hot regions
        color = lerp(float3(1,1,1), float3(0.6, 0.7, 1.0), saturate(t_norm - 1.0));
    } else {
        // White to orange-red for cooler regions
        color = lerp(float3(1.0, 0.3, 0.1), float3(1,1,1), t_norm);
    }
    
    return color;
}
```

### 7. Doppler Effect

```hlsl
float CalculateDopplerEffect(float velocity, float angle) {
    // Relativistic Doppler formula
    // Material on approaching side is blueshifted (brighter)
    // Material on receding side is redshifted (dimmer)
    
    float beta = velocity; // v/c in geometric units
    float cosAngle = cos(angle - _DiscRotationPhase);
    
    // Relativistic Doppler factor
    float gamma = 1.0 / sqrt(1.0 - beta * beta);
    float doppler = 1.0 / (gamma * (1.0 - beta * cosAngle));
    
    // For emission, factor is cubed (frequency shift + beaming + aberration)
    return pow(doppler, 3.0);
}
```

### 8. Gravitational Redshift

```hlsl
float CalculateGravitationalRedshift(float r) {
    // Photons lose energy climbing out of potential well
    // Redshift factor: sqrt(1 - r_s/r) where r_s = 2M
    
    float r_s = 2.0 * _BlackHoleMass;
    return sqrt(1.0 - r_s / r);
}
```

## Shader Property Definitions

```hlsl
// Black Hole Properties
float _BlackHoleMass = 1.0; // In geometric units

// Disc Properties
float _DiscInnerRadius = 6.0; // ISCO for Schwarzschild (6M)
float _DiscOuterRadius = 20.0; // Arbitrary outer boundary
float _MaxTemperature = 100000.0; // Kelvin at inner edge
float _AccretionRate = 1.0; // Controls overall brightness

// Camera
float3 _CameraPosition;
float _CameraDistance;
float _AspectRatio;

// Animation
float _DiscRotationPhase; // Animate over time
float _Time;

// Textures (optional)
Texture2D _DiscTexture; // For adding detail/structure
Texture2D _NoiseTexture; // For turbulence
```

## Full Shader Structure

```hlsl
// Main shader function
float4 BlackHoleAccretionDisc(float2 uv) {
    // 1. Convert UV to screen space
    float2 screenPos = (uv * 2.0 - 1.0) * _ViewportScale;
    
    // 2. Calculate impact parameter
    float b = length(screenPos) * _CameraDistance;
    float viewAngle = atan2(screenPos.y, screenPos.x);
    
    // 3. Calculate deflection
    float deflectionAngle = DeflectionAngle(b, _BlackHoleMass, viewAngle);
    
    // 4. Trace ray to disc
    float3 rayDir = DeflectedRayDirection(viewAngle, deflectionAngle);
    float3 hitPoint;
    float discRadius;
    float discAngle;
    
    bool hitDisc = RayDiscIntersection(
        _CameraPosition, 
        rayDir, 
        hitPoint, 
        discRadius, 
        discAngle
    );
    
    if (hitDisc && discRadius > _DiscInnerRadius && discRadius < _DiscOuterRadius) {
        // 5. Calculate disc properties
        float temp = CalculateTemperature(discRadius);
        float velocity = sqrt(_BlackHoleMass / discRadius);
        
        // 6. Calculate color
        float3 baseColor = BlackbodyColor(temp);
        
        // 7. Apply relativistic effects
        float doppler = CalculateDopplerEffect(velocity, discAngle);
        float redshift = CalculateGravitationalRedshift(discRadius);
        
        // 8. Final color
        float3 color = baseColor * doppler * redshift;
        
        // Optional: Add texture detail
        float2 discUV = float2(discRadius / _DiscOuterRadius, discAngle / (2*PI));
        float detail = tex2D(_DiscTexture, discUV).r;
        color *= (0.5 + 0.5 * detail);
        
        return float4(color, 1.0);
    }
    
    // Check if ray goes into event horizon
    if (b < 2.6 * _BlackHoleMass) { // Roughly photon sphere
        return float4(0, 0, 0, 1); // Black hole shadow
    }
    
    // Background
    return float4(0, 0, 0, 1);
}
```

## Advanced Features from the Reference Shader

The Real-Time Black Hole shader includes:

1. **KeplerianRotatingClumps**: Adds turbulent structure to the disc
2. **Multiple Image Handling**: Light can orbit the black hole multiple times
3. **Photon Ring**: The bright ring at the photon sphere
4. **Accurate Elliptic Integrals**: For exact GR calculations
5. **Lens-Thirring Precession**: For rotating black holes (Kerr metric)

For your initial implementation, focus on:
- Basic ray-disc intersection
- Temperature-based coloring
- Simple Doppler and redshift effects

Then add complexity:
- Accurate deflection angles
- Multiple images
- Disc texture and turbulence
- Animation and rotation

## Key Takeaways

1. **Don't sample a cubemap** - that's for distant backgrounds
2. **Do ray-plane intersection** - find where deflected ray hits disc
3. **Apply local effects** - temperature, velocity are position-dependent
4. **Multiple solutions exist** - a ray can hit the disc multiple times after wrapping around
5. **Photon sphere is critical** - rays with b ≈ 3√3 M need special handling

## Testing Strategy

1. **Start simple**: Flat disc, no lensing, just basic colors
2. **Add weak lensing**: Simple deflection formula
3. **Add disc physics**: Temperature gradient, blackbody colors
4. **Add rotation effects**: Doppler shift
5. **Add exact GR**: Elliptic integrals for deflection
6. **Add multi-images**: Multiple ray paths
7. **Add detail**: Texture, turbulence, animation

This modular approach will help you debug each component separately.

## Reference Shader Components to Study

From the Real-Time Black Hole project:
- `DeflectionAngle.shadersubgraph` - Core GR calculation
- `b.shadersubgraph` - Impact parameter calculation
- `phi_p.shadersubgraph` - Angular deflection (elliptic integrals)
- `Disk Temperature.shadersubgraph` - Temperature profile
- `Disk Color.shadersubgraph` - Final color calculation
- `KeplerianRotatingClumps.shadersubgraph` - Disc structure
- `BlackbodyColor.shadersubgraph` - Temperature to color conversion

Good luck with your implementation!
