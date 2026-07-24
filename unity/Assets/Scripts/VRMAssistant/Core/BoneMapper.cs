using UnityEngine;

namespace VRMAssistant.Core
{
    /// <summary>
    /// Struct yang menyimpan referensi semua bone yang diperlukan animasi.
    /// Diisi otomatis dari Animator humanoid via BoneMapper.Resolve().
    /// </summary>
    [System.Serializable]
    public struct BoneReferences
    {
        public Transform hips;
        public Transform spine;
        public Transform chest;
        public Transform neck;
        public Transform head;
        public Transform leftUpperArm;
        public Transform rightUpperArm;
        public Transform leftLowerArm;
        public Transform rightLowerArm;

        /// <summary>Apakah semua bone utama (hips, spine, chest, head) sudah terisi?</summary>
        public bool IsValid =>
            hips != null && spine != null && chest != null && head != null;
    }

    /// <summary>
    /// Utility untuk resolve bone references dari Animator humanoid.
    /// Mendukung VRM model yang di-load runtime maupun yang sudah ada di scene.
    /// </summary>
    public static class BoneMapper
    {
        /// <summary>
        /// Resolve semua bone dari Animator component.
        /// Animator harus punya Avatar humanoid yang valid.
        /// </summary>
        public static BoneReferences Resolve(Animator animator)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("[BoneMapper] Animator null atau bukan humanoid!");
                return default;
            }

            return new BoneReferences
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips),
                spine = animator.GetBoneTransform(HumanBodyBones.Spine),
                chest = animator.GetBoneTransform(HumanBodyBones.Chest),
                neck = animator.GetBoneTransform(HumanBodyBones.Neck),
                head = animator.GetBoneTransform(HumanBodyBones.Head),
                leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
            };
        }
    }
}
