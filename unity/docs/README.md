# Lia VA — Documentation

Dokumentasi lengkap untuk project **Lia VA** (Floating VRM Anime Character Overlay Android). Foundation untuk animation work, bone manipulation, dan future AI integration.

---

## 📚 Documents Index

### Core References
| Document | Purpose | Audience |
|----------|---------|----------|
| **[bone-mapper.md](bone-mapper.md)** | Complete bone mapping Kohaku VRM (53 humanoid bones) — names, hierarchy, axis convention, code patterns | Animator, Developer |
| **[bone-axes-tested.md](bone-axes-tested.md)** | Live tracker tested axis behaviors (✅/❌/🟡 status per bone) | Developer working on animation |
| **[kohaku-bones.json](kohaku-bones.json)** | Machine-readable bone reference (parse di tool/script) | Automation, AI |
| **[animation-from-anime.md](animation-from-anime.md)** | Pipeline lengkap convert video anime → Lia animation | Animator |

### TODO (next docs)
- `architecture.md` — Phase 1-3 system architecture (Unity ↔ Kotlin bridge, Service overlay, AI command flow)
- `phase-roadmap.md` — Detailed phase planning (current Phase 2.6 done, Phase 3 backend AI integration)
- `device-setup.md` — Xiaomi 17 Pro Max specific (HyperOS permissions, autostart, wireless ADB)
- `troubleshooting.md` — Common issues + fixes (T-pose distortion, Unity domain reload stuck, MCP disconnect)

---

## 🎯 Quick Start untuk Animation

### Saya mau bikin gesture baru (e.g., wave dari video anime)

1. Read [animation-from-anime.md](animation-from-anime.md) section 3 (Plask Motion workflow)
2. Convert video → FBX via Plask AI
3. Convert FBX → VRMA via [tk256ailab/fbx2vrma-converter](https://github.com/tk256ailab/fbx2vrma-converter)
4. Drop `.vrma` ke `Assets/StreamingAssets/VRMA/`
5. Register di `VrmaPlaybackController.vrmaPool`:
   ```csharp
   new VrmaEntry { gestureName = "MyGesture", vrmaFileName = "my_gesture.vrma" }
   ```
6. Trigger via Compose UI atau backend AI:
   ```kotlin
   UnityBridge.sendMessage("VRMAssistant", "TriggerGesture", "MyGesture")
   ```

### Saya mau tweak procedural animation existing (e.g., breathing slower)

1. Read [bone-mapper.md](bone-mapper.md) section 4 (Animation Code Patterns)
2. Edit `Assets/Scripts/VRMAssistant/Animation/IdleAnimationState.cs`:
   ```csharp
   [SerializeField] private float breathFrequency = 0.20f;  // turun = lebih lambat
   ```
3. Build & deploy via gradle:
   ```bash
   cd /Users/lendra/Documents/Projects/L/LiaVA-Android
   JAVA_HOME=... ./gradlew :launcher:assembleDebug
   adb install -r launcher/build/outputs/apk/debug/launcher-debug.apk
   ```

### Saya mau verify axis bone tertentu

1. Read [bone-axes-tested.md](bone-axes-tested.md) untuk tested axes existing
2. Add temporary log di code:
   ```csharp
   var bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
   Debug.Log($"[Test] world={bone.up}, {bone.right}, {bone.forward}");
   ```
3. Build, deploy, run di device, capture log:
   ```bash
   adb logcat -d 2>&1 | grep "Test"
   ```
4. Update [bone-axes-tested.md](bone-axes-tested.md) dengan result

---

## 📦 Project Structure (relevant ke docs)

```
OC-X1/                                ← Unity project
├── Assets/
│   ├── Animations/Clips/VRMA/        ← .vrma files (11 clips dari tk256ailab)
│   ├── StreamingAssets/
│   │   ├── Kohaku_dress_1.10_VRM.vrm ← Source character
│   │   └── VRMA/                     ← Runtime-loadable VRMA copies
│   └── Scripts/VRMAssistant/
│       ├── Core/                     ← BoneMapper, VRMModelLoader, StateManager
│       ├── Animation/                ← Procedural states, VrmaPlayback, BlendShapeControllers
│       ├── Behavior/                 ← BehaviorScheduler, MovementController
│       └── AI/                       ← CommandReceiver (UnitySendMessage handlers)
├── docs/                             ← (you are here)
│   ├── README.md
│   ├── bone-mapper.md
│   ├── bone-axes-tested.md
│   ├── animation-from-anime.md
│   └── kohaku-bones.json
└── Scenes/Main.unity                 ← Scene utama: VRMAssistant root + Camera + Lights
```

```
LiaVA-Android/                        ← Gradle Android project (Unity exported)
├── launcher/                         ← Native Android app (Compose + Kotlin)
│   └── src/main/java/com/l3n/liaVA/
│       ├── MainActivity.kt           ← Compose control panel UI
│       ├── OverlayService.kt         ← Foreground service hosting Unity overlay
│       ├── UnityBridge.kt            ← UnitySendMessage wrapper
│       ├── AICommand.kt              ← AI command schema (sealed class)
│       └── AICommandDispatcher.kt    ← Route AI commands → Unity
├── unityLibrary/                     ← Unity exported library (auto-regenerated)
└── shared/, build.gradle, ...        ← Shared Gradle config
```

---

## 🛠 Key Code Files

### Unity-side
- **`AnimationOrchestrator.cs`** — central coordinator: state machine + procedural Tick + rest pose cache + auto-attach Phase 2.6 components
- **`VrmaPlaybackController.cs`** — Runtime VRMA loader + HumanPoseHandler retargeting (currently safe-disabled, defer Phase 3 untuk proper axis fix)
- **`AdditiveLayerHelper.cs`** — Critical fix: `bone.localRotation = restPose * offset` (no accumulation)
- **`CommandReceiver.cs`** — UnitySendMessage handlers (TriggerGesture, SetCharacterScale, SetExpression, dll)

### Kotlin-side
- **`OverlayService.kt`** — Foreground Service + WindowManager overlay + Unity lifecycle (recursion guard di Phase 2.6 fix)
- **`UnityBridge.kt`** — `UnityPlayer.UnitySendMessage(go, method, arg)` wrapper
- **`AICommand.kt`** + **`AICommandDispatcher.kt`** — JSON command schema + parser/dispatcher untuk Phase 3 AI backend

---

## 🎨 Animation Workflow Tier

**Tier 1 — Procedural (FAST, current default)**
- Sin wave bone rotation di IAnimationState.Tick
- Cocok: breathing, sway, micro-movement
- Cost: minimal, immediate feedback

**Tier 2 — VRMA Clip Playback**
- Free anime-style clips dari github.com/tk256ailab
- Cost: ~120KB per clip, retargeting via HumanPoseHandler
- Status: Currently safe-disabled, defer Phase 3 untuk axis fix

**Tier 3 — Animator Controller (best quality)**
- Mecanim state machine + transitions + blend trees
- Source: Mixamo, Asset Store, custom-baked
- Status: TBD Phase 3+

**Tier 4 — Custom keyframe via Blender + VRM Add-on (highest control)**
- Manual keyframe per pose, export VRMA
- Cocok untuk anime-stylized signature gestures
- Status: Future, after Tier 2/3 stable

---

## 🐛 Known Issues + Mitigations

| Issue | Status | Mitigation |
|-------|--------|------------|
| T-pose karakter (tangan kaku ke samping) | Phase 2.6 | Defer arm rest fix sampai axis tested via DumpBoneInfo. T-pose stable > broken folded arms |
| VRMA muscle retargeting tangan ke depan | Phase 2.6 disabled | Need verify Mecanim Avatar config Kohaku → muscle convention test |
| Unity rebuild overwrite Gradle/Manifest | Permanent | Re-apply 5 file pattern setiap rebuild (lihat [bone-mapper.md](bone-mapper.md) section 8) |
| HP unresponsive setelah Hentikan total | Fixed Phase 2.6 | Recursion guard di `OverlayService.stopServiceInternal()` |
| Karakter terpotong saat scale up | Fixed Phase 2.6 | Camera Z follow scale + Y compensation di `CommandReceiver.SetCharacterScale` |

---

## 📞 References & Community

- **VRM Spec:** https://vrm.dev/en/
- **VRoid Studio (free VRM creator):** https://vroid.com/en/studio
- **VRoid Hub (community VRM library):** https://hub.vroid.com
- **BOOTH .vrma motion store:** https://booth.pm/en/browse/VRM
- **UniVRM (Unity SDK):** https://github.com/vrm-c/UniVRM
- **MediaPipe (pose extraction):** https://github.com/google-ai-edge/mediapipe
- **Plask Motion (free anime mocap):** https://plask.ai
- **Mixamo (humanoid animations):** https://www.mixamo.com
