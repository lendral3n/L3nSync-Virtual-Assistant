using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// State Active — alert + engaged. Procedural overlay = breathing lebih cepat + slight forward lean.
    /// Gesture pool (wave, peace, peek, dll) di-handle oleh Animator triggers via BehaviorScheduler,
    /// BUKAN di sini.
    /// </summary>
    [System.Serializable]
    public class ActiveAnimationState : IAnimationState
    {
        [Header("Active Parameters")]
        [SerializeField] private float breathFrequency = 0.32f;
        [SerializeField] private float breathAmplitude = 2.0f;
        [SerializeField] private float spineLeanForward = -2.5f;

        private bool _initialized;

        public AssistantState State => AssistantState.Active;

        public void Initialize(BoneReferences bones)
        {
            _initialized = bones.IsValid;
        }

        public void OnEnter() { }
        public void OnExit() { }

        public void Tick(float deltaTime, ref BoneOffsets offsets)
        {
            if (!_initialized) return;
            float t = Time.time;

            // Breathing lebih cepat + amplitude lebih besar
            float chestX = breathAmplitude * Mathf.Sin(2f * Mathf.PI * breathFrequency * t);
            offsets.chest = Quaternion.Euler(chestX, 0, 0);

            // Slight forward lean (additive to base pose)
            float spineX = spineLeanForward + 0.7f * Mathf.Sin(2f * Mathf.PI * breathFrequency * t);
            offsets.spine = Quaternion.Euler(spineX, 0, 0);

            // Hip micro sway
            float hipsZ = 0.4f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
            offsets.hips = Quaternion.Euler(0, 0, hipsZ);

            // Head: lebih ekspresif tapi tetap subtle
            float headX = 0.8f * Mathf.Sin(2f * Mathf.PI * breathFrequency * t);
            float headY = 1.5f * Mathf.Sin(2f * Mathf.PI * 0.22f * t + 1.0f);
            float headZ = 0.5f * Mathf.Sin(2f * Mathf.PI * 0.25f * t + 0.5f);
            offsets.head = Quaternion.Euler(headX, headY, headZ);
        }
    }
}
