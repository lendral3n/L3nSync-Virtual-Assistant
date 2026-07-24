using UnityEngine;
using VRMAssistant.Core;
using VRMAssistant.AI;

namespace VRMAssistant.UI
{
    /// <summary>
    /// Indikator status suara di atas kepala Lia (jalan di overlay & app):
    ///   • hijau  "Mendengar…"  → VoiceListener menangkap suaramu
    ///   • kuning "Mikir…"       → AI (Gemini) sedang memproses
    ///   • biru   "Bicara…"      → TTS ElevenLabs sedang diputar
    /// Idle → tidak tampil apa-apa (biar bersih). Non-interaktif (tak ganggu click-through).
    /// </summary>
    public class VoiceStatusUI : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("VoiceStatusUI");
            go.AddComponent<VoiceStatusUI>();
            DontDestroyOnLoad(go);
        }

        private VRMModelLoader _loader;
        private LiaBrain _brain;
        private CommandReceiver _receiver;
        private VoiceListener _voice;
        private Camera _cam;
        private GUIStyle _style;

        private void Resolve()
        {
            if (_loader == null) _loader = FindAnyObjectByType<VRMModelLoader>();
            if (_brain == null) _brain = FindAnyObjectByType<LiaBrain>();
            if (_receiver == null) _receiver = FindAnyObjectByType<CommandReceiver>();
            if (_voice == null) _voice = FindAnyObjectByType<VoiceListener>();
            if (_cam == null) _cam = Camera.main;
        }

        private void OnGUI()
        {
            Resolve();
            if (_cam == null || _loader == null || _loader.LoadedModel == null) return;

            // Prioritas: Bicara > Mikir > Mendengar. "Mendengar" muncul real-time saat kamu
            // benar-benar ngomong (VAD di atas ambang 0.06); diam → hilang. Idle → tak ada teks.
            string text; Color col;
            if (_receiver != null && _receiver.IsSpeaking) { text = "Bicara…"; col = new Color(0.25f, 0.55f, 1f); }
            else if (_brain != null && _brain.IsBusy)      { text = "Mikir…";  col = new Color(1f, 0.7f, 0.15f); }
            else if (_voice != null && _voice.IsHearing)   { text = "Mendengar…"; col = new Color(0.2f, 0.8f, 0.35f); }
            else return; // diam / hening → tak tampil

            // Posisi tepat di atas kepala — pakai tulang Head (bounds max.y ketinggian krn rambut).
            var t = _loader.LoadedModel.transform;
            Vector3 headWorld;
            var anim = _loader.ModelAnimator;
            Transform headBone = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (headBone != null) headWorld = headBone.position + Vector3.up * (0.16f * t.localScale.y);
            else headWorld = t.position + Vector3.up * (1.6f * t.localScale.y);
            Vector3 sp = _cam.WorldToScreenPoint(headWorld);
            if (sp.z < 0f) return;

            if (_style == null)
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 15,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };

            float w = 150f, hgt = 30f;
            float gx = sp.x - w / 2f;
            float gy = Screen.height - sp.y - hgt - 6f;   // flip Y (GUI top-left) + sedikit di atas kepala
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = col;
            GUI.Box(new Rect(gx, gy, w, hgt), "● " + text, _style);
            GUI.backgroundColor = prev;
        }
    }
}
