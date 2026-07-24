using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// State Speaking — body bounce sync audio amplitude. Animator clip "Speaking" handle gesture loop.
    /// Procedural overlay: subtle body bounce + head ekspresif scale ke amplitude.
    /// </summary>
    [System.Serializable]
    public class SpeakingAnimationState : IAnimationState
    {
        [Header("Speaking Link")]
        public LipSyncController lipSyncController;

        private bool _initialized;

        public AssistantState State => AssistantState.Speaking;

        public void Initialize(BoneReferences bones) => _initialized = bones.IsValid;

        public void OnEnter() { }
        public void OnExit() { }

        public void Tick(float deltaTime, ref BoneOffsets offsets)
        {
            if (!_initialized) return;

            float t = Time.time;
            float ampNorm = (lipSyncController != null) ? lipSyncController.CurrentAmplitudeNorm : 0f;

            // Subtle body bounce sync amplitude
            float chestX = 1.5f * Mathf.Sin(2f * Mathf.PI * 0.32f * t);
            float speakBounce = 0.6f * Mathf.Sin(2f * Mathf.PI * 2.0f * t) * ampNorm;
            offsets.chest = Quaternion.Euler(chestX + speakBounce * 0.5f, 0, 0);

            float spineX = -2f + 0.6f * Mathf.Sin(2f * Mathf.PI * 0.30f * t);
            offsets.spine = Quaternion.Euler(spineX, 0, 0);

            offsets.hips = Quaternion.Euler(speakBounce * 0.3f, 0, 0);

            // Head ekspresif scale ke amplitudo
            float ampFactor = Mathf.Clamp(ampNorm * 2.0f, 0.3f, 1.0f);
            float headX = 1.2f * Mathf.Sin(2f * Mathf.PI * 0.35f * t) * ampFactor;
            float headY = 2.0f * Mathf.Sin(2f * Mathf.PI * 0.28f * t + 1.5f) * ampFactor;
            float headZ = 0.6f * Mathf.Sin(2f * Mathf.PI * 0.22f * t + 0.8f);
            offsets.head = Quaternion.Euler(headX, headY, headZ);
        }
    }
}
