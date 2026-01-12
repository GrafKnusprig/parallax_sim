#!/usr/bin/env python3
"""
NAIF ID to Name Mapper
Reads CSV datasets and creates a mapping file of NAIF IDs to object names
using the JPL Horizons API.

Usage:
    python naif_id_mapper.py <csv_file> [output_json]
    
Example:
    python naif_id_mapper.py ../Assets/StreamingAssets/PlanetDatasetPlus/solar_dataset_plus.csv naif_names.json
"""

import csv
import json
import sys
import time
from pathlib import Path
from typing import Dict, Set
import urllib.request
import urllib.parse
import urllib.error


class HorizonsAPI:
    """Simple wrapper for JPL Horizons API queries"""
    
    BASE_URL = "https://ssd.jpl.nasa.gov/api/horizons.api"
    
    @staticmethod
    def get_object_name(naif_id: int) -> str:
        """
        Query Horizons API for object name by NAIF ID
        
        Args:
            naif_id: NAIF integer ID
            
        Returns:
            Object name string, or fallback string if not found
        """
        # Horizons API parameters
        params = {
            'format': 'text',
            'COMMAND': str(naif_id),
            'OBJ_DATA': 'NO',
            'MAKE_EPHEM': 'NO'
        }
        
        url = f"{HorizonsAPI.BASE_URL}?{urllib.parse.urlencode(params)}"
        
        try:
            with urllib.request.urlopen(url, timeout=10) as response:
                data = response.read().decode('utf-8')
                
                # Parse the response to extract object name
                # Look for "Target body name:" or similar patterns
                for line in data.split('\n'):
                    line = line.strip()
                    
                    # Pattern 1: "Target body name: NAME"
                    if 'Target body name:' in line:
                        parts = line.split(':', 1)
                        if len(parts) > 1:
                            name = parts[1].strip()
                            # Clean up extra info in parentheses or brackets
                            name = name.split('{')[0].split('(')[0].strip()
                            return name
                    
                    # Pattern 2: Look for object designation
                    if line.startswith('Target body name:') or line.startswith('Revised:'):
                        continue
                        
                # If no name found in expected format, return unknown
                return f"Unknown_{naif_id}"
                
        except urllib.error.HTTPError as e:
            if e.code == 404:
                print(f"  ⚠ ID {naif_id} not found in Horizons")
                return f"Unknown_{naif_id}"
            else:
                print(f"  ✗ HTTP Error {e.code} for ID {naif_id}")
                return f"Error_{naif_id}"
                
        except Exception as e:
            print(f"  ✗ Error querying ID {naif_id}: {e}")
            return f"Error_{naif_id}"


def read_naif_ids_from_csv(csv_path: Path) -> Set[int]:
    """
    Extract all unique NAIF IDs from a CSV dataset
    
    Args:
        csv_path: Path to CSV file with 'source_id' column
        
    Returns:
        Set of unique NAIF IDs
    """
    naif_ids = set()
    
    try:
        with open(csv_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            
            for row in reader:
                if 'source_id' in row:
                    try:
                        naif_id = int(row['source_id'])
                        naif_ids.add(naif_id)
                    except (ValueError, TypeError):
                        continue
                        
    except Exception as e:
        print(f"Error reading CSV {csv_path}: {e}")
        sys.exit(1)
        
    return naif_ids


def create_naif_mapping(naif_ids: Set[int], use_cache: bool = True) -> Dict[int, str]:
    """
    Create a mapping of NAIF IDs to names using Horizons API
    
    Args:
        naif_ids: Set of NAIF IDs to look up
        use_cache: If True, use hardcoded cache for common bodies (faster)
        
    Returns:
        Dictionary mapping NAIF ID -> name
    """
    mapping = {}
    
    # Hardcoded cache for common solar system bodies (to reduce API calls)
    COMMON_BODIES = {
        10: "Sun",
        199: "Mercury",
        299: "Venus",
        399: "Earth",
        301: "Moon",
        401: "Phobos",
        402: "Deimos",
        499: "Mars",
        5: "Jupiter",
        501: "Io",
        502: "Europa",
        503: "Ganymede",
        504: "Callisto",
        599: "Jupiter",
        601: "Mimas",
        602: "Enceladus",
        603: "Tethys",
        604: "Dione",
        605: "Rhea",
        606: "Titan",
        608: "Iapetus",
        699: "Saturn",
        701: "Ariel",
        702: "Umbriel",
        703: "Titania",
        704: "Oberon",
        705: "Miranda",
        799: "Uranus",
        801: "Triton",
        899: "Neptune",
        901: "Charon",
        902: "Nix",
        904: "Kerberos",
        999: "Pluto",
    }
    
    sorted_ids = sorted(naif_ids)
    total = len(sorted_ids)
    
    print(f"\n📊 Processing {total} unique NAIF IDs...")
    print("=" * 60)
    
    for idx, naif_id in enumerate(sorted_ids, 1):
        # Check cache first
        if use_cache and naif_id in COMMON_BODIES:
            name = COMMON_BODIES[naif_id]
            mapping[naif_id] = name
            print(f"[{idx:3d}/{total}] ID {naif_id:8d} -> {name:30s} (cached)")
        else:
            # Query API
            print(f"[{idx:3d}/{total}] ID {naif_id:8d} -> Querying API...", end=' ')
            name = HorizonsAPI.get_object_name(naif_id)
            mapping[naif_id] = name
            print(f"{name}")
            
            # Be nice to the API - add a small delay
            time.sleep(0.5)
    
    print("=" * 60)
    print(f"✓ Mapping complete: {len(mapping)} objects\n")
    
    return mapping


def save_mapping(mapping: Dict[int, str], output_path: Path):
    """
    Save NAIF ID mapping to JSON file
    
    Args:
        mapping: Dictionary of NAIF ID -> name
        output_path: Path to output JSON file
    """
    # Convert int keys to strings for JSON compatibility
    json_mapping = {str(k): v for k, v in sorted(mapping.items())}
    
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(json_mapping, f, indent=2, ensure_ascii=False)
    
    print(f"💾 Saved mapping to: {output_path}")
    print(f"   Total entries: {len(json_mapping)}")


def main():
    """Main entry point"""
    
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    
    csv_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2] if len(sys.argv) > 2 else "naif_id_mapping.json")
    
    if not csv_path.exists():
        print(f"Error: CSV file not found: {csv_path}")
        sys.exit(1)
    
    print(f"🔍 Reading NAIF IDs from: {csv_path}")
    naif_ids = read_naif_ids_from_csv(csv_path)
    
    if not naif_ids:
        print("No NAIF IDs found in CSV!")
        sys.exit(1)
    
    # Create mapping using API
    mapping = create_naif_mapping(naif_ids, use_cache=True)
    
    # Save to file
    save_mapping(mapping, output_path)
    
    print("\n✓ Done!")


if __name__ == "__main__":
    main()
