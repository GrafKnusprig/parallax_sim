import csv
import math
import struct
import json
import os
import sys

# Configuration
INPUT_CSV_PATH = "../Assets/StreamingAssets/PlanetDatasetPlus/solar_dataset_plus.csv"
OUTPUT_BYTES_PATH = "../Assets/StreamingAssets/PlanetDatasetPlus/shadow_vectors.bytes"
OUTPUT_MANIFEST_PATH = "../Assets/StreamingAssets/PlanetDatasetPlus/shadow_vectors_manifest.json"

# Constants
NAIF_ID_SUN = 10
NAIF_ID_BLACK_HOLE = 9000000000

def parse_float(value, default=0.0):
    try:
        return float(value)
    except (ValueError, TypeError):
        return default

def calculate_unity_direction(ra_deg, dec_deg):
    """
    Converts RA/Dec to Unity Cartesian Unit Vector (Result is Normalized).
    Math mimics SolarSystemParallaxManager.cs:
    x = cos(dec) * cos(ra)
    y = sin(dec)
    z = cos(dec) * sin(ra)
    """
    ra_rad = math.radians(ra_deg)
    dec_rad = math.radians(dec_deg)
    
    cos_dec = math.cos(dec_rad)
    
    x = cos_dec * math.cos(ra_rad)
    y = math.sin(dec_rad)
    z = cos_dec * math.sin(ra_rad)
    
    # Normalize (should already be unit, but floating point precision...)
    length = math.sqrt(x*x + y*y + z*z)
    if length > 1e-6:
        return x/length, y/length, z/length
    else:
        return 0.0, 0.0, 0.0

def main():
    # Resolve absolute paths
    script_dir = os.path.dirname(os.path.abspath(__file__))
    input_path = os.path.join(script_dir, INPUT_CSV_PATH)
    output_bytes_path = os.path.join(script_dir, OUTPUT_BYTES_PATH)
    output_manifest_path = os.path.join(script_dir, OUTPUT_MANIFEST_PATH)
    
    print(f"Reading from: {input_path}")
    
    if not os.path.exists(input_path):
        print(f"Error: Input file not found: {input_path}")
        return

    vectors = []
    manifest = []
    
    row_count = 0
    
    with open(input_path, 'r', encoding='utf-8') as f:
        # Check if header exists
        # The file provided in context has a header on line 1
        reader = csv.reader(f)
        header = next(reader, None) # Skip header
        
        for row in reader:
            if not row: continue # Skip empty lines
            
            # Match C# filtering: parts.Length < 17 check
            # but standard csv reader handles splits.
            # We just verify we have enough columns for ID(0), Type(1), RA(2), Dec(3)
            # Row 19 (H) is index 19.
            if len(row) < 4: 
                continue

            try:
                naif_id = int(row[0])
                obj_type = row[1]
                ra_str = row[2]
                dec_str = row[3]
                
                # Default vector (0,0,0,0) - used for Sun, Black Hole, or invalid
                vector_entry = (0.0, 0.0, 0.0, 0.0) 
                
                is_valid_target = True
                
                # Logic: "calculate direction vector from the sun to every planet and every moon"
                # "ignore black hole"
                # To maintain 1:1 index mapping with the loaded CSV in Unity, we MUST include an entry 
                # for every row, even if it's ignored (just set to zero).
                
                if naif_id == NAIF_ID_SUN:
                    is_valid_target = False
                elif naif_id == NAIF_ID_BLACK_HOLE:
                    is_valid_target = False
                    
                if is_valid_target:
                    # Parse position
                    try:
                        ra = float(ra_str)
                        dec = float(dec_str)
                        
                        ux, uy, uz = calculate_unity_direction(ra, dec)
                        vector_entry = (ux, uy, uz, 1.0) # Alpha 1.0 indicates valid
                        
                    except ValueError:
                        print(f"Warning: Invalid RA/Dec for ID {naif_id}: {ra_str}, {dec_str}")
                        is_valid_target = False
                
                vectors.append(vector_entry)
                manifest.append({
                    "index": row_count,
                    "naifId": naif_id,
                    "name": obj_type, # Or lookup name
                    "valid": is_valid_target,
                    "vector": vector_entry
                })
                
                row_count += 1
                
            except ValueError:
                continue

    # Write Binary File
    print(f"Writing {len(vectors)} vectors to {output_bytes_path}...")
    with open(output_bytes_path, 'wb') as f:
        for v in vectors:
            # Pack 4 floats (RGBA Float32 in Unity)
            # 'f' is standard 32-bit float
            f.write(struct.pack('ffff', v[0], v[1], v[2], v[3]))
            
    # Write Manifest
    print(f"Writing manifest to {output_manifest_path}...")
    with open(output_manifest_path, 'w') as f:
        json.dump(manifest, f, indent=2)
        
    print("Done.")

if __name__ == "__main__":
    main()
