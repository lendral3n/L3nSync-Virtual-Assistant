using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Bone offset yang diisi tiap Tick() oleh state animasi.
    /// Semua field default-nya Quaternion.identity (no offset).
    /// Orchestrator apply offset secara additive ke bone rotation hasil Animator clip,
    /// sehingga procedural breathing/sway TIDAK override Animator clip — hanya delta on top.
    /// </summary>
    public struct BoneOffsets
    {
        public Quaternion chest;
        public Quaternion spine;
        public Quaternion hips;
        public Quaternion head;
        public Quaternion neck;
        public Quaternion leftUpperArm;
        public Quaternion rightUpperArm;

        public static BoneOffsets Identity => new BoneOffsets
        {
            chest = Quaternion.identity,
            spine = Quaternion.identity,
            hips = Quaternion.identity,
            head = Quaternion.identity,
            neck = Quaternion.identity,
            leftUpperArm = Quaternion.identity,
            rightUpperArm = Quaternion.identity,
        };
    }

    /// <summary>
    /// Interface untuk satu state animasi (Idle, Active, Thinking, dst).
    /// Setiap state mengisi `BoneOffsets` dengan delta rotation (bukan absolute).
    /// Orchestrator yang apply offset ke bone setelah Animator clip di-evaluate.
    /// </summary>
    public interface IAnimationState
    {
        /// <summary>State enum yang ditangani implementasi ini.</summary>
        AssistantState State { get; }

        /// <summary>Inisialisasi state dengan bone references dari VRM model.</summary>
        void Initialize(BoneReferences bones);

        /// <summary>Dipanggil saat state ini menjadi aktif.</summary>
        void OnEnter();

        /// <summary>Dipanggil saat state ini berhenti aktif.</summary>
        void OnExit();

        /// <summary>
        /// Dipanggil tiap LateUpdate selama state aktif. Isi <paramref name="offsets"/>
        /// dengan delta Quaternion (relatif terhadap pose Animator). Identity = no offset.
        /// </summary>
        void Tick(float deltaTime, ref BoneOffsets offsets);
    }
}
