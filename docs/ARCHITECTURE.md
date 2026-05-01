# LiaVA — Frontend Architecture Document

**Version:** 1.0  
**Phase:** 1 — Architecture & Setup  
**Status:** Awaiting review before Phase 2

---

## Table of Contents

1. [Project Context](#1-project-context)
2. [High-Level Module Diagram](#2-high-level-module-diagram)
3. [Data Flow](#3-data-flow)
4. [Module Specifications](#4-module-specifications)
5. [Command Contract Specification](#5-command-contract-specification)
6. [Unity Project Folder Structure](#6-unity-project-folder-structure)
7. [Required Unity Packages](#7-required-unity-packages)
8. [AndroidManifest Permissions](#8-androidmanifest-permissions)
9. [Open Questions for Review](#9-open-questions-for-review)

---

## 1. Project Context

LiaVA is a Unity Android application that renders a VRM character as an interactive virtual assistant. The frontend is a pure **command executor**: it receives structured commands and audio from an external backend over a defined interface and translates them into visual output (animation, expression, lip sync, UI) and Android system actions.

The backend — including LLM, STT (Whisper), and TTS (ElevenLabs) — is a separate project. This document covers only the frontend contract and architecture.

**Existing state at architecture time:**
- Unity 6 LTS project initialized with URP 17.1.0
- UniVRM 0.129.0 installed (character already imported as prefab `5988336557246295560`)
- uLipSync 3.1.1 installed
- Android min SDK 23, target SDK not yet pinned
- Basic `AssistantCamera` script exists; no other modules implemented

---

## 2. High-Level Module Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        EXTERNAL BACKEND                             │
│         (LLM · STT · TTS · Conversation State — out of scope)       │
└────────────────────────────┬────────────────────────────────────────┘
                             │  JSON command messages + audio stream
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    COMMAND RECEIVER (Interface)                     │
│   WebSocket client / local mock  →  CommandRouter dispatcher        │
└──┬───────────┬──────────┬────────────┬────────────┬────────────────┘
   │           │          │            │            │
   ▼           ▼          ▼            ▼            ▼
┌──────┐  ┌────────┐  ┌──────┐  ┌─────────┐  ┌──────────┐
│Anim  │  │Express-│  │Lip   │  │Head &   │  │Android   │
│Orche-│  │ion     │  │Sync  │  │Eye      │  │System    │
│strat.│  │Module  │  │Module│  │Tracking │  │Bridge    │
└──┬───┘  └───┬────┘  └──┬───┘  └────┬────┘  └──────────┘
   │          │          │           │
   └──────────┴──────────┴───────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │   VRM CHARACTER      │
              │  (Animator, Blend-   │
              │   ShapeProxy, bones) │
              └──────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │    UI OVERLAY        │
              │  (UI Toolkit canvas  │
              │   above 3D view)     │
              └──────────────────────┘
```

### Dependency Rules

- **CommandRouter** is the single entry point for all runtime commands. No module polls or self-triggers in response to backend state — they only respond to dispatched commands.
- **VRM Character Module** is the shared resource owner. All other visual modules hold references obtained from it at startup; they never search the scene hierarchy themselves.
- **UI Module** is fully independent of the animation/expression pipeline. It reads commands from the router directly.
- **Android System Bridge** has no Unity visual dependencies. It is a pure C# ↔ Android JNI wrapper.

---

## 3. Data Flow

### 3.1 Full Interaction Example — "User says hi → assistant waves and replies"

```
BACKEND                          FRONTEND
──────                           ────────

1. Backend receives STT text
   "hi" from user microphone

2. LLM generates response text
   + selects gesture + emotion

3. TTS generates audio file
   (e.g., "Hi there! How can
    I help you today?")

4. Backend sends commands:

   ① { "type": "set_status",        →  UI Module
       "status": "speaking" }           └─ StatusIndicator shows "speaking"

   ② { "type": "play_gesture",      →  AnimationOrchestrator
       "gesture": "wave",               └─ CrossFade to Wave clip
       "blend_duration": 0.3 }          └─ Returns to idle after clip ends

   ③ { "type": "set_emotion",       →  ExpressionModule
       "emotion": "Happy",              └─ BlendShape fade to Happy (0.6)
       "intensity": 0.6,                └─ Scheduled to fade out in 3.5s
       "fade_duration": 0.3 }

   ④ { "type": "speak",             →  LipSyncModule
       "audio_url": "...",              └─ Loads AudioClip
       "transcript": "Hi there!        └─ Plays AudioSource
        How can I help you today?" }    └─ uLipSync drives viseme BlendShapes
                                    →  UI Module
                                        └─ ChatBubble shows transcript text

5. LipSyncModule fires OnSpeakEnd   →  AnimationOrchestrator
   event when audio finishes            └─ Returns to idle state

6. Backend sends:
   { "type": "set_status",         →  UI Module
     "status": "idle" }                └─ StatusIndicator returns to idle
```

### 3.2 Event Flow Within a Frame (Runtime)

```
Update() loop
│
├─ CommandRouter.ProcessQueue()        // drain incoming command queue
│   └─ dispatch to module(s)
│
├─ AnimationOrchestrator.Tick()        // manage state machine timers
│
├─ ExpressionModule.Tick()             // advance BlendShape fade lerps
│   ├─ emotion blend
│   └─ auto-blink timer
│
├─ HeadEyeTrackingModule.Tick()        // LookAt IK weight lerp
│
└─ LipSyncModule                       // driven by uLipSync internally
    └─ (uLipSync runs on AudioSource DSP thread + job system)
```

---

## 4. Module Specifications

### 4.1 VRM Character Module (`VrmCharacterModule.cs`)

**Responsibility:** Lifecycle owner of the VRM character. Single source of truth for all sub-system references.

**Public API:**
```csharp
// Load from prefab reference set in Inspector (Phase 2 default)
Task LoadCharacter();

// Hot-reload a different VRM file at runtime (Phase 3+)
Task LoadCharacterFromPath(string vrmFilePath);

void DespawnCharacter();

// Reference accessors (populated after LoadCharacter completes)
Animator          GetAnimator();
VRMBlendShapeProxy GetBlendShapeProxy();
Transform         GetHeadBone();
Transform         GetLeftEyeBone();
Transform         GetRightEyeBone();
AudioSource       GetAudioSource();

// Events
event Action OnCharacterReady;
event Action OnCharacterDespawned;
```

**Notes:**
- The prefab for `5988336557246295560` is already extracted in the project. The module will reference it via a serialized field in the Inspector so the scene does not depend on a hardcoded path.
- Exposes `OnCharacterReady` so all dependent modules initialise only after the VRM is ready.

---

### 4.2 Animation Orchestrator (`AnimationOrchestrator.cs`)

**Responsibility:** Wrap Unity's Animator with a state-machine layer that enforces ordered transitions and returns to idle automatically.

**States:**
```
Idle (looping)
  └─► Gesture (one-shot, then auto-return to Idle)
  └─► Talking (looping while speech is active)
  └─► Idle Variant (swap idle clip)
```

**Public API:**
```csharp
void PlayGesture(string gestureName, float blendDuration = 0.25f);
void SetIdleVariant(string variantName, float blendDuration = 0.5f);
void EnterTalkingState(float blendDuration = 0.2f);
void ExitTalkingState(float blendDuration = 0.3f);
void BlendTo(string stateName, float duration);

// Query
bool IsPlayingGesture { get; }
string CurrentState { get; }
```

**Animation Clip Registry (`AnimationClipRegistry.asset` — ScriptableObject):**
```
clips:
  - key: "idle_default"    clip: <AnimationClip ref>  loop: true
  - key: "idle_relaxed"    clip: <AnimationClip ref>  loop: true
  - key: "wave"            clip: <AnimationClip ref>  loop: false
  - key: "nod"             clip: <AnimationClip ref>  loop: false
  - key: "shrug"           clip: <AnimationClip ref>  loop: false
  - key: "point"           clip: <AnimationClip ref>  loop: false
  - key: "talking_loop"    clip: <AnimationClip ref>  loop: true
```

Clips are Mixamo humanoid animations retargeted to the VRM Avatar. The registry is data-driven: adding a new gesture requires only adding a new entry and dropping the retargeted clip — no code change.

**Animator Controller layout:**
```
Base Layer
  ├─ Idle        (BlendTree: idle_default ↔ idle_relaxed via float IdleBlend)
  ├─ Gesture     (single-state, set via Animator.CrossFadeInFixedTime)
  └─ Talking     (looping talking_loop clip)

Additive Layer (weight 0.0–1.0, runtime-controlled)
  └─ HeadNod     (subtle head motion during talking, driven by orchestrator)
```

---

### 4.3 Expression Module (`ExpressionModule.cs`)

**Responsibility:** Drive VRM BlendShapes for emotions and automatic blinking.

**Emotion types (maps to VRM standard presets):**
```csharp
public enum EmotionType
{
    Neutral, Happy, Sad, Angry, Surprised, Relaxed
}
```

**Viseme types (for lip sync — managed by LipSyncModule, not this module):**
```
Aa, Ih, Ou, Ee, Oh, Neutral (VRM standard viseme presets)
```

**Public API:**
```csharp
void SetEmotion(EmotionType emotion, float intensity, float fadeDuration = 0.3f);
void ClearEmotion(float fadeDuration = 0.3f);

// Auto-blink runs automatically; these allow override
void ForceBlink();
void SetBlinkEnabled(bool enabled);
```

**Auto-blink behaviour:**
- Interval: random between 2.5 s and 6.0 s
- Blink duration: ~0.15 s close + 0.1 s open
- Runs in a coroutine independent of emotion state
- Blink BlendShape is additive on top of current emotion weight

**Constraints:**
- Emotion and viseme BlendShapes are on separate VRM preset keys — they do not conflict.
- Intensity is clamped [0, 1]. Fades use `Mathf.Lerp` in `Update()`, not coroutines, so they can be interrupted mid-fade cleanly.

---

### 4.4 Lip Sync Module (`LipSyncModule.cs`)

**Responsibility:** Bridge between audio playback and uLipSync-driven viseme BlendShapes.

**Public API:**
```csharp
// Play from a loaded AudioClip (backend sends clip reference or byte[] buffer)
Task PlaySpeech(AudioClip clip, string transcript = null);

// Play from a URL (backend sends a URL to a TTS-generated audio file)
Task PlaySpeechFromUrl(string url);

// Immediate stop
void StopSpeech();

bool IsSpeaking { get; }

event Action OnSpeechStarted;
event Action OnSpeechEnded;
```

**uLipSync integration notes:**
- `uLipSync` component is attached to the same GameObject as `AudioSource`.
- The VRM BlendShape proxy is wired to uLipSync via `uLipSyncBlendShape` component on the VRM root.
- Viseme-to-BlendShape mapping (A→Aa, I→Ih, U→Ou, E→Ee, O→Oh) is configured in the `uLipSync` profile ScriptableObject.
- `OnSpeechEnded` fires via `AudioSource.clip` length tracking + `OnAudioFilterRead` callback — not a fixed timer.

**Audio input modes (both supported):**
| Mode | Description |
|------|-------------|
| `AudioClip` | Backend delivers a complete clip; loaded into `AudioSource.clip` |
| `URL` | Backend delivers a URL; fetched via `UnityWebRequestMultimedia` at runtime |

Streaming PCM (byte buffer) is deferred to Phase 3+ pending backend contract confirmation.

---

### 4.5 Head & Eye Tracking Module (`HeadEyeTrackingModule.cs`)

**Responsibility:** Control where the character looks using Animation Rigging LookAt constraints.

**Rig setup (configured in scene, not at runtime):**
```
Rig (Animation Rigging Rig component, weight 1.0)
  ├─ HeadLookAt   (Multi-Aim Constraint on Head bone, Up Axis: Y, weight 0.6)
  └─ EyesLookAt  (Two Bone IK or Multi-Aim on each eye bone, weight 0.8)
```

**LookAt target:** A single world-space `Transform` (`_lookAtTarget`) that this module moves. The constraints follow it automatically.

**Public API:**
```csharp
void LookAt(Transform target);
void LookAtPoint(Vector3 worldPosition);
void LookAtCamera();          // convenience: targets main camera position
void LookForward();           // resets to character's forward direction

// Ambient mode: subtle random saccades for liveliness when idle
void SetAmbientLookEnabled(bool enabled);

// Weight control (e.g., soften gaze during gesture)
void SetHeadWeight(float weight, float lerpDuration = 0.3f);
void SetEyeWeight(float weight, float lerpDuration = 0.15f);
```

**Ambient look behaviour (when idle and no explicit target):**
- Every 2–5 s, shifts the LookAt target to a random point within ±15° horizontal and ±8° vertical of forward.
- Interpolates with `SmoothDamp` over 0.4–0.8 s.
- Disabled automatically when a gesture is playing (orchestrator notifies via event).

---

### 4.6 UI Module (`UIModule.cs` + UI Toolkit)

**Responsibility:** Render all 2D overlay UI above the 3D character view.

**Technology:** Unity UI Toolkit (UIElements). UXML + USS for layout and styling. Chosen over uGUI for better Android scalability and separation of markup from logic.

**Components:**

| Component | UXML element ID | Description |
|-----------|----------------|-------------|
| `ChatBubble` | `#chat-bubble` | Displays assistant's speech transcript. Fades in when text arrives, fades out after `auto_hide_delay` seconds or on next message. |
| `StatusIndicator` | `#status-indicator` | Icon + label showing current state: `idle`, `listening`, `thinking`, `speaking`. |
| `SettingsPanel` | `#settings-panel` | Slide-in panel (placeholder). Toggle via hamburger icon. |
| `DebugOverlay` | `#debug-overlay` | Editor/dev-build only. Shows last received command JSON. Hidden in release builds via `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`. |

**Public API:**
```csharp
void ShowTranscript(string text, float autoHideDelay = 5f);
void HideTranscript();
void SetStatus(AssistantStatus status);  // Idle, Listening, Thinking, Speaking
void ShowNotification(string message, float duration = 3f);
```

**Screen size handling:**
- Root `VisualElement` uses flex layout with `align-items: center` and percentage-based widths.
- Safe area insets applied via script reading `Screen.safeArea` on `Start()` — handles notches and punch-holes.
- Font sizes use `em` units; base size set proportional to `Screen.dpi`.

---

### 4.7 Android System Bridge (`AndroidBridge.cs`)

**Responsibility:** Expose system-level Android actions as safe, permission-checked C# methods. The bridge does NOT decide when to call these — `CommandRouter` calls them on behalf of backend commands.

**All methods are stubs in Phase 1. Full implementation in Phase 4+.**

```csharp
// App control
void OpenApp(string packageName);
void OpenUrl(string url);

// Media & device
void SetVolume(AudioStream stream, int level);       // 0–15
void SetBrightness(float normalizedLevel);           // 0.0–1.0

// Connectivity  (requires user permission dialog on Android 13+)
void SetWifiEnabled(bool enabled);
void SetBluetoothEnabled(bool enabled);

// Alarms & timers
void SetAlarm(int hour, int minute, string label);
void SetTimer(int seconds, string label);

// Notifications (read — requires BIND_NOTIFICATION_LISTENER_SERVICE)
NotificationInfo[] GetActiveNotifications();
void DismissNotification(string key);
```

**Permission check pattern (applied to every method):**
```csharp
private bool HasPermission(string androidPermission)
{
    return Permission.HasUserAuthorizedPermission(androidPermission);
}
```

If permission is missing, the method logs a warning and returns without crashing. No silent failures — all errors surface to a `BridgeError` event the UI module can display.

**Feature flags:** Each capability is guarded by a static bool in `AndroidBridgeConfig.cs`, so capabilities can be enabled/disabled without removing code:
```csharp
public static class AndroidBridgeConfig
{
    public static bool AllowOpenApp        = true;
    public static bool AllowVolumeControl  = true;
    public static bool AllowWifiToggle     = false;  // disabled until tested
    public static bool AllowNotifications  = false;  // requires extra service
}
```

---

### 4.8 Command Receiver & Router

**CommandReceiver** (`CommandReceiver.cs`): Manages the transport layer. In Phase 4 this will be a WebSocket client. For Phase 2–3, a mock implementation (`MockCommandSender.cs`) injects commands from an Editor debug panel.

**Interface contract:**
```csharp
public interface ICommandReceiver
{
    event Action<string> OnRawCommandReceived;  // raw JSON string
    void Connect(string endpoint);
    void Disconnect();
    bool IsConnected { get; }
}
```

**CommandRouter** (`CommandRouter.cs`): Parses JSON, deserialises into typed command objects, and dispatches to the correct module. Thread-safe: incoming messages are enqueued and processed on the main thread in `Update()`.

```csharp
public class CommandRouter : MonoBehaviour
{
    // Injected references
    [SerializeField] AnimationOrchestrator _animator;
    [SerializeField] ExpressionModule      _expression;
    [SerializeField] LipSyncModule         _lipSync;
    [SerializeField] HeadEyeTrackingModule _headEye;
    [SerializeField] UIModule              _ui;
    [SerializeField] AndroidBridge         _bridge;

    void Update() => ProcessQueue();
}
```

**MockCommandSender** (`MockCommandSender.cs`): Editor-only `MonoBehaviour` with a custom Inspector GUI (using `[CustomEditor]`) that presents buttons for every command type. Allows full end-to-end testing of the visual pipeline without any backend.

---

## 5. Command Contract Specification

### 5.1 Transport

| Property | Value |
|----------|-------|
| Protocol | WebSocket (ws:// or wss://) |
| Message format | UTF-8 JSON, one command per message |
| Direction | Backend → Frontend only (frontend does not send commands back in Phase 1) |
| Framing | Each WebSocket message is one complete JSON object |

### 5.2 Command Envelope

Every command shares this envelope:

```json
{
  "type": "<command_type>",
  "id": "<optional_uuid_for_ack>",
  "timestamp": 1714500000.000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `string` | yes | Discriminator. See catalogue below. |
| `id` | `string` | no | UUID. Reserved for future ack/nack. |
| `timestamp` | `number` | no | Unix epoch seconds. Used for logging only. |

### 5.3 Command Catalogue

---

#### `speak`
Play audio and display transcript.

```json
{
  "type": "speak",
  "audio_url": "http://192.168.1.10:8080/tts/abc123.wav",
  "transcript": "Hi there! How can I help you today?",
  "language": "en"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `audio_url` | `string` | yes | URL to a WAV or MP3 audio file. |
| `transcript` | `string` | no | Text to display in the chat bubble. Omit to suppress chat bubble. |
| `language` | `string` | no | BCP-47 language tag (e.g., `"id"`, `"en"`, `"ja"`). For future display/font hinting. |

**Frontend actions triggered:**
1. `LipSyncModule.PlaySpeechFromUrl(audio_url)`
2. `UIModule.ShowTranscript(transcript)` (if present)
3. `UIModule.SetStatus(Speaking)`
4. On `OnSpeechEnded`: `UIModule.SetStatus(Idle)`

---

#### `play_gesture`
Trigger a named animation clip.

```json
{
  "type": "play_gesture",
  "gesture": "wave",
  "blend_duration": 0.3
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `gesture` | `string` | yes | Key from `AnimationClipRegistry`. e.g. `"wave"`, `"nod"`, `"shrug"`, `"point"`. |
| `blend_duration` | `number` | no | CrossFade duration in seconds. Default `0.25`. |

---

#### `set_emotion`
Set or clear a facial expression.

```json
{
  "type": "set_emotion",
  "emotion": "Happy",
  "intensity": 0.7,
  "fade_duration": 0.3
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `emotion` | `string` | yes | One of: `Neutral`, `Happy`, `Sad`, `Angry`, `Surprised`, `Relaxed`. |
| `intensity` | `number` | no | BlendShape weight 0.0–1.0. Default `1.0`. |
| `fade_duration` | `number` | no | Transition time in seconds. Default `0.3`. |

---

#### `set_idle_variant`
Switch between idle animation styles.

```json
{
  "type": "set_idle_variant",
  "variant": "relaxed",
  "blend_duration": 0.8
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `variant` | `string` | yes | One of: `default`, `relaxed`. (Expandable.) |
| `blend_duration` | `number` | no | CrossFade duration. Default `0.5`. |

---

#### `set_status`
Update the status indicator UI.

```json
{
  "type": "set_status",
  "status": "listening"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `status` | `string` | yes | One of: `idle`, `listening`, `thinking`, `speaking`. |

---

#### `set_look_target`
Direct the character's gaze.

```json
{
  "type": "set_look_target",
  "target": "camera"
}
```

```json
{
  "type": "set_look_target",
  "target": "point",
  "world_position": [0.3, 1.5, 2.0]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `target` | `string` | yes | One of: `camera`, `forward`, `ambient`, `point`. |
| `world_position` | `[x, y, z]` | if `target == "point"` | World-space coordinates. |

---

#### `show_notification`
Display a temporary UI notification.

```json
{
  "type": "show_notification",
  "message": "Alarm set for 7:00 AM.",
  "duration": 4.0
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `message` | `string` | yes | Text to display. |
| `duration` | `number` | no | Display time in seconds. Default `3.0`. |

---

#### `system_action`
Invoke an Android system capability.

```json
{
  "type": "system_action",
  "action": "open_app",
  "params": {
    "package_name": "com.spotify.music"
  }
}
```

```json
{
  "type": "system_action",
  "action": "set_alarm",
  "params": {
    "hour": 7,
    "minute": 0,
    "label": "Good morning"
  }
}
```

```json
{
  "type": "system_action",
  "action": "set_volume",
  "params": {
    "stream": "music",
    "level": 8
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `action` | `string` | yes | One of: `open_app`, `open_url`, `set_volume`, `set_brightness`, `set_wifi`, `set_bluetooth`, `set_alarm`, `set_timer`. |
| `params` | `object` | yes | Action-specific parameters (see AndroidBridge spec above). |

---

#### `composite`
Send multiple commands atomically (executed in order, same frame's queue).

```json
{
  "type": "composite",
  "commands": [
    { "type": "set_status", "status": "speaking" },
    { "type": "set_emotion", "emotion": "Happy", "intensity": 0.6, "fade_duration": 0.3 },
    { "type": "play_gesture", "gesture": "wave", "blend_duration": 0.3 },
    { "type": "speak", "audio_url": "http://...", "transcript": "Hi!" }
  ]
}
```

`CommandRouter` unwraps `commands` and pushes them onto the front of the queue in order.

---

### 5.4 Error Handling

The frontend does not send error responses in Phase 1. Unknown `type` values are logged as warnings and discarded. Malformed JSON is logged as an error and discarded. No crash on bad command.

---

## 6. Unity Project Folder Structure

```
unity/
├── Assets/
│   │
│   ├── _Project/                        ← All custom project code lives here
│   │   │
│   │   ├── Characters/
│   │   │   ├── VRM/
│   │   │   │   └── [imported VRM prefab and extracted assets]
│   │   │   └── Animations/
│   │   │       ├── Clips/              ← Retargeted Mixamo .anim files
│   │   │       └── AnimationClipRegistry.asset
│   │   │
│   │   ├── Modules/
│   │   │   ├── VrmCharacter/
│   │   │   │   └── VrmCharacterModule.cs
│   │   │   ├── AnimationOrchestrator/
│   │   │   │   └── AnimationOrchestrator.cs
│   │   │   ├── Expression/
│   │   │   │   └── ExpressionModule.cs
│   │   │   ├── LipSync/
│   │   │   │   ├── LipSyncModule.cs
│   │   │   │   └── Profiles/           ← uLipSync .asset profile files
│   │   │   ├── HeadEyeTracking/
│   │   │   │   └── HeadEyeTrackingModule.cs
│   │   │   ├── UI/
│   │   │   │   ├── UIModule.cs
│   │   │   │   ├── UXML/
│   │   │   │   │   ├── MainOverlay.uxml
│   │   │   │   │   ├── ChatBubble.uxml
│   │   │   │   │   ├── StatusIndicator.uxml
│   │   │   │   │   └── SettingsPanel.uxml
│   │   │   │   └── USS/
│   │   │   │       ├── Variables.uss
│   │   │   │       └── MainOverlay.uss
│   │   │   └── AndroidBridge/
│   │   │       ├── AndroidBridge.cs
│   │   │       └── AndroidBridgeConfig.cs
│   │   │
│   │   ├── CommandSystem/
│   │   │   ├── CommandRouter.cs
│   │   │   ├── ICommandReceiver.cs
│   │   │   ├── WebSocketCommandReceiver.cs
│   │   │   ├── Commands/               ← One file per command type (POCOs)
│   │   │   │   ├── SpeakCommand.cs
│   │   │   │   ├── PlayGestureCommand.cs
│   │   │   │   ├── SetEmotionCommand.cs
│   │   │   │   ├── SetStatusCommand.cs
│   │   │   │   ├── SetLookTargetCommand.cs
│   │   │   │   ├── SystemActionCommand.cs
│   │   │   │   ├── CompositeCommand.cs
│   │   │   │   └── ShowNotificationCommand.cs
│   │   │   └── Editor/
│   │   │       └── MockCommandSender.cs ← Editor debug panel
│   │   │
│   │   ├── Scenes/
│   │   │   ├── Main.unity              ← Production scene
│   │   │   └── DevSandbox.unity        ← Testing scene with debug tools
│   │   │
│   │   └── Settings/                   ← ScriptableObject config assets
│   │       ├── AppConfig.asset
│   │       └── NetworkConfig.asset
│   │
│   ├── Plugins/
│   │   └── Android/
│   │       └── AndroidManifest.xml
│   │
│   ├── StreamingAssets/                ← Runtime-loaded files (audio, VRM)
│   │
│   ├── UniGLTF/                        ← Package (already imported)
│   ├── VRM/                            ← Package (already imported)
│   └── uLipSync/                       ← Package (already imported)
│
├── Packages/
│   └── manifest.json
│
└── ProjectSettings/
```

**Rationale for `_Project/` prefix:** Keeps all custom code alphabetically sorted to the top in the Project window, cleanly separated from third-party packages imported into `Assets/`.

---

## 7. Required Unity Packages

### 7.1 Already Installed

| Package | Version | Purpose |
|---------|---------|---------|
| `com.vrmc.univrm` | 0.129.0 | VRM character loading and BlendShape control |
| `com.vrmc.gltf` (UniGLTF) | 0.129.0 | GLTF dependency for UniVRM |
| `com.hecomi.ulipsync` | 3.1.1 | MFCC-based lip sync from AudioSource |
| `com.unity.render-pipelines.universal` | 17.1.0 | URP rendering |
| `com.unity.inputsystem` | 1.14.0 | Input handling |
| `com.unity.modules.androidjni` | 1.0.0 | Android JNI for system bridge |
| `com.unity.modules.uielements` | 1.0.0 | UI Toolkit |
| `com.unity.modules.unitywebrequest` | 1.0.0 | Audio URL fetching |
| `com.unity.modules.audio` | 1.0.0 | AudioSource / AudioClip |

### 7.2 Needs to Be Added

| Package | Version | How to Add | Purpose |
|---------|---------|-----------|---------|
| `com.unity.animation.rigging` | **1.3.x** (latest for Unity 6) | Package Manager → Unity Registry | LookAt constraints for head/eye tracking |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 | Package Manager → Unity Registry | Robust JSON parsing for CommandRouter |

**Adding Animation Rigging** (`manifest.json` addition):
```json
"com.unity.animation.rigging": "1.3.0"
```

### 7.3 Under Consideration (Flag for Approval, Do Not Purchase Yet)

| Package | Price | Benefit |
|---------|-------|---------|
| **Final IK** (RootMotion) | ~$90 USD | More stable full-body IK, better LookAt quality than Animation Rigging for VRM rigs. Recommended if head-tracking quality is insufficient after Phase 2 testing. |
| **NativeWebSocket** (free, GitHub) | Free | Lightweight WebSocket client for Unity Android (no SSL complexity). Add if Unity's built-in `ClientWebSocket` proves unreliable on Android. |

---

## 8. AndroidManifest Permissions

File location: `Assets/Plugins/Android/AndroidManifest.xml`

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="com.liava.assistant">

    <!-- ═══════════════════════════════════════════════════
         NETWORK — Required for WebSocket to backend and
         fetching TTS audio URLs at runtime
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />

    <!-- ═══════════════════════════════════════════════════
         AUDIO — Required for recording microphone input
         (STT flow: mic → backend). Frontend only plays back;
         but AudioRecord access still requires RECORD_AUDIO.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.RECORD_AUDIO" />

    <!-- ═══════════════════════════════════════════════════
         ALARMS & TIMERS — SET_ALARM is a normal permission.
         SCHEDULE_EXACT_ALARM requires user approval on
         Android 12+ (API 31+); request via Settings intent.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="com.android.alarm.permission.SET_ALARM" />
    <uses-permission android:name="android.permission.SCHEDULE_EXACT_ALARM"
        android:minSdkVersion="31" />

    <!-- ═══════════════════════════════════════════════════
         VOLUME / BRIGHTNESS — Modify audio settings.
         WRITE_SETTINGS is a special permission on API 23+;
         user must grant via Settings → Special App Access.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.MODIFY_AUDIO_SETTINGS" />
    <uses-permission android:name="android.permission.WRITE_SETTINGS" />

    <!-- ═══════════════════════════════════════════════════
         WIFI — On Android 10+ (API 29+) apps cannot
         directly enable/disable WiFi; they can only open
         the WiFi settings panel. Permission still required
         for legacy devices.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.CHANGE_WIFI_STATE" />
    <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />

    <!-- ═══════════════════════════════════════════════════
         BLUETOOTH — BLUETOOTH_CONNECT required on API 31+.
         Legacy BLUETOOTH for API < 31.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.BLUETOOTH"
        android:maxSdkVersion="30" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT"
        android:minSdkVersion="31" />

    <!-- ═══════════════════════════════════════════════════
         NOTIFICATIONS — POST_NOTIFICATIONS is runtime
         permission on Android 13+ (API 33+). Required for
         the assistant to post its own notifications.
         BIND_NOTIFICATION_LISTENER_SERVICE is a separate
         special permission; declared as service, not here.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.POST_NOTIFICATIONS"
        android:minSdkVersion="33" />

    <!-- ═══════════════════════════════════════════════════
         VIBRATION — For haptic feedback on UI interactions.
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.VIBRATE" />

    <!-- ═══════════════════════════════════════════════════
         WAKE LOCK — Prevent screen sleep while assistant
         is actively in a conversation (speaking/listening).
    ═══════════════════════════════════════════════════ -->
    <uses-permission android:name="android.permission.WAKE_LOCK" />

    <application
        android:label="@string/app_name"
        android:icon="@mipmap/ic_launcher">

        <activity
            android:name="com.unity3d.player.UnityPlayerGameActivity"
            android:theme="@style/UnityThemeSelector"
            android:screenOrientation="portrait"
            android:configChanges="mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|fontScale|layoutDirection|density"
            android:hardwareAccelerated="true"
            android:exported="true">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>

        <!-- Notification Listener Service (Phase 4+ — disabled until needed) -->
        <!--
        <service android:name=".LiavaNotificationListenerService"
            android:permission="android.permission.BIND_NOTIFICATION_LISTENER_SERVICE"
            android:exported="true">
            <intent-filter>
                <action android:name="android.service.notification.NotificationListenerService" />
            </intent-filter>
        </service>
        -->

    </application>

</manifest>
```

### Permission Risk Summary

| Permission | Risk Level | Notes |
|-----------|------------|-------|
| `INTERNET` | Low — standard | Required for all network calls |
| `RECORD_AUDIO` | Medium — runtime | Must request at runtime; show rationale dialog |
| `WRITE_SETTINGS` | High — special | Opens system Settings UI; cannot be granted programmatically |
| `SCHEDULE_EXACT_ALARM` | Medium — special (API 31+) | Redirect to `ACTION_REQUEST_SCHEDULE_EXACT_ALARM` |
| `BLUETOOTH_CONNECT` | Medium — runtime (API 31+) | Must request at runtime |
| `POST_NOTIFICATIONS` | Low — runtime (API 33+) | Standard runtime request |
| `CHANGE_WIFI_STATE` | Medium — limited | Direct toggle blocked on API 29+; use settings panel instead |

---

## 9. Open Questions for Review

The following decisions need your input before Phase 2 implementation begins.

---

**Q1 — WebSocket or HTTP polling for the command interface?**

The architecture above assumes WebSocket (persistent, low-latency, ideal for real-time audio coordination). Alternative: HTTP long-polling or REST calls initiated by the frontend after each TTS completes.

- **WebSocket** is recommended: it allows the backend to push `speak` + `set_emotion` + `play_gesture` simultaneously and in order without the frontend polling.
- **Decision needed:** Confirm WebSocket. If the backend cannot host a WebSocket server, we fall back to REST and the `composite` command type becomes critical.

---

**Q2 — Audio delivery: URL vs. base64 inline vs. byte stream?**

The current contract uses `audio_url` (a URL the frontend fetches). Alternatives:
- **Base64 in JSON**: avoids a second HTTP round-trip but bloats message size (~33% overhead for audio).
- **Binary WebSocket frame**: fastest, but requires framing protocol on top of the JSON text channel.

- **Recommendation:** Start with URL. It keeps the JSON clean and the backend can serve static files easily. If latency is unacceptable in testing, evaluate binary frames.
- **Decision needed:** Confirm URL-based delivery for Phase 2.

---

**Q3 — Minimum Android API level: 23 or higher?**

Current Unity project setting is `minSdkVersion 23` (Android 6.0). Several features degrade on older APIs:
- Exact alarms require API 31+
- WiFi toggle blocked on API 29+
- Notification listener requires API 18+ (fine)
- Bluetooth connect requires API 31+ runtime permission

- **Recommendation:** Set minimum to **API 28** (Android 9, released 2018) to avoid the WiFi toggle regression and ensure `FileProvider` compatibility for audio caching.
- **Decision needed:** Confirm acceptable minimum API. Target API 36 (Android 16) is already agreed.

---

**Q4 — VRM file: prefab reference or runtime load from `StreamingAssets`?**

The character prefab (`5988336557246295560.prefab`) is already extracted in the project. Two options:
- **Prefab in scene (simpler):** Drag the prefab into the Main scene. VRM is always loaded; no async wait. Good for Phase 2.
- **Runtime load from StreamingAssets:** `VrmCharacterModule` loads the `.vrm` binary at startup. Allows swapping characters. Slightly longer startup.

- **Recommendation:** Use the **prefab in scene** for Phase 2 to keep iteration fast. Wire up runtime loading as an optional path in Phase 3.
- **Decision needed:** Confirm prefab-first approach.

---

**Q5 — Should the frontend send any data back to the backend?**

Current design is **receive-only**. Potential outbound data:
- Speech end event (so backend knows when to listen again)
- Android bridge result (e.g., "app opened successfully")
- Error/warning events

- **Recommendation:** Define a minimal outbound event contract even in Phase 1, so the WebSocket channel is bidirectional from the start. Suggested event: `{ "event": "speech_ended" }` and `{ "event": "bridge_result", "action": "...", "success": true }`.
- **Decision needed:** Approve minimal outbound events, or keep strictly receive-only for now.

---

**Q6 — Talking animation: dedicated loop clip or additive overlay?**

During speech, the character should visually appear to be talking (body sway, gesture energy). Two approaches:
- **Dedicated `talking_loop` clip (recommended):** A Mixamo or custom loop with subtle body sway. Clean, predictable.
- **Additive overlay:** Add a sway layer on top of idle. More flexible, harder to tune.

- **Recommendation:** Start with a `talking_loop` Mixamo clip. Do you have a suitable one, or should we download one from Mixamo during Phase 2?
- **Decision needed:** Confirm talking clip strategy. If using Mixamo, name the specific clip (e.g., "Talking" or "Standing Idle" variation).

---

**Q7 — UI Toolkit vs. uGUI?**

UI Toolkit is recommended for modern Android and is the architecture choice above. One known friction point: **UI Toolkit PanelSettings requires URP camera setup** in Unity 6, which needs one extra step. If you prefer uGUI (simpler Canvas setup), the module design is the same — only the implementation layer changes.

- **Decision needed:** Confirm UI Toolkit, or switch to uGUI.

---

*End of Architecture Document — Phase 1*  
*Proceed to Phase 2 after all Open Questions above are resolved.*
