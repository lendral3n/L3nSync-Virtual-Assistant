using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// State Thinking — head tilt + slow breathing. Animator clip "Idle_Thinking" handle pose hand-near-chin.
    /// Procedural di sini hanya add subtle micro-movement on top.
    /// </summary>
    [System.Serializable]
    public class ThinkingAnimationState : IAnimationState
    {
        [Header("Thinking Parameters")]
        [SerializeField] private float headTiltZTarget = 6f;
        [SerializeField] private float smoothSpeed = 4.0f;

        private bool _initialized;
        private float _currentHeadZ;

        public AssistantState State => AssistantState.Thinking;

        public void Initialize(BoneReferences bones) => _initialized = bones.IsValid;

        public void OnEnter()
        {
            _currentHeadZ = 0f;
        }

        public void OnExit() { }

        public void Tick(float deltaTime, ref BoneOffsets offsets)
        {
            if (!_initialized) return;
            float t = Time.time;

            // Smooth lerp head tilt (subtle)
            _currentHeadZ = Mathf.Lerp(_currentHeadZ, headTiltZTarget, 1f - Mathf.Exp(-smoothSpeed * deltaTime));

            float headZ = _currentHeadZ + 0.6f * Mathf.Sin(2f * Mathf.PI * 0.4f * t);
            float headX = 0.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
            float headY = 1.5f * Mathf.Sin(2f * Mathf.PI * 0.3f * t + 0.7f);
            offsets.head = Quaternion.Euler(headX, headY, headZ);

            // Slow breathing additive
            float chestX = 1.8f * Mathf.Sin(2f * Mathf.PI * 0.20f * t);
            offsets.chest = Quaternion.Euler(chestX, 0, 0);
            offsets.spine = Quaternion.Euler(chestX * 0.3f, 0, 0);

            float hipsZ = 0.2f * Mathf.Sin(2f * Mathf.PI * 0.15f * t);
            offsets.hips = Quaternion.Euler(0, 0, hipsZ);
        }
    }
}
