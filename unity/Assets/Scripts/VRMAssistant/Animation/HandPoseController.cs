using System.Collections.Generic;
using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Hand pose library + per-finger Perlin micro-fidget overlay.
    ///
    /// VRM model default punya finger di T/A-pose (kaku). Controller ini bake 5 hand poses:
    /// - Relaxed: natural curl idle (default)
    /// - HoldingSkirt: tangan dekat pinggul, posisi feminine
    /// - NearFace: salah satu tangan dekat wajah (touching cheek/hair)
    /// - BehindBack: tangan terlipat di belakang
    /// - OpenGesture: telapak terbuka (saat speaking)
    ///
    /// Setiap pose disimpan sebagai array Quaternion[15] untuk 15 finger phalanges per tangan
    /// (3 phalanges × 5 fingers). Switch random pose tiap 8-20 detik dengan smooth fade 0.5s.
    /// </summary>
    public class HandPoseController : MonoBehaviour
    {
        public enum HandPose
        {
            Relaxed,
            HoldingSkirt,
            NearFace,
            BehindBack,
            OpenGesture
        }

        [Header("Pose Switching")]
        [SerializeField] private float minIntervalSec = 8f;
        [SerializeField] private float maxIntervalSec = 20f;
        [SerializeField] private float fadeDurationSec = 0.5f;

        [Header("Finger Fidget (Perlin)")]
        [SerializeField] private float fidgetAmplitudeDeg = 2.0f;
        [SerializeField] private float fidgetFrequency = 0.4f;

        [Header("Auto Switch")]
        [Tooltip("Saat true, random pose switching aktif. Set false untuk lock pose tertentu via SetPose().")]
        [SerializeField] private bool autoSwitchEnabled = true;

        // Bone arrays per hand (15 each)
        private Transform[] _leftFingers;
        private Transform[] _rightFingers;

        // Pose data — array of 15 quaternions per pose per hand
        private Dictionary<HandPose, Quaternion[]> _leftPoses;
        private Dictionary<HandPose, Quaternion[]> _rightPoses;

        // Current state
        private HandPose _currentPose = HandPose.Relaxed;
        private HandPose _previousPose = HandPose.Relaxed;
        private float _poseStartTime;
        private float _nextSwitchTime;
        private bool _isFading;

        private bool _initialized;

        public void Initialize(Animator animator)
        {
            if (animator == null) return;

            _leftFingers = new Transform[15];
            _rightFingers = new Transform[15];

            // Map HumanBodyBones finger enum ke arrays
            HumanBodyBones[] leftBones = {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal
            };
            HumanBodyBones[] rightBones = {
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
            };

            for (int i = 0; i < 15; i++)
            {
                _leftFingers[i] = animator.GetBoneTransform(leftBones[i]);
                _rightFingers[i] = animator.GetBoneTransform(rightBones[i]);
            }

            BuildPoseLibrary();

            _nextSwitchTime = Time.time + Random.Range(minIntervalSec, maxIntervalSec);
            _initialized = true;
        }

        // Skip saat playback penuh (VMD/VRMA/Clip) supaya tidak fight nulis finger bones
        private VmdPlaybackController _vmd;
        private VrmaPlaybackController _vrma;
        private ClipGestureController _clip;

        private void LateUpdate()
        {
            if (!_initialized) return;

            // Mutual exclusion dengan playback penuh (mocap/VMD/VRMA menulis finger sendiri)
            if (_vmd == null) _vmd = FindAnyObjectByType<VmdPlaybackController>();
            if (_vrma == null) _vrma = FindAnyObjectByType<VrmaPlaybackController>();
            if (_clip == null) _clip = FindAnyObjectByType<ClipGestureController>();
            if (_vmd != null && _vmd.IsPlaying) return;
            if (_vrma != null && _vrma.IsPlaying) return;
            if (_clip != null && _clip.IsPlaying) return;

            float now = Time.time;

            // Auto pose switching
            if (autoSwitchEnabled && !_isFading && now >= _nextSwitchTime)
            {
                SwitchToRandomPose();
            }

            // Compute fade weight
            float fadeT = _isFading
                ? Mathf.Clamp01((now - _poseStartTime) / fadeDurationSec)
                : 1f;

            if (_isFading && fadeT >= 1f) _isFading = false;

            ApplyPose(_leftFingers, _leftPoses, _previousPose, _currentPose, fadeT);
            ApplyPose(_rightFingers, _rightPoses, _previousPose, _currentPose, fadeT);
        }

        public void SetPose(HandPose pose, bool immediate = false)
        {
            if (pose == _currentPose) return;
            _previousPose = _currentPose;
            _currentPose = pose;
            _poseStartTime = Time.time;
            _isFading = !immediate;
        }

        public void SetAutoSwitch(bool enabled)
        {
            autoSwitchEnabled = enabled;
            if (enabled) _nextSwitchTime = Time.time + Random.Range(minIntervalSec, maxIntervalSec);
        }

        private void SwitchToRandomPose()
        {
            // Pilih pose berbeda dari current
            var values = (HandPose[])System.Enum.GetValues(typeof(HandPose));
            HandPose newPose;
            int safety = 0;
            do
            {
                newPose = values[Random.Range(0, values.Length)];
                safety++;
            } while (newPose == _currentPose && safety < 10);

            SetPose(newPose);
            _nextSwitchTime = Time.time + Random.Range(minIntervalSec, maxIntervalSec);
        }

        private void ApplyPose(Transform[] bones, Dictionary<HandPose, Quaternion[]> poses,
                               HandPose from, HandPose to, float fadeT)
        {
            if (bones == null || poses == null) return;
            if (!poses.TryGetValue(to, out var toRot)) return;

            float t = Time.time;
            poses.TryGetValue(from, out var fromRot);

            for (int i = 0; i < 15; i++)
            {
                if (bones[i] == null) continue;

                Quaternion target = (fromRot != null)
                    ? Quaternion.Slerp(fromRot[i], toRot[i], fadeT)
                    : toRot[i];

                // Per-finger Perlin micro-fidget — HANYA sumbu X (curl axis Kohaku).
                // Y/Z bikin jari twist/splay aneh (verifikasi empiris 2026-07-22).
                float phase = i * 0.7f;
                float pX = (Mathf.PerlinNoise(t * fidgetFrequency + phase, 0f) - 0.5f) * 2f;
                Quaternion fidget = Quaternion.Euler(pX * fidgetAmplitudeDeg, 0f, 0f);

                bones[i].localRotation = target * fidget;
            }
        }

        private void BuildPoseLibrary()
        {
            _leftPoses = new Dictionary<HandPose, Quaternion[]>();
            _rightPoses = new Dictionary<HandPose, Quaternion[]>();

            // Pose data: array of 15 quaternions [thumbP, thumbI, thumbD, indexP, indexI, indexD, ...]
            // Curl angles dalam degrees per phalanx — semua rotation di local Z (curl direction generic)

            // Relaxed: subtle curl semua jari ~15-25°
            _leftPoses[HandPose.Relaxed] = BuildCurlPose(thumb: 10f, index: 18f, middle: 22f, ring: 25f, little: 28f, isLeft: true);
            _rightPoses[HandPose.Relaxed] = BuildCurlPose(thumb: 10f, index: 18f, middle: 22f, ring: 25f, little: 28f, isLeft: false);

            // HoldingSkirt: ringer slightly more curl, thumb out
            _leftPoses[HandPose.HoldingSkirt] = BuildCurlPose(thumb: 15f, index: 30f, middle: 35f, ring: 38f, little: 40f, isLeft: true);
            _rightPoses[HandPose.HoldingSkirt] = BuildCurlPose(thumb: 15f, index: 30f, middle: 35f, ring: 38f, little: 40f, isLeft: false);

            // NearFace: index mengarah, finger lain curl
            _leftPoses[HandPose.NearFace] = BuildCurlPose(thumb: 5f, index: 10f, middle: 40f, ring: 45f, little: 50f, isLeft: true);
            _rightPoses[HandPose.NearFace] = BuildCurlPose(thumb: 5f, index: 10f, middle: 40f, ring: 45f, little: 50f, isLeft: false);

            // BehindBack: tangan rileks, agak curl
            _leftPoses[HandPose.BehindBack] = BuildCurlPose(thumb: 12f, index: 20f, middle: 25f, ring: 28f, little: 30f, isLeft: true);
            _rightPoses[HandPose.BehindBack] = BuildCurlPose(thumb: 12f, index: 20f, middle: 25f, ring: 28f, little: 30f, isLeft: false);

            // OpenGesture: telapak terbuka, semua jari minim curl
            _leftPoses[HandPose.OpenGesture] = BuildCurlPose(thumb: 0f, index: 5f, middle: 5f, ring: 8f, little: 10f, isLeft: true);
            _rightPoses[HandPose.OpenGesture] = BuildCurlPose(thumb: 0f, index: 5f, middle: 5f, ring: 8f, little: 10f, isLeft: false);
        }

        /// <summary>
        /// Build 15-Quaternion array untuk satu hand pose dengan curl angle per finger.
        /// Setiap finger punya 3 phalanx — total angle dibagi 3 (proximal, intermediate, distal).
        ///
        /// SUMBU diverifikasi EMPIRIS di Kohaku VRM 0.x (screenshot loop 2026-07-22):
        ///   - Jari (index..little): curl = +X, SAMA untuk kedua tangan (tidak mirror).
        ///     Z = splay (mekar ke samping) — JANGAN dipakai (sumber distorsi safe-mode lama).
        ///   - Thumb: pakai Y kecil (mirror kiri/kanan) — konservatif supaya tidak nembus telapak.
        /// </summary>
        private Quaternion[] BuildCurlPose(float thumb, float index, float middle, float ring, float little, bool isLeft)
        {
            float thumbSign = isLeft ? -1f : 1f;
            float[] curls = { thumb, index, middle, ring, little };

            var result = new Quaternion[15];
            for (int finger = 0; finger < 5; finger++)
            {
                float total = curls[finger];
                float perPhalanx = total / 3f;
                for (int phalanx = 0; phalanx < 3; phalanx++)
                {
                    int idx = finger * 3 + phalanx;
                    if (finger == 0)
                    {
                        // Thumb: curl kecil di Y (mirror per tangan)
                        result[idx] = Quaternion.Euler(0f, perPhalanx * thumbSign, 0f);
                    }
                    else
                    {
                        // Fingers: curl di +X untuk KEDUA tangan
                        result[idx] = Quaternion.Euler(perPhalanx, 0f, 0f);
                    }
                }
            }
            return result;
        }
    }
}
