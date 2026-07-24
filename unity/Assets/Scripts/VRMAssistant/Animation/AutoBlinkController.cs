using UnityEngine;
using VRM;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Auto-blink controller — kedipan random tiap 2-6 detik via BlendShapePreset.Blink.
    /// Durasi blink ~150ms (close 50ms, hold 30ms, open 70ms) untuk kesan natural.
    /// </summary>
    public class AutoBlinkController : MonoBehaviour
    {
        [Header("Blink Timing")]
        [SerializeField] private float minInterval = 2.0f;
        [SerializeField] private float maxInterval = 6.0f;
        [SerializeField] private float closeDuration = 0.05f;
        [SerializeField] private float holdDuration = 0.03f;
        [SerializeField] private float openDuration = 0.07f;

        [Header("VRM Reference")]
        [SerializeField] private VRMBlendShapeProxy blendShapeProxy;

        private float _nextBlinkTime;
        private float _blinkStartTime;
        private bool _isBlinking;

        public bool active = true;

        public void SetBlendShapeProxy(VRMBlendShapeProxy proxy)
        {
            blendShapeProxy = proxy;
        }

        private void Start()
        {
            ScheduleNextBlink();
        }

        private void LateUpdate()
        {
            if (!active || blendShapeProxy == null) return;

            float t = Time.time;

            if (_isBlinking)
            {
                float elapsed = t - _blinkStartTime;
                float totalDuration = closeDuration + holdDuration + openDuration;
                float blinkValue = ComputeBlinkValue(elapsed);

                blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), blinkValue);

                if (elapsed >= totalDuration)
                {
                    _isBlinking = false;
                    blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), 0f);
                    ScheduleNextBlink();
                }
            }
            else if (t >= _nextBlinkTime)
            {
                _isBlinking = true;
                _blinkStartTime = t;
            }
        }

        /// <summary>Compute blink amount [0..1] dari elapsed time.</summary>
        private float ComputeBlinkValue(float elapsed)
        {
            if (elapsed < closeDuration)
            {
                // Closing: lerp 0 → 1
                return Mathf.Clamp01(elapsed / closeDuration);
            }
            else if (elapsed < closeDuration + holdDuration)
            {
                // Hold closed
                return 1f;
            }
            else
            {
                // Opening: lerp 1 → 0
                float openElapsed = elapsed - closeDuration - holdDuration;
                return Mathf.Clamp01(1f - openElapsed / openDuration);
            }
        }

        private void ScheduleNextBlink()
        {
            _nextBlinkTime = Time.time + Random.Range(minInterval, maxInterval);
        }
    }
}
