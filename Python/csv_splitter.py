#!/usr/bin/env python3
"""
CSV File Splitter

This script splits large CSV files into smaller files of approximately 50MB each.
The script preserves the header row in each output file and maintains data integrity.

Usage:
    python csv_splitter.py <input_csv_file>
    
Example:
    python csv_splitter.py large_dataset.csv
"""

import os
import sys
import csv
from pathlib import Path


def get_file_size_mb(filepath):
    """Get file size in megabytes."""
    return os.path.getsize(filepath) / (1024 * 1024)


def split_csv(input_file, max_size_mb=50):
    """
    Split a CSV file into smaller files of approximately max_size_mb.
    
    Args:
        input_file (str): Path to the input CSV file
        max_size_mb (int): Maximum size of each output file in MB (default: 50)
    
    Returns:
        list: List of output file paths created
    """
    input_path = Path(input_file)
    
    if not input_path.exists():
        raise FileNotFoundError(f"Input file not found: {input_file}")
    
    print(f"Splitting {input_file} into files of ~{max_size_mb}MB each...")
    print(f"Original file size: {get_file_size_mb(input_file):.2f} MB")
    
    # Prepare output directory and file naming
    output_dir = input_path.parent
    base_name = input_path.stem
    extension = input_path.suffix
    
    output_files = []
    max_size_bytes = max_size_mb * 1024 * 1024
    
    with open(input_file, 'r', newline='', encoding='utf-8') as infile:
        reader = csv.reader(infile)
        
        # Read header
        try:
            header = next(reader)
        except StopIteration:
            print("Error: CSV file is empty")
            return []
        
        file_count = 1
        current_size = 0
        current_rows = []
        
        # Calculate approximate size of header
        header_size = len(','.join(header).encode('utf-8')) + 1  # +1 for newline
        
        for row in reader:
            # Calculate row size in bytes (approximate)
            row_size = len(','.join(row).encode('utf-8')) + 1  # +1 for newline
            
            # If adding this row would exceed the limit, write current batch
            if current_size + row_size > max_size_bytes and current_rows:
                output_file = output_dir / f"{base_name}_part{file_count:03d}{extension}"
                write_csv_chunk(output_file, header, current_rows)
                output_files.append(str(output_file))
                
                print(f"Created: {output_file.name} ({get_file_size_mb(output_file):.2f} MB, {len(current_rows):,} rows)")
                
                # Reset for next file
                file_count += 1
                current_rows = []
                current_size = header_size
            
            current_rows.append(row)
            current_size += row_size
        
        # Write remaining rows
        if current_rows:
            output_file = output_dir / f"{base_name}_part{file_count:03d}{extension}"
            write_csv_chunk(output_file, header, current_rows)
            output_files.append(str(output_file))
            
            print(f"Created: {output_file.name} ({get_file_size_mb(output_file):.2f} MB, {len(current_rows):,} rows)")
    
    print(f"\nSplit complete! Created {len(output_files)} files.")
    return output_files


def write_csv_chunk(output_file, header, rows):
    """Write header and rows to a CSV file."""
    with open(output_file, 'w', newline='', encoding='utf-8') as outfile:
        writer = csv.writer(outfile)
        writer.writerow(header)
        writer.writerows(rows)


def main():
    """Main function to handle command line arguments and execute splitting."""
    if len(sys.argv) != 2:
        print("Usage: python csv_splitter.py <input_csv_file>")
        print("Example: python csv_splitter.py large_dataset.csv")
        sys.exit(1)
    
    input_file = sys.argv[1]
    
    try:
        split_csv(input_file)
        print("CSV splitting completed successfully!")
    except FileNotFoundError as e:
        print(f"Error: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"An error occurred: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()