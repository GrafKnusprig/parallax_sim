# Planet Texture Loading System - Documentation

## Overview
The dynamic planet texture loading mechanism has been completely reworked to support both Unity Editor and runtime builds with proper async loading capabilities.

## Key Improvements

### 1. **Unified Texture Loading**
- Works in both Unity Editor (using AssetDatabase) and runtime builds (using Resources)
- Automatic detection of file extensions (.jpg, .png, .tif, .tiff)
- Proper error handling and fallback mechanisms

### 2. **Async Loading Support**
- Optional asynchronous texture loading to prevent freezing during initialization
- Configurable loading delay between textures
- Progress tracking and loading status per planet

### 3. **Build Support**
- Automatic build preprocessing to copy textures to Resources folder
- Manual tools for texture management
- Proper Resources folder structure for runtime loading

### 4. **Better Material Management**
- Centralized texture database with all texture types
- Robust material creation with fallback options
- Proper shader validation and emergency material creation

### 5. **Enhanced Debugging**
- Verbose logging option for detailed texture loading information
- Loading status tracking (isLoading, loadingFailed)
- Texture count statistics

## File Structure

### Required Folders
```
Assets/
├── HighResTextures/          # PRIMARY texture source (ALWAYS USED)
│   ├── 8k_earth_daymap.jpg
│   ├── 8k_earth_clouds.jpg
│   ├── Jupiter_Moons/
│   ├── Saturn_Moons/
│   └── ...
└── Resources/                 # Runtime textures (Builds only)
    └── HighResTextures/       # Auto-copied during build
        └── ... (same structure)
```

**IMPORTANT:** The system ALWAYS loads from `Assets/HighResTextures/` in the Editor. The old materials in `planet_materials.json` (which reference "Planets of the Solar System 3D") are only used as fallbacks if HighResTextures are not available.

## Setup Instructions

### In Unity Editor

1. **Assign Material Templates**
   - Select the GameObject with `PlanetTextureManager`
   - In Inspector, assign:
     - `Planet Material Template` (required)
     - `Atmosphere Material Template` (optional)
     - `Sun Material Template` (optional)

2. **Configure Settings**
   - `Use Async Loading`: Enable for smoother loading (recommended)
   - `Loading Delay`: Time between texture loads (0.05s default)
   - `Textures Path`: Path relative to Assets/ or Resources/ ("HighResTextures" default)
   - `Enable Atmospheres`: Toggle atmosphere layer creation
   - `Verbose Logging`: Enable for detailed debug information

3. **Texture Organization**
   - Place all textures in `Assets/HighResTextures/`
   - Maintain folder structure (e.g., `Jupiter_Moons/`, `Saturn_Moons/`)
   - Supported formats: JPG, PNG, TIF, TIFF

### For Builds

1. **Automatic (Recommended)**
   - Textures are automatically copied to Resources folder during build
   - No manual intervention needed

2. **Manual Copy**
   - Menu: `Tools > Planet Textures > Copy to Resources Folder`
   - Use this if you want to test Resources loading in Editor

3. **Clean Resources**
   - Menu: `Tools > Planet Textures > Clean Resources Folder`
   - Removes copied textures from Resources folder

## API Reference

### PlanetTextureManager Methods

#### `void Initialize()`
Initializes the texture manager and starts loading textures. Called automatically by SolarSystemParallaxManager.

#### `Material GetOrCreatePlanetMaterial(long naifId, Material fallbackMaterial = null)`
Gets or creates a material for a specific planet with textures applied.
- Returns cached material if already created
- Returns fallback if textures not available or still loading
- Creates emergency material if all else fails

#### `GameObject CreateAtmosphereLayer(long naifId, GameObject planetObject)`
Creates an atmosphere layer for planets that have atmosphere textures.
- Returns null if atmospheres disabled or no texture available
- Creates sphere 5% larger than planet by default

#### `bool HasHighResTextures(long naifId)`
Checks if high-resolution textures are available and loaded for a planet.

#### `bool IsLoading(long naifId)`
Checks if textures are currently being loaded for a planet.

#### `PlanetTextureSet GetTextureSet(long naifId)`
Gets the complete texture set for a planet (base, normal, specular, etc.).

#### `int GetLoadedTextureCount()`
Returns the number of successfully loaded texture sets.

#### `void SetAtmospheresEnabled(bool enabled)`
Toggles atmosphere layer creation on/off.

#### `void ReloadAllTextures()`
Forces a complete reload of all textures (useful for debugging).

## Texture Database

The system uses a centralized texture database that maps NAIF IDs to texture files:

```csharp
private static readonly Dictionary<long, TextureInfo> textureDatabase = new Dictionary<long, TextureInfo>
{
    { 399, new TextureInfo("8k_earth_daymap", 
                           normalFile: "8k_earth_normal_map", 
                           specularFile: "8k_earth_specular_map", 
                           atmosphereFile: "8k_earth_clouds", 
                           nightLightsFile: "8k_earth_nightmap") },
    // ... more planets
};
```

### Supported Texture Types
- **Base Texture**: Main color/albedo map (required)
- **Normal Map**: Surface detail normals (optional)
- **Bump Map**: Height/displacement map (optional)
- **Specular Map**: Reflectivity/glossiness (optional)
- **Atmosphere Texture**: Cloud/atmosphere layer (optional)
- **Night Lights**: Emissive city lights (optional)

## Adding New Planets

To add textures for a new planet:

1. Place texture files in `Assets/HighResTextures/`
2. Add entry to `textureDatabase` in PlanetTextureManager.cs:

```csharp
{ NAIF_ID, new TextureInfo("texture_name", 
                           normalFile: "normal_map_name",  // optional
                           atmosphereFile: "clouds_name") },  // optional
```

3. Textures will be automatically loaded on next initialization

## Troubleshooting

### Textures not loading in Editor
- Check that textures are in `Assets/HighResTextures/`
- Enable `Verbose Logging` to see detailed loading information
- Verify file extensions are supported (.jpg, .png, .tif, .tiff)

### Textures not loading in Build
- Run `Tools > Planet Textures > Copy to Resources Folder` before building
- Or build normally (auto-copy is enabled)
- Check Build console for preprocessing messages

### Materials appear magenta/pink
- Verify `Planet Material Template` is assigned in Inspector
- Check that template material has a valid shader
- Look for "NULL shader" errors in console

### Performance issues
- Enable async loading: `Use Async Loading = true`
- Increase `Loading Delay` for slower loading but smoother startup
- Consider reducing texture resolution for mobile/VR

### Memory issues
- Large textures (8K) consume significant memory
- Consider compression settings in texture import settings
- Use lower resolution textures for less powerful platforms

## Performance Considerations

### Async vs Sync Loading
- **Async (recommended)**: Spreads loading over multiple frames, prevents freezing
- **Sync**: Loads all textures immediately, may cause startup freeze with many/large textures

### Memory Usage
Approximate memory usage per texture:
- 8K texture (8192×4096): ~128-256 MB uncompressed
- 4K texture (4096×2048): ~32-64 MB uncompressed
- 2K texture (2048×1024): ~8-16 MB uncompressed

Use texture compression in Unity import settings to reduce memory usage.

## Version History

### v2.0 (Current)
- Complete rewrite of texture loading mechanism
- Added async loading support
- Unified Editor and Runtime loading
- Improved error handling and fallbacks
- Added build preprocessing for Resources folder
- Enhanced debugging and logging

### v1.0 (Previous)
- Basic texture loading from Assets folder
- Editor-only support
- No async loading
- Limited error handling
