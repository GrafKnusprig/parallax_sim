# Gaia GDR3 Dataset for Unity - Usage Guide

This dataset contains approximately 10 million stars from the **Gaia Data Release 3 (GDR3)**, specifically filtered to represent the **Milky Way Galactic Disk**. It is optimized for high-performance rendering in Unity using a custom binary format.

## Dataset Overview

- **Star Count**: ~10,000,000
- **Primary Importance**: Absolute Magnitude (Brightest stars priority)
- **Spatial Filtering**:
  - Galactocentric Radius < 16,000 pc
  - Galactic Plane Height |Z| < 2,000 pc
- **Coordinate System**: ICRS (RA/Dec) + Galactic Cartesian Coordinates

## File Structure

The dataset consists of two optimized binary files:

1.  **`gaia_stars.bin`**: Contains the physical data for each star.
    - **Header**: `uint32` (Count of stars)
    - **Records**: Sequential records of 4x `float32` (Little-Endian)
        - `RA` (Right Ascension in degrees)
        - `Dec` (Declination in degrees)
        - `Distance` (Distance from Sun in parsecs)
        - `AbsMag` (Absolute Magnitude)

2.  **`gaia_stars_ids.bin`**: (Optional) Mapping to Gaia Source IDs for metadata lookups.
    - **Header**: `uint32` (Count of stars)
    - **Records**: Sequential records of `uint64` (Gaia Source ID)

## Unity Setup

### 1. File Placement
Move the `.bin` files into your Unity project's `StreamingAssets` folder:
`Assets/StreamingAssets/gaia_stars.bin`

### 2. Implementation logic (C#)

To load the data efficiently, use a `BinaryReader`. Below is a simplified example of how to parse the `gaia_stars.bin` file:

```csharp
using UnityEngine;
using System.IO;

public class GaiaDatasetLoader : MonoBehaviour
{
    public void LoadGaiaData(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        
        using (BinaryReader reader = new BinaryReader(File.Open(path, FileMode.Open)))
        {
            uint totalStars = reader.ReadUInt32();
            Debug.Log($"Loading {totalStars} stars...");

            for (uint i = 0; i < totalStars; i++)
            {
                float ra = reader.ReadSingle();
                float dec = reader.ReadSingle();
                float dist = reader.ReadSingle();
                float mag = reader.ReadSingle();

                // Convert RA/Dec to Cartesian direction
                float raRad = ra * Mathf.Deg2Rad;
                float decRad = dec * Mathf.Deg2Rad;
                
                Vector3 direction = new Vector3(
                    Mathf.Cos(decRad) * Mathf.Cos(raRad),
                    Mathf.Sin(decRad),
                    Mathf.Cos(decRad) * Mathf.Sin(raRad)
                ).normalized;

                Vector3 posParsecs = direction * dist;
                
                // Use the data for rendering...
            }
        }
    }
}
```

### 3. Performance Tips

- **GPU Instancing**: With 10M stars, use `Graphics.DrawMeshInstancedIndirect` and a Compute Shader for culling and position calculation.
- **Floating Origin**: When moving large distances, use a floating origin system to avoid precision jitter.
- **Culling**: Implement FOV and distance-based culling to only process stars relevant to the camera.

## Credits

Data provided by the **European Space Agency (ESA) Gaia Mission**. 
Processed for Unity using custom galactocentric filtering.
