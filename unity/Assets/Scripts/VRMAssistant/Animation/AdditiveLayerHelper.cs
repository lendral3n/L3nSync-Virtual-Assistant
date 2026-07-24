using System.Collections.Generic;
using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Apply BoneOffsets sebagai additive on top of REST pose (bukan akumulasi rotation tiap frame).
    ///
    /// FIX: Pola sebelumnya `bone.localRotation = bone.localRotation * offset` mengakumulasi rotasi
    /// per frame karena tidak ada Animator yang reset pose ke rest. Setelah beberapa detik,
    /// rotation accumulate ke nilai gila → bone twisted parah.
    ///
    /// Pola benar: cache rest pose saat Initialize, setiap frame:
    ///   bone.localRotation = restRotation * offset  (TIDAK mul current)
    /// </summary>
    public static class AdditiveLayerHelper
    {
        private static Dictionary<Transform, Quaternion> _restPoseCache = new Dictionary<Transform, Quaternion>();

        /// <summary>Snapshot rest pose dari bone references — dipanggil setelah model loaded.</summary>
        public static void CacheRestPose(in BoneReferences bones)
        {
            _restPoseCache.Clear();
            CacheBone(bones.chest);
            CacheBone(bones.spine);
            CacheBone(bones.hips);
            CacheBone(bones.head);
            CacheBone(bones.neck);
            CacheBone(bones.leftUpperArm);
            CacheBone(bones.rightUpperArm);
        }

        private static void CacheBone(Transform t)
        {
            if (t == null) return;
            _restPoseCache[t] = t.localRotation;
        }

        /// <summary>Apply offsets ke bones = restRotation * offset (BUKAN akumulasi).</summary>
        public static void ApplyAdditive(in BoneReferences bones, in BoneOffsets offsets)
        {
            ApplyBone(bones.chest, offsets.chest);
            ApplyBone(bones.spine, offsets.spine);
            ApplyBone(bones.hips, offsets.hips);
            ApplyBone(bones.head, offsets.head);
            ApplyBone(bones.neck, offsets.neck);
            ApplyBone(bones.leftUpperArm, offsets.leftUpperArm);
            ApplyBone(bones.rightUpperArm, offsets.rightUpperArm);
        }

        private static void ApplyBone(Transform t, Quaternion offset)
        {
            if (t == null) return;
            if (_restPoseCache.TryGetValue(t, out var rest))
            {
                t.localRotation = rest * offset;
            }
            else
            {
                // Fallback kalau rest pose belum di-cache
                t.localRotation = offset;
            }
        }
    }
}
