# Gravitational Lensing Analysis - Black Hole Shader Implementation

Based on analysis of the Real-Time Black Hole v2 shader implementation.

## Overview

The black hole shader uses ray-tracing techniques combined with General Relativity equations to simulate gravitational lensing in real-time. The implementation is split into modular shader subgraphs that calculate different aspects of the lensing effect.

## Key Shader Components

### 1. **DeflectionAngle.shadersubgraph**
- Calculates the deflection angle of light rays as they pass near the black hole
- This is the core of the gravitational lensing effect
- Takes into account the distance from the black hole and the viewing angle

### 2. **b.shadersubgraph** (Impact Parameter)
- Calculates the impact parameter `b` for photon trajectories
- The impact parameter is the perpendicular distance from the black hole center to the asymptotic trajectory of the photon
- Critical for determining whether light will be captured, orbit, or deflect

### 3. **r_phi_b.shadersubgraph** (Radial Distance)
- Computes the radial distance and angular position based on the impact parameter
- Uses elliptic integrals to solve the geodesic equations

### 4. **phi_p.shadersubgraph** and **phi_TP.shadersubgraph**
- Calculate angular deflection using elliptic integrals
- These implement the exact General Relativity solutions for photon paths

### 5. **Disk Color.shadersubgraph** and **Disk Temperature.shadersubgraph**
- Handle the visual appearance of the accretion disk
- Apply Doppler shifting, gravitational redshift, and relativistic beaming effects
- Temperature-based blackbody radiation coloring

### 6. **KeplerianRotatingClumps.shadersubgraph**
- Simulates rotating material in the accretion disk
- Uses Keplerian orbital velocities

### 7. **BlackbodyColor.shadersubgraph**
- Converts temperature to realistic blackbody radiation colors
- Important for the glow and colors of the accretion disk

## Physics Principles Used

### General Relativity in Schwarzschild Metric

The shader appears to use the Schwarzschild solution for a non-rotating black hole:

```
Impact parameter: b = r * sin(θ) / sqrt(1 - 2M/r)
Deflection angle: Δφ ≈ 4M/b (for large distances)
Exact calculation uses elliptic integrals
```

Where:
- `M` = Black hole mass (in geometric units where G=c=1)
- `r` = radial distance from black hole
- `b` = impact parameter
- `θ` = viewing angle

### Critical Radius Values

1. **Event Horizon**: r = 2M (Schwarzschild radius)
2. **Photon Sphere**: r = 3M (unstable circular orbit for photons)
3. **Innermost Stable Circular Orbit (ISCO)**: r = 6M (for Schwarzschild)

## Implementation for Accretion Disc Lensing

### Key Differences from Skybox Lensing

When lensing an **accretion disc** instead of a skybox, you need to:

1. **Ray-Disc Intersection**
   - After calculating the deflected ray direction, intersect it with the disc plane
   - The disc is typically in the equatorial plane (θ = π/2)
   - Calculate where the bent light ray intersects this plane

2. **Disc Coordinates**
   - Convert the intersection point to disc radial coordinates
   - Apply texture mapping based on distance from black hole center
   - Consider the disc extends from inner radius (typically ~3M or ISCO) to outer radius

3. **Doppler and Relativistic Effects**
   - **Doppler Shift**: Material rotating toward observer is blueshifted, away is redshifted
   - **Gravitational Redshift**: Light climbing out of gravitational well loses energy
   - **Relativistic Beaming**: Material moving near light speed appears brighter when moving toward observer

4. **Multi-Image Formation**
   - Light can wrap around the black hole multiple times
   - Primary image: direct view of disc
   - Secondary images: light that orbits partway around before reaching observer
   - Photon ring: light from behind the black hole that wraps around

### Practical Shader Approach for Your Implementation

```hlsl
// Pseudo-code structure for accretion disc lensing

// 1. Calculate impact parameter from screen UV
float2 screenPos = UV * 2 - 1; // Center at origin
float b = length(screenPos) * viewDistance;
float viewAngle = atan2(screenPos.y, screenPos.x);

// 2. Calculate deflection angle using elliptic integrals
// (This is the complex part - see DeflectionAngle.shadersubgraph)
float deflectionAngle = CalculateDeflectionAngle(b, M, r);

// 3. Trace ray to find disc intersection
float3 rayDir = CalculateDeflectedRayDirection(b, viewAngle, deflectionAngle);
float discIntersectionRadius = IntersectWithDiscPlane(rayOrigin, rayDir);

// 4. Check if intersection is within disc bounds
if (discIntersectionRadius > innerRadius && discIntersectionRadius < outerRadius) {
    // 5. Calculate disc properties at intersection
    float discAngle = atan2(intersectionPoint.y, intersectionPoint.x);
    float temperature = CalculateDiscTemperature(discIntersectionRadius, M);
    float velocity = CalculateKeplerianVelocity(discIntersectionRadius, M);
    
    // 6. Apply relativistic effects
    float dopplerFactor = CalculateDopplerShift(velocity, viewAngle);
    float redshift = CalculateGravitationalRedshift(discIntersectionRadius, M);
    
    // 7. Calculate final color
    float3 intrinsicColor = BlackbodyColor(temperature);
    float3 finalColor = intrinsicColor * dopplerFactor * redshift;
    
    return finalColor;
} else {
    // Ray doesn't hit disc - return background or black
    return backgroundColor;
}
```

### Key Mathematical Components

#### Impact Parameter Calculation
```hlsl
// For a ray at distance d from observer, angle θ from black hole
b = d * sin(viewAngle)
```

#### Deflection Angle (Simplified for weak field)
```hlsl
// Exact formula requires elliptic integrals
// Weak field approximation:
deflection ≈ 4*M / b

// For paths near photon sphere (b ≈ 3√3 M), use exact calculation
```

#### Disc Temperature (Shakura-Sunyaev model)
```hlsl
// Temperature decreases with radius
T(r) ∝ (M_dot / r³)^(1/4) * (1 - sqrt(r_inner/r))^(1/4)
// Where M_dot is accretion rate, r_inner is ISCO
```

#### Keplerian Velocity
```hlsl
v = sqrt(M / r)  // In geometric units
```

#### Doppler Factor
```hlsl
// For material with velocity v at angle φ to line of sight
doppler = 1 / (1 - v*cos(φ))  // Simplified relativistic Doppler
```

#### Gravitational Redshift
```hlsl
redshift = sqrt(1 - 2*M/r)
// Photons lose energy climbing out of potential well
```

## Shader Graph Structure for Your Use Case

1. **Input Properties**:
   - Black hole mass (M)
   - Camera position and orientation
   - Disc inner/outer radii
   - Disc texture or procedural parameters
   - Accretion rate (for temperature)

2. **Ray Setup**:
   - Screen UV → World space ray direction
   - Calculate impact parameter and viewing angle

3. **Gravitational Deflection**:
   - Use impact parameter to calculate deflection
   - Integrate photon geodesic (elliptic integrals or numerical)
   - Handle critical cases (photon sphere, event horizon)

4. **Disc Intersection**:
   - Ray-plane intersection
   - Convert to disc coordinates (r, φ)
   - Check against disc geometry

5. **Physical Effects**:
   - Temperature from radius
   - Velocity from Keplerian orbit
   - Doppler shift calculation
   - Gravitational redshift
   - Relativistic beaming intensity boost

6. **Final Color**:
   - Blackbody radiation color
   - Apply all relativistic factors
   - Add glow/bloom for high temperatures
   - Blend with background/skybox

## Critical Parameters

- **Event Horizon Radius**: `r_s = 2M` (in geometric units)
- **Photon Sphere**: `r_ph = 3M`
- **ISCO**: `r_isco = 6M` (for non-rotating black hole)
- **Disc Inner Edge**: Usually at or slightly outside ISCO
- **Disc Outer Edge**: Arbitrary, typically 20M-100M for visibility

## Elliptic Integrals

The exact solution for photon trajectories requires elliptic integrals of the first and third kind. The shader likely uses:
- Approximations for distant rays
- Lookup tables or analytical approximations for near-field
- Special handling for critical impact parameters (photon sphere)

## Optimization Considerations

1. **LOD for Deflection Calculation**:
   - Use full elliptic integrals only near black hole
   - Switch to weak-field approximation for distant rays

2. **Early Ray Termination**:
   - Check if ray will hit event horizon (capture)
   - Skip complex calculations for rays that miss disc entirely

3. **Texture Sampling**:
   - Use mipmaps for disc texture
   - Consider procedural noise for disc structure

4. **Multi-Pass Rendering**:
   - Render disc without lensing first pass
   - Apply lensing as post-process effect
   - Or: render lensed disc directly

## Light Source Considerations

Unlike a skybox (distant light), the accretion disc is:
- **Close to the black hole**: Strong field effects dominate
- **Emissive**: Self-luminous from temperature
- **Dynamic**: Rotating, potentially turbulent
- **Physically constrained**: Lies in equatorial plane

This means you need to:
- Calculate exact ray-disc intersection
- Apply local lighting (temperature-based emission)
- Handle multiple images from light wrapping around
- Consider occultation (disc blocking parts of itself)

## References from Shader Structure

The shader implements a sophisticated physically-based approach using:
1. Schwarzschild metric photon geodesics
2. Elliptic integral solutions (exact GR)
3. Relativistic radiative transfer
4. Proper handling of coordinate systems

This is significantly more accurate than simple UV distortion or lens effects.

## Next Steps for Implementation

1. **Extract the core math** from DeflectionAngle.shadersubgraph
2. **Implement disc intersection** logic
3. **Port the physical effects** from Disk Color/Temperature shaders
4. **Test with simple disc texture** first
5. **Add complexity** (rotation, clumps, etc.) incrementally
6. **Optimize** for real-time performance

The key insight is that gravitational lensing for nearby objects (like accretion discs) requires **ray tracing** rather than simple image distortion, because you need to know where each ray actually intersects the physical disc geometry after being deflected by spacetime curvature.
