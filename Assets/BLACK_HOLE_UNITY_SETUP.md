# Black Hole Setup Guide - Unity Inspector Configuration

## ⚠️ Quick Fix Checklist

If you can't see the photosphere or gravitational lensing, check these:

### 1. **Black Hole Sphere Size** ⭐ CRITICAL
The black hole GameObject sphere must be **large enough** to show the photosphere at ISCO (3Rs).

**In Unity Inspector:**
```
Black Hole GameObject
  └─ Transform
      └─ Scale: (6, 6, 6) or larger
         // Must be at least 2x the ISCO radius (3Rs × 2 = 6)
         // Photosphere appears at 3Rs from center
```

**Why?** The photosphere renders on the sphere surface at radius 3.0 in shader space. If your sphere is too small, it won't render.

### 2. **Material Assignment** ⭐ CRITICAL

**Black Hole Sphere:**
```
Inspector → Mesh Renderer → Materials
  └─ Element 0: BlackHole (Material)
      // Should use Custom/BlackHole shader
```

**Accretion Disc:**
```
Inspector → Mesh Renderer → Materials
  └─ Element 0: AccretionDiscMaterial (Material)
      // Should use Custom/AccretionDisc shader
```

### 3. **Enable HDR Camera** ⭐ IMPORTANT
The photosphere is very bright and needs HDR to display properly.

```
Main Camera → Camera Component
  └─ Rendering
      └─ Allow HDR: ✓ CHECKED
```

### 4. **Enable Bloom (Recommended)**
Makes the photosphere and ISCO glow visible.

```
Main Camera → Post-Processing Volume
  └─ Bloom
      ✓ Enabled
      Threshold: 1.0
      Intensity: 0.3-0.5
```

---

## 📋 Step-by-Step Unity Setup

### Step 1: Black Hole GameObject Configuration

1. **Select your Black Hole GameObject** in the Hierarchy
2. **Ensure it has a Sphere mesh** or create one:
   ```
   Right-click in Hierarchy → 3D Object → Sphere
   Name it "BlackHole"
   ```

3. **Set the Transform Scale:**
   ```
   Transform Component:
     Position: (0, 0, 0) // Relative to parent
     Rotation: (0, 0, 0)
     Scale: (6, 6, 6)    // ⭐ CRITICAL - Must show 3Rs radius
   ```

4. **Assign BlackHole Material:**
   ```
   Mesh Renderer Component:
     Materials → Element 0: Drag "BlackHole.mat" here
   ```

5. **Verify Material Settings** (click on BlackHole.mat):
   ```
   Inspector:
     Shader: Custom/BlackHole ✓
     
     _SchwarzschildRadius: 1.0
     _PhotonSphereRadius: 1.5
     _LensingStrength: 2.0
     _SpaceDistortion: 4.5
     _DiscLensingIterations: 32
     _PhotosphereIntensity: 3.5 (increase to 5-8 if too dim)
     
     _EventHorizonColor: Black (0, 0, 0)
     _PhotosphereColor: Orange (1.0, 0.6, 0.2) or Yellow-White (1, 0.9, 0.7)
   ```

### Step 2: Accretion Disc Configuration

The disc is created automatically by the SolarSystemParallaxManager, but verify:

1. **In Scene View**, you should see:
   ```
   BlackHole (parent)
     ├─ AccretionDisc (created at runtime)
     ├─ LensingRing_Top (if gravitational lensing enabled)
     └─ LensingRing_Bottom (if gravitational lensing enabled)
   ```

2. **Select SolarSystemParallaxManager GameObject:**
   ```
   Inspector → Solar System Parallax Manager Script:
   
   Black Hole Accretion Disc - Physically Accurate
     Accretion Disc Material: AccretionDiscMaterial ✓
     Schwarzschild Radius: 1.0
     Accretion Disc Inner Radius: 3.0 (ISCO)
     Accretion Disc Outer Radius: 10.0
     Disc View Angle: 65 (60-75 for best Doppler effect)
     
     Enable Gravitational Lensing: ✓ CHECKED
     Lensing Ray Steps: 32 (lower for performance)
     Space Distortion: 4.5
     Lensing Image Opacity: 0.6
   ```

3. **If disc is not created**, check Console for:
   ```
   "[Physics] Accretion disc configured: ISCO=3Rs..."
   "[Physics] Created gravitational lensing ring..."
   ```

### Step 3: Camera & Lighting Setup

1. **Main Camera Settings:**
   ```
   Camera Component:
     Clear Flags: Skybox (to see background stars)
     Background: Black or Skybox
     
     Rendering:
       Allow HDR: ✓ CHECKED ⭐ CRITICAL
       Allow MSAA: ✓ (for smooth edges)
   ```

2. **Position Camera to View Black Hole:**
   ```
   Transform:
     Position: (15, 5, -15) // Adjust to taste
     Rotation: Look at black hole (use Ctrl+Shift+F in Scene)
   ```

3. **Add Skybox (for realistic space background):**
   ```
   Window → Rendering → Lighting
     Environment:
       Skybox Material: Drag a space/stars skybox here
       (Or use Starfield Skybox from your Assets)
   ```

### Step 4: Post-Processing Setup (Recommended)

1. **Create Global Volume:**
   ```
   Right-click in Hierarchy → Volume → Global Volume
   ```

2. **Add Bloom:**
   ```
   Global Volume → Profile:
     Add Override → Post-processing → Bloom
       ✓ Enabled
       Threshold: 1.0-1.5
       Intensity: 0.3-0.5
       Scatter: 0.7
   ```

3. **Add Tonemapping (for HDR):**
   ```
   Add Override → Post-processing → Tonemapping
       ✓ Enabled
       Mode: ACES (cinematic look)
   ```

---

## 🔍 Troubleshooting

### Problem: "I don't see the photosphere"

**Diagnosis:**
- Photosphere appears at radius 3.0 in shader coordinates
- Black hole sphere mesh must be large enough to contain it

**Solutions:**

1. ✅ **Increase Black Hole Scale:**
   ```
   Black Hole GameObject → Transform → Scale: (8, 8, 8)
   ```

2. ✅ **Increase Photosphere Intensity:**
   ```
   BlackHole Material → _PhotosphereIntensity: 6.0-10.0
   ```

3. ✅ **Enable HDR on Camera:**
   ```
   Main Camera → Rendering → Allow HDR: ✓
   ```

4. ✅ **Add Bloom Effect:**
   ```
   Makes bright glow visible
   ```

5. ✅ **Check Photosphere Color:**
   ```
   BlackHole Material → _PhotosphereColor: 
     Try (1, 0.9, 0.7) for bright yellow-white
     Or (1, 1, 1) for pure white hot
   ```

### Problem: "Accretion disc is not visible"

**Solutions:**

1. ✅ **Increase Disc Intensity:**
   ```
   AccretionDiscMaterial → _Intensity: 20.0-30.0
   ```

2. ✅ **Check Transparency:**
   ```
   AccretionDiscMaterial → _Transparency: 0.85-1.0
   ```

3. ✅ **Verify View Angle:**
   ```
   SolarSystemParallaxManager → Disc View Angle: 60-70
   (Not 90 - that's edge-on and very thin!)
   ```

4. ✅ **Check Material Queue:**
   ```
   AccretionDiscMaterial should be in "Transparent" queue
   ```

### Problem: "I don't see gravitational lensing"

**Solutions:**

1. ✅ **Enable in Script:**
   ```
   SolarSystemParallaxManager:
     Enable Gravitational Lensing: ✓ CHECKED
   ```

2. ✅ **Check Runtime Objects:**
   ```
   Play Mode → Hierarchy:
     BlackHole
       ├─ LensingRing_Top (should appear)
       └─ LensingRing_Bottom (should appear)
   ```

3. ✅ **Increase Lensing Visibility:**
   ```
   SolarSystemParallaxManager:
     Lensing Image Opacity: 0.8-1.0
   ```

4. ✅ **View from Proper Angle:**
   ```
   Lensing is most visible at 45-75° viewing angle
   Not visible face-on (0-20°)
   ```

### Problem: "Black hole looks flat/solid"

**Solutions:**

1. ✅ **Increase Lensing Strength:**
   ```
   BlackHole Material:
     _LensingStrength: 3.0-4.0
     _SpaceDistortion: 5.0-7.0
   ```

2. ✅ **More Ray Steps:**
   ```
   _DiscLensingIterations: 48-64
   (Warning: slower performance)
   ```

3. ✅ **Check Rendering Path:**
   ```
   Project Settings → Graphics:
     Scriptable Render Pipeline: UniversalRenderPipelineAsset
   ```

### Problem: "Everything is too dim"

**Solutions:**

1. ✅ **Increase All Intensities:**
   ```
   BlackHole Material:
     _PhotosphereIntensity: 8.0
   
   AccretionDiscMaterial:
     _Intensity: 25.0
     _InnerGlow: 8.0
   ```

2. ✅ **HDR Tonemapping:**
   ```
   Post-Processing → Tonemapping → Mode: ACES
   Adjust exposure if needed
   ```

3. ✅ **Bloom Effect:**
   ```
   Bloom → Intensity: 0.5-0.8
   ```

---

## 🎨 Recommended Visual Settings

### For M87* Style (EHT Image)
```
BlackHole Material:
  _PhotosphereIntensity: 4.0
  _PhotosphereColor: (1.0, 0.6, 0.2) Orange
  _LensingStrength: 2.5

AccretionDiscMaterial:
  _Intensity: 20.0
  _InnerTemperature: 6.0
  _ViewAngle: 17 (face-on)
  _DopplerIntensity: 1.8
  _SpiralArms: 0 (smooth ring)

Camera Position: View from slightly above
```

### For Interstellar "Gargantua" Style
```
BlackHole Material:
  _PhotosphereIntensity: 6.0
  _PhotosphereColor: (1.0, 0.9, 0.7) Yellow-white
  _LensingStrength: 3.5
  _SpaceDistortion: 6.5

AccretionDiscMaterial:
  _Intensity: 25.0
  _InnerTemperature: 8.0
  _ViewAngle: 63 (iconic angle)
  _DopplerIntensity: 2.5
  _BeamingPower: 4.5
  _SpiralArms: 3

Black Hole Scale: (10, 10, 10)
Camera: Orbit around at distance 20-30 units
```

### For Dramatic Visualization
```
BlackHole Material:
  _PhotosphereIntensity: 8.0
  _LensingStrength: 4.0

AccretionDiscMaterial:
  _Intensity: 30.0
  _InnerGlow: 10.0
  _ViewAngle: 70 (strong Doppler)
  _DopplerIntensity: 3.0
  _SpiralArms: 4
  _SpiralTightness: 12.0

Post-Processing:
  Bloom → Intensity: 0.6
  Color Grading → Saturation: 1.1
```

---

## 🎬 Runtime Animation Script

Add this script to smoothly orbit the camera:

```csharp
using UnityEngine;

public class BlackHoleCamera : MonoBehaviour
{
    public Transform blackHole;
    public float orbitSpeed = 5f;
    public float orbitRadius = 20f;
    public float height = 5f;
    
    void Update()
    {
        if (blackHole == null) return;
        
        float angle = Time.time * orbitSpeed * Mathf.Deg2Rad;
        
        Vector3 position = new Vector3(
            Mathf.Cos(angle) * orbitRadius,
            height,
            Mathf.Sin(angle) * orbitRadius
        );
        
        transform.position = blackHole.position + position;
        transform.LookAt(blackHole);
    }
}
```

---

## 📊 Performance Settings

| Quality Level | Lensing Steps | Space Distortion | Expected FPS |
|--------------|---------------|------------------|--------------|
| Ultra | 64 | 6.5 | 30-40 |
| High | 32 | 4.5 | 60 |
| Medium | 24 | 3.5 | 90 |
| Low | 16 | 2.5 | 120+ |

Adjust in SolarSystemParallaxManager → Lensing Ray Steps

---

## ✅ Final Verification Checklist

Before asking "why doesn't it work":

- [ ] Black hole sphere scale is at least (6, 6, 6)
- [ ] BlackHole.mat is assigned to black hole sphere
- [ ] AccretionDiscMaterial.mat exists and uses AccretionDisc shader
- [ ] Camera has "Allow HDR" enabled
- [ ] Gravitational lensing is enabled in SolarSystemParallaxManager
- [ ] View angle is 60-75° (not edge-on at 90°)
- [ ] Photosphere intensity is at least 3.5 (higher for better visibility)
- [ ] Disc intensity is at least 15.0
- [ ] Console shows "[Physics] Accretion disc configured..." message
- [ ] Scene is in Play Mode (disc is created at runtime)
- [ ] Post-processing volume exists with Bloom enabled

If all checked and still not visible, **increase all intensity values by 2-3x** and check the Scene view, not just Game view!

---

## 🆘 Still Not Working?

**Check Console Logs:**
Look for these messages when entering Play Mode:
```
[Physics] Accretion disc configured: ISCO=3Rs, Outer=10Rs, ViewAngle=65°
[Physics] Black hole shader configured: Rs=1, Photon Sphere=1.5Rs
[Physics] Created gravitational lensing ring (secondary disc image)
```

If missing, the SolarSystemParallaxManager is not creating the black hole properly.

**Scene View vs Game View:**
- Always check **Scene View** first (F key to focus)
- Game view might have camera in wrong position
- Use Scene view gizmos to see object positions

**Contact Points:**
- Check BLACK_HOLE_EXAMPLES.md for preset configurations
- Check BLACK_HOLE_PHYSICS_README.md for physics explanation
- Unity Console may have shader compilation errors
