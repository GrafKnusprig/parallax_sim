#!/usr/bin/env python3
"""
Gaia GDR3 streaming sampler for Unity starfields.

Downloads GaiaSource_*.csv.gz from:
    https://cdn.gea.esac.esa.int/Gaia/gdr3/gaia_source/

One file at a time:
  download -> stream-process -> delete

Global (catalog-wide) weighted reservoir sampling -> ~TARGET_STARS final sample.

WEIGHTING STRATEGY (Milky Way Representation):
  - Brightness: 10^(-0.4 * mag) [Standard flux weighting]
  - Distance: 1 / sqrt(distance) [Soft penalty for very distant stars]
  - NO density damping: We want natural density variations (Milky Way disk) within the sample.

Outputs TWO binary files:
  1. gaia_stars.bin: Unity-ready binary (Header + Records[RA, Dec, Dist, Mag])
     - Float32 format for direct GPU/C# loading.
  2. gaia_source_ids.bin: Source IDs for metadata matching (Header + Records[SourceID])
     - Uint64 format. Used to fetch color/temp later without re-downloading positions.
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
from typing import Dict, List, Optional, Tuple
from urllib.parse import urljoin

import requests

# --------- CONFIG ---------
BASE_URL = "https://cdn.gea.esac.esa.int/Gaia/gdr3/gaia_source/"

TARGET_STARS = 10_000_000

# Keep all stars brighter than this G mag (still subject to reservoir if we overflow, but very high weight)
# This ensures naked-eye stars are almost guaranteed to be in.
ALWAYS_KEEP_MAG_LE = 8.0

# Checkpointing - save progress every N files
CHECKPOINT_INTERVAL = 5

# Network robustness
HTTP_TIMEOUT = 120
HTTP_RETRIES = 5
CHUNK_BYTES = 1 << 20

# Heap item: (key, source_id, ra, dec, parallax, distance_pc, mag)
# We don't need abs_mag in the heap, we can compute it if needed, but we output mag.
HeapItem = Tuple[float, int, float, float, float, float, float]

# Global state for signal handling
_graceful_shutdown = False
_current_heap = None
_current_output_base = None # Base name for output files


# --------- HELPERS ---------
def parse_float(x: str) -> Optional[float]:
    if x is None:
        return None
    x = x.strip()
    if not x or x.lower() == "nan":
        return None
    try:
        return float(x)
    except ValueError:
        return None


def calculate_weight(mag: float, distance_pc: float) -> float:
    """
    Calculate sampling weight.
    Target: Represent Milky Way structure well.
    Strategy:
      - Bright stars are important (flux).
      - We want to see the disk structure, so we don't suppress high density areas.
      - We gently damp extremely distant background stars that would be invisible anyway,
        to save budget for the galactic disk structure.
    """
    # 1. Flux-based weight (standard candle visibility volume scales with this?)
    # Actually, simple flux weight is 10^(-0.4 * mag).
    w_flux = 10.0 ** (-0.4 * mag)
    
    # 2. Distance penalty.
    # We want to favor stars that define the local and medium-range structure (arms).
    # A simple 1/sqrt(d) bias gently suppresses the 'infinite' background
    # without cutting it off entirely.
    # Add small epsilon to dist to avoid div/0, though min dist is usually > 0.
    w_dist = 1.0 / math.sqrt(max(1.0, distance_pc))
    
    return w_flux * w_dist


def deterministic_u01(source_id: int) -> float:
    """
    Deterministic pseudo-random U(0,1) from source_id.
    Reproducible without RNG state.
    """
    x = (source_id ^ 0x9E3779B97F4A7C15) & 0xFFFFFFFFFFFFFFFF
    x = (x + 0x9E3779B97F4A7C15) & 0xFFFFFFFFFFFFFFFF
    z = x
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    z = z ^ (z >> 31)
    return (z + 1) / (2**64 + 2)


def weighted_key(u: float, w: float) -> float:
    """
    Weighted reservoir sampling (Efraimidis–Spirakis):
    key = u^(1/w). Keep the largest keys.
    """
    if w <= 0:
        return 0.0
    return u ** (1.0 / w)


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
    """Parse the HTML index to find GaiaSource_*.csv.gz filenames."""
    print(f"Fetching index from {BASE_URL}...")
    r = session.get(BASE_URL, timeout=HTTP_TIMEOUT)
    r.raise_for_status()
    
    # Regex for GDR3 files
    # Expected format: <a href="GaiaSource_000000-003111.csv.gz">GaiaSource_000000-003111.csv.gz</a>
    names = re.findall(r'href="(GaiaSource_[^"]+\.csv\.gz)"', r.text)
    names = sorted(set(names))
    
    if not names:
        # Fallback: maybe they are not directly in href quotes in some directory listings
        # Try a broader regex if the first one fails
        names = re.findall(r'(GaiaSource_\d+-\d+\.csv\.gz)', r.text)
        names = sorted(set(names))

    if not names:
        raise RuntimeError("No GaiaSource_*.csv.gz found. Directory listing format may have changed.")
    
    return names


def download_to_path(session: requests.Session, url: str, dest_path: str) -> None:
    r = http_get_with_retries(session, url)
    with r:
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=CHUNK_BYTES):
                if chunk:
                    f.write(chunk)


# --------- CORE PROCESSING ---------
def stream_process_one_gz(
    gz_path: str,
    heap: List[HeapItem],
    kept_ids: set,
    target_heap_size: int = TARGET_STARS,
) -> Tuple[int, int]:
    """
    Stream one gzipped CSV, update global reservoir.
    Returns (rows_seen, rows_considered).
    """
    rows_seen = 0
    rows_considered = 0

    with gzip.open(gz_path, "rt", newline="", encoding="utf-8") as f:
        # Gaia CSVs often have comment lines at the start.
        # We need to skip them to find the header.
        
        # Read lines until we find one that doesn't start with '#'
        # We can't use f.tell() / seek() easily with gzip in text mode sometimes, 
        # but we can just use a generator or iterate.
        
        # Since DictReader takes an iterable, we can wrap f.
        def skip_comments(file_obj):
            for line in file_obj:
                if not line.startswith('#'):
                    yield line
        
        reader = csv.DictReader(skip_comments(f))
        
        # Normailze field names (strip whitespace)
        if reader.fieldnames:
            reader.fieldnames = [x.strip() for x in reader.fieldnames]

        # Debug: print header found
        # print(f"DEBUG: Found columns: {reader.fieldnames}")

        # sanity check: required columns

        # sanity check: required columns
        # GDR3 columns: source_id, ra, dec, parallax, phot_g_mean_mag, etc.
        required = {"source_id", "ra", "dec", "parallax", "phot_g_mean_mag"}
        found_cols = set(reader.fieldnames or [])
        missing = required - found_cols
        
        if missing:
            # Maybe it's a metadata file, skip gracefully if it lacks critical columns
            print(f"Warning: {os.path.basename(gz_path)} missing columns {missing}. Skipping file.")
            return 0, 0

        for row in reader:
            rows_seen += 1

            sid_s = (row.get("source_id") or "").strip()
            if not sid_s:
                continue
            try:
                sid = int(sid_s)
            except ValueError:
                continue

            # Avoid overlap duplicates (shouldn't happen in disjoint files, but good safety)
            if sid in kept_ids:
                continue

            ra = parse_float(row.get("ra", ""))
            dec = parse_float(row.get("dec", ""))
            mag = parse_float(row.get("phot_g_mean_mag", ""))
            parallax = parse_float(row.get("parallax", ""))

            if ra is None or dec is None or mag is None or parallax is None:
                continue

            # FILTER: Must have valid positive parallax (we need distance)
            if parallax <= 0:
                continue
            
            # FILTER: Rough Magnitude cut (soft limit)
            # We don't want to fill the heap with mag 20 stars if we have a 10M limit.
            # But the reservoir sampling naturally handles this via weights.
            # Still, skipping very faint stars (e.g. mag > 15 or 16) optimization?
            # User said "not too crazy filtering". Let's rely on weights mostly.
            # But parallax error is high for faint stars.
            # Let's cull only truly junk data or very faint if purely for visualization.
            # Let's say keep everything that has parallax > 0 for now, trust the weights.

            distance_pc = 1000.0 / parallax  # parallax in mas => distance in parsec

            rows_considered += 1

            # --- Decide priority key ---
            if mag <= ALWAYS_KEEP_MAG_LE:
                # Super high weight for naked eye stars
                w = 1e9 
            else:
                w = calculate_weight(mag, distance_pc)

            if w <= 0:
                continue

            u = deterministic_u01(sid)
            key = weighted_key(u, w)

            item: HeapItem = (key, sid, ra, dec, parallax, distance_pc, mag)

            # --- Global reservoir update ---
            if len(heap) < target_heap_size:
                heapq.heappush(heap, item)
                kept_ids.add(sid)
            else:
                # heap[0] is the smallest key (item to drop)
                if key > heap[0][0]:
                    dropped = heapq.heapreplace(heap, item)
                    kept_ids.remove(dropped[1])  # dropped source_id
                    kept_ids.add(sid)

    return rows_seen, rows_considered


def save_checkpoint(checkpoint_path: str, heap: List[HeapItem], kept_ids: set, 
                  processed_files: List[str], total_seen: int, total_considered: int) -> None:
    """Save current progress to checkpoint file."""
    checkpoint_data = {
        'heap': heap,
        'kept_ids': list(kept_ids),
        'processed_files': processed_files,
        'total_seen': total_seen,
        'total_considered': total_considered,
        'version': '2.0-gdr3'
    }
    
    with open(checkpoint_path, 'wb') as f:
        pickle.dump(checkpoint_data, f)
    print(f"    Checkpoint saved: {len(heap):,} stars, {len(processed_files)} files processed")


def load_checkpoint(checkpoint_path: str) -> Optional[Dict]:
    """Load progress from checkpoint file if it exists."""
    if not os.path.exists(checkpoint_path):
        return None
    
    try:
        with open(checkpoint_path, 'rb') as f:
            data = pickle.load(f)
        
        data['kept_ids'] = set(data['kept_ids'])
        
        # Version check
        if data.get('version') != '2.0-gdr3':
            print("Checkpoint version mismatch. Ignoring.")
            return None

        print(f"Checkpoint loaded: {len(data['heap']):,} stars, {len(data['processed_files'])} files processed")
        return data
    except Exception as e:
        print(f"Warning: Could not load checkpoint ({e}). Starting fresh.")
        return None


def write_binary_outputs(heap: List[HeapItem], base_name: str) -> None:
    """
    Write TWO binary files.
    1. {base_name}.bin: Unity data (RA, DEC, Dist, Mag)
    2. {base_name}_ids.bin: Source IDs
    """
    
    # Sort for final output: usually by magnitude (brightest first) is best for rendering priority
    # Or by ID?
    # Unity usually doesn't care, but brightest first is good for linear loading fading.
    print("Sorting data for output (Brightest first)...")
    # key is heap item [0], mag is [6]
    items = sorted(heap, key=lambda x: x[6]) 

    bin_path = f"{base_name}.bin"
    ids_path = f"{base_name}_ids.bin"
    
    count = len(items)
    print(f"Writing {bin_path} ({count:,} stars)...")
    
    with open(bin_path, 'wb') as f:
        # Header: uint32 total count
        f.write(struct.pack('<I', count))
        
        # Records: float32 RA, DEC, Dist, Magnitude
        # 4 floats * 4 bytes = 16 bytes per star
        # Batch packing for speed
        batch_size = 10000
        for i in range(0, count, batch_size):
            batch = items[i:i+batch_size]
            # Create format string 'ffff...ffff'
            # Or just struct.pack many times. struct.pack_into with buffer is faster but python overhead dominates.
            # Let's just loop, it's IO bound mostly.
            for item in batch:
                # item: (key, sid, ra, dec, parallax, dist, mag)
                # write: ra, dec, dist, mag
                f.write(struct.pack('<ffff', item[2], item[3], item[5], item[6]))
    
    print(f"Writing {ids_path} for metadata matching...")
    with open(ids_path, 'wb') as f:
        # Header: uint32 total count
        f.write(struct.pack('<I', count))
        
        # Records: uint64 SourceID
        for item in items:
            # item[1] is source_id (int)
            f.write(struct.pack('<Q', item[1]))

    print(f"Done. Files created:\n -> {bin_path}\n -> {ids_path}")


def signal_handler(signum, frame):
    """Handle Ctrl+C gracefully by saving current progress."""
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
    parser = argparse.ArgumentParser(description="Gaia DR3 Dataset Loader")
    parser.add_argument("output_base", nargs="?", default="gaia_v3_10m", help="Output file basename (default: gaia_v3_10m)")
    parser.add_argument("--max-files", type=int, default=None, help="Stop after processing N files (for testing)")
    parser.add_argument("--target-stars", type=int, default=TARGET_STARS, help=f"Target sample size (default: {TARGET_STARS})")
    
    args = parser.parse_args()
    
    output_base = args.output_base.replace('.bin', '').replace('.csv', '')
    
    _current_output_base = output_base
    checkpoint_path = f"{output_base}_checkpoint.pkl"

    # Set up signal handler
    signal.signal(signal.SIGINT, signal_handler)

    session = requests.Session()
    session.headers["User-Agent"] = "gaia-unity-sampler/2.0"

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

    # Try to load existing checkpoint
    checkpoint = load_checkpoint(checkpoint_path)
    if checkpoint:
        heap = checkpoint['heap']
        kept_ids = checkpoint['kept_ids']
        processed_files = checkpoint['processed_files']
        total_seen = checkpoint['total_seen']
        total_considered = checkpoint['total_considered']
        
        # Filter files
        processed_set = set(processed_files)
        remaining_files = [f for f in files if f not in processed_set]
        print(f"Resuming: {len(remaining_files)} files remaining.")
        # We must only iterate remaining.
        # But we need to download them.
        files_to_do = remaining_files
    else:
        heap: List[HeapItem] = []
        kept_ids: set = set()
        processed_files: List[str] = []
        total_seen = 0
        total_considered = 0
        files_to_do = files
        print("Starting fresh.")

    _current_heap = heap

    with tempfile.TemporaryDirectory(prefix="gaia_dl_") as tmp:
        for idx, filename in enumerate(files_to_do, 1):
            if _graceful_shutdown:
                break
            
            url = urljoin(BASE_URL, filename)
            gz_path = os.path.join(tmp, filename)
            
            print(f"[{idx}/{len(files_to_do)}] Downloading {filename}...")
            try:
                download_to_path(session, url, gz_path)
            except Exception as e:
                print(f"    Failed download: {e}")
                print("    Saving checkpoint and stopping.")
                save_checkpoint(checkpoint_path, heap, kept_ids, processed_files, total_seen, total_considered)
                return 1

            print(f"    Processing... (Current sample: {len(heap):,})")
            seen, considered = stream_process_one_gz(gz_path, heap, kept_ids, target_heap_size)
            total_seen += seen
            total_considered += considered
            processed_files.append(filename)

            # cleanup
            try:
                os.remove(gz_path)
            except OSError:
                pass

            # Periodically show stats
            if idx % 1 == 0: # Print every file for visual feedback since files are large
                print(f"       -> Seen {seen:,} rows, Kept {len(heap):,} total.")
            
            # Checkpoint
            if idx % CHECKPOINT_INTERVAL == 0:
                save_checkpoint(checkpoint_path, heap, kept_ids, processed_files, total_seen, total_considered)

    if _graceful_shutdown:
        return 1

    print("\nAll files processed.")
    print(f"Total rows seen: {total_seen:,}")
    print(f"Total rows considered: {total_considered:,}")
    print(f"Final sample size: {len(heap):,}")

    write_binary_outputs(heap, output_base)
    
    # Cleanup checkpoint
    if os.path.exists(checkpoint_path):
        os.remove(checkpoint_path)
        print("Checkpoint removed.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
