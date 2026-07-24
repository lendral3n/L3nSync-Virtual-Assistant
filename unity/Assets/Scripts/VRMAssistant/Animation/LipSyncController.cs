using UnityEngine;
using VRM;

namespace VRMAssistant.Animation
{
    /// <summary>
    /// LipSync controller — analisa audio FFT spectrum lalu drive blendshape A/I/U/O.
    /// Refactor dari LipSyncAnimation.cs sebagai standalone MonoBehaviour.
    /// Expose CurrentAmplitudeNorm untuk dibaca SpeakingAnimationState.
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        [Header("Komponen Audio & VRM")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private VRMBlendShapeProxy blendShapeProxy;

        [Header("LipSync Parameters")]
        [SerializeField] private float gainFactor = 150.0f;
        [SerializeField] private float smoothSpeedOpen = 20.0f;
        [SerializeField] private float smoothSpeedClose = 14.0f;
        [SerializeField] private float minMouthOpen = 5.0f;
        [SerializeField] private float amplitudeThreshold = 0.08f;

        [Header("Runtime Control")]
        [Tooltip("Set false untuk hentikan lip sync (e.g., saat state bukan Speaking)")]
        public bool active = false;

        [Tooltip("Saat active tapi tidak ada audio playing, gerakkan mulut dengan pola sintetis " +
                 "(Perlin noise). Dipakai untuk test chip LipSync + state Speaking sebelum TTS ada.")]
        [SerializeField] private bool mouthDriveWithoutAudio = true;

        private float[] _spectrumData = new float[256];
        private float _currentA, _currentI, _currentU, _currentO;

        /// <summary>Amplitudo audio ter-normalize 0-1, dibaca SpeakingAnimationState.</summary>
        public float CurrentAmplitudeNorm { get; private set; }

        /// <summary>Set audio source secara runtime (e.g., dari VRMModelLoader hook).</summary>
        public void SetAudioSource(AudioSource source)
        {
            audioSource = source;
        }

        /// <summary>Set VRM blendshape proxy secara runtime (dari OnModelLoaded).</summary>
        public void SetBlendShapeProxy(VRMBlendShapeProxy proxy)
        {
            blendShapeProxy = proxy;
        }

        private void LateUpdate()
        {
            bool hasAudio = audioSource != null && audioSource.isPlaying;
            bool useSynthetic = mouthDriveWithoutAudio && !hasAudio;

            if (!active || blendShapeProxy == null || (!hasAudio && !useSynthetic))
            {
                // Smooth release saat tidak aktif
                if (_currentA > 0.01f || _currentI > 0.01f || _currentU > 0.01f || _currentO > 0.01f)
                {
                    float dt = Time.deltaTime;
                    _currentA = InterpolateMouth(_currentA, 0f, dt);
                    _currentI = InterpolateMouth(_currentI, 0f, dt);
                    _currentU = InterpolateMouth(_currentU, 0f, dt);
                    _currentO = InterpolateMouth(_currentO, 0f, dt);
                    if (blendShapeProxy != null) ApplyBlendShapes();
                }
                CurrentAmplitudeNorm = 0f;
                return;
            }

            if (hasAudio)
            {
                // 5.1 FFT spectrum 0-2.6kHz (16 bins)
                audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

                float amplitudeSum = 0f;
                for (int i = 0; i < 16; i++) amplitudeSum += _spectrumData[i];
                float amplitude = amplitudeSum / 16f;
                CurrentAmplitudeNorm = Mathf.Clamp01(amplitude * gainFactor);
            }
            else
            {
                // Pola bicara sintetis — Perlin noise bergelombang dengan jeda natural,
                // rescale supaya sesekali turun di bawah threshold (mulut menutup sejenak).
                float n = Mathf.PerlinNoise(Time.time * 2.2f, 0.37f);
                CurrentAmplitudeNorm = Mathf.Clamp01((n - 0.25f) * 1.4f);
            }

            // 5.2 Mapping phase ke target blendshapes
            float targetA = 0, targetI = 0, targetU = 0, targetO = 0;
            if (CurrentAmplitudeNorm > amplitudeThreshold)
            {
                float phase = Time.time * 8.0f;
                int idx = (int)phase % 4;
                switch (idx)
                {
                    case 0: targetA = CurrentAmplitudeNorm * 100f; break;
                    case 1: targetI = CurrentAmplitudeNorm * 80f; targetA = CurrentAmplitudeNorm * 20f; break;
                    case 2: targetU = CurrentAmplitudeNorm * 85f; targetA = CurrentAmplitudeNorm * 15f; break;
                    case 3: targetO = CurrentAmplitudeNorm * 70f; targetA = CurrentAmplitudeNorm * 30f; break;
                }
            }

            float deltaTime = Time.deltaTime;
            float minA = (CurrentAmplitudeNorm > amplitudeThreshold) ? minMouthOpen : 0f;
            _currentA = InterpolateMouth(_currentA, Mathf.Max(targetA, minA), deltaTime);
            _currentI = InterpolateMouth(_currentI, targetI, deltaTime);
            _currentU = InterpolateMouth(_currentU, targetU, deltaTime);
            _currentO = InterpolateMouth(_currentO, targetO, deltaTime);

            ApplyBlendShapes();
        }

        private void ApplyBlendShapes()
        {
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.A), _currentA / 100f);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.I), _currentI / 100f);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.U), _currentU / 100f);
            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.O), _currentO / 100f);
        }

        private float InterpolateMouth(float current, float target, float dt)
        {
            float speed = (target > current) ? smoothSpeedOpen : smoothSpeedClose;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));
        }
    }
}
