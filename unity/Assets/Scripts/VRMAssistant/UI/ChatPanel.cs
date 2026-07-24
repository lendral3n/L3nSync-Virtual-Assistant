using UnityEngine;
using VRMAssistant.AI;

namespace VRMAssistant.UI
{
    /// <summary>
    /// Panel chat Lia (IMGUI) untuk Mac: ketik pesan → Lia jawab (Gemini) + bicara (ElevenLabs).
    /// Tombol 💬 (kiri-atas, di sebelah ⚙). Ada bagian "Setelan" untuk isi API key
    /// (disimpan PlayerPrefs app-private — TIDAK di-hardcode/commit). Tekan ENTER untuk kirim.
    ///
    /// Auto-attach + bikin LiaBrain via RuntimeInitializeOnLoadMethod.
    /// </summary>
    public class ChatPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("LiaChat");
            go.AddComponent<LiaBrain>();
            go.AddComponent<ChatPanel>();
            DontDestroyOnLoad(go);
        }

        private LiaBrain _brain;
        private bool _open;
        private bool _showSettings;
        private string _input = "";
        private string _status = "";
        private string _lastReply = "";
        private string _geminiKey = "";
        private string _elevenKey = "";
        private Vector2 _scroll;
        private GUIStyle _hdr, _bubble;
        private bool _styles;

        // Voice recording
        private bool _recording;
        private string _micDevice;
        private AudioClip _recClip;
        private float _recStart;
        private const int RecMaxSec = 15;
        private const int RecRate = 16000;   // cukup untuk speech, hemat data

        private void StartRec()
        {
            if (Microphone.devices.Length == 0) { _status = "Tidak ada mikrofon terdeteksi."; return; }
            _micDevice = Microphone.devices[0];
            _recClip = Microphone.Start(_micDevice, false, RecMaxSec, RecRate);
            _recording = true;
            _recStart = Time.time;
            _status = "";
        }

        private void StopRecAndSend()
        {
            _recording = false;
            int pos = Microphone.GetPosition(_micDevice);
            Microphone.End(_micDevice);
            if (_recClip == null || pos <= 0) { _status = "Rekaman kosong."; return; }

            // Trim clip ke panjang aktual yang direkam.
            var full = new float[_recClip.samples * _recClip.channels];
            _recClip.GetData(full, 0);
            int n = Mathf.Min(pos * _recClip.channels, full.Length);
            var trimmed = AudioClip.Create("mic", pos, _recClip.channels, _recClip.frequency, false);
            var buf = new float[n];
            System.Array.Copy(full, buf, n);
            trimmed.SetData(buf, 0);

            _status = "Lia mendengar…";
            if (_brain != null) _brain.AskVoice(trimmed);
        }

        private void Start()
        {
            _brain = GetComponent<LiaBrain>() ?? FindAnyObjectByType<LiaBrain>();
            _geminiKey = LiaBrain.GeminiKey;
            _elevenKey = LiaBrain.ElevenKey;
            if (_brain != null)
            {
                _brain.OnReplyText += s => { _lastReply = s; _status = ""; };
                _brain.OnError += s => { _status = s; };
            }
            _showSettings = string.IsNullOrEmpty(_geminiKey); // buka setelan kalau key kosong
        }

        private void EnsureStyles()
        {
            if (_styles) return;
            _hdr = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _bubble = new GUIStyle(GUI.skin.box) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
            _styles = true;
        }

        private void OnGUI()
        {
            // Di mode overlay transparan: karakter bersih saja, chat/config hanya di app normal.
            if (VRMAssistant.Behavior.LiaInput.OverlayActive) return;
            EnsureStyles();

            if (GUI.Button(new Rect(116, 12, 96, 30), _open ? "✕ Chat" : "💬 Chat"))
                _open = !_open;
            if (!_open) return;

            float w = 360f, h = 300f, x = 116f, y = 52f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUILayout.BeginArea(new Rect(x + 10, y + 8, w - 20, h - 16));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Ngobrol dengan Lia", _hdr);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_showSettings ? "Chat" : "⚙ API", GUILayout.Width(70))) _showSettings = !_showSettings;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_showSettings)
            {
                bool ready = !string.IsNullOrEmpty(LiaBrain.GeminiKey);
                GUILayout.Label(ready ? "Siap ngobrol ✓" : "Belum ada Gemini key", _hdr);
                GUILayout.Space(2);
                GUILayout.Label("Isi API key di file:");
                GUILayout.Label("lia_ai.env (folder yang sama dgn LiaVA.app),", _bubble);
                GUILayout.Label("lalu restart app. File terbaca: " + LiaBrain.EnvPathUsed, _bubble);
                GUILayout.Space(4);
                GUILayout.Label("Atau isi manual di sini (opsional):");
                var g = GUILayout.PasswordField(_geminiKey ?? "", '•');
                if (g != _geminiKey) { _geminiKey = g; PlayerPrefs.SetString(LiaBrain.PrefGeminiKey, g); PlayerPrefs.Save(); }
                GUILayout.Label("ElevenLabs (opsional, suara):");
                var e = GUILayout.PasswordField(_elevenKey ?? "", '•');
                if (e != _elevenKey) { _elevenKey = e; PlayerPrefs.SetString(LiaBrain.PrefElevenKey, e); PlayerPrefs.Save(); }
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(h - 110));
                if (!string.IsNullOrEmpty(_lastReply)) GUILayout.Label("Lia: " + _lastReply, _bubble);
                if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, _bubble);
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                GUI.SetNextControlName("chatInput");
                _input = GUILayout.TextField(_input ?? "", GUILayout.Height(26));
                bool enter = Event.current.type == EventType.KeyDown &&
                             (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                             GUI.GetNameOfFocusedControl() == "chatInput";
                bool send = GUILayout.Button("Kirim", GUILayout.Width(64), GUILayout.Height(26));
                GUILayout.EndHorizontal();

                // Tombol SUARA: klik untuk mulai rekam, klik lagi untuk kirim ke Lia.
                GUI.color = _recording ? new Color(1f, 0.5f, 0.5f) : Color.white;
                if (GUILayout.Button(_recording ? "⏹ Kirim suara (rekam…)" : "🎤 Bicara ke Lia", GUILayout.Height(30)))
                {
                    if (!_recording) StartRec(); else StopRecAndSend();
                }
                GUI.color = Color.white;

                if ((send || enter) && !string.IsNullOrWhiteSpace(_input) && _brain != null && !_brain.IsBusy)
                {
                    _status = "Lia mikir…";
                    _brain.Ask(_input);
                    _input = "";
                }
                if (_recording) GUILayout.Label($"● Merekam {(Time.time - _recStart):F0}s — klik untuk kirim");
                else if (_brain != null && _brain.IsBusy) GUILayout.Label("Lia sedang menjawab…");
            }

            GUILayout.EndArea();
        }
    }
}
