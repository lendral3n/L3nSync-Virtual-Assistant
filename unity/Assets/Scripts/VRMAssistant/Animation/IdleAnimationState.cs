using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// State Idle — karakter hidup saat diam (breathing, sway, head turn).
    /// Procedural offset additive on top of REST pose.
    /// </summary>
    [System.Serializable]
    public class IdleAnimationState : IAnimationState
    {
        [Header("Idle Parameters (DRAMATIC amplitude untuk visibility test)")]
        [SerializeField] private float breathAmplitude = 12f;
        [SerializeField] private float breathFrequency = 0.25f;
        [SerializeField] private float swayAmplitudeZ = 6f;
        private const float HeadTurnAmplitude = 18f;

        private bool _initialized;

        public AssistantState State => AssistantState.Idle;

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

            // Breathing — chest expansion forward-back (X axis)
            float chestRotX = breathAmplitude * Mathf.Sin(2f * Mathf.PI * breathFrequency * t);
            offsets.chest = Quaternion.Euler(chestRotX, 0, 0);
            offsets.spine = Quaternion.Euler(chestRotX * 0.4f, 0, 0);

            // Body sway (hips Z roll) — side weight shift
            float swayZ = swayAmplitudeZ * Mathf.Sin(2f * Mathf.PI * 0.18f * t);
            float swayX = 1.5f * Mathf.Sin(2f * Mathf.PI * 0.18f * t);
            offsets.hips = Quaternion.Euler(swayX, 0, swayZ);

            // Head turn left-right + small pitch + tilt
            float headX = 4f * Mathf.Sin(2f * Mathf.PI * 0.22f * t + 0.5f);
            float headY = HeadTurnAmplitude * Mathf.Sin(2f * Mathf.PI * 0.15f * t + 1.8f);
            float headZ = 3f * Mathf.Sin(2f * Mathf.PI * 0.19f * t + 0.9f);
            offsets.head = Quaternion.Euler(headX, headY, headZ);
            offsets.neck = Quaternion.Euler(headX * 0.5f, headY * 0.4f, headZ * 0.3f);
        }
    }
}
