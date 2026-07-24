#if BVH_BROWSER
using System.Collections.Generic;
using UnityEngine;

namespace BvhBrowser
{
    /// <summary>
    /// Retarget BVH → VRM Kohaku (world-rotation absolut) dengan KALIBRASI ke arah tulang
    /// Kohaku sendiri. Robust untuk bind-pose BVH aneh (rest = I-pose rebah).
    ///
    /// Kalibrasi (sekali): pose skeleton BVH agar tiap tulang meng-"aim" ke arah yang SAMA
    /// dengan tulang Kohaku menuju anaknya (jadi bentuknya = rest Kohaku). Simpan
    ///   rOffset = inv(calibWorld_bvh) * restWorld_kohaku.
    /// Tiap frame: kBoneWorld = bvhCurWorld * rOffset (set world, parent-first). Karena
    /// calib menyamakan orientasi, pose absolut ikut benar (lengan turun tetap turun).
    /// </summary>
    public class BvhRetargeter
    {
        private const float SCALE = 0.01f;

        private static readonly Dictionary<string, HumanBodyBones> BoneMap = new Dictionary<string, HumanBodyBones>
        {
            { "Hips", HumanBodyBones.Hips }, { "Spine", HumanBodyBones.Spine }, { "Chest", HumanBodyBones.Chest },
            { "Neck", HumanBodyBones.Neck }, { "Head", HumanBodyBones.Head },
            { "Shoulder_L", HumanBodyBones.LeftShoulder }, { "UpperArm_L", HumanBodyBones.LeftUpperArm },
            { "LowerArm_L", HumanBodyBones.LeftLowerArm }, { "Hand_L", HumanBodyBones.LeftHand },
            { "Shoulder_R", HumanBodyBones.RightShoulder }, { "UpperArm_R", HumanBodyBones.RightUpperArm },
            { "LowerArm_R", HumanBodyBones.RightLowerArm }, { "Hand_R", HumanBodyBones.RightHand },
            { "UpperLeg_L", HumanBodyBones.LeftUpperLeg }, { "LowerLeg_L", HumanBodyBones.LeftLowerLeg },
            { "Foot_L", HumanBodyBones.LeftFoot }, { "Toes_L", HumanBodyBones.LeftToes },
            { "UpperLeg_R", HumanBodyBones.RightUpperLeg }, { "LowerLeg_R", HumanBodyBones.RightLowerLeg },
            { "Foot_R", HumanBodyBones.RightFoot }, { "Toes_R", HumanBodyBones.RightToes },
        };

        // Anak "utama" tiap tulang (arah aim = ke anak ini).
        private static readonly Dictionary<string, string> PrimaryChild = new Dictionary<string, string>
        {
            { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" }, { "Neck", "Head" },
            { "Shoulder_L", "UpperArm_L" }, { "UpperArm_L", "LowerArm_L" }, { "LowerArm_L", "Hand_L" },
            { "Shoulder_R", "UpperArm_R" }, { "UpperArm_R", "LowerArm_R" }, { "LowerArm_R", "Hand_R" },
            { "UpperLeg_L", "LowerLeg_L" }, { "LowerLeg_L", "Foot_L" }, { "Foot_L", "Toes_L" },
            { "UpperLeg_R", "LowerLeg_R" }, { "LowerLeg_R", "Foot_R" }, { "Foot_R", "Toes_R" },
        };

        private class Map { public int bvhIndex; public Transform kBone; public Quaternion rOffset; public Quaternion kRestLocal; }

        private BvhClip _clip;
        private Transform _srcRoot;
        private Transform[] _bones;
        private readonly List<Map> _maps = new List<Map>();
        private Transform _hips, _legL, _legR;
        private Vector3 _hipsRestPos;
        private Quaternion _rootYawFix = Quaternion.identity;
        private Quaternion[] _qbuf;
        public bool Ready { get; private set; }

        public bool Setup(BvhClip clip, Animator target)
        {
            Dispose();
            _clip = clip;
            if (clip == null || target == null || target.avatar == null) return false;

            // 1) skeleton BVH (rest offset)
            _srcRoot = new GameObject("BvhSource").transform;
            _srcRoot.position = new Vector3(5000, 5000, 5000);
            _bones = new Transform[clip.joints.Count];
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < clip.joints.Count; i++)
            {
                var j = clip.joints[i];
                var t = new GameObject("b" + i).transform;
                t.SetParent(j.parent >= 0 ? _bones[j.parent] : _srcRoot, false);
                t.localPosition = j.offset * SCALE;
                t.localRotation = Quaternion.identity;
                _bones[i] = t;
                if (!indexByName.ContainsKey(j.name)) indexByName[j.name] = i;
            }
            Physics.SyncTransforms();

            // 2) KALIBRASI: arahkan tiap tulang source ke arah tulang Kohaku (aim ke anak utama)
            for (int i = 0; i < clip.joints.Count; i++)
            {
                var name = clip.joints[i].name;
                if (!BoneMap.TryGetValue(name, out var hbb)) continue;
                if (!PrimaryChild.TryGetValue(name, out var childName)) continue;
                if (!indexByName.TryGetValue(childName, out var ci)) continue;

                var kBone = target.GetBoneTransform(hbb);
                var kChild = BoneMap.TryGetValue(childName, out var chbb) ? target.GetBoneTransform(chbb) : null;
                if (kBone == null || kChild == null) continue;

                Vector3 localChildDir = _bones[ci].localPosition.normalized;   // arah anak di frame lokal tulang
                Vector3 kAim = (kChild.position - kBone.position).normalized;  // arah tulang Kohaku
                if (localChildDir.sqrMagnitude < 1e-8f || kAim.sqrMagnitude < 1e-8f) continue;
                _bones[i].rotation = Quaternion.FromToRotation(localChildDir, kAim); // set world aim
            }
            Physics.SyncTransforms();

            // 3) rekam rOffset per tulang termap
            _maps.Clear();
            for (int i = 0; i < clip.joints.Count; i++)
            {
                if (!BoneMap.TryGetValue(clip.joints[i].name, out var hbb)) continue;
                var kBone = target.GetBoneTransform(hbb);
                if (kBone == null) continue;
                _maps.Add(new Map
                {
                    bvhIndex = i,
                    kBone = kBone,
                    rOffset = Quaternion.Inverse(_bones[i].rotation) * kBone.rotation,
                });
            }

            _hips = target.GetBoneTransform(HumanBodyBones.Hips);
            if (_hips != null) _hipsRestPos = _hips.position;
            _legL = target.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _legR = target.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            // simpan rest LOCAL Kohaku (buat RestoreRest saat kembali ke list)
            foreach (var m in _maps) m.kRestLocal = m.kBone.localRotation;

            Ready = _maps.Count > 0;

            // AUTO-HADAP DEPAN: trial apply frame 0 (yaw=identity), ukur arah hadap dari garis
            // pinggul, lalu rootYawFix agar karakter menghadap -Z (ke kamera default). Restore.
            if (Ready)
            {
                _rootYawFix = Quaternion.identity;
                Apply(0);
                if (_legL != null && _legR != null)
                {
                    Vector3 hipLine = (_legR.position - _legL.position);
                    hipLine.y = 0f;
                    if (hipLine.sqrMagnitude > 1e-6f)
                    {
                        Vector3 fwd = Vector3.Cross(hipLine.normalized, Vector3.up); // arah hadap (perkiraan)
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude > 1e-6f)
                            _rootYawFix = Quaternion.FromToRotation(fwd.normalized, Vector3.back);
                    }
                }
                RestoreRest();
            }
            return Ready;
        }

        /// <summary>Kembalikan Kohaku ke rest pose (dipakai saat tak ada clip / list view).</summary>
        public void RestoreRest()
        {
            foreach (var m in _maps) if (m.kBone != null) m.kBone.localRotation = m.kRestLocal;
            if (_hips != null) _hips.position = _hipsRestPos;
        }

        public void Apply(int frame)
        {
            if (!Ready) return;
            if (_qbuf == null || _qbuf.Length != _clip.joints.Count) _qbuf = new Quaternion[_clip.joints.Count];

            _clip.EvaluateLocalRotations(frame, _qbuf, out _);
            for (int i = 0; i < _bones.Length; i++)
            {
                var q = _qbuf[i];
                if (IsValid(q)) _bones[i].localRotation = q;
            }
            Physics.SyncTransforms();

            foreach (var m in _maps)               // parent-first (urutan deklarasi BVH)
            {
                Quaternion w = _rootYawFix * (_bones[m.bvhIndex].rotation * m.rOffset);
                if (IsValid(w)) m.kBone.rotation = w;   // guard NaN → cegah crash SpringBone/PhysX
            }

            if (_hips != null) _hips.position = _hipsRestPos;
        }

        private static bool IsValid(Quaternion q)
        {
            float s = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return !float.IsNaN(s) && !float.IsInfinity(s) && s > 1e-6f;
        }

        public void Dispose()
        {
            Ready = false;
            _maps.Clear();
            if (_srcRoot != null) { Object.Destroy(_srcRoot.gameObject); _srcRoot = null; }
            _bones = null; _clip = null;
        }
    }
}
#endif
