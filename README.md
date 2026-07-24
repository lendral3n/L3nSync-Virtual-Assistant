# LiaVA — Floating VRM AI Companion

**Lia** (Kohaku) is a floating anime **VRM** character that lives on your screen — on **macOS** as a transparent always-on-top desktop companion, and on **Android** as an overlay that walks along the bottom of your phone. She listens, talks, reacts, and animates with real mocap — an AI companion you can actually see and talk to.

> **Engine:** Unity `6000.4.6f1` + URP 17.4 · **Character:** UniVRM (VRM 0.x) · **Platforms:** macOS (Apple Silicon) + Android 8+ · **Bundle:** `com.l3n.liaVA`

---

## ✨ Features

- **Floating VRM character** — Kohaku rendered live; on macOS a borderless, transparent, click-through always-on-top window (native Objective-C++ overlay plugin); on Android a bottom overlay strip you can use the phone through.
- **Talk to her by voice** — wake word *"Lia"*, continuous listening with VAD, natural spoken replies.
- **Multi-backend AI brain** — pluggable, no rebuild needed:
  - **macOS:** Google **Gemini** *or* **self-hosted Ollama** (`gpt-oss:120b` on private VMs) — toggle via `lia_ai.env`.
  - **Android:** **on-device Gemma 4** (fully local, offline-capable).
- **Voice pipeline** — STT (self-hosted **faster-whisper**, multilingual) → LLM → **ElevenLabs** TTS, with lip-sync.
- **Real mocap animation** — Bandai Namco Research + Mixamo clips and **VRMA** poses, retargeted onto the VRM at runtime via `HumanPoseHandler` (muscle-space) — the AI itself decides which gesture fits its reply.
- **Aliveness** — cursor awareness, look-at, blush-on-pet, roaming to empty screen areas, footstep/magic-circle VFX.
- **In-app animation picker** — browse and choose which gestures Lia uses, with click-to-preview.

---

## 🏗 Architecture

One Unity project targets **both** macOS and Android; platform specifics live behind thin adapters.

```
LiaVA/
├── unity/                              # Unity project (shared Mac + Android)
│   └── Assets/
│       ├── Scripts/VRMAssistant/
│       │   ├── AI/          # LiaBrain (Gemini/Ollama), LiaPersona, VoiceListener, CommandReceiver
│       │   ├── Animation/   # ClipGestureController, VrmaPlaybackController, LipSync, LookAt, blink…
│       │   ├── Behavior/    # BehaviorScheduler, GestureLibrary, CharacterMovement, Aliveness
│       │   ├── Platform/    # MacDesktopController (overlay)
│       │   ├── Core/        # VRMModelLoader (runtime VRM load + URP material convert)
│       │   └── UI/          # Chat / animation-settings / voice-status panels (IMGUI)
│       ├── Scripts/BvhBrowser/          # Companion tool: browse 3k+ Bandai BVH, preview on VRM
│       ├── Shaders/                     # LiaVA/UnlitSolid (opaque-over-transparent)
│       ├── Editor/                      # Mac build scripts
│       └── Resources/ · Settings/ · Scenes/
├── android/                            # Android Studio (Unity-as-a-Library wrapper, Kotlin/Compose)
│   └── launcher/…                      # Dashboard, OverlayService, UnityBridge, on-device Gemma 4
├── mac-plugin-src/                     # Native macOS overlay plugin (LiaWindow.mm, Objective-C++)
└── docs/                               # Design & engineering notes
```

**Runtime animation retargeting** is the core trick: Unity's Mecanim does not retarget onto a *runtime-loaded* VRM, so mocap clips (`.anim`) and `.vrma` are played on a hidden source rig and transferred to Kohaku in muscle space via `HumanPoseHandler` every `LateUpdate`.

---

## 🧩 Tech Stack

| Layer | Tech |
|-------|------|
| 3D / render | Unity 6000.4.6f1, URP 17.4 |
| Character | UniVRM 0.131 (VRM 0.x) + UniVRM10 (`.vrma`) |
| macOS overlay | Objective-C++ plugin (Cocoa / QuartzCore / CoreGraphics), Metal |
| Android shell | Kotlin + Jetpack Compose, Unity-as-a-Library, on-device Gemma 4 |
| AI (desktop) | Gemini API · Ollama (self-hosted `gpt-oss:120b`) |
| Voice | faster-whisper (STT) · ElevenLabs (TTS) |
| Tooling | Python (BVH/VMD converters), Blender |

---

## 🚀 Build (macOS)

Requires Unity `6000.4.6f1`.

```bash
# 1) Native overlay plugin (once)
cd mac-plugin-src && ./build.sh      # → LiaWindow.bundle (universal arm64+x86_64)

# 2) Build the app (batchmode)
/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit \
  -projectPath unity \
  -executeMethod LiaVA.Editor.LiaVAMacBuildScript.BuildMac \
  -logFile build.log
# → build-mac/LiaVA.app
```

### AI configuration (`build-mac/lia_ai.env`)

The app reads keys/config from `lia_ai.env` next to `LiaVA.app`. **This file is git-ignored — never commit it.**

```ini
# Backend: "gemini" (cloud) or "ollama" (self-hosted)
LLM_BACKEND=ollama
OLLAMA_URL=http://<vm-ip>:11434
OLLAMA_MODEL=gpt-oss:120b
STT_URL=http://<vm-ip>:9000          # faster-whisper server (for voice)

GEMINI_API_KEY=…                     # if LLM_BACKEND=gemini
ELEVENLABS_API_KEY=…                 # optional, for TTS
```

---

## 📦 Assets not included (licensing)

To keep the repo lean and avoid redistributing third-party content, these are **git-ignored** — add them locally:

- **Kohaku VRM** (`unity/Assets/StreamingAssets/*.vrm`) — NFD "Kohaku" model; download from the official source and drop it in.
- **Bandai Namco Research Motion Dataset** (BVH) — used by the BvhBrowser tool; fetch from its official repository.
- Large imported model folders, uLipSync samples, and Blender sources are regenerable / third-party.

---

## 🗺 Roadmap

- Procedural **aliveness layer** on top of mocap: idle breathing, subtle hip sway, auto-blink, gaze control (paused on bone-level during big motions; blink + gaze always on).
- Export chosen Bandai BVH → LiaVA animation clips.
- Deeper multi-model routing and richer voice interaction.

---

## 🙏 Credits & Licenses

- **Kohaku** VRM model — © its respective creator (NFD); not redistributed here.
- **Bandai Namco Research Motion Dataset** — © Bandai Namco Research; used under its license.
- **UniVRM** (MIT), **ElevenLabs**, **Google Gemini / Gemma**, **Ollama / gpt-oss**.
- Application code in this repository: see [LICENSE](LICENSE).

---

*Built by [@lendral3n](https://github.com/lendral3n). Backend & AI integration; collaborating on animation & character expression.*
