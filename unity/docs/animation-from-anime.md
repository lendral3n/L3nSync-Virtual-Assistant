# Anime Video → Lia Animation Workflow

Panduan lengkap untuk convert gerakan karakter dari video anime menjadi animation Kohaku VRM Lia. Foundation: bone-mapper.md + bone-axes-tested.md.

**User vision:** kasih video anime, animate Kohaku ikuti gerakan tersebut.

---

## 1. Pipeline Overview

```
[Video Anime]
    ↓ (1. Pose extraction)
[Pose keypoints per frame: x,y per landmark]
    ↓ (2. Retarget ke humanoid skeleton)
[BVH atau FBX animation clip]
    ↓ (3. Convert ke VRMA)
[.vrma file]
    ↓ (4. Drop ke StreamingAssets/VRMA/ + register di VrmaPlaybackController pool)
[Triggerable via UnityBridge.sendMessage("VRMAssistant", "TriggerGesture", "<name>")]
```

---

## 2. Tools Comparison

| Tool | Cost | Quality | Anime support | Output |
|------|------|---------|---------------|--------|
| **MediaPipe Pose** (free, local) | Free | Medium | OK untuk 2D anime | 33 landmarks |
| **OpenPose / DWPose** | Free | Good | OK | 17-68 keypoints + hand |
| **Plask Motion** (web) | Free tier | High | Good (anime style) | FBX, BVH |
| **Rokoko Video** | Paid (~$25/mo) | Very High | Best for character | FBX |
| **DeepMotion Animate 3D** | Paid (~$15+/mo) | High | OK | FBX, BVH, VMD |
| **Cascadeur** | Free + Pro | Pro | Manual + AI assist | FBX |
| **Manual Blender keyframe** | Free | Highest (kontrol penuh) | Best | BVH, FBX |

**Recommended starter pipeline (free, anime-friendly):**
- **Plask Motion** untuk extract → FBX
- **fbx2vrma-converter** untuk convert ke VRMA
- Drop di Lia project StreamingAssets

---

## 3. Detailed Steps — Plask Motion Workflow

### Step 1: Capture pose dari video
1. Buka https://plask.ai (free tier)
2. Upload video anime (mp4, atau klik Plask untuk extract from YouTube URL)
3. Pilih character di video (kalau multiple) — bound box
4. Plask AI process (1-3 menit untuk video ≤30 detik)
5. Preview keyframe pose di timeline editor
6. Edit keyframe manual kalau ada error (trim weird poses)
7. Export → **FBX (Mecanim Humanoid compatible)**

### Step 2: Verify FBX di Blender
1. Buka Blender, import FBX
2. Verify bone structure: harus humanoid standard (Mixamo-compatible naming OR Mecanim-friendly)
3. Trim animation length kalau perlu (e.g., loop 4-second wave gesture)
4. Export ulang sebagai FBX untuk Unity import

### Step 3: Convert FBX → VRMA
1. Clone https://github.com/tk256ailab/fbx2vrma-converter
2. Install Python dependencies (`pip install -r requirements.txt`)
3. Run: `python fbx2vrma.py input.fbx output.vrma`
4. Verify .vrma file di **VRoid Hub viewer** atau https://github.com/flarom/figure (web)

### Step 4: Integrate ke Lia project
1. Drop `output.vrma` ke `Assets/StreamingAssets/VRMA/`
2. Edit `VrmaPlaybackController.cs` field `vrmaPool`:
   ```csharp
   new VrmaEntry { gestureName = "AnimeWave", vrmaFileName = "output.vrma" }
   ```
3. Build Unity → APK → install → trigger via:
   ```kotlin
   UnityBridge.sendMessage("VRMAssistant", "TriggerGesture", "AnimeWave")
   ```

---

## 4. Manual Keyframe Workflow (Higher Quality)

Untuk character expression yang **anime-stylized exaggerated** (point dramatic, wave dengan slight bend, dll), automatic pose extraction sering miss subtle artistic intent. Manual keyframe at Blender lebih bagus.

### Setup Blender
1. Install **VRM Add-on for Blender** by saturday06: https://github.com/saturday06/VRM-Addon-for-Blender
2. Import `Kohaku_dress_1.10_VRM.vrm` ke Blender (auto-create Armature humanoid)
3. Set frame rate ke target (e.g., 30 fps)

### Reference Video Side-by-Side
1. Buka video anime di player (YouTube atau lokal) di samping Blender
2. Identify **key poses** (start, midpoint, end) — biasanya 3-5 keyframe per gesture
3. Pause video di key frame, screenshot

### Keyframe Each Pose
1. Frame 0: import default rest pose
2. Frame X (key 1): pose Armature ikuti screenshot — rotate bones individual
3. Frame Y (key 2): next key pose
4. Tween automatic via Blender (linear/bezier)
5. Loop: set keyframe 0 dan last frame identical untuk seamless loop

### Export
- File → Export → FBX (Animation Only, Armature only)
- Atau langsung VRM Add-on: File → Export → VRM Animation (.vrma)

### Pro tips
- Keep keyframe count low (10-20 untuk 2-3 detik gesture) → smoother
- Use **Auto IK** di Blender untuk hand placement natural
- Export at **60 fps** untuk smooth loop, biar 30 fps Unity playback bisa interpolate

---

## 5. Animation Patterns Specific to Anime Style

### 5.1 Anticipation + Recovery
Anime motion biasa **exaggerated anticipation** (preparation) sebelum action, lalu **recovery** (overshoot back).

Example: wave gesture
- Frame 0-15: arm di rest
- Frame 16-25: **anticipation** — arm slight back-down
- Frame 26-50: **action** — arm swing up + side-side wave
- Frame 51-65: **recovery** — slight overshoot back, settle ke rest

### 5.2 Squash & Stretch
Body deformation saat dynamic motion.
- VRM tidak support per-bone scale di animation by default. Workaround: scale entire root selama action peak (e.g., scale 1.05 saat jump up, 0.95 saat land).

### 5.3 Hold Frames
Anime sering **hold pose 3-5 frame** di key moments (impact). Versus Western animation yang continuous tweening.

### 5.4 Hair / Cloth Secondary Motion
VRM SpringBone handle ini otomatis (Kohaku punya). Body motion → hair sway natural follow. Don't over-animate.

### 5.5 Eye Smear / Distortion
Anime sometimes use eye distortion saat fast head turn. VRM tidak support easily — gunakan **VRMBlendShapeProxy.Surprised** preset briefly to fake.

---

## 6. Common Anime Gestures Referensi

| Gesture | Description | Frames @ 30fps |
|---------|-------------|----------------|
| **Wave (greeting)** | Arm up, hand wave side-side 2-3x | 60-90 |
| **Peace sign** | V sign with index+middle, hold | 30-45 (mostly hold) |
| **Hair touch** | Right hand to side hair, slight tilt head | 45-60 |
| **Bow (greeting)** | Spine forward 30-45°, head tilt down | 60 (slow + held) |
| **Surprised** | Body jerk back, hands up to chest | 15-30 (fast) |
| **Thinking** | Right hand to chin, head tilt, eye look up | 90 (slow + held) |
| **Embarrassed** | Hand near face, tilt head down, body sway | 60 |
| **Pointing** | Right arm extend forward, index out | 30-45 |
| **Hug self** | Both arms cross over chest | 60 (slow embrace) |
| **Shrug** | Shoulders up + arms slight out, brief | 20-30 (fast) |

---

## 7. Naming Convention untuk VRMA Pool

Saat import banyak VRMA dari berbagai source, gunakan naming convention konsisten supaya AI backend nanti mudah trigger:

```
<Category>_<Name>_<Variation>.vrma

Categories:
- greet_*: Hello, Bow, Wave, Goodbye
- emote_*: Happy, Sad, Angry, Surprised, Embarrassed, Tired
- gesture_*: Peace, Point, ThumbsUp, Clap
- idle_*: Relaxed, AlertLook, Sway
- thinking_*: HandToChin, LookUp, Tap
- action_*: Walk, Sit, Sleep, Jump

Examples:
- greet_Wave_Bouncy.vrma
- emote_Happy_Skip.vrma
- gesture_Peace_Closeup.vrma
```

Backend AI nanti bisa parse JSON `{"gesture": "greet_Wave_Bouncy"}` → trigger via `VrmaPlaybackController.PlayGesture(gestureName)`.

---

## 8. Quality Checklist sebelum integrate VRMA

- [ ] FBX/VRMA preview di standalone viewer — pose tidak twisted
- [ ] Animation length 2-5 detik (max), loop seamless start = end
- [ ] No floor sliding (root stable kecuali walking gesture)
- [ ] Hands not penetrate body
- [ ] Head + neck rotation max ±60° (anatomical limit)
- [ ] Arms not exceed shoulder limit (cek via Mecanim retarget preview di Unity)
- [ ] Frame rate consistent (30 atau 60 fps)
- [ ] File size <500KB per VRMA (untuk APK budget)

---

## 9. Future Enhancements

- **Voice-to-gesture pipeline:** TTS audio analyze → keyword match → trigger VRMA gesture
  Example: "halo" → trigger `greet_Wave_Bouncy.vrma`
- **Real-time MediaPipe overlay:** webcam capture user → drive Kohaku live (VTuber-style)
- **Procedural blending:** mix multiple VRMA clips dengan weight (e.g., 70% Idle + 30% Wave for "casual greet")
- **Emotion detection from text:** AI backend extract sentiment → blend Expression presets
- **Adaptive idle:** backend tracks user attention via screen-on time → karakter peeking edge saat user lama tidak interact

---

## 10. References

- [VRoid Studio (create custom VRM)](https://vroid.com/en/studio)
- [VRoid Hub (community VRM library)](https://hub.vroid.com)
- [BOOTH .vrma category](https://booth.pm/en/browse/3D%20Motion)
- [Mixamo (humanoid animations)](https://www.mixamo.com)
- [Plask Motion AI](https://plask.ai)
- [tk256ailab/fbx2vrma-converter](https://github.com/tk256ailab/fbx2vrma-converter)
- [tk256ailab/vrm-viewer (free VRMA samples + viewer)](https://github.com/tk256ailab/vrm-viewer)
- [VRM Add-on for Blender](https://github.com/saturday06/VRM-Addon-for-Blender)
- [12 Principles of Animation (Disney)](https://en.wikipedia.org/wiki/Twelve_basic_principles_of_animation)
- [Anime Animation Industry Practices (Sakuga Blog)](https://blog.sakugabooru.com)
