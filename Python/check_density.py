#!/usr/bin/env python3
import struct
import os
import math

def haversine_distance(ra1, dec1, ra2, dec2):
    ra1, dec1, ra2, dec2 = map(math.radians, [ra1, dec1, ra2, dec2])
    d_ra = ra2 - ra1
    d_dec = dec2 - dec1
    a = math.sin(d_dec/2)**2 + math.cos(dec1) * math.cos(dec2) * math.sin(d_ra/2)**2
    c = 2 * math.atan2(math.sqrt(a), math.sqrt(1-a))
    return math.degrees(c)

def check_density(bin_path, target_ra, target_dec, radius_deg=5.0):
    if not os.path.exists(bin_path):
        print(f"Error: {bin_path} not found")
        return

    print(f"Checking star density near RA={target_ra}, Dec={target_dec} within {radius_deg} deg...")
    
    with open(bin_path, 'rb') as f:
        header = f.read(4)
        star_count = struct.unpack('<I', header)[0]
        
        count = 0
        sum_mag = 0
        min_dist = 1e9
        
        for i in range(star_count):
            data = f.read(16)
            if len(data) < 16: break
            
            ra, dec, dist, mag = struct.unpack('<ffff', data)
            
            dist_deg = haversine_distance(ra, dec, target_ra, target_dec)
            if dist_deg < radius_deg:
                count += 1
                sum_mag += mag
                if dist < min_dist:
                    min_dist = dist

    print(f"Found {count} stars.")
    if count > 0:
        print(f"Average magnitude: {sum_mag/count:.2f}")
        print(f"Minimum distance: {min_dist:.2f} pc")
    
    # Check a random "empty" spot for comparison
    print(f"\nChecking star density near RA=0, Dec=0 (for comparison)...")
    f.seek(4)
    count_ref = 0
    for i in range(star_count):
        data = f.read(16)
        if len(data) < 16: break
        ra, dec, dist, mag = struct.unpack('<ffff', data)
        if haversine_distance(ra, dec, 0, 0) < radius_deg:
            count_ref += 1
    print(f"Found {count_ref} stars.")

if __name__ == "__main__":
    bin_file = "/Users/philippraven/Documents/git/ws2526/parallax_sim/Assets/StreamingAssets/GDR3/gaia3_10M.bin"
    # Sagittarius A*
    check_density(bin_file, 266.41683, -29.00781, radius_deg=2.0)
