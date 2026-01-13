#!/usr/bin/env python3
"""
Asteroid snapshot -> CSV (optimized for speed, position-only)

This script fetches asteroid positions from JPL Horizons API and outputs them in a Gaia-like CSV format.
Optimized for speed by:
- Only fetching position vectors (no physical properties)
- Only processing asteroids (no planets/moons)
- Minimal API calls (1 per asteroid instead of 2-3)
- Streamlined output (position data only)

Output columns:
source_id, object_type, ra_deg, dec_deg, parallax_mas, distance_pc, 
vx_au_d, vy_au_d, vz_au_d, speed_km_s
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import math
import os
import time
from dataclasses import dataclass
from typing import Any, Optional

import requests


HORIZONS_URL = "https://ssd.jpl.nasa.gov/api/horizons.api"
SBDB_QUERY_URL = "https://ssd-api.jpl.nasa.gov/sbdb_query.api"

# constants
AU_KM = 149_597_870.700
DAY_S = 86_400.0
AU_PER_PARSEC = 206_264.80624709636


@dataclass(frozen=True)
class Body:
    name: str
    command: str
    source_id: str
    kind: str


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def http_get_json(url: str, params: dict[str, str], timeout: int = 60, retries: int = 4, backoff_s: float = 1.5) -> dict[str, Any]:
    last_err: Optional[Exception] = None
    for attempt in range(retries):
        try:
            r = requests.get(url, params=params, timeout=timeout)
            r.raise_for_status()
            return r.json()
        except Exception as e:
            last_err = e
            time.sleep(backoff_s * (2 ** attempt))
    raise RuntimeError(f"Request failed after {retries} tries: {url} {params}") from last_err


def parse_horizons_csv_vectors(result_text: str) -> tuple[list[str], list[str]]:
    lines = result_text.splitlines()
    try:
        i0 = lines.index("$$SOE")
        i1 = lines.index("$$EOE")
    except ValueError:
        raise RuntimeError(f"Could not find $$SOE/$$EOE in Horizons output:\n{result_text[:900]}")

    table = [ln.strip() for ln in lines[i0 + 1 : i1] if ln.strip()]
    if len(table) < 1:
        raise RuntimeError(f"Not enough rows between $$SOE/$$EOE:\n{table}")

    row = [c.strip() for c in table[0].split(",")]
    
    if len(row) >= 8:
        header = ["JD", "Calendar_Date", "X", "Y", "Z", "VX", "VY", "VZ"] + [f"Col{i}" for i in range(8, len(row))]
    else:
        raise RuntimeError(f"Unexpected number of columns in Horizons output: {len(row)} columns in {row}")

    return header, row


def horizons_vectors_icrf(command: str, epoch_utc: str, center: str, step: str = "1 d") -> dict[str, float]:
    """Get XYZ and VX,VY,VZ from Horizons at one epoch (AU and AU/day)."""
    try:
        epoch_dt = dt.datetime.fromisoformat(epoch_utc.replace('Z', '+00:00'))
        stop_dt = epoch_dt + dt.timedelta(days=1)
        stop_utc = stop_dt.strftime('%Y-%m-%dT%H:%M:%S.%fZ')[:-3] + 'Z'
    except Exception:
        stop_utc = epoch_utc.replace('T00:00:00Z', 'T00:00:01Z').replace('T12:00:00Z', 'T12:00:01Z')
        if stop_utc == epoch_utc:
            stop_utc = epoch_utc[:-1] + '1Z' if epoch_utc.endswith('Z') else epoch_utc + '1'
    
    params = {
        "format": "json",
        "EPHEM_TYPE": "VECTORS",
        "MAKE_EPHEM": "YES",
        "OBJ_DATA": "NO",
        "COMMAND": f"'{command}'",
        "CENTER": f"'{center}'",
        "REF_PLANE": "FRAME",
        "REF_SYSTEM": "ICRF",
        "OUT_UNITS": "AU-D",
        "CSV_FORMAT": "YES",
        "START_TIME": f"'{epoch_utc}'",
        "STOP_TIME": f"'{stop_utc}'",
        "STEP_SIZE": f"'{step}'",
        "VEC_TABLE": "2",
        "VEC_LABELS": "YES",
    }

    data = http_get_json(HORIZONS_URL, params=params)
    result = data.get("result", "")
    if not result:
        raise RuntimeError(f"Empty Horizons result for {command}")

    header, row = parse_horizons_csv_vectors(result)

    def find_col(name: str) -> int:
        name_u = name.upper()
        for idx, col in enumerate(header):
            if col.strip().upper() == name_u:
                return idx
        for idx, col in enumerate(header):
            if col.strip().upper().startswith(name_u):
                return idx
        raise RuntimeError(f"Could not find {name} in Horizons header: {header}")

    ix, iy, iz = find_col("X"), find_col("Y"), find_col("Z")
    ivx, ivy, ivz = find_col("VX"), find_col("VY"), find_col("VZ")

    return {
        "x": float(row[ix]),
        "y": float(row[iy]),
        "z": float(row[iz]),
        "vx": float(row[ivx]),
        "vy": float(row[ivy]),
        "vz": float(row[ivz]),
    }


def xyz_to_radec_deg(x: float, y: float, z: float) -> tuple[float, float, float]:
    r = math.sqrt(x*x + y*y + z*z)
    if r == 0.0:
        return 0.0, 0.0, 0.0
    ra = math.degrees(math.atan2(y, x)) % 360.0
    dec = math.degrees(math.asin(z / r))
    return ra, dec, r


def au_to_pc(au: float) -> float:
    return au / AU_PER_PARSEC


def parallax_mas_from_pc(distance_pc: float) -> float:
    if distance_pc <= 0.0:
        return 0.0
    return 1000.0 / distance_pc


def au_per_day_to_km_s(v_au_d: float) -> float:
    return (v_au_d * AU_KM) / DAY_S


def speed_km_s(vx_au_d: float, vy_au_d: float, vz_au_d: float) -> float:
    v = math.sqrt(vx_au_d*vx_au_d + vy_au_d*vy_au_d + vz_au_d*vz_au_d)
    return au_per_day_to_km_s(v)


def sbdb_asteroid_list(limit: int, offset: int = 0) -> list[Body]:
    """Fetch asteroid list from SBDB (only spkid and name, no physical properties)."""
    print(f"[INFO] Fetching {limit} asteroids from SBDB API (offset: {offset})...")
    params = {
        "fields": "spkid,full_name",
        "sb-kind": "a",
        "sort": "-diameter",
        "limit": str(limit),
        "limit-from": str(offset),
    }
    
    data = http_get_json(SBDB_QUERY_URL, params=params)
    fields = data.get("fields", [])
    rows = data.get("data", [])
    
    if not fields or not isinstance(rows, list):
        raise RuntimeError(f"Unexpected SBDB response: {data}")
    
    print(f"[INFO] SBDB returned {len(rows)} asteroids")
    
    bodies: list[Body] = []
    for row in rows:
        rm = dict(zip(fields, row))
        spkid = str(rm.get("spkid", "")).strip()
        name = str(rm.get("full_name", spkid)).strip()
        if not spkid:
            continue
        
        cmd = f"DES={spkid};"
        bodies.append(Body(name=name, command=cmd, source_id=spkid, kind="asteroid"))
    
    print(f"[INFO] Successfully fetched {len(bodies)} asteroid identifiers")
    return bodies


def load_existing_progress(csv_path: str) -> set[str]:
    """Load existing progress from CSV file."""
    completed_ids: set[str] = set()
    if not os.path.exists(csv_path):
        return completed_ids
    
    try:
        with open(csv_path, "r", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            for row in reader:
                source_id = row.get("source_id", "").strip()
                if source_id:
                    completed_ids.add(source_id)
        print(f"[INFO] Loaded {len(completed_ids)} completed entries from {csv_path}")
    except Exception as e:
        print(f"[WARN] Could not read existing CSV {csv_path}: {e}")
    
    return completed_ids


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--epoch", default=None, help="UTC epoch ISO, e.g. 2026-01-12T12:00:00Z (default: now)")
    ap.add_argument("--out", default="asteroids_optimized.csv", help="Output CSV")
    ap.add_argument("--center", default="500@10", help="Horizons CENTER, default heliocentric (Sun)")
    ap.add_argument("--rate-limit", type=float, default=0.15, help="Sleep seconds between Horizons calls")
    ap.add_argument("--asteroids", type=int, default=10000, help="How many asteroids to fetch")
    ap.add_argument("--asteroid-offset", type=int, default=0, help="SBDB paging offset")
    ap.add_argument("--resume", action="store_true", help="Resume from existing CSV")
    ap.add_argument("--progress-interval", type=int, default=100, help="Print progress every N bodies")

    args = ap.parse_args()
    epoch = args.epoch or utc_now_iso()
    
    print(f"[INFO] ===== Asteroid Position Snapshot (Optimized) =====")
    print(f"[INFO] Epoch: {epoch}")
    print(f"[INFO] Center: {args.center}")
    print(f"[INFO] Rate limit: {args.rate_limit}s between API calls")
    print(f"[INFO] Asteroids to fetch: {args.asteroids}")
    print(f"[INFO] Output file: {args.out}")
    print(f"[INFO] Resume mode: {args.resume}")
    print(f"[INFO] ===================================================")

    # Load existing progress if resuming
    completed_ids: set[str] = set()
    if args.resume:
        print(f"[INFO] Checking for existing progress in {args.out}...")
        completed_ids = load_existing_progress(args.out)
    
    # Fetch asteroid list
    print(f"[INFO] Fetching asteroid list from SBDB...")
    asteroids = sbdb_asteroid_list(args.asteroids, args.asteroid_offset)
    
    # Filter out already completed asteroids if resuming
    if args.resume:
        original_count = len(asteroids)
        asteroids = [a for a in asteroids if a.source_id not in completed_ids]
        skipped_count = original_count - len(asteroids)
        print(f"[INFO] Resuming: skipping {skipped_count} already completed, processing {len(asteroids)} remaining")
    
    if not asteroids:
        print("[INFO] All asteroids already processed!")
        return

    # Open file in append mode if resuming, write mode if starting fresh
    file_mode = "a" if args.resume and os.path.exists(args.out) else "w"
    write_header = file_mode == "w" or not os.path.exists(args.out)
    
    with open(args.out, file_mode, newline="", encoding="utf-8") as f:
        w = csv.writer(f)

        # Write header only for new files
        if write_header:
            w.writerow([
                "source_id",
                "object_type",
                "ra_deg",
                "dec_deg",
                "parallax_mas",
                "distance_pc",
                "vx_au_d",
                "vy_au_d",
                "vz_au_d",
                "speed_km_s",
            ])
        
        print(f"[INFO] Processing {len(asteroids)} asteroids, writing to {args.out} (mode: {file_mode})")
        print(f"[INFO] Each asteroid requires 1 Horizons API call (vectors only)")
        print(f"[INFO] ===================================================")

        processed_count = 0
        failed_count = 0
        start_time = time.time()
        
        for i, asteroid in enumerate(asteroids, 1):
            # Get position vectors only (no physical properties)
            try:
                vec = horizons_vectors_icrf(asteroid.command, epoch, args.center)
                x, y, z = vec["x"], vec["y"], vec["z"]
                vx, vy, vz = vec["vx"], vec["vy"], vec["vz"]

                ra_deg, dec_deg, r_au = xyz_to_radec_deg(x, y, z)
                dist_pc = au_to_pc(r_au)
                plx_mas = parallax_mas_from_pc(dist_pc)
                sp_kms = speed_km_s(vx, vy, vz)

                w.writerow([
                    asteroid.source_id,
                    asteroid.kind,
                    f"{ra_deg:.9f}",
                    f"{dec_deg:.9f}",
                    f"{plx_mas:.9f}",
                    f"{dist_pc:.12e}",
                    f"{vx:.12e}",
                    f"{vy:.12e}",
                    f"{vz:.12e}",
                    f"{sp_kms:.9f}",
                ])
                
                f.flush()
                processed_count += 1
                
            except Exception as e:
                # Write empty row and continue
                print(f"[WARN] [{i}/{len(asteroids)}] FAILED: {asteroid.name} ({asteroid.source_id}): {str(e)[:80]}")
                w.writerow([asteroid.source_id, asteroid.kind] + [""] * 8)
                f.flush()
                failed_count += 1
            
            # Progress reporting
            if processed_count % args.progress_interval == 0:
                elapsed = time.time() - start_time
                rate = processed_count / elapsed if elapsed > 0 else 0
                remaining = len(asteroids) - i
                eta = remaining / rate if rate > 0 else 0
                print(f"[INFO] Progress: {i}/{len(asteroids)} ({100*i/len(asteroids):.1f}%) | "
                      f"Success: {processed_count} | Failed: {failed_count} | "
                      f"Rate: {rate:.1f}/s | ETA: {eta/60:.1f}m")

            time.sleep(args.rate_limit)

        total_time = time.time() - start_time
        total_rows = len(completed_ids) + processed_count if args.resume else processed_count
        print(f"[INFO] ===================================================")
        print(f"[INFO] Completed! Wrote {args.out}")
        print(f"[INFO] Total rows: {total_rows} (processed this run: {processed_count}, failed: {failed_count})")
        print(f"[INFO] Total time: {total_time/60:.1f} minutes ({total_time/processed_count:.2f}s per asteroid)")
        print(f"[INFO] ===================================================")


if __name__ == "__main__":
    main()
