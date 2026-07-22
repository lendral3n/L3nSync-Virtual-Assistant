# MoCap untuk Lia VA — Research & Implementation Guide

> **Status**: Research phase, belum implementation. Dokumen ini compile diskusi pencarian MoCap pipeline untuk apply animasi ke karakter Kohaku VRM.
>
> **Last updated**: 2026-05-13
> **Context**: Diskusi dari Claude Code session — implementasi animation room di app vs pakai external MoCap tools.

---

## 🎯 Konteks & Keputusan Awal

### Kebutuhan User
1. **All users, semua kalangan** — UX harus simple/intuitif
2. **Full body** — body + jari (fingers) + ekor (tail) + face
3. **Variable length animation**
4. **MoCap-based** approach (user pilih)
5. **Save di HP** (no cloud)
6. **Naming**: TBD (kandidat: Studio Lia, Sanggar Lia, Mirror, Cermin Lia)

### Reality Check Awal
- 55+ bones manipulable di Kohaku VRM (25 humanoid + 30 finger + ~10 springbones)
- Springbones (ekor/hair) wajib **physics-driven**, bukan manual
- Finger detail = dropdown preset, bukan per-bone slider (mustahil di phone UI)
- Manual control praktis hanya untuk ~14 body bones + hand preset + face dropdown

### Keputusan: Build vs Buy
| Approach | Effort | Quality |
|----------|--------|---------|
| Build MoCap in-app (MediaPipe Holistic) | 3-4 bulan | Variable |
| **Use external MoCap + import to app** ⭐ | **1-2 minggu** | **Use any tool — even pro** |
| Puppet Mode in-app (IK + touch-drag) | 3-4 minggu | Geometric kalau pemula |

**Keputusan final**: **Pakai MoCap tool eksternal + build "Import Animation" feature di app**. 8-10x lebih cepat ship, quality lebih fleksibel.

---

## 🧠 Konsep Inti: Humanoid Avatar Retargeting

VRM Kohaku punya **Unity Humanoid Avatar** (25 standard bones via HumanBodyBones enum). Mayoritas MoCap tools export ke FBX/VMD yang juga humanoid-aware.

**Magic**: Unity otomatis terjemahin bone names yang beda jika source dan target keduanya `Humanoid Type`.

```
MoCap source bone        Unity Avatar Definition         VRM Kohaku bone
─────────────────         ──────────────────────         ─────────────────
"mixamorig:Hips"     ──►  Humanoid muscle  ──►          "J_Bip_C_Hips"
"mixamorig:Spine"    ──►  "Spine"          ──►          "J_Bip_C_Spine"
"mixamorig:LeftArm"  ──►  "LeftUpperArm"   ──►          "J_Bip_L_UpperArm"
```

Beda bone naming, beda proporsi — Unity tetap bisa retarget pakai **muscle space** (relative joint angles, bukan world positions).

---

## 📊 MoCap Software Landscape (Complete Reference)

### 🆓 Free / Webcam-based

| Software | Platform | Output | Quality | Catatan |
|----------|----------|--------|---------|---------|
| **Rokoko Vision** ⭐ | Web (browser) | FBX, BVH | ⭐⭐⭐⭐ | Gratis selamanya, no install. https://www.rokoko.com/products/vision |
| **VSeeFace** ⭐ | Windows | VMD (via plugin), Live | ⭐⭐⭐⭐ | **VRM native**, VTuber standard. https://www.vseeface.icu |
| **Freemocap** | Win/Mac/Linux | FBX, BVH | ⭐⭐⭐⭐⭐ | Open-source, multi-webcam. https://github.com/freemocap/freemocap |
| **3tene Free** | Windows | VMD | ⭐⭐⭐ | VRM-friendly Japanese tool. https://3tene.com |
| **MikuMikuMoving** | Windows | VMD | ⭐⭐⭐ | MMD ecosystem free editor |
| **Warudo** | Windows | FBX, VMD | ⭐⭐⭐⭐ | Modern VTuber suite. https://warudo.app |

### 📱 Mobile / Cloud-based

| Software | Platform | Output | Pricing | Catatan |
|----------|----------|--------|---------|---------|
| **DeepMotion Animate 3D** ⭐ | Web upload | FBX, BVH, MP4 | Free 30sec/clip | **AI video upload** — paste video, dapat FBX. https://www.deepmotion.com |
| **RADiCAL Motion** | iOS/Android/Web | FBX, BVH | Free tier limited | Mobile real-time + upload. https://www.radicalmotion.com |
| **Move.ai** | iOS + Web | FBX, USD | $25+/bulan | Pro quality. https://www.move.ai |
| **Hyprmeet** | iOS | FBX | Paid | iPhone face+body. |
| **Plask Motion** | Web | FBX | Status uncertain | Was free, now under xperis 2024 |

### 🇯🇵 MMD Ecosystem (FREE Goldmine)

Pre-made animations dari professional anime animators — quality > AI video extraction untuk anime style.

| Source | URL | Content |
|--------|-----|---------|
| **NicoNico Commons** | https://commons.nicovideo.jp (search "MMD モーション") | Vocaloid dances, gestures |
| **BowlRoll** | https://bowlroll.net | Mass library MMD motions |
| **Tagged @Nico** | https://www.nicovideo.jp/tag/MMDモーション配布 | Curated motion data |
| **bear0830/mmd** | https://github.com/bear0830/mmd | GitHub VMD collection |
| **Mikudance** | https://mikudance.jp | Anime motion sharing |

Project sudah pakai sebagian via `unity/Assets/StreamingAssets/Anim/` (walk, nekomimi, foxsay, fuwari, heartbeat, baby).

### 💰 Pro / Hardware (Skip dulu)

- Rokoko Smartsuit Pro 2 ($2,500+)
- Xsens MVN Animate ($5,000+)
- iPi Motion Capture ($295-1,995)
- OptiTrack ($10,000+, studio level)
- Manus VR Quantum Gloves ($5,000, finger-only)

### 🔬 Open Source / Research-Grade

- **EasyMocap**: https://github.com/zju3dv/EasyMocap (Python, multi-view setup)
- **OpenMMPose**: https://github.com/open-mmlab/mmpose (developer-focused)
- **MediaPipe**: https://google.github.io/mediapipe (Google's pose detection, real-time)
- **Cascadeur**: https://cascadeur.com (AI-assisted keyframe animation, free tier)

---

## 🛣 Implementation Pipelines (4 Routes)

### Pipeline 1: VMD Route (PALING MUDAH — Native dengan Project)

Project sudah punya VMD playback infrastructure (`VmdPlaybackController`). Tinggal pakai.

```
[1] MoCap tool yang export VMD (VSeeFace, MMM, 3tene)
       ↓ rekam, save sebagai dance.vmd
[2] Convert VMD → JSON:
    cd /Users/lendra/Documents/codeV/LiaVA
    python3 unity/Tools/vmd_to_json.py dance.vmd dance.json
       ↓
[3] Copy ke StreamingAssets:
    cp dance.json unity/Assets/StreamingAssets/Anim/
       ↓ App load otomatis saat startup
[4] Trigger di dashboard chip
    → CommandReceiver.PlayVmd("dance")
    → VmdPlaybackController.Play("dance")
    → Kohaku bergerak
```

**JSON Structure**:
```json
{
  "name": "dance",
  "duration": 5.0,
  "frameRate": 30,
  "bones": {
    "Hips":          [{"frame":0, "r":[0,0,0,1]}, {"frame":30, "r":[0.1,0,0,0.99]}, ...],
    "Spine":         [...],
    "LeftUpperArm":  [...],
    "RightUpperArm": [...],
    "Head":          [...]
  }
}
```

`VmdPlaybackController` baca JSON → cari bone via `HumanBodyBones.LeftUpperArm` → set localRotation per frame.

**Pros**:
- No Unity rebuild needed (drop file, app load di startup)
- VMD dirancang khusus humanoid anime
- 100% reuse existing infrastructure
- File size kecil (~50-500 KB)

**Cons**:
- Format VMD niche (Japanese MMD community)
- Less common di Western MoCap tools

---

### Pipeline 2: FBX Route (Industri Standar)

Mainstream MoCap tools (Rokoko, DeepMotion, Move.ai) export ke **FBX**.

```
[1] MoCap tool export dance.fbx
       ↓
[2] Drag FBX ke Unity Editor → Assets/Animations/Custom/dance.fbx
       ↓
[3] Click file → Inspector → Rig tab:
   - Animation Type: Humanoid (KRITIKAL)
   - Avatar Definition: Create From This Model
   - [Apply]
       ↓ Unity build internal Avatar mapping
[4] Animation tab:
   - Rename clip "Take 001" → "MyWaveDance"
   - Loop Time: ✓
   - [Apply]
       ↓
[5] Wire ke AnimatorController:
   - Buka unity/Assets/Resources/LiaVAController.controller
   - State "Custom" (di ActionOverride layer)
   - Set Motion = MyWaveDance clip
       ↓
[6] Trigger dari dashboard:
   - Tap "Custom (GIF)" chip
   - CommandReceiver.PlayVmd("Custom") → ActionId=4
   - Animator transition ke "Custom" state
   - Unity Humanoid Retargeter auto-applies ke Kohaku
```

**Penting**:
- Setiap import FBX, WAJIB set `Animation Type=Humanoid` + `Create From This Model`
- Kalau lupa = dipakai sebagai "Generic" = janky pose

**Pros**:
- FBX = format paling kompatibel lintas tool
- Quality animation typically tinggi (smooth curves)
- Auto-retargeting Unity = no manual mapping

**Cons**:
- WAJIB Unity Editor rebuild + Gradle assemble untuk tambah animasi baru (FBX baked di build)
- Tidak bisa "drop file at runtime"
- FBX import 1x = ~10-30 detik wait di Unity Editor

---

### Pipeline 3: BVH Route (Less Common)

BVH = format lama, plain-text mocap.

```
[1] Tool export dance.bvh
       ↓
[2] Open Blender (free):
   File → Import → Motion Capture (.bvh) → dance.bvh
       ↓
[3] Blender: File → Export → FBX
   - Settings: Bake Animation ✓, NLA Strips ✓
       ↓
[4] Continue Pipeline 2 (FBX route)
```

Skip BVH kecuali tool kamu **cuma** support BVH.

---

### Pipeline 4: VSeeFace Route (BEST untuk VRM Specific) ⭐⭐⭐

VSeeFace **bisa load Kohaku.vrm langsung** sebagai source character. Output VMD sudah VRM bone-aware.

```
[1] Install VSeeFace (Windows free) https://www.vseeface.icu
       ↓
[2] Buka VSeeFace → Load VRM:
   File → Open VRM → assets/NFD_KohakuFullSet_V1.21/Kohaku.vrm
       ↓
[3] Aktifkan webcam tracking:
   - Body tracking: ON
   - Hand tracking: ON
   - Face tracking: ON (auto-map ke VRM BlendShapeProxy!)
       ↓
[4] Live preview: Kohaku mirror gerakan kamu
       ↓
[5] Tekan F8 (atau OBS-VMD plugin) → mulai recording
   - User joget/gesture
   - F8 lagi → stop
       ↓
[6] Output: dance.vmd (sudah dengan face blendshape data)
       ↓
[7] Convert + drop ke app (Pipeline 1)
```

**Pros**:
- **Face blendshape ke-capture langsung** (Joy, Sorrow, Wink, A/I/U/E/O) — fitur unik VRM-aware
- **No retargeting needed** — source character literally Kohaku
- VMD output = native pipeline support
- Hand tracking included
- Free

**Cons**:
- Windows only
- Butuh setup VSeeFace + Kohaku VRM file

**This is the OPTIMAL path untuk all-in-one capture (face + body + hand).**

---

### Pipeline 5: Video Upload (DeepMotion / RADiCAL) — Mobile-friendly

Untuk user yang mau upload video (e.g., orang joget) tanpa rekam live:

```
[1] Rekam video diri sendiri / download video reference
   - Single person, full body visible
   - Lighting cukup
   - Background contrast OK
       ↓
[2] Upload ke DeepMotion Animate 3D (https://www.deepmotion.com)
   - Sign up free
   - Upload .mp4 (max 30 detik free tier)
   - Pilih AI Settings (Default / Hi-Fi)
       ↓
[3] AI process (~2-3 menit), download FBX
       ↓
[4] Continue Pipeline 2 (FBX route)
```

**Pros**:
- Bisa pakai video apa pun (kamu joget, atau download referensi)
- Tidak butuh setup webcam + tripod
- Quality decent untuk casual use

**Cons**:
- Free tier limited (30 sec)
- AI bisa miss saat tangan occluded
- Video harus single person, full body

---

## 🎌 Penting: Video Anime vs Video Manusia

AI MoCap dilatih untuk **manusia nyata**. Video anime = hasil jelek/random.

**Solusi:**

### A. Rekam Manusia
- Cara orang biasa
- Upload video orang joget asli → DeepMotion → FBX → apply ke Kohaku
- Industry-standard approach

### B. Untuk Style Anime → MMD Library (GOLDMINE)
- Kalau mau anime/VTuber dance style, **MMD library lebih bagus** dari AI extraction
- Hasil animator profesional anime (bukan AI)
- 100% gratis, thousands of motions
- Format VMD = native pipeline

### C. Hybrid: Reference Video Anime → Manual Recreate
- Tonton clip anime
- Rekam diri sendiri niru gerakan
- Upload ke DeepMotion
- Pose anime ditranslate via gerakan kamu

---

## 📦 Project Integration Points

### Existing Pipeline (Sudah Ada)

| File | Lokasi | Function |
|------|--------|----------|
| `VmdPlaybackController.cs` | `unity/Assets/Scripts/VRMAssistant/Animation/` | Play VMD JSON, set bone localRotation per frame |
| `BoneMapper.cs` | `unity/Assets/Scripts/VRMAssistant/Core/` | Resolve HumanBodyBones → Transform |
| `CommandReceiver.cs` | `unity/Assets/Scripts/VRMAssistant/AI/` | Route Kotlin commands ke Unity (PlayVmd, dll) |
| `LiaVAController.controller` | `unity/Assets/Resources/` | AnimatorController 3-layer (Base, ActionOverride, FacialBlend) |
| `vmd_to_json.py` | `unity/Tools/` | VMD binary → JSON converter |
| Animation library | `unity/Assets/StreamingAssets/Anim/` | walk, nekomimi, foxsay, fuwari, heartbeat, baby |

### Yang Perlu Ditambah (untuk "Import Animation" feature)

| Komponen | Lokasi | Effort |
|----------|--------|--------|
| `AnimationImportScreen` (Compose) | `android/launcher/.../ui/import/` | 2-3 hari |
| Storage Access Framework picker | Built-in Android | 1 hari |
| VMD format validator | Reuse existing parser | 1 hari |
| FBX→JSON converter sidecar | Port `vmd_to_json.py` ke Kotlin atau Python sidecar | 3-4 hari |
| Dynamic chip dashboard | Modify `MainActivity.kt` | 1 hari |
| Animation library screen (list/delete/rename) | New screen | 2-3 hari |
| **Total** | | **~1.5-2 minggu** |

### Architecture Flow

```
Dashboard → tombol [📥 Import Animation]
                  ↓
            Android Storage Access Framework picker
                  ↓
            User pilih file VMD / .json / .fbx dari HP
                  ↓
            Validate format
                  ↓
            Convert (kalau perlu): FBX → VMD JSON
                  ↓
            Save ke /data/data/com.l3n.liaVA/files/animations/<name>.json
                  ↓
            Refresh dashboard → muncul chip baru otomatis
                  ↓
            Tap chip → play via VmdPlaybackController existing
```

### Format Support Priority

| Format | Source | Effort Import | Priority |
|--------|--------|---------------|----------|
| **VMD** | MMD, VSeeFace, MMD library | ✅ Direct (existing parser) | **HIGH** |
| VMD JSON | App-specific converted | ✅ Direct | HIGH |
| FBX | Rokoko, DeepMotion, Blender | 🟡 Hard (in-app FBX importer butuh Humanoid retarget) | MEDIUM |
| BVH | Webcam tools | 🟡 Medium (BVH→VMD converter available) | LOW |
| glTF | Modern tools | ❌ Skip | LATEST |

**Recommendation**: **VMD-only import MVP**. FBX/BVH user convert ke VMD di luar app (Blender / MMD Sizing tool).

---

## 🎯 Concrete Walkthrough Examples

### Example 1: VSeeFace untuk Animasi Full Face + Body + Hand

**Setup awal (one-time, ~15 menit)**:
1. Install VSeeFace di Windows (https://www.vseeface.icu)
2. Copy `Kohaku.vrm` dari `assets/NFD_KohakuFullSet_V1.21/` ke laptop
3. Load di VSeeFace → kalibrasi (T-pose 5 detik)
4. Test webcam, pastikan tracking smooth

**Recording (per animasi, ~2-5 menit)**:
5. Mundur ~2 meter dari laptop, full body visible
6. Tekan F8 → mulai record
7. User joget 5 detik
8. Tekan F8 → stop
9. File `motion_2026_01_15_123456.vmd` saved

**Apply ke App (~30 detik)**:
10. Copy VMD ke project:
    ```bash
    cp ~/Desktop/motion_*.vmd \
       /Users/lendra/Documents/codeV/LiaVA/temp/HappyBirthday.vmd
    ```
11. Convert ke JSON:
    ```bash
    cd /Users/lendra/Documents/codeV/LiaVA
    python3 unity/Tools/vmd_to_json.py \
       temp/HappyBirthday.vmd \
       unity/Assets/StreamingAssets/Anim/happybirthday.json
    ```
12. Tambah ke `VmdPlaybackController.preloadAnimNames`:
    ```csharp
    [SerializeField] private string[] preloadAnimNames = {
        "walk", "nekomimi", "foxsay", "fuwari", "heartbeat", "baby",
        "happybirthday"  // ← new
    };
    ```
13. Add chip di dashboard (`MainActivity.kt`):
    ```kotlin
    AssistedChip("🎂 HappyBirthday") {
        UnityBridge.sendMessage("VRMAssistant", "PlayVmd", "happybirthday")
    }
    ```
14. Rebuild + install APK
15. Buka Lia → tap chip → Kohaku joget identik dengan user ✨

### Example 2: DeepMotion Animate 3D (Video Upload, No Laptop Required)

1. Buka https://www.deepmotion.com/animate-3d/
2. Sign up free
3. Upload video 20-30 detik (rekam HP)
4. Pilih AI Settings = Default
5. Tunggu process ~2-3 menit
6. Download FBX

In Unity:
7. Drag FBX ke `unity/Assets/Animations/Custom/`
8. Inspector → Rig → Animation Type=Humanoid → Apply
9. Animation tab → rename clip
10. Wire ke AnimatorController state "Custom"
11. Rebuild app

### Example 3: MMD Library (Zero Cost, Zero Recording)

1. Buka https://bowlroll.net
2. Search "VMD dance"
3. Download VMD gratis (e.g., "Sailor Moon Dance.vmd")
4. Convert + drop:
   ```bash
   python3 unity/Tools/vmd_to_json.py \
      ~/Downloads/sailor_moon.vmd \
      unity/Assets/StreamingAssets/Anim/sailormoon.json
   ```
5. Tambah ke `preloadAnimNames` + dashboard chip
6. Rebuild + test

**Pipeline terbukti tanpa beli/install tool apa pun.**

---

## 🚧 Common Gotchas + Solutions

### 1. Bone Mismatch — Hasil Pose "Patah"
- **Cause**: FBX di-import sebagai "Generic" bukan "Humanoid"
- **Fix**: Inspector → Rig → Animation Type = Humanoid

### 2. Character "Tenggelam" atau "Melayang"
- **Cause**: Foot IK belum di-handle, scale beda
- **Fix**: Animation Clip Inspector → "Bake into Pose" untuk Position Y → "Original" or "Center of Mass"

### 3. Face Blendshape Tidak Muncul
- **Cause**: FBX/BVH cuma bawa bone data, tidak face shape
- **Fix**: Pakai VSeeFace yang face-aware (Pipeline 4)

### 4. Hair/Tail Tidak Bergerak Natural
- **Cause**: Spring bones tidak ke-capture MoCap (memang by design)
- **Fix**: Pastikan VRM Spring Bone component aktif — physics auto-handle saat body bergerak

### 5. Frame Rate Aneh
- **Cause**: VMD = 30fps, FBX bisa 24/30/60 fps
- **Fix**: Otomatis di-interpolate. Kalau jittery, set Animation Importer Sample Rate = native fps

### 6. Hand/Finger Glitchy
- **Cause**: MoCap hand tracking inaccurate saat occluded
- **Fix**:
  - Pastikan tangan visible ke kamera selama recording
  - Atau body-only + overlay hand pose preset di app

### 7. Anime Video Hasil Random
- **Cause**: AI dilatih untuk human, anime proportion beda
- **Fix**: Gunakan MMD library yang sudah dibuat oleh animator anime profesional

---

## 🎯 Recommended Action Plan

### Phase A — Quick Win (3-5 hari) 💡

**Audit existing VMD library + expand dashboard**:
- Cek semua VMD/JSON di `unity/Assets/StreamingAssets/Anim/`
- Cek `assets/Sound/VMD/` untuk additional resources
- Tambah chip dashboard untuk setiap VMD (atau dropdown "Motion Library")
- Document cara user bisa drop VMD baru manually

### Phase B — Import Feature (1-2 minggu)

**Build "Import Animation" UI**:
- Storage picker (SAF)
- VMD validation + save ke local storage
- Dynamic chip generation
- List/delete/rename animations
- User guide: "Cara bikin animasi dengan Rokoko Vision / VSeeFace / DeepMotion"

### Phase C — Quality of Life (1 minggu)

- Preview di import screen (play first 3 detik)
- Mass import (folder)
- Animation tagging (#dance, #idle, #gesture)
- Cloud download? (no, user said HP-only)

### Phase D — Optional Power-up (later)

- In-app simple Puppet Mode untuk quick adjustments
- FBX direct import (butuh Unity FBX SDK)
- BVH support
- VMD recorder dalam app (untuk pakai dengan VSeeFace plugin)

---

## ⚖️ Decision Matrix

| Pilihan | Effort | Output Quality | Recommend For |
|---------|--------|----------------|----------------|
| **Skip Studio, polish existing app** | Zero | Use existing VMD | Quick ship |
| **Build "Import" feature** ⭐ | 1-2 minggu | High (use any MoCap tool) | Best ROI |
| **Build full MoCap in-app** | 3-4 bulan | Variable | Long-term, commitment besar |
| **Build Puppet Mode** | 3-4 minggu | Geometric | Power users |

**Saran**: **Build "Import Animation" feature**. User dapat fleksibilitas pilih MoCap tool apa saja, app cukup play.

---

## 🤔 Open Questions untuk Next Session

Disimpan untuk lanjut decision:

1. **MoCap tool yang appeal ke user**:
   - Laptop + Rokoko Vision (free, webcam)?
   - HP + RADiCAL/DeepMotion (free tier limit)?
   - Windows + VSeeFace (VRM native, gratis)?
   - Atau pakai VMD library aja?

2. **Format prioritas import**:
   - VMD only (mudah, fit pipeline)
   - VMD + FBX (lebih kompatibel, effort 2x)

3. **OK ada step "convert FBX → VMD di Blender" untuk user?**
   - Atau wajib FBX direct import di app?

4. **Naming feature ini**:
   - "Import Animation"
   - "Animation Library"
   - "Studio Lia"
   - "Sanggar Lia"

5. **Apakah user punya laptop Windows untuk VSeeFace?**

6. **Next concrete step**:
   - Download VMD MMD trial dulu (zero effort)
   - Install VSeeFace + start recording
   - Build "Import Animation" feature
   - Audit existing VMD library

---

## 📚 References + Links

### MoCap Tools
- DeepMotion Animate 3D: https://www.deepmotion.com/animate-3d
- RADiCAL Motion: https://www.radicalmotion.com
- Move.ai: https://www.move.ai
- Rokoko Vision: https://www.rokoko.com/products/vision
- VSeeFace: https://www.vseeface.icu
- Freemocap: https://github.com/freemocap/freemocap
- Warudo: https://warudo.app
- 3tene: https://3tene.com
- Plask: https://plask.ai

### MMD Library
- NicoNico Commons: https://commons.nicovideo.jp
- BowlRoll: https://bowlroll.net
- bear0830/mmd: https://github.com/bear0830/mmd
- Mikudance: https://mikudance.jp

### VRM Docs
- VRM Spec: https://vrm.dev/en/
- UniVRM: https://github.com/vrm-c/UniVRM
- Unity Humanoid Avatar: https://docs.unity3d.com/Manual/AvatarCreationandSetup.html

### Project Files Referenced
- `unity/Assets/Scripts/VRMAssistant/Animation/VmdPlaybackController.cs`
- `unity/Assets/Scripts/VRMAssistant/AI/CommandReceiver.cs`
- `unity/Assets/Resources/LiaVAController.controller`
- `unity/Tools/vmd_to_json.py`
- `unity/Assets/StreamingAssets/Anim/*.json`
- `android/launcher/src/main/java/com/l3n/liaVA/MainActivity.kt`

---

## ✅ TL;DR untuk Next Session

**User mau apply MoCap animation ke Kohaku VRM**.

**Keputusan**: Tidak build MoCap in-app, **gunakan tool eksternal + build "Import Animation" feature** di app.

**Pipeline pilihan utama**: **Pipeline 1 (VMD)** — native dengan existing infrastructure.

**Tools recommended**:
- VSeeFace (Windows, free, VRM-aware, face+body+hand)
- DeepMotion Animate 3D (web, video upload, FBX output)
- MMD library (NicoNico/BowlRoll) — free professional anime motions

**Next steps**:
1. User test MoCap tool pilihannya (atau pakai MMD library dulu)
2. Audit existing VMD assets di project
3. Build "Import Animation" feature di app (1-2 minggu)

**Effort**: 1-2 minggu vs 3-4 bulan kalau build in-app MoCap.
