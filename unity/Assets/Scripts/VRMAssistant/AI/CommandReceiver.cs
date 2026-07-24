using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using VRM;
using VRMAssistant.Animation;
using VRMAssistant.Core;

namespace VRMAssistant.AI
{
    /// <summary>
    /// MonoBehaviour yang attach ke GameObject "VRMAssistant" untuk receive command dari Kotlin via UnitySendMessage.
    ///
    /// Method-method public di sini dipanggil oleh AICommandDispatcher.kt di Android side.
    /// Setiap method menerima string arg (UnitySendMessage signature).
    /// </summary>
    public class CommandReceiver : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;
        [SerializeField] private AssistantStateManager stateManager;
        [SerializeField] private ExpressionController expressionController;
        [SerializeField] private LookAtController lookAtController;
        [SerializeField] private VrmaPlaybackController vrmaController;
        [SerializeField] private VmdPlaybackController vmdController;
        [SerializeField] private VRMAssistant.Behavior.CharacterMovementController movementController;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private LipSyncController lipSyncController;

        private void Awake()
        {
            // Fallback wiring — field baru yang belum ter-serialize di scene lama
            if (lipSyncController == null) lipSyncController = FindAnyObjectByType<LipSyncController>();

            // AudioSource untuk TTS playback — buat kalau belum ada supaya lipsync FFT punya sumber.
            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }
            // Normalisasi setting apa pun sumbernya (defensif thd config scene lama)
            audioSource.playOnAwake = false;
            audioSource.loop = false;        // WAJIB false — kalau loop, TTS tak pernah selesai
            audioSource.spatialBlend = 0f;   // 2D — overlay, bukan spatial
            // Wire AudioSource ke LipSyncController supaya GetSpectrumData baca audio TTS ini
            if (lipSyncController != null) lipSyncController.SetAudioSource(audioSource);
        }

        /// <summary>True saat TTS sedang diputar (untuk indikator "bicara").</summary>
        public bool IsSpeaking => audioSource != null && audioSource.isPlaying;

        private Coroutine _audioRoutine;
        // Status TTS terakhir — dibaca via logcat/MCP untuk debug on-device (milestone saja, bukan per-frame).
        public static string LastAudioDebug = "(belum)";

        private ClipGestureController _clipController;

        /// <summary>Triggered dari Kotlin: gesture by name (Animator trigger / VRMA).</summary>
        public void TriggerGesture(string gestureName)
        {
            if (string.IsNullOrEmpty(gestureName)) return;

            // Prioritas 1: mocap clip (Bandai/Mixamo) via ClipGestureController — SATU-SATUNYA
            // jalur mocap yang retarget benar ke VRM runtime (HumanPoseHandler). Animator trigger
            // (Mecanim) TIDAK retarget ke model yang di-load runtime → gerakan tak kelihatan.
            if (_clipController == null) _clipController = FindAnyObjectByType<ClipGestureController>();
            if (_clipController != null && _clipController.HasClip(gestureName))
            {
                _clipController.PlayGesture(gestureName);
                Debug.Log("[CommandReceiver] mocap gesture: " + gestureName);
                return;
            }
            // Prioritas 2: VRMA playback (gesture VRM: Blush/Surprised/ModelPose/dll)
            if (vrmaController != null)
            {
                vrmaController.PlayGesture(gestureName);
                Debug.Log("[CommandReceiver] VRMA gesture: " + gestureName);
                return;
            }
            // Prioritas 3: Animator trigger (fallback terakhir, kalau ada controller ber-trigger)
            var animMain = modelLoader != null ? modelLoader.ModelAnimator : null;
            if (animMain != null && animMain.runtimeAnimatorController != null)
            {
                foreach (var p in animMain.parameters)
                {
                    if (p.name == gestureName && p.type == AnimatorControllerParameterType.Trigger)
                    {
                        animMain.SetTrigger(gestureName);
                        Debug.Log("[CommandReceiver] Animator gesture (fallback): " + gestureName);
                        return;
                    }
                }
            }
            Debug.LogWarning("[CommandReceiver] Skip gesture '" + gestureName + "' (bukan mocap clip / VRMA / trigger)");
        }

        /// <summary>Triggered dari Kotlin: AICommand.Expression. Format: "Mood|intensity" e.g. "Happy|0.8".</summary>
        public void SetExpression(string payload)
        {
            if (expressionController == null) return;
            var parts = payload.Split('|');
            if (parts.Length < 1) return;

            float intensity = 1f;
            if (parts.Length >= 2) float.TryParse(parts[1], out intensity);

            // Map ke BlendShapePreset
            BlendShapePreset preset = BlendShapePreset.Neutral;
            switch (parts[0])
            {
                case "Happy": preset = BlendShapePreset.Joy; break;
                case "Sad": preset = BlendShapePreset.Sorrow; break;
                case "Angry": preset = BlendShapePreset.Angry; break;
                case "Surprised": preset = BlendShapePreset.Fun; break;
                case "Neutral": preset = BlendShapePreset.Neutral; break;
            }
            expressionController.SetExpression(preset, intensity);
        }

        /// <summary>Triggered dari Kotlin: AICommand.LookAt — "User" atau "Wander".</summary>
        public void SetLookAtMode(string mode)
        {
            if (lookAtController == null) return;
            // Lookbehavior delegate ke SetModeForState — pakai state mapping
            switch (mode)
            {
                case "User":
                    lookAtController.SetModeForState(AssistantState.Listening); // track target
                    break;
                case "Wander":
                    lookAtController.SetModeForState(AssistantState.Idle); // saccade wander
                    break;
            }
        }

        /// <summary>Triggered dari Kotlin saat character moving via window animation. Phase 2.5 stub.</summary>
        public void SetWalking(string flag)
        {
            bool walking = flag == "true" || flag == "True" || flag == "1";
            if (modelLoader != null && modelLoader.ModelAnimator != null && modelLoader.ModelAnimator.runtimeAnimatorController != null)
            {
                // Cek apakah ada parameter "Walking" bool
                foreach (var p in modelLoader.ModelAnimator.parameters)
                {
                    if (p.name == "Walking" && p.type == AnimatorControllerParameterType.Bool)
                    {
                        modelLoader.ModelAnimator.SetBool("Walking", walking);
                        return;
                    }
                }
            }
            Debug.Log("[CommandReceiver] SetWalking " + walking + " (Animator parameter 'Walking' tidak ada — Phase 2.5 stub)");
        }

        /// <summary>
        /// Triggered dari Kotlin: load + play audio TTS lalu drive lipsync + state Speaking.
        /// Path = file lokal (absolute, mis. dari cacheDir Android). Format MP3/WAV/OGG.
        /// Saat selesai otomatis balik ke Idle. LipSync FFT baca AudioSource ini.
        /// </summary>
        public void PlayAudio(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogWarning("[CommandReceiver] PlayAudio path kosong");
                return;
            }
            if (_audioRoutine != null) StopCoroutine(_audioRoutine);
            _audioRoutine = StartCoroutine(PlayAudioRoutine(audioPath));
        }

        private IEnumerator PlayAudioRoutine(string audioPath)
        {
            // file:// prefix untuk UnityWebRequest local file
            string url = audioPath.StartsWith("file://") || audioPath.StartsWith("http")
                ? audioPath : "file://" + audioPath;

            var audioType = audioPath.ToLower().EndsWith(".wav") ? AudioType.WAV
                : audioPath.ToLower().EndsWith(".ogg") ? AudioType.OGGVORBIS
                : AudioType.MPEG; // default MP3

            LastAudioDebug = "start url=" + url + " type=" + audioType;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                // streamAudio=false → clip di-decode penuh ke memori, langsung playable
                // (default streaming bikin isPlaying false sesaat → playback gagal start).
                if (req.downloadHandler is DownloadHandlerAudioClip dh) dh.streamAudio = false;

                yield return req.SendWebRequest();
                LastAudioDebug = "sendDone result=" + req.result + " err=" + (req.error ?? "-");
                if (req.result != UnityWebRequest.Result.Success)
                {
                    LastAudioDebug = "LOAD_FAIL " + req.error + " (" + url + ")";
                    Debug.LogError("[CommandReceiver] PlayAudio gagal load: " + req.error + " (" + url + ")");
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) { LastAudioDebug = "CLIP_NULL"; yield break; }

                // Pastikan audio data ter-load sebelum Play (anti isPlaying-false-frame-pertama)
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                float loadWait = 0f;
                while (clip.loadState == AudioDataLoadState.Loading && loadWait < 3f)
                {
                    loadWait += Time.deltaTime; yield return null;
                }
                LastAudioDebug = "clip len=" + clip.length.ToString("F1") + " loadState=" + clip.loadState + " ch=" + clip.channels + " freq=" + clip.frequency;

                // Masuk state Speaking + aktifkan lipsync yang baca AudioSource ini
                if (stateManager != null) stateManager.SetState(AssistantState.Speaking);
                if (lipSyncController != null)
                {
                    lipSyncController.SetAudioSource(audioSource);
                    lipSyncController.active = true;
                }

                audioSource.clip = clip;
                audioSource.Play();
                LastAudioDebug = "PLAY called, isPlaying=" + audioSource.isPlaying + " len=" + clip.length.ToString("F1");
                Debug.Log("[CommandReceiver] TTS playing " + clip.length.ToString("F1") + "s");

                // Tunggu selama durasi clip (timer, BUKAN isPlaying — di Editor isPlaying
                // bisa flicker false). req tetap alive di using block selama playback ini,
                // jadi clip data (streamAudio=false) tidak di-free di tengah jalan.
                float dur = clip.length;
                float elapsed = 0f;
                while (elapsed < dur)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                // Selesai bicara → lipsync off, balik Idle
                if (lipSyncController != null) lipSyncController.active = false;
                if (stateManager != null) stateManager.SetState(AssistantState.Idle);
                Debug.Log("[CommandReceiver] TTS selesai → Idle");
            }
            _audioRoutine = null;
        }

        /// <summary>Play AudioClip langsung (LiaBrain TTS C#: PCM ElevenLabs → AudioClip) —
        /// Speaking + lipsync + balik Idle, tanpa lewat file/UnityWebRequest.</summary>
        public void PlayClip(AudioClip clip)
        {
            if (clip == null) { Debug.LogWarning("[CommandReceiver] PlayClip null"); return; }
            if (_audioRoutine != null) StopCoroutine(_audioRoutine);
            _audioRoutine = StartCoroutine(PlayClipRoutine(clip));
        }

        private IEnumerator PlayClipRoutine(AudioClip clip)
        {
            if (stateManager != null) stateManager.SetState(AssistantState.Speaking);
            if (lipSyncController != null)
            {
                lipSyncController.SetAudioSource(audioSource);
                lipSyncController.active = true;
            }
            audioSource.clip = clip;
            audioSource.Play();
            Debug.Log("[CommandReceiver] TTS(clip) playing " + clip.length.ToString("F1") + "s");

            float dur = clip.length, elapsed = 0f;
            while (elapsed < dur) { elapsed += Time.deltaTime; yield return null; }

            if (lipSyncController != null) lipSyncController.active = false;
            if (stateManager != null) stateManager.SetState(AssistantState.Idle);
            _audioRoutine = null;
        }

        /// <summary>Hentikan audio TTS di tengah jalan (mis. user kirim pesan baru).</summary>
        public void StopAudio(string _)
        {
            if (_audioRoutine != null) { StopCoroutine(_audioRoutine); _audioRoutine = null; }
            if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
            if (lipSyncController != null) lipSyncController.active = false;
        }

        /// <summary>
        /// Triggered dari Kotlin: SetCharacterScale dengan float string "1.0".
        /// Scale + adjust camera Z (mundur) + Y position supaya full body tetap fit di window.
        /// Clamp 0.5x – 1.5x — beyond 1.5x karakter terpotong window kotak floating.
        /// </summary>
        public void SetCharacterScale(string scaleStr)
        {
            if (modelLoader == null || modelLoader.LoadedModel == null) return;
            if (!float.TryParse(scaleStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float scale)) return;
            scale = Mathf.Clamp(scale, 0.5f, 1.5f);

            var t = modelLoader.LoadedModel.transform;
            t.localScale = new Vector3(scale, scale, scale);

            // Compensate Y: feet stay grounded at original feet level
            float feetCompensation = 0.9f * (scale - 1f);
            t.localPosition = new Vector3(t.localPosition.x, feetCompensation, t.localPosition.z);

            // Camera follow scale: zoom out saat scale up supaya full body tetap fit
            var cam = Camera.main;
            if (cam != null)
            {
                // Base camera Z = 1.75 at scale 1.0 (framing full-body, feet visible — 2026-07-22).
                float baseZ = 1.75f;
                float scaleOffset = (scale - 1f) * 1.5f;
                var camPos = cam.transform.position;
                cam.transform.position = new Vector3(camPos.x, camPos.y, baseZ + scaleOffset);
            }

            Debug.Log("[CommandReceiver] Scale=" + scale + " Y comp=" + feetCompensation);
        }

        /// <summary>
        /// Play animation by name. Core names route ke AssistantStateManager (procedural
        /// animation states — breathing/sway/hand pose/blink/look-at via AnimationOrchestrator),
        /// fallback ke VMD JSON playback untuk MMD community animations (walk/nekomimi/foxsay/...).
        ///
        /// Core (procedural via AssistantStateManager):
        ///   "Idle" / "Active" / "Thinking" / "Listening" / "Speaking"
        ///   "LipSync" — LipSyncController.active ON (mulut saja; pola sintetis bila tanpa audio)
        ///
        /// Otherwise (VMD-driven):
        ///   forward ke vmdController.Play (walk/nekomimi/foxsay/fuwari/heartbeat/baby).
        /// </summary>
        public void PlayVmd(string animName)
        {
            if (TryRouteToState(animName)) return;

            // Fallback: VMD JSON playback
            if (vmdController == null) { Debug.LogWarning("[CommandReceiver] vmdController not assigned"); return; }
            vmdController.Play(animName);
        }

        /// <summary>Stop semua playback — VMD/VRMA/Clip stop, lipsync off, state kembali Idle.</summary>
        public void StopVmd(string _)
        {
            if (vmdController != null) vmdController.Stop();
            if (vrmaController != null) vrmaController.StopGesture();
            if (_clipController == null) _clipController = FindAnyObjectByType<ClipGestureController>();
            if (_clipController != null) _clipController.Stop();
            if (lipSyncController != null) lipSyncController.active = false;
            if (stateManager != null) stateManager.SetState(AssistantState.Idle);
            Debug.Log("[CommandReceiver] Stop → semua playback berhenti, state Idle.");
        }

        /// <summary>
        /// Route core animation name ke AssistantStateManager (procedural states).
        /// Return true bila core name ter-handle; false bila bukan (caller fallback VMD).
        /// </summary>
        private bool TryRouteToState(string animName)
        {
            switch (animName)
            {
                case "Idle": EnterCoreState(AssistantState.Idle); return true;
                case "Active": EnterCoreState(AssistantState.Active); return true;
                case "Thinking": EnterCoreState(AssistantState.Thinking); return true;
                case "Listening": EnterCoreState(AssistantState.Listening); return true;
                case "Speaking": EnterCoreState(AssistantState.Speaking); return true;
                case "LipSync":
                    // Test mulut saja tanpa ganti body state
                    if (lipSyncController != null)
                    {
                        lipSyncController.active = true;
                        Debug.Log("[CommandReceiver] LipSync ON (mulut saja)");
                    }
                    else Debug.LogWarning("[CommandReceiver] LipSyncController tidak ditemukan");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Masuk ke core state procedural. VMD/VRMA yang sedang jalan dihentikan dulu
        /// supaya tidak fight menulis bone localRotation di LateUpdate yang sama.
        /// </summary>
        private void EnterCoreState(AssistantState state)
        {
            if (vmdController != null && vmdController.IsPlaying) vmdController.Stop();
            if (vrmaController != null && vrmaController.IsPlaying) vrmaController.StopGesture();

            if (stateManager == null)
            {
                Debug.LogError("[CommandReceiver] stateManager belum di-assign — core state tidak bisa jalan");
                return;
            }
            stateManager.SetState(state);
            Debug.Log("[CommandReceiver] State → " + state);
        }

        /// <summary>
        /// Triggered dari Kotlin: ganti karakter runtime. Arg: alias ("dress"/"kimono")
        /// atau nama file .vrm di StreamingAssets. Playback dihentikan dulu, state reset Idle,
        /// lalu model di-swap — semua controller re-wire via OnModelLoaded.
        /// </summary>
        public async void SwitchCharacter(string nameOrFile)
        {
            if (modelLoader == null) { Debug.LogWarning("[CommandReceiver] modelLoader null"); return; }
            StopVmd("");
            await modelLoader.SwitchCharacterAsync(nameOrFile);
            Debug.Log("[CommandReceiver] SwitchCharacter selesai: " + nameOrFile);
        }

        /// <summary>
        /// Triggered dari Kotlin OverlayService saat jendela sedang digeser (roam) / diam.
        /// Arg: "walk" = main loop FemWalk, "idle" = stop → kembali prosedural idle.
        /// Gerak posisi di-handle Kotlin (jendela pindah); Unity cuma animasi.
        /// </summary>
        public void SetLocomotion(string cmd)
        {
            // Jalur standar: Animator bool "Walking" (Idle↔Walk di LiaAnimator)
            var anim = modelLoader != null ? modelLoader.ModelAnimator : null;
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.SetBool("Walking", cmd == "walk");
                return;
            }
            // Fallback lama (tanpa controller)
            if (_clipController == null) _clipController = FindAnyObjectByType<ClipGestureController>();
            if (_clipController == null) return;
            if (cmd == "walk") _clipController.PlayLoop("FemWalk");
            else _clipController.StopLoop();
        }

        /// <summary>Triggered dari Kotlin saat user TAP karakter — Lia bereaksi + lihat user.</summary>
        public void OnTapReaction(string _)
        {
            // Reaksi saat dielus/disentuh — hanya pose kurasi (yang lain dinilai aneh) +
            // ekspresi senang (Joy = "pipi merah/seneng dielus", VRM 0.x tak punya preset blush).
            string[] reactions = { "Respond", "Call", "LookAround", "ModelPose" };
            TriggerGesture(reactions[Random.Range(0, reactions.Length)]);
            if (expressionController != null) expressionController.SetExpression(BlendShapePreset.Joy, 0.8f);
            if (lookAtController != null) lookAtController.SetModeForState(AssistantState.Listening);
        }

        /// <summary>Triggered dari Kotlin: TriggerWander — karakter walk ke titik random.</summary>
        public void TriggerWander(string _)
        {
            if (movementController == null) return;
            if (Random.value < 0.3f) movementController.WalkToEdge();
            else movementController.WalkToRandom();
        }

        /// <summary>
        /// Triggered dari Kotlin saat user drag hit box — Unity character ikut pindah ke world position.
        /// Format payload: "screenX,screenY" (pixel coords).
        /// </summary>
        public void OnUserDragCharacter(string payload)
        {
            if (movementController == null) return;
            var parts = payload.Split(',');
            if (parts.Length < 2) return;
            if (!int.TryParse(parts[0], out int sx) || !int.TryParse(parts[1], out int sy)) return;

            // Convert screen → world dengan camera ray
            var cam = Camera.main;
            if (cam == null) return;
            // Flip Y (Android top → Unity bottom)
            var unityScreenY = Screen.height - sy;
            var depth = cam.transform.position.z - 0f; // assume karakter at z=0
            var worldPos = cam.ScreenToWorldPoint(new Vector3(sx, unityScreenY, Mathf.Abs(depth)));
            worldPos.y = movementController.CurrentPosition.y; // jaga di plane horizontal

            movementController.WalkTo(worldPos);
        }
    }
}
