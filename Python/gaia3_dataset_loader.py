#!/usr/bin/env python3
"""
Gaia GDR3 streaming sampler for Unity starfields.
v3.1 - Galactocentric Filtering & Top-K Brightness Selection

Downloads GaiaSource_*.csv.gz from:
    https://cdn.gea.esac.esa.int/Gaia/gdr3/gaia_source/

One file at a time:
  download -> stream-process -> delete

SELECTION STRATEGY:
  - Space Volume: Milky Way Disk Cylinder (Centered on Sag A*).
    - Radius < 16,000 pc from Galactic Center.
    - Height < 2,000 pc from Galactic Plane.
  - Importance Metric: Absolute Magnitude (Intrinsic Brightness).
  - Algorithm: Keep the N brightest stars found so far (Min-Heap of -AbsMag).

Outputs TWO binary files:
  1. gaia_stars.bin: Unity-ready binary (Header + Records[RA, Dec, Dist, Mag])
     - Float32 format.
  2. gaia_source_ids.bin: Source IDs for metadata.
     - Uint64 format.
"""

from __future__ import annotations

import csv
import gzip
import heapq
import math
import os
import pickle
import re
import signal
import struct
import sys
import tempfile
import time
from typing import Dict, List, Optional, Tuple, Set
from urllib.parse import urljoin

import requests

# --------- CONFIG ---------
BASE_URL = "https://cdn.gea.esac.esa.int/Gaia/gdr3/gaia_source/"

TARGET_STARS = 10_000_000

# Galactocentric Filtering Constants
# Sun is roughly 8kpc from Galactic Center (Sag A*)
SUN_DIST_TO_GC_PC = 8000.0

# Cylinder Dimensions for "Milky Way Disk"
MAX_GAL_RADIUS_PC = 16_000.0  # Radius from Galactic Axis (Z)
MAX_GAL_HEIGHT_PC = 2_000.0   # Height above/below Galactic Plane

# Checkpointing - save progress every N files
CHECKPOINT_INTERVAL = 5

# Network robustness
HTTP_TIMEOUT = 120
HTTP_RETRIES = 5
CHUNK_BYTES = 1 << 20

# Heap item: (key, source_id, ra, dec, parallax, distance_pc, abs_mag)
# Key = -1.0 * AbsoluteMagnitude (Max-Heap for AbsMag)
HeapItem = Tuple[float, int, float, float, float, float, float]

# Global state for signal handling
_graceful_shutdown = False
_current_heap = None
_current_output_base = None

# --------- HELPERS ---------
def parse_float(x: str) -> Optional[float]:
    if x is None: return None
    x = x.strip()
    if not x or x.lower() == "nan": return None
    try: return float(x)
    except ValueError: return None

def calculate_abs_mag(app_mag: float, parallax_mas: float) -> float:
    # M = m + 5log10(parallax_mas) - 10
    if parallax_mas <= 1e-9: return 100.0
    return app_mag + 5.0 * math.log10(parallax_mas) - 10.0

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
    names = re.findall(r'href="(GaiaSource_[^"]+\.csv\.gz)"', r.text)
    names = sorted(set(names))
    if not names:
        names = re.findall(r'(GaiaSource_\d+-\d+\.csv\.gz)', r.text)
        names = sorted(set(names))
    if not names:
        raise RuntimeError("No GaiaSource_*.csv.gz found.")
    return names

def download_to_path(session: requests.Session, url: str, dest_path: str) -> None:
    r = http_get_with_retries(session, url)
    with r:
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=CHUNK_BYTES):
                if chunk: f.write(chunk)

# --------- CORE PROCESSING ---------
def stream_process_one_gz(
    gz_path: str,
    heap: List[HeapItem],
    kept_ids: Set[int],
    target_heap_size: int = TARGET_STARS,
) -> Tuple[int, int, int]:
    """
    Stream one gzipped CSV, update global reservoir (Top-K AbsMag).
    Returns (rows_seen, rows_considered, replacements).
    """
    rows_seen = 0
    rows_considered = 0
    replacements = 0

    min_key_threshold = -9999.0
    heap_full = (len(heap) >= target_heap_size)
    if heap_full:
        min_key_threshold = heap[0][0]

    with gzip.open(gz_path, "rt", newline="", encoding="utf-8") as f:
        def skip_comments(file_obj):
            for line in file_obj:
                if not line.startswith('#'):
                    yield line
        
        reader = csv.DictReader(skip_comments(f))
        
        if reader.fieldnames:
            reader.fieldnames = [x.strip() for x in reader.fieldnames]

        # Require 'l' and 'b' for galactic coords
        required = {"source_id", "ra", "dec", "parallax", "phot_g_mean_mag", "l", "b"}
        found_cols = set(reader.fieldnames or [])
        missing = required - found_cols
        
        if missing:
            print(f"Warning: {os.path.basename(gz_path)} missing columns {missing}. Skipping file.")
            return 0, 0, 0

        for row in reader:
            rows_seen += 1

            sid_s = (row.get("source_id") or "").strip()
            if not sid_s: continue
            try: sid = int(sid_s)
            except ValueError: continue

            if sid in kept_ids: continue

            # Parallax & Distance
            try:
                parallax = float(row.get("parallax", ""))
            except ValueError: continue
            if parallax <= 0: continue
            
            distance_pc = 1000.0 / parallax

            # Galactic Coordinates (l, b) in degrees
            try:
                l_deg = float(row.get("l", ""))
                b_deg = float(row.get("b", ""))
            except ValueError: continue

            # --- GALACTOCENTRIC FILTERING ---
            # 1. Convert to Radians
            l_rad = math.radians(l_deg)
            b_rad = math.radians(b_deg)

            # 2. Convert to Cartesian Heliocentric Galactic Coords (X points to GC)
            # Definition: 
            # l=0, b=0 is direction TO Galactic Center.
            # Z axis is North Galactic Pole (b=90).
            # Y axis is direction of rotation (l=90).
            # So:
            # x_helio = d * cos(b) * cos(l)  (Towards GC)
            # y_helio = d * cos(b) * sin(l)  (Rotation direction)
            # z_helio = d * sin(b)           (Out of plane)
            
            cos_b = math.cos(b_rad)
            x_helio = distance_pc * cos_b * math.cos(l_rad)
            y_helio = distance_pc * cos_b * math.sin(l_rad)
            z_helio = distance_pc * math.sin(b_rad)

            # 3. Convert to Galactocentric Coords
            # Assume Sun is at (-8000, 0, 0) relative to GC (or GC is at (8000, 0, 0) relative to Sun).
            # If GC is at X = +8000 from Sun, then:
            # Pos_from_GC = Pos_from_Sun - Pos_of_GC
            # x_gal = x_helio - 8000.  (Wait. If I look at l=0, I see GC. x_helio is +d. If d=8000, x_helio=8000. x_gal should be 0. So x_gal = 8000 - x_helio? Or x_gal = x_helio - 8000?)
            # Let's standardize: GC is Origin (0,0,0).
            # Sun is at (-8000, 0, 0).
            # Star at l=0, d=1000 (Towards GC). Sun -> Star -> GC.
            # Star should be at (-7000, 0, 0).
            # x_helio = 1000.
            # Coordinate transformation:
            # X_gc = X_helio - 8000 => 1000 - 8000 = -7000. Correct.
            
            x_gc = x_helio - SUN_DIST_TO_GC_PC # Axis towards Sun from GC (roughly)
            y_gc = y_helio
            z_gc = z_helio

            # 4. Check Disk Cylinder
            # Height check
            if abs(z_gc) > MAX_GAL_HEIGHT_PC:
                continue

            # Radius check (R^2 = x^2 + y^2)
            r_gc_sq = x_gc*x_gc + y_gc*y_gc
            if r_gc_sq > (MAX_GAL_RADIUS_PC * MAX_GAL_RADIUS_PC):
                continue
            
            # --- SELECTION ---
            try:
                mag = float(row.get("phot_g_mean_mag", ""))
            except ValueError: continue

            abs_mag = mag + 5.0 * math.log10(parallax) - 10.0
            
            # Key = -AbsMag (Max-Heap logic)
            key = -1.0 * abs_mag

            # We check heap condition BEFORE parsing RA/Dec to save time
            if heap_full and key <= min_key_threshold:
                continue

            try:
                ra = float(row.get("ra", ""))
                dec = float(row.get("dec", ""))
            except ValueError: continue

            rows_considered += 1

            item: HeapItem = (key, sid, ra, dec, parallax, distance_pc, abs_mag)

            if not heap_full:
                heapq.heappush(heap, item)
                kept_ids.add(sid)
                if len(heap) >= target_heap_size:
                    heap_full = True
                    min_key_threshold = heap[0][0]
            else:
                if key > min_key_threshold:
                    dropped = heapq.heapreplace(heap, item)
                    kept_ids.remove(dropped[1])
                    kept_ids.add(sid)
                    replacements += 1
                    min_key_threshold = heap[0][0]

    return rows_seen, rows_considered, replacements

def save_checkpoint(checkpoint_path: str, heap: List[HeapItem], kept_ids: Set[int], 
                  processed_files: List[str], total_seen: int, total_considered: int) -> None:
    checkpoint_data = {
        'heap': heap,
        'kept_ids': list(kept_ids),
        'processed_files': processed_files,
        'total_seen': total_seen,
        'total_considered': total_considered,
        'version': '3.1-galcen'
    }
    tmp_name = checkpoint_path + ".tmp"
    with open(tmp_name, 'wb') as f:
        pickle.dump(checkpoint_data, f)
    os.replace(tmp_name, checkpoint_path)
    print(f"    Checkpoint saved: {len(heap):,} stars, {len(processed_files)} files processed")

def load_checkpoint(checkpoint_path: str) -> Optional[Dict]:
    if not os.path.exists(checkpoint_path): return None
    try:
        with open(checkpoint_path, 'rb') as f:
            data = pickle.load(f)
        data['kept_ids'] = set(data['kept_ids'])
        if data.get('version') != '3.1-galcen':
            print("Checkpoint version mismatch. Ignoring.")
            return None
        print(f"Checkpoint loaded: {len(data['heap']):,} stars, {len(data['processed_files'])} files processed")
        return data
    except Exception as e:
        print(f"Warning: Could not load checkpoint ({e}). Starting fresh.")
        return None

def write_binary_outputs(heap: List[HeapItem], base_name: str) -> None:
    print("Sorting data for output (Brightest first)...")
    items = sorted(heap, key=lambda x: x[6]) 

    bin_path = f"{base_name}.bin"
    ids_path = f"{base_name}_ids.bin"
    
    count = len(items)
    print(f"Writing {bin_path} ({count:,} stars)...")
    
    with open(bin_path, 'wb') as f:
        f.write(struct.pack('<I', count))
        # Write: RA, Dec, Dist, AbsMag
        for item in items:
            f.write(struct.pack('<ffff', item[2], item[3], item[5], item[6]))
    
    print(f"Writing {ids_path} for metadata matching...")
    with open(ids_path, 'wb') as f:
        f.write(struct.pack('<I', count))
        for item in items:
            f.write(struct.pack('<Q', item[1]))

    print(f"Done. Files created:\n -> {bin_path}\n -> {ids_path}")

def signal_handler(signum, frame):
    global _graceful_shutdown
    print("\n\n=== Graceful shutdown requested (Ctrl+C) ===")
    _graceful_shutdown = True
    if _current_heap and _current_output_base:
        print(f"Saving PARTIAL binary output...")
        write_binary_outputs(_current_heap, _current_output_base + "_partial")
    print("You can resume later using the checkpoint file.")
    sys.exit(0)

def main() -> int:
    global _current_heap, _current_output_base
    
    import argparse
    parser = argparse.ArgumentParser(description="Gaia DR3 Dataset Loader (Galactocentric)")
    parser.add_argument("output_base", nargs="?", default="gaia_v3_10m", help="Output file basename")
    parser.add_argument("--max-files", type=int, default=None, help="Stop after N files")
    parser.add_argument("--target-stars", type=int, default=TARGET_STARS, help=f"Target sample size")
    
    args = parser.parse_args()
    
    output_base = args.output_base.replace('.bin', '').replace('.csv', '')
    _current_output_base = output_base
    checkpoint_path = f"{output_base}_checkpoint.pkl"

    signal.signal(signal.SIGINT, signal_handler)

    session = requests.Session()
    session.headers["User-Agent"] = "gaia-unity-sampler/3.1"

    print(f"Reading file list from {BASE_URL}...")
    try:
        files = list_remote_files(session)
    except Exception as e:
        print(f"Failed to list files: {e}")
        return 1
        
    print(f"Found {len(files)} files.")
    
    if args.max_files:
        files = files[:args.max_files]
        print(f"Testing mode: Limiting to first {args.max_files} files.")
    
    target_heap_size = args.target_stars

    checkpoint = load_checkpoint(checkpoint_path)
    if checkpoint:
        heap = checkpoint['heap']
        kept_ids = checkpoint['kept_ids']
        processed_files = checkpoint['processed_files']
        total_seen = checkpoint['total_seen']
        total_considered = checkpoint['total_considered']
        processed_set = set(processed_files)
        files_to_do = [f for f in files if f not in processed_set]
        print(f"Resuming: {len(files_to_do)} files remaining.")
    else:
        heap = []
        kept_ids = set()
        processed_files = []
        total_seen = 0
        total_considered = 0
        files_to_do = files
        print("Starting fresh.")

    _current_heap = heap

    with tempfile.TemporaryDirectory(prefix="gaia_dl_") as tmp:
        for idx, filename in enumerate(files_to_do, 1):
            if _graceful_shutdown: break
            
            url = urljoin(BASE_URL, filename)
            gz_path = os.path.join(tmp, filename)
            
            # Failsafe Download Loop
            while True:
                if _graceful_shutdown: break
                try:
                    download_to_path(session, url, gz_path)
                    break # Success
                except Exception as e:
                    print(f"    Download failed: {e}")
                    print("    Retrying in 10 seconds...")
                    time.sleep(10)
            
            if _graceful_shutdown: break

            now_k = len(heap) / 1000.0
            print(f"    Processing... (Current sample: {now_k:.1f}k stars)")
            seen, considered, repl = stream_process_one_gz(gz_path, heap, kept_ids, target_heap_size)
            total_seen += seen
            total_considered += considered
            processed_files.append(filename)

            try: os.remove(gz_path)
            except OSError: pass

            dimmest_mag = -heap[0][0] if heap else 0
            
            print(f"       -> Checked {seen:,}, In GalCylinder {considered:,}, Swapped {repl:,}")
            if heap:
                print(f"       -> Heap Dimmest AbsMag: {dimmest_mag:.2f}")

            if idx % CHECKPOINT_INTERVAL == 0:
                save_checkpoint(checkpoint_path, heap, kept_ids, processed_files, total_seen, total_considered)

    if _graceful_shutdown: return 1

    print("\nAll files processed.")
    print(f"Total rows seen: {total_seen:,}")
    print(f"Total rows in Galactic Disk: {total_considered:,}")
    print(f"Final sample size: {len(heap):,}")

    write_binary_outputs(heap, output_base)
    
    if os.path.exists(checkpoint_path):
        os.remove(checkpoint_path)

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
