# Kohaku VRM — Bone Axis Tested Results

Tracking testing actual axis behavior dari Kohaku VRM model. Update setiap kali ada result baru dari device test atau editor inspection.

**Status legend:**
- ✅ Confirmed working (visually verified di device)
- 🟡 Tentative (logical guess, not verified)
- ❌ Tested, NOT working
- ⏳ TODO test

---

## Body Core

### Hips (`J_Bip_C_Hips`)
| Axis | Direction | Status | Notes |
|------|-----------|--------|-------|
| Local X+ | Hip tilt forward (lower back arch) | 🟡 |
| Local Y+ | Hip rotate left | 🟡 |
| Local Z+ | Hip lean side LEFT | ✅ | Confirmed via sway animation di IdleAnimationState |

### Spine (`J_Bip_C_Spine`)
| Axis | Direction | Status |
|------|-----------|--------|
| Local X+ | Bow forward | 🟡 |
| Local Y+ | Twist left | 🟡 |
| Local Z+ | Side bend left | 🟡 |

### Chest (`J_Bip_C_Chest`)
| Axis | Direction | Status |
|------|-----------|--------|
| Local X+ | Chest expand forward (breathing in) | ✅ | IdleAnimationState breathing pattern |
| Local Y+ | Chest twist left | 🟡 |
| Local Z+ | Chest side bend left | 🟡 |

### Head (`J_Bip_C_Head`)
| Axis | Direction | Status |
|------|-----------|--------|
| Local X+ | Nod down (chin to chest) | 🟡 |
| Local Y+ | Turn LEFT (look behind left) | ✅ | Visually confirmed at amplitude 18° |
| Local Z+ | Tilt LEFT (ear to shoulder) | 🟡 |

---

## Arms (CRITICAL — needs testing)

### LeftUpperArm (`J_Bip_L_UpperArm`)
T-pose default: arm pointing -X world (horizontal left).

| Test | Result | Status |
|------|--------|--------|
| `localRotation *= Euler(0,0,60)` | T-pose tetap (no swing down) | ❌ Phase 2.6 attempt — POSITIVE Z = swing UP, hampir invisible delta saat T-pose |
| `Rotate(Vector3.forward, 60, Space.World)` | T-pose tetap | ❌ Phase 2.6 attempt 2 — same root cause |
| `HumanPose Left Arm Down-Up = -0.85` | Arm folded ke depan dada (forward+slight up) | ❌ Phase 2.6 attempt 3 — Mecanim muscle axis mismatch |
| `Localization rest pose` | Arms remain horizontal in T-pose | ✅ Stable default (safe mode) |
| `localRotation = Euler(0, 0, -75)` | **PENDING** (per right-hand rule analysis: NEGATIVE Z swing arm DOWN) | 🟡 Ready to test (Phase 2.6.E re-apply) |

**Root cause hypothesis (refined per docs/bone-xyz-directions.md):**
- LeftArm pointing world -X. Untuk swing -X → -Y (down), pakai right-hand rule rotation around Z axis: NEGATIVE.
- Earlier `+60` Z = swing UP (toward +Y), so visually nothing moved beyond head height — looked like "T-pose tetap".
- Mecanim Down-Up muscle convention mungkin reversed di Kohaku Avatar config (Z axis sign flipped).

**Current applied (build pending verify):**
```csharp
leftUpperArm.localRotation  = Quaternion.Euler(0, 0, -75);  // negative Z → arm down LEFT side
rightUpperArm.localRotation = Quaternion.Euler(0, 0, +75);  // positive Z → arm down RIGHT side (mirror)
leftLowerArm.localRotation  = Quaternion.Euler(0, +10, 0);  // slight forearm forward
rightLowerArm.localRotation = Quaternion.Euler(0, -10, 0);  // mirror
```

**Verification checklist:**
- [ ] Visual: arms swing down to side body (A-pose) bukan T-pose
- [ ] Logcat: `[ArmRest] LeftUpperArm set to Euler(0,0,-75)` muncul
- [ ] Logcat: `[BoneDump] LeftUpperArm localEuler=(0.0,0.0,-75.0)` (atau equivalent quaternion form 285°)
- [ ] Forearm sedikit ke depan (tidak stiff straight)
- [ ] Hands TIDAK clipping ke body (kalau clipping → reduce angle dari 75 ke 60)

### LeftLowerArm (`J_Bip_L_LowerArm`)
T-pose default: continuation dari upper arm, lurus ke -X world.

| Test | Result | Status |
|------|--------|--------|
| `HumanPose Left Forearm Stretch = 0` | Lurus continuation upper arm | ⏳ Not visually verified yet |
| `HumanPose Left Forearm Stretch = -0.5` | Bend ~45° toward chest | ⏳ |

### LeftHand (`J_Bip_L_Hand`)
T-pose default: palm facing -Y (down), fingers pointing -X.

⏳ All axes need testing.

---

## Legs (untested)

### LeftUpperLeg / RightUpperLeg
⏳ TODO. Default T-pose = lurus turun -Y.

### LeftLowerLeg / RightLowerLeg
⏳ TODO. Knee bend axis unknown.

### LeftFoot / RightFoot
⏳ TODO.

### LeftToes / RightToes
⏳ TODO.

---

## Fingers (untested — HandPoseController disabled)

### Per-finger 3-phalanx structure
- Proximal: connect ke palm
- Intermediate: middle joint
- Distal: tip

**Hypothesis (need verify):**
- Curl finger ke palm: rotate around local **X axis** (positive untuk left hand, negative untuk right? — mirror convention)
- Spread (jari ke samping): rotate around local Y axis
- Twist: local Z axis

**Phase 2.5 HandPoseController test:**
- Pakai `Quaternion.Euler(0, 0, perPhalanx * sign)` untuk fingers (skip thumb).
- Result: tangan terdistorsi parah (twisted random direction).
- **Conclusion:** Local Z axis bukan curl axis. Need test local X axis.

---

## How to Update This Doc

Setelah test rotation di device:

1. **Add row dengan test description** ke section bone yang relevan
2. **Update Status:** ✅ kalau visually confirmed, ❌ kalau jelas salah, 🟡 kalau parsial
3. **Note hypothesis** di bawah kalau tidak match expectation
4. **Reference test code** kalau perlu reproduction

Hasil testing penting untuk:
- HandPoseController re-enable (Phase 3)
- Animation Controller proper rest pose
- VRMA retargeting fine-tune kalau retargeting salah arah

---

## Testing Workflow Reference

### Editor (preferred)
1. Open scene `Main.unity` di Unity Editor
2. Hit Play, Lia VRM load di Game view
3. Pause Editor
4. Inspect bone di Hierarchy: `VRMAssistant > VRM > J_Bip_C_Hips > ...`
5. Manual rotate via Inspector → observe visual change
6. Note axis + result di table di atas

### Device (production reality check)
1. Add temporary log di code: `Debug.Log($"[Test] axis result: {axis}, value: {value}")`
2. Build + deploy APK
3. `adb logcat -d 2>&1 | grep "Test"`
4. Cross-reference dengan visual screenshot
