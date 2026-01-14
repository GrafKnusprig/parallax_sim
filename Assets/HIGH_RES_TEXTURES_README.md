# High-Resolution Planet Textures Integration

This system automatically applies high-resolution textures, normal maps, bump maps, and atmospheric layers to planetary bodies in the solar system simulation.

## Features

### 1. **High-Resolution Base Textures**
The following planets now use 8K/4K/2K textures from `Assets/HighResTextures/`:

- **Sun**: 8k_sun.jpg
- **Mercury**: 8k_mercury.jpg
- **Venus**: 8k_venus_surface.jpg
- **Earth**: 8k_earth_daymap.jpg
- **Moon**: 8k_moon.jpg
- **Mars**: 8k_mars.jpg
- **Jupiter**: 8k_jupiter.jpg
- **Saturn**: 8k_saturn.jpg
- **Uranus**: 2k_uranus.jpg
- **Neptune**: 2k_neptune.jpg
- **Pluto**: plutomap2k.jpg

### 2. **Jupiter Moons**
Located in `Assets/HighResTextures/Jupiter_Moons/`:
- Io, Europa, Ganymede, Callisto, Amalthea, Elara, Himalia, Pasiphae, Thebe

### 3. **Saturn Moons**
Located in `Assets/HighResTextures/Saturn_Moons/`:
- Mimas, Enceladus, Tethys, Dione, Rhea, Titan (surface & clouds), Iapetus

### 4. **Normal & Bump Maps**
- **Earth**: 8k_earth_normal_map.tif (for realistic terrain relief)
- **Pluto**: plutobump2k.jpg (bump map for surface detail)

### 5. **Specular Maps**
- **Earth**: 8k_earth_specular_map.tif (for realistic water reflections)

### 6. **Atmosphere & Cloud Layers**
Transparent layers rendered separately for:
- **Earth**: 8k_earth_clouds.jpg
- **Venus**: 4k_venus_atmosphere.jpg
- **Titan**: TitanClouds.jpg

### 7. **Night Lights (Emissive)**
- **Earth**: 8k_earth_nightmap.jpg (city lights on night side)

## Components

### PlanetTextureManager.cs
Main manager script that:
- Loads high-res textures for each planetary body based on NAIF ID
- Applies normal maps and bump maps correctly
- Creates transparent atmosphere layers with fresnel glow
- Manages material creation and caching

### EnhancedPlanet.shader
Custom shader supporting:
- Albedo (base color) texture
- Normal mapping with adjustable strength
- Specular/glossiness maps
- Emission maps for night lights
- Full PBR (Physically Based Rendering)

### PlanetAtmosphere.shader
Transparent shader for atmosphere layers featuring:
- Alpha blending for cloud transparency
- Fresnel effect for atmospheric rim glow
- Adjustable opacity
- Layered rendering (renders after planets)

### Materials
- **EnhancedPlanetMaterial.mat**: Template for planet surfaces
- **PlanetAtmosphereMaterial.mat**: Template for atmospheric layers

## Integration

The system is automatically integrated into `SolarSystemParallaxManager`:

1. **Initialization**: On `Start()`, the texture manager loads all available textures
2. **Material Assignment**: When creating planet bodies, the system checks for high-res textures:
   - If available, creates a custom material with all maps applied
   - Falls back to default materials if textures aren't available
3. **Atmosphere Creation**: Automatically creates atmosphere layers for Earth, Venus, and Titan

## Configuration

### In Unity Inspector:
On the `SolarSystemParallaxManager` GameObject, the `PlanetTextureManager` component provides:
- **Enable Atmospheres**: Toggle atmospheric layers on/off
- **Atmosphere Height Multiplier**: Controls how much larger atmosphere spheres are (default: 1.05 = 5% larger)
- **Material Templates**: References to planet and atmosphere material templates

### Supported Texture Formats:
- JPG/JPEG
- PNG
- TIF/TIFF

## Technical Details

### Texture Loading
- **Editor Mode**: Uses `AssetDatabase` to load textures directly from `Assets/HighResTextures/`
- **Build Mode**: Textures should be in a `Resources/HighResTextures/` folder for runtime loading

### Planet ID Mapping
Textures are mapped to planets using NAIF IDs from `stellar_object_names.json`:
```json
{
  "399": "Earth",
  "299": "Venus",
  "606": "Titan",
  ...
}
```

### Atmosphere Rendering
- Rendered as slightly larger spheres (default 5% larger than planet)
- Uses alpha blending for transparency
- Fresnel effect creates atmospheric glow at edges
- Shadow casting disabled to prevent self-shadowing

## Optional Toggle

To enable/disable atmospheres at runtime:
```csharp
textureManager.SetAtmospheresEnabled(true/false);
```

## Performance Notes
- 8K textures are memory intensive; ensure adequate VRAM
- Atmosphere layers add one additional sphere per planet with atmosphere
- Normal maps increase shader complexity slightly
- All materials are cached to avoid recreation

## Future Enhancements
Possible additions:
- Ring systems for other planets (Uranus, Neptune)
- Procedural atmosphere scattering
- Dynamic day/night texture blending for Earth
- Seasonal texture variations
- Animated cloud layers
