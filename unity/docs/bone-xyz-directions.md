# Kohaku VRM — XYZ Rotation Direction Analysis

**Purpose:** Untuk setiap bone, dokumentasikan ARAH gerakan saat rotate dengan **+X, -X, +Y, -Y, +Z, -Z**. Ini critical untuk write animation yang benar (animation procedural Lia saat ini terlihat aneh karena saya guess axis salah).

---

## Foundational Convention

### Unity Coordinate System (Left-handed, Y-up)
```
        +Y (up)
        |
        |
        +-------- +X (right, dari POV character looking forward)
       /
      /
    +Z (forward, toward camera ketika character menghadap camera)
```

### Right-Hand Rule untuk Rotation
Saat rotate **positif** sekitar axis tertentu (e.g., +X), arah putaran:
- **+X axis (point ke kanan)** → pitch DOWN (head/upper body tilt ke depan, "menunduk")
- **+Y axis (point ke atas)** → yaw LEFT (turn ke kiri, looking at character from front)
- **+Z axis (point ke depan/ke kamera)** → roll LEFT (head tilt ear-to-shoulder ke kiri)

### Kohaku Initial State
Dari `kohaku-bones.json`:
- **All initial rotations = identity** `(0, 0, 0, 1)`
- **Local axes = World axes** (no parent rotation chain)
- Character spawn facing **+Z direction** (toward camera saat camera at +Z looking -Z)
- Hip pivot world Y = 0.792m

---

## 1. Body Core Bones

### Hips (`Hips`) — node 1, world (0, 0.792, 0)
Root pivot. Translation X=0, Y=0.792, Z=0.

| Rotation | Arah movement | Visual effect |
|----------|---------------|---------------|
| `Euler(+30, 0, 0)` | Pitch FORWARD around X | Pelvis tilt ke depan (lower back arch) |
| `Euler(-30, 0, 0)` | Pitch BACKWARD | Lower back arch belakang (anterior tilt) |
| `Euler(0, +30, 0)` | Yaw LEFT (CCW from above) | Body rotate ke kiri (looking from above) |
| `Euler(0, -30, 0)` | Yaw RIGHT (CW from above) | Body rotate ke kanan |
| `Euler(0, 0, +30)` | Roll LEFT (lean ke kiri) | Hip tilt ear-to-shoulder LEFT |
| `Euler(0, 0, -30)` | Roll RIGHT | Hip tilt RIGHT |

### Spine (`Spine`) — translation (0, 0.013, 0)
Lumbar joint. Slight up dari Hips.

Sama dengan Hips axis:
- `Euler(+30, 0, 0)` = bow FORWARD (chest forward)
- `Euler(-30, 0, 0)` = back arch
- `Euler(0, +30, 0)` = twist LEFT
- `Euler(0, 0, +30)` = side bend LEFT

### Chest (`Chest`) — translation (0, 0.086, -0.006)
Thoracic. Up + slight back dari Spine.

- `Euler(+30, 0, 0)` = chest expand FORWARD (breathing in deep)
- `Euler(-30, 0, 0)` = chest collapse BACK (slouch)
- `Euler(0, +30, 0)` = chest twist LEFT
- `Euler(0, 0, +30)` = chest tilt LEFT (lean side)

⚠️ **Note breathing animation:** Untuk subtle breath in/out, pakai amplitude **±2-3°** di X axis, bukan ke Y/Z (tidak natural).

### Neck (`Neck`) — translation (0, 0.198, 0.024)
Up + slight forward.

- `Euler(+30, 0, 0)` = head pitch DOWN (chin to chest)
- `Euler(-30, 0, 0)` = head pitch UP (look at sky)
- `Euler(0, +30, 0)` = head turn LEFT (look at left ear direction)
- `Euler(0, 0, +30)` = head tilt LEFT (right ear toward right shoulder)

### Head (`Head`) — translation (0, 0.037, -0.003)
Pivot dari head.

Sama dengan Neck (additive ke Neck rotation):
- `Euler(+30, 0, 0)` = additional NOD DOWN
- `Euler(0, +30, 0)` = additional TURN LEFT

⚠️ **Untuk head turn natural** (looking around): pakai **Y axis ±5-15°**.
**Untuk nod yes/no:**
- Yes (nod up-down) = X axis ±10° pulse
- No (shake left-right) = Y axis ±15° pulse

---

## 2. Left Arm

### Critical: Arm pointing direction
LeftArm translation = `(-0.045, 0, 0)` → **arm pointing -X direction** (LEFT side dari character).

Local axes (saat parent identity) = world axes:
- Local **+X** = world +X (right, OPPOSITE arm direction)
- Local **+Y** = world +Y (up)
- Local **+Z** = world +Z (forward toward camera)

### LeftShoulder (`LeftShoulder`) — translation (-0.036, 0.164, 0.023)
Connector ke LeftArm. Slight elevation + forward.

| Rotation | Arah movement |
|----------|---------------|
| `Euler(+30, 0, 0)` | Shoulder pitch (slight effect — bone short) |
| `Euler(0, +30, 0)` | Shoulder rotate forward LEFT (yaw) |
| `Euler(0, 0, +30)` | **Shoulder LIFT UP** (shrug LEFT) |
| `Euler(0, 0, -30)` | Shoulder PRESS DOWN |

### LeftUpperArm (`LeftArm`) — translation (-0.045, 0, 0)
**Most important bone untuk arm animation.**

Karena arm pointing -X, rotasi mempengaruhi gerakan arm di sekitar shoulder pivot.

| Rotation | Arah arm movement | Analogi |
|----------|-------------------|---------|
| `Euler(+30, 0, 0)` | **Arm rotate around its OWN length** (twist/pronation) | Twist palm ke arah ground |
| `Euler(-30, 0, 0)` | Arm twist palm ke arah sky (supination) | |
| `Euler(0, +30, 0)` | **Arm swing FORWARD** (to camera direction +Z) | Punch forward |
| `Euler(0, -30, 0)` | Arm swing BACKWARD (-Z) | Reach behind |
| `Euler(0, 0, +30)` | **Arm swing UP** ⤴ (-X rotates toward +Y) | Raise arm sideways |
| `Euler(0, 0, -30)` | **Arm swing DOWN** ⤵ (-X rotates toward -Y) | **Natural rest pose dari T-pose!** |

✅ **CRITICAL FINDING:**
- Untuk swing left arm DOWN ke samping body (natural rest): `Euler(0, 0, -90)` (NEGATIVE 90° around Z)
- Bukan `+60` (saya sebelumnya), bukan `-0.85 Down-Up muscle` (Mecanim retargeting axis salah).

**Rest pose natural:** `leftArm.localRotation = Quaternion.Euler(0, 0, -75)` (slightly less than 90° untuk leave room di hip).

### LeftLowerArm (`LeftForeArm`) — translation (-0.208, 0, 0.005)
Continue arm direction (-X). Forearm length 20.8cm.

| Rotation | Arah movement |
|----------|---------------|
| `Euler(+30, 0, 0)` | Forearm twist (pronate hand DOWN) |
| `Euler(0, +30, 0)` | **Elbow bend FORWARD** (forearm comes up & forward) |
| `Euler(0, -30, 0)` | Elbow bend BACKWARD (less common, joint limit) |
| `Euler(0, 0, +30)` | Forearm yaw — minor effect |

✅ **Elbow bend natural:** `Euler(0, +60, 0)` (forearm comes up 60° toward face).

### LeftHand (`LeftHand`) — translation (-0.175, 0, -0.005)
Continue arm direction.

| Rotation | Arah movement |
|----------|---------------|
| `Euler(+30, 0, 0)` | Wrist palm-down (rotate palm to floor) |
| `Euler(0, +30, 0)` | Wrist deviation up (back of hand to forearm) |
| `Euler(0, 0, +30)` | Wrist deviation side |

---

## 3. Right Arm — MIRROR Left Arm

Translation positive X (arm pointing +X).

| Bone | Equivalent rotation untuk swing arm DOWN |
|------|------------------------------------------|
| RightShoulder | `Euler(0, 0, -30)` (= shoulder lift UP, mirror) |
| **RightUpperArm** | **`Euler(0, 0, +75)`** (POSITIVE Z, mirror of left's negative) |
| RightLowerArm | `Euler(0, -60, 0)` (negative Y, mirror) |
| RightHand | Similar wrist rotation |

✅ **Right arm rest pose:** `rightArm.localRotation = Quaternion.Euler(0, 0, +75)`

**Sign convention summary:**
- LEFT arm Z rotation: NEGATIVE = down, POSITIVE = up
- RIGHT arm Z rotation: POSITIVE = down, NEGATIVE = up

---

## 4. Legs

### LeftUpperLeg (`LeftUpLeg`) — translation (-0.063, -0.081, 0)
Leg pointing **-Y** (down) primarily, slight -X.

| Rotation | Arah movement |
|----------|---------------|
| `Euler(+30, 0, 0)` | Leg lift FORWARD (knee toward chest, walking step) |
| `Euler(-30, 0, 0)` | Leg back kick |
| `Euler(0, +30, 0)` | Leg yaw OUTWARD (open hip) |
| `Euler(0, 0, +30)` | Leg side LIFT (lateral raise) |

### LeftLowerLeg (`LeftLeg`) — translation (0, -0.323, 0)
Knee joint.

- `Euler(+30, 0, 0)` = knee bend (heel to butt direction)
- `Euler(-30, 0, 0)` = knee hyperextend (limit)

### LeftFoot, LeftToeBase
- Foot: `Euler(+30, 0, 0)` = toe POINT down (plantar flex)
- Foot: `Euler(-30, 0, 0)` = toe UP (dorsiflex)

### Right Leg — Mirror Left

---

## 5. Hands & Fingers

Finger curl axis testing required (lihat [bone-axes-tested.md](bone-axes-tested.md) Phase 2.5 attempt = wrong axis Z, distorted).

### Tentative finger axis (HYPOTHESIS — need verification)
Each finger phalanx local axes:
- Local **+X**: along finger length (distal direction)
- Local **+Y**: dorsal (back of finger)
- Local **+Z**: lateral (side)

**Curl axis:** finger curl ke palm = rotate around local **Z axis**?
- Untuk LEFT hand fingers: curl direction TBD
- Untuk RIGHT hand fingers: mirror sign

⏳ **TEST NEEDED:** Apply `Euler(0, 0, +30)` ke each phalanx, observe direction.

### Per-finger character
| Finger | Use | Curl angle range |
|--------|-----|------------------|
| Thumb | Approval, holding | ±30° |
| Index | Pointing | ±60° |
| Middle | (with Index) Peace sign | ±90° |
| Ring | Default curl | ±70° |
| Pinky | Cute pose, default | ±80° |

---

## 6. Eyes (`LeftEye`, `RightEye`)

Translation child of Head: (-0.033, 0.055, -0.005) — slight in front of head pivot.

| Rotation | Arah movement |
|----------|---------------|
| `Euler(+30, 0, 0)` | Look DOWN |
| `Euler(-30, 0, 0)` | Look UP |
| `Euler(0, +30, 0)` | Look LEFT (viewer's right) |
| `Euler(0, -30, 0)` | Look RIGHT |

⚠️ **VRM punya VRMLookAtBoneApplyer** yang drive eyes via VRMLookAtHead.target — biarkan itu yang handle, jangan rotate eye bones langsung.

---

## 7. Quick Reference Table — Common Animations

| Animation | Bone | Rotation |
|-----------|------|----------|
| Breathing in (chest expand) | Chest | `Euler(+2, 0, 0)` |
| Breathing out | Chest | `Euler(-2, 0, 0)` |
| Head turn LEFT (natural) | Head | `Euler(0, +10, 0)` |
| Head turn RIGHT | Head | `Euler(0, -10, 0)` |
| Nod YES (down phase) | Head | `Euler(+15, 0, 0)` |
| Shake NO (left phase) | Head | `Euler(0, +20, 0)` |
| Shrug LEFT | LeftShoulder | `Euler(0, 0, +20)` |
| Shrug RIGHT | RightShoulder | `Euler(0, 0, -20)` |
| Left arm rest down | LeftUpperArm | `Euler(0, 0, -75)` |
| Right arm rest down | RightUpperArm | `Euler(0, 0, +75)` |
| Left elbow bend forward | LeftLowerArm | `Euler(0, +60, 0)` |
| Right elbow bend forward | RightLowerArm | `Euler(0, -60, 0)` |
| Wave (left arm up + sway) | LeftUpperArm | `Euler(0, 0, +90)` + Z oscillate ±15° |
| Body sway LEFT | Hips | `Euler(0, 0, +5)` |

---

## 8. Verification Workflow

Untuk setiap claim di atas, verify via runtime test:

### Method 1: Direct rotation test
```csharp
// Di OnModelLoaded, override SAU bone:
var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
leftArm.localRotation = Quaternion.Euler(0, 0, -75);  // expect: arm DOWN to side
```

Build APK, observe visual. Update [bone-axes-tested.md](bone-axes-tested.md):
- ✅ Confirmed
- ❌ Wrong direction → flip sign
- 🟡 Partial (different axis maybe)

### Method 2: Comprehensive sweep test
```csharp
// AnimationOrchestrator.LateUpdate (saat testing only):
float t = (Time.time % 4f) / 4f;  // 0-1 over 4s
float angle = Mathf.Lerp(-90, 90, t);
animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation
    = Quaternion.Euler(0, 0, angle);
// Watch: arm should swing from full UP to full DOWN
```

### Method 3: Editor inspection (Play Mode)
1. Hit Play di Unity Editor
2. Hierarchy: VRMAssistant → VRM → Hips → ... → LeftArm
3. Inspector: tweak `Local Rotation` X/Y/Z value
4. Observe Game View

---

## 9. Common Pitfalls + Why Animations Look Weird

| Pitfall | Symptom | Root cause | Fix |
|---------|---------|------------|-----|
| Wrong axis | Arm goes forward bukan down | Z+ vs Z- confusion | Flip sign |
| Wrong magnitude | Movement terlalu kecil/besar | Not match bone length scale | Adjust amplitude |
| Wrong frame of reference | Rotation OK in Editor, weird on device | Animator override | Apply LateUpdate after Animator |
| Quaternion accumulation | Twist gila over time | `bone *= offset` instead of `restPose * offset` | Use AdditiveLayerHelper |
| Mecanim normalization | HumanPose muscle berbeda dari direct rotation | Avatar config T-pose detection | Use direct localRotation, not HumanPose for rest pose |
| MMD coord convention | VMD playback looks twisted | MMD uses different local axes per bone | Rotation quaternion direct copy doesn't always work — need bone-specific axis remap |

---

## 10. References

- [Unity Coordinate System](https://docs.unity3d.com/Manual/class-Transform.html)
- [Right-Hand Rule (Unity vs left-handed)](https://docs.unity3d.com/Manual/QuaternionAndEulerRotationsInUnity.html)
- [VRM 0.x Bone Convention](https://github.com/vrm-c/vrm-specification/blob/master/specification/0.0/README.md)
- [bone-mapper.md](bone-mapper.md) — bone hierarchy + names
- [bone-axes-tested.md](bone-axes-tested.md) — verified test results
- [kohaku-bones.json](kohaku-bones.json) — machine-readable transforms
