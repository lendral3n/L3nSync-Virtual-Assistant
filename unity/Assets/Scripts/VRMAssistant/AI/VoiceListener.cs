using System.Collections.Generic;
using UnityEngine;

namespace VRMAssistant.AI
{
    /// <summary>
    /// Wake-word listening: mic nyala terus, deteksi suara (VAD/RMS), saat user selesai
    /// ngomong → kirim audio ke Gemini mode WAKE (Gemini hanya jawab kalau ditujukan ke
    /// "Lia"). Jalan di overlay & app (mic tak perlu window focus).
    ///
    /// Anti-feedback: berhenti menangkap saat Lia sedang mikir/bicara (IsBusy) supaya
    /// suara TTS-nya sendiri tidak ikut terekam. Toggle: pref liava_voice (default 1).
    /// </summary>
    public class VoiceListener : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("VoiceListener");
            go.AddComponent<VoiceListener>();
            DontDestroyOnLoad(go);
        }

        const int RATE = 16000;
        const int LOOP_SEC = 10;
        static float THRESH = 0.06f;      // ambang suara (RMS) — tunable: liava_vad
        const float SILENCE_HOLD = 0.9f;  // diam sekian detik → ucapan selesai
        const float MIN_UTTER = 1.0f;     // minimal durasi ucapan (skip fragmen pendek)
        const float MAX_UTTER = 9f;       // maksimal
        const float RESUME_DELAY = 0.4f;  // jeda setelah Lia selesai bicara
        const float SEND_COOLDOWN = 3f;   // jeda antar-kirim ke Gemini (hemat kuota / anti-429)
        private float _peakRms;           // debug: RMS tertinggi selama utterance
        private float _nextSend;          // waktu boleh kirim berikutnya

        private LiaBrain _brain;
        private string _mic;
        private AudioClip _loop;
        private int _lastPos;
        private bool _active;

        private readonly List<float> _utter = new List<float>();
        private bool _listening;
        private float _startT, _lastVoiceT, _resumeAt;

        /// <summary>True saat sedang menangkap ucapan user (untuk indikator "mendengar").</summary>
        public bool IsHearing => _active && _listening;
        public bool Active => _active;

        private void Start()
        {
            _brain = FindAnyObjectByType<LiaBrain>();
            if (PlayerPrefs.GetInt("liava_voice", 1) != 1) { Debug.Log("[VoiceListener] dimatikan (liava_voice=0)"); return; }
            if (Microphone.devices.Length == 0) { Debug.LogWarning("[VoiceListener] tak ada mikrofon"); return; }
            THRESH = PlayerPrefs.GetFloat("liava_vad", 0.06f);
            _mic = Microphone.devices[0];
            _loop = Microphone.Start(_mic, true, LOOP_SEC, RATE);
            _lastPos = 0;
            _active = true;
            Debug.Log($"[VoiceListener] mendengar via '{_mic}' (VAD thresh={THRESH:F3}, sebut \"Lia\")");
        }

        private void OnDestroy()
        {
            if (_active && _mic != null) Microphone.End(_mic);
        }

        private void Update()
        {
            if (!_active || _loop == null) return;
            if (_brain == null) { _brain = FindAnyObjectByType<LiaBrain>(); return; }

            // Anti-feedback: saat Lia mikir/bicara, jangan tangkap (buang buffer).
            if (_brain.IsBusy)
            {
                _listening = false; _utter.Clear();
                _lastPos = Microphone.GetPosition(_mic);
                _resumeAt = Time.time + RESUME_DELAY;
                return;
            }
            if (Time.time < _resumeAt) { _lastPos = Microphone.GetPosition(_mic); return; }

            float[] chunk = ReadNew();
            if (chunk == null || chunk.Length == 0) return;
            ProcessChunk(chunk);
        }

        private float[] ReadNew()
        {
            int pos = Microphone.GetPosition(_mic);
            int len = _loop.samples;
            if (pos == _lastPos || len == 0) return null;
            int count = (pos - _lastPos + len) % len;
            if (count <= 0 || count > len) { _lastPos = pos; return null; }

            var buf = new float[count];
            if (_lastPos + count <= len)
            {
                _loop.GetData(buf, _lastPos);
            }
            else
            {
                int first = len - _lastPos;
                var b1 = new float[first]; _loop.GetData(b1, _lastPos);
                var b2 = new float[count - first]; _loop.GetData(b2, 0);
                System.Array.Copy(b1, 0, buf, 0, first);
                System.Array.Copy(b2, 0, buf, first, count - first);
            }
            _lastPos = pos;
            return buf;
        }

        private void ProcessChunk(float[] buf)
        {
            double sum = 0;
            for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
            float rms = Mathf.Sqrt((float)(sum / buf.Length));
            bool voiced = rms > THRESH;
            float now = Time.time;

            if (!_listening)
            {
                if (voiced)
                {
                    _listening = true;
                    _utter.Clear();
                    _utter.AddRange(buf);
                    _startT = now; _lastVoiceT = now; _peakRms = rms;
                }
                return;
            }

            _utter.AddRange(buf);
            if (rms > _peakRms) _peakRms = rms;
            if (voiced) _lastVoiceT = now;
            float dur = now - _startT;

            if (now - _lastVoiceT > SILENCE_HOLD || dur > MAX_UTTER)
            {
                _listening = false;
                bool longEnough = dur >= MIN_UTTER && _utter.Count > RATE / 3;
                bool cooledDown = now >= _nextSend && now >= LiaBrain.RateLimitedUntil;
                if (longEnough && cooledDown)
                {
                    _nextSend = now + SEND_COOLDOWN;   // hemat kuota Gemini (anti-429)
                    var clip = AudioClip.Create("utter", _utter.Count, 1, RATE, false);
                    clip.SetData(_utter.ToArray(), 0);
                    Debug.Log($"[VoiceListener] ucapan {dur:F1}s (peakRMS={_peakRms:F3}) → cek 'Lia'…");
                    if (_brain != null) _brain.AskVoice(clip, true);   // wake filter
                }
                _utter.Clear();
            }
        }
    }
}
