#!/usr/bin/env python3
import struct
import os
import math

def haversine_distance(ra1, dec1, ra2, dec2):
    # Convert to radians
    ra1, dec1, ra2, dec2 = map(math.radians, [ra1, dec1, ra2, dec2])
    d_ra = ra2 - ra1
    d_dec = dec2 - dec1
    a = math.sin(d_dec/2)**2 + math.cos(dec1) * math.cos(dec2) * math.sin(d_ra/2)**2
    c = 2 * math.atan2(math.sqrt(a), math.sqrt(1-a))
    return math.degrees(c)

def verify_alignment(bin_path, targets):
    if not os.path.exists(bin_path):
        print(f"Error: {bin_path} not found")
        return

    print(f"Searching for targets in {bin_path}...")
    
    with open(bin_path, 'rb') as f:
        header = f.read(4)
        star_count = struct.unpack('<I', header)[0]
        print(f"Total stars: {star_count:,}")

        matches = {name: [] for name in targets}
        
        for i in range(star_count):
            data = f.read(16)
            if len(data) < 16: break
            
            ra, dec, dist, mag = struct.unpack('<ffff', data)
            
            for name, coords in targets.items():
                target_ra, target_dec = coords
                dist_deg = haversine_distance(ra, dec, target_ra, target_dec)
                if dist_deg < 0.05: # 0.05 degree tolerance
                    matches[name].append((ra, dec, dist, mag, dist_deg))

    for name, m_list in matches.items():
        print(f"\nMatches for {name} ({targets[name]}):")
        if not m_list:
            print("  No matches found")
            continue
        
        # Sort by proximity
        m_list.sort(key=lambda x: x[4])
        for ra, dec, dist, mag, d_deg in m_list[:5]:
            print(f"  RA: {ra:8.4f}, Dec: {dec:8.4f}, Dist: {dist:8.4f} pc, Mag: {mag:6.2f}, Sep: {d_deg:8.6f} deg")

if __name__ == "__main__":
    targets = {
        "Alpha Centauri A": (219.90085, -60.83562),
        "Alpha Centauri B": (219.89623, -60.83481),
        "Proxima Centauri": (217.42890, -62.67940)
    }
    
    bin_file = "/Users/philippraven/Documents/git/ws2526/parallax_sim/Assets/StreamingAssets/GDR3/gaia3_10M.bin"
    verify_alignment(bin_file, targets)
