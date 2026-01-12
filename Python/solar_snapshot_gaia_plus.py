#!/usr/bin/env python3
"""
Solar system snapshot -> CSV in Gaia-like schema + rich metadata (all via APIs, no hardcoded body constants)

APIs used:
- JPL Horizons: snapshot vectors (position+velocity) and physical properties block (OBJ_DATA=YES)
  Docs: https://ssd.jpl.nasa.gov/doc/horizons.html
- JPL SBDB Query API: asteroid physical fields (diameter, albedo, H, rotation period where available)
  Docs: https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html

Output columns (Gaia-like + extras):
source_id, ra_deg, dec_deg, parallax_mas, distance_pc, phot_g_mean_mag, abs_mag_g,
size_km,
vx_au_d, vy_au_d, vz_au_d, speed_km_s,
gm_km3_s2, mass_kg, density_g_cm3, mean_radius_km, albedo, rot_per_hr, H

Notes:
- RA/DEC computed from heliocentric position vector in ICRF (equatorial) so it behaves like star catalogs.
- distance_pc is heliocentric distance (AU -> parsec).
- parallax_mas = 1000 / distance_pc (same definition as Gaia).
- size_km:
    - planets/moons: fetched from Horizons physical block (mean radius -> diameter when available; else blank)
    - asteroids: fetched from SBDB diameter when available (else blank)
- gravity-related metadata:
    - gm_km3_s2 fetched from Horizons when available
    - mass_kg derived from GM using gravitational constant G (a physical constant, not a body constant)
    - surface gravity can be derived downstream from GM and radius if you want (kept out to avoid schema bloat)
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import math
import os
import re
import time
from dataclasses import dataclass
from typing import Any, Optional

import requests


HORIZONS_URL = "https://ssd.jpl.nasa.gov/api/horizons.api"
SBDB_QUERY_URL = "https://ssd-api.jpl.nasa.gov/sbdb_query.api"

# constants (not body-specific)
AU_KM = 149_597_870.700  # exact-ish by definition used widely
DAY_S = 86_400.0
AU_PER_PARSEC = 206_264.80624709636
G_SI = 6.67430e-11  # m^3 kg^-1 s^-2 (CODATA 2018)


@dataclass(frozen=True)
class Body:
    name: str
    command: str     # Horizons COMMAND (e.g. '399' or 'DES=2000001;')
    source_id: str   # written to CSV source_id
    kind: str        # debug info


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
    if len(table) < 2:
        raise RuntimeError(f"Not enough CSV rows between $$SOE/$$EOE:\n{table}")

    header = [c.strip() for c in table[0].split(",")]
    row = [c.strip() for c in table[1].split(",")]
    return header, row


def horizons_vectors_icrf(command: str, epoch_utc: str, center: str, step: str = "1 d") -> dict[str, float]:
    """
    Get XYZ and VX,VY,VZ from Horizons at one epoch (AU and AU/day).
    Returns dict with keys x,y,z,vx,vy,vz (floats).
    """
    params = {
        "format": "json",
        "EPHEM_TYPE": "VECTORS",
        "MAKE_EPHEM": "YES",
        "OBJ_DATA": "NO",

        "COMMAND": f"'{command}'",
        "CENTER": f"'{center}'",

        # Gaia-like direction: equatorial inertial
        "REF_PLANE": "FRAME",
        "REF_SYSTEM": "ICRF",

        "OUT_UNITS": "AU-D",
        "CSV_FORMAT": "YES",

        "START_TIME": f"'{epoch_utc}'",
        "STOP_TIME": f"'{epoch_utc}'",
        "STEP_SIZE": f"'{step}'",

        # include velocity components in the CSV
        "VEC_TABLE": "2",
        "VEC_LABELS": "NO",
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
    # velocity column names can appear as VX, VY, VZ
    ivx, ivy, ivz = find_col("VX"), find_col("VY"), find_col("VZ")

    return {
        "x": float(row[ix]),
        "y": float(row[iy]),
        "z": float(row[iz]),
        "vx": float(row[ivx]),
        "vy": float(row[ivy]),
        "vz": float(row[ivz]),
    }


def horizons_obj_data(command: str) -> str:
    """
    Fetch Horizons object data block (physical properties etc.) as raw text.
    """
    params = {
        "format": "json",
        "MAKE_EPHEM": "NO",
        "OBJ_DATA": "YES",
        "COMMAND": f"'{command}'",
    }
    data = http_get_json(HORIZONS_URL, params=params)
    result = data.get("result", "")
    if not result:
        raise RuntimeError(f"Empty Horizons OBJ_DATA for {command}")
    return result


def _extract_float(pattern: str, text: str, flags: int = re.IGNORECASE) -> Optional[float]:
    m = re.search(pattern, text, flags)
    if not m:
        return None
    # normalize weird spacing and exponent forms
    s = m.group(1).strip().replace("D", "E")
    try:
        return float(s)
    except Exception:
        return None


def parse_physical_properties(obj_text: str) -> dict[str, Optional[float]]:
    """
    Parse a few common physical properties out of Horizons OBJ_DATA text.
    These are not guaranteed to exist for every object.
    Returns dict with keys:
      gm_km3_s2, mass_kg (if directly stated), density_g_cm3, mean_radius_km, albedo, rot_per_hr
    """
    # Try a few variations that appear in Horizons output across object classes
    gm = (
        _extract_float(r"GM\s*\(km\^3/s\^2\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        or _extract_float(r"GM\s*=\s*([0-9\.\+\-EeDd]+)\s*\(km\^3/s\^2\)", obj_text)
    )

    # Mass sometimes shown like: "Mass, 10^24 kg = 5.97219"
    # We try to capture both scaled and direct forms.
    mass_scaled = _extract_float(r"Mass[^=\n]*10\^(\d+)\s*kg\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
    # If the above regex doesn't work due to 2-group, handle with a manual parse:
    mass_kg = None
    m2 = re.search(r"Mass[^=\n]*10\^(\d+)\s*kg\s*=\s*([0-9\.\+\-EeDd]+)", obj_text, re.IGNORECASE)
    if m2:
        try:
            exp = int(m2.group(1))
            mant = float(m2.group(2).strip().replace("D", "E"))
            mass_kg = mant * (10 ** exp)
        except Exception:
            mass_kg = None
    else:
        # direct-ish mass form (rare)
        mass_kg = _extract_float(r"Mass\s*\(kg\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)

    density = (
        _extract_float(r"Density\s*\(g/cm\^3\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        or _extract_float(r"Density\s*=\s*([0-9\.\+\-EeDd]+)\s*\(g/cm\^3\)", obj_text)
    )

    # Mean radius often: "Mean radius (km) = 6371.008"
    mean_radius = (
        _extract_float(r"Mean\s+radius\s*\(km\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        or _extract_float(r"Radius\s*\(km\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
    )

    albedo = (
        _extract_float(r"Bond\s+albedo\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        or _extract_float(r"Geometric\s+albedo\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        or _extract_float(r"Albedo\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
    )

    # Rotation period: can appear in hours or days. We'll try hours first.
    rot_hr = _extract_float(r"Rot\.\s*period[^=\n]*\(h\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
    if rot_hr is None:
        rot_days = _extract_float(r"Rot\.\s*period[^=\n]*\(d\)\s*=\s*([0-9\.\+\-EeDd]+)", obj_text)
        if rot_days is not None:
            rot_hr = rot_days * 24.0

    return {
        "gm_km3_s2": gm,
        "mass_kg_direct": mass_kg,
        "density_g_cm3": density,
        "mean_radius_km": mean_radius,
        "albedo": albedo,
        "rot_per_hr": rot_hr,
    }


def gm_to_mass_kg(gm_km3_s2: float) -> float:
    # GM [km^3/s^2] -> [m^3/s^2] then /G
    gm_m3_s2 = gm_km3_s2 * 1e9
    return gm_m3_s2 / G_SI


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


def default_planets_and_major_moons() -> list[Body]:
    def b(name: str, naif: str, kind: str) -> Body:
        return Body(name=name, command=naif, source_id=naif, kind=kind)

    return [
        b("Sun", "10", "sun"),
        b("Mercury", "199", "planet"),
        b("Venus", "299", "planet"),
        b("Earth", "399", "planet"),
        b("Mars", "499", "planet"),
        b("Jupiter", "599", "planet"),
        b("Saturn", "699", "planet"),
        b("Uranus", "799", "planet"),
        b("Neptune", "899", "planet"),
        b("Pluto", "999", "dwarf_planet"),

        b("Moon", "301", "moon"),
        b("Phobos", "401", "moon"),
        b("Deimos", "402", "moon"),

        b("Io", "501", "moon"),
        b("Europa", "502", "moon"),
        b("Ganymede", "503", "moon"),
        b("Callisto", "504", "moon"),

        b("Mimas", "601", "moon"),
        b("Enceladus", "602", "moon"),
        b("Tethys", "603", "moon"),
        b("Dione", "604", "moon"),
        b("Rhea", "605", "moon"),
        b("Titan", "606", "moon"),
        b("Iapetus", "608", "moon"),

        b("Ariel", "701", "moon"),
        b("Umbriel", "702", "moon"),
        b("Titania", "703", "moon"),
        b("Oberon", "704", "moon"),
        b("Miranda", "705", "moon"),

        b("Triton", "801", "moon"),

        b("Charon", "901", "moon"),
        b("Nix", "902", "moon"),
        b("Hydra", "904", "moon"),
    ]


def sbdb_top_asteroids(limit: int, offset: int = 0) -> list[Body]:
    """
    Fetch asteroids (largest by diameter where known) and carry SBDB physical fields via a sidecar dict.
    We'll store SBDB diameter/albedo/H/rot_per later by looking them up in the SBDB rows we got.
    """
    params = {
        "fields": "spkid,full_name,diameter,albedo,H,rot_per",
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

    bodies: list[Body] = []
    for row in rows:
        rm = dict(zip(fields, row))
        spkid = str(rm.get("spkid", "")).strip()
        name = str(rm.get("full_name", spkid)).strip()
        if not spkid:
            continue
        cmd = f"DES={spkid};"
        bodies.append(Body(name=name, command=cmd, source_id=spkid, kind="asteroid"))
    return bodies


def sbdb_lookup_fields(spkid_list: list[str]) -> dict[str, dict[str, Optional[float]]]:
    """
    Batch-ish lookup via SBDB Query:
    we request a page sized to the set of SPKIDs we already selected and match by spkid.
    (SBDB Query doesn't do an explicit IN(spkid,...) filter cleanly for big lists, so we do a second query
     with 'spkid' filter if small; otherwise skip and rely on the first query fields if you integrate it.)
    For simplicity + robustness, we do per-object lookup only when needed would be too slow; instead,
    we use a single query by 'spkid' when list is short.

    If this feels too opinionated: keep asteroids count reasonable (<2000) and this works fine.
    """
    out: dict[str, dict[str, Optional[float]]] = {s: {} for s in spkid_list}

    # If very large, don't do any extra calls; user can extend later.
    if len(spkid_list) > 500:
        return out

    # SBDB Query supports filtering by "spkid" parameter.
    # We do one request per spkid to keep it simple and reliable (still okay for a few hundred).
    for spkid in spkid_list:
        try:
            params = {
                "fields": "spkid,diameter,albedo,H,rot_per",
                "spkid": spkid,
                "limit": "1",
            }
            data = http_get_json(SBDB_QUERY_URL, params=params)
            fields = data.get("fields", [])
            rows = data.get("data", [])
            if not fields or not rows:
                continue
            rm = dict(zip(fields, rows[0]))
            def f(key: str) -> Optional[float]:
                v = rm.get(key)
                if v in (None, "", "null"):
                    return None
                try:
                    return float(v)
                except Exception:
                    return None
            out[spkid] = {
                "diameter_km": f("diameter"),
                "albedo": f("albedo"),
                "H": f("H"),
                "rot_per_hr": f("rot_per"),
            }
        except Exception:
            continue

    return out


def fmt(x: Optional[float], fmt_str: str) -> str:
    if x is None:
        return ""
    try:
        return fmt_str.format(x)
    except Exception:
        return ""


def load_existing_progress(csv_path: str) -> set[str]:
    """
    Load existing progress from CSV file by reading source_id column.
    Returns set of source_ids that have already been processed.
    """
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
    ap.add_argument("--out", default="solar_snapshot_gaia_plus.csv", help="Output CSV")
    ap.add_argument("--center", default="500@10", help="Horizons CENTER, default heliocentric (Sun)")
    ap.add_argument("--rate-limit", type=float, default=0.2, help="Sleep seconds between Horizons calls")

    ap.add_argument("--asteroids", type=int, default=200, help="How many asteroids (largest-by-diameter via SBDB)")
    ap.add_argument("--asteroid-offset", type=int, default=0, help="SBDB paging offset")
    
    ap.add_argument("--resume", action="store_true", help="Resume from existing CSV (skip already processed bodies)")
    ap.add_argument("--progress-interval", type=int, default=10, help="Print progress every N bodies (default: 10)")

    args = ap.parse_args()
    epoch = args.epoch or utc_now_iso()

    # Load existing progress if resuming
    completed_ids: set[str] = set()
    if args.resume:
        completed_ids = load_existing_progress(args.out)
    
    bodies = default_planets_and_major_moons()
    asteroid_bodies: list[Body] = []
    if args.asteroids > 0:
        asteroid_bodies = sbdb_top_asteroids(args.asteroids, args.asteroid_offset)
        bodies.extend(asteroid_bodies)

    # Filter out already completed bodies if resuming
    if args.resume:
        original_count = len(bodies)
        bodies = [b for b in bodies if b.source_id not in completed_ids]
        skipped_count = original_count - len(bodies)
        print(f"[INFO] Resuming: skipping {skipped_count} already completed, processing {len(bodies)} remaining")
    
    if not bodies:
        print("[INFO] All bodies already processed!")
        return

    # Optional: enrich asteroid physical fields (diameter/albedo/H/rot)
    asteroid_phys = sbdb_lookup_fields([b.source_id for b in asteroid_bodies]) if asteroid_bodies else {}

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
                "phot_g_mean_mag",
                "abs_mag_g",
                "size_km",
                "vx_au_d",
                "vy_au_d",
                "vz_au_d",
                "speed_km_s",
                "gm_km3_s2",
                "mass_kg",
                "density_g_cm3",
                "mean_radius_km",
                "albedo",
                "rot_per_hr",
                "H",
            ])
        
        print(f"[INFO] Processing {len(bodies)} bodies, writing to {args.out} (mode: {file_mode})")

        processed_count = 0
        for i, b in enumerate(bodies, 1):
            # 1) snapshot vectors
            try:
                vec = horizons_vectors_icrf(b.command, epoch, args.center)
                x, y, z = vec["x"], vec["y"], vec["z"]
                vx, vy, vz = vec["vx"], vec["vy"], vec["vz"]

                ra_deg, dec_deg, r_au = xyz_to_radec_deg(x, y, z)
                dist_pc = au_to_pc(r_au)
                plx_mas = parallax_mas_from_pc(dist_pc)
                sp_kms = speed_km_s(vx, vy, vz)
            except Exception as e:
                print(f"[WARN] vectors failed for {b.name} ({b.command}): {e}")
                # write a mostly empty row and continue
                w.writerow([b.source_id, b.kind] + [""] * 18)
                f.flush()  # Ensure data is written immediately
                processed_count += 1
                if processed_count % args.progress_interval == 0:
                    print(f"[INFO] Progress: {processed_count}/{len(bodies)} ({100*processed_count/len(bodies):.1f}%)")
                time.sleep(args.rate_limit)
                continue

            # 2) physical properties via Horizons OBJ_DATA (works best for planets/moons)
            gm = mass_kg = density = mean_radius = albedo = rot_hr = None
            size_km = None

            # asteroid: prefer SBDB for size/albedo/H/rot; Horizons OBJ_DATA may be sparse
            H_val = None
            if b.kind == "asteroid":
                phys = asteroid_phys.get(b.source_id, {})
                size_km = phys.get("diameter_km")
                albedo = phys.get("albedo")
                rot_hr = phys.get("rot_per_hr")
                H_val = phys.get("H")

            # still try Horizons physicals (for planets/moons, and sometimes asteroids)
            try:
                obj = horizons_obj_data(b.command)
                props = parse_physical_properties(obj)
                gm = props["gm_km3_s2"]
                density = props["density_g_cm3"]
                mean_radius = props["mean_radius_km"]
                if albedo is None:
                    albedo = props["albedo"]
                if rot_hr is None:
                    rot_hr = props["rot_per_hr"]

                # size: if we have mean radius, convert to diameter as size_km if not already set
                if size_km is None and mean_radius is not None:
                    size_km = 2.0 * mean_radius

                # mass: use direct if present, else derive from GM
                mass_direct = props["mass_kg_direct"]
                if mass_direct is not None:
                    mass_kg = mass_direct
                elif gm is not None:
                    mass_kg = gm_to_mass_kg(gm)
            except Exception as e:
                # non-fatal
                print(f"[WARN] obj_data parse failed for {b.name} ({b.command}): {e}")

            w.writerow([
                b.source_id,
                b.kind,
                f"{ra_deg:.9f}",
                f"{dec_deg:.9f}",
                f"{plx_mas:.9f}",
                f"{dist_pc:.12e}",
                "",  # Gaia G band mag not applicable
                "",  # Gaia abs mag not applicable
                fmt(size_km, "{:.6f}"),
                f"{vx:.12e}",
                f"{vy:.12e}",
                f"{vz:.12e}",
                f"{sp_kms:.9f}",
                fmt(gm, "{:.9f}"),
                fmt(mass_kg, "{:.6e}"),
                fmt(density, "{:.6f}"),
                fmt(mean_radius, "{:.6f}"),
                fmt(albedo, "{:.6f}"),
                fmt(rot_hr, "{:.6f}"),
                fmt(H_val, "{:.6f}"),
            ])
            
            f.flush()  # Ensure data is written immediately
            processed_count += 1
            
            # Progress reporting
            if processed_count % args.progress_interval == 0 or processed_count == len(bodies):
                print(f"[INFO] Progress: {processed_count}/{len(bodies)} ({100*processed_count/len(bodies):.1f}%) - completed {b.name}")

            time.sleep(args.rate_limit)

    total_rows = len(completed_ids) + processed_count if args.resume else processed_count
    print(f"[INFO] Completed! Wrote {args.out} (total rows: {total_rows}, processed this run: {processed_count})")


if __name__ == "__main__":
    main()
