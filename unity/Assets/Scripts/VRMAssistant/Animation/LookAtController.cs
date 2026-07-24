using UnityEngine;
using VRM;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// LookAt controller — wrapper untuk VRMLookAtHead dengan SACCADE pattern.
    /// Saccade = realistic eye movement: snap to new target dalam &lt;80ms, hold 0.3-1.2s, lalu pick target baru.
    /// Lebih natural dari smooth lerp (yang terasa robotic).
    ///
    /// Mode:
    /// - Track: follow target transform terus
    /// - Wander (saccade): jump randomly setiap 2-6 detik, occasional dramatic look-around
    /// </summary>
    public class LookAtController : MonoBehaviour
    {
        [Header("VRM Reference")]
        [SerializeField] private VRMLookAtHead lookAtHead;

        [Header("Target")]
        [Tooltip("Transform yang akan di-look. Biasanya kamera atau LookAtTarget GameObject.")]
        [SerializeField] private Transform target;

        [Header("Saccade Wander Mode")]
        [Tooltip("Saat true, eye saccade pattern aktif (Idle/Thinking).")]
        [SerializeField] private bool wanderEnabled = false;
        [SerializeField] private float wanderRadius = 0.4f;
        [SerializeField] private Vector3 wanderCenter = new Vector3(0f, 1.4f, 1.5f);

        [Header("Saccade Timing")]
        [SerializeField] private float saccadeMinHold = 0.3f;
        [SerializeField] private float saccadeMaxHold = 1.2f;
        [SerializeField] private float saccadeDuration = 0.06f;  // <80ms snap
        [SerializeField] private float dramaticChance = 0.05f;   // 5% dramatic look-around
        [SerializeField] private float dramaticRadiusMultiplier = 2.5f;

        public bool active = true;

        // Saccade state
        private Vector3 _currentLookPoint;
        private Vector3 _targetLookPoint;
        private float _saccadeStartTime;
        private float _nextSaccadeTime;
        private bool _saccading;

        public void SetLookAtHead(VRMLookAtHead head)
        {
            lookAtHead = head;
            if (lookAtHead != null) lookAtHead.Target = target;
        }

        public void SetTarget(Transform t)
        {
            target = t;
            if (lookAtHead != null) lookAtHead.Target = t;
        }

        public void SetModeForState(AssistantState state)
        {
            switch (state)
            {
                case AssistantState.Speaking:
                case AssistantState.Listening:
                case AssistantState.Active:
                    wanderEnabled = false;
                    break;
                case AssistantState.Idle:
                case AssistantState.Thinking:
                    wanderEnabled = true;
                    PickNextSaccadeTarget(); // initialize saccade
                    break;
            }
        }

        private void LateUpdate()
        {
            if (!active || lookAtHead == null) return;

            if (wanderEnabled)
            {
                TickSaccade();
            }
            else
            {
                if (target != null && lookAtHead.Target != target)
                {
                    lookAtHead.Target = target;
                }
            }
        }

        private void TickSaccade()
        {
            float now = Time.time;

            // Trigger saccade baru kalau hold time selesai
            if (!_saccading && now >= _nextSaccadeTime)
            {
                PickNextSaccadeTarget();
            }

            // Saat saccading, lerp dari current ke target dalam saccadeDuration
            if (_saccading)
            {
                float elapsed = now - _saccadeStartTime;
                float k = Mathf.Clamp01(elapsed / saccadeDuration);
                // Smoothstep untuk easing — lebih natural dari linear
                float eased = k * k * (3f - 2f * k);
                _currentLookPoint = Vector3.Lerp(_currentLookPoint, _targetLookPoint, eased);

                if (k >= 1f)
                {
                    _currentLookPoint = _targetLookPoint;
                    _saccading = false;
                    _nextSaccadeTime = now + Random.Range(saccadeMinHold, saccadeMaxHold);
                }
            }

            // Apply look point — Target = null + LookWorldPosition langsung
            lookAtHead.Target = null;
            lookAtHead.LookWorldPosition(_currentLookPoint, out _, out _);
        }

        private void PickNextSaccadeTarget()
        {
            // 5% chance dramatic look-around (radius lebih besar)
            float radius = Random.value < dramaticChance
                ? wanderRadius * dramaticRadiusMultiplier
                : wanderRadius;

            float offX = Random.Range(-radius, radius);
            float offY = Random.Range(-radius * 0.6f, radius * 0.6f); // vertical sedikit terbatas

            Vector3 basePos = (target != null) ? target.position : wanderCenter;
            _targetLookPoint = basePos + new Vector3(offX, offY, 0f);
            _saccadeStartTime = Time.time;
            _saccading = true;
        }
    }
}
