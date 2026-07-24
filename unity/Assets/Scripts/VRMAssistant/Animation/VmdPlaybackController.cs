using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Custom VMD JSON playback engine — bypass Blender/Animator Controller pipeline.
    ///
    /// Source: MMD Vocaloid Motion Data dari github.com/bear0830/mmd, parsed via Tools/vmd_to_json.py
    /// + filtered via Tools/vmd_filter_optimize.py menjadi humanoid bones only (HumanBodyBones).
    ///
    /// Coordinate convention (VMD = MMD = Unity-compatible left-handed):
    /// - X right, Y up, Z forward (sama Unity)
    /// - Quaternion (x, y, z, w) — direct compatible
    ///
    /// Pakai sebagai alternatif Animator clip. Trigger via UnityBridge.PlayVmd("walk").
    /// File source: Assets/StreamingAssets/Anim/*.json
    /// </summary>
    public class VmdPlaybackController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;

        [Header("Playback Settings")]
        [Tooltip("Bones default available di pool — preload saat Start")]
        [SerializeField] private string[] preloadAnimNames = {
            // MMD Vocaloid community
            "walk", "nekomimi", "foxsay", "fuwari", "heartbeat", "baby"
        };

        [Header("Runtime")]
        [SerializeField] private bool isPlaying = false;
        [SerializeField] private string activeAnimName = "";
        [SerializeField] private float playbackTime = 0f;
        [SerializeField] private float playbackSpeed = 1.0f;
        [SerializeField] private bool loop = true;

        [Header("Bone Application")]
        [Tooltip("Saat true, bone localRotation di-OVERRIDE per frame (bypass procedural).")]
        [SerializeField] private bool overrideBones = true;
        [Tooltip("Bones to skip override (let procedural/state animation run di bones ini).")]
        [SerializeField] private HumanBodyBones[] skipBones = { };

        // Per-bone keyframe stream
        [Serializable]
        private class Keyframe
        {
            public int frame;
            public float[] t;  // translation [x, y, z]
            public float[] r;  // rotation quaternion [x, y, z, w]
        }

        [Serializable]
        private class AnimationData
        {
            public string name;
            public string modelName;
            public float duration;
            public int frameRate;
            public int sampleRate;
            // bones: Dictionary<string HumanBodyBones name, List<Keyframe>>
            // Unity JsonUtility tidak support Dictionary, parse manual
        }

        // Loaded animations: name → bone keyframe map
        private Dictionary<string, Dictionary<HumanBodyBones, List<Keyframe>>> _loadedAnims =
            new Dictionary<string, Dictionary<HumanBodyBones, List<Keyframe>>>();
        private Dictionary<string, float> _animDurations = new Dictionary<string, float>();
        private Dictionary<HumanBodyBones, Transform> _boneCache = new Dictionary<HumanBodyBones, Transform>();
        private HashSet<HumanBodyBones> _skipSet = new HashSet<HumanBodyBones>();

        // Rest pose cache — captured AFTER AnimationOrchestrator applies arm rest A-pose.
        // Dipakai untuk reset bone saat Play() / Stop() supaya animasi sebelumnya tidak tumpuk.
        private Dictionary<HumanBodyBones, Quaternion> _restPoseCache = new Dictionary<HumanBodyBones, Quaternion>();

        // Current playing animation reference
        private Dictionary<HumanBodyBones, List<Keyframe>> _activeAnim;
        private float _activeDuration;

        private async void Start()
        {
            if (modelLoader == null) return;
            // SELALU subscribe (bukan else-branch) supaya character switch runtime re-wire bone cache
            modelLoader.OnModelLoaded += _ => OnModelReady();
            if (modelLoader.LoadedModel != null) OnModelReady();

            foreach (var b in skipBones) _skipSet.Add(b);

            // Pre-load animations
            foreach (var animName in preloadAnimNames)
            {
                await LoadAnim(animName);
            }
        }

        private void OnModelReady()
        {
            var animator = modelLoader.ModelAnimator;
            if (animator == null) return;

            // Reset state playback + cache lama (penting saat character switch)
            if (isPlaying) Stop();
            _boneCache.Clear();

            // Cache all human body bones we need
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null) _boneCache[bone] = t;
            }

            // Cache rest pose dari current state — dipanggil setelah AnimationOrchestrator
            // sudah apply ApplyNaturalArmRest, jadi rest pose = A-pose (lengan di samping body),
            // bukan T-pose default VRM.
            CacheRestPose();

            Debug.Log($"[VmdPlayback] Bone cache populated: {_boneCache.Count} bones, rest pose cached: {_restPoseCache.Count}");
        }

        /// <summary>
        /// Snapshot rest pose semua bone di _boneCache. Dipakai untuk reset bone saat Play/Stop
        /// supaya animasi sebelumnya tidak tumpuk dengan animasi baru.
        /// </summary>
        private void CacheRestPose()
        {
            _restPoseCache.Clear();
            foreach (var kv in _boneCache)
            {
                _restPoseCache[kv.Key] = kv.Value.localRotation;
            }
        }

        /// <summary>
        /// Reset semua bone yang dipakai VMD ke rest pose. Dipanggil saat Play() (sebelum anim baru)
        /// dan Stop() (kembalikan ke A-pose).
        /// </summary>
        private void ResetAllBonesToRest()
        {
            foreach (var kv in _restPoseCache)
            {
                if (_boneCache.TryGetValue(kv.Key, out var transform))
                {
                    transform.localRotation = kv.Value;
                }
            }
        }

        private async Task LoadAnim(string animName)
        {
            if (_loadedAnims.ContainsKey(animName)) return;

            string path = Path.Combine(Application.streamingAssetsPath, "Anim", animName + ".json");
            byte[] bytes;

#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[VmdPlayback] Load fail: {animName} — {req.error}");
                    return;
                }
                bytes = req.downloadHandler.data;
            }
#else
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[VmdPlayback] File not found: {path}");
                return;
            }
            bytes = File.ReadAllBytes(path);
#endif

            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            ParseAndStore(animName, jsonStr);
            await Task.Yield();
        }

        private void ParseAndStore(string animName, string jsonStr)
        {
            // Manual parse karena JsonUtility tidak support Dictionary
            // Pattern: {"name":"...","duration":N,"bones":{"<HumanBodyBones>":[{"frame":N,"t":[...],"r":[...]},...]}}
            try
            {
                var jObj = SimpleJson.Parse(jsonStr) as Dictionary<string, object>;
                if (jObj == null) { Debug.LogError($"[VmdPlayback] Parse '{animName}' returned non-object"); return; }
                float duration = (float)(double)jObj["duration"];
                var bonesObj = jObj["bones"] as Dictionary<string, object>;

                var animBones = new Dictionary<HumanBodyBones, List<Keyframe>>();
                foreach (var kv in bonesObj)
                {
                    if (!System.Enum.TryParse<HumanBodyBones>(kv.Key, out var humanBone)) continue;
                    var kfList = kv.Value as List<object>;
                    var frames = new List<Keyframe>();
                    foreach (var item in kfList)
                    {
                        var dict = item as Dictionary<string, object>;
                        var tArr = dict["t"] as List<object>;
                        var rArr = dict["r"] as List<object>;
                        frames.Add(new Keyframe
                        {
                            frame = (int)(double)dict["frame"],
                            t = new float[] { (float)(double)tArr[0], (float)(double)tArr[1], (float)(double)tArr[2] },
                            r = new float[] { (float)(double)rArr[0], (float)(double)rArr[1], (float)(double)rArr[2], (float)(double)rArr[3] }
                        });
                    }
                    animBones[humanBone] = frames;
                }

                _loadedAnims[animName] = animBones;
                _animDurations[animName] = duration;
                Debug.Log($"[VmdPlayback] Loaded '{animName}' — {animBones.Count} bones, duration {duration:F2}s");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VmdPlayback] Parse error '{animName}': {e.Message}");
            }
        }

        /// <summary>Public API: play VMD animation by name. Trigger via UnityBridge.</summary>
        public void Play(string animName)
        {
            if (!_loadedAnims.TryGetValue(animName, out var anim))
            {
                Debug.LogWarning($"[VmdPlayback] Anim not loaded: {animName}");
                return;
            }

            // FIX: reset SEMUA bone ke rest pose sebelum apply anim baru. Mencegah bone yang
            // dipakai animasi sebelumnya tetap stuck di rotasi terakhir (visual: tumpuk pose).
            ResetAllBonesToRest();

            _activeAnim = anim;
            _activeDuration = _animDurations[animName];
            activeAnimName = animName;
            playbackTime = 0f;
            isPlaying = true;
            // Matikan Animator (LiaAnimator) selama VMD main biar tak beradu.
            if (modelLoader != null && modelLoader.ModelAnimator != null)
                modelLoader.ModelAnimator.enabled = false;
            Debug.Log($"[VmdPlayback] Playing '{animName}' duration {_activeDuration:F2}s (reset {_restPoseCache.Count} bones)");
        }

        public void Stop()
        {
            bool was = isPlaying;
            isPlaying = false;
            _activeAnim = null;
            activeAnimName = "";
            // Nyalakan Animator lagi → balik idle
            if (was && modelLoader != null && modelLoader.ModelAnimator != null)
                modelLoader.ModelAnimator.enabled = true;

            // FIX: kembalikan semua bone ke rest pose (A-pose) supaya procedural state animation
            // bisa lanjut tanpa residue dari frame VMD terakhir.
            ResetAllBonesToRest();
            Debug.Log($"[VmdPlayback] Stopped, bones reset to rest pose");
        }

        private void LateUpdate()
        {
            if (!isPlaying || _activeAnim == null || !overrideBones) return;

            playbackTime += Time.deltaTime * playbackSpeed;
            if (playbackTime >= _activeDuration)
            {
                if (loop) playbackTime %= _activeDuration;
                else { Stop(); return; }
            }

            float frameNumber = playbackTime * 30f; // MMD = 30fps

            foreach (var kv in _activeAnim)
            {
                var bone = kv.Key;
                if (_skipSet.Contains(bone)) continue;
                if (!_boneCache.TryGetValue(bone, out var transform)) continue;

                var frames = kv.Value;
                if (frames.Count == 0) continue;

                // Find keyframe pair untuk interpolasi
                Quaternion rot = InterpolateRotation(frames, frameNumber);
                transform.localRotation = rot;
            }
        }

        private Quaternion InterpolateRotation(List<Keyframe> frames, float currentFrame)
        {
            // Edge cases
            if (frames.Count == 1)
            {
                var k = frames[0];
                return new Quaternion(k.r[0], k.r[1], k.r[2], k.r[3]);
            }
            if (currentFrame <= frames[0].frame)
            {
                var k = frames[0];
                return new Quaternion(k.r[0], k.r[1], k.r[2], k.r[3]);
            }
            if (currentFrame >= frames[frames.Count - 1].frame)
            {
                var k = frames[frames.Count - 1];
                return new Quaternion(k.r[0], k.r[1], k.r[2], k.r[3]);
            }

            // Binary search for surrounding keyframes
            int lo = 0, hi = frames.Count - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].frame <= currentFrame) lo = mid;
                else hi = mid;
            }

            var k1 = frames[lo];
            var k2 = frames[hi];
            float t = (currentFrame - k1.frame) / (float)(k2.frame - k1.frame);

            var q1 = new Quaternion(k1.r[0], k1.r[1], k1.r[2], k1.r[3]);
            var q2 = new Quaternion(k2.r[0], k2.r[1], k2.r[2], k2.r[3]);
            return Quaternion.Slerp(q1, q2, t);
        }

        public bool IsPlaying => isPlaying;
        public string ActiveAnimName => activeAnimName;
    }

    /// <summary>
    /// Simple JSON parser yang support Dictionary di runtime (Unity JsonUtility limitation workaround).
    /// Hanya support subset: object, array, number, string, bool, null.
    /// </summary>
    internal static class SimpleJson
    {
        public static object Parse(string json)
        {
            int idx = 0;
            return ParseValue(json, ref idx);
        }

        private static object ParseValue(string s, ref int idx)
        {
            SkipWhitespace(s, ref idx);
            if (idx >= s.Length) return null;
            char c = s[idx];
            if (c == '{') return ParseObject(s, ref idx);
            if (c == '[') return ParseArray(s, ref idx);
            if (c == '"') return ParseString(s, ref idx);
            if (c == 't' || c == 'f') return ParseBool(s, ref idx);
            if (c == 'n') { idx += 4; return null; }
            return ParseNumber(s, ref idx);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int idx)
        {
            var dict = new Dictionary<string, object>();
            idx++; // skip {
            SkipWhitespace(s, ref idx);
            if (s[idx] == '}') { idx++; return dict; }

            while (idx < s.Length)
            {
                SkipWhitespace(s, ref idx);
                string key = ParseString(s, ref idx);
                SkipWhitespace(s, ref idx);
                idx++; // skip :
                dict[key] = ParseValue(s, ref idx);
                SkipWhitespace(s, ref idx);
                if (s[idx] == ',') { idx++; continue; }
                if (s[idx] == '}') { idx++; break; }
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int idx)
        {
            var list = new List<object>();
            idx++; // skip [
            SkipWhitespace(s, ref idx);
            if (s[idx] == ']') { idx++; return list; }

            while (idx < s.Length)
            {
                list.Add(ParseValue(s, ref idx));
                SkipWhitespace(s, ref idx);
                if (s[idx] == ',') { idx++; continue; }
                if (s[idx] == ']') { idx++; break; }
            }
            return list;
        }

        private static string ParseString(string s, ref int idx)
        {
            idx++; // skip "
            int start = idx;
            while (idx < s.Length && s[idx] != '"')
            {
                if (s[idx] == '\\') idx++;
                idx++;
            }
            string result = s.Substring(start, idx - start);
            idx++; // skip closing "
            return result;
        }

        private static double ParseNumber(string s, ref int idx)
        {
            int start = idx;
            while (idx < s.Length && (char.IsDigit(s[idx]) || s[idx] == '-' || s[idx] == '.' || s[idx] == 'e' || s[idx] == 'E' || s[idx] == '+'))
                idx++;
            return double.Parse(s.Substring(start, idx - start), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string s, ref int idx)
        {
            if (s[idx] == 't') { idx += 4; return true; }
            idx += 5; return false;
        }

        private static void SkipWhitespace(string s, ref int idx)
        {
            while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
        }
    }
}
