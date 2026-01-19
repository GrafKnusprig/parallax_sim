#!/usr/bin/env python3
"""
Analyze spatial coverage of Gaia GDR1 binary star dataset.
Determines what portion of the sky and Milky Way is covered.

Usage:
    python analyze_coverage.py
    python analyze_coverage.py --bin-file /path/to/gaia_stars.bin
"""

import argparse
import struct
import numpy as np
from pathlib import Path


def load_binary_stars(bin_file):
    """Load stars from binary file."""
    print(f"Loading binary file: {bin_file}")
    
    with open(bin_file, 'rb') as f:
        # Read header
        star_count = struct.unpack('<I', f.read(4))[0]
        print(f"Total stars in file: {star_count:,}")
        
        # Read all star data
        stars = np.zeros((star_count, 4), dtype=np.float32)
        for i in range(star_count):
            ra, dec, dist, mag = struct.unpack('<ffff', f.read(16))
            stars[i] = [ra, dec, dist, mag]
    
    return stars


def analyze_sky_coverage(stars):
    """Analyze sky coverage (RA and Dec distribution)."""
    ra = stars[:, 0]
    dec = stars[:, 1]
    
    print("\n" + "=" * 60)
    print("SKY COVERAGE ANALYSIS")
    print("=" * 60)
    
    # RA coverage
    ra_min, ra_max = ra.min(), ra.max()
    ra_range = ra_max - ra_min
    ra_coverage = (ra_range / 360.0) * 100
    
    print(f"\nRight Ascension (RA):")
    print(f"  Range: {ra_min:.2f}° to {ra_max:.2f}°")
    print(f"  Span: {ra_range:.2f}° ({ra_coverage:.1f}% of 360°)")
    
    # Dec coverage
    dec_min, dec_max = dec.min(), dec.max()
    dec_range = dec_max - dec_min
    dec_coverage = (dec_range / 180.0) * 100
    
    print(f"\nDeclination (Dec):")
    print(f"  Range: {dec_min:.2f}° to {dec_max:.2f}°")
    print(f"  Span: {dec_range:.2f}° ({dec_coverage:.1f}% of 180°)")
    
    # Sky fraction (solid angle)
    # Full sky = 4π steradians = 41253 square degrees
    # Approximation for rectangular patch: cos(dec_mid) * delta_ra * delta_dec
    dec_mid = (dec_min + dec_max) / 2.0
    sky_area_deg2 = np.abs(np.cos(np.radians(dec_mid)) * ra_range * dec_range)
    full_sky_deg2 = 41252.96  # 4π steradians in square degrees
    sky_fraction = (sky_area_deg2 / full_sky_deg2) * 100
    
    print(f"\nSky Area Coverage:")
    print(f"  Approximate area: {sky_area_deg2:.1f} square degrees")
    print(f"  Full sky: {full_sky_deg2:.1f} square degrees")
    print(f"  Coverage: {sky_fraction:.2f}% of full sky")
    
    # Check for RA wrap-around at 0/360°
    if ra_max - ra_min > 350:
        print(f"  Note: RA spans {ra_range:.1f}° - likely covers full RA range")
    
    return ra_min, ra_max, dec_min, dec_max, sky_fraction


def analyze_distance_distribution(stars):
    """Analyze distance distribution."""
    dist = stars[:, 2]
    
    print("\n" + "=" * 60)
    print("DISTANCE DISTRIBUTION")
    print("=" * 60)
    
    dist_min, dist_max = dist.min(), dist.max()
    dist_median = np.median(dist)
    dist_mean = np.mean(dist)
    
    print(f"\nDistance Statistics (parsecs):")
    print(f"  Minimum: {dist_min:.2f} pc")
    print(f"  Maximum: {dist_max:.2f} pc")
    print(f"  Mean: {dist_mean:.2f} pc")
    print(f"  Median: {dist_median:.2f} pc")
    
    # Distance percentiles
    percentiles = [10, 25, 50, 75, 90, 95, 99]
    print(f"\nDistance Percentiles:")
    for p in percentiles:
        val = np.percentile(dist, p)
        print(f"  {p}th percentile: {val:.2f} pc")
    
    # Distance bins
    bins = [0, 100, 250, 500, 1000, 2500, 5000, 10000, float('inf')]
    bin_labels = ['<100', '100-250', '250-500', '500-1k', '1k-2.5k', '2.5k-5k', '5k-10k', '>10k']
    
    print(f"\nDistance Distribution (parsecs):")
    for i in range(len(bins) - 1):
        count = np.sum((dist >= bins[i]) & (dist < bins[i+1]))
        pct = (count / len(dist)) * 100
        print(f"  {bin_labels[i]:>10} pc: {count:>7,} stars ({pct:>5.1f}%)")
    
    return dist_min, dist_max, dist_median


def convert_to_galactic(ra_deg, dec_deg):
    """Convert equatorial (RA, Dec) to galactic (l, b) coordinates."""
    # North Galactic Pole (J2000)
    ra_ngp = 192.859508  # degrees
    dec_ngp = 27.128336  # degrees
    l_ncp = 122.932     # galactic longitude of north celestial pole
    
    # Convert to radians
    ra = np.radians(ra_deg)
    dec = np.radians(dec_deg)
    ra_ngp_rad = np.radians(ra_ngp)
    dec_ngp_rad = np.radians(dec_ngp)
    l_ncp_rad = np.radians(l_ncp)
    
    # Calculate galactic latitude (b)
    sin_b = (np.sin(dec) * np.sin(dec_ngp_rad) + 
             np.cos(dec) * np.cos(dec_ngp_rad) * np.cos(ra - ra_ngp_rad))
    b = np.arcsin(sin_b)
    
    # Calculate galactic longitude (l)
    y = np.cos(dec) * np.sin(ra - ra_ngp_rad)
    x = (np.sin(dec) * np.cos(dec_ngp_rad) - 
         np.cos(dec) * np.sin(dec_ngp_rad) * np.cos(ra - ra_ngp_rad))
    l = l_ncp_rad - np.arctan2(y, x)
    
    # Convert to degrees and normalize
    l_deg = np.degrees(l)
    l_deg = l_deg % 360  # Normalize to [0, 360)
    b_deg = np.degrees(b)
    
    return l_deg, b_deg


def analyze_galactic_coverage(stars):
    """Analyze coverage in galactic coordinates."""
    ra = stars[:, 0]
    dec = stars[:, 1]
    
    print("\n" + "=" * 60)
    print("GALACTIC COORDINATE ANALYSIS")
    print("=" * 60)
    print("(Converting equatorial to galactic coordinates...)")
    
    l, b = convert_to_galactic(ra, dec)
    
    # Galactic longitude
    l_min, l_max = l.min(), l.max()
    l_range = l_max - l_min
    
    print(f"\nGalactic Longitude (l):")
    print(f"  Range: {l_min:.2f}° to {l_max:.2f}°")
    print(f"  Span: {l_range:.2f}° ({l_range/360*100:.1f}% of 360°)")
    
    # Galactic latitude
    b_min, b_max = b.min(), b.max()
    b_range = b_max - b_min
    
    print(f"\nGalactic Latitude (b):")
    print(f"  Range: {b_min:.2f}° to {b_max:.2f}°")
    print(f"  Span: {b_range:.2f}° ({b_range/180*100:.1f}% of 180°)")
    
    # Check coverage relative to galactic plane
    near_plane = np.sum(np.abs(b) < 10)  # Within ±10° of plane
    pct_near_plane = (near_plane / len(b)) * 100
    
    print(f"\nGalactic Plane Coverage:")
    print(f"  Stars within ±10° of plane: {near_plane:,} ({pct_near_plane:.1f}%)")
    
    # Latitude bins
    b_bins = [-90, -60, -30, -10, 10, 30, 60, 90]
    b_labels = ['<-60°', '-60 to -30°', '-30 to -10°', '-10 to +10°', 
                '+10 to +30°', '+30 to +60°', '>+60°']
    
    print(f"\nGalactic Latitude Distribution:")
    for i in range(len(b_bins) - 1):
        count = np.sum((b >= b_bins[i]) & (b < b_bins[i+1]))
        pct = (count / len(b)) * 100
        print(f"  {b_labels[i]:>15}: {count:>7,} stars ({pct:>5.1f}%)")


def analyze_magnitude_distribution(stars):
    """Analyze magnitude distribution."""
    mag = stars[:, 3]
    
    print("\n" + "=" * 60)
    print("MAGNITUDE DISTRIBUTION")
    print("=" * 60)
    
    mag_min, mag_max = mag.min(), mag.max()
    mag_median = np.median(mag)
    
    print(f"\nMagnitude Statistics:")
    print(f"  Brightest (min): {mag_min:.2f}")
    print(f"  Faintest (max): {mag_max:.2f}")
    print(f"  Median: {mag_median:.2f}")
    
    # Magnitude bins
    mag_bins = [-5, 0, 2, 4, 6, 8, 10, 12, 14, 16, 20, 25]
    
    print(f"\nMagnitude Distribution:")
    for i in range(len(mag_bins) - 1):
        count = np.sum((mag >= mag_bins[i]) & (mag < mag_bins[i+1]))
        pct = (count / len(mag)) * 100
        print(f"  {mag_bins[i]:>3} to {mag_bins[i+1]:>3}: {count:>7,} stars ({pct:>5.1f}%)")


def main():
    parser = argparse.ArgumentParser(
        description='Analyze spatial coverage of Gaia GDR1 binary star data',
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    parser.add_argument(
        '--bin-file',
        type=str,
        help='Path to gaia_stars.bin file (default: ../Assets/StreamingAssets/GDR1/gaia_stars.bin)'
    )
    
    args = parser.parse_args()
    
    # Default path
    if args.bin_file:
        bin_file = Path(args.bin_file)
    else:
        script_dir = Path(__file__).parent
        bin_file = script_dir / '..' / 'Assets' / 'StreamingAssets' / 'GDR1' / 'gaia_stars.bin'
    
    if not bin_file.exists():
        print(f"Error: Binary file not found: {bin_file}")
        print(f"Please specify the correct path with --bin-file")
        return 1
    
    # Load data
    stars = load_binary_stars(bin_file)
    
    # Run analyses
    analyze_sky_coverage(stars)
    analyze_distance_distribution(stars)
    analyze_galactic_coverage(stars)
    analyze_magnitude_distribution(stars)
    
    # Summary
    print("\n" + "=" * 60)
    print("SUMMARY")
    print("=" * 60)
    print(f"\nYour dataset contains {len(stars):,} stars.")
    print("\nTo determine exact Milky Way coverage, consider:")
    print("  - The Milky Way contains ~100-400 billion stars")
    print("  - Gaia DR1 cataloged ~1.1 billion sources")
    print("  - Your subset represents a specific region/magnitude limit")
    print("  - Coverage is determined by sky area AND distance range")
    
    return 0


if __name__ == '__main__':
    exit(main())
