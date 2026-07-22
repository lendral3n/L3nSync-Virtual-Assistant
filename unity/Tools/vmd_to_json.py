#!/usr/bin/env python3
"""
VMD → JSON converter untuk Unity runtime playback.

Parse Vocaloid Motion Data (.vmd) → output JSON dengan struktur:
{
  "name": "<file>",
  "frameCount": int,
  "duration": float (seconds, assume 30fps),
  "bones": {
    "<MMD bone name>": [
      {"frame": int, "translation": [x,y,z], "rotation": [x,y,z,w]},
      ...
    ]
  }
}

VMD binary format spec:
- Header (30 bytes): "Vocaloid Motion Data 0002\0" (Shift-JIS encoding)
- Model name (20 bytes): Shift-JIS string
- Bone frame count (4 bytes uint32)
- Per bone frame (111 bytes):
  - Bone name (15 bytes Shift-JIS)
  - Frame number (4 bytes uint32)
  - Translation x,y,z (12 bytes 3 float)
  - Rotation quat x,y,z,w (16 bytes 4 float)
  - Interpolation params (64 bytes — skip)

Usage:
    python3 vmd_to_json.py input.vmd output.json
"""

import struct
import json
import sys
import os


def read_string(data: bytes, length: int) -> str:
    """Read fixed-length Shift-JIS string, strip null + garbage."""
    raw = data[:length]
    # find first null byte
    null_idx = raw.find(b'\x00')
    if null_idx >= 0:
        raw = raw[:null_idx]
    try:
        return raw.decode('shift-jis').strip()
    except UnicodeDecodeError:
        # fallback latin-1 untuk debug
        return raw.decode('latin-1', errors='replace').strip()


def parse_vmd(filepath: str) -> dict:
    """Parse VMD file, return structured dict."""
    with open(filepath, 'rb') as f:
        data = f.read()

    offset = 0

    # Header check
    header = data[offset:offset+30]
    offset += 30
    if not header.startswith(b'Vocaloid Motion Data'):
        raise ValueError(f"Not a valid VMD file: {filepath}")

    # Model name (20 bytes)
    model_name = read_string(data[offset:offset+20], 20)
    offset += 20

    # Bone frame count
    bone_frame_count = struct.unpack('<I', data[offset:offset+4])[0]
    offset += 4

    print(f"  Model: {model_name}, Bone frames: {bone_frame_count}")

    bones = {}
    max_frame = 0

    for i in range(bone_frame_count):
        # Bone name (15 bytes Shift-JIS)
        bone_name = read_string(data[offset:offset+15], 15)
        offset += 15

        # Frame number (4 bytes uint32)
        frame_num = struct.unpack('<I', data[offset:offset+4])[0]
        offset += 4

        # Translation (3 floats LE)
        tx, ty, tz = struct.unpack('<fff', data[offset:offset+12])
        offset += 12

        # Rotation quaternion (4 floats LE)
        rx, ry, rz, rw = struct.unpack('<ffff', data[offset:offset+16])
        offset += 16

        # Skip interpolation (64 bytes)
        offset += 64

        if bone_name not in bones:
            bones[bone_name] = []

        bones[bone_name].append({
            'frame': frame_num,
            'translation': [tx, ty, tz],
            'rotation': [rx, ry, rz, rw]
        })

        if frame_num > max_frame:
            max_frame = frame_num

    # Sort each bone's keyframes by frame
    for bone_name in bones:
        bones[bone_name].sort(key=lambda x: x['frame'])

    return {
        'name': os.path.basename(filepath).replace('.vmd', ''),
        'modelName': model_name,
        'frameCount': max_frame + 1,
        'duration': (max_frame + 1) / 30.0,  # MMD = 30fps
        'totalKeyframes': bone_frame_count,
        'bones': bones
    }


def main():
    if len(sys.argv) < 3:
        print("Usage: python3 vmd_to_json.py <input.vmd> <output.json>")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]

    print(f"Parsing {input_path}...")
    result = parse_vmd(input_path)

    print(f"  → {len(result['bones'])} unique bones, {result['totalKeyframes']} keyframes, "
          f"{result['duration']:.2f}s @ 30fps")

    # Compact JSON (no indent for size)
    with open(output_path, 'w') as f:
        json.dump(result, f, separators=(',', ':'))

    out_size = os.path.getsize(output_path)
    print(f"  Wrote {output_path} ({out_size:,} bytes)")


if __name__ == '__main__':
    main()
