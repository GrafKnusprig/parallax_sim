# Implementation Roadmap: Gravitational Lensing for Accretion Disc

## Quick Reference Summary

Based on analysis of the Real-Time Black Hole v2 project, here's your implementation roadmap.

## What You Have vs What You Need

### Current State (Your Project)
- ✅ AccretionDisc.shader (basic shader)
- ✅ BlackHole.shader (basic shader)
- ✅ AccretionDiscMaterial.mat
- ✅ BlackHole.mat
- ✅ Unity scene setup

### What's Missing for True Gravitational Lensing
- ❌ Ray-disc intersection calculation
- ❌ Deflection angle from General Relativity
- ❌ Physical temperature profile
- ❌ Relativistic Doppler effects
- ❌ Gravitational redshift
- ❌ Multi-image handling

## Implementation Strategy

### Phase 1: Foundation (Week 1)
**Goal**: Get basic disc rendering with simple physics

#### Step 1.1: Add Material Parameters
Update your materials with these essential properties:

```csharp
// In your AccretionDisc shader
Properties {
    // Black Hole
    _Mass ("Black Hole Mass", Float) = 1.0
    
    // Disc Geometry
    _DiscInnerRadius ("Inner Radius (ISCO)", Float) = 6.0
    _DiscOuterRadius ("Outer Radius", Float) = 50.0
    _DiscOuterFalloff ("Outer Edge Smoothness", Float) = 0.1
    _ISCOSmoothness ("ISCO Cutoff Smoothness", Float) = 2.5
    
    // Temperature
    _MaxTemperature ("Max Temperature (K)", Float) = 100000
    _TemperatureFalloff ("Temperature Falloff", Float) = 0.75
    
    // Rotation
    _RotationSpeed ("Rotation Speed", Float) = -100
    
    // Detail
    _TextureScale ("Noise Scale", Float) = 0.05
    [NoiseTexture] _NoiseTex ("Noise Texture", 2D) = "white" {}
    
    // Appearance
    _Brightness ("Overall Brightness", Float) = -3.6
    _BeamingStrength ("Beaming Strength", Float) = 0.4
}
```

#### Step 1.2: Simple Disc Renderer (No Lensing Yet)
Create a basic disc without gravitational lensing first:

```hlsl
// In fragment shader
float4 frag(v2f i) : SV_Target {
    // 1. Get position relative to black hole (assuming centered at origin)
    float3 localPos = i.worldPos - _BlackHoleCenter;
    
    // 2. Calculate disc coordinates (assume equatorial plane)
    float discRadius = length(localPos.xy);
    float discAngle = atan2(localPos.y, localPos.x);
    
    // 3. Check if in disc bounds
    if (discRadius < _DiscInnerRadius || discRadius > _DiscOuterRadius) {
        discard; // Outside disc
    }
    
    // 4. Calculate temperature
    float temp = CalculateTemperature(discRadius);
    
    // 5. Get base color from temperature
    float3 color = BlackbodyColor(temp);
    
    // 6. Add rotation (simple for now)
    float rotPhase = discAngle + _Time.y * _RotationSpeed * 0.01;
    
    return float4(color, 1.0);
}
```

**Test**: You should see a colored disc without any lensing.

### Phase 2: Basic Ray Tracing (Week 2)
**Goal**: Implement ray-disc intersection

#### Step 2.1: Setup Ray from Camera
```hlsl
float3 rayOrigin = _WorldSpaceCameraPos;
float3 rayDir = normalize(i.worldPos - rayOrigin);

// For screen-space rendering, use:
// float2 screenPos = (i.uv * 2.0 - 1.0) * _ViewportScale;
// float3 rayDir = CalculateRayDirection(screenPos);
```

#### Step 2.2: Ray-Plane Intersection
```hlsl
// Intersect ray with equatorial plane (z = 0 for horizontal disc)
float t = -rayOrigin.z / rayDir.z;

if (t > 0) {
    float3 hitPoint = rayOrigin + rayDir * t;
    float discRadius = length(hitPoint.xy);
    float discAngle = atan2(hitPoint.y, hitPoint.x);
    
    // Now use discRadius and discAngle to calculate color
    // (same as Phase 1, but now traced via ray)
}
```

**Test**: Disc should look the same, but now ray-traced.

### Phase 3: Simple Gravitational Deflection (Week 3)
**Goal**: Add weak-field lensing

#### Step 3.1: Calculate Impact Parameter
```hlsl
// Screen position to impact parameter
float2 screenPos = (i.uv * 2.0 - 1.0);
screenPos.y *= _AspectRatio;

float distanceToBlackHole = length(_WorldSpaceCameraPos - _BlackHoleCenter);
float b = length(screenPos) * distanceToBlackHole;
float viewAngle = atan2(screenPos.y, screenPos.x);
```

#### Step 3.2: Simple Deflection (Weak Field)
```hlsl
// Einstein deflection angle
float deflectionAngle = 4.0 * _Mass / b;

// Modify ray direction
float newAngle = viewAngle + deflectionAngle;
float3 deflectedDir = float3(
    cos(newAngle),
    sin(newAngle),
    rayDir.z // Keep z component
);
deflectedDir = normalize(deflectedDir);

// Now trace this deflected ray to disc
```

**Test**: Disc should appear slightly distorted, especially near edges.

### Phase 4: Accurate Deflection (Week 4)
**Goal**: Implement exact GR deflection

#### Step 4.1: Create Lookup Table
Pre-compute deflection angles and store in a texture:

```python
# Python script to generate LUT
import numpy as np
from scipy.special import ellipk, elliprf

# Impact parameters from 0 to 10*M
b_values = np.linspace(2.5, 100, 512)
deflection = []

for b in b_values:
    # Calculate exact deflection using elliptic integrals
    # (See BLACK_HOLE_MATH_FORMULAS.md for details)
    angle = calculate_exact_deflection(b, M=1.0)
    deflection.append(angle)

# Save as texture
save_as_texture("deflection_lut.png", deflection)
```

#### Step 4.2: Use LUT in Shader
```hlsl
// Sample deflection from lookup table
float b_normalized = saturate(b / 100.0); // Normalize to [0,1]
float deflectionAngle = tex2D(_DeflectionLUT, float2(b_normalized, 0.5)).r;
```

**Test**: Accurate lensing, including strong effects near photon sphere.

### Phase 5: Physical Disc Effects (Week 5)
**Goal**: Add realistic disc physics

#### Step 5.1: Temperature Profile
```hlsl
float CalculateTemperature(float r) {
    float r_in = _DiscInnerRadius;
    float r_norm = r / r_in;
    
    // Shakura-Sunyaev profile
    float temp = _MaxTemperature * pow(r_norm, -_TemperatureFalloff);
    temp *= pow(1.0 - sqrt(r_in / r), 0.25);
    
    return clamp(temp, 1000.0, 100000.0);
}
```

#### Step 5.2: Blackbody Color
```hlsl
float3 BlackbodyColor(float T) {
    float t = T / 10000.0;
    
    float3 color;
    if (t > 1.0) {
        // Hot: blue-white
        color = lerp(float3(1,1,1), float3(0.6, 0.7, 1.0), saturate(t - 1.0));
    } else {
        // Cool: orange to white
        color = lerp(float3(1.0, 0.3, 0.1), float3(1,1,1), t);
    }
    
    return color;
}
```

**Test**: Disc should show temperature gradient (blue inner, red outer).

### Phase 6: Relativistic Effects (Week 6)
**Goal**: Add Doppler and redshift

#### Step 6.1: Doppler Shift
```hlsl
float CalculateDopplerFactor(float r, float phi) {
    // Keplerian velocity
    float v = sqrt(_Mass / r);
    
    // Apply rotation speed scaling
    v *= _RotationSpeed / 100.0;
    
    // Angle relative to observer
    float cosAngle = cos(phi);
    
    // Relativistic Doppler
    float beta = v;
    float gamma = 1.0 / sqrt(1.0 - beta * beta);
    float D = 1.0 / (gamma * (1.0 - beta * cosAngle));
    
    // Beaming: D^3 for intensity
    return pow(D, 3.0 * _BeamingStrength);
}
```

#### Step 6.2: Gravitational Redshift
```hlsl
float GravitationalRedshift(float r) {
    return sqrt(1.0 - 2.0 * _Mass / r);
}
```

#### Step 6.3: Apply All Effects
```hlsl
float3 finalColor = baseColor;
finalColor *= dopplerFactor;      // Doppler shift + beaming
finalColor *= gravRedshift;        // Gravitational redshift
finalColor *= exp(_Brightness);    // Overall brightness
```

**Test**: One side of disc brighter (approaching), other dimmer (receding).

### Phase 7: Detail and Polish (Week 7+)
**Goal**: Add visual richness

#### Step 7.1: Procedural Noise
```hlsl
// Sample noise for turbulence
float2 noiseUV = float2(r / _DiscOuterRadius, phi / (2 * 3.14159));
noiseUV *= _TextureScale;
noiseUV.y += _Time.y * 0.1; // Animate

float noise = tex2D(_NoiseTex, noiseUV).r;
float turbulence = FractalNoise(noiseUV, 4); // Multiple octaves

// Modulate disc properties
float density = lerp(0.5, 1.0, turbulence);
finalColor *= density;
```

#### Step 7.2: Smooth Edges
```hlsl
// Smooth ISCO cutoff
float innerMask = smoothstep(_DiscInnerRadius, 
                            _DiscInnerRadius + _ISCOSmoothness, 
                            r);

// Smooth outer edge
float outerMask = 1.0 - smoothstep(_DiscOuterRadius - _DiscOuterFalloff,
                                   _DiscOuterRadius,
                                   r);

float mask = innerMask * outerMask;
finalColor *= mask;
```

#### Step 7.3: Photon Ring
```hlsl
// Add bright ring at photon sphere
float photonSphere = 3.0 * _Mass;
float ringDist = abs(r - photonSphere);
float ringGlow = exp(-ringDist * 20.0) * 0.5;

finalColor += float3(ringGlow, ringGlow, ringGlow);
```

**Test**: Rich, detailed disc with turbulence and glowing edges.

## File Structure

```
YourProject/
├── Assets/
│   ├── Shaders/
│   │   ├── AccretionDisc.shader          [MODIFY]
│   │   ├── BlackHole.shader              [MODIFY]
│   │   ├── DeflectionLUT.shader          [NEW]
│   │   └── Helpers/
│   │       ├── BlackbodyColor.cginc      [NEW]
│   │       ├── RelativisticEffects.cginc [NEW]
│   │       └── RayTracing.cginc          [NEW]
│   │
│   ├── Materials/
│   │   ├── AccretionDiscMaterial.mat     [UPDATE]
│   │   └── BlackHole.mat                 [UPDATE]
│   │
│   ├── Textures/
│   │   ├── DeflectionLUT.png             [NEW]
│   │   ├── NoiseTexture.png              [NEW]
│   │   └── FractalNoise.png              [NEW]
│   │
│   └── Scripts/
│       ├── BlackHoleController.cs        [NEW]
│       └── AccretionDiscAnimator.cs      [NEW]
│
└── Documentation/  [ALREADY CREATED]
    ├── GRAVITATIONAL_LENSING_ANALYSIS.md
    ├── ACCRETION_DISC_LENSING_IMPLEMENTATION.md
    ├── BLACK_HOLE_MATH_FORMULAS.md
    └── BLACK_HOLE_MATERIAL_ANALYSIS.md
```

## Testing Checklist

### Phase 1 Tests
- [ ] Disc renders at correct size
- [ ] Color gradient visible (inner to outer)
- [ ] ISCO cutoff working
- [ ] Outer radius boundary correct

### Phase 2 Tests
- [ ] Ray tracing produces same result as Phase 1
- [ ] Camera movement works correctly
- [ ] Disc visible from all angles

### Phase 3 Tests
- [ ] Weak lensing visible near disc edges
- [ ] Deflection increases closer to black hole
- [ ] Background properly distorted

### Phase 4 Tests
- [ ] Strong lensing near photon sphere
- [ ] Einstein ring visible at critical angle
- [ ] Multiple images possible

### Phase 5 Tests
- [ ] Temperature gradient realistic (blue→white→red)
- [ ] Inner disc hottest
- [ ] Outer disc coolest

### Phase 6 Tests
- [ ] Approaching side brighter
- [ ] Receding side dimmer
- [ ] Redshift effect visible
- [ ] Rotation animation smooth

### Phase 7 Tests
- [ ] Turbulence adds realism
- [ ] Edges smooth and natural
- [ ] Photon ring visible
- [ ] Performance acceptable (>30 fps)

## Performance Optimization

### Optimization 1: LOD System
```csharp
// In C# script
float distanceToCamera = Vector3.Distance(camera.position, blackHole.position);

if (distanceToCamera > 100f) {
    // Use weak-field approximation
    material.SetFloat("_UseExactGR", 0);
} else {
    // Use full GR calculation
    material.SetFloat("_UseExactGR", 1);
}
```

### Optimization 2: Early Ray Termination
```hlsl
// In shader
if (b > _DiscOuterRadius * 1.5) {
    // Ray definitely misses disc
    return float4(0, 0, 0, 1);
}

if (b < 2.5 * _Mass) {
    // Ray captured by black hole
    return float4(0, 0, 0, 1); // Black hole shadow
}
```

### Optimization 3: Texture Resolution
- Deflection LUT: 512x1 (sufficient)
- Noise textures: 256x256 (repeating)
- Detail maps: 512x512 (maximum)

### Optimization 4: Shader Variants
```hlsl
#pragma multi_compile _ SIMPLE_PHYSICS FULL_PHYSICS

#ifdef SIMPLE_PHYSICS
    // Weak-field approximation
#elif FULL_PHYSICS
    // Exact GR with elliptic integrals
#else
    // No lensing
#endif
```

## Common Pitfalls

### ❌ Pitfall 1: Using Cubemap Sampling for Disc
**Wrong**:
```hlsl
color = texCUBE(_Cubemap, deflectedDir);
```

**Right**:
```hlsl
float3 hitPoint = RayPlaneIntersection(rayOrigin, deflectedDir);
float r = length(hitPoint.xy);
color = CalculateDiscColor(r, angle);
```

### ❌ Pitfall 2: Forgetting Geometric Units
**Wrong**:
```hlsl
float r_s = 2 * _Mass * G * c * c; // Too complicated
```

**Right**:
```hlsl
float r_s = 2.0 * _Mass; // G = c = 1 in geometric units
```

### ❌ Pitfall 3: Linear Temperature-Color Mapping
**Wrong**:
```hlsl
color = float3(temp, temp, temp) / 100000.0;
```

**Right**:
```hlsl
color = BlackbodyColor(temp); // Proper Wien's law
```

### ❌ Pitfall 4: Ignoring Relativistic Effects
Without Doppler/redshift, disc looks unrealistic. Always include:
- Doppler shift (rotation)
- Gravitational redshift
- Relativistic beaming

## Debug Visualization

Add debug modes to your shader:

```hlsl
// In Properties
[Toggle] _DebugMode ("Debug Visualization", Float) = 0
[Enum(Normal,0,ImpactParameter,1,Deflection,2,Temperature,3,Doppler,4)] 
_DebugChannel ("Debug Channel", Float) = 0

// In fragment shader
#ifdef _DEBUGMODE_ON
    if (_DebugChannel == 1) {
        return float4(b / 50.0, 0, 0, 1); // Visualize impact parameter
    } else if (_DebugChannel == 2) {
        return float4(deflection / 3.14159, 0, 0, 1); // Visualize deflection
    } else if (_DebugChannel == 3) {
        return float4(temp / 100000.0, 0, 0, 1); // Visualize temperature
    } else if (_DebugChannel == 4) {
        return float4(dopplerFactor, 0, 0, 1); // Visualize Doppler
    }
#endif
```

## Reference Parameter Values

### Realistic Stellar-Mass Black Hole
```
_Mass: 1.0
_DiscInnerRadius: 6.0
_DiscOuterRadius: 50.0
_MaxTemperature: 1000000
_TemperatureFalloff: 0.75
_RotationSpeed: -100
_BeamingStrength: 0.4
_Brightness: -3.6
```

### Artistic "Interstellar" Style
```
_Mass: 1.0
_DiscInnerRadius: 6.0
_DiscOuterRadius: 100.0
_MaxTemperature: 50000
_TemperatureFalloff: 0.6
_RotationSpeed: -150
_BeamingStrength: 0.8
_Brightness: -2.0
_ISCOSmoothness: 5.0
```

## Success Criteria

You'll know your implementation is working when:

1. ✅ Disc bends light realistically
2. ✅ One side brighter than other (Doppler)
3. ✅ Temperature gradient visible (blue inner, red outer)
4. ✅ Smooth cutoff at ISCO
5. ✅ Photon ring visible at critical angles
6. ✅ Multiple images possible for some viewing angles
7. ✅ Animation smooth and physically motivated
8. ✅ Performance acceptable (>30 fps on target hardware)

## Next Steps After Implementation

Once basic implementation works:

1. **Add Kerr Metric** (rotating black hole)
   - Changes ISCO location
   - Adds frame-dragging effects
   - More complex lensing

2. **Multi-Image Rendering**
   - Trace multiple ray paths
   - Show secondary/tertiary images
   - Photon ring structure

3. **Time Evolution**
   - Animate disc accretion
   - Hotspots orbiting
   - Quasi-periodic oscillations

4. **Spectral Rendering**
   - Multi-wavelength images
   - X-ray vs visible
   - Spectral line profiles

Good luck with your implementation! Start with Phase 1 and work through systematically. Each phase builds on the previous, so don't skip ahead until the current phase works correctly.
