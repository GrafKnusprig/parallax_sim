#!/usr/bin/env python3
import struct
import os

def scan_nearby(bin_path, max_dist_pc=100.0):
    if not os.path.exists(bin_path):
        print(f"Error: {bin_path} not found")
        return

    print(f"Scanning for stars closer than {max_dist_pc} pc in {bin_path}...")
    
    with open(bin_path, 'rb') as f:
        header = f.read(4)
        star_count = struct.unpack('<I', header)[0]
        
        found = 0
        min_dist = 1e9
        
        for i in range(star_count):
            data = f.read(16)
            if len(data) < 16: break
            
            ra, dec, dist, mag = struct.unpack('<ffff', data)
            
            if dist < min_dist:
                min_dist = dist
                
            if dist < max_dist_pc:
                found += 1
                if found <= 20:
                    print(f"  Idx {i:8}: RA={ra:8.4f}, Dec={dec:8.4f}, Dist={dist:8.4f}, Mag={mag:6.2f}")

    print(f"\nFound {found} stars closer than {max_dist_pc} pc.")
    print(f"Minimum distance found: {min_dist:8.4f} pc")

if __name__ == "__main__":
    bin_file = "/Users/philippraven/Documents/git/ws2526/parallax_sim/Assets/StreamingAssets/GDR3/gaia3_10M.bin"
    scan_nearby(bin_file, 50.0)
