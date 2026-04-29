#!/usr/bin/env python3
"""
Script to append unique hashes to all mutation code entries in JSON files.
"""

import json
import hashlib
import sys
import re
from pathlib import Path


def remove_trailing_commas(json_str: str) -> str:
    """
    Remove trailing commas from JSON string (allows more lenient JSON parsing).
    """
    # Remove trailing commas before closing braces/brackets
    json_str = re.sub(r',\s*([}\]])', r'\1', json_str)
    return json_str


def generate_hash(code: str, mutation_index: int, variant_index: int) -> str:
    """
    Generate a unique hash based on the code and its position.
    Returns a short 8-character hex hash.
    """
    combined = f"{code}_{variant_index}_{mutation_index}"
    hash_obj = hashlib.sha256(combined.encode())
    return hash_obj.hexdigest()[:8]


def process_json_file(file_path: str) -> None:
    """
    Process a JSON file and append hashes to mutation codes.
    """
    file_path = Path(file_path)
    
    if not file_path.exists():
        print(f"Error: File '{file_path}' not found")
        sys.exit(1)
    
    # Read the JSON file
    with open(file_path, 'r', encoding='utf-8') as f:
        json_str = f.read()
    
    # Remove trailing commas to make it valid JSON
    json_str = remove_trailing_commas(json_str)
    
    # Parse the JSON
    data = json.loads(json_str)
    
    mutation_count = 0
    
    # Process each operation (assuming patch format)
    for op in data:
        if 'value' in op and isinstance(op['value'], list):
            # Process each variant
            for variant_idx, variant in enumerate(op['value']):
                if 'mutations' in variant and isinstance(variant['mutations'], list):
                    # Process each mutation
                    for mut_idx, mutation in enumerate(variant['mutations']):
                        if 'code' in mutation:
                            original_code = mutation['code']
                            hash_suffix = generate_hash(original_code, mut_idx, variant_idx)
                            mutation['code'] = f"{original_code}-{hash_suffix}"
                            mutation_count += 1
                            print(f"Updated: {original_code} → {mutation['code']}")
    
    # Write back to file with pretty formatting
    json_str = json.dumps(data, indent='\t', ensure_ascii=False)
    
    # Collapse numeric arrays to single line
    json_str = re.sub(
        r'\[\s*([0-9\s.,\-]+)\s*\]',
        lambda m: '[' + re.sub(r'\s+', ' ', m.group(1).strip()) + ']',
        json_str
    )
    
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(json_str)
    
    print(f"\n✓ Successfully updated {mutation_count} mutation codes in '{file_path}'")


def main():
    if len(sys.argv) < 2:
        print("Usage: python add_hashes_to_mutations.py <json_file_path>")
        print("\nExample:")
        print("  python add_hashes_to_mutations.py survival-worldgen-landforms.json")
        sys.exit(1)
    
    file_path = sys.argv[1]
    process_json_file(file_path)


if __name__ == "__main__":
    main()
