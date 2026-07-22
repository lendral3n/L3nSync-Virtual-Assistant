#!/usr/bin/env python3
"""
Filter VMD JSON ke humanoid bones only + downsample keyframes untuk size reduction.

Target: 50-200KB per JSON (down from 0.5-4MB), playback-ready di Unity runtime.

MMD bone names (Japanese) → HumanBodyBones (Unity):
"""

import json
import sys
import os

# MMD Japanese bone name → Unity HumanBodyBones (string enum)
MMD_TO_HUMANOID = {
    # Core
    'センター': 'Hips',         # Center (hip pivot)
    '下半身': 'Hips',           # Lower body (alt hip)
    '上半身': 'Spine',          # Upper body
    '上半身2': 'Chest',         # Upper body 2 (chest)
    '首': 'Neck',
    '頭': 'Head',
    '両目': 'Eye_Center',       # Both eyes (custom — split di playback)

    # Left arm
    '左肩': 'LeftShoulder',
    '左腕': 'LeftUpperArm',
    '左ひじ': 'LeftLowerArm',
    '左手首': 'LeftHand',

    # Right arm
    '右肩': 'RightShoulder',
    '右腕': 'RightUpperArm',
    '右ひじ': 'RightLowerArm',
    '右手首': 'RightHand',

    # Left leg
    '左足': 'LeftUpperLeg',
    '左ひざ': 'LeftLowerLeg',
    '左足首': 'LeftFoot',
    '左つま先': 'LeftToes',

    # Right leg
    '右足': 'RightUpperLeg',
    '右ひざ': 'RightLowerLeg',
    '右足首': 'RightFoot',
    '右つま先': 'RightToes',

    # Left fingers (MMD has thumb 0 = metacarpal, 1 = proximal, 2 = intermediate)
    '左親指０': 'LeftThumbProximal',
    '左親指1': 'LeftThumbIntermediate',
    '左親指2': 'LeftThumbDistal',
    '左人指１': 'LeftIndexProximal',
    '左人指２': 'LeftIndexIntermediate',
    '左人指３': 'LeftIndexDistal',
    '左中指１': 'LeftMiddleProximal',
    '左中指２': 'LeftMiddleIntermediate',
    '左中指３': 'LeftMiddleDistal',
    '左薬指１': 'LeftRingProximal',
    '左薬指２': 'LeftRingIntermediate',
    '左薬指３': 'LeftRingDistal',
    '左小指１': 'LeftLittleProximal',
    '左小指２': 'LeftLittleIntermediate',
    '左小指３': 'LeftLittleDistal',

    # Right fingers
    '右親指０': 'RightThumbProximal',
    '右親指1': 'RightThumbIntermediate',
    '右親指2': 'RightThumbDistal',
    '右人指１': 'RightIndexProximal',
    '右人指２': 'RightIndexIntermediate',
    '右人指３': 'RightIndexDistal',
    '右中指１': 'RightMiddleProximal',
    '右中指２': 'RightMiddleIntermediate',
    '右中指３': 'RightMiddleDistal',
    '右薬指１': 'RightRingProximal',
    '右薬指２': 'RightRingIntermediate',
    '右薬指３': 'RightRingDistal',
    '右小指１': 'RightLittleProximal',
    '右小指２': 'RightLittleIntermediate',
    '右小指３': 'RightLittleDistal',
}

# Skip these MMD-specific bones (IK targets, helper bones, dll yang tidak ada di humanoid)
SKIP_BONES = {'左足ＩＫ', '右足ＩＫ', 'センター先', '上半身先', '全ての親'}


def filter_optimize(input_path: str, output_path: str,
                    max_duration: float = 8.0,
                    sample_rate: int = 2):
    """
    Filter humanoid bones + trim ke first max_duration seconds + downsample.

    sample_rate: 1 = keep all keyframes, 2 = every other, 3 = every 3rd, dll.
    """
    with open(input_path, 'r') as f:
        data = json.load(f)

    max_frame = int(max_duration * 30)  # 30fps
    filtered_bones = {}
    mapping_used = {}

    for mmd_name, frames in data['bones'].items():
        if mmd_name in SKIP_BONES:
            continue
        if mmd_name not in MMD_TO_HUMANOID:
            continue

        humanoid_name = MMD_TO_HUMANOID[mmd_name]

        # Trim ke max_frame + downsample
        trimmed = [f for f in frames if f['frame'] <= max_frame]
        sampled = trimmed[::sample_rate]

        if not sampled:
            continue

        # Round float values untuk size reduction (3 decimal places)
        compressed = [{
            'frame': kf['frame'],
            't': [round(v, 3) for v in kf['translation']],
            'r': [round(v, 4) for v in kf['rotation']]
        } for kf in sampled]

        filtered_bones[humanoid_name] = compressed
        mapping_used[mmd_name] = humanoid_name

    out = {
        'name': data['name'],
        'modelName': data.get('modelName', ''),
        'duration': min(max_duration, data['duration']),
        'frameRate': 30,
        'sampleRate': sample_rate,
        'bones': filtered_bones,
        '_mapping': mapping_used  # debug info
    }

    with open(output_path, 'w') as f:
        json.dump(out, f, separators=(',', ':'))

    out_size = os.path.getsize(output_path)
    print(f"  {len(filtered_bones)} humanoid bones, "
          f"{sum(len(b) for b in filtered_bones.values())} total kf, "
          f"{out_size:,} bytes")


def main():
    vmd_dir = '/Users/lendra/Documents/Projects/L/OC-X1/Assets/StreamingAssets/VMD'
    output_dir = '/Users/lendra/Documents/Projects/L/OC-X1/Assets/StreamingAssets/Anim'
    os.makedirs(output_dir, exist_ok=True)

    files = [f for f in os.listdir(vmd_dir) if f.endswith('.json') and not f.startswith('_')]

    # Per-file config: max_duration (s), sample_rate
    configs = {
        'walk.json': (5.0, 2),       # 5s walk loop
        'foxsay.json': (8.0, 2),     # 8s fox-style
        'nekomimi.json': (10.0, 3),  # 10s cat-style longer
        'fuwari.json': (8.0, 3),     # 8s gentle
        'heartbeat.json': (6.0, 3),  # 6s heartbeat
        'baby.json': (8.0, 3),       # 8s sample
    }

    for f in files:
        if f not in configs:
            continue
        max_dur, sample = configs[f]
        in_path = os.path.join(vmd_dir, f)
        out_path = os.path.join(output_dir, f)
        print(f"Optimizing {f} (duration={max_dur}s, sample 1/{sample})...")
        filter_optimize(in_path, out_path, max_dur, sample)

    print("\nFinal output sizes:")
    for f in os.listdir(output_dir):
        size = os.path.getsize(os.path.join(output_dir, f))
        print(f"  {f}: {size:,} bytes")


if __name__ == '__main__':
    main()
