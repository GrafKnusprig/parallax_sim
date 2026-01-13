#!/usr/bin/env python3
"""
Convert Gaia GDR1 CSV files to binary format for faster Unity loading.

Binary format (little-endian):
- Header: uint32 - total star count
- Records: float32 RA (deg), float32 DEC (deg), float32 Distance (pc), float32 Magnitude
- Each record: 16 bytes (4 floats × 4 bytes)

Usage:
    python gaia_csv_to_binary.py
    python gaia_csv_to_binary.py --input-dir /path/to/gaia/csvs --output-file stars.bin
"""

import argparse
import csv
import glob
import os
import struct
import sys
from pathlib import Path


def convert_csv_to_binary(csv_files, output_file, verbose=True):
    """
    Convert multiple Gaia CSV files to a single binary file.
    
    Args:
        csv_files: List of CSV file paths to convert
        output_file: Path to output binary file
        verbose: Print progress messages
    """
    total_stars = 0
    valid_stars = []
    
    # First pass: collect all valid star data
    if verbose:
        print(f"Reading {len(csv_files)} CSV files...")
    
    for csv_file in sorted(csv_files):
        if verbose:
            print(f"  Processing: {os.path.basename(csv_file)}")
        
        file_count = 0
        with open(csv_file, 'r', encoding='utf-8') as f:
            reader = csv.reader(f)
            next(reader)  # Skip header
            
            for row in reader:
                if len(row) < 7:
                    continue
                
                try:
                    # Parse: source_id,ra_deg,dec_deg,parallax_mas,distance_pc,phot_g_mean_mag,abs_mag_g
                    ra_deg = float(row[1])
                    dec_deg = float(row[2])
                    distance_pc = float(row[4])
                    magnitude = float(row[5])
                    
                    # Validate data
                    if distance_pc <= 0 or distance_pc != distance_pc:  # Check for NaN
                        continue
                    
                    valid_stars.append((ra_deg, dec_deg, distance_pc, magnitude))
                    file_count += 1
                    
                except (ValueError, IndexError):
                    continue
        
        if verbose:
            print(f"    Loaded {file_count:,} valid stars")
        total_stars += file_count
    
    if verbose:
        print(f"\nTotal valid stars: {total_stars:,}")
        print(f"Writing binary file: {output_file}")
    
    # Write binary file
    with open(output_file, 'wb') as f:
        # Write header: total count (uint32)
        f.write(struct.pack('<I', total_stars))
        
        # Write star records (4 float32s per star)
        batch_size = 10000
        for i, (ra, dec, dist, mag) in enumerate(valid_stars):
            f.write(struct.pack('<ffff', ra, dec, dist, mag))
            
            if verbose and (i + 1) % batch_size == 0:
                progress = (i + 1) / total_stars * 100
                print(f"  Progress: {i + 1:,}/{total_stars:,} ({progress:.1f}%)")
    
    # Calculate file size
    file_size = os.path.getsize(output_file)
    size_mb = file_size / (1024 * 1024)
    bytes_per_star = file_size / total_stars if total_stars > 0 else 0
    
    if verbose:
        print(f"\n✓ Conversion complete!")
        print(f"  Output file: {output_file}")
        print(f"  File size: {size_mb:.2f} MB")
        print(f"  Stars: {total_stars:,}")
        print(f"  Bytes per star: {bytes_per_star:.1f}")


def main():
    parser = argparse.ArgumentParser(
        description='Convert Gaia GDR1 CSV files to binary format for Unity',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Convert default Gaia files in StreamingAssets/GDR1
  python gaia_csv_to_binary.py
  
  # Specify custom input directory and output file
  python gaia_csv_to_binary.py --input-dir /path/to/gaia --output-file stars.bin
  
  # Quiet mode (no progress output)
  python gaia_csv_to_binary.py --quiet

Binary format:
  Header:  uint32 (4 bytes) - total star count
  Records: float32 RA, DEC, Distance, Magnitude (16 bytes each)
        """
    )
    
    parser.add_argument(
        '--input-dir',
        type=str,
        help='Directory containing Gaia CSV files (default: ../Assets/StreamingAssets/GDR1)'
    )
    
    parser.add_argument(
        '--output-file',
        type=str,
        default='gaia_stars.bin',
        help='Output binary file name (default: gaia_stars.bin)'
    )
    
    parser.add_argument(
        '--pattern',
        type=str,
        default='gaia_gdr1_homogen_subset_part*.csv',
        help='File pattern to match CSV files (default: gaia_gdr1_homogen_subset_part*.csv)'
    )
    
    parser.add_argument(
        '--quiet',
        action='store_true',
        help='Suppress progress output'
    )
    
    args = parser.parse_args()
    
    # Determine input directory
    if args.input_dir:
        input_dir = Path(args.input_dir)
    else:
        # Default: ../Assets/StreamingAssets/GDR1 relative to script location
        script_dir = Path(__file__).parent
        input_dir = script_dir.parent / 'Assets' / 'StreamingAssets' / 'GDR1'
    
    if not input_dir.exists():
        print(f"Error: Input directory not found: {input_dir}", file=sys.stderr)
        print(f"\nPlease specify the directory containing Gaia CSV files:", file=sys.stderr)
        print(f"  python {sys.argv[0]} --input-dir /path/to/gaia/csvs", file=sys.stderr)
        sys.exit(1)
    
    # Find CSV files
    csv_pattern = str(input_dir / args.pattern)
    csv_files = glob.glob(csv_pattern)
    
    if not csv_files:
        print(f"Error: No CSV files found matching pattern: {csv_pattern}", file=sys.stderr)
        print(f"\nAvailable files in {input_dir}:", file=sys.stderr)
        for f in sorted(input_dir.glob('*.csv'))[:10]:
            print(f"  {f.name}", file=sys.stderr)
        sys.exit(1)
    
    # Determine output file path
    output_file = input_dir / args.output_file
    
    if not args.quiet:
        print("Gaia CSV to Binary Converter")
        print("=" * 60)
        print(f"Input directory: {input_dir}")
        print(f"CSV files found: {len(csv_files)}")
        print(f"Output file: {output_file}")
        print("=" * 60)
        print()
    
    # Convert
    try:
        convert_csv_to_binary(csv_files, output_file, verbose=not args.quiet)
    except KeyboardInterrupt:
        print("\n\nConversion cancelled by user.", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"\nError during conversion: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
