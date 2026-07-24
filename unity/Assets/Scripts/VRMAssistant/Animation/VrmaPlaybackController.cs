using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using UnityEngine.Networking;
using UniVRM10;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Runtime VRMA playback dengan HumanPoseHandler retargeting (cross-VRM-version).
    ///
    /// Approach:
    /// 1. Load .vrma → spawn hidden VRMA instance dengan Animator + Avatar
    /// 2. Source HumanPoseHandler.GetHumanPose() — dapat muscle-space pose (T-pose normalized)
    /// 3. Target HumanPoseHandler.SetHumanPose() — apply ke Kohaku VRM 0.x avatar
    ///
    /// Muscle space cross-compatible antara VRM 1.0 dan VRM 0.x karena Mecanim normalisasi pose.
    /// Tidak ada twisting karena tidak copy raw localRotation.
    /// </summary>
    public class VrmaPlaybackController : MonoBehaviour
    {
        [Header("Target Model")]
        [SerializeField] private VRMModelLoader modelLoader;

        [Header("VRMA Files (StreamingAssets relative)")]
        [Tooltip("Mapping nama gesture → file path .vrma di StreamingAssets/VRMA/")]
        [SerializeField] private List<VrmaEntry> vrmaPool = new List<VrmaEntry>();

        // Daftar default isi StreamingAssets/VRMA/ — hardcode karena Directory scan
        // tidak jalan di Android (StreamingAssets ada di dalam APK).
        private static readonly string[] DefaultVrmaNames = {
            // Set gratis tk256ailab
            "Angry", "Blush", "Clapping", "Goodbye", "Jump",
            "LookAround", "Relax", "Sad", "Sleepy", "Surprised", "Thinking",
            // Set resmi VRoid/pixiv (VRMA_01-07) — lisensi bersih
            "ShowBody", "Greeting", "Peace", "Shoot", "Spin", "ModelPose", "Squat"
        };

        [System.Serializable]
        public class VrmaEntry
        {
            public string gestureName;
            public string vrmaFileName;
        }

        private Dictionary<string, GameObject> _loadedVrmaInstances = new Dictionary<string, GameObject>();
        private Dictionary<string, HumanPoseHandler> _sourceHandlers = new Dictionary<string, HumanPoseHandler>();

        private GameObject _activeVrmaInstance;
        private string _activeGestureName;
        private float _activeStartTime;
        private float _activeDuration;
        private bool _isPlaying;
        private HumanPoseHandler _activeSourceHandler;

        private HumanPoseHandler _targetHandler;
        private HumanPose _humanPose;   // reusable buffer (source)
        private HumanPose _targetPose;  // reusable buffer (target, untuk baseline body)

        // Baseline body pose target — di-capture SEKALI saat gesture mulai, dipakai ulang tiap frame.
        // JANGAN Get→Set target per frame: roundtrip HumanPoseHandler tidak idempoten,
        // error kecil terakumulasi → karakter melorot konstan (bug "jatuh" 2026-07-22).
        private Vector3 _baselineBodyPos;
        private Quaternion _baselineBodyRot = Quaternion.identity;

        // Fade-in: muscle di-blend dari pose saat gesture dipanggil → pose clip,
        // supaya transisi tidak "snap" kaku. 0.3s cukup untuk terasa halus di 60-120fps.
        private float[] _startMuscles;
        private const float FADE_IN_SEC = 0.35f;

        // Fade-out: blend muscle dari pose gesture terakhir → rest saat durasi habis,
        // supaya gesture tidak "snap" mati di akhir (keluhan "gerakan tiba-tiba/menyeramkan").
        private const float FADE_OUT_SEC = 0.4f;
        private bool _fadingOut;
        private float _fadeOutStart;
        private float[] _fadeOutFromMuscles;
        private float[] _restMuscles;

        // Rest pose cache (lazy, di first PlayGesture — setelah arm rest orchestrator pasti applied).
        // Restore saat stop supaya gesture tidak meninggalkan residue pose.
        private readonly Dictionary<Transform, Quaternion> _restRotCache = new Dictionary<Transform, Quaternion>();
        private Transform _hipsBone;
        private Vector3 _hipsRestLocalPos;
        private bool _restCached;

        private async void Start()
        {
            if (modelLoader == null) return;
            // SELALU subscribe (bukan else-branch) supaya character switch runtime re-wire handler
            modelLoader.OnModelLoaded += _ => OnTargetModelReady();
            if (modelLoader.LoadedModel != null) OnTargetModelReady();

            // Auto-register default VRMA files bila belum ada di pool (no scene wiring needed)
            foreach (var name in DefaultVrmaNames)
            {
                bool exists = false;
                foreach (var e in vrmaPool) if (e.gestureName == name) { exists = true; break; }
                if (!exists) vrmaPool.Add(new VrmaEntry { gestureName = name, vrmaFileName = name + ".vrma" });
            }

            // Pre-load VRMA pool entries
            foreach (var entry in vrmaPool)
            {
                if (string.IsNullOrEmpty(entry.gestureName) || string.IsNullOrEmpty(entry.vrmaFileName)) continue;
                _ = PreloadVrma(entry.gestureName, entry.vrmaFileName);
            }
            await Task.Yield();
        }

        private void OnTargetModelReady()
        {
            var animator = modelLoader.ModelAnimator;
            if (animator == null || animator.avatar == null)
            {
                Debug.LogWarning("[VrmaPlayback] Target Animator/Avatar belum siap");
                return;
            }

            // Reset state dari model sebelumnya (penting saat character switch)
            StopActiveVrma();
            _restRotCache.Clear();
            _restCached = false;
            _hipsBone = null;

            _targetHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            Debug.Log("[VrmaPlayback] Target HumanPoseHandler initialized");
        }

        public async void PlayGesture(string gestureName)
        {
            VrmaEntry entry = null;
            foreach (var e in vrmaPool)
                if (string.Equals(e.gestureName, gestureName, System.StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
            if (entry == null)
            {
                Debug.LogWarning($"[VrmaPlayback] Gesture '{gestureName}' tidak ada di pool");
                return;
            }
            gestureName = entry.gestureName;

            // Tangkap pose HIDUP saat ini (idle Animator / gesture sebelumnya) SEBELUM di-reset,
            // supaya fade-in blend dari pose nyata → mulus (bukan snap dari rest dulu).
            if (_targetHandler != null)
            {
                _targetHandler.GetHumanPose(ref _targetPose);
                _startMuscles = (float[])_targetPose.muscles.Clone();
            }

            GameObject instance;
            if (!_loadedVrmaInstances.TryGetValue(gestureName, out instance))
            {
                instance = await PreloadVrma(gestureName, entry.vrmaFileName);
                if (instance == null) return;
            }

            StopActiveVrma();
            _fadingOut = false;

            // Cache + restore rest pose supaya baseline body di-capture dari pose bersih
            CacheRestPoseIfNeeded();
            RestoreRestPose();

            // Capture baseline body + rest muscles (target fade-out) dari pose bersih
            if (_targetHandler != null)
            {
                _targetHandler.GetHumanPose(ref _targetPose);
                _baselineBodyPos = _targetPose.bodyPosition;
                _baselineBodyRot = _targetPose.bodyRotation;
                _restMuscles = (float[])_targetPose.muscles.Clone();
            }

            _activeVrmaInstance = instance;
            _activeGestureName = gestureName;
            _activeStartTime = Time.time;
            _isPlaying = true;
            _sourceHandlers.TryGetValue(gestureName, out _activeSourceHandler);

            // MATIKAN Animator (LiaAnimator) selama VRMA main — kalau tidak, Animator idle
            // menimpa pose VRMA tiap frame (dua sistem beradu). SetHumanPose tetap jalan
            // walau Animator disabled. Di-nyalakan lagi di StopActiveVrma → balik idle.
            if (modelLoader != null && modelLoader.ModelAnimator != null)
                modelLoader.ModelAnimator.enabled = false;

            // Activate source instance supaya Animation legacy update bones tiap frame
            instance.SetActive(true);

            // Animation component bisa ada di child (tergantung importer) — cari menyeluruh.
            var anim = instance.GetComponentInChildren<UnityEngine.Animation>(true);
            if (anim != null && anim.clip != null)
            {
                _activeDuration = anim.clip.length;
                anim.Play();
                Debug.Log($"[VrmaPlayback] Playing '{gestureName}' duration {_activeDuration:F2}s");
            }
            else
            {
                // Tanpa clip, source statis — kasih durasi fallback supaya auto-stop
                // tetap jalan (duration 0 = LateUpdate retarget selamanya = bug).
                _activeDuration = 3f;
                Debug.LogWarning($"[VrmaPlayback] '{gestureName}' tidak punya Animation clip — fallback duration 3s");
            }
        }

        /// <summary>Lazy cache rest pose humanoid (localRotation semua bone + localPosition hips).</summary>
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

        /// <summary>Kembalikan semua bone ke rest pose (rotasi) + hips ke posisi rest.</summary>
        private void RestoreRestPose()
        {
            if (!_restCached) return;
            foreach (var kv in _restRotCache)
                if (kv.Key != null) kv.Key.localRotation = kv.Value;
            if (_hipsBone != null) _hipsBone.localPosition = _hipsRestLocalPos;
        }

        private void StopActiveVrma()
        {
            if (_activeVrmaInstance == null) return;
            var anim = _activeVrmaInstance.GetComponentInChildren<UnityEngine.Animation>(true);
            if (anim != null) anim.Stop();
            _activeVrmaInstance.SetActive(false); // hide hidden instance lagi
            _activeVrmaInstance = null;
            _activeGestureName = null;
            _activeSourceHandler = null;
            _isPlaying = false;
            _fadingOut = false;

            // Bersihkan residue pose gesture (rotasi + posisi hips)
            RestoreRestPose();

            // Nyalakan Animator lagi → karakter balik ke idle (LiaAnimator).
            if (modelLoader != null && modelLoader.ModelAnimator != null)
                modelLoader.ModelAnimator.enabled = true;
        }

        private async Task<GameObject> PreloadVrma(string gestureName, string fileName)
        {
            if (_loadedVrmaInstances.ContainsKey(gestureName))
                return _loadedVrmaInstances[gestureName];

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "VRMA", fileName);
                byte[] bytes = await LoadBytesAsync(path);
                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogError($"[VrmaPlayback] Failed to load bytes: {path}");
                    return null;
                }

                using (var gltfData = new GlbBinaryParser(bytes, fileName).Parse())
                {
                    var vrmaData = new VrmAnimationData(gltfData);
                    using (var importer = new VrmAnimationImporter(vrmaData))
                    {
                        var instance = await importer.LoadAsync(new ImmediateCaller());
                        var go = instance.Root;
                        go.name = $"VRMA_{gestureName}";
                        // Move far away (out of camera) instead of SetActive(false)
                        // — Animation legacy hanya update kalau gameobject active
                        go.transform.SetParent(transform, false);
                        go.transform.localPosition = new Vector3(1000f, 1000f, 1000f);

                        // Set animation looping
                        var anim = go.GetComponent<UnityEngine.Animation>();
                        if (anim != null)
                        {
                            foreach (AnimationState state in anim)
                            {
                                state.wrapMode = WrapMode.Loop;
                            }
                        }

                        // Setup source HumanPoseHandler
                        var sourceAnimator = go.GetComponent<Animator>();
                        if (sourceAnimator != null && sourceAnimator.avatar != null)
                        {
                            var handler = new HumanPoseHandler(sourceAnimator.avatar, go.transform);
                            _sourceHandlers[gestureName] = handler;
                            Debug.Log($"[VrmaPlayback] Source HumanPoseHandler '{gestureName}' ready");
                        }
                        else
                        {
                            Debug.LogWarning($"[VrmaPlayback] '{gestureName}' tidak punya valid Avatar");
                        }

                        // Inactive sampai PlayGesture dipanggil
                        go.SetActive(false);
                        _loadedVrmaInstances[gestureName] = go;
                        Debug.Log($"[VrmaPlayback] Preloaded '{gestureName}' from {fileName}");
                        return go;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VrmaPlayback] Preload {fileName} gagal: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private async Task<byte[]> LoadBytesAsync(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result == UnityWebRequest.Result.Success)
                    return req.downloadHandler.data;
                return null;
            }
#else
            if (!File.Exists(path)) return null;
            return await Task.FromResult(File.ReadAllBytes(path));
#endif
        }

        private void LateUpdate()
        {
            if (!_isPlaying || _activeVrmaInstance == null || _activeSourceHandler == null || _targetHandler == null) return;

            // Durasi habis → mulai FADE-OUT ke rest (bukan langsung stop) supaya tidak snap.
            if (Time.time - _activeStartTime > _activeDuration && _activeDuration > 0f)
            {
                if (!_fadingOut)
                {
                    _fadingOut = true;
                    _fadeOutStart = Time.time;
                    // Snapshot pose gesture terakhir sebagai sumber fade-out
                    _activeSourceHandler.GetHumanPose(ref _humanPose);
                    _fadeOutFromMuscles = (float[])_humanPose.muscles.Clone();
                }

                float fo = FADE_OUT_SEC > 0f ? Mathf.Clamp01((Time.time - _fadeOutStart) / FADE_OUT_SEC) : 1f;
                if (fo >= 1f || _fadeOutFromMuscles == null || _restMuscles == null)
                {
                    StopActiveVrma();
                    return;
                }

                // Blend muscle: pose gesture terakhir → rest, body pinned ke baseline.
                _humanPose.bodyPosition = _baselineBodyPos;
                _humanPose.bodyRotation = _baselineBodyRot;
                int m = Mathf.Min(_fadeOutFromMuscles.Length, Mathf.Min(_restMuscles.Length, _humanPose.muscles.Length));
                for (int i = 0; i < m; i++)
                    _humanPose.muscles[i] = Mathf.Lerp(_fadeOutFromMuscles[i], _restMuscles[i], fo);
                _targetHandler.SetHumanPose(ref _humanPose);
                if (_hipsBone != null) _hipsBone.localPosition = _hipsRestLocalPos;
                return;
            }

            // HumanPose retargeting (muscle space, cross-VRM-version).
            // FIX bug rotasi safe-mode lama: bodyPosition/bodyRotation TIDAK ditransfer dari source —
            // pin ke BASELINE target (captured sekali di PlayGesture) supaya karakter tidak
            // flip/teleport mengikuti root VRMA, dan tidak drift akumulatif dari roundtrip Get→Set.
            // Trade-off: root motion (mis. lompatan Jump.vrma) tidak ikut — hanya muscle/limb.
            _activeSourceHandler.GetHumanPose(ref _humanPose);
            _humanPose.bodyPosition = _baselineBodyPos;
            _humanPose.bodyRotation = _baselineBodyRot;

            // Fade-in: blend muscle dari pose awal → pose clip di 0.3s pertama (anti-snap)
            if (_startMuscles != null)
            {
                float blend = Mathf.Clamp01((Time.time - _activeStartTime) / FADE_IN_SEC);
                if (blend < 1f)
                {
                    int n = Mathf.Min(_startMuscles.Length, _humanPose.muscles.Length);
                    for (int i = 0; i < n; i++)
                        _humanPose.muscles[i] = Mathf.Lerp(_startMuscles[i], _humanPose.muscles[i], blend);
                }
            }

            _targetHandler.SetHumanPose(ref _humanPose);

            // Pin hips localPosition ke rest — Get→Set bodyPosition tidak simetris saat
            // root model tidak di origin (root Y=-1 kebawa ganda → karakter melorot 1 unit).
            // Rotasi hips tetap dari retarget supaya lean/bow gesture jalan.
            if (_hipsBone != null) _hipsBone.localPosition = _hipsRestLocalPos;
        }

        public void StopGesture()
        {
            StopActiveVrma();
        }

        public bool IsPlaying => _isPlaying;
        public string ActiveGesture => _activeGestureName;
    }
}
