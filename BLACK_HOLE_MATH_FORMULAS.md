# Black Hole Gravitational Lensing - Mathematical Formulas

## Coordinate Systems and Conventions

**Geometric Units**: G = c = 1
- Mass M is in units of length (M☉ ≈ 1.5 km)
- Time is in units of length
- Schwarzschild radius: r_s = 2M

**Schwarzschild Metric** (non-rotating black hole):
```
ds² = -(1 - 2M/r)dt² + (1 - 2M/r)⁻¹dr² + r²(dθ² + sin²θ dφ²)
```

## Photon Trajectories

### Impact Parameter

The impact parameter `b` is the conserved quantity related to angular momentum:

```
b = L/E = r sin(θ) / √(1 - 2M/r)
```

Where:
- L = angular momentum of photon
- E = energy of photon
- r = radial coordinate
- θ = angle from black hole-observer axis

At infinity (r → ∞):
```
b∞ = lim(r→∞) b = perpendicular distance to asymptotic trajectory
```

### Critical Impact Parameters

1. **Photon Sphere**: b_ph = 3√3 M ≈ 5.196 M
   - Unstable circular orbit at r = 3M
   - Photons can orbit indefinitely

2. **Event Horizon**: r_s = 2M
   - Any photon with r < 2M cannot escape

3. **Capture Cross-Section**: b_capture ≈ 5.2 M
   - Photons with b < b_capture eventually fall in

## Deflection Angle

### Exact Formula (Elliptic Integrals)

The deflection angle for a photon passing at closest approach r_0:

```
Δφ = 2∫[r_0 to ∞] dr / (r² √(1/b² - (1 - 2M/r)/r²)) - π
```

This integral requires elliptic functions for exact evaluation.

### Closest Approach Radius

Given impact parameter b, find r_0 (closest approach) by solving:

```
b² = r_0² / (1 - 2M/r_0)
```

Or equivalently:
```
r_0³ - b²r_0 + 2Mb² = 0
```

### Weak Field Approximation

For b >> 2M (far from black hole):

```
Δφ ≈ 4M/b
```

This is the same as Newtonian deflection doubled (Einstein's prediction).

### Strong Field Expansion

Near the photon sphere, use expansion in terms of:
```
b̄ = b/(3√3 M) - 1
```

For small b̄:
```
Δφ ≈ -ā log(b̄) + b̄ + O(b̄²)
```

Where ā and b̄ are numerical coefficients (ā ≈ 1.0, b̄ ≈ -0.4)

## Elliptic Integral Implementation

### Change of Variables

Let u = 1/r, then:
```
Δφ = ∫[0 to u_0] du / √(P(u))
```

Where:
```
P(u) = 1/b² - u²(1 - 2Mu)
```

### Standard Form

Convert to elliptic integrals using substitution:
```
u = u_0 sin²(ψ/2)
```

Result involves elliptic integral of the first kind K(k) and third kind Π(n, k).

### Numerical Evaluation

For real-time rendering, options:
1. **Lookup Table**: Pre-compute Δφ(b, M) and interpolate
2. **Analytical Approximation**: Piecewise functions for different b ranges
3. **Series Expansion**: Different expansions for weak/strong field
4. **GPU-Friendly Functions**: Approximate elliptic integrals with polynomials

## Ray Tracing Equations

### Initial Conditions

Observer at position r_obs with viewing angle θ_view:

```
b = r_obs sin(θ_view)
φ_0 = 0  (set observer at φ = 0)
```

### Ray Path in Equatorial Plane

For rays in the equatorial plane (θ = π/2):

```
φ(r) = φ_0 + ∫[r to r_obs] b dr / (r² √(r² - b²(1 - 2M/r)))
```

### Multiple Images

Light can wrap around the black hole n times:
```
Total deflection = Δφ_primary + 2πn
```

For each integer n ≥ 0, there's a potential image.

## Accretion Disc Physics

### Keplerian Velocity

Orbital velocity at radius r:
```
v(r) = √(M/r)  (in geometric units)
v(r) = √(GM/r) (SI units)
```

At ISCO (r = 6M):
```
v_ISCO = 1/√6 c ≈ 0.408c
```

### Temperature Profile

**Shakura-Sunyaev Thin Disc**:
```
T(r) = T_0 (r/r_in)^(-3/4) [1 - √(r_in/r)]^(1/4)
```

Where:
- T_0 = maximum temperature (at inner edge)
- r_in = inner disc radius (typically ISCO)

**Maximum Temperature**:
```
T_max ≈ 6×10⁷ K (M/M☉)^(-1/4) (Ṁ/M☉ year⁻¹)^(1/4)
```

For stellar mass black hole (M ≈ 10 M☉) with moderate accretion:
- T_max ≈ 10⁶ - 10⁷ K (X-ray temperatures)

### Surface Brightness

```
I(r) ∝ (r/r_in)^(-3) [1 - √(r_in/r)]
```

## Relativistic Effects

### Doppler Factor

For material with velocity v at angle α to line of sight:
```
D = 1/(γ(1 - β cos(α)))
```

Where:
- β = v/c
- γ = 1/√(1 - β²) (Lorentz factor)

Observed frequency:
```
ν_obs = D × ν_emit
```

### Relativistic Beaming

Intensity is boosted by:
```
I_obs = D³ × I_emit
```

The D³ factor comes from:
- D¹ from frequency shift (energy)
- D¹ from time dilation (photon rate)
- D¹ from aberration (solid angle)

### Gravitational Redshift

Photon climbing from radius r to infinity:
```
g(r) = √(1 - 2M/r)
```

Observed frequency:
```
ν_obs = g(r) × ν_emit
```

Combined with Doppler:
```
ν_obs = g(r) × D × ν_emit
```

### Total Shift Factor

```
F(r, φ) = g(r) × D(r, φ)
```

Observed intensity:
```
I_obs = F⁴ × I_emit
```

(F⁴ because intensity ∝ ν³ × photon rate)

## Blackbody Radiation

### Planck's Law

Spectral radiance at temperature T:
```
B_ν(T) = (2hν³/c²) × 1/(exp(hν/kT) - 1)
```

### Wien's Displacement Law

Peak wavelength:
```
λ_max = b/T
```

Where b = 2.898×10⁻³ m·K

For T = 10⁶ K:
- λ_max ≈ 3 nm (soft X-rays)

### Color Temperature Approximation

For visible rendering:
- T < 3000K: Red
- T ≈ 5000K: White (like Sun)
- T > 10000K: Blue-white

Approximate RGB conversion:
```
R = clamp(T/5000, 0.5, 1.0)
G = clamp(T/5500, 0.7, 1.0)  
B = clamp(T/6000, 0.8, 1.0)
```

(Very simplified; real conversion requires color matching functions)

## Disc Geometry

### Inner Edge (ISCO)

**Schwarzschild (non-rotating)**:
```
r_ISCO = 6M
```

**Kerr (maximally rotating, co-rotating)**:
```
r_ISCO = M
```

**Kerr (maximally rotating, counter-rotating)**:
```
r_ISCO = 9M
```

### Disc Height

For thin disc approximation:
```
H(r)/r ≈ 0.01 - 0.1
```

(Depends on accretion rate and viscosity)

### Outer Edge

No strict limit; observationally ~100-1000 M depending on system.

## Light Bending Functions

### Lens Equation

Relates source position β to image position θ:
```
β = θ - α(θ)
```

Where α(θ) is deflection angle.

For black hole:
```
α(θ) ≈ 4M/D sin(θ)
```

(D = observer-lens distance)

### Einstein Radius

Characteristic angular scale:
```
θ_E = √(4M D_LS/(D_L D_S))
```

Where:
- D_L = observer-lens distance
- D_S = observer-source distance  
- D_LS = lens-source distance

For accretion disc (D_LS ≈ 0):
- Not directly applicable
- Instead use impact parameter b

## Numerical Methods

### Ray Integration

To integrate photon path:

```
d²r/dφ² + r - 3Mr² = (1/b²)(r⁴ - 2Mr³)
```

Can use Runge-Kutta or similar.

### Root Finding

To find r_0 from b, solve:
```
f(r) = r³ - b²r + 2Mb² = 0
```

Use Newton-Raphson:
```
r_{n+1} = r_n - f(r_n)/f'(r_n)
```

### Interpolation

Pre-compute deflection function on grid:
```
Δφ_table[i][j] for b_i, M_j
```

Use bilinear interpolation for intermediate values.

## Practical Shader Constants

In geometric units (G = c = 1):
```
Schwarzschild radius: r_s = 2M = 2.0
Photon sphere: r_ph = 3M = 3.0
ISCO: r_ISCO = 6M = 6.0
Critical impact parameter: b_c = 3√3 M ≈ 5.196
Capture cross-section: σ ≈ 27πM² ≈ 84.8M²
```

## Example Values

For M = 10 M☉ black hole:
- Schwarzschild radius: 30 km
- Photon sphere: 45 km
- ISCO: 90 km
- Light crossing time: 0.1 ms

For M = 10⁶ M☉ supermassive black hole:
- Schwarzschild radius: 3 × 10⁹ m ≈ 0.02 AU
- Photon sphere: 4.5 × 10⁹ m  
- ISCO: 9 × 10⁹ m
- Light crossing time: 10 seconds

## Computational Complexity

Operations per pixel:
1. Impact parameter: O(1)
2. Deflection angle: O(10-100) [elliptic integral or table lookup]
3. Ray-disc intersection: O(1)
4. Disc properties: O(1)
5. Relativistic factors: O(10) [exp, sqrt, trig]

Total: ~100-1000 operations per pixel (feasible for real-time)

## Optimization Strategies

1. **Symmetry**: Use φ-symmetry to reduce computation
2. **Lookup Tables**: Pre-compute deflection angles
3. **LOD**: Use simpler formulas for distant rays
4. **Early Termination**: Skip rays that obviously miss disc
5. **Caching**: Reuse calculations across frames
6. **GPU Parallelism**: Each pixel independent

## Further Reading

- Bardeen & Cunningham (1973) - "The Optical Appearance of a Star Orbiting an Extreme Kerr Black Hole"
- Luminet (1979) - "Image of a spherical black hole with thin accretion disk"  
- Gralla & Lupsasca (2020) - "Lensing by Kerr Black Holes"
- Vincent et al. (2011) - "Imaging a Boson Star at the Galactic Center"

## Useful Approximations

**Small angle** (θ << 1):
```
sin(θ) ≈ θ
cos(θ) ≈ 1 - θ²/2
```

**Thin disc** (H << r):
- Ignore vertical structure
- Treat as 2D in equatorial plane

**Weak field** (r >> 2M):
```
g(r) ≈ 1 - M/r
Δφ ≈ 4M/b
```

**Strong field** (near photon sphere):
- Must use exact elliptic integrals
- Or pre-computed tables
- Multiple images become important

This should give you all the mathematical tools needed to implement physically accurate gravitational lensing for your accretion disc!
