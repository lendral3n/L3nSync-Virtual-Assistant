using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using VRM;

namespace VRMAssistant.AI
{
    /// <summary>
    /// Otak AI Lia untuk Mac/C# (port dari LiaBrain.kt Android): teks user → Gemini
    /// (gemini-flash-latest, output JSON {say,emotion,gesture}) → ekspresi + gesture +
    /// TTS ElevenLabs (PCM→AudioClip) → CommandReceiver (Speaking + lipsync).
    ///
    /// API key TIDAK di-hardcode / tidak di-commit: dibaca dari PlayerPrefs (diisi user
    /// di ChatPanel). Kalau kosong → chat/TTS di-skip dengan pesan.
    /// </summary>
    public class LiaBrain : MonoBehaviour
    {
        public const string PrefGeminiKey = "liava_gemini_key";
        public const string PrefElevenKey = "liava_eleven_key";
        private const string DefaultModel = "gemini-flash-latest";
        // Model bisa diganti dari lia_ai.env (GEMINI_MODEL=...) tanpa rebuild — misal
        // pindah ke model lain untuk kuota harian fresh, atau saat ganti key/project.
        private static string GeminiModel { get { EnsureEnv(); return string.IsNullOrEmpty(_envModel) ? DefaultModel : _envModel; } }
        private const string ElevenVoice = "B8gJV1IhpuegLxdpXFOE"; // kuon (anime cute)

        public bool IsBusy { get; private set; }
        public string LastReply { get; private set; } = "";
        // Diset saat kena HTTP 429 (rate limit) → VoiceListener berhenti kirim sementara.
        public static float RateLimitedUntil { get; private set; }
        public event Action<string> OnReplyText;      // untuk UI
        public event Action<string> OnError;

        private CommandReceiver _receiver;
        private readonly List<GContent> _history = new List<GContent>();

        // Key diprioritaskan dari file lia_ai.env (diisi user), fallback PlayerPrefs.
        public static string GeminiKey
        {
            get { EnsureEnv(); return !string.IsNullOrEmpty(_envGemini) ? _envGemini : PlayerPrefs.GetString(PrefGeminiKey, ""); }
        }
        public static string ElevenKey
        {
            get { EnsureEnv(); return !string.IsNullOrEmpty(_envEleven) ? _envEleven : PlayerPrefs.GetString(PrefElevenKey, ""); }
        }

        private static bool _envLoaded;
        private static string _envGemini = "";
        private static string _envEleven = "";
        private static string _envModel = "";
        // Backend: "gemini" (default) atau "ollama" (self-host gpt-oss:120b di VM Elara + STT).
        private static string _envBackend = "";
        private static string _envOllamaUrl = "";
        private static string _envOllamaModel = "";
        private static string _envSttUrl = "";
        public static string EnvPathUsed { get; private set; } = "(tidak ada)";

        /// <summary>true kalau pakai Ollama self-host (bukan Gemini).</summary>
        public static bool UseOllama { get { EnsureEnv(); return _envBackend.Trim().ToLowerInvariant() == "ollama"; } }
        private static string OllamaUrl { get { EnsureEnv(); return string.IsNullOrEmpty(_envOllamaUrl) ? "" : _envOllamaUrl.TrimEnd('/'); } }
        private static string OllamaModel { get { EnsureEnv(); return string.IsNullOrEmpty(_envOllamaModel) ? "gpt-oss:120b" : _envOllamaModel; } }
        private static string SttUrl { get { EnsureEnv(); return string.IsNullOrEmpty(_envSttUrl) ? "" : _envSttUrl.TrimEnd('/'); } }

        /// <summary>Muat GEMINI_API_KEY / ELEVENLABS_API_KEY dari lia_ai.env (sekali).</summary>
        private static void EnsureEnv()
        {
            if (_envLoaded) return;
            _envLoaded = true;
            foreach (var path in EnvCandidatePaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        var k = line.Substring(0, eq).Trim();
                        var v = line.Substring(eq + 1).Trim().Trim('"', '\'');
                        if (k == "GEMINI_API_KEY") _envGemini = v;
                        else if (k == "ELEVENLABS_API_KEY") _envEleven = v;
                        else if (k == "GEMINI_MODEL") _envModel = v;
                        else if (k == "LLM_BACKEND") _envBackend = v;
                        else if (k == "OLLAMA_URL") _envOllamaUrl = v;
                        else if (k == "OLLAMA_MODEL") _envOllamaModel = v;
                        else if (k == "STT_URL") _envSttUrl = v;
                    }
                    EnvPathUsed = path;
                    string backendInfo = _envBackend.Trim().ToLowerInvariant() == "ollama"
                        ? $"ollama({(_envOllamaModel.Length>0 ? _envOllamaModel : "gpt-oss:120b")} @ {(_envOllamaUrl.Length>0 ? _envOllamaUrl : "?")}, stt={(_envSttUrl.Length>0 ? "ada" : "KOSONG")})"
                        : $"gemini({(_envModel.Length>0 ? _envModel : "default")})";
                    Debug.Log($"[LiaBrain] env dibaca: {path} | backend={backendInfo} | gemini={( _envGemini.Length>0 ? "ada" : "kosong")} | eleven={(_envEleven.Length>0 ? "ada" : "kosong")}");
                    break;
                }
                catch (Exception e) { Debug.LogWarning("[LiaBrain] baca env gagal: " + e.Message); }
            }
        }

        private static IEnumerable<string> EnvCandidatePaths()
        {
            // 1) folder yang berisi LiaVA.app (macOS standalone) — paling gampang diedit user
            string appDir = null;
            try
            {
                var d = new DirectoryInfo(Application.dataPath);
                while (d != null && !d.Name.EndsWith(".app")) d = d.Parent;
                if (d?.Parent != null) appDir = d.Parent.FullName;
            }
            catch { }
            if (!string.IsNullOrEmpty(appDir)) yield return Path.Combine(appDir, "lia_ai.env");
            // 2) persistentDataPath (persist antar rebuild)
            yield return Path.Combine(Application.persistentDataPath, "lia_ai.env");
            // 3) StreamingAssets (kalau di-bake sebelum build)
            yield return Path.Combine(Application.streamingAssetsPath, "lia_ai.env");
        }

        private void Awake()
        {
            _receiver = FindAnyObjectByType<CommandReceiver>();
        }

        /// <summary>Kirim pesan user. Mulai pipeline async (coroutine).</summary>
        public void Ask(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText) || IsBusy) return;
            if (UseOllama)
            {
                if (string.IsNullOrEmpty(OllamaUrl)) { OnError?.Invoke("OLLAMA_URL belum diisi di lia_ai.env."); return; }
            }
            else if (string.IsNullOrEmpty(GeminiKey))
            {
                OnError?.Invoke("Gemini API key belum diisi (buka panel Chat → Setelan).");
                return;
            }
            StartCoroutine(AskRoutine(userText.Trim()));
        }

        private IEnumerator AskRoutine(string userText)
        {
            IsBusy = true;
            if (_receiver == null) _receiver = FindAnyObjectByType<CommandReceiver>();

            _history.Add(new GContent { role = "user", parts = new[] { new GPart { text = userText } } });
            TrimHistory();

            var res = new LlmResult();
            yield return StartCoroutine(LlmChat(res));

            if (res.err != null || string.IsNullOrEmpty(res.reply))
            {
                Debug.LogWarning("[LiaBrain] " + (res.err ?? "balasan kosong"));
                OnError?.Invoke(res.err ?? "Lia tidak menjawab, coba lagi.");
                IsBusy = false;
                yield break;
            }

            yield return StartCoroutine(HandleReply(res.reply));
            IsBusy = false;
        }

        private class LlmResult { public string reply; public string err; }

        /// <summary>Panggil LLM (Gemini atau Ollama) dengan _history + system prompt. Hasil di res.reply / res.err.</summary>
        private IEnumerator LlmChat(LlmResult res)
        {
            if (UseOllama) yield return StartCoroutine(OllamaChat(res));
            else           yield return StartCoroutine(GeminiChat(res));
        }

        /// <summary>Persona + konteks waktu nyata (biar "hari ini hari apa" dijawab benar).</summary>
        private static string BuildSystemPrompt()
        {
            var now = System.DateTime.Now;
            string[] hari = { "Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" };
            string[] bulan = { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
            string tgl = $"{hari[(int)now.DayOfWeek]}, {now.Day} {bulan[now.Month]} {now.Year}, pukul {now:HH:mm}";
            return LiaPersona.SystemPrompt + $"\n\n(Konteks waktu nyata — pakai HANYA bila ditanya: sekarang {tgl}.)";
        }

        private IEnumerator GeminiChat(LlmResult res)
        {
            var req = new GReq
            {
                systemInstruction = new GSysInstr { parts = new[] { new GPart { text = BuildSystemPrompt() } } },
                contents = _history.ToArray(),
                generationConfig = new GGenConfig { responseMimeType = "application/json" },
            };
            string body = JsonUtility.ToJson(req);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={GeminiKey}";
            using (var www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    res.err = $"Gemini error: {www.responseCode} {www.error}";
                    if (www.responseCode == 429) { RateLimitedUntil = Time.time + 45f; res.err = "Kuota Gemini penuh (429)."; }
                }
                else res.reply = ExtractGeminiText(www.downloadHandler.text);
            }
        }

        /// <summary>Ollama /api/chat (gpt-oss:120b di VM Elara), output dipaksa JSON persona.</summary>
        private IEnumerator OllamaChat(LlmResult res)
        {
            // Susun messages: system + history (map role Gemini→Ollama: model→assistant).
            var sb = new StringBuilder();
            sb.Append("{\"model\":").Append(JsonEscape(OllamaModel));
            sb.Append(",\"stream\":false,\"format\":\"json\"");
            // think=low: gpt-oss model reasoning — reasoning rendah = jauh lebih cepat (~4.6s vs ~8-15s),
            // jawaban tetap oke. keep_alive: model tetap di RAM biar tak reload tiap panggilan.
            sb.Append(",\"think\":\"low\",\"keep_alive\":\"30m\"");
            sb.Append(",\"options\":{\"temperature\":0.7,\"num_predict\":200}");
            sb.Append(",\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":").Append(JsonEscape(BuildSystemPrompt())).Append('}');
            foreach (var c in _history)
            {
                if (c.parts == null || c.parts.Length == 0) continue;
                string role = c.role == "model" ? "assistant" : "user";
                sb.Append(",{\"role\":\"").Append(role).Append("\",\"content\":").Append(JsonEscape(c.parts[0].text)).Append('}');
            }
            sb.Append("]}");
            string body = sb.ToString();

            string url = OllamaUrl + "/api/chat";
            using (var www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = 180;   // gpt-oss:120b CPU bisa lama
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                    res.err = $"Ollama error: {www.responseCode} {www.error}";
                else
                    res.reply = ExtractOllamaText(www.downloadHandler.text);
            }
        }

        /// <summary>Handler balasan Gemini (parse JSON persona → ekspresi + gesture + TTS). Dipakai teks & suara.</summary>
        private IEnumerator HandleReply(string replyText)
        {
            LiaReply reply = ParsePersonaJson(replyText);
            if (reply == null || string.IsNullOrEmpty(reply.say)) reply = new LiaReply { say = replyText, emotion = "neutral" };

            _history.Add(new GContent { role = "model", parts = new[] { new GPart { text = replyText } } });
            LastReply = reply.say;
            OnReplyText?.Invoke(reply.say);
            Debug.Log($"[LiaBrain] Lia: \"{reply.say}\" (emotion={reply.emotion}, gesture={reply.gesture})");

            ApplyExpression(reply.emotion);
            if (!string.IsNullOrEmpty(reply.gesture) && reply.gesture != "null" && _receiver != null)
                _receiver.TriggerGesture(reply.gesture);

            if (!string.IsNullOrEmpty(ElevenKey))
                yield return StartCoroutine(SpeakRoutine(reply.say));
        }

        /// <summary>Ngobrol pakai SUARA: audio mic → Gemini (multimodal, paham audio langsung,
        /// tanpa STT terpisah) → balasan JSON persona → ekspresi + gesture + TTS.</summary>
        public void AskVoice(AudioClip clip, bool wakeFilter = false)
        {
            if (clip == null || IsBusy) return;
            if (UseOllama)
            {
                if (string.IsNullOrEmpty(SttUrl)) { OnError?.Invoke("STT_URL belum diisi di lia_ai.env."); return; }
                StartCoroutine(AskVoiceOllamaRoutine(clip, wakeFilter));
                return;
            }
            if (string.IsNullOrEmpty(GeminiKey)) { OnError?.Invoke("Gemini API key belum diisi (lia_ai.env)."); return; }
            StartCoroutine(AskVoiceRoutine(clip, wakeFilter));
        }

        /// <summary>Suara via Ollama: WAV → STT (faster-whisper VM) → transkrip → filter wake 'lia' → gpt-oss.</summary>
        private IEnumerator AskVoiceOllamaRoutine(AudioClip clip, bool wakeFilter)
        {
            IsBusy = true;
            if (_receiver == null) _receiver = FindAnyObjectByType<CommandReceiver>();

            var stt = new SttResult();
            yield return StartCoroutine(SttTranscribe(clip, stt));

            string transcript = stt.text != null ? stt.text.Trim() : "";
            if (!string.IsNullOrEmpty(stt.err))
            {
                Debug.LogWarning("[LiaBrain] STT gagal: " + stt.err);
                if (!wakeFilter) OnError?.Invoke("STT gagal: " + stt.err);
                IsBusy = false; yield break;
            }
            if (transcript.Length == 0) { IsBusy = false; yield break; }
            Debug.Log($"[LiaBrain] STT lang={stt.lang}: \"{transcript}\"");

            // Wake filter: hanya lanjut kalau transkrip menyebut "lia" (toleran mishear).
            if (wakeFilter && !MentionsWake(transcript))
            {
                Debug.Log("[LiaBrain] Wake: transkrip tak menyebut 'Lia' → diabaikan.");
                IsBusy = false; yield break;
            }

            _history.Add(new GContent { role = "user", parts = new[] { new GPart { text = transcript } } });
            TrimHistory();

            var res = new LlmResult();
            yield return StartCoroutine(OllamaChat(res));

            if (res.err != null || string.IsNullOrEmpty(res.reply))
            {
                Debug.LogWarning("[LiaBrain] voice(ollama) gagal: " + (res.err ?? "reply kosong"));
                if (!wakeFilter) OnError?.Invoke(res.err ?? "Lia tidak menjawab, coba lagi.");
                IsBusy = false; yield break;
            }

            if (wakeFilter)
            {
                var chk = ParsePersonaJson(res.reply);
                if (chk == null || string.IsNullOrWhiteSpace(chk.say))
                {
                    Debug.Log("[LiaBrain] Wake(ollama): say kosong → diabaikan.");
                    IsBusy = false; yield break;
                }
            }

            yield return StartCoroutine(HandleReply(res.reply));
            IsBusy = false;
        }

        private class SttResult { public string text; public string lang; public string err; }

        /// <summary>Kirim WAV ke faster-whisper HTTP server (VM) → transkrip.</summary>
        private IEnumerator SttTranscribe(AudioClip clip, SttResult res)
        {
            byte[] wav = EncodeWavBytes(clip);
            if (wav == null) { res.err = "WAV kosong"; yield break; }

            using (var www = new UnityWebRequest(SttUrl + "/transcribe", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(wav);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/octet-stream");
                www.timeout = 60;
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    res.err = $"HTTP {www.responseCode} {www.error}";
                    yield break;
                }
                try
                {
                    var r = JsonUtility.FromJson<SttResp>(www.downloadHandler.text);
                    res.text = r != null ? r.text : "";
                    res.lang = r != null ? r.lang : "";
                }
                catch (Exception e) { res.err = "parse STT gagal: " + e.Message; }
            }
        }

        [Serializable] private class SttResp { public string text; public string lang; }

        private IEnumerator AskVoiceRoutine(AudioClip clip, bool wakeFilter)
        {
            IsBusy = true;
            if (_receiver == null) _receiver = FindAnyObjectByType<CommandReceiver>();

            string b64 = EncodeWavBase64(clip);
            if (string.IsNullOrEmpty(b64)) { IsBusy = false; yield break; }

            // Mode wake: Gemini HANYA jawab kalau ucapan ditujukan ke Lia / menyebut "Lia".
            string instr = wakeFilter
                ? "Dengar audio ini. HANYA jawab kalau jelas ditujukan ke kamu (Lia) atau menyebut nama \\\"Lia\\\". Kalau obrolan lain / bukan untukmu / tak jelas, WAJIB balas {\\\"say\\\":\\\"\\\"} (kosong). Sebagai Lia:"
                : "Dengar & respon ucapan suara berikut, sebagai Lia:";

            // Body manual (JsonUtility tak bisa omit field 'text' untuk part audio).
            string body = "{"
                + "\"systemInstruction\":{\"parts\":[{\"text\":" + JsonEscape(BuildSystemPrompt()) + "}]},"
                + "\"contents\":[{\"role\":\"user\",\"parts\":["
                + "{\"text\":\"" + instr + "\"},"
                + "{\"inline_data\":{\"mime_type\":\"audio/wav\",\"data\":\"" + b64 + "\"}}"
                + "]}],"
                + "\"generationConfig\":{\"responseMimeType\":\"application/json\"}}";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={GeminiKey}";
            string replyText = null, err = null, raw = null;
            using (var www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = 40;
                yield return www.SendWebRequest();
                raw = www.downloadHandler != null ? www.downloadHandler.text : null;
                if (www.result != UnityWebRequest.Result.Success)
                    err = $"HTTP {www.responseCode} {www.error}";
                else
                    replyText = ExtractGeminiText(raw);
            }

            if (err != null || string.IsNullOrEmpty(replyText))
            {
                // Log SELALU (termasuk wake mode) biar bisa di-debug.
                Debug.LogWarning($"[LiaBrain] voice gagal: {err ?? "replyText kosong"} | resp: {(raw != null ? raw.Substring(0, Mathf.Min(300, raw.Length)) : "null")}");
                if (err != null && err.Contains("429"))
                {
                    RateLimitedUntil = Time.time + 45f;   // backoff kuota Gemini
                    OnError?.Invoke("Kuota Gemini penuh sebentar (429) — tunggu ~45 detik.");
                }
                else if (!wakeFilter) OnError?.Invoke(err ?? "Lia tidak menangkap suaramu, coba lagi.");
                IsBusy = false;
                yield break;
            }

            // Mode wake: kalau say kosong → ucapan bukan untuk Lia → diabaikan (tak bicara).
            if (wakeFilter)
            {
                var chk = ParsePersonaJson(replyText);
                if (chk == null || string.IsNullOrWhiteSpace(chk.say))
                {
                    Debug.Log("[LiaBrain] Wake: ucapan bukan untuk Lia → diabaikan.");
                    IsBusy = false;
                    yield break;
                }
            }

            yield return StartCoroutine(HandleReply(replyText));
            IsBusy = false;
        }

        /// <summary>AudioClip → WAV 16-bit PCM (bytes). Dipakai STT (raw) & Gemini (base64).</summary>
        private static byte[] EncodeWavBytes(AudioClip clip)
        {
            int n = clip.samples * clip.channels;
            if (n <= 0) return null;
            var samples = new float[n];
            clip.GetData(samples, 0);
            int sr = clip.frequency, ch = clip.channels, dataLen = n * 2;

            using (var ms = new System.IO.MemoryStream(44 + dataLen))
            using (var bw = new System.IO.BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + dataLen);
                bw.Write(Encoding.ASCII.GetBytes("WAVE")); bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16); bw.Write((short)1); bw.Write((short)ch); bw.Write(sr);
                bw.Write(sr * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataLen);
                for (int i = 0; i < n; i++)
                    bw.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f));
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>AudioClip → WAV 16-bit → base64 (untuk inline audio Gemini).</summary>
        private static string EncodeWavBase64(AudioClip clip)
        {
            var b = EncodeWavBytes(clip);
            return b == null ? null : System.Convert.ToBase64String(b);
        }

        private IEnumerator SpeakRoutine(string text)
        {
            const int sampleRate = 24000;
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{ElevenVoice}?output_format=pcm_{sampleRate}";
            // flash_v2_5 = TTS latency-rendah (multi-bahasa) — lebih cepat dari multilingual_v2.
            string body = "{\"text\":" + JsonEscape(text) +
                          ",\"model_id\":\"eleven_flash_v2_5\"," +
                          "\"voice_settings\":{\"stability\":0.4,\"similarity_boost\":0.8}}";

            byte[] pcm = null; string err = null;
            using (var www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("xi-api-key", ElevenKey);
                www.SetRequestHeader("Accept", "audio/pcm");
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                    err = $"ElevenLabs error: {www.responseCode} {www.error}";
                else
                    pcm = www.downloadHandler.data;
            }

            if (err != null || pcm == null || pcm.Length < 2)
            {
                Debug.LogWarning("[LiaBrain] TTS gagal: " + (err ?? "kosong"));
                yield break;
            }

            // PCM 16-bit LE mono → AudioClip
            int sampleCount = pcm.Length / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = s / 32768f;
            }
            var clip = AudioClip.Create("LiaTTS", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);

            if (_receiver != null) _receiver.PlayClip(clip);
        }

        private void ApplyExpression(string emotion)
        {
            if (_receiver == null) return;
            switch (emotion)
            {
                case "happy":     _receiver.SetExpression("Happy|0.9"); break;
                case "sad":       _receiver.SetExpression("Sad|0.8"); break;
                case "angry":     _receiver.SetExpression("Angry|0.8"); break;
                case "surprised": _receiver.SetExpression("Surprised|0.8"); break;
                default:          _receiver.SetExpression("Neutral|1.0"); break;
            }
        }

        private void TrimHistory()
        {
            const int max = 16;
            while (_history.Count > max) _history.RemoveAt(0);
        }

        // ---------- JSON helpers ----------
        private static string ExtractGeminiText(string json)
        {
            try
            {
                var resp = JsonUtility.FromJson<GResp>(json);
                if (resp?.candidates != null && resp.candidates.Length > 0 &&
                    resp.candidates[0].content?.parts != null && resp.candidates[0].content.parts.Length > 0)
                    return resp.candidates[0].content.parts[0].text;
            }
            catch (Exception e) { Debug.LogWarning("[LiaBrain] parse Gemini gagal: " + e.Message); }
            return null;
        }

        private static string ExtractOllamaText(string json)
        {
            try
            {
                var r = JsonUtility.FromJson<OResp>(json);
                if (r?.message != null && !string.IsNullOrEmpty(r.message.content))
                    return r.message.content;
            }
            catch (Exception e) { Debug.LogWarning("[LiaBrain] parse Ollama gagal: " + e.Message); }
            return null;
        }

        // Wake-word toleran mishear Whisper ("lia" sering jadi leo/lya/lea/ria/liya/dia).
        private static readonly string[] WakeVariants = { "lia", "lya", "liya", "lea", "leah", "leo", "ria", "lija", "lian", "kohaku" };
        private static bool MentionsWake(string transcript)
        {
            string t = transcript.ToLowerInvariant();
            foreach (var w in WakeVariants)
                if (t.IndexOf(w, StringComparison.Ordinal) >= 0) return true;

            // Kata PERTAMA berjarak ≤1 edit dari "lia" (mis. "dia"/"sia"/"nia" saat menyapa).
            // Cuma kata pertama → pola menyapa; edit≤1 → tak kena "saya"/"aku"/"apa".
            string first = t.TrimStart();
            int sp = first.IndexOfAny(new[] { ' ', ',', '.', '!', '?' });
            if (sp > 0) first = first.Substring(0, sp);
            first = first.Trim(',', '.', '!', '?', ' ');
            if (first.Length >= 2 && first.Length <= 4 && LevAtMost1(first, "lia")) return true;
            return false;
        }

        /// <summary>true kalau jarak edit (Levenshtein) a↔b ≤ 1.</summary>
        private static bool LevAtMost1(string a, string b)
        {
            int la = a.Length, lb = b.Length;
            if (Math.Abs(la - lb) > 1) return false;
            if (a == b) return true;
            int i = 0, j = 0, diff = 0;
            while (i < la && j < lb)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (++diff > 1) return false;
                if (la == lb) { i++; j++; }        // substitusi
                else if (la > lb) i++;             // hapus di a
                else j++;                          // sisip di a
            }
            if (i < la || j < lb) diff++;          // sisa 1 char di ujung
            return diff <= 1;
        }

        private static LiaReply ParsePersonaJson(string text)
        {
            try
            {
                string t = text.Trim();
                // Kadang model bungkus dengan ```json ... ```
                int b = t.IndexOf('{'); int e = t.LastIndexOf('}');
                if (b >= 0 && e > b) t = t.Substring(b, e - b + 1);
                return JsonUtility.FromJson<LiaReply>(t);
            }
            catch { return null; }
        }

        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---------- DTO (JsonUtility) ----------
        [Serializable] public class GPart { public string text; }
        [Serializable] public class GContent { public string role; public GPart[] parts; }
        [Serializable] public class GSysInstr { public GPart[] parts; }
        [Serializable] public class GGenConfig { public string responseMimeType; }
        [Serializable] public class GReq { public GSysInstr systemInstruction; public GContent[] contents; public GGenConfig generationConfig; }
        [Serializable] public class GRespPart { public string text; }
        [Serializable] public class GRespContent { public GRespPart[] parts; }
        [Serializable] public class GCandidate { public GRespContent content; }
        [Serializable] public class GResp { public GCandidate[] candidates; }
        [Serializable] public class LiaReply { public string say; public string emotion; public string gesture; }
        // Ollama /api/chat
        [Serializable] public class OMessage { public string role; public string content; }
        [Serializable] public class OResp { public OMessage message; }
    }
}
