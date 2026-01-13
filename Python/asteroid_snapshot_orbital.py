#!/usr/bin/env python3
"""
Asteroid snapshot using orbital elements (ultra-fast, batch processing)

This script computes asteroid positions from orbital elements instead of individual API calls.
Optimized for processing 100,000+ asteroids quickly.

Process:
1. Fetch orbital elements from SBDB in batches (10k-100k at a time)
2. Compute positions locally using Keplerian orbit mechanics
3. Output Gaia-like CSV format

Output columns:
source_id, object_type, ra_deg, dec_deg, parallax_mas, distance_pc, 
vx_au_d, vy_au_d, vz_au_d, speed_km_s
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import math
import struct
import time
from typing import Any, Optional

import requests


SBDB_QUERY_URL = "https://ssd-api.jpl.nasa.gov/sbdb_query.api"

# Constants
AU_KM = 149_597_870.700
DAY_S = 86_400.0
AU_PER_PARSEC = 206_264.80624709636
MU_SUN = 1.32712440018e20  # Sun's gravitational parameter [m^3/s^2]
MU_SUN_AU3_DAY2 = 2.9591220828559093e-4  # in AU^3/day^2
DEG_TO_RAD = math.pi / 180.0
RAD_TO_DEG = 180.0 / math.pi

# Obliquity of ecliptic (J2000) for ecliptic->equatorial conversion
OBLIQUITY_J2000 = 23.43928 * DEG_TO_RAD


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def http_get_json(url: str, params: dict[str, str], timeout: int = 120, retries: int = 4, backoff_s: float = 2.0) -> dict[str, Any]:
    last_err: Optional[Exception] = None
    for attempt in range(retries):
        try:
            r = requests.get(url, params=params, timeout=timeout)
            r.raise_for_status()
            return r.json()
        except Exception as e:
            last_err = e
            time.sleep(backoff_s * (2 ** attempt))
    raise RuntimeError(f"Request failed after {retries} tries: {url}") from last_err


def fetch_orbital_elements_batch(limit: int, offset: int, include_magnitude: bool = False) -> list[dict[str, Any]]:
    """Fetch orbital elements from SBDB in one batch."""
    print(f"[INFO] Fetching orbital elements for {limit} asteroids (offset: {offset})...")
    
    # Request orbital elements and basic identifiers
    fields = "spkid,full_name,a,e,i,om,w,ma,epoch"
    if include_magnitude:
        fields += ",H"
    
    params = {
        "fields": fields,
        "sb-kind": "a",  # asteroids only
        "sort": "-diameter",  # largest first
        "limit": str(limit),
        "limit-from": str(offset),
    }
    
    start_time = time.time()
    data = http_get_json(SBDB_QUERY_URL, params=params)
    elapsed = time.time() - start_time
    
    fields = data.get("fields", [])
    rows = data.get("data", [])
    
    if not fields or not isinstance(rows, list):
        raise RuntimeError(f"Unexpected SBDB response: {data}")
    
    print(f"[INFO] Received {len(rows)} asteroids in {elapsed:.1f}s")
    
    # Parse into list of dictionaries
    asteroids = []
    for row in rows:
        asteroid = dict(zip(fields, row))
        # Only include if we have complete orbital elements
        if all(asteroid.get(k) is not None for k in ["a", "e", "i", "om", "w", "ma", "epoch"]):
            asteroids.append(asteroid)
    
    print(f"[INFO] {len(asteroids)} asteroids have complete orbital elements")
    return asteroids


def solve_kepler(M: float, e: float, tolerance: float = 1e-8, max_iter: int = 50) -> float:
    """Solve Kepler's equation M = E - e*sin(E) for eccentric anomaly E using Newton-Raphson."""
    E = M  # Initial guess
    for _ in range(max_iter):
        f = E - e * math.sin(E) - M
        f_prime = 1.0 - e * math.cos(E)
        E_new = E - f / f_prime
        if abs(E_new - E) < tolerance:
            return E_new
        E = E_new
    return E  # Return best estimate if not converged


def orbital_elements_to_position(a: float, e: float, i: float, omega: float, w: float, 
                                  M0: float, epoch_jd: float, target_jd: float) -> tuple[float, float, float, float, float, float]:
    """
    Compute heliocentric position and velocity from orbital elements.
    
    Args:
        a: semi-major axis [AU]
        e: eccentricity
        i: inclination [degrees]
        omega: longitude of ascending node [degrees]
        w: argument of perihelion [degrees]
        M0: mean anomaly at epoch [degrees]
        epoch_jd: epoch of orbital elements [Julian Date]
        target_jd: target epoch for position [Julian Date]
    
    Returns:
        (x, y, z, vx, vy, vz) in AU and AU/day (ICRF equatorial frame)
    """
    # Convert angles to radians
    i_rad = i * DEG_TO_RAD
    omega_rad = omega * DEG_TO_RAD
    w_rad = w * DEG_TO_RAD
    M0_rad = M0 * DEG_TO_RAD
    
    # Compute mean motion [rad/day]
    n = math.sqrt(MU_SUN_AU3_DAY2 / (a ** 3))
    
    # Propagate mean anomaly to target epoch
    dt_days = target_jd - epoch_jd
    M = M0_rad + n * dt_days
    M = M % (2 * math.pi)  # Normalize to [0, 2π)
    
    # Solve Kepler's equation for eccentric anomaly
    E = solve_kepler(M, e)
    
    # Compute true anomaly
    nu = 2.0 * math.atan2(math.sqrt(1 + e) * math.sin(E / 2), math.sqrt(1 - e) * math.cos(E / 2))
    
    # Distance from Sun
    r = a * (1 - e * math.cos(E))
    
    # Position in orbital plane (x' along perihelion, z' perpendicular to orbit)
    x_orb = r * math.cos(nu)
    y_orb = r * math.sin(nu)
    
    # Velocity in orbital plane
    v_factor = math.sqrt(MU_SUN_AU3_DAY2 / (a * (1 - e * e)))
    vx_orb = -v_factor * math.sin(nu)
    vy_orb = v_factor * (e + math.cos(nu))
    
    # Rotation matrices: orbital plane -> ecliptic
    cos_w = math.cos(w_rad)
    sin_w = math.sin(w_rad)
    cos_omega = math.cos(omega_rad)
    sin_omega = math.sin(omega_rad)
    cos_i = math.cos(i_rad)
    sin_i = math.sin(i_rad)
    
    # Combined rotation matrix elements for position
    P11 = cos_w * cos_omega - sin_w * sin_omega * cos_i
    P12 = -sin_w * cos_omega - cos_w * sin_omega * cos_i
    P21 = cos_w * sin_omega + sin_w * cos_omega * cos_i
    P22 = -sin_w * sin_omega + cos_w * cos_omega * cos_i
    P31 = sin_w * sin_i
    P32 = cos_w * sin_i
    
    # Position in ecliptic frame
    x_ecl = P11 * x_orb + P12 * y_orb
    y_ecl = P21 * x_orb + P22 * y_orb
    z_ecl = P31 * x_orb + P32 * y_orb
    
    # Velocity in ecliptic frame
    vx_ecl = P11 * vx_orb + P12 * vy_orb
    vy_ecl = P21 * vx_orb + P22 * vy_orb
    vz_ecl = P31 * vx_orb + P32 * vy_orb
    
    # Convert from ecliptic to equatorial (ICRF) coordinates
    cos_eps = math.cos(OBLIQUITY_J2000)
    sin_eps = math.sin(OBLIQUITY_J2000)
    
    x = x_ecl
    y = y_ecl * cos_eps - z_ecl * sin_eps
    z = y_ecl * sin_eps + z_ecl * cos_eps
    
    vx = vx_ecl
    vy = vy_ecl * cos_eps - vz_ecl * sin_eps
    vz = vy_ecl * sin_eps + vz_ecl * cos_eps
    
    return x, y, z, vx, vy, vz


def xyz_to_radec_deg(x: float, y: float, z: float) -> tuple[float, float, float]:
    """Convert Cartesian to spherical coordinates."""
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


def speed_km_s(vx: float, vy: float, vz: float) -> float:
    """Compute speed in km/s from velocity in AU/day."""
    v = math.sqrt(vx*vx + vy*vy + vz*vz)
    return au_per_day_to_km_s(v)


def iso_to_jd(iso_utc: str) -> float:
    """Convert ISO UTC timestamp to Julian Date."""
    dt_obj = dt.datetime.fromisoformat(iso_utc.replace('Z', '+00:00'))
    # Julian Date calculation
    a = (14 - dt_obj.month) // 12
    y = dt_obj.year + 4800 - a
    m = dt_obj.month + 12 * a - 3
    jd = dt_obj.day + (153 * m + 2) // 5 + 365 * y + y // 4 - y // 100 + y // 400 - 32045
    # Add fractional day
    jd += (dt_obj.hour - 12) / 24.0 + dt_obj.minute / 1440.0 + dt_obj.second / 86400.0
    return jd


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--epoch", default=None, help="UTC epoch ISO, e.g. 2026-01-12T12:00:00Z (default: now)")
    ap.add_argument("--out", default="asteroids_orbital.bin", help="Output file (.bin for binary, .csv for CSV)")
    ap.add_argument("--asteroids", type=int, default=100000, help="How many asteroids to fetch (default: 100k)")
    ap.add_argument("--batch-size", type=int, default=10000, help="SBDB query batch size (max ~100k)")
    ap.add_argument("--offset", type=int, default=0, help="Starting offset for SBDB query")
    ap.add_argument("--include-magnitude", action="store_true", help="Include H magnitude in binary output (for brightness)")

    args = ap.parse_args()
    epoch = args.epoch or utc_now_iso()
    target_jd = iso_to_jd(epoch)
    
    is_binary = args.out.endswith('.bin')
    
    print(f"[INFO] ===== Asteroid Position from Orbital Elements =====")
    print(f"[INFO] Epoch: {epoch} (JD {target_jd:.2f})")
    print(f"[INFO] Asteroids to fetch: {args.asteroids}")
    print(f"[INFO] Batch size: {args.batch_size}")
    print(f"[INFO] Output file: {args.out} ({'BINARY' if is_binary else 'CSV'})")
    if is_binary:
        print(f"[INFO] Binary format: {'RA, DEC, Distance, H' if args.include_magnitude else 'RA, DEC, Distance'}")
    print(f"[INFO] ======================================================")

    all_asteroids = []
    total_requested = args.asteroids
    current_offset = args.offset
    
    # Fetch asteroids in batches
    while len(all_asteroids) < total_requested:
        remaining = total_requested - len(all_asteroids)
        batch_size = min(args.batch_size, remaining)
        
        try:
            batch = fetch_orbital_elements_batch(batch_size, current_offset, args.include_magnitude)
            if not batch:
                print(f"[WARN] No more asteroids available from SBDB")
                break
            
            all_asteroids.extend(batch)
            current_offset += batch_size
            print(f"[INFO] Total asteroids fetched: {len(all_asteroids)}/{total_requested}")
            
            # Small delay between batches to be nice to the API
            if len(all_asteroids) < total_requested:
                time.sleep(0.5)
                
        except Exception as e:
            print(f"[ERROR] Failed to fetch batch: {e}")
            break
    
    if not all_asteroids:
        print("[ERROR] No asteroids fetched!")
        return
    
    print(f"[INFO] ======================================================")
    print(f"[INFO] Computing positions for {len(all_asteroids)} asteroids...")
    
    # Process asteroids - collect data first
    asteroid_data = []
    processed = 0
    failed = 0
    start_time = time.time()
    
    for i, asteroid in enumerate(all_asteroids, 1):
        try:
            spkid = str(asteroid["spkid"])
            a = float(asteroid["a"])
            e = float(asteroid["e"])
            inc = float(asteroid["i"])
            omega = float(asteroid["om"])
            w = float(asteroid["w"])
            ma = float(asteroid["ma"])
            epoch_jd = float(asteroid["epoch"])
            
            # Compute position and velocity
            x, y, z, vx, vy, vz = orbital_elements_to_position(
                a, e, inc, omega, w, ma, epoch_jd, target_jd
            )
            
            # Convert to RA/DEC
            ra_deg, dec_deg, r_au = xyz_to_radec_deg(x, y, z)
            
            # Store data - keep distance in AU for solar system objects
            record = {
                'spkid': spkid,
                'ra_deg': ra_deg,
                'dec_deg': dec_deg,
                'distance_au': r_au,
                'vx': vx,
                'vy': vy,
                'vz': vz,
            }
            
            if args.include_magnitude and 'H' in asteroid and asteroid['H'] is not None:
                record['H'] = float(asteroid['H'])
            
            asteroid_data.append(record)
            processed += 1
            
        except Exception as e:
            failed += 1
            if failed <= 10:  # Only print first 10 errors
                print(f"[WARN] Failed to process asteroid {asteroid.get('spkid', 'unknown')}: {e}")
        
        # Progress reporting
        if i % 10000 == 0:
            elapsed = time.time() - start_time
            rate = i / elapsed if elapsed > 0 else 0
            print(f"[INFO] Progress: {i}/{len(all_asteroids)} ({100*i/len(all_asteroids):.1f}%) | "
                  f"Rate: {rate:.0f}/s | Success: {processed} | Failed: {failed}")
    
    total_time = time.time() - start_time
    
    # Write output based on format
    if is_binary:
        write_binary_output(args.out, asteroid_data, args.include_magnitude)
    else:
        write_csv_output(args.out, asteroid_data)
    
    print(f"[INFO] ======================================================")
    print(f"[INFO] Completed! Wrote {args.out}")
    print(f"[INFO] Successfully processed: {processed}/{len(all_asteroids)} asteroids")
    print(f"[INFO] Failed: {failed}")
    print(f"[INFO] Total time: {total_time:.1f}s ({total_time/processed:.3f}s per asteroid)")
    print(f"[INFO] Processing rate: {processed/total_time:.0f} asteroids/second")
    print(f"[INFO] ======================================================")


def write_binary_output(filename: str, asteroid_data: list[dict], include_magnitude: bool) -> None:
    """
    Write asteroid data in binary format for efficient Unity loading.
    
    Format:
    - Header: uint32 count (number of asteroids)
    - For each asteroid:
      - float32 ra_deg
      - float32 dec_deg
      - float32 distance_au (in Astronomical Units)
      - [optional] float32 H (magnitude)
    
    Total size per asteroid: 12 bytes (or 16 bytes with magnitude)
    """
    print(f"[INFO] Writing binary file: {filename}")
    
    record_size = 16 if include_magnitude else 12
    total_size = 4 + len(asteroid_data) * record_size
    print(f"[INFO] File size: {total_size / (1024*1024):.2f} MB ({len(asteroid_data)} asteroids × {record_size} bytes)")
    
    with open(filename, 'wb') as f:
        # Write header: number of asteroids (uint32, little-endian)
        f.write(struct.pack('<I', len(asteroid_data)))
        
        # Write asteroid records
        for asteroid in asteroid_data:
            # Pack as little-endian float32 (Unity standard)
            f.write(struct.pack('<f', asteroid['ra_deg']))
            f.write(struct.pack('<f', asteroid['dec_deg']))
            f.write(struct.pack('<f', asteroid['distance_au']))
            
            if include_magnitude:
                H = asteroid.get('H', 20.0)  # Default magnitude if missing
                f.write(struct.pack('<f', H))
    
    print(f"[INFO] Binary file written successfully")


def write_csv_output(filename: str, asteroid_data: list[dict]) -> None:
    """Write asteroid data in CSV format."""
    print(f"[INFO] Writing CSV file: {filename}")
    
    with open(filename, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        
        w.writerow([
            "source_id",
            "object_type",
            "ra_deg",
            "dec_deg",
            "distance_au",
            "vx_au_d",
            "vy_au_d",
            "vz_au_d",
            "speed_km_s",
        ])
        
        for asteroid in asteroid_data:
            sp_kms = speed_km_s(asteroid['vx'], asteroid['vy'], asteroid['vz'])
            
            w.writerow([
                asteroid['spkid'],
                "asteroid",
                f"{asteroid['ra_deg']:.9f}",
                f"{asteroid['dec_deg']:.9f}",
                f"{asteroid['distance_au']:.9f}",
                f"{asteroid['vx']:.12e}",
                f"{asteroid['vy']:.12e}",
                f"{asteroid['vz']:.12e}",
                f"{sp_kms:.9f}",
            ])
    
    print(f"[INFO] CSV file written successfully")


if __name__ == "__main__":
    main()
