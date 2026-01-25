#!/usr/bin/env python3
import struct
import os
import sys
import math

def verify_binary(file_path):
    print(f"Verifying {file_path}...")
    
    if not os.path.exists(file_path):
        print(f"Error: File not found: {file_path}")
        return

    file_size = os.path.getsize(file_path)
    print(f"File Size: {file_size:,} bytes")

    with open(file_path, 'rb') as f:
        # Read Header
        header_bytes = f.read(4)
        if len(header_bytes) < 4:
            print("Error: File too small to contain header.")
            return

        star_count = struct.unpack('<I', header_bytes)[0]
        print(f"Header Star Count: {star_count:,}")

        expected_size = 4 + (star_count * 16)
        if file_size != expected_size:
            print(f"ERROR: Size mismatch!")
            print(f"  Expected: {expected_size:,} bytes (4 + {star_count:,} * 16)")
            print(f"  Actual:   {file_size:,} bytes")
            diff = file_size - expected_size
            print(f"  Difference: {diff:,} bytes ({diff/16:.2f} records)")
        else:
            print("Size check: OK")

        # Read Records
        print("Scanning records for corruption...")
        
        # We can read in chunks for speed
        chunk_size = 10000
        struct_fmt = '<ffff' # RA, Dec, Dist, Mag
        record_size = 16
        
        nans_found = 0
        infs_found = 0
        bad_values = 0
        
        try:
            for i in range(star_count):
                data = f.read(record_size)
                if len(data) < record_size:
                    print(f"Error: Unexpected EOF at record {i}")
                    break
                
                ra, dec, dist, mag = struct.unpack(struct_fmt, data)
                
                # Check for NaN/Inf
                if math.isnan(ra) or math.isnan(dec) or math.isnan(dist) or math.isnan(mag):
                    nans_found += 1
                    if nans_found <= 5:
                        print(f"  [NaN Found] Idx {i}: RA={ra}, Dec={dec}, Dist={dist}, Mag={mag}")
                
                if math.isinf(ra) or math.isinf(dec) or math.isinf(dist) or math.isinf(mag):
                    infs_found += 1
                    if infs_found <= 5:
                        print(f"  [Inf Found] Idx {i}: RA={ra}, Dec={dec}, Dist={dist}, Mag={mag}")

                # Value sanity checks
                if not (0 <= ra <= 360) and not math.isnan(ra):
                    if bad_values < 5: print(f"  [Bad RA] Idx {i}: {ra}")
                    bad_values += 1
                if not (-90 <= dec <= 90) and not math.isnan(dec):
                    if bad_values < 5: print(f"  [Bad Dec] Idx {i}: {dec}")
                    bad_values += 1
                if dist <= 0 and not math.isnan(dist):
                    if bad_values < 5: print(f"  [Bad Dist] Idx {i}: {dist}")
                    bad_values += 1

                if i % 1000000 == 0 and i > 0:
                    print(f"  Scanned {i:,} records...")

        except Exception as e:
            print(f"Exception while reading: {e}")

    print("-" * 30)
    print("Verification Complete.")
    print(f"NaNs: {nans_found}")
    print(f"Infs: {infs_found}")
    print(f"Bad Values: {bad_values}")
    
    if nans_found == 0 and infs_found == 0 and bad_values == 0 and file_size == expected_size:
        print("SUCCESS: File appears valid.")
    else:
        print("FAILURE: File corrupt or invalid.")

if __name__ == "__main__":
    target_file = "../Assets/StreamingAssets/GDR3/gaia3_10M.bin"
    if len(sys.argv) > 1:
        target_file = sys.argv[1]
    
    verify_binary(target_file)
