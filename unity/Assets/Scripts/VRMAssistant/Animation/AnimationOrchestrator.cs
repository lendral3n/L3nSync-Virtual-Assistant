using System.Collections.Generic;
using UnityEngine;
using VRM;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Orchestrator state machine animasi. Listen ke AssistantStateManager.OnStateChanged,
    /// toggle IAnimationState yang aktif, dan koordinasi controller (Blink, Expression, LookAt, LipSync).
    ///
    /// Auto-wire semua controller via VRMModelLoader.OnModelLoaded.
    /// </summary>
    public class AnimationOrchestrator : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;
        [SerializeField] private AssistantStateManager stateManager;

        [Header("Controllers (auto-wire saat model loaded)")]
        [SerializeField] private LipSyncController lipSyncController;
        [SerializeField] private AutoBlinkController blinkController;
        [SerializeField] private ExpressionController expressionController;
        [SerializeField] private LookAtController lookAtController;
        [SerializeField] private HandPoseController handPoseController;

        [Header("LookAt Target")]
        [SerializeField] private Transform lookAtTarget;

        [Header("Animator Controller (auto-load saat model loaded)")]
        // LiaAnimator = jalur standar Unity (keputusan 2026-07-22): Idle(IdleVar)↔Walk(FemWalk)
        // + 11 gesture trigger, semua clip mocap humanoid ASLI (Mixamo/Bandai) — Mecanim
        // retarget otomatis. Procedural body additive skip saat controller aktif (LateUpdate);
        // facial (blink/lipsync/lookat/expression) tetap jalan karena level blendshape.
        [SerializeField] private string animatorControllerPath = "LiaAnimator";
        [SerializeField] private bool autoAssignAnimatorController = true;

        [Header("Animation State Configs")]
        [SerializeField] private IdleAnimationState idleState = new IdleAnimationState();
        [SerializeField] private ActiveAnimationState activeState = new ActiveAnimationState();
        [SerializeField] private ThinkingAnimationState thinkingState = new ThinkingAnimationState();
        [SerializeField] private ListeningAnimationState listeningState = new ListeningAnimationState();
        [SerializeField] private SpeakingAnimationState speakingState = new SpeakingAnimationState();

        private Dictionary<AssistantState, IAnimationState> _stateMap;
        private IAnimationState _currentState;
        private BoneReferences _bones;
        private bool _ready;

        // Reference ke VmdPlaybackController/VrmaPlaybackController — saat IsPlaying true,
        // skip additive layer supaya playback frame tidak fight dengan procedural offset
        // di same LateUpdate cycle.
        private VmdPlaybackController _vmdController;
        private VrmaPlaybackController _vrmaController;
        private ClipGestureController _clipController;

        private void Awake()
        {
            _stateMap = new Dictionary<AssistantState, IAnimationState>
            {
                { AssistantState.Idle, idleState },
                { AssistantState.Active, activeState },
                { AssistantState.Thinking, thinkingState },
                { AssistantState.Listening, listeningState },
                { AssistantState.Speaking, speakingState },
            };

            // Wire up SpeakingState ke LipSyncController di awal
            if (lipSyncController != null) speakingState.lipSyncController = lipSyncController;

            // Subscribe state changes
            if (stateManager != null) stateManager.OnStateChanged += HandleStateChanged;
        }

        private void Start()
        {
            // SELALU subscribe (bukan else-branch) supaya character switch runtime re-wire semua
            if (modelLoader != null)
            {
                modelLoader.OnModelLoaded += OnModelLoaded;
                if (modelLoader.LoadedModel != null) OnModelLoaded(modelLoader.LoadedModel);
            }
        }

        private void OnDestroy()
        {
            if (modelLoader != null) modelLoader.OnModelLoaded -= OnModelLoaded;
            if (stateManager != null) stateManager.OnStateChanged -= HandleStateChanged;
        }

        private void OnModelLoaded(GameObject model)
        {
            // Resolve bones
            _bones = BoneMapper.Resolve(modelLoader.ModelAnimator);
            if (!_bones.IsValid)
            {
                Debug.LogError("[Orchestrator] BoneReferences invalid! Cek apakah Animator humanoid valid.");
                return;
            }

            // Phase B fix (iterated): Lower character Y supaya feet TEPAT di bottom edge view.
            // Math: camera Y=0.15, Z=2.0, FOV vertikal 60° → view bottom edge Y = 0.15 - 2.0*tan(30°)
            //                                                                  = 0.15 - 1.155 = -1.005
            // Character Y = -1.0 → feet di Y=-1.0 (1cm above mathematical bottom, safe margin)
            //                       head di Y=-1.0+1.6 = +0.6 (well within view top +1.3)
            // Visual: character fills ~70% of strip vertical, feet flush dengan strip bottom edge.
            var pos = model.transform.position;
            model.transform.position = new Vector3(pos.x, -1.0f, pos.z);
            Debug.Log($"[Orchestrator] Character Y lowered to -1.0 (was {pos.y:F2}) — feet di strip bottom edge, no more terbang.");

            // Initialize semua animation state dengan bones
            foreach (var s in _stateMap.Values) s.Initialize(_bones);

            // Apply natural arm rest pose dari T-pose ke A-pose (arms swing down to side body).
            // Berdasarkan analisis bone-xyz-directions.md (right-hand rule + Kohaku bone hierarchy):
            //   LeftUpperArm  Euler(0, 0, -75)  → swing down to LEFT side  (NEGATIVE Z)
            //   RightUpperArm Euler(0, 0, +75)  → swing down to RIGHT side (POSITIVE Z, mirror)
            // Slight elbow bend supaya tangan tidak terlalu lurus stiff:
            //   LeftLowerArm  Euler(0, +10, 0)  → forearm slight forward
            //   RightLowerArm Euler(0, -10, 0)  → mirror
            ApplyNaturalArmRest();

            // Cache rest pose untuk additive layer SETELAH arm rest applied,
            // supaya procedural offset apply on top of A-pose (bukan T-pose).
            AdditiveLayerHelper.CacheRestPose(_bones);

            Debug.Log("[Orchestrator] Bones resolved + natural arm rest applied + rest pose cached. " +
                "Bones: chest=" + (_bones.chest != null ? _bones.chest.name : "null") +
                " head=" + (_bones.head != null ? _bones.head.name : "null") +
                " leftUpperArm=" + (_bones.leftUpperArm != null ? _bones.leftUpperArm.name : "null"));

            // Wire up controllers ke VRM components
            var proxy = modelLoader.BlendShapeProxy;
            if (lipSyncController != null) lipSyncController.SetBlendShapeProxy(proxy);
            if (blinkController != null) blinkController.SetBlendShapeProxy(proxy);
            if (expressionController != null) expressionController.SetBlendShapeProxy(proxy);

            if (lookAtController != null)
            {
                var lookAtHead = model.GetComponent<VRMLookAtHead>();
                lookAtController.SetLookAtHead(lookAtHead);
                if (lookAtTarget != null) lookAtController.SetTarget(lookAtTarget);
            }

            // Initialize HandPoseController dengan animator (untuk resolve finger bones)
            if (handPoseController != null && modelLoader.ModelAnimator != null)
            {
                handPoseController.Initialize(modelLoader.ModelAnimator);
            }

            // Auto-attach CharacterMovementController ke VRMAssistant root (idempotent).
            EnsureMovementController();

            // Auto-attach VmdPlaybackController + wire ke CommandReceiver
            EnsureVmdComponents();

            // Auto-attach WanderController ke runtime VRM model GameObject (sama dengan Animator).
            // WanderController [RequireComponent(Animator)] — wajib di model, bukan VRMAssistant root.
            EnsureWanderController(model);

            // Animator Controller assignment — default OFF, procedural + VMD/VRMA jadi jalur utama.
            EnsureAnimatorController();  // no-op saat autoAssignAnimatorController=false

            _ready = true;

            // Apply state pertama kali (mungkin sudah di-set sebelum model loaded)
            if (stateManager != null)
            {
                HandleStateChanged(stateManager.PreviousState, stateManager.CurrentState);
            }

            Debug.Log("[Orchestrator] Model siap, semua controller ter-wire.");
        }

        /// <summary>
        /// Rotate upper arms + lower arms ke natural A-pose dari default T-pose VRM.
        /// Direct localRotation set sebelum CacheRestPose, supaya procedural offset
        /// kemudian apply on top of natural pose (bukan T-pose).
        ///
        /// Axis convention dari docs/bone-xyz-directions.md (right-hand rule + Kohaku hierarchy):
        ///   LEFT arm Z negative → swing down (arm pointing -X, rotates -X → -Y)
        ///   RIGHT arm Z positive → swing down (mirror, arm pointing +X, rotates +X → -Y)
        /// </summary>
        private void ApplyNaturalArmRest()
        {
            if (!_bones.IsValid) return;

            // Upper arms: swing down nearly vertical (T-pose horizontal → arms down beside body).
            // 90° = arms fully vertical lurus ke bawah; 88° = sedikit angle untuk natural look.
            // SIGN diverifikasi empiris di Editor 2026-07-22 (screenshot loop):
            // Kohaku_dress VRM → LEFT +88 / RIGHT -88 (kebalikan asumsi lama yang bikin lengan ke ATAS).
            // 82° (bukan 88°) + sedikit rotasi Y supaya forearm agak ke depan —
            // tangan tidak terkubur di rok lebar, siku ada tekukan natural gaya anime/MMO idle.
            if (_bones.leftUpperArm != null)
            {
                _bones.leftUpperArm.localRotation = Quaternion.Euler(0, -6, 82);
                Debug.Log($"[ArmRest] LeftUpperArm set to Euler(0,-6,+82)");
            }
            if (_bones.rightUpperArm != null)
            {
                _bones.rightUpperArm.localRotation = Quaternion.Euler(0, 6, -82);
                Debug.Log($"[ArmRest] RightUpperArm set to Euler(0,+6,-82)");
            }

            // Lower arms: tekukan siku ke depan supaya tangan rileks di depan paha
            if (_bones.leftLowerArm != null)
            {
                _bones.leftLowerArm.localRotation = Quaternion.Euler(0, -14, 0);
            }
            if (_bones.rightLowerArm != null)
            {
                _bones.rightLowerArm.localRotation = Quaternion.Euler(0, 14, 0);
            }
        }

        private void HandleStateChanged(AssistantState prev, AssistantState next)
        {
            if (!_ready) return; // tunggu model loaded

            // Exit state lama
            _currentState?.OnExit();

            // Enter state baru
            if (_stateMap.TryGetValue(next, out var newState))
            {
                _currentState = newState;
                _currentState.OnEnter();
            }
            else
            {
                _currentState = null;
                Debug.LogWarning($"[Orchestrator] No animation state mapped for {next}");
            }

            // Update controllers berdasarkan state baru
            if (expressionController != null) expressionController.SetExpressionForState(next);
            if (lookAtController != null) lookAtController.SetModeForState(next);

            // LipSync hanya aktif saat Speaking
            if (lipSyncController != null) lipSyncController.active = (next == AssistantState.Speaking);

            // Hand pose default per state — auto switching pause untuk Thinking/Speaking yang punya pose tetap
            if (handPoseController != null)
            {
                switch (next)
                {
                    case AssistantState.Thinking:
                        handPoseController.SetAutoSwitch(false);
                        handPoseController.SetPose(HandPoseController.HandPose.NearFace);
                        break;
                    case AssistantState.Speaking:
                        handPoseController.SetAutoSwitch(false);
                        handPoseController.SetPose(HandPoseController.HandPose.OpenGesture);
                        break;
                    case AssistantState.Listening:
                        handPoseController.SetAutoSwitch(false);
                        handPoseController.SetPose(HandPoseController.HandPose.Relaxed);
                        break;
                    case AssistantState.Idle:
                    case AssistantState.Active:
                    default:
                        handPoseController.SetAutoSwitch(true); // random switching
                        break;
                }
            }

            // Trigger Animator state untuk state baru (kalau Animator ada)
            ApplyAnimatorState(next);
        }

        /// <summary>
        /// Set Animator parameter "AssistantState" int agar transition state machine match enum.
        /// Animator Controller harus punya int param "AssistantState" + transitions yang baca int ini.
        /// Kalau Animator belum di-assign clip, fallback no-op.
        /// </summary>
        private void ApplyAnimatorState(AssistantState state)
        {
            if (modelLoader == null || modelLoader.ModelAnimator == null) return;
            var animator = modelLoader.ModelAnimator;
            if (animator.runtimeAnimatorController == null) return; // belum ada controller
            animator.SetInteger("AssistantState", (int)state);
        }

        private float _debugLogTimer = 0f;

        private void LateUpdate()
        {
            if (!_ready || _currentState == null) return;

            // FIX 1: Mutual exclusion VMD/VRMA/Clip vs procedural — saat playback jalan, skip additive.
            if (_vmdController != null && _vmdController.IsPlaying) return;
            if (_vrmaController == null) _vrmaController = FindAnyObjectByType<VrmaPlaybackController>();
            if (_vrmaController != null && _vrmaController.IsPlaying) return;
            if (_clipController == null) _clipController = FindAnyObjectByType<ClipGestureController>();
            if (_clipController != null && _clipController.IsPlaying) return;

            // FIX 2 RESTORED (Phase B): Procedural additive SKIPPED saat Animator runtime
            // controller present. Original removal di Phase A trigger SIGTRAP crash
            // (Active→Thinking transition, tombstone_02 di 17:18:07).
            // Hipotesis: procedural + Animator + BehaviorScheduler bersaing menulis bone
            // localRotation di LateUpdate → race condition di IL2CPP managed thread →
            // memory corruption → native SIGTRAP.
            // Trade-off: kehilangan procedural breathing additive saat Animator playing,
            // tapi Animator clip sendiri include chest/RootT/Head curves jadi visual tetap alive.
            if (modelLoader != null && modelLoader.ModelAnimator != null
                && modelLoader.ModelAnimator.runtimeAnimatorController != null)
            {
                return;
            }

            // Step 1 architecture: state mengisi BoneOffsets, orchestrator apply additive
            // setelah Animator selesai write bone localRotation. Hanya jalan kalau
            // tidak ada Animator Controller (fallback procedural-only mode).
            var offsets = BoneOffsets.Identity;
            _currentState.Tick(Time.deltaTime, ref offsets);
            AdditiveLayerHelper.ApplyAdditive(_bones, offsets);

            // Debug log offset every 2 seconds untuk verify state working + catch upside-down
            _debugLogTimer += Time.deltaTime;
            if (_debugLogTimer > 2f)
            {
                _debugLogTimer = 0f;
                var chestEuler = offsets.chest.eulerAngles;
                var headEuler = offsets.head.eulerAngles;
                var modelTransform = modelLoader != null ? modelLoader.LoadedModel?.transform : null;
                if (modelTransform != null)
                {
                    var modelRot = modelTransform.eulerAngles;
                    var modelPos = modelTransform.position;
                    var modelScale = modelTransform.localScale;
                    Debug.Log($"[Orchestrator Tick] state={_currentState.State} model: pos={modelPos}, " +
                              $"rot=({modelRot.x:F1},{modelRot.y:F1},{modelRot.z:F1}), scale={modelScale.x:F2}");
                }
                else
                {
                    Debug.Log($"[Orchestrator Tick] state={_currentState.State} chest=({chestEuler.x:F1},{chestEuler.y:F1},{chestEuler.z:F1}) head=({headEuler.x:F1},{headEuler.y:F1},{headEuler.z:F1})");
                }
            }
        }

        /// <summary>
        /// Auto-attach CharacterMovementController ke gameObject yang sama. Idempotent.
        /// Mengeliminasi kebutuhan manual scene editing setiap rebuild Unity.
        /// </summary>
        private void EnsureMovementController()
        {
            var movementType = System.Type.GetType("VRMAssistant.Behavior.CharacterMovementController, Assembly-CSharp");
            if (movementType == null) return;

            var movement = gameObject.GetComponent(movementType) as MonoBehaviour;
            if (movement == null) movement = gameObject.AddComponent(movementType) as MonoBehaviour;

            // Wire dependencies via reflection
            var loaderField = movementType.GetField("modelLoader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loaderField?.SetValue(movement, modelLoader);
            var stateField = movementType.GetField("stateManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            stateField?.SetValue(movement, stateManager);
        }

        /// <summary>
        /// Auto-load AnimatorController dari project assets dan assign ke model Animator.
        /// Setelah ini, Animator akan drive bone animation dari clip yang diimport (kohaku_states.fbx).
        /// </summary>
        private void EnsureAnimatorController()
        {
            if (!autoAssignAnimatorController) return;
            if (modelLoader == null || modelLoader.ModelAnimator == null) return;

            var animator = modelLoader.ModelAnimator;
            if (animator.runtimeAnimatorController != null)
            {
                Debug.Log($"[Orchestrator] Animator already has controller: {animator.runtimeAnimatorController.name}");
                return;
            }

            // Coba load dari Resources path dulu (kalau dipindah di Resources/)
            var controller = Resources.Load<RuntimeAnimatorController>(animatorControllerPath);

#if UNITY_EDITOR
            // Editor fallback: load via AssetDatabase kalau tidak ada di Resources
            if (controller == null)
            {
                controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    $"Assets/{animatorControllerPath}.controller");
            }
#endif

            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                // Root motion OFF — posisi karakter dikendalikan window Kotlin (roam) /
                // CharacterMovementController, BUKAN oleh clip. Tanpa ini karakter
                // "jalan sendiri" keluar frame mengikuti root motion clip.
                animator.applyRootMotion = false;
                Debug.Log($"[Orchestrator] Animator Controller assigned: {controller.name} " +
                          $"(parameters: {animator.parameterCount}, rootMotion=off)");
            }
            else
            {
                Debug.LogWarning($"[Orchestrator] Animator Controller not found at '{animatorControllerPath}'. " +
                                 "Procedural-only mode (no Animator clips).");
            }
        }

        /// <summary>
        /// Public API: trigger one-shot gesture animation (e.g., Wave).
        /// Dipanggil oleh CommandReceiver via UnitySendMessage("PlayGesture", "Wave").
        /// </summary>
        public void PlayGesture(string gestureName)
        {
            if (modelLoader == null || modelLoader.ModelAnimator == null) return;
            var animator = modelLoader.ModelAnimator;
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[Orchestrator] PlayGesture('{gestureName}') skipped — no Animator Controller assigned");
                return;
            }
            // Trigger by parameter name (Wave is an Animator trigger param)
            animator.SetTrigger(gestureName);
            Debug.Log($"[Orchestrator] PlayGesture triggered: {gestureName}");
        }

        /// <summary>
        /// Auto-attach WanderController ke runtime VRM model GameObject.
        /// WanderController butuh Animator pada GameObject yang sama ([RequireComponent]).
        /// Idempotent — skip kalau sudah ada.
        /// </summary>
        private void EnsureWanderController(GameObject model)
        {
            if (model == null) return;
            var wanderType = System.Type.GetType("VRMAssistant.Locomotion.WanderController, Assembly-CSharp");
            if (wanderType == null)
            {
                Debug.LogWarning("[Orchestrator] WanderController type not found via reflection — skip.");
                return;
            }
            var existing = model.GetComponent(wanderType);
            if (existing == null)
            {
                model.AddComponent(wanderType);
                Debug.Log("[Orchestrator] WanderController attached to VRM model.");
            }
            else
            {
                Debug.Log("[Orchestrator] WanderController already present on VRM model.");
            }
        }

        /// <summary>Auto-attach VmdPlaybackController + wire ke CommandReceiver.</summary>
        private void EnsureVmdComponents()
        {
            var vmd = gameObject.GetComponent<VmdPlaybackController>();
            if (vmd == null) vmd = gameObject.AddComponent<VmdPlaybackController>();
            _vmdController = vmd;

            // Wire modelLoader (private SerializeField)
            var vmdType = typeof(VmdPlaybackController);
            var loaderField = vmdType.GetField("modelLoader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loaderField?.SetValue(vmd, modelLoader);

            // Wire ke CommandReceiver.vmdController
            var receiverType = System.Type.GetType("VRMAssistant.AI.CommandReceiver, Assembly-CSharp");
            if (receiverType != null)
            {
                var receiver = gameObject.GetComponent(receiverType) as MonoBehaviour;
                if (receiver != null)
                {
                    var vmdField = receiverType.GetField("vmdController",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    vmdField?.SetValue(receiver, vmd);
                }
            }
            Debug.Log("[Orchestrator] VmdPlaybackController auto-attached + wired");
        }
    }
}
