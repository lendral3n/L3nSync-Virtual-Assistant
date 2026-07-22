# Kohaku VRM — Complete Bone Mapper

**Foundation document untuk semua animation work.**

Data extracted **directly dari `Kohaku_dress_1.10_VRM.vrm` glTF binary** (lihat [kohaku-bones.json](kohaku-bones.json) untuk machine-readable form). Bukan asumsi — actual values yang model define.

---

## TL;DR — Quick Reference

- **53 humanoid bones** + 213 secondary bones (hair, dress, accessories) = **266 total nodes**
- **Naming convention:** Mixamo-style (`LeftArm`, `LeftForeArm`, `Hips`) — BUKAN VRoid `J_Bip_L_UpperArm`
- **All initial rotations = identity** `(0,0,0,1)` — T-pose dengan no pre-rotation per bone
- **Hip pivot** di world Y = 0.792 (model spawned di y=0 → hip at world y=0.792)
- **No UpperChest** — Kohaku model TIDAK punya `upperChest` (mapping returns null, fallback ke Chest)
- **Local axis convention:** identity rotation berarti **local axes = world axes**. Karena translation `LeftArm = (-0.045, 0, 0)`, arm pointing local **-X direction**.

---

## 1. Bone Hierarchy Diagram

```
Hips                              [world (0, 0.792, 0)]
├── LeftUpLeg                     [-0.063, -0.081, 0]  → leftUpperLeg
│   └── LeftLeg                   [0, -0.323, 0]       → leftLowerLeg
│       └── LeftFoot              [0, -0.327, 0.024]   → leftFoot
│           └── LeftToeBase       [0, -0.073, -0.040]  → leftToes
│
├── RightUpLeg                    [0.063, -0.081, 0]   → rightUpperLeg
│   └── RightLeg                  [0, -0.323, 0]
│       └── RightFoot             [0, -0.327, 0.024]
│           └── RightToeBase      [0, -0.073, -0.040]
│
└── Spine                         [0, 0.013, 0]        → spine
    └── Chest                     [0, 0.086, -0.006]   → chest (no UpperChest!)
        ├── Neck                  [0, 0.198, 0.024]    → neck
        │   └── Head              [0, 0.037, -0.003]   → head
        │       ├── LeftEye       [-0.033, 0.055, -0.005]
        │       └── RightEye      [0.033, 0.055, -0.005]
        │
        ├── LeftShoulder          [-0.036, 0.164, 0.023]
        │   └── LeftArm           [-0.045, 0, 0]       → leftUpperArm
        │       └── LeftForeArm   [-0.208, 0, 0.005]   → leftLowerArm
        │           └── LeftHand  [-0.175, 0, -0.005]  → leftHand
        │               ├── LeftHandThumb1   → LeftHandThumb2 → LeftHandThumb3
        │               ├── LeftHandIndex1   → LeftHandIndex2 → LeftHandIndex3
        │               ├── LeftHandMiddle1  → LeftHandMiddle2 → LeftHandMiddle3
        │               ├── LeftHandRing1    → LeftHandRing2 → LeftHandRing3
        │               └── LeftHandPinky1   → LeftHandPinky2 → LeftHandPinky3
        │
        └── RightShoulder         [0.036, 0.164, 0.023]
            └── RightArm          [0.045, 0, 0]
                └── RightForeArm  [0.208, 0, 0.005]
                    └── RightHand [0.175, 0, -0.005]
                        ├── RightHandThumb1 → 2 → 3
                        ├── RightHandIndex1 → 2 → 3
                        ├── RightHandMiddle1 → 2 → 3
                        ├── RightHandRing1 → 2 → 3
                        └── RightHandPinky1 → 2 → 3
```

**Translations dalam METER, relative ke parent.** Rotation semua identity.

---

## 2. Complete Bone Table

Lengkap 53 humanoid bones dengan name actual + index + initial transform.

### 2.1 Body Core (10 bones)

| HumanBodyBones | Name (Kohaku) | Node Idx | Parent | Translation (relative) |
|----------------|----------------|----------|--------|------------------------|
| `Hips` | `Hips` | 1 | (root) | (0, 0.792, 0) — world position |
| `Spine` | `Spine` | 74 | Hips | (0, 0.013, 0) — slight up |
| `Chest` | `Chest` | 75 | Spine | (0, 0.086, -0.006) — up + slight back |
| **`UpperChest`** | _(NOT PRESENT)_ | — | — | Kohaku tidak punya, mapper returns null |
| `Neck` | `Neck` | 105 | Chest | (0, 0.198, 0.024) — up + slight forward |
| `Head` | `Head` | 106 | Neck | (0, 0.037, -0.003) — up |
| `LeftEye` | `LeftEye` | 209 | Head | (-0.033, 0.055, -0.005) |
| `RightEye` | `RightEye` | 213 | Head | (0.033, 0.055, -0.005) |
| `Jaw` | _(NOT PRESENT)_ | — | — | — |

### 2.2 Left Arm Chain (4 bones, excluding fingers)

| HumanBodyBones | Name | Node Idx | Parent | Translation (relative) |
|----------------|------|----------|--------|------------------------|
| `LeftShoulder` | `LeftShoulder` | 78 | Chest | (-0.036, 0.164, 0.023) |
| `LeftUpperArm` | `LeftArm` | 79 | LeftShoulder | (-0.045, 0, 0) — pointing -X |
| `LeftLowerArm` | `LeftForeArm` | 80 | LeftArm | (-0.208, 0, 0.005) — continue -X (arm length 20.8cm) |
| `LeftHand` | `LeftHand` | 84 | LeftForeArm | (-0.175, 0, -0.005) — continue -X (forearm length 17.5cm) |

⚠️ **CRITICAL:** Arm pointing -X (LEFT side dari character). Local axis convention =
- **Local X**: along arm length (NEGATIVE direction = bone forward / outward)
- **Local Y**: perpendicular up (Y axis normal)
- **Local Z**: perpendicular forward (Z axis normal)

Untuk swing arm DOWN (rotate -X to -Y world):
- Rotate around **local Z axis by 90°** (positive = CCW looking down -Z)
- Math: rotate -X by 90° around Z → -Y ✅
- Code: `leftArm.localRotation = Quaternion.Euler(0, 0, 90)` (assuming parent identity)

### 2.3 Right Arm Chain (4 bones)

Mirror dari Left — translation X **positive** (pointing +X = right side).

| HumanBodyBones | Name | Translation (relative) |
|----------------|------|------------------------|
| `RightShoulder` | `RightShoulder` | (0.036, 0.164, 0.023) |
| `RightUpperArm` | `RightArm` | (0.045, 0, 0) — pointing +X |
| `RightLowerArm` | `RightForeArm` | (0.208, 0, 0.005) |
| `RightHand` | `RightHand` | (0.175, 0, -0.005) |

Untuk swing right arm DOWN: `rightArm.localRotation = Quaternion.Euler(0, 0, -90)` (negative Z, mirror).

### 2.4 Left Leg Chain (4 bones)

| HumanBodyBones | Name | Node Idx | Translation (relative) |
|----------------|------|----------|------------------------|
| `LeftUpperLeg` | `LeftUpLeg` | 2 | (-0.063, -0.081, 0) |
| `LeftLowerLeg` | `LeftLeg` | 3 | (0, -0.323, -0.000) — knee 32cm down |
| `LeftFoot` | `LeftFoot` | 4 | (0, -0.327, 0.024) — ankle 32cm down |
| `LeftToes` | `LeftToeBase` | 5 | (0, -0.073, -0.040) |

**Total leg length:** 0.081 + 0.323 + 0.327 = ~0.731m (hip to ankle)

### 2.5 Right Leg Chain (4 bones)

Mirror Left — `RightUpLeg` translation (0.063, -0.081, 0).

### 2.6 Left Hand Fingers (15 bones)

Fingers attached ke `LeftHand`. Each finger has 3 phalanx (Proximal → Intermediate → Distal).

| HumanBodyBones | Name | Translation (relative ke parent finger) |
|----------------|------|-----------------------------------------|
| `LeftThumbProximal` | `LeftHandThumb1` | (-0.020, -0.007, -0.020) |
| `LeftThumbIntermediate` | `LeftHandThumb2` | (-0.026, -0.006, -0.016) |
| `LeftThumbDistal` | `LeftHandThumb3` | (-0.024, -0.005, -0.016) |
| `LeftIndexProximal` | `LeftHandIndex1` | (-0.074, 0.001, -0.020) |
| `LeftIndexIntermediate` | `LeftHandIndex2` | (-0.027, 0, -0.001) |
| `LeftIndexDistal` | `LeftHandIndex3` | (-0.017, 0, -0.001) |
| `LeftMiddleProximal` | `LeftHandMiddle1` | (-0.072, 0.002, -0.004) |
| `LeftMiddleIntermediate` | `LeftHandMiddle2` | (-0.033, 0, 0) |
| `LeftMiddleDistal` | `LeftHandMiddle3` | (-0.021, 0, 0) |
| `LeftRingProximal` | `LeftHandRing1` | (-0.070, 0, 0.012) |
| `LeftRingIntermediate` | `LeftHandRing2` | (-0.030, 0, 0) |
| `LeftRingDistal` | `LeftHandRing3` | (-0.020, 0, 0) |
| `LeftLittleProximal` | `LeftHandPinky1` | (-0.066, -0.004, 0.026) |
| `LeftLittleIntermediate` | `LeftHandPinky2` | (-0.022, 0, 0.002) |
| `LeftLittleDistal` | `LeftHandPinky3` | (-0.013, 0, 0.001) |

**Finger axis observation:**
- Translation X negative = finger pointing **-X** (continuing arm direction)
- Z component varies: index slightly negative, middle ~0, ring slight positive, pinky most positive — ini spread finger di T-pose

**Curl axis:** For finger to curl ke palm (toward -Z world saat T-pose), rotate around **local Z axis** dengan negative value (untuk left hand). Test required.

### 2.7 Right Hand Fingers (15 bones)

Mirror Left — translation X **positive**, Z mirror per finger.

---

## 3. HumanBodyBones → Mecanim Muscle Mapping

Saat pakai `HumanPoseHandler.SetHumanPose`, gunakan `HumanTrait.MuscleName[i]` indexing.

### Body
- `Spine Front-Back`, `Spine Left-Right`, `Spine Twist Left-Right`
- `Chest Front-Back`, `Chest Left-Right`, `Chest Twist Left-Right`

### Head + Neck
- `Neck Nod Down-Up`, `Neck Tilt Left-Right`, `Neck Turn Left-Right`
- `Head Nod Down-Up`, `Head Tilt Left-Right`, `Head Turn Left-Right`

### Left Arm
- `Left Shoulder Down-Up`, `Left Shoulder Front-Back`
- `Left Arm Down-Up` ⚠️ — convention TBD, butuh test verify
- `Left Arm Front-Back`
- `Left Arm Twist In-Out`
- `Left Forearm Stretch` (elbow bend: -1=full bent, 0=straight, +1=hyperextend)
- `Left Forearm Twist In-Out`
- `Left Hand Down-Up`, `Left Hand In-Out`

### Right Arm
Mirror Left.

### Left Leg
- `Left Upper Leg Front-Back`, `Left Upper Leg In-Out`, `Left Upper Leg Twist In-Out`
- `Left Lower Leg Stretch`, `Left Lower Leg Twist In-Out`
- `Left Foot Up-Down`, `Left Foot In-Out`
- `Left Toes Up-Down`

### Right Leg
Mirror Left.

### Fingers (4 muscles per finger × 5 fingers × 2 hands = 40)
Per finger: `<Bone> <Finger> 1 Stretched`, `<Bone> <Finger> Spread`, `<Bone> <Finger> 2 Stretched`, `<Bone> <Finger> 3 Stretched`

**Total muscle count:** ~95 (lihat `HumanTrait.MuscleCount` runtime).

---

## 4. Animation Code Patterns

### 4.1 Direct Bone Rotation (via Animator.GetBoneTransform)

```csharp
var animator = vrmModelLoader.ModelAnimator;
var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

// Swing arm DOWN (90° around local Z, assuming parent identity)
leftArm.localRotation = Quaternion.Euler(0, 0, 90);

// Equivalent dengan FromToRotation (more explicit)
leftArm.localRotation = Quaternion.FromToRotation(Vector3.left, Vector3.down);
```

⚠️ Direct bone rotation **OVERRIDES** Mecanim Animator output. Lakukan di LateUpdate setelah Animator evaluate.

### 4.2 Mecanim Muscle Space (HumanPoseHandler)

```csharp
var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
HumanPose pose = default;
poseHandler.GetHumanPose(ref pose);

for (int i = 0; i < HumanTrait.MuscleCount; i++) {
    string name = HumanTrait.MuscleName[i];
    if (name == "Left Arm Down-Up") pose.muscles[i] = -0.85f;
    if (name == "Left Forearm Stretch") pose.muscles[i] = 0f;
    // ... per muscle
}
poseHandler.SetHumanPose(ref pose);
```

⚠️ Test result Phase 2.6: muscle behavior tidak match documentation. Tangan ke depan saat Down-Up=-0.85 (expected: down ke samping). Kemungkinan Avatar config Kohaku tidak normalized strict. Lihat [bone-axes-tested.md](bone-axes-tested.md).

### 4.3 Procedural Additive (current Phase 2.5)

```csharp
// IAnimationState.Tick fills BoneOffsets
public void Tick(float deltaTime, ref BoneOffsets offsets) {
    float t = Time.time;
    offsets.chest = Quaternion.Euler(2.5f * Mathf.Sin(2f * Mathf.PI * 0.25f * t), 0, 0);
    // ... per bone
}

// AnimationOrchestrator.LateUpdate apply additive on rest pose cache
// bone.localRotation = restPose * offset
```

### 4.4 Animator Controller (clip-based)

```csharp
// Setup di Editor: assign Animator Controller dengan motion clips
// Runtime trigger:
animator.SetInteger("AssistantState", (int)AssistantState.Active);
animator.SetTrigger("Wave");
animator.SetBool("Walking", true);
```

**Clip source:**
- `Assets/Animations/Clips/VRMA/*.vrma` (11 clips dari tk256ailab/vrm-viewer)
- Mixamo FBX retargeted ke Kohaku Avatar
- Asset Store packs

### 4.5 VRMA Runtime Playback (Phase 2.5)

```csharp
// VrmaPlaybackController.PlayGesture("Wave")
// → load Assets/StreamingAssets/VRMA/Goodbye.vrma
// → spawn hidden VRMA instance
// → HumanPoseHandler retarget per frame ke Kohaku
```

**Available clips (current):**
| Gesture name | VRMA file | Use case |
|--------------|-----------|----------|
| `Wave` | Goodbye.vrma | Greeting |
| `Peace` | Clapping.vrma | Happy gesture |
| `HairTouch` | Blush.vrma | Embarrassed |
| `Tilt` | LookAround.vrma | Curious |
| `Peek` | Surprised.vrma | Reaction |
| `Thinking` | Thinking.vrma | State Thinking |
| `Relax` | Relax.vrma | State Idle |
| `Sad` | Sad.vrma | State Sad |
| `Sleepy` | Sleepy.vrma | Tired |
| `Angry` | Angry.vrma | State Angry |
| `Jump` | Jump.vrma | Excited |

---

## 5. Common Animation Recipes

### 5.1 Subtle Breathing
```csharp
float breath = Mathf.Sin(2f * Mathf.PI * 0.25f * Time.time);  // 4s period
offsets.chest = Quaternion.Euler(2.5f * breath, 0, 0);   // ±2.5° rotate around X
offsets.spine = Quaternion.Euler(1.0f * breath, 0, 0);   // sympathy
```

### 5.2 Head Look Around
```csharp
float lookY = Mathf.Sin(2f * Mathf.PI * 0.15f * Time.time);
offsets.head = Quaternion.Euler(0, 5f * lookY, 0);  // ±5° turn
```

### 5.3 Body Sway
```csharp
float sway = Mathf.Sin(2f * Mathf.PI * 0.18f * Time.time);
offsets.hips = Quaternion.Euler(0, 0, 2f * sway);  // ±2° Z roll
```

### 5.4 Auto Blink (BlendShape)
```csharp
// AutoBlinkController.cs — random blink 2-6s interval
blendShapeProxy.ImmediatelySetValue(BlendShapePreset.Blink, blinkValue);
```

### 5.5 Lip Sync FFT
```csharp
// LipSyncController.cs — analyze audio → drive A/I/U/O blendshapes
audioSource.GetSpectrumData(samples, 0, FFTWindow.BlackmanHarris);
float amp = ...;  // normalize 0-1 dari spectrum
blendShapeProxy.ImmediatelySetValue(BlendShapePreset.A, amp);
```

### 5.6 Eye Saccade (realistic)
```csharp
// LookAtController.cs — snap target every 2-6s, hold 0.3-1.2s
// + 5% chance dramatic radius (look around)
```

---

## 6. Tested Axis Behaviors

⚠️ **Critical for animation work** — semua hipotesis di docs ini perlu verified at device. Track results di [bone-axes-tested.md](bone-axes-tested.md).

Run `DumpBoneInfo` di `AnimationOrchestrator.cs` untuk extract real local rotation + world axes saat model loaded:
```bash
adb logcat -d 2>&1 | grep BoneDump
```

---

## 7. Body Proportions Kohaku

```
World Y axis (height):
   1.460  ← Top of head (Head + 0.092 to skull top approx)
   1.368  ← LeftEye / RightEye level (Head + 0.055)
   1.313  ← Head center (Hips Y + 0.792 + Spine 0.013 + Chest 0.086 + Neck 0.198 + Head 0.037)
   1.276  ← Neck base
   1.078  ← Chest (collar bone area)
   0.992  ← Spine
   0.792  ← Hips (root pivot)
   0.711  ← Upper leg root
   0.388  ← Knee
   0.061  ← Ankle (foot pivot)
  -0.012  ← Toes base
   0.000  ← world Y origin (model spawn point)
```

**Total height: ~1.46m** (head top, accounting for Head bone + skull ~0.15m)

**Width (X):**
- Hip width: 0.126m (LeftUpLeg + RightUpLeg)
- Shoulder width: ~0.072m antara shoulders
- Arm span (T-pose, fingertip to fingertip): ~1.0m

---

## 8. Common Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Bone localRotation MUL accumulation | Karakter twist gila over time | Cache rest pose, apply `rest * offset` instead of `current * offset` |
| Mecanim muscle convention mismatch | Tangan ke depan saat expect down | Test direct localRotation dulu, fallback Mecanim if axis confirmed |
| Animator override LateUpdate | Procedural rotation di-reset tiap frame | Pastikan tidak ada Animator Controller yang override, atau apply procedural di Update0 |
| HandPose finger axis salah | Finger twisted random direction | Verify finger curl axis (X, Y, atau Z) per VRM model — bisa beda |
| VRMA HumanPose retarget direct | Body distort (cross-version VRM 1.0 → 0.x) | Pakai HumanPoseHandler.GetHumanPose / SetHumanPose, bukan direct localRotation copy |
| Scale at hip pivot causes head out of frame | Karakter naik saat scale up | Compensate Y position: `y = 0.9 * (scale - 1)` untuk anchor feet |

---

## 9. References

- [VRM 0.x Specification](https://github.com/vrm-c/vrm-specification/blob/master/specification/0.0/README.md)
- [Unity HumanBodyBones enum](https://docs.unity3d.com/ScriptReference/HumanBodyBones.html)
- [Unity HumanPoseHandler](https://docs.unity3d.com/ScriptReference/HumanPoseHandler.html)
- [Mixamo Animation Library](https://www.mixamo.com)
- [VRoid Studio (create custom VRM)](https://vroid.com)

**See also:**
- [bone-axes-tested.md](bone-axes-tested.md) — track tested axis behaviors
- [animation-from-anime.md](animation-from-anime.md) — anime video → animation pipeline
- [kohaku-bones.json](kohaku-bones.json) — machine-readable bone reference
