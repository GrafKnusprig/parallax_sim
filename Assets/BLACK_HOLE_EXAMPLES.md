# Black Hole Shader Configuration Examples

## Scene Setup Examples

### 1. M87* (First Black Hole Image - EHT 2019)
The supermassive black hole in M87 galaxy - this is what the Event Horizon Telescope photographed!

```
Black Hole Material:
  _SchwarzschildRadius: 1.0
  _PhotonSphereRadius: 1.5
  _LensingStrength: 2.5
  _SpaceDistortion: 5.0
  _DiscLensingIterations: 48
  _PhotosphereIntensity: 4.0

Accretion Disc Material:
  _DiscInnerRadius: 3.0
  _DiscOuterRadius: 12.0
  _InnerTemperature: 6.0
  _TemperatureFalloff: 0.75
  _ViewAngle: 17°  // M87* viewed nearly face-on
  _DopplerIntensity: 1.8
  _BeamingPower: 3.5
  _RotationSpeed: 0.8
  _SpiralArms: 0  // M87* shows ring structure
  _Intensity: 20.0
```

### 2. Sagittarius A* (Milky Way's Black Hole)
Our galaxy's central black hole - also imaged by EHT in 2022.

```
Black Hole Material:
  _SchwarzschildRadius: 1.0
  _LensingStrength: 2.2
  _SpaceDistortion: 4.8
  _DiscLensingIterations: 40

Accretion Disc Material:
  _DiscInnerRadius: 3.0
  _DiscOuterRadius: 10.0
  _InnerTemperature: 7.0
  _ViewAngle: 50°  // Sgr A* viewed at moderate inclination
  _DopplerIntensity: 2.0
  _BeamingPower: 4.0
  _RotationSpeed: 1.5  // Faster variability
  _SpiralArms: 2
  _SpiralTightness: 12.0
  _NoiseScale: 10.0  // More turbulent
  _Intensity: 18.0
```

### 3. Gargantua (Interstellar Movie Style)
The fictional black hole from the movie - spinning rapidly (Kerr metric approximation).

```
Black Hole Material:
  _SchwarzschildRadius: 1.0
  _PhotonSphereRadius: 1.5
  _LensingStrength: 3.5  // Strong lensing
  _SpaceDistortion: 6.5
  _DiscLensingIterations: 64  // High quality
  _PhotosphereIntensity: 5.0

Accretion Disc Material:
  _DiscInnerRadius: 2.5  // Closer due to spin (simulated)
  _DiscOuterRadius: 15.0  // Large disc
  _InnerTemperature: 8.0  // Very hot
  _TemperatureFalloff: 0.7
  _ViewAngle: 63°  // Iconic movie angle
  _DopplerIntensity: 2.5  // Strong asymmetry
  _BeamingPower: 4.5
  _RotationSpeed: 2.0  // Fast rotation
  _SpiralArms: 3
  _SpiralTightness: 10.0
  _InnerGlow: 8.0  // Bright ISCO
  _Intensity: 25.0
```

### 4. Stellar-Mass Black Hole (Cygnus X-1)
A black hole formed from a collapsed star, feeding from a companion star.

```
Black Hole Material:
  _SchwarzschildRadius: 1.0
  _LensingStrength: 1.8
  _SpaceDistortion: 3.5
  _DiscLensingIterations: 32
  _PhotosphereIntensity: 3.0

Accretion Disc Material:
  _DiscInnerRadius: 3.0
  _DiscOuterRadius: 8.0  // Smaller disc
  _InnerTemperature: 9.0  // Very hot (X-ray emission)
  _TemperatureFalloff: 0.8
  _ViewAngle: 35°
  _DopplerIntensity: 1.6
  _BeamingPower: 3.0
  _RotationSpeed: 3.0  // Very fast
  _SpiralArms: 4  // More chaotic
  _SpiralTightness: 15.0
  _NoiseScale: 12.0
  _Intensity: 30.0  // X-ray bright
  _InnerGlow: 6.0
```

### 5. Quasar (Active Galactic Nucleus)
Extremely bright accretion disc powering a distant galaxy.

```
Black Hole Material:
  _SchwarzschildRadius: 1.0
  _LensingStrength: 2.0
  _SpaceDistortion: 5.5
  _DiscLensingIterations: 40

Accretion Disc Material:
  _DiscInnerRadius: 3.0
  _DiscOuterRadius: 20.0  // Huge disc
  _InnerTemperature: 10.0  // Extremely hot
  _TemperatureFalloff: 0.75
  _ViewAngle: 70°  // Edge-on for jet visibility
  _DopplerIntensity: 2.8
  _BeamingPower: 5.0  // Extreme beaming
  _RotationSpeed: 1.2
  _SpiralArms: 2
  _SpiralTightness: 6.0
  _NoiseScale: 8.0
  _Intensity: 40.0  // Extremely bright
  _InnerGlow: 10.0
  _Transparency: 0.7
```

## Viewing Angle Effects

### Face-On (0-30°)
- Symmetric appearance
- No strong Doppler asymmetry
- Ring-like structure visible
- Good for showing Einstein ring
```
_ViewAngle: 15°
_DopplerIntensity: 1.0  // Subtle
```

### Intermediate (30-60°)
- Balanced view of structure and dynamics
- Moderate Doppler asymmetry
- 3D appearance
- Best for educational visualization
```
_ViewAngle: 45°
_DopplerIntensity: 1.5
```

### Highly Inclined (60-80°)
- Strong Doppler asymmetry (bright/dim sides)
- Disc appears elliptical
- Dramatic visual effect
- Shows relativistic beaming clearly
```
_ViewAngle: 70°
_DopplerIntensity: 2.0
_BeamingPower: 4.0
```

### Edge-On (80-90°)
- Thin line appearance
- Maximum Doppler effect
- Jets (if implemented) visible
- Einstein ring above/below disc
```
_ViewAngle: 85°
_DopplerIntensity: 2.5
```

## Performance Presets

### Ultra (Cinematic)
```
_DiscLensingIterations: 64
_SpaceDistortion: 6.0
Ring Mesh Segments: 512
Expected: 30-45 FPS on RTX 3060
```

### High (Default)
```
_DiscLensingIterations: 32
_SpaceDistortion: 4.5
Ring Mesh Segments: 256
Expected: 60 FPS on RTX 3060
```

### Medium (Balanced)
```
_DiscLensingIterations: 24
_SpaceDistortion: 3.5
Ring Mesh Segments: 128
Expected: 90 FPS on GTX 1070
```

### Low (Performance)
```
_DiscLensingIterations: 16
_SpaceDistortion: 2.5
Ring Mesh Segments: 64
Expected: 120+ FPS on GTX 1070
```

## Animation Tips

### Rotating Camera
```csharp
// Orbit around black hole
float angle = Time.time * 0.1f;
float distance = 15.0f;
camera.position = new Vector3(
    Mathf.Cos(angle) * distance,
    5.0f,
    Mathf.Sin(angle) * distance
);
camera.LookAt(blackHole.position);
```

### Dynamic Parameters
```csharp
// Pulsating accretion rate
float pulse = 1.0f + 0.3f * Mathf.Sin(Time.time * 2.0f);
discMaterial.SetFloat("_Intensity", baseIntensity * pulse);

// Varying turbulence
float turbulence = Mathf.PerlinNoise(Time.time * 0.5f, 0) * 15.0f + 5.0f;
discMaterial.SetFloat("_NoiseScale", turbulence);
```

### Accretion Event
```csharp
// Simulate matter falling in
float fallInTime = 5.0f; // seconds
float t = (Time.time % fallInTime) / fallInTime;
float brightness = Mathf.Exp(-t * 3.0f); // Exponential decay
discMaterial.SetFloat("_InnerGlow", baseglow * (1.0f + brightness * 5.0f));
```

## Color Schemes

### Realistic (EHT-style)
- Based on actual radio telescope data
- Orange to white gradient
```
_InnerTemperature: 6.0
_TemperatureFalloff: 0.75
Result: Orange core, dimming outward
```

### X-Ray Binary
- Hot, blue-white inner regions
- Very high temperature
```
_InnerTemperature: 10.0
_TemperatureFalloff: 0.8
Result: Blue-white to orange gradient
```

### Optical (Visible Light)
- What we might see with our eyes (if we survived!)
```
_InnerTemperature: 5.0
_TemperatureFalloff: 0.7
Result: Yellow-white to red
```

## Troubleshooting Guide

### "Black hole looks like a solid ball"
**Problem**: No lensing effects visible
**Solution**: 
```
Increase _LensingStrength to 2.5-3.0
Increase _DiscLensingIterations to 40+
Check that _SpaceDistortion > 4.0
```

### "Disc appears static/not rotating"
**Problem**: Keplerian rotation not visible
**Solution**:
```
Increase _RotationSpeed to 1.5-2.0
Add _SpiralArms: 2-4 for visible structure
Increase _SpiralTightness to 10-15
View at 45-70° angle for better visibility
```

### "No Doppler asymmetry"
**Problem**: Both sides of disc look same brightness
**Solution**:
```
Set _ViewAngle: 60-75° (not face-on!)
Increase _DopplerIntensity to 2.0+
Increase _BeamingPower to 4.0+
Ensure rotation is visible
```

### "Too bright/washed out"
**Problem**: Overexposed appearance
**Solution**:
```
Reduce _Intensity to 10-15
Reduce _InnerGlow to 3-5
Use HDR with proper tone mapping
Enable bloom with threshold
```

### "Too dim/barely visible"
**Problem**: Can't see disc
**Solution**:
```
Increase _Intensity to 20-30
Increase _InnerTemperature to 6-8
Check _Transparency isn't too low
Verify material is not culled
```

### "Flickering/artifacts"
**Problem**: Visual glitches
**Solution**:
```
Increase _DiscLensingIterations (32+)
Check mesh has smooth normals
Reduce _NoiseScale if too chaotic
Ensure camera isn't inside event horizon
```

## Scientific Visualization Modes

### Educational Mode
Clear structure, exaggerated effects for teaching
```
_ViewAngle: 60°
_DopplerIntensity: 2.5  // Exaggerated
_SpiralArms: 3  // Visible structure
_Intensity: 25.0  // Bright
_InnerGlow: 8.0  // Prominent ISCO
```

### Realistic Mode
Based on actual observations
```
_ViewAngle: 17-50°  // Based on known objects
_DopplerIntensity: 1.5  // Measured values
_SpiralArms: 0-2  // Subtle
_Intensity: 15.0  // Conservative
_InnerGlow: 5.0  // Physical
```

### Artistic Mode
Visually striking, scientifically plausible
```
_ViewAngle: 70°  // Dramatic
_DopplerIntensity: 3.0  // Strong
_SpiralArms: 4  // Beautiful structure
_Intensity: 30.0  // Vibrant
_InnerGlow: 10.0  // Glowing
_SpiralTightness: 12.0  // Elegant curves
```

## Integration with Unity Timeline

```csharp
// Example Timeline track for black hole flyby
0s:    Camera far, face-on view (ViewAngle: 0°)
5s:    Zoom in, rotate to 45°
10s:   Close approach, ViewAngle: 75° (dramatic)
15s:   Pass through disc plane (edge-on: 90°)
20s:   Pull back, return to 45°
25s:   Final wide shot

// Animate intensity during flyby
0-10s:  Intensity: 15 → 25 (approaching)
10-15s: Intensity: 25 → 30 (closest approach)
15-25s: Intensity: 30 → 15 (receding)
```

## Recommended Post-Processing

1. **Bloom**: Threshold 1.5, Intensity 0.3
2. **HDR**: ACES Tonemapping
3. **Color Grading**: Slight warm tint
4. **Vignette**: Subtle (0.2)
5. **Chromatic Aberration**: Very subtle near edges
6. **Motion Blur**: For camera movement only

This enhances the hot ISCO glow and creates cinematic appearance!
