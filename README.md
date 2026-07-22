# Lia VA

Floating VRM anime assistant overlay untuk Android — character Kohaku yang hidup di bottom strip layar HP, bisa idle/aktif/jalan-jalan, dan responsif ke command via AI dispatcher.

> **Bundle**: `com.l3n.liaVA` · **Version**: 0.1 · **Target**: Android 8+ (minSdk 26, targetSdk 34) · **Arch**: arm64-v8a

---

## 🎯 Visi

Anime VTuber-style assistant yang **hidup di bawah HP** seperti pet/companion app. Character (Kohaku, VRM 0.x) di overlay transparent strip — user tetap bisa pakai HP normal sambil Lia idle, walking, atau reaksi ke perintah.

```
┌─────────────────────────────────────┐
│                                     │
│        (app user normal)            │
│         home/chrome/dll             │
│                                     │
│                                     │
├─────────────────────────────────────┤  ← strip 250dp landscape
│                                     │
│   ✨    🚶 Lia (Kohaku VRM)         │  ← walking left-right
│                                     │
└─────────────────────────────────────┘
```

---

## 🏗 Arsitektur Stack

| Layer | Tech | Purpose |
|-------|------|---------|
| **3D render** | Unity 6.4 (`6000.4.6f1`) + URP 17.4 | VRM rendering, animation, transparent overlay |
| **Character model** | UniVRM 0.131 (`com.vrmc.univrm`) | VRM 0.x runtime loading, BlendShape, LookAt |
| **Embed** | Unity-as-a-Library (UaaL) | Unity diekspor sebagai Android library module |
| **Native shell** | Android Kotlin Compose | Dashboard UI, foreground service, WindowManager overlay |
| **Overlay** | `TYPE_APPLICATION_OVERLAY` + `FLAG_NOT_TOUCHABLE` | Full-width 250dp strip flush bottom, touch passthrough |
| **AI dispatcher** | `UnityBridge.sendMessage` (UnitySendMessage) | Kotlin → Unity command bus untuk animation, gestures, expressions |
| **MCP** | CoplayDev `com.coplaydev.unity-mcp` | Editor automation, build trigger from Claude Code |

---

## 📂 Struktur Repository

```
LiaVA/
├── .git/                          # Git history
├── .gitignore                     # Unity + Android + macOS ignores
├── LICENSE
├── README.md                      # (this file)
│
├── unity/                         # 🎮 Unity project (Lia VA scene + scripts)
│   ├── Assets/
│   │   ├── Scenes/Main.unity      # Active scene (camera + character + lighting)
│   │   ├── Scripts/VRMAssistant/  # Animation, AI, Behavior, Locomotion, Rendering
│   │   ├── Animations/            # 6 core anim + Locomotion + Custom (.anim, .controller, .mask)
│   │   ├── Resources/             # LiaVAController.controller (loaded di runtime)
│   │   ├── Settings/              # URP asset, RendererData
│   │   └── Editor/                # LiaVABuildScript.cs (one-click Android export)
│   ├── Packages/manifest.json     # UniVRM + URP + MCP + AI Assistant
│   └── ProjectSettings/
│
├── android/                       # 📱 Android Studio Gradle project (UaaL wrapper)
│   ├── launcher/                  # APK module — MainActivity + OverlayService + Compose dashboard
│   │   └── src/main/java/com/l3n/liaVA/
│   │       ├── MainActivity.kt           # Compose dashboard
│   │       ├── OverlayService.kt         # Foreground service + WindowManager
│   │       ├── UnityBridge.kt            # Kotlin → Unity command bus
│   │       └── PermissionHelper.kt       # Overlay + battery + MIUI autostart
│   ├── unityLibrary/              # Unity-exported Android module (regenerable)
│   ├── shared/                    # Common Gradle config (Unity symbol keep, etc.)
│   ├── build.gradle               # Root build config
│   └── .gradle-customizations-backup/   # Restore script utk Unity-overwritten gradle files
│       ├── restore.sh
│       ├── build.gradle.root
│       ├── build.gradle.launcher
│       ├── gradle.properties
│       ├── AndroidManifest.xml
│       └── strings.xml
│
├── assets/                        # 📦 Raw materials (VRM source, sounds, NFD packs)
│   ├── NFD_KohakuFullSet_V1.21/
│   ├── NFD_Kohaku_Head_ForMARUBODY_V1.00/
│   └── Sound/
│
└── docs/                          # 📖 Documentation & diagrams
    ├── Blueprint_App_Flow.md
    ├── Flowchart_Arsitektur.md
    ├── Roadmap_Developer.md
    ├── Roadmap_TEAM.txt
    ├── BoneMap_Kohaku.txt
    ├── kohaku_Map.md
    ├── animation_roadmap.txt
    ├── formula_animation.md
    └── appArch.png / appFlow.png
```

---

## 🧬 Pipeline Animasi (procedural-first, sejak 2026-07-22)

> Animator Controller + clip muscle hand-authored DIHAPUS — nama muscle-nya invalid
> (tidak pernah nge-bind) dan Animator humanoid menimpa seluruh pose tiap frame.
> Arsitektur sekarang: **3 lapisan runtime tanpa Animator Controller**.

```
┌─ Layer 1: Procedural states (AnimationOrchestrator) ────────────┐
│  Idle/Active/Thinking/Listening/Speaking                        │
│  breathing + sway + head motion + hand pose + blink + look-at   │
│  (AdditiveLayerHelper di atas rest pose A-pose 88°)             │
└─────────────────────────────────────────────────────────────────┘
┌─ Layer 2: Playback (mutual-exclusive dengan Layer 1) ───────────┐
│  VMD  — VmdPlaybackController (StreamingAssets/Anim/*.json)     │
│  VRMA — VrmaPlaybackController (StreamingAssets/VRMA/*.vrma)    │
│         retarget HumanPoseHandler, body di-pin ke baseline      │
└─────────────────────────────────────────────────────────────────┘
┌─ Layer 3: Facial (VRMBlendShapeProxy, selalu aktif) ────────────┐
│  LipSyncController — FFT audio ATAU pola sintetis Perlin        │
│  AutoBlink + ExpressionController + LookAt                      │
└─────────────────────────────────────────────────────────────────┘
```

### Command flow

```
Compose chip tap          UnityBridge                CommandReceiver
─────────────────────    ───────────────             ──────────────────
"Thinking" chip      →   sendMessage(           →   PlayVmd("Thinking")
                          "VRMAssistant",                  │
                          "PlayVmd",                       ▼
                          "Thinking")               TryRouteToState():
                                                    stateManager.SetState(Thinking)
                                                          │
                                                          ▼
                                                    Orchestrator → procedural state
                                                    + HandPose NearFace + LookAt
```

---

## 🚀 Build & Run

### Prerequisites

- **Unity 6.4** (`6000.4.6f1`) dengan Android Build Support + IL2CPP
- **Android SDK** dengan build-tools 36.0.0, NDK 27.2.12479018 (bundled di Unity Hub)
- **ADB** terhubung ke device (USB / wireless)
- **Permission** di HP: Overlay (tampil di atas app lain), Battery whitelist, MIUI autostart (kalau MIUI/HyperOS)

### Build flow

```bash
# 1. Unity Editor → File > Build And Run
#    atau menu: Lia VA > Build Android (Export to LiaVA-Android)
#    Output: android/unityLibrary/

# 2. Restore Gradle customizations (Unity overwrite gradle file tiap export)
cd android
bash .gradle-customizations-backup/restore.sh

# 3. Gradle assembleDebug (pakai Unity-bundled Java + Gradle launcher)
export JAVA_HOME=/Applications/Unity/Hub/Editor/6000.4.6f1/PlaybackEngines/AndroidPlayer/OpenJDK
java -classpath /Applications/Unity/Hub/Editor/6000.4.6f1/PlaybackEngines/AndroidPlayer/Tools/gradle/lib/gradle-launcher-9.1.0.jar \
     org.gradle.launcher.GradleMain :launcher:assembleDebug --no-daemon

# 4. Install + launch
adb install -r launcher/build/outputs/apk/debug/launcher-debug.apk
adb shell am start -n com.l3n.liaVA/.MainActivity
```

---

## 🧪 Test Animation

Dashboard di app punya 7 chip test + pemilih karakter (Dress/Kimono, persist):

| Chip | Route | Effect |
|------|-------|--------|
| **Idle** | state Idle (procedural) | Breathing + sway + head turn |
| **Active** | state Active (procedural) | Napas cepat + spine lean + arm micro |
| **Thinking** | state Thinking (procedural) | Head tilt + hand pose NearFace |
| **Listening** | state Listening (procedural) | Lean forward + head nod |
| **Speaking** | state Speaking (procedural) | Body emphasis + lipsync ON |
| **LipSync** | LipSyncController.active=true | Mulut A/I/U/O — audio FFT atau sintetis |
| **⏹ Stop** | StopVmd | VMD+VRMA stop, lipsync off, state Idle |

Semua animasi lain (VMD by-name, gesture VRMA, wander, expression, switch karakter)
dipicu via command — jalur yang sama yang nanti dipakai AI (`AICommandDispatcher`).

Gesture VRMA (via `TriggerGesture` / BehaviorScheduler acak): Wave→Goodbye, Peace→Clapping,
HairTouch→Blush, Tilt→LookAround, Peek→Surprised, plus Thinking/Relax/Sad/Sleepy/Angry/Jump
— 11 file di `StreamingAssets/VRMA/`, retarget HumanPoseHandler dengan body pinned.

---

## 🔑 Permission Required

```xml
<!-- launcher/src/main/AndroidManifest.xml -->
<uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW"/>
<uses-permission android:name="android.permission.FOREGROUND_SERVICE"/>
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_SPECIAL_USE"/>
<uses-permission android:name="android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS"/>
```

Plus user-facing flow di MainActivity dashboard untuk minta runtime permission.

---

## 🎨 Character

**Kohaku** (なっふな堂) — VRM 0.x anime girl model.

- Humanoid Avatar dengan VRM bones (HumanBodyBones mapping)
- BlendShapeProxy untuk facial expressions (Joy, Sorrow, Angry, A/I/U/E/O lipsync)
- VRMLookAtHead untuk eye tracking
- Source: `assets/NFD_KohakuFullSet_V1.21/` (VRM 0.x)

---

## 🧰 MCP Integration (Editor automation)

Project pakai CoplayDev `com.coplaydev.unity-mcp` untuk:
- Read Unity console + project state dari Claude Code
- Execute C# code in-Editor (refactor, scene mutation, build trigger)
- Menu item execution (`Lia VA > Build Android`)

Setup di `~/Library/Application Support/Claude/claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "unity-editor": {
      "command": "python3",
      "args": ["/Users/lendra/Documents/codeV/mcp-stdio-bridge.py"],
      "env": { "MCP_HTTP_URL": "http://127.0.0.1:8080/mcp" }
    }
  }
}
```

> Note: Plugin WebSocket keep-alive bug di CoplayDev 3.2.4 kadang putus — restart bridge dari Unity menu `Window > MCP for Unity > Start Bridge`.

---

## 📋 Status & Roadmap

### ✅ Phase A — Foundation (DONE)
- Transparent overlay (alpha=0 backdrop) ✓
- Camera framing (full body visible) ✓
- 6 core Animator clips + AnimatorController (LiaVAController) ✓
- Animation routing: dashboard chip → Animator params ✓

### ✅ Phase B — Stability (DONE)
- Anti-crash: Restore procedural-skip when Animator present ✓
- Character Y=-1.0 anti-"terbang" (feet di strip bottom) ✓
- A-pose arm angle 88° (lebih natural) ✓
- Landscape strip overlay (MATCH_PARENT × 250dp flush bottom) ✓

### ✅ Phase C — Animation Rework (DONE 2026-07-22)
- Root cause animasi mati ditemukan & dibereskan: clip muscle invalid dihapus,
  Animator Controller dilepas, procedural-first restored
- Scene dibersihkan: VRMRoot statis (dobel karakter) + AnimationTestBench dihapus
- A-pose arm rest fix (sign terbalik → lengan ke atas), verified via screenshot loop
- VRMA gesture playback ENABLED: 11 gesture, retarget HumanPoseHandler,
  fix drift/sink (baseline body pin + hips pin), auto-stop fallback
- LipSync sintetis (Perlin) saat tanpa audio — Speaking hidup sebelum TTS ada
- Walk ditunda sampai ada walk clip humanoid asli (Mixamo — Lapisan 2)

### 🔄 Phase C.5 — Animation Assets (NEXT)
- Mixamo FBX humanoid (walk cycle, talk gestures, idle variations)
- VMD tambahan dari MMD library (BowlRoll/NicoNico)
- Video-to-motion custom (DeepMotion/Rokoko Vision)

### ✅ Phase D — AI Integration (DONE 2026-07-22, verifikasi runtime saat build)
- **Otak**: Google Gemini free tier (`gemini-2.0-flash`) — `ai/GeminiClient.kt`.
  Balasan JSON `{say, emotion, gesture}`, riwayat percakapan 16 turn.
- **Suara**: ElevenLabs TTS (`ai/ElevenLabsClient.kt`) → MP3 ke cacheDir →
  Unity `PlayAudio(path)` load + play + lipsync FFT dari audio asli + auto-Idle.
- **Orkestrasi**: `ai/LiaBrain.kt` — user text → Gemini → ekspresi + gesture + suara.
- **UI**: `ui/ChatScreen.kt` (ngobrol ketik) + kartu Setelan AI (API key in-app,
  tersimpan `AiPrefs` app-private). Persona Lia teman ngobrol = `ai/LiaPersona.kt`.
- API key diisi user langsung di app (Gemini wajib, ElevenLabs opsional untuk suara).

### 🔮 Selanjutnya (FUTURE)
- Voice input (STT) — sekarang input via ketik
- WanderController auto-walk (butuh walk clip Mixamo dulu)
- Chat langsung dari overlay (sekarang via dashboard)

Lihat detail di `docs/Roadmap_Developer.md` + `docs/Roadmap_TEAM.txt`.

---

## 🔗 References

- **VRM Spec**: https://vrm.dev/en/
- **UniVRM**: https://github.com/vrm-c/UniVRM
- **Unity UaaL**: https://docs.unity3d.com/Manual/UnityasaLibrary-Android.html
- **CoplayDev MCP for Unity**: https://github.com/CoplayDev/unity-mcp

---

## 📝 License

Lihat [LICENSE](LICENSE).

Kohaku VRM model © なっふな堂 — used under VRM model creator license. Lihat metadata VRM file untuk full terms.
