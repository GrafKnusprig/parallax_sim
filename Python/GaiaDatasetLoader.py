#!/usr/bin/env python3
"""
Gaia GDR1 streaming sampler for Unity starfields with robust checkpointing.

Downloads GaiaSource_*.csv.gz from:
    https://cdn.gea.esac.esa.int/Gaia/gdr1/gaia_source/csv/

One file at a time:
  download -> stream-process -> delete

Global (catalog-wide) weighted reservoir sampling -> ~TARGET_STARS final sample.

ROBUST FEATURES:
  - Automatic checkpointing every 10 files
  - Resume capability after interruption/failure
  - Graceful Ctrl+C handling (saves partial results)
  - Progress tracking (remembers processed files)

Output CSV columns are minimal for Unity:
  - source_id
  - ra_deg, dec_deg
  - parallax_mas (must be > 0)
  - distance_pc (derived)
  - phot_g_mean_mag
  - abs_mag_g (derived; optional but useful)

Files created:
  - output.csv (final results)
  - output_checkpoint.pkl (progress checkpoint - auto-deleted on completion)
  - output_partial.csv (created on Ctrl+C for immediate partial results)

Requires: requests
Install:  pip install requests
Run:      python GaiaDatasetLoader.py [output.csv]
Resume:   Just run again - automatically detects and resumes from checkpoint
"""

from __future__ import annotations

import csv
import gzip
import heapq
import json
import math
import os
import pickle
import re
import signal
import sys
import tempfile
import time
from typing import Dict, List, Optional, Tuple
from urllib.parse import urljoin

import requests

# --------- CONFIG ---------
BASE_URL = "https://cdn.gea.esac.esa.int/Gaia/gdr1/gaia_source/csv/"

TARGET_STARS = 1_000_000

# Keep all stars brighter than this G mag. (They still must have parallax > 0.)
ALWAYS_KEEP_MAG_LE = 12.0

# Checkpointing - save progress every N files
CHECKPOINT_INTERVAL = 10

# Equal-area-ish binning to avoid Milky Way domination.
# (RA bins in degrees; Dec bins are uniform in sin(dec).)
N_RA = 360
N_SINDEC = 180

# Density damping strength: higher -> more uniform sky, less Milky Way overkill.
DENSITY_DAMPING = 0.002

# Network robustness
HTTP_TIMEOUT = 120
HTTP_RETRIES = 5
CHUNK_BYTES = 1 << 20

# Output columns
OUT_FIELDS = [
    "source_id",
    "ra_deg",
    "dec_deg",
    "parallax_mas",
    "distance_pc",
    "phot_g_mean_mag",
    "abs_mag_g",
]

# Heap item: (key, source_id, ra, dec, parallax, distance_pc, mag, abs_mag)
HeapItem = Tuple[float, int, float, float, float, float, float, float]

# Global state for signal handling
_graceful_shutdown = False
_current_heap = None
_current_output_path = None


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


def sky_cell(ra_deg: float, dec_deg: float) -> int:
    """Equal-area-ish bin: uniform in RA and sin(dec)."""
    ra_bin = int(ra_deg) % N_RA
    sin_dec = math.sin(math.radians(dec_deg))  # [-1, 1]
    sbin = int((sin_dec + 1.0) * 0.5 * N_SINDEC)
    if sbin < 0:
        sbin = 0
    elif sbin >= N_SINDEC:
        sbin = N_SINDEC - 1
    return sbin * N_RA + ra_bin


def brightness_weight(mag: float) -> float:
    """Relative weight vs ALWAYS_KEEP_MAG_LE; drops exponentially for fainter stars."""
    return 10.0 ** (-0.4 * (mag - ALWAYS_KEEP_MAG_LE))


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
    """Parse the HTML index and return GaiaSource_*.csv.gz filenames."""
    r = session.get(BASE_URL, timeout=HTTP_TIMEOUT)
    r.raise_for_status()
    names = re.findall(r'href="(GaiaSource_[^"]+\.csv\.gz)"', r.text)
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
    cell_seen: Dict[int, int],
) -> Tuple[int, int]:
    """
    Stream one gzipped CSV, update global reservoir.
    Returns (rows_seen, rows_considered).
    """
    rows_seen = 0
    rows_considered = 0

    with gzip.open(gz_path, "rt", newline="") as f:
        reader = csv.DictReader(f)

        # sanity check: required columns
        required = {"source_id", "ra", "dec", "parallax", "phot_g_mean_mag"}
        missing = required - set(reader.fieldnames or [])
        if missing:
            raise RuntimeError(f"{os.path.basename(gz_path)} missing required columns: {sorted(missing)}")

        for row in reader:
            rows_seen += 1

            sid_s = (row.get("source_id") or "").strip()
            if not sid_s:
                continue
            try:
                sid = int(sid_s)
            except ValueError:
                continue

            # Avoid overlap duplicates
            if sid in kept_ids:
                continue

            ra = parse_float(row.get("ra", ""))
            dec = parse_float(row.get("dec", ""))
            mag = parse_float(row.get("phot_g_mean_mag", ""))
            parallax = parse_float(row.get("parallax", ""))

            if ra is None or dec is None or mag is None or parallax is None:
                continue

            # Need usable distance for your parallax positioning
            if parallax <= 0:
                continue

            distance_pc = 1000.0 / parallax  # parallax in mas => distance in parsec

            # Absolute magnitude (useful for "intrinsic-ish" brightness/size)
            abs_mag = mag - 5.0 * (math.log10(distance_pc) - 1.0)

            rows_considered += 1

            # --- Decide priority key ---
            if mag <= ALWAYS_KEEP_MAG_LE:
                # Always keep bright stars (still subject to reservoir size)
                key = 2.0
            else:
                cell = sky_cell(ra, dec)
                c = cell_seen.get(cell, 0)
                cell_seen[cell] = c + 1

                w = brightness_weight(mag) / (1.0 + DENSITY_DAMPING * c)
                if w <= 0:
                    continue

                u = deterministic_u01(sid)
                key = weighted_key(u, w)

            item: HeapItem = (key, sid, ra, dec, parallax, distance_pc, mag, abs_mag)

            # --- Global reservoir update ---
            if len(heap) < TARGET_STARS:
                heapq.heappush(heap, item)
                kept_ids.add(sid)
            else:
                # keep the largest keys; heap[0] is currently the smallest
                if key > heap[0][0]:
                    dropped = heapq.heapreplace(heap, item)
                    kept_ids.remove(dropped[1])  # dropped source_id
                    kept_ids.add(sid)

    return rows_seen, rows_considered


def save_checkpoint(checkpoint_path: str, heap: List[HeapItem], kept_ids: set, 
                  cell_seen: Dict[int, int], processed_files: List[str], 
                  total_seen: int, total_considered: int) -> None:
    """Save current progress to checkpoint file."""
    checkpoint_data = {
        'heap': heap,
        'kept_ids': list(kept_ids),  # Convert set to list for JSON
        'cell_seen': cell_seen,
        'processed_files': processed_files,
        'total_seen': total_seen,
        'total_considered': total_considered,
        'version': '1.0'
    }
    
    # Use pickle for binary data, more reliable for complex objects
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
        
        # Convert kept_ids back to set
        data['kept_ids'] = set(data['kept_ids'])
        
        print(f"Checkpoint loaded: {len(data['heap']):,} stars, {len(data['processed_files'])} files processed")
        return data
    except Exception as e:
        print(f"Warning: Could not load checkpoint ({e}). Starting fresh.")
        return None


def signal_handler(signum, frame):
    """Handle Ctrl+C gracefully by saving current progress."""
    global _graceful_shutdown, _current_heap, _current_output_path
    
    print("\n\n=== Graceful shutdown requested (Ctrl+C) ===")
    _graceful_shutdown = True
    
    if _current_heap and _current_output_path:
        partial_path = _current_output_path.replace('.csv', '_partial.csv')
        print(f"Saving current progress to: {partial_path}")
        write_output(_current_heap, partial_path)
        print(f"Partial results saved with {len(_current_heap):,} stars.")
    
    print("You can resume later using the checkpoint file.")
    sys.exit(0)


def write_output(heap: List[HeapItem], out_path: str) -> None:
    # Sort by magnitude (bright first) for nicer debugging / preview
    items = list(heap)
    items.sort(key=lambda x: x[6])  # mag

    with open(out_path, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=OUT_FIELDS)
        w.writeheader()
        for key, sid, ra, dec, parallax, dist_pc, mag, abs_mag in items:
            w.writerow(
                {
                    "source_id": sid,
                    "ra_deg": ra,
                    "dec_deg": dec,
                    "parallax_mas": parallax,
                    "distance_pc": dist_pc,
                    "phot_g_mean_mag": mag,
                    "abs_mag_g": abs_mag,
                }
            )


def main() -> int:
    global _current_heap, _current_output_path
    
    out_path = "gaia_gdr1_unity_1M.csv"
    if len(sys.argv) >= 2:
        out_path = sys.argv[1]
    
    # Set up signal handler for graceful shutdown
    signal.signal(signal.SIGINT, signal_handler)
    _current_output_path = out_path
    
    checkpoint_path = out_path.replace('.csv', '_checkpoint.pkl')
    
    session = requests.Session()
    session.headers["User-Agent"] = "gaia-unity-stream-sampler/1.0"

    print(f"Listing files from {BASE_URL}")
    files = list_remote_files(session)
    print(f"Found {len(files)} files.")
    
    # Try to load existing checkpoint
    checkpoint = load_checkpoint(checkpoint_path)
    if checkpoint:
        heap = checkpoint['heap']
        kept_ids = checkpoint['kept_ids']
        cell_seen = checkpoint['cell_seen']
        processed_files = checkpoint['processed_files']
        total_seen = checkpoint['total_seen']
        total_considered = checkpoint['total_considered']
        
        # Filter out already processed files
        remaining_files = [f for f in files if f not in processed_files]
        print(f"Resuming: {len(remaining_files)} files remaining")
        files = remaining_files
    else:
        heap: List[HeapItem] = []
        kept_ids: set = set()
        cell_seen: Dict[int, int] = {}
        processed_files: List[str] = []
        total_seen = 0
        total_considered = 0
        print("Starting fresh (no checkpoint found)")
    
    _current_heap = heap  # For signal handler

    with tempfile.TemporaryDirectory(prefix="gaia_dl_") as tmp:
        for idx, name in enumerate(files, 1):
            if _graceful_shutdown:
                break
                
            url = urljoin(BASE_URL, name)
            gz_path = os.path.join(tmp, name)

            print(f"[{idx}/{len(files)}] Downloading {name}")
            try:
                download_to_path(session, url, gz_path)
            except Exception as e:
                print(f"    Failed to download {name}: {e}")
                print(f"    Saving checkpoint and exiting...")
                save_checkpoint(checkpoint_path, heap, kept_ids, cell_seen, processed_files, total_seen, total_considered)
                return 1

            print(f"    Processing... kept={len(heap):,}")
            seen, considered = stream_process_one_gz(gz_path, heap, kept_ids, cell_seen)
            total_seen += seen
            total_considered += considered
            processed_files.append(name)

            # delete immediately
            try:
                os.remove(gz_path)
            except OSError:
                pass

            # Save checkpoint periodically
            if idx % CHECKPOINT_INTERVAL == 0:
                save_checkpoint(checkpoint_path, heap, kept_ids, cell_seen, processed_files, total_seen, total_considered)
            
            if idx % 25 == 0 or idx == len(files):
                print(
                    f"    Progress: files={idx}/{len(files)}, "
                    f"rows_seen={total_seen:,}, rows_used={total_considered:,}, kept={len(heap):,}"
                )

    if _graceful_shutdown:
        print("Processing interrupted by user.")
        return 1
        
    print(f"Writing final output: {out_path} (stars={len(heap):,})")
    write_output(heap, out_path)
    
    # Clean up checkpoint file on successful completion
    try:
        os.remove(checkpoint_path)
        print("Checkpoint file cleaned up.")
    except OSError:
        pass

    print("Done.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())