#!/usr/bin/env python3
"""
Gaia GDR3 Metadata Loader

Fetches astrophysical parameters for stars that were already selected by `gaia3_dataset_loader.py`.
Matches stars by `source_id` read from `*_ids.bin`.

Data Source:
    https://cdn.gea.esac.esa.int/Gaia/gdr3/Astrophysical_parameters/astrophysical_parameters/

Output Format (Binary):
    Header: uint32 count
    Records:
        - teff (float32, K)
        - luminosity (float32, L_sun)
        - radius (float32, R_sun)
        - a_g (float32, mag)
        - ebp_min_rp (float32, mag)
        - flags (uint32, bitmask)

    Missing values are stored as -1.0.
"""

import argparse
import csv
import gzip
import os
import pickle
import re
import signal
import struct
import sys
import tempfile
import time
from typing import Dict, List, Optional, Set, Tuple
from urllib.parse import urljoin

import requests

# --------- CONFIG ---------
BASE_URL = "https://cdn.gea.esac.esa.int/Gaia/gdr3/Astrophysical_parameters/astrophysical_parameters/"
HTTP_TIMEOUT = 120
HTTP_RETRIES = 5
CHUNK_BYTES = 1 << 20

# Checkpointing
CHECKPOINT_INTERVAL = 5

# Global state
_graceful_shutdown = False
_current_metadata = None
_current_output_base = None

# --------- DATA STRUCTURES ---------
# We need to store metadata for each ID.
# Since we need to output in the EXACT SAME ORDER as the input IDs,
# we will store data in a dict {source_id -> MetadataTuple}
# and then write out by iterating the original ID list.
MetadataTuple = Tuple[float, float, float, float, float, int] # teff, lum, rad, ag, color, flags

# --------- HELPERS ---------
def parse_float(x: str, default: float = -1.0) -> float:
    if not x: return default
    try:
        val = float(x)
        if val != val: return default # NaN check
        return val
    except ValueError:
        return default

def parse_int(x: str, default: int = 0) -> int:
    if not x: return default
    try:
        return int(float(x)) # sometimes ints are parsed as floats "1.0"
    except ValueError:
        return default

def load_ids(ids_path: str) -> List[int]:
    """Load ordered source IDs from binary sidecar file."""
    print(f"Loading IDs from {ids_path}...")
    ids = []
    with open(ids_path, 'rb') as f:
        # Header: uint32 count
        data = f.read(4)
        if len(data) < 4: return []
        count = struct.unpack('<I', data)[0]
        
        print(f"  Expecting {count:,} IDs...")
        
        # optimized read?
        # 8 bytes per ID
        # Read all at once typically fine for 10MB file
        chunk = f.read()
        # struct.unpack helper
        # count integers
        # chunk length should be count * 8
        if len(chunk) != count * 8:
            print(f"Warning: File size mismatch. Expected {count*8} bytes, got {len(chunk)}.")
            # Adjust count if needed
            count = len(chunk) // 8
            
        ids = struct.unpack(f'<{count}Q', chunk)
        
    print(f"  Loaded {len(ids):,} IDs.")
    return list(ids)

def http_get_with_retries(session: requests.Session, url: str) -> requests.Response:
    last_err = None
    for attempt in range(1, HTTP_RETRIES + 1):
        try:
            r = session.get(url, stream=True, timeout=HTTP_TIMEOUT)
            r.raise_for_status()
            return r
        except Exception as e:
            last_err = e
            if attempt < HTTP_RETRIES:
                sleep_s = min(20, 2**attempt)
                print(f"    HTTP error (attempt {attempt}/{HTTP_RETRIES}): {e} -> retry in {sleep_s}s")
                time.sleep(sleep_s)
            else:
                raise
    raise RuntimeError(f"Unreachable: {last_err}")

def list_remote_files(session: requests.Session) -> List[str]:
    print(f"Fetching index from {BASE_URL}...")
    r = session.get(BASE_URL, timeout=HTTP_TIMEOUT)
    r.raise_for_status()
    
    names = re.findall(r'href="(AstrophysicalParameters_[^"]+\.csv\.gz)"', r.text)
    names = sorted(set(names))
    
    if not names:
        raise RuntimeError("No AstrophysicalParameters_*.csv.gz files found.")
    return names

def download_to_path(session: requests.Session, url: str, dest_path: str) -> None:
    r = http_get_with_retries(session, url)
    with r:
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=CHUNK_BYTES):
                if chunk:
                    f.write(chunk)

def process_file(gz_path: str, target_ids: Set[int], metadata_store: Dict[int, MetadataTuple]) -> int:
    """
    Process one file, extract data for target IDs.
    Returns number of matches found in this file.
    """
    matches = 0
    with gzip.open(gz_path, "rt", newline="", encoding="utf-8") as f:
        # Skip comment lines
        def skip_comments(file_obj):
            for line in file_obj:
                if not line.startswith('#'):
                    yield line
        
        reader = csv.DictReader(skip_comments(f))
        
        if reader.fieldnames:
            reader.fieldnames = [x.strip() for x in reader.fieldnames]

        # Check required columns availability strictly?
        # Or just use .get() with defaults.
        # We know lum_flame and flags_gspphot might vary.
        
        for row in reader:
            try:
                sid_s = row.get("source_id")
                if not sid_s: continue
                sid = int(sid_s)
                
                if sid in target_ids:
                    # Found a match!
                    # Extract params
                    
                    # Color: teff_gspphot (Temperature). User asked for color intrisic.
                    # Usually color is BP-RP, but teff is also good for blackbody.
                    # User listed "Color (intrinsic) -> teff_gspphot".
                    teff = parse_float(row.get("teff_gspphot"))
                    
                    # Brightness: luminosity_gspphot (User requested), found lum_flame
                    lum = parse_float(row.get("lum_flame"))
                    if lum < 0:
                        # try gspphot if exists?
                        lum = parse_float(row.get("luminosity_gspphot"))
                    
                    # Size: radius_gspphot
                    rad = parse_float(row.get("radius_gspphot"))
                    
                    # Dust: a_g, e_bp_min_rp_gspphot
                    # header has ag_gspphot
                    ag = parse_float(row.get("ag_gspphot"))
                    color_excess = parse_float(row.get("ebpminrp_gspphot"))
                    
                    # Flags: flags_gspphot? missing.
                    # Use flags_flame as proxy for lum quality
                    flags = parse_int(row.get("flags_flame"))
                    if flags == 0:
                        # try flags_gspspec
                        # '0' is valid flag, but if parsed failed it returns 0.
                        # flags are usually strings "00010.." or ints?
                        # In CSV they look like strings usually.
                        # But Gaia archive flags are often string of bits.
                        # Let's try parsing as int directly or skip.
                        pass

                    metadata_store[sid] = (teff, lum, rad, ag, color_excess, flags)
                    matches += 1
                    
            except ValueError:
                continue
                
    return matches

def save_checkpoint(path: str, metadata_store: Dict[int, MetadataTuple], processed_files: List[str]) -> None:
    data = {
        'metadata_store': metadata_store,
        'processed_files': processed_files,
        'version': '1.0'
    }
    with open(path, 'wb') as f:
        pickle.dump(data, f)
    print(f"    Checkpoint saved: {len(metadata_store):,} matches found.")

def load_checkpoint(path: str) -> Optional[Dict]:
    if not os.path.exists(path): return None
    try:
        with open(path, 'rb') as f:
            return pickle.load(f)
    except:
        return None

def write_output(ids_list: List[int], metadata_store: Dict[int, MetadataTuple], out_path: str) -> None:
    print(f"Writing binary output to {out_path}...")
    
    count = len(ids_list)
    found_count = 0
    
    with open(out_path, 'wb') as f:
        # Header
        f.write(struct.pack('<I', count))
        
        # Data
        # Default tuple for missing stars
        # (-1.0, -1.0, -1.0, -1.0, -1.0, 0)
        default_tup = (-1.0, -1.0, -1.0, -1.0, -1.0, 0)
        
        batch_size = 10000
        buffer = bytearray()
        
        for i, sid in enumerate(ids_list):
            val = metadata_store.get(sid, default_tup)
            if val is not default_tup:
                found_count += 1
            
            # Pack: 5 floats, 1 uint
            # <fffffI
            buffer.extend(struct.pack('<fffffI', *val))
            
            if len(buffer) >= batch_size * 24: # 24 bytes per record
                f.write(buffer)
                buffer.clear()
        
        if buffer:
            f.write(buffer)
            
    print(f"Done. Wrote {count:,} records (Found data for {found_count:,} stars).")

def signal_handler(signum, frame):
    global _graceful_shutdown
    print("\n\n=== Halted (Ctrl+C) ===")
    _graceful_shutdown = True
    sys.exit(0)

def main():
    global _graceful_shutdown, _current_metadata, _current_output_base
    
    parser = argparse.ArgumentParser(description="Gaia DR3 Metadata Loader")
    parser.add_argument("input_base", help="Base name of input files (e.g. gaia_v3_10m -> reads gaia_v3_10m_ids.bin)")
    parser.add_argument("--max-files", type=int, default=None, help="Test limit")
    args = parser.parse_args()
    
    input_base = args.input_base.replace('_ids.bin', '').replace('.bin', '')
    ids_path = f"{input_base}_ids.bin"
    out_path = f"{input_base}_metadata.bin"
    checkpoint_path = f"{input_base}_metadata_checkpoint.pkl"
    
    if not os.path.exists(ids_path):
        print(f"Error: IDs file not found: {ids_path}")
        return 1
        
    signal.signal(signal.SIGINT, signal_handler)
    
    # 1. Load Target IDs
    ids_list = load_ids(ids_path)
    if not ids_list:
        print("No IDs loaded.")
        return 1
        
    target_ids_set = set(ids_list)
    print(f"Targeting {len(target_ids_set):,} unique stars.")
    
    # 2. Setup Session
    session = requests.Session()
    session.headers["User-Agent"] = "gaia-unity-metadata/1.0"
    
    # 3. List Files
    try:
        files = list_remote_files(session)
        print(f"Found {len(files)} source files.")
    except Exception as e:
        print(e)
        return 1
        
    if args.max_files:
        files = files[:args.max_files]
        print(f"Testing mode: {len(files)} files.")
        
    # 4. Checkpoint
    checkpoint = load_checkpoint(checkpoint_path)
    if checkpoint:
        metadata_store = checkpoint['metadata_store']
        processed = set(checkpoint['processed_files'])
        files = [f for f in files if f not in processed]
        processed_files_list = checkpoint['processed_files']
        print(f"Resuming: {len(files)} files left.")
    else:
        metadata_store = {}
        processed_files_list = []
        
    _current_metadata = metadata_store
    
    # 5. Process
    with tempfile.TemporaryDirectory(prefix="gaia_meta_") as tmp:
        for idx, fname in enumerate(files, 1):
            if _graceful_shutdown: break
            
            url = urljoin(BASE_URL, fname)
            gz_path = os.path.join(tmp, fname)
            
            print(f"[{idx}/{len(files)}] {fname}...")
            try:
                download_to_path(session, url, gz_path)
                matches = process_file(gz_path, target_ids_set, metadata_store)
                print(f"    -> Found {matches} matches. Total: {len(metadata_store):,}")
                
                processed_files_list.append(fname)
                os.remove(gz_path)
                
                if idx % CHECKPOINT_INTERVAL == 0:
                    save_checkpoint(checkpoint_path, metadata_store, processed_files_list)
                    
            except Exception as e:
                print(f"    Error: {e}")
                save_checkpoint(checkpoint_path, metadata_store, processed_files_list)
                return 1
                
    # 6. Finish
    print("Processing complete.")
    write_output(ids_list, metadata_store, out_path)
    
    if os.path.exists(checkpoint_path):
        os.remove(checkpoint_path)
        
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
