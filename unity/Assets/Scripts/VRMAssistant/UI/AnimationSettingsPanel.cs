using UnityEngine;
using VRMAssistant.Behavior;

namespace VRMAssistant.UI
{
    /// <summary>
    /// Panel setting animasi (IMGUI) untuk Mac/desktop:
    ///   - Checklist 32 animasi: centang mana yang dipakai scheduler (persist PlayerPrefs).
    ///   - Klik nama / tombol ▶ → karakter LANGSUNG memperagakan animasi itu (preview).
    /// Toggle panel: tombol ⚙ pojok kiri-atas atau tekan TAB.
    ///
    /// Auto-dibuat via RuntimeInitializeOnLoadMethod (tanpa edit scene). Jalan di semua platform.
    /// </summary>
    public class AnimationSettingsPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("AnimationSettingsPanel");
            go.AddComponent<AnimationSettingsPanel>();
            DontDestroyOnLoad(go);
        }

        private bool _open;
        private Vector2 _scroll;
        private Component _receiver;
        private System.Reflection.MethodInfo _trigger;
        private GUIStyle _hdr, _row, _btn;
        private bool _stylesReady;

        private void ResolveReceiver()
        {
            if (_receiver != null) return;
            var t = System.Type.GetType("VRMAssistant.AI.CommandReceiver, Assembly-CSharp");
            if (t == null) return;
            _receiver = FindAnyObjectByType(t) as Component;
            _trigger = t.GetMethod("TriggerGesture");
        }

        private void Preview(string gestureName)
        {
            ResolveReceiver();
            _trigger?.Invoke(_receiver, new object[] { gestureName });
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _hdr = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _row = new GUIStyle(GUI.skin.toggle) { fontSize = 12, normal = { textColor = Color.white }, onNormal = { textColor = Color.white } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _stylesReady = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) _open = !_open;
        }

        private void OnGUI()
        {
            // Di mode overlay transparan: karakter bersih saja, UI setting hanya di app normal.
            if (VRMAssistant.Behavior.LiaInput.OverlayActive) return;
            EnsureStyles();

            // Tombol ⚙ selalu tampak (pojok kiri-atas)
            if (GUI.Button(new Rect(12, 12, 96, 30), _open ? "✕ Tutup" : "⚙ Animasi"))
                _open = !_open;

            if (!_open) return;

            float w = 340f, h = Mathf.Min(Screen.height - 90f, 560f);
            var box = new Rect(12, 52, w, h);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 16));

            GUILayout.Label($"Animasi Lia — aktif {GestureLibrary.EnabledCount()}/{GestureLibrary.All.Length}", _hdr);
            GUILayout.Label("Centang = dipakai otomatis · ▶ = coba sekarang", GUILayout.Height(16));
            GUILayout.Space(4);

            if (GUILayout.Button("Semua ON / OFF", _btn, GUILayout.Height(22)))
            {
                bool anyOff = false;
                foreach (var g in GestureLibrary.All) if (!GestureLibrary.IsEnabled(g.name)) { anyOff = true; break; }
                foreach (var g in GestureLibrary.All) GestureLibrary.SetEnabled(g.name, anyOff); // kalau ada yg off → nyalakan semua, else matikan semua
            }
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var g in GestureLibrary.All)
            {
                GUILayout.BeginHorizontal();
                bool cur = GestureLibrary.IsEnabled(g.name);
                bool now = GUILayout.Toggle(cur, $"  {g.label}", _row);
                if (now != cur) GestureLibrary.SetEnabled(g.name, now);
                GUILayout.FlexibleSpace();
                GUILayout.Label(g.vrma ? "VRMA" : "mocap", GUILayout.Width(46));
                if (GUILayout.Button("▶", _btn, GUILayout.Width(28))) Preview(g.name);
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
