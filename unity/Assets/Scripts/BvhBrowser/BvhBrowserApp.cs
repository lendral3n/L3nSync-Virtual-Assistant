#if BVH_BROWSER
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VRMAssistant.Core;

namespace BvhBrowser
{
    /// <summary>
    /// BVH Browser (Mac, Unity) — jelajahi ribuan mocap Bandai .bvh, preview stick-figure 3D
    /// (orbit kamera), tandai favorit → export favorites.txt. Port dari app Android BvhBrowser.
    ///
    /// Aktif hanya di build ber-define BVH_BROWSER (bukan LiaVA biasa). Semua dibuat via kode
    /// (kamera/lampu/skeleton), jadi tak butuh scene ter-author.
    /// </summary>
    public class BvhBrowserApp : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("BvhBrowserApp");
            go.AddComponent<BvhBrowserApp>();
            DontDestroyOnLoad(go);
        }

        // ---- data ----
        private string _dir;
        private List<string> _files = new List<string>();     // path lengkap .bvh
        private readonly HashSet<string> _fav = new HashSet<string>();
        private string _favPath;

        // ---- ui state ----
        private string _query = "";
        private bool _favOnly;
        private Vector2 _scroll;
        private int _selected = -1;      // index di _files
        private List<int> _shown = new List<int>();
        private GUIStyle _title, _sub, _row, _btn, _ctrl, _search, _starOn, _starOff;
        private Texture2D _texPanel, _texSel, _texBtn, _texBtnH, _texField, _texAccent;
        private bool _styles;

        // ---- playback ----
        private BvhClip _clip;
        private int _frame;
        private float _frameAccum;
        private bool _playing = true;
        private float _speed = 1f;
        private string _error;

        // ---- render ----
        private Camera _cam;
        private Transform _skelRoot;
        private LineRenderer[] _bones;
        private Vector3[] _pos;             // world pos per joint (cache)
        private float _fitScale = 1f;
        private Vector3 _center;
        private float _yaw = 0f, _pitch = 8f, _dist = 3.2f;
        private Material _lineMat;

        // ---- VRM (Kohaku) ----
        private VRMModelLoader _loader;
        private Animator _kohaku;
        private BvhRetargeter _retarget;
        private bool _vrmMode;             // true = preview di Kohaku, false = stick-figure

        void Start()
        {
            Application.runInBackground = true;
            ResolveDir();
            LoadFileList();
            LoadFavorites();
            SetupScene();
            LoadKohaku();
            CheckCaptureRequest();
        }

        // ---- Harness verifikasi-diri: bila ada bvh_capture.txt ("substring|frame|[stick]"),
        // auto-buka clip, pose frame, screenshot ke bvh_shot.png, lalu quit. ----
        private void CheckCaptureRequest()
        {
            string ad = AppDir();
            string req = ad != null ? Path.Combine(ad, "bvh_capture.txt") : null;
            if (string.IsNullOrEmpty(req) || !File.Exists(req)) return;
            var parts = File.ReadAllText(req).Trim().Split('|');
            string sub = parts[0].Trim();
            string firstSub = null;   // "a;b" = buka a dulu (reproduksi browsing), lalu b
            int semi = sub.IndexOf(';');
            if (semi > 0) { firstSub = sub.Substring(0, semi).Trim(); sub = sub.Substring(semi + 1).Trim(); }
            int fr;
            if (parts.Length > 1 && parts[1].Trim().Equals("play", System.StringComparison.OrdinalIgnoreCase)) fr = -1;
            else if (parts.Length > 1 && parts[1].Trim().Equals("seri", System.StringComparison.OrdinalIgnoreCase)) fr = -2;
            else fr = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var f) ? f : 30;
            bool stick = parts.Length > 2 && parts[2].Trim().Equals("stick", System.StringComparison.OrdinalIgnoreCase);
            StartCoroutine(CaptureRoutine(firstSub, sub, fr, stick, Path.Combine(ad, "bvh_shot.png")));
        }

        private System.Collections.IEnumerator CaptureRoutine(string firstSub, string sub, int frame, bool stick, string outPng)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!stick && _kohaku == null && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            if (stick) _vrmMode = false;

            // Reproduksi alur browsing: buka clip pertama, mainkan sebentar, baru clip target.
            if (!string.IsNullOrEmpty(firstSub))
            {
                int idx0 = _files.FindIndex(p => Path.GetFileName(p).IndexOf(firstSub, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (idx0 >= 0)
                {
                    OpenClip(idx0);
                    _playing = true;
                    for (int k = 0; k < 45; k++) yield return new WaitForEndOfFrame();
                    Debug.Log("[BvhCapture] pre-clip dimainkan: " + firstSub);
                }
            }

            int idx = _files.FindIndex(p => Path.GetFileName(p).IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (idx < 0) { Debug.Log("[BvhCapture] tak ketemu: " + sub); Application.Quit(); yield break; }
            OpenClip(idx);
            // mode "play" (-1) → biarkan berjalan utk reproduksi crash saat play
            if (frame == -1)
            {
                _playing = true;
                for (int k = 0; k < 400; k++)
                {
                    if (k % 60 == 0) Debug.Log("[BvhCapture] alive f=" + _frame + " k=" + k);
                    yield return new WaitForEndOfFrame();
                }
                ScreenCapture.CaptureScreenshot(outPng);
                Debug.Log("[BvhCapture] play-test selesai (tak crash) → " + outPng);
                for (int k = 0; k < 20; k++) yield return new WaitForEndOfFrame();
                Application.Quit();
                yield break;
            }
            _playing = false;
            for (int k = 0; k < 40; k++) yield return new WaitForEndOfFrame();   // settle yaw-lock

            // frame bisa >=0 (single) ATAU -2 = SERI: sampel 8 frame merata → bvh_f{N}.png.
            string mdir = System.IO.Path.GetDirectoryName(outPng);
            int fc = _clip != null ? _clip.FrameCount : 1;
            int[] frames = frame >= 0
                ? new[] { Mathf.Clamp(frame, 0, fc - 1) }
                : new[] { 0, fc / 7, 2 * fc / 7, 3 * fc / 7, 4 * fc / 7, 5 * fc / 7, 6 * fc / 7, fc - 1 };
            foreach (int f in frames)
            {
                _frame = f;
                if (_vrmMode && _retarget != null && _retarget.Ready) _retarget.Apply(_frame);
                for (int k = 0; k < 3; k++) yield return new WaitForEndOfFrame();
                string p = frames.Length == 1 ? outPng : System.IO.Path.Combine(mdir, "bvh_f" + f + ".png");
                ScreenCapture.CaptureScreenshot(p);
                Debug.Log("[BvhCapture] shot f=" + f);
                for (int k = 0; k < 8; k++) yield return new WaitForEndOfFrame();
            }
            for (int k = 0; k < 10; k++) yield return new WaitForEndOfFrame();
            Application.Quit();
        }

        private void LoadKohaku()
        {
            // VRMModelLoader.loadOnStart=true → dia auto-load sendiri; cukup subscribe
            // (JANGAN panggil LoadModelAsync lagi — bikin model dobel).
            var go = new GameObject("VRMLoader");
            _loader = go.AddComponent<VRMModelLoader>();
            _loader.OnModelLoaded += OnKohakuLoaded;
        }

        private void OnKohakuLoaded(GameObject model)
        {
            if (_kohaku != null) return;   // idempoten (guard event dobel)
            _kohaku = _loader.ModelAnimator;
            if (_kohaku == null) return;

            // Matikan LookAt bawaan VRM — kepala/mata "menoleh sendiri" ke kamera menutupi
            // orientasi badan yang sebenarnya (preview harus jujur 100% dari data mocap).
            foreach (var b in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string tn = b.GetType().Name;
                if (tn.Contains("LookAt") || tn == "Blinker" || tn == "VRMBlink")
                    b.enabled = false;
            }
            _vrmMode = true;
            _retarget = new BvhRetargeter();
            // framing kamera untuk karakter setinggi ~1.5m di origin
            _center = new Vector3(0f, 0.9f, 0f);
            _fitScale = 1.8f;
            _dist = 2.6f; _pitch = 6f; _yaw = 180f;   // swing-twist yaw-lock + kamera yaw 180 → lihat WAJAH
            if (_bones != null) foreach (var b in _bones) if (b) b.enabled = false;  // sembunyikan stick-figure
            if (_selected >= 0 && _clip != null) _retarget.Setup(_clip, _kohaku);
            Debug.Log("[BvhBrowser] Kohaku siap → mode VRM");
        }

        // -------- dataset dir --------
        private void ResolveDir()
        {
            // 1) config bvh_dir.txt di sebelah .app  2) default path dataset  3) persistentDataPath/bvh
            foreach (var p in ConfigCandidates())
            {
                if (File.Exists(p))
                {
                    var d = File.ReadAllText(p).Trim();
                    if (Directory.Exists(d)) { _dir = d; break; }
                }
            }
            if (string.IsNullOrEmpty(_dir))
            {
                // Root dataset (berisi Motiondataset-1 [175] + Motiondataset-2 [2902] = 3077).
                string def = "/Users/lendra/Documents/codeV/LiaVA/assets/mocap/bandai/dataset";
                _dir = Directory.Exists(def) ? def : Path.Combine(Application.persistentDataPath, "bvh");
            }
            Directory.CreateDirectory(_dir);
            // favorites.txt di sebelah .app biar gampang dipakai untuk apply ke LiaVA.
            _favPath = Path.Combine(AppDir() ?? _dir, "favorites.txt");
            Debug.Log($"[BvhBrowser] dir={_dir}");
        }

        private string AppDir()
        {
            try
            {
                var d = new DirectoryInfo(Application.dataPath);
                while (d != null && !d.Name.EndsWith(".app")) d = d.Parent;
                return d?.Parent?.FullName;
            }
            catch { return null; }
        }

        private IEnumerable<string> ConfigCandidates()
        {
            string appDir = AppDir();
            if (!string.IsNullOrEmpty(appDir)) yield return Path.Combine(appDir, "bvh_dir.txt");
            yield return Path.Combine(Application.persistentDataPath, "bvh_dir.txt");
        }

        private void LoadFileList()
        {
            try
            {
                _files = Directory.EnumerateFiles(_dir, "*.bvh", SearchOption.AllDirectories)
                                  .OrderBy(Path.GetFileName).ToList();
            }
            catch (System.Exception e) { Debug.LogWarning("[BvhBrowser] list gagal: " + e.Message); }
            Debug.Log($"[BvhBrowser] {_files.Count} file .bvh");
            RebuildShown();
        }

        private void LoadFavorites()
        {
            _fav.Clear();
            if (File.Exists(_favPath))
                foreach (var l in File.ReadAllLines(_favPath))
                    if (!string.IsNullOrWhiteSpace(l)) _fav.Add(l.Trim());
        }

        private void SaveFavorites()
        {
            try { File.WriteAllLines(_favPath, _fav.OrderBy(x => x)); }
            catch (System.Exception e) { Debug.LogWarning("[BvhBrowser] simpan fav gagal: " + e.Message); }
        }

        private string NameOf(int idx) => Path.GetFileNameWithoutExtension(_files[idx]);
        private string FileNameOf(int idx) => Path.GetFileName(_files[idx]);

        private void RebuildShown()
        {
            _shown.Clear();
            for (int i = 0; i < _files.Count; i++)
            {
                string n = FileNameOf(i);
                if (_favOnly && !_fav.Contains(n)) continue;
                if (!string.IsNullOrEmpty(_query) && n.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                _shown.Add(i);
            }
        }

        // -------- scene / render --------
        private void SetupScene()
        {
            var camGo = new GameObject("BvhCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.06f, 0.06f, 0.09f);
            _cam.fieldOfView = 45f;

            var lightGo = new GameObject("BvhLight");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            _lineMat = new Material(Shader.Find("Sprites/Default"));
            _skelRoot = new GameObject("Skeleton").transform;
        }

        private void BuildSkeletonRenderers(BvhClip clip)
        {
            // bersihkan lama
            if (_bones != null) foreach (var b in _bones) if (b) Destroy(b.gameObject);
            foreach (Transform c in _skelRoot) Destroy(c.gameObject);

            _pos = new Vector3[clip.joints.Count];
            var boneList = new List<LineRenderer>();
            for (int i = 0; i < clip.joints.Count; i++)
            {
                if (clip.joints[i].parent < 0) continue;
                var go = new GameObject("bone" + i);
                go.transform.SetParent(_skelRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _lineMat;
                lr.widthMultiplier = 0.018f;
                lr.positionCount = 2;
                lr.numCapVertices = 4;
                lr.startColor = lr.endColor = new Color(0.39f, 0.71f, 0.96f);
                lr.useWorldSpace = true;
                boneList.Add(lr);
            }
            _bones = boneList.ToArray();
        }

        private void OpenClip(int idx)
        {
            _selected = idx;
            _error = null; _clip = null; _frame = 0; _frameAccum = 0f; _playing = true;
            try
            {
                _clip = BvhClip.Parse(_files[idx]);
                _pos = new Vector3[_clip.joints.Count];
                _clip.EvaluateWorld(0, _pos);
                if (_vrmMode && _kohaku != null && _retarget != null)
                {
                    _retarget.Setup(_clip, _kohaku);   // preview di Kohaku
                }
                else
                {
                    ComputeFit();
                    BuildSkeletonRenderers(_clip);     // fallback stick-figure
                }
            }
            catch (System.Exception e) { _error = e.Message; Debug.LogWarning("[BvhBrowser] parse: " + e.Message); }
        }

        private void ComputeFit()
        {
            Vector3 min = Vector3.one * 1e9f, max = -Vector3.one * 1e9f;
            for (int i = 0; i < _pos.Length; i++)
            {
                min = Vector3.Min(min, _pos[i]); max = Vector3.Max(max, _pos[i]);
            }
            _center = (min + max) * 0.5f;
            _fitScale = Mathf.Max(0.001f, (max - min).magnitude);
            _dist = _fitScale * 1.4f;
        }

        void Update()
        {
            // playback clock
            if (_clip != null && _playing && _clip.FrameCount > 0)
            {
                _frameAccum += Time.deltaTime * _speed;
                float ft = Mathf.Max(0.0001f, _clip.frameTime);
                while (_frameAccum >= ft)
                {
                    _frameAccum -= ft;
                    _frame = (_frame + 1) % _clip.FrameCount;
                }
            }
            if (_clip != null && !_vrmMode && _pos != null)
            {
                _clip.EvaluateWorld(_frame, _pos);
                UpdateBones();
            }
            UpdateCamera();
        }

        private bool _applyErrLogged;
        void LateUpdate()
        {
            // Retarget setelah update lain (VRM springbone dsb) supaya pose kita menang.
            if (_vrmMode && _retarget != null && _retarget.Ready && _clip != null)
            {
                try { _retarget.Apply(_frame); }
                catch (System.Exception e)
                {
                    if (!_applyErrLogged) { _applyErrLogged = true; Debug.LogError("[BvhBrowser] Apply error: " + e); }
                }
            }
        }

        private void UpdateBones()
        {
            if (_bones == null) return;
            int bi = 0;
            for (int i = 0; i < _clip.joints.Count; i++)
            {
                int p = _clip.joints[i].parent;
                if (p < 0) continue;
                if (bi >= _bones.Length) break;
                _bones[bi].SetPosition(0, _pos[p]);
                _bones[bi].SetPosition(1, _pos[i]);
                bi++;
            }
        }

        private void UpdateCamera()
        {
            if (_cam == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 eye = _center + rot * new Vector3(0, 0, -_dist);
            _cam.transform.position = eye;
            _cam.transform.LookAt(_center);
        }

        // -------- GUI --------
        void OnGUI()
        {
            EnsureStyles();
            if (_selected < 0) DrawList();
            else DrawPlayer();
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; return t;
        }

        private void EnsureStyles()
        {
            if (_styles) return;
            _texPanel  = Solid(new Color(0.06f, 0.07f, 0.10f, 0.96f));
            _texSel    = Solid(new Color(0.35f, 0.62f, 0.95f, 0.35f));
            _texBtn    = Solid(new Color(0.16f, 0.18f, 0.24f, 1f));
            _texBtnH   = Solid(new Color(0.24f, 0.42f, 0.66f, 1f));
            _texField  = Solid(new Color(0.10f, 0.12f, 0.16f, 1f));
            _texAccent = Solid(new Color(0.35f, 0.62f, 0.95f, 1f));
            Color white = Color.white, gray = new Color(0.60f, 0.64f, 0.71f);

            _title = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, normal = { textColor = white } };
            _sub   = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = gray } };
            _row   = new GUIStyle(GUI.skin.label) {
                fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 6, 0, 0),
                normal = { textColor = new Color(0.85f, 0.88f, 0.92f) },
                hover  = { textColor = white, background = _texBtn } };
            _btn = new GUIStyle(GUI.skin.button) {
                fontSize = 12, border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = white, background = _texBtn },
                hover  = { textColor = white, background = _texBtnH },
                active = { textColor = white, background = _texBtnH } };
            _ctrl = new GUIStyle(_btn) { fontSize = 17 };
            _search = new GUIStyle(GUI.skin.textField) {
                fontSize = 13, padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = white, background = _texField },
                focused = { textColor = white, background = _texField } };
            _starOn  = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.82f, 0.28f) } };
            _starOff = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.42f, 0.45f, 0.52f) } };
            _styles = true;
        }

        private void DrawList()
        {
            float W = Screen.width, H = Screen.height;
            float sw = Mathf.Min(400f, W * 0.44f);
            GUI.DrawTexture(new Rect(0, 0, sw, H), _texPanel);
            GUI.DrawTexture(new Rect(sw - 2, 0, 2, H), _texAccent);   // garis aksen

            float pad = 16f, x = pad, y = 14f, iw = sw - pad * 2;
            GUI.Label(new Rect(x, y, iw, 26), "BVH Browser", _title); y += 28;
            GUI.Label(new Rect(x, y, iw, 16), $"Bandai mocap · {_files.Count} animasi", _sub); y += 22;

            string q = GUI.TextField(new Rect(x, y, iw, 30), _query, _search);
            if (q != _query) { _query = q; RebuildShown(); }
            y += 38;

            float bw = (iw - 8) / 2f;
            if (GUI.Button(new Rect(x, y, bw, 28), (_favOnly ? "★ " : "☆ ") + $"Favorit ({_fav.Count})", _btn)) { _favOnly = !_favOnly; RebuildShown(); }
            if (GUI.Button(new Rect(x + bw + 8, y, bw, 28), "Export .txt", _btn)) { SaveFavorites(); Debug.Log("[BvhBrowser] favorites → " + _favPath); }
            y += 34;
            GUI.Label(new Rect(x, y, iw, 16), $"{_shown.Count} / {_files.Count} file", _sub); y += 22;

            if (_files.Count == 0)
                GUI.Label(new Rect(x, y, iw, 60), $"Dataset kosong:\n{_dir}", _sub);

            var listRect = new Rect(x, y, iw, H - y - pad);
            float rowH = 30f;
            var view = new Rect(0, 0, iw - 18, _shown.Count * rowH);
            _scroll = GUI.BeginScrollView(listRect, _scroll, view);
            for (int k = 0; k < _shown.Count; k++)
            {
                int idx = _shown[k];
                var r = new Rect(0, k * rowH, iw - 18, rowH - 2);
                if (idx == _selected) GUI.DrawTexture(r, _texSel);
                bool isFav = _fav.Contains(FileNameOf(idx));
                if (GUI.Button(new Rect(r.x + 2, r.y, 26, r.height), isFav ? "★" : "☆", isFav ? _starOn : _starOff))
                { if (isFav) _fav.Remove(FileNameOf(idx)); else _fav.Add(FileNameOf(idx)); SaveFavorites(); }
                if (GUI.Button(new Rect(r.x + 30, r.y, r.width - 30, r.height), NameOf(idx), _row)) OpenClip(idx);
            }
            GUI.EndScrollView();

            GUI.Label(new Rect(sw + 16, H - 26, 320, 20), "drag: putar  ·  scroll: zoom", _sub);
        }

        private void DrawPlayer()
        {
            float W = Screen.width, H = Screen.height;
            // ── top bar ──
            GUI.DrawTexture(new Rect(0, 0, W, 52), _texPanel);
            if (GUI.Button(new Rect(12, 11, 92, 30), "←  Kembali", _btn))
            { _selected = -1; _clip = null; if (_vrmMode && _retarget != null) _retarget.RestoreRest(); }
            GUI.Label(new Rect(116, 13, W - 400, 26), NameOf(_selected), _title);
            bool isFav = _fav.Contains(FileNameOf(_selected));
            if (GUI.Button(new Rect(W - 152, 11, 140, 30), isFav ? "★  Favorit" : "☆  Favorit", _btn))
            { if (isFav) _fav.Remove(FileNameOf(_selected)); else _fav.Add(FileNameOf(_selected)); SaveFavorites(); }

            // ── orbit area (antara bar) ──
            HandleOrbit(new Rect(0, 52, W, H - 52 - 60));
            if (_error != null) GUI.Label(new Rect(20, 70, W - 40, 40), "Parse error: " + _error, _sub);

            // ── bottom control bar ──
            float by = H - 56;
            GUI.DrawTexture(new Rect(0, by, W, 56), _texPanel);
            float cx = W / 2f;
            if (GUI.Button(new Rect(cx - 132, by + 13, 42, 30), "«", _ctrl)) Step(-1);
            if (GUI.Button(new Rect(cx - 86, by + 13, 52, 30), _playing ? "II" : "▶", _ctrl)) _playing = !_playing;
            if (GUI.Button(new Rect(cx - 30, by + 13, 42, 30), "»", _ctrl)) Step(1);
            GUI.Label(new Rect(cx + 24, by + 18, 44, 20), "Speed", _sub);
            _speed = GUI.HorizontalSlider(new Rect(cx + 68, by + 23, 150, 20), _speed, 0.25f, 3f);
            int fc = _clip != null ? _clip.FrameCount : 0;
            GUI.Label(new Rect(cx + 228, by + 18, 140, 20), $"{_speed:0.00}x  ·  f:{_frame}/{fc}", _sub);
        }

        private void Step(int dir)
        {
            if (_shown.Count == 0) return;
            int pos = _shown.IndexOf(_selected);
            if (pos < 0) pos = 0;
            pos = (pos + dir + _shown.Count) % _shown.Count;
            OpenClip(_shown[pos]);
        }

        private void HandleOrbit(Rect area)
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type == EventType.MouseDrag && area.Contains(e.mousePosition))
            {
                _yaw += e.delta.x * 0.4f;
                _pitch = Mathf.Clamp(_pitch - e.delta.y * 0.3f, -80f, 80f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
            {
                _dist = Mathf.Clamp(_dist + e.delta.y * _fitScale * 0.05f, _fitScale * 0.4f, _fitScale * 4f);
                e.Use();
            }
        }
    }
}
#endif
