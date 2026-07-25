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
        private Vector3 _restFwd = Vector3.forward;      // arah hadap Kohaku saat rest (bersih)
        private Vector3 _hipsLatLocal = Vector3.right;   // sumbu lateral hips (local) — pitch-invariant
        private Quaternion _kHipsRestRot = Quaternion.identity;
        private Map _hipsMap;                            // map tulang Hips (untuk kunci-hadap)
        private Quaternion _clipYawFix = Quaternion.identity;  // koreksi hadap TETAP per clip
        private int _srcLegL = -1, _srcLegR = -1;        // index kaki di JOINT clip (FK murni)
        private Quaternion _lastYawFix = Quaternion.identity;  // smoothing antar frame
        private Vector3[] _fkPos;                        // buffer FK murni utk ukur heading
        private Transform _kShoulderL, _kShoulderR;      // bahu Kohaku (ukur heading hasil akhir)
        private int _dbgCount;                           // debug yaw (sementara)
        private Quaternion[] _qbuf;
        // TRUE REST Kohaku — direkam SEKALI saat pertama (pose T bersih). Setup clip ke-2+
        // WAJIB restore ini dulu; tanpa itu kalibrasi memakai pose clip sebelumnya → seluruh
        // retarget miring (badan serong, muka beda arah). Ini akar bug "hanya clip pertama benar".
        private readonly Dictionary<Transform, Quaternion> _trueRest = new Dictionary<Transform, Quaternion>();
        private Vector3 _trueHipsPos;
        private bool _trueRestCaptured;
        public bool Ready { get; private set; }

        public bool Setup(BvhClip clip, Animator target)
        {
            Dispose();
            _clip = clip;
            if (clip == null || target == null || target.avatar == null) return false;

            // Rekam/restore TRUE REST sebelum apa pun mengukur pose Kohaku.
            var hipsT = target.GetBoneTransform(HumanBodyBones.Hips);
            if (!_trueRestCaptured)
            {
                foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone) continue;
                    var t0 = target.GetBoneTransform(b);
                    if (t0 != null) _trueRest[t0] = t0.localRotation;
                }
                if (hipsT != null) _trueHipsPos = hipsT.position;
                _trueRestCaptured = true;
            }
            else
            {
                foreach (var kv in _trueRest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
                if (hipsT != null) hipsT.position = _trueHipsPos;
                Physics.SyncTransforms();
            }

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
                t.localPosition = j.offset * SCALE;   // apa adanya (sinkron parser, tanpa mirror)
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
            _kShoulderL = target.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _kShoulderR = target.GetBoneTransform(HumanBodyBones.RightUpperArm);

            // simpan rest LOCAL Kohaku (buat RestoreRest saat kembali ke list)
            foreach (var m in _maps) m.kRestLocal = m.kBone.localRotation;

            // Data untuk KUNCI-HADAP per-frame: rest-forward BERSIH (garis kaki simetris saat
            // rest) + rotasi hips rest + referensi map hips. Facing dihitung tiap frame di Apply
            // supaya karakter SELALU menghadap kamera walau mocap-nya memutar badan.
            _restFwd = Vector3.forward;
            Vector3 restLat = Vector3.right;
            if (_legL != null && _legR != null)
            {
                Vector3 lat = _legR.position - _legL.position; lat.y = 0f;
                if (lat.sqrMagnitude > 1e-6f)
                {
                    restLat = lat.normalized;
                    _restFwd = Vector3.Cross(restLat, Vector3.up).normalized;
                }
            }
            _kHipsRestRot = _hips != null ? _hips.rotation : Quaternion.identity;
            // lateral dalam frame lokal hips → dipakai per-frame (tahan pitch/nunduk)
            _hipsLatLocal = Quaternion.Inverse(_kHipsRestRot) * restLat;
            _hipsMap = null;
            foreach (var m in _maps) if (clip.joints[m.bvhIndex].name == "Hips") { _hipsMap = m; break; }

            Ready = _maps.Count > 0;

            // Simpan index kaki source (heading dihitung per-frame di Apply dari garis kaki).
            if (!indexByName.TryGetValue("UpperLeg_L", out _srcLegL)) _srcLegL = -1;
            if (!indexByName.TryGetValue("UpperLeg_R", out _srcLegR)) _srcLegR = -1;
            _clipYawFix = Quaternion.identity;
            _lastYawFix = Quaternion.identity;

            // Pre-warm yawFix: pose frame 0 mentah ke Kohaku, ukur bahu, set langsung
            // (tanpa smoothing) → frame pertama sudah menghadap benar. Lalu pulihkan rest.
            if (Ready && clip.FrameCount > 0 && _kShoulderL != null && _kShoulderR != null)
            {
                var q0 = new Quaternion[clip.joints.Count];
                clip.EvaluateLocalRotations(0, q0, out _);
                for (int i = 0; i < _bones.Length; i++) if (IsValid(q0[i])) _bones[i].localRotation = q0[i];
                Physics.SyncTransforms();
                foreach (var m in _maps)
                {
                    Quaternion w = _bones[m.bvhIndex].rotation * m.rOffset;
                    if (IsValid(w)) m.kBone.rotation = w;
                }
                Vector3 lat = _kShoulderR.position - _kShoulderL.position; lat.y = 0f;
                if (lat.sqrMagnitude > 1e-8f)
                {
                    Vector3 fwd = Vector3.Cross(Vector3.up, lat.normalized);
                    if (fwd.sqrMagnitude > 1e-8f)
                    {
                        float rawYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                        _lastYawFix = Quaternion.Euler(0f, -rawYaw + YawOffsetDeg, 0f);
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

            // KUNCI-HADAP per-frame: ukur arah hadap dari hips (raw, sebelum yawFix), lalu putar
            // seluruh tulang → karakter selalu menghadap kamera (canonical -Z), pose tetap utuh.
            // KUNCI-HADAP per-frame dari GEOMETRI (garis kaki source = posisi joint, bukan
            // orientasi hips yang terkontaminasi rig rebah Bandai). Lateral kaki tetap valid
            // saat membungkuk/miring → heading stabil; di-smooth antar frame (anti-jitter).
            // PASS 1: terapkan pose mentah (tanpa koreksi hadap) ke Kohaku.
            foreach (var m in _maps)               // parent-first (urutan deklarasi BVH)
            {
                Quaternion w = _bones[m.bvhIndex].rotation * m.rOffset;
                if (IsValid(w)) m.kBone.rotation = w;
            }

            // Ukur heading dari BAHU KOHAKU HASIL AKHIR (geometri nyata yang tampil — bukan
            // source FK, karena kalibrasi aim menyisakan twist arbitrer antara keduanya).
            // Garis bahu tetap lateral saat bungkuk/jalan → stabil. Lalu PASS 2: netralkan.
            // Lateral gabungan BAHU + PANGGUL (counter-rotation saat jalan saling meniadakan;
            // saat bungkuk keduanya sejajar) + smoothing berat → heading tenang & akurat.
            Quaternion yawFix = _lastYawFix;
            Vector3 latSum = Vector3.zero;
            if (_kShoulderL != null && _kShoulderR != null)
            {
                Vector3 ls = _kShoulderR.position - _kShoulderL.position; ls.y = 0f;
                if (ls.sqrMagnitude > 1e-8f) latSum += ls.normalized;
            }
            if (_legL != null && _legR != null)
            {
                Vector3 lp = _legR.position - _legL.position; lp.y = 0f;
                if (lp.sqrMagnitude > 1e-8f) latSum += lp.normalized;
            }
            if (latSum.sqrMagnitude > 1e-6f)
            {
                Vector3 fwd = Vector3.Cross(Vector3.up, latSum.normalized);
                if (fwd.sqrMagnitude > 1e-8f)
                {
                    float rawYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                    Quaternion target = Quaternion.Euler(0f, -rawYaw + YawOffsetDeg, 0f);
                    yawFix = Quaternion.Slerp(_lastYawFix, target, 0.35f);
                }
            }
            _lastYawFix = yawFix;

            // PASS 2: putar seluruh pose dengan yawFix (rigid) → menghadap kamera.
            foreach (var m in _maps)
            {
                Quaternion w = yawFix * (_bones[m.bvhIndex].rotation * m.rOffset);
                if (IsValid(w)) m.kBone.rotation = w;   // guard NaN → cegah crash SpringBone/PhysX
            }

            if (_hips != null) _hips.position = _hipsRestPos;
        }

        // Offset yaw tambahan (derajat) — bisa dituning via file bvh_yaw_offset.txt di sebelah
        // .app TANPA rebuild (dibaca sekali per launch). Default final di-hardcode setelah
        // diverifikasi. Positif = putar searah jarum jam dilihat dari atas.
        private static float? _yawOffsetCache;
        private static float YawOffsetDeg
        {
            get
            {
                if (_yawOffsetCache.HasValue) return _yawOffsetCache.Value;
                // 180 = koreksi pseudovector: mirror-X (RH→LH) membalik hasil cross-product
                // (heading kaki) tapi TIDAK membalik arah gerak → selisih 180° dikompensasi
                // di sini. Diverifikasi numerik (legsYaw vs bowDirYaw) + visual (bow ke kamera).
                float v = 180f;
                try
                {
                    var d = new System.IO.DirectoryInfo(Application.dataPath);
                    while (d != null && !d.Name.EndsWith(".app")) d = d.Parent;
                    string p = d?.Parent != null ? System.IO.Path.Combine(d.Parent.FullName, "bvh_yaw_offset.txt") : null;
                    if (p != null && System.IO.File.Exists(p))
                        float.TryParse(System.IO.File.ReadAllText(p).Trim(), out v);
                }
                catch { }
                _yawOffsetCache = v;
                return v;
            }
        }

        /// <summary>Yaw murni (derajat) di sumbu Y via swing-twist — tahan pitch & roll.</summary>
        private static float YawDeg(Quaternion q)
        {
            if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
            float yaw = 2f * Mathf.Atan2(q.y, q.w) * Mathf.Rad2Deg;
            if (yaw > 180f) yaw -= 360f; else if (yaw < -180f) yaw += 360f;
            return yaw;
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
