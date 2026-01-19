# Physically Accurate Black Hole Rendering

## Overview
This implementation uses physically accurate equations from general relativity to render black holes and their accretion discs in real-time. The approach is based on research from the "Interstellar" movie (Kip Thorne et al.) and GPU raymarching techniques.

## Key Physical Phenomena Implemented

### 1. **Black Hole Shader** (`BlackHole.shader`)

#### Schwarzschild Metric
- Event horizon at radius Rs (Schwarzschild radius)
- Photon sphere at 1.5 Rs where light can orbit
- ISCO (Innermost Stable Circular Orbit) at 3 Rs

#### Gravitational Lensing
- **Light bending**: Rays bend according to deflection angle α ≈ 2Rs/b (impact parameter)
- **Einstein rings**: Bright rings form at the photon sphere where light orbits
- **Multiple images**: Light can take different paths around the black hole
- **Adaptive raymarching**: Smaller steps near event horizon for accuracy

#### Gravitational Redshift
- Light loses energy climbing out of gravity well
- Redshift factor: √(1 - Rs/r)
- Results in dimmer, redder light from inner regions

#### Visual Features
- **Event Horizon**: Pure black sphere at r = Rs
- **Photosphere**: Glowing plasma layer at ISCO (3Rs)
- **Temperature-based colors**: Hotter regions (closer to ISCO) appear blue-white, cooler regions orange-red

### 2. **Accretion Disc Shader** (`AccretionDisc.shader`)

#### Keplerian Rotation
- Orbital velocity: v(r) ∝ 1/√r
- Inner regions rotate faster than outer regions
- Physically accurate differential rotation creates spiral structure

#### Temperature Distribution
- Follows thin disc theory: T(r) ∝ r^(-3/4)
- **Shakura-Sunyaev model** for accretion disc physics
- Inner regions (~ISCO) can reach millions of Kelvin

#### Blackbody Radiation
- Color determined by temperature (Wien's displacement law)
- Hot inner disc: Blue-white (high energy photons)
- Cool outer disc: Orange-red (low energy photons)
- Realistic spectral emission approximation

#### Relativistic Doppler Beaming
- **Approaching side** (rotation towards viewer):
  - Blue-shifted (higher frequency)
  - Appears brighter due to relativistic beaming (intensity ∝ γ³)
  - Light compressed by motion
  
- **Receding side** (rotation away from viewer):
  - Red-shifted (lower frequency)
  - Appears dimmer
  - Light stretched by motion

- **Asymmetry**: Creates characteristic bright-dim pattern seen in real black hole images

#### Gravitational Effects on Disc
- **Gravitational redshift**: Light from inner disc loses energy
- **Lensing**: Disc light bends around black hole
- **Multiple images**: Top and bottom of disc can appear above/below black hole

#### Disc Structure
- **Spiral arms**: Density waves propagate through disc
- **Turbulence**: FBM noise creates realistic gas dynamics
- **Hot spots**: Local density/temperature variations
- **ISCO glow**: Strong emission at innermost stable orbit

## Parameters Guide

### Black Hole Shader Parameters

| Parameter | Physical Meaning | Typical Value | Range |
|-----------|-----------------|---------------|--------|
| `_SchwarzschildRadius` | Event horizon size (Rs = 2GM/c²) | 1.0 | 0.1 - 5.0 |
| `_PhotonSphereRadius` | Light orbit radius | 1.5 | Fixed at 1.5Rs |
| `_LensingStrength` | Ray bending intensity | 2.0 | 0 - 5.0 |
| `_SpaceDistortion` | Curvature strength | 4.5 | 0 - 10.0 |
| `_DiscLensingIterations` | Ray integration steps | 32 | 8 - 64 |
| `_PhotosphereIntensity` | ISCO glow brightness | 3.5 | 0 - 10.0 |

### Accretion Disc Parameters

| Parameter | Physical Meaning | Typical Value | Range |
|-----------|-----------------|---------------|--------|
| `_DiscInnerRadius` | ISCO position | 3.0 Rs | 2.0 - 4.0 |
| `_DiscOuterRadius` | Outer edge | 10.0 Rs | 5 - 15 |
| `_InnerTemperature` | Peak temperature | 5.0 | 1 - 10 |
| `_TemperatureFalloff` | T(r) exponent | 0.75 | 0.5 - 1.0 |
| `_RotationSpeed` | Keplerian velocity | 1.0 | 0.1 - 5.0 |
| `_ViewAngle` | Inclination (degrees) | 60° | 0 - 90 |
| `_DopplerIntensity` | Beaming strength | 1.5 | 0 - 3.0 |
| `_BeamingPower` | Relativistic boost | 3.0 | 1 - 5 |
| `_GravRedshift` | Redshift strength | 0.8 | 0 - 2.0 |
| `_SpiralArms` | Number of arms | 2 | 0 - 8 |
| `_SpiralTightness` | Arm wind-up | 8.0 | 1 - 20 |
| `_InnerGlow` | ISCO brightness | 5.0 | 1 - 10 |

## Scientific Accuracy

### What's Physically Accurate:
✅ Schwarzschild metric for non-rotating black holes
✅ Keplerian orbital mechanics
✅ Thin disc temperature profile (T ∝ r^(-3/4))
✅ Blackbody radiation approximation
✅ Relativistic Doppler beaming
✅ Gravitational redshift (1 - Rs/r)^(1/2)
✅ Light deflection angle (2Rs/impact parameter)
✅ ISCO at 3Rs (for non-rotating black hole)
✅ Photon sphere at 1.5Rs

### Simplifications for Real-Time Performance:
⚠️ Simplified geodesic integration (not full Kerr metric)
⚠️ Approximate blackbody colors (not full Planck spectrum)
⚠️ Limited ray steps (real physics needs infinite precision)
⚠️ No frame-dragging effects (Kerr black holes)
⚠️ Simplified lensing (no multiple orbit images)

## Performance Optimization

### Recommended Settings:
- **High Quality**: 64 lensing steps, full distortion
- **Medium Quality**: 32 lensing steps (default)
- **Low Quality**: 16 lensing steps, reduced distortion

### Performance Tips:
1. Reduce `_DiscLensingIterations` for faster rendering
2. Lower mesh resolution for distant black holes
3. Use LOD system for multiple black holes
4. Disable lensing for very distant objects

## References

1. **Kip Thorne et al. (2015)** - "Gravitational Lensing by Spinning Black Holes in Astrophysics, and in the Movie Interstellar"
   - arXiv:1502.03808
   - Full DNGR (Double Negative Gravitational Renderer) methodology

2. **Shakura & Sunyaev (1973)** - Black hole accretion disc theory
   - Temperature distribution in thin discs
   - α-disc model

3. **Adrian Boegli** - Raymarching Distance Fields in Unity
   - https://adrianb.io/2016/10/01/raymarching.html
   - Practical implementation techniques

4. **GitHub Implementations**:
   - MangoButtermilch/Unity-volumetric-black-hole (compute shader approach)
   - dogefromage/unity-black-hole (physically correct geodesics)

## Future Enhancements

### Possible Additions:
- [ ] Full Kerr metric for rotating black holes
- [ ] Frame-dragging effects (Lense-Thirring precession)
- [ ] Higher-order lensing (photons orbiting multiple times)
- [ ] Proper radiative transfer through disc
- [ ] Jet emission from accretion disc
- [ ] Time-dependent variability (flares, fluctuations)
- [ ] Spectral line emission (iron Kα line)
- [ ] Shadow size variation with spin

### Kerr Metric (Rotating Black Holes):
- Inner edge can be as close as 1Rs (prograde orbit)
- Ergosphere allows energy extraction
- Frame dragging twists spacetime
- More complex geodesic equations

## Usage in Scene

```csharp
// In SolarSystemParallaxManager.cs
[Header("Black Hole Accretion Disc - Physically Accurate")]
schwarzschildRadius = 1.0f;          // Event horizon size
accretionDiscInnerRadius = 3.0f;     // ISCO for non-rotating BH
accretionDiscOuterRadius = 10.0f;    // Outer edge
discViewAngle = 65f;                 // Viewing inclination
enableGravitationalLensing = true;   // Enable Einstein rings
lensingRaySteps = 32;                // Quality vs performance
spaceDistortion = 4.5f;              // Curvature strength
```

## Visualization Tips

### For Best Visual Results:
1. **Viewing angle**: 60-75° shows both Doppler asymmetry and disc structure
2. **Lighting**: Black holes appear best against star fields
3. **Animation**: Keplerian rotation is automatically applied
4. **HDR**: Use HDR rendering for bright ISCO glow
5. **Bloom**: Post-processing bloom enhances photosphere

### Expected Visual Features:
- **Event horizon**: Pure black sphere
- **Photon sphere**: Bright ring at 1.5Rs
- **ISCO glow**: Intense emission at 3Rs
- **Doppler asymmetry**: One side of disc brighter than other
- **Color gradient**: Blue-white inside, orange-red outside
- **Spiral structure**: Density waves in disc
- **Einstein ring**: Background light bent into ring

## Troubleshooting

**Q: Black hole appears as solid color**
- Check that `_SchwarzschildRadius` is set correctly
- Ensure camera is not inside event horizon
- Verify material is assigned

**Q: Accretion disc not visible**
- Check `_Intensity` parameter (increase if too dim)
- Verify inner/outer radius makes sense for scale
- Ensure transparency rendering is enabled

**Q: No Doppler effect visible**
- Increase `_DopplerIntensity` 
- Check `_ViewAngle` (edge-on views show more Doppler)
- Increase `_BeamingPower` for stronger effect

**Q: Performance issues**
- Reduce `_DiscLensingIterations` to 16 or lower
- Lower mesh segment count
- Disable lensing for distant objects

## Credits

**Implementation**: Based on research from astrophysics literature and practical GPU raymarching techniques
**Shader Architecture**: HLSL/Unity URP-compatible
**Physics Consultant**: Kip Thorne's "Interstellar" paper (highly recommended reading!)
