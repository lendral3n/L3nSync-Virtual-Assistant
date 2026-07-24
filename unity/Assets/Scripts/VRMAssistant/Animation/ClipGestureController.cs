using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Playback AnimationClip humanoid (mocap Bandai Namco / Mixamo) ke karakter VRM
    /// via HumanPoseHandler retarget — mesin yang sama dengan VrmaPlaybackController.
    ///
    /// Arsitektur: rig sumber tersembunyi (Resources/MocapRig) memainkan clip via
    /// PlayableGraph, tiap LateUpdate pose muscle-nya ditransfer ke Kohaku dengan:
    ///   - body position/rotation di-pin ke baseline target (anti flip/teleport/drift)
    ///   - hips localPosition di-pin ke rest (anti sink — root offset kebawa ganda)
    ///   - fade-in 0.3s dari pose berjalan (anti snap)
    ///   - restore rest pose saat stop (anti residue)
    ///
    /// Clip di Resources/MocapClips/*.anim. Loop clips (Walk/Run) via PlayLoop/StopLoop.
    /// </summary>
    public class ClipGestureController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;

        private static readonly string[] ClipNames = {
            // Bandai Namco mocap (feminine style)
            "Walk", "Run", "Bow", "Bye", "ByeBye", "WaveHand",
            "WaveBoth", "RaiseHand", "Call", "DanceShort", "Respond",
            // Mixamo (FemWalk = walk cycle 1.2s murni — default locomotion)
            "FemWalk", "HappyIdle", "IdleVar", "Laughing"
        };

        private readonly Dictionary<string, AnimationClip> _clips =
            new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);

        private GameObject _sourceRig;
        private Animator _sourceAnimator;
        private HumanPoseHandler _sourceHandler;
        private HumanPoseHandler _targetHandler;
        private HumanPose _pose;
        private HumanPose _targetPose;

        private PlayableGraph _graph;
        private bool _graphAlive;

        private bool _isPlaying;
        private bool _isLooping;
        private string _activeName;
        private float _startTime;
        private float _duration;

        private Vector3 _baselineBodyPos;
        private Quaternion _baselineBodyRot = Quaternion.identity;
        private float[] _startMuscles;
        private const float FADE_IN_SEC = 0.3f;

        private readonly Dictionary<Transform, Quaternion> _restRotCache = new Dictionary<Transform, Quaternion>();
        private Transform _hipsBone;
        private Vector3 _hipsRestLocalPos;
        private bool _restCached;

        // Idle mocap sebagai animasi DASAR yang selalu jalan (bukan A-pose kaku).
        // Gesture/jalan menimpanya sementara, lalu balik ke idle ini. Tangan pun natural.
        [SerializeField] private string defaultIdle = "IdleVar";
        private bool _idleStarted;

        public bool IsPlaying => _isPlaying;
        public string ActiveClip => _activeName;
        public bool HasClip(string name) => _clips.ContainsKey(name ?? "");

        /// <summary>Mulai/kembali ke loop idle dasar (dipanggil saat ready + setelah gesture/jalan).</summary>
        public void ResumeIdle()
        {
            if (!string.IsNullOrEmpty(defaultIdle) && _clips.ContainsKey(defaultIdle))
                Play(defaultIdle, loop: true);
        }

        private void Start()
        {
            if (modelLoader == null) modelLoader = FindAnyObjectByType<VRMModelLoader>();
            if (modelLoader == null) { Debug.LogWarning("[ClipGesture] VRMModelLoader tidak ada"); return; }

            // SELALU subscribe supaya character switch re-wire handler
            modelLoader.OnModelLoaded += _ => OnTargetModelReady();
            if (modelLoader.LoadedModel != null) OnTargetModelReady();

            LoadClips();
            SetupSourceRig();
        }

        private void LoadClips()
        {
            foreach (var n in ClipNames)
            {
                var clip = Resources.Load<AnimationClip>("MocapClips/" + n);
                if (clip != null) _clips[n] = clip;
                else Debug.LogWarning("[ClipGesture] Clip tidak ketemu: " + n);
            }
            Debug.Log("[ClipGesture] " + _clips.Count + " clip loaded");
        }

        private void SetupSourceRig()
        {
            var prefab = Resources.Load<GameObject>("MocapRig");
            if (prefab == null) { Debug.LogError("[ClipGesture] Resources/MocapRig tidak ada"); return; }
            _sourceRig = Instantiate(prefab, new Vector3(2000f, 2000f, 2000f), Quaternion.identity, transform);
            _sourceRig.name = "MocapSourceRig(hidden)";
            _sourceAnimator = _sourceRig.GetComponent<Animator>();
            if (_sourceAnimator == null || _sourceAnimator.avatar == null || !_sourceAnimator.avatar.isHuman)
            {
                Debug.LogError("[ClipGesture] MocapRig tidak punya humanoid Animator");
                return;
            }
            _sourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // off-screen tetap update
            _sourceHandler = new HumanPoseHandler(_sourceAnimator.avatar, _sourceAnimator.transform);
        }

        private void OnTargetModelReady()
        {
            var animator = modelLoader.ModelAnimator;
            if (animator == null || animator.avatar == null) return;

            StopInternal(); // reset state model sebelumnya
            _restRotCache.Clear();
            _restCached = false;
            _hipsBone = null;

            _targetHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            Debug.Log("[ClipGesture] Target handler ready");
        }

        /// <summary>Play gesture sekali (auto-stop di akhir clip).</summary>
        public bool PlayGesture(string name) => Play(name, loop: false);

        /// <summary>Play clip looping (Walk/Run) — berhenti via StopLoop/Stop.</summary>
        public bool PlayLoop(string name) => Play(name, loop: true);

        public void StopLoop() { if (_isLooping) StopAndRestore(); }
        public void Stop() { if (_isPlaying) StopAndRestore(); }

        private bool Play(string name, bool loop)
        {
            if (_sourceAnimator == null || _targetHandler == null) return false;
            if (!_clips.TryGetValue(name ?? "", out var clip)) return false;

            StopInternal();
            CacheRestPoseIfNeeded();
            RestoreRestPose();

            // Baseline body dari pose bersih (anti drift — JANGAN Get→Set per frame)
            _targetHandler.GetHumanPose(ref _targetPose);
            _baselineBodyPos = _targetPose.bodyPosition;
            _baselineBodyRot = _targetPose.bodyRotation;
            _startMuscles = (float[])_targetPose.muscles.Clone();

            // Mainkan clip di rig sumber via PlayableGraph (tanpa AnimatorController)
            AnimationPlayableUtilities.PlayClip(_sourceAnimator, clip, out _graph);
            _graphAlive = true;

            _isPlaying = true;
            _isLooping = loop;
            _activeName = name;
            _startTime = Time.time;
            _duration = clip.length;

            Debug.Log("[ClipGesture] Play '" + name + "' " + _duration.ToString("F1") + "s loop=" + loop);
            return true;
        }

        private void LateUpdate()
        {
            if (!_isPlaying || _sourceHandler == null || _targetHandler == null) return;

            // Auto-stop untuk one-shot
            if (!_isLooping && Time.time - _startTime > _duration)
            {
                StopAndRestore();
                return;
            }

            _sourceHandler.GetHumanPose(ref _pose);
            _pose.bodyPosition = _baselineBodyPos;
            _pose.bodyRotation = _baselineBodyRot;

            // Fade-in anti-snap
            if (_startMuscles != null)
            {
                float blend = Mathf.Clamp01((Time.time - _startTime) / FADE_IN_SEC);
                if (blend < 1f)
                {
                    int n = Mathf.Min(_startMuscles.Length, _pose.muscles.Length);
                    for (int i = 0; i < n; i++)
                        _pose.muscles[i] = Mathf.Lerp(_startMuscles[i], _pose.muscles[i], blend);
                }
            }

            _targetHandler.SetHumanPose(ref _pose);

            // Pin hips localPosition (anti sink — lihat VrmaPlaybackController)
            if (_hipsBone != null) _hipsBone.localPosition = _hipsRestLocalPos;
        }

        private void StopAndRestore()
        {
            StopInternal();
            RestoreRestPose();
        }

        private void StopInternal()
        {
            if (_graphAlive) { _graph.Destroy(); _graphAlive = false; }
            _isPlaying = false;
            _isLooping = false;
            _activeName = null;
        }

        private void OnDestroy()
        {
            if (_graphAlive) _graph.Destroy();
        }

        private void CacheRestPoseIfNeeded()
        {
            if (_restCached || modelLoader == null || modelLoader.ModelAnimator == null) return;
            var animator = modelLoader.ModelAnimator;
            foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(b);
                if (t != null) _restRotCache[t] = t.localRotation;
            }
            _hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_hipsBone != null) _hipsRestLocalPos = _hipsBone.localPosition;
            _restCached = true;
        }

        private void RestoreRestPose()
        {
            if (!_restCached) return;
            foreach (var kv in _restRotCache)
                if (kv.Key != null) kv.Key.localRotation = kv.Value;
            if (_hipsBone != null) _hipsBone.localPosition = _hipsRestLocalPos;
        }
    }
}
