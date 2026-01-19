# Black Hole Material Analysis

## Material Configuration Analysis

Based on the "Black Hole Material.mat" file from the Real-Time Black Hole v2 project.

## Material Overview

**Material Name**: Black Hole Material  
**Shader**: Custom Shader Graph (Black Hole Shader Ep06-07)  
**Render Queue**: Default  
**Double-Sided**: No  

## Texture Inputs

The material uses multiple texture slots (likely for lookup tables and noise patterns):

### Primary Textures

1. **_Cubemap**: Environment/skybox cubemap
   - Used for lensing the background around the black hole
   - This is what gets distorted by gravitational lensing

2. **Multiple SampleTexture2D slots**: 
   - Various 2D textures for procedural effects
   - Likely include:
     - Noise textures for disc turbulence
     - Lookup tables for elliptic integral calculations
     - Gradient maps for color/temperature
     - Detail textures for disc structure

### Texture GUIDs Referenced
```
- fabf768d079aa2540a4df5b56be3325e (Cubemap)
- f07fd4ebe66989046b52be724c193dd5 (Texture2D - used twice)
- a3fbdba1359b12d4f9b21af8594f4cf7 (Texture2D)
- 37dde46b0d6971d419fa0d629e43d7ce (Texture2D)
- 580c3920f8f9ff4449ccf541f2298933 (Texture2D)
- 6929312cebcc7b9428912a17ae3ea099 (Texture2D)
- bff5b9c06b7e0fd4b8807260aa9aaa68 (Texture2D)
- 270bff3a8197d044984a0fb7f8102486 (Texture2D - used 3 times)
```

## Material Parameters (Float Properties)

### Black Hole Physics

#### **_Mass**: 1.0
- Black hole mass in geometric units
- Affects gravitational lensing strength
- Schwarzschild radius = 2M
- Photon sphere = 3M
- ISCO = 6M
- **Usage**: Core parameter for all GR calculations

#### **_Mass_Accretion**: 0.0000001
- Accretion rate (Ṁ)
- Controls overall disc brightness and temperature
- Very small value = low accretion rate
- **Usage**: Affects disc luminosity and temperature profile

### Disc Geometry

#### **_Outer_Radius**: 50
- Outer edge of accretion disc (in units of M)
- Disc extends from ISCO (~6M) to this radius
- Value of 50M means disc is 50 Schwarzschild radii wide
- **Usage**: Defines disc extent, affects ray-disc intersection

#### **_Outer_Radius_Falloff**: 0.1
- How smoothly the disc fades at outer edge
- Lower value = sharper edge
- Higher value = softer, more gradual fade
- **Usage**: Smooth transition at disc boundary

#### **_Ring_Width**: 0.2
- Thickness or width of specific ring features
- Possibly for photon ring or specific disc features
- **Usage**: Detail control for ring structures

#### **_ISCO_Cutoff_Smoothness**: 2.55
- Controls how smoothly disc fades at inner edge (ISCO)
- Higher value = softer transition
- Material can't orbit stably inside ISCO
- **Usage**: Smooth cutoff at innermost stable orbit

### Disc Appearance

#### **_Density_Range_Low**: 0.685
#### **_Density_Range_High**: 1.0
- Range mapping for disc density/opacity
- Controls overall disc visibility
- Lower bound excludes very low-density regions
- **Usage**: Threshold for disc rendering

#### **_Density_Gradient_Low**: 0.26
#### **_Density_Gradient_High**: 1.0
- Gradient mapping for density variation
- Creates radial density profile
- Likely exponential or power-law falloff
- **Usage**: Disc density distribution

#### **_Texture_Scale**: 0.05
- Scale factor for procedural textures
- Controls detail/turbulence size
- Smaller = finer detail
- **Usage**: Noise/detail texture scaling

### Physical Effects

#### **_Temperature_Falloff**: 0.75
- Exponent for temperature profile: T(r) ∝ r^(-n)
- Standard thin disc: n = 0.75 (matches Shakura-Sunyaev)
- Controls how temperature decreases with radius
- **Usage**: Temperature gradient calculation

#### **_Temperature_Brightness_Influence**: 1.0
- How much temperature affects brightness
- Full value (1.0) = direct correlation
- **Usage**: Coupling between temperature and emission

#### **_Beaming_Strength**: 0.404
- Relativistic beaming intensity
- Controls Doppler boosting effect
- Material moving toward observer appears brighter
- Value of 0.4 = moderate beaming
- **Usage**: Relativistic intensity boost factor

#### **_Rotation_Speed**: -100
- Angular velocity of disc rotation
- Negative value = specific rotation direction
- High magnitude = fast rotation
- **Usage**: Animation and Doppler shift calculation

### Rendering

#### **_Brightness**: -3.6
- Overall brightness/exposure adjustment
- Negative value = darker (log scale likely)
- Compensates for HDR range
- **Usage**: Final output scaling

#### **_Environment_Brightness**: 1.0
- Brightness of background/skybox
- Separate from disc brightness
- Allows independent control
- **Usage**: Background cubemap intensity

#### **_Test**: 0
- Debug/test parameter
- Likely toggles visualization modes
- **Usage**: Development/debugging

## Parameter Analysis

### Physical Accuracy

The material parameters show physically-motivated design:

1. **Temperature Falloff = 0.75**: Matches theoretical prediction for thin disc
   ```
   T(r) ∝ r^(-0.75) (Shakura-Sunyaev model)
   ```

2. **Mass Accretion = 1×10^-7**: Realistic low accretion rate
   - Corresponds to dim, underluminous black hole
   - Typical for stellar-mass BH in quiescence

3. **Outer Radius = 50M**: Reasonable disc extent
   - Not too large (computational cost)
   - Large enough to show full structure

4. **Beaming = 0.404**: Moderate relativistic effect
   - Full beaming would be D³ factor
   - This appears to be a tuning parameter

### Artistic Adjustments

Some parameters are tuned for visual appeal:

1. **Brightness = -3.6**: Exposure adjustment for HDR → LDR
2. **Density Range**: Tweaked to hide faint regions
3. **Ring Width**: Artistic control over photon ring visibility

## Material Usage in Shader

Based on the parameter names and values, the shader likely:

1. **Calculates ray deflection** using `_Mass`
2. **Traces to disc plane** checking against `_Outer_Radius`
3. **Calculates disc coordinates** (r, φ)
4. **Computes temperature** using `T(r) ∝ r^(-_Temperature_Falloff)`
5. **Applies Doppler shift** based on `_Rotation_Speed` and position
6. **Adds turbulence** using noise textures at `_Texture_Scale`
7. **Modulates density** within `_Density_Range`
8. **Applies beaming** with `_Beaming_Strength` factor
9. **Samples background** from `_Cubemap` for non-disc rays
10. **Adjusts final brightness** with `_Brightness` and `_Environment_Brightness`

## Typical Rendering Flow

```
For each pixel:
  1. Calculate impact parameter b from screen position
  2. Calculate deflection angle Δφ(b, _Mass)
  3. Trace deflected ray
  
  4. Check ray-disc intersection:
     - Intersect with equatorial plane
     - Check if r < _Outer_Radius
     - Check if r > ISCO (with _ISCO_Cutoff_Smoothness)
     
  5. If hit disc:
     a. Calculate r, φ in disc coordinates
     
     b. Calculate temperature:
        T = T_max × (r/r_in)^(-_Temperature_Falloff)
     
     c. Calculate velocity:
        v = sqrt(_Mass/r) × _Rotation_Speed
     
     d. Calculate Doppler factor:
        D = f(v, φ, _Beaming_Strength)
     
     e. Sample noise textures:
        noise = Sample2D(r, φ, _Texture_Scale)
     
     f. Calculate density:
        ρ = f(r, noise, _Density_Range, _Density_Gradient)
     
     g. Calculate color:
        color = BlackbodyColor(T)
        color *= D^3  (Doppler + beaming)
        color *= GravitationalRedshift(r)
        color *= ρ  (density modulation)
        color *= _Temperature_Brightness_Influence
     
     h. Apply brightness:
        color *= exp(_Brightness)
     
     return color
     
  6. Else (no disc hit):
     a. Sample environment cubemap
        env = SampleCube(_Cubemap, deflectedDirection)
     
     b. Apply brightness:
        env *= _Environment_Brightness
     
     return env
```

## Key Insights for Your Implementation

### 1. Separate Background and Disc

The material clearly distinguishes:
- **Disc rendering**: Full physics simulation
- **Background rendering**: Simple cubemap sampling with deflection

For your use case (no skybox lensing), you can:
- Remove cubemap sampling
- Return black/transparent for non-disc rays
- Focus computational effort on disc regions only

### 2. Parameterization

The material exposes ~18 parameters for artistic control. Essential ones:
- **Physics**: `_Mass`, `_Mass_Accretion`, `_Temperature_Falloff`
- **Geometry**: `_Outer_Radius`, `_ISCO_Cutoff_Smoothness`
- **Dynamics**: `_Rotation_Speed`, `_Beaming_Strength`
- **Detail**: `_Texture_Scale`, `_Density_Range`
- **Appearance**: `_Brightness`

### 3. Optimization Opportunities

Parameters suggest optimization strategies:
- **Early termination**: Check `_Outer_Radius` first
- **LOD switching**: Use `_Density_Range` to cull faint regions
- **Texture detail**: Adjust `_Texture_Scale` based on distance
- **Simplified physics**: For distant/faint regions

### 4. Physical Models Used

Evidence from parameters:
- ✅ Shakura-Sunyaev temperature profile
- ✅ Keplerian rotation
- ✅ Relativistic Doppler beaming
- ✅ Gravitational redshift (implicit)
- ✅ Procedural turbulence
- ✅ Density-based rendering

### 5. Texture Usage

Multiple texture slots suggest:
- **Elliptic integral LUTs**: Pre-computed deflection angles
- **Noise textures**: Fractal noise for disc structure
- **Gradient maps**: Smooth transitions (ISCO, outer edge)
- **Detail maps**: Fine-scale turbulence

## Recommended Parameter Values for Different Scenarios

### Stellar-Mass Black Hole (M = 10 M☉)
```
_Mass: 1.0
_Mass_Accretion: 0.0000001  (quiescent)
_Outer_Radius: 50
_Temperature_Falloff: 0.75
_Rotation_Speed: -100
_Brightness: -3.6
```

### Supermassive Black Hole (M = 10^6 M☉)
```
_Mass: 1.0  (still normalized)
_Mass_Accretion: 0.001  (higher for AGN)
_Outer_Radius: 100  (larger disc)
_Temperature_Falloff: 0.75
_Rotation_Speed: -50  (slower angular velocity)
_Brightness: -2.0  (brighter overall)
```

### High-Accretion State
```
_Mass_Accretion: 0.01
_Temperature_Brightness_Influence: 1.5
_Beaming_Strength: 0.6
_Brightness: -2.0
_Density_Range_Low: 0.4  (show more structure)
```

### Artistic Glow
```
_ISCO_Cutoff_Smoothness: 5.0  (softer inner edge)
_Outer_Radius_Falloff: 0.3  (softer outer edge)
_Beaming_Strength: 0.8  (stronger glow)
_Temperature_Brightness_Influence: 2.0  (exaggerate)
```

## Implementation Checklist

Based on this material analysis, to implement similar functionality:

- [ ] **Shader Properties**: Add all float parameters
- [ ] **Texture Slots**: Setup cubemap (optional) and noise textures
- [ ] **Ray Setup**: Screen UV → impact parameter
- [ ] **Deflection**: Calculate Δφ using _Mass
- [ ] **Disc Intersection**: Check against _Outer_Radius, ISCO
- [ ] **Temperature Calculation**: Use _Temperature_Falloff exponent
- [ ] **Rotation**: Apply _Rotation_Speed for animation
- [ ] **Doppler Effect**: Use _Beaming_Strength
- [ ] **Noise Sampling**: At _Texture_Scale
- [ ] **Density Modulation**: Apply _Density_Range
- [ ] **Color Calculation**: Blackbody + all effects
- [ ] **Brightness**: Final scaling with _Brightness
- [ ] **Background**: Cubemap or solid color for non-disc rays

## Performance Considerations

Parameters affecting performance:
- **_Outer_Radius**: Larger = more pixels to compute
- **_Texture_Scale**: Smaller = more texture samples
- **_Density_Range_Low**: Higher = early termination opportunities
- **_ISCO_Cutoff_Smoothness**: Higher = more blend calculations

Optimization via parameters:
1. Reduce `_Outer_Radius` for distant views
2. Increase `_Density_Range_Low` to cull faint regions
3. Adjust `_Texture_Scale` based on LOD
4. Use simpler deflection for `_Mass` < critical values

## Comparison to Your Current Shaders

Your existing shaders (`AccretionDisc.shader`, `BlackHole.shader`) likely need:

**Add from Black Hole Material**:
- Mass-dependent lensing calculations
- Temperature falloff parameter
- Rotation speed and Doppler shifting
- Beaming strength for relativistic effects
- ISCO cutoff with smoothness control
- Density range mapping
- Multiple texture slots for detail

**Keep from Your Shaders**:
- Basic structure and organization
- Any existing visual features you like
- Current parameter naming if preferred

**Modify in Your Shaders**:
- Replace simple UV distortion with proper ray-disc intersection
- Add physical temperature/color calculations
- Implement relativistic effects (Doppler, redshift, beaming)
- Add procedural detail with noise textures

This material configuration represents a sophisticated, physically-based approach to black hole rendering with extensive artistic control!
