using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// State Listening — STT aktif. Animator clip "Idle_Listening" handle forward attentive pose.
    /// Procedural overlay: breathing + occasional nod (micro-nod via sin pulse).
    /// </summary>
    [System.Serializable]
    public class ListeningAnimationState : IAnimationState
    {
        [Header("Listening Parameters")]
        [SerializeField] private float leanTargetX = -3f;
        [SerializeField] private float smoothSpeed = 5.0f;
        [SerializeField] private float headTurnY = -2f;

        private bool _initialized;
        private float _currentLeanX;
        private float _lastNodTime;
        private float _nodInterval = 3f;

        public AssistantState State => AssistantState.Listening;

        public void Initialize(BoneReferences bones) => _initialized = bones.IsValid;

        public void OnEnter()
        {
            _currentLeanX = 0f;
            _lastNodTime = Time.time;
        }

        public void OnExit() { }

        public void Tick(float deltaTime, ref BoneOffsets offsets)
        {
            if (!_initialized) return;
            float t = Time.time;

            // Forward lean (additive)
            _currentLeanX = Mathf.Lerp(_currentLeanX, leanTargetX, 1f - Mathf.Exp(-smoothSpeed * deltaTime));
            float spineX = _currentLeanX + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.30f * t);
            offsets.spine = Quaternion.Euler(spineX, 0, 0);

            float chestX = _currentLeanX * 0.5f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.30f * t);
            offsets.chest = Quaternion.Euler(chestX, 0, 0);

            // Periodic nod
            if (t - _lastNodTime > _nodInterval)
            {
                _lastNodTime = t + Random.Range(2.5f, 5.0f);
            }

            float headX = 0f;
            float nodDuration = 0.35f;
            float nodProgress = (t - _lastNodTime) / nodDuration;
            if (nodProgress > 0f && nodProgress < 1.0f)
            {
                headX += Mathf.Sin(Mathf.PI * nodProgress) * 4f;
            }
            headX += 0.3f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);

            offsets.head = Quaternion.Euler(headX, headTurnY, 0.3f * Mathf.Sin(2f * Mathf.PI * 0.20f * t));
        }
    }
}
