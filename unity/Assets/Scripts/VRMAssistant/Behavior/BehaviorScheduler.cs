using System.Collections.Generic;
using UnityEngine;
using VRMAssistant.Animation;
using VRMAssistant.Core;

namespace VRMAssistant.Behavior
{
    /// <summary>
    /// Tick-based scheduler yang pilih BehaviorEntry random secara weighted setiap interval,
    /// constrained ke current AssistantState + cooldown per entry.
    /// Inspired by Shimeji-ee desktop pet behavior XML.
    ///
    /// Hook ke Animator (Active state gesture trigger), HandPoseController (pose switch),
    /// dan AutonomousMovementController (request window move via UnitySendMessage ke Kotlin).
    /// </summary>
    public class BehaviorScheduler : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;
        [SerializeField] private AssistantStateManager stateManager;
        [SerializeField] private HandPoseController handPoseController;
        [SerializeField] private VrmaPlaybackController vrmaController;
        [SerializeField] private CharacterMovementController movementController;

        [Header("Tick Interval")]
        [Tooltip("Setiap berapa detik scheduler evaluasi behavior pool")]
        [SerializeField] private float tickIntervalSec = 5f;

        [Header("Behavior Pool")]
        [SerializeField]
        private List<BehaviorEntry> behaviors = new List<BehaviorEntry>
        {
            // Gesture mocap (halus, feminine) — triggerName = nama clip di ClipGestureController.
            // Dipanggil acak saat Idle supaya karakter hidup & variatif (bukan diam).
            new BehaviorEntry { label = "Look Around", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "Call", weight = 1.0f, minIntervalSec = 14f, durationSec = 3f,
                allowedStates = new[] { AssistantState.Idle, AssistantState.Active } },
            new BehaviorEntry { label = "Respond", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "Respond", weight = 0.7f, minIntervalSec = 22f, durationSec = 3f,
                allowedStates = new[] { AssistantState.Idle, AssistantState.Active } },
            new BehaviorEntry { label = "Raise Hand", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "RaiseHand", weight = 0.6f, minIntervalSec = 26f, durationSec = 3f,
                allowedStates = new[] { AssistantState.Active } },
            new BehaviorEntry { label = "Wave", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "WaveHand", weight = 0.6f, minIntervalSec = 30f, durationSec = 3f,
                allowedStates = new[] { AssistantState.Active } },
            new BehaviorEntry { label = "Laughing", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "Laughing", weight = 0.4f, minIntervalSec = 40f, durationSec = 4f,
                allowedStates = new[] { AssistantState.Active } },
            new BehaviorEntry { label = "Look (VRMA)", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "LookAround", weight = 0.5f, minIntervalSec = 18f, durationSec = 3f,
                allowedStates = new[] { AssistantState.Idle } },

            // Catatan: roam keliling layar sekarang di-handle Kotlin (jendela overlay pindah),
            // bukan world-space Unity. Scheduler ini fokus variasi gesture idle saja.
            new BehaviorEntry { label = "Bow", kind = BehaviorEntry.Kind.AnimatorTrigger,
                triggerName = "Bow", weight = 0.4f, minIntervalSec = 45f, durationSec = 4f,
                allowedStates = new[] { AssistantState.Idle } },
        };

        [Header("Wander Settings")]
        [Tooltip("Frekuensi jalan-jalan otonom (detik). Kecil = sering keliling.")]
        [SerializeField] private float wanderMinIntervalSec = 18f;
        [SerializeField] private float wanderMaxIntervalSec = 40f;

        private Animator _animator;
        private VRMAssistant.Animation.ClipGestureController _clipController;
        private float _nextTickTime;
        private float _nextWanderTime;

        private void OnEnable()
        {
            // Pool gesture LENGKAP dibangun di KODE (override list serialized di scene yang
            // cuma berisi 2 gesture "Tilt"/"HairTouch" → itu sebab lama "cuma garuk kepala").
            // Deterministik: scene lama tidak bisa mempersempit variasi lagi.
            behaviors = BuildRichPool();
            tickIntervalSec = 4.5f;   // sering ganti → terasa hidup, tapi tidak frantic

            if (modelLoader != null)
            {
                // SELALU subscribe supaya wiring re-run saat model (re)loaded / character switch
                modelLoader.OnModelLoaded += OnModelLoaded;
                if (modelLoader.LoadedModel != null) OnModelLoaded(modelLoader.LoadedModel);
            }

            _nextTickTime = Time.time + 3f;    // tick pertama cepat
            _nextWanderTime = Time.time + 5f;  // wander pertama ~5s setelah muncul → langsung hidup
        }

        /// <summary>
        /// Pool gesture LENGKAP (32 animasi): 10 gesture mocap via LiaAnimator trigger
        /// (Bandai/Mixamo) + 18 gesture VRMA + wander. Routing di ExecuteBehavior:
        /// nama yang cocok trigger LiaAnimator → Mecanim (Animator disable saat VRMA main,
        /// jadi tidak fight); sisanya → VrmaPlaybackController. allowedStates = null
        /// (selalu boleh) supaya variasi tidak tersaring state.
        /// </summary>
        private static List<BehaviorEntry> BuildRichPool()
        {
            var pool = new List<BehaviorEntry>();

            // Semua gesture pool-able dari katalog (33 animasi, yang pool=true). Scheduler
            // menyaring runtime lewat GestureLibrary.IsEnabled → apa pun yang user centang di
            // panel ⚙ Animasi otomatis ikut. Bobot & cooldown seragam-wajar (variasi terasa
            // hidup tanpa frantic); routing mocap vs VRMA ditentukan di ExecuteBehavior.
            foreach (var g in GestureLibrary.All)
            {
                if (!g.pool) continue;   // lewati lokomotor/idle (Walk/Run/FemWalk/IdleVar/HappyIdle)
                pool.Add(new BehaviorEntry
                {
                    label = g.label,
                    kind = BehaviorEntry.Kind.AnimatorTrigger,
                    triggerName = g.name,
                    weight = 0.8f,
                    minIntervalSec = 20f,
                    durationSec = 3.5f,
                    allowedStates = null,
                });
            }

            // --- Wander (jalan keliling world-space) ---
            pool.Add(new BehaviorEntry
            {
                label = "Wander", kind = BehaviorEntry.Kind.AutonomousMove,
                weight = 0.8f, minIntervalSec = 22f, durationSec = 3f, allowedStates = null,
            });

            return pool;
        }

        private void OnDisable()
        {
            if (modelLoader != null) modelLoader.OnModelLoaded -= OnModelLoaded;
        }

        private void OnModelLoaded(GameObject model)
        {
            _animator = modelLoader.ModelAnimator;
            // Fallback wiring — dep yang belum ter-serialize di scene
            if (movementController == null) movementController = FindAnyObjectByType<CharacterMovementController>();
            if (vrmaController == null) vrmaController = FindAnyObjectByType<VrmaPlaybackController>();
            if (handPoseController == null) handPoseController = FindAnyObjectByType<HandPoseController>();
        }

        private void Update()
        {
            if (stateManager == null) return;
            float now = Time.time;
            if (now < _nextTickTime) return;

            _nextTickTime = now + tickIntervalSec;

            // Pilih behavior weighted random dari yang valid
            var current = stateManager.CurrentState;
            var candidates = new List<BehaviorEntry>();
            float totalWeight = 0f;

            foreach (var b in behaviors)
            {
                if (!b.IsAllowedInState(current)) continue;
                if (!b.CanTrigger(now)) continue;
                if (b.kind == BehaviorEntry.Kind.AutonomousMove && now < _nextWanderTime) continue;
                // Di overlay, roam ditangani MacDesktopController (sadar-window) → skip wander acak.
                if (b.kind == BehaviorEntry.Kind.AutonomousMove && LiaInput.OverlayActive) continue;
                // JANGAN gesture saat sedang jalan — gesture (VRMA) mematikan Animator → FemWalk
                // berhenti → karakter terlihat "melayang". Biarkan jalan kaki tampil utuh.
                if (b.kind == BehaviorEntry.Kind.AnimatorTrigger &&
                    movementController != null && movementController.IsWalking) continue;
                // Hormati pilihan user di panel setting (gesture yang di-uncheck tidak dipakai).
                if (b.kind == BehaviorEntry.Kind.AnimatorTrigger && !GestureLibrary.IsEnabled(b.triggerName)) continue;

                candidates.Add(b);
                totalWeight += b.weight;
            }

            if (candidates.Count == 0) return;

            float pick = Random.value * totalWeight;
            float accum = 0f;
            BehaviorEntry chosen = null;
            foreach (var b in candidates)
            {
                accum += b.weight;
                if (pick <= accum) { chosen = b; break; }
            }
            if (chosen == null) chosen = candidates[candidates.Count - 1];

            ExecuteBehavior(chosen, now);
        }

        private void ExecuteBehavior(BehaviorEntry entry, float now)
        {
            entry.lastTriggeredTime = now;

            switch (entry.kind)
            {
                case BehaviorEntry.Kind.AnimatorTrigger:
                    // Prioritas: mocap clip (ClipGestureController, retarget benar) → VRMA → Animator.
                    if (_clipController == null) _clipController = FindAnyObjectByType<VRMAssistant.Animation.ClipGestureController>();
                    if (_clipController != null && _clipController.HasClip(entry.triggerName))
                    {
                        _clipController.PlayGesture(entry.triggerName);
                        Debug.Log($"[BehaviorScheduler] mocap gesture: {entry.label}");
                    }
                    else if (vrmaController != null && !string.IsNullOrEmpty(entry.triggerName))
                    {
                        vrmaController.PlayGesture(entry.triggerName);
                        Debug.Log($"[BehaviorScheduler] VRMA gesture: {entry.label}");
                    }
                    else if (_animator != null && HasAnimatorTrigger(_animator, entry.triggerName))
                    {
                        _animator.SetTrigger(entry.triggerName);
                        Debug.Log($"[BehaviorScheduler] Animator gesture (fallback): {entry.label}");
                    }
                    else
                    {
                        Debug.LogWarning($"[BehaviorScheduler] Skip {entry.label} (bukan mocap/VRMA/trigger '{entry.triggerName}')");
                    }
                    break;

                case BehaviorEntry.Kind.HandPose:
                    if (handPoseController != null)
                    {
                        handPoseController.SetPose(entry.handPoseTarget);
                        Debug.Log($"[BehaviorScheduler] Hand pose: {entry.label}");
                    }
                    break;

                case BehaviorEntry.Kind.AutonomousMove:
                    // Jalan ke titik acak di layar (FemWalk cycle + retarget via ClipGestureController).
                    _nextWanderTime = now + Random.Range(wanderMinIntervalSec, wanderMaxIntervalSec);
                    if (movementController == null) movementController = FindAnyObjectByType<CharacterMovementController>();
                    if (movementController != null)
                    {
                        if (Random.value < 0.3f) movementController.WalkToEdge();
                        else movementController.WalkToRandom();
                        Debug.Log("[BehaviorScheduler] Wander triggered");
                    }
                    else Debug.LogWarning("[BehaviorScheduler] Wander skip — movementController null");
                    break;

                case BehaviorEntry.Kind.CompositeSequence:
                    // Reserved untuk Phase 3 — multi-step behavior
                    Debug.Log($"[BehaviorScheduler] Composite (TODO Phase 3): {entry.label}");
                    break;
            }
        }

        private bool HasAnimatorTrigger(Animator animator, string name)
        {
            if (animator.runtimeAnimatorController == null) return false;
            foreach (var p in animator.parameters)
                if (p.name == name && p.type == AnimatorControllerParameterType.Trigger) return true;
            return false;
        }
    }
}
