# Quick Reference - Gravitational Lensing Implementation

## Files Created/Modified

### ✅ NEW
- `BlackHolePhysics.cginc` - Physics calculations library
- `BlackHoleController.cs` - Unity component script
- `SETUP_GUIDE.md` - Detailed setup instructions

### ✅ UPDATED
- `BlackHole.shader` - Now does ray-traced gravitational lensing
- `AccretionDisc.shader` - Uses physics library functions

## Quick Setup (5 minutes)

1. **Assign Materials**
   - Black hole GameObject → BlackHole.mat (shader: Custom/BlackHoleGravitationalLens)
   - Disc GameObject → AccretionDiscMaterial.mat (shader: Custom/AccretionDisc)

2. **Add Controller**
   - Select black hole → Add Component → BlackHoleController
   - Drag disc GameObject to "Accretion Disc" field

3. **Position Disc**
   - Same Y position as black hole
   - Horizontal orientation (rotation 0,0,0)

4. **Test**
   - Press Play
   - Move camera around
   - Should see light bending!

## Key Parameters

### Black Hole Material
```
Mass: 1.0               // Higher = stronger gravity
Disc Inner: 6.0         // ISCO radius
Disc Outer: 50.0        // Visible disc extent
Rotation Speed: -100    // Negative = counterclockwise
Beaming: 0.4           // Doppler brightness boost
Brightness: 1.0        // Overall exposure
```

### BlackHoleController
```
Mass: 1.0
Schwarzschild Radius: 2.0  // = 2×Mass
Disc Inner: 6.0           // = 6×Mass (ISCO)
Disc Outer: 50.0
Use Strong Field: ✓       // Better but slower
Lens Stars: ✓             // Enable star lensing
```

## What You'll See

✅ **Light Bending** - Disc warps around black hole  
✅ **Doppler Effect** - One side bright, other dim  
✅ **Event Horizon** - Black sphere at center  
✅ **Photon Ring** - Bright ring near photon sphere  
✅ **Temperature** - Blue inner, red outer  
✅ **Beaming** - Approaching side extra bright  

## Common Issues

| Problem | Solution |
|---------|----------|
| All black | Increase Brightness to 2.0 |
| No lensing | Enable "Use Strong Field" |
| Disc not visible | Check disc Y position = black hole Y |
| Shader errors | Verify BlackHolePhysics.cginc exists |
| Low FPS | Disable "Use Strong Field" |

## Physics Constants

```
Schwarzschild radius: r_s = 2M
Photon sphere: r_ph = 2.598M (3√3 M)
ISCO: r_isco = 6M
Critical impact: b_c ≈ 2.6M
```

## Shader Functions (BlackHolePhysics.cginc)

```hlsl
// Temperature to color
float3 BlackbodyColor(float temp)

// Disc temperature at radius
float CalculateDiscTemperature(float r, float rInner, float maxTemp)

// Orbital velocity
float KeplerianVelocity(float r, float mass)

// Doppler shift
float DopplerFactor(float r, float angle, float mass, float rotSpeed)

// Gravitational redshift
float GravitationalRedshift(float r, float mass)

// Deflection angle
float CalculateDeflectionAngle(float b, float mass, bool useStrong)

// Ray-disc intersection
bool IntersectAccretionDisc(...)
```

## Debugging

Add to BlackHole.shader frag():
```hlsl
// Show impact parameter
return half4(b / 10.0, 0, 0, 1);

// Show if ray hits disc
return half4(hitDisc ? 1.0 : 0.0, 0, 0, 1);

// Show disc radius
return half4(discRadius / 50.0, 0, 0, 1);
```

## Performance Tips

- Disable strong field for distant views
- Reduce disc outer radius
- Lower mesh polygon count
- Disable star lensing if not needed

## Presets

### Dramatic (Interstellar-style)
Mass: 1.0, Outer: 100, Rotation: -150, Beaming: 0.8, Brightness: 2.0

### Scientific
Mass: 1.0, Outer: 50, Rotation: -100, Beaming: 0.4, Brightness: 1.0

### Performance
Use Strong Field: ✗, Outer: 30, Beaming: 0.3

## Next Steps

1. ✅ Basic lensing working
2. ⏳ Create deflection LUT for performance
3. ⏳ Implement star lensing intersection
4. ⏳ Add multiple disc images (light wrapping)
5. ⏳ Add noise textures for turbulence

## Support

See detailed guides:
- SETUP_GUIDE.md - Full setup instructions
- IMPLEMENTATION_ROADMAP.md - Development plan
- BLACK_HOLE_MATH_FORMULAS.md - Physics equations
- GRAVITATIONAL_LENSING_ANALYSIS.md - Technical details

Enjoy your scientifically accurate black hole! 🌌
