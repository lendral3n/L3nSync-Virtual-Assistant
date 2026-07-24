using UnityEngine;
using VRM;
using VRMAssistant.Core;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// Expression controller — drive emotion blend shapes (Joy/Angry/Sorrow/Fun/Neutral).
    /// Smooth lerp ke target untuk transisi natural antar state.
    /// </summary>
    public class ExpressionController : MonoBehaviour
    {
        [Header("VRM Reference")]
        [SerializeField] private VRMBlendShapeProxy blendShapeProxy;

        [Header("Smoothing")]
        [SerializeField] private float lerpSpeed = 6f;

        // Current values (smoothed)
        private float _currentJoy, _currentAngry, _currentSorrow, _currentFun, _currentNeutral = 1f;

        // Target values (set by SetExpression)
        private float _targetJoy, _targetAngry, _targetSorrow, _targetFun, _targetNeutral = 1f;

        public void SetBlendShapeProxy(VRMBlendShapeProxy proxy)
        {
            blendShapeProxy = proxy;
        }

        /// <summary>Set ekspresi target. Value 0-1.</summary>
        public void SetExpression(BlendShapePreset preset, float value)
        {
            value = Mathf.Clamp01(value);
            switch (preset)
            {
                case BlendShapePreset.Joy: _targetJoy = value; break;
                case BlendShapePreset.Angry: _targetAngry = value; break;
                case BlendShapePreset.Sorrow: _targetSorrow = value; break;
                case BlendShapePreset.Fun: _targetFun = value; break;
                case BlendShapePreset.Neutral: _targetNeutral = value; break;
            }
        }

        /// <summary>Set ekspresi default berdasarkan state asisten.</summary>
        public void SetExpressionForState(AssistantState state)
        {
            // Reset semua dulu
            _targetJoy = 0f;
            _targetAngry = 0f;
            _targetSorrow = 0f;
            _targetFun = 0f;
            _targetNeutral = 0f;

            switch (state)
            {
                case AssistantState.Idle:
                    _targetNeutral = 1f;
                    break;
                case AssistantState.Active:
                    _targetFun = 0.4f;
                    _targetNeutral = 0.6f;
                    break;
                case AssistantState.Thinking:
                    _targetNeutral = 0.7f;
                    _targetSorrow = 0.2f; // sedikit melankolis (mode thinking)
                    break;
                case AssistantState.Listening:
                    _targetNeutral = 0.8f;
                    _targetFun = 0.2f;
                    break;
                case AssistantState.Speaking:
                    _targetJoy = 0.3f;
                    _targetNeutral = 0.7f;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (blendShapeProxy == null) return;

            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-lerpSpeed * dt);

            _currentJoy = Mathf.Lerp(_currentJoy, _targetJoy, k);
            _currentAngry = Mathf.Lerp(_currentAngry, _targetAngry, k);
            _currentSorrow = Mathf.Lerp(_currentSorrow, _targetSorrow, k);
            _currentFun = Mathf.Lerp(_currentFun, _targetFun, k);
            _currentNeutral = Mathf.Lerp(_currentNeutral, _targetNeutral, k);

            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy), _currentJoy);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Angry), _currentAngry);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Sorrow), _currentSorrow);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Fun), _currentFun);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Neutral), _currentNeutral);
        }
    }
}
