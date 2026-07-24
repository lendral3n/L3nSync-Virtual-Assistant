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
        private GUIStyle _title, _row, _small;
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
            int fr;
            if (parts.Length > 1 && parts[1].Trim().Equals("play", System.StringComparison.OrdinalIgnoreCase)) fr = -1;
            else fr = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var f) ? f : 30;
            bool stick = parts.Length > 2 && parts[2].Trim().Equals("stick", System.StringComparison.OrdinalIgnoreCase);
            StartCoroutine(CaptureRoutine(sub, fr, stick, Path.Combine(ad, "bvh_shot.png")));
        }

        private System.Collections.IEnumerator CaptureRoutine(string sub, int frame, bool stick, string outPng)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!stick && _kohaku == null && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            if (stick) _vrmMode = false;

            int idx = _files.FindIndex(p => Path.GetFileName(p).IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (idx < 0) { Debug.Log("[BvhCapture] tak ketemu: " + sub); Application.Quit(); yield break; }
            OpenClip(idx);
            // mode "play" (frame ke-3 = play) → biarkan berjalan utk reproduksi crash saat play
            if (frame < 0)
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
            _frame = _clip != null ? Mathf.Clamp(frame, 0, _clip.FrameCount - 1) : 0;

            for (int k = 0; k < 10; k++) yield return new WaitForEndOfFrame();
            if (_vrmMode && _retarget != null && _retarget.Ready) _retarget.Apply(_frame);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(outPng);
            Debug.Log("[BvhCapture] shot(frame=" + _frame + ", vrm=" + _vrmMode + ") → " + outPng);
            for (int k = 0; k < 30; k++) yield return new WaitForEndOfFrame();
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
            _vrmMode = true;
            _retarget = new BvhRetargeter();
            // framing kamera untuk karakter setinggi ~1.5m di origin
            _center = new Vector3(0f, 0.9f, 0f);
            _fitScale = 1.8f;
            _dist = 2.6f; _pitch = 6f; _yaw = 0f;   // karakter di-auto-hadap -Z → kamera ini lihat depan
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

        private void EnsureStyles()
        {
            if (_styles) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.gray } };
            _styles = true;
        }

        private void DrawList()
        {
            float W = Screen.width, H = Screen.height;
            GUI.Box(new Rect(0, 0, W, H), GUIContent.none);
            GUILayout.BeginArea(new Rect(16, 14, W - 32, H - 28));

            GUILayout.BeginHorizontal();
            GUILayout.Label("BVH Browser — Bandai mocap", _title);
            GUILayout.FlexibleSpace();
            bool fo = GUILayout.Toggle(_favOnly, $"  ⭐ {_fav.Count}", GUILayout.Height(24));
            if (fo != _favOnly) { _favOnly = fo; RebuildShown(); }
            if (GUILayout.Button("Export favorites.txt", GUILayout.Height(24), GUILayout.Width(160)))
            {
                SaveFavorites();
                Debug.Log("[BvhBrowser] favorites → " + _favPath);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cari:", GUILayout.Width(38));
            string q = GUILayout.TextField(_query, GUILayout.Width(360));
            if (q != _query) { _query = q; RebuildShown(); }
            GUILayout.Label($"{_shown.Count}/{_files.Count} file  ·  favorit tersimpan di: {_favPath}", _small);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (_files.Count == 0)
                GUILayout.Label($"Dataset kosong di:\n{_dir}\n(taruh .bvh di sini atau isi bvh_dir.txt di folder app)", _small);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (int idx in _shown)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                bool isFav = _fav.Contains(FileNameOf(idx));
                bool nf = GUILayout.Toggle(isFav, "", GUILayout.Width(20));
                if (nf != isFav) { if (nf) _fav.Add(FileNameOf(idx)); else _fav.Remove(FileNameOf(idx)); SaveFavorites(); }
                GUILayout.Label(isFav ? "★" : "☆", GUILayout.Width(16));
                if (GUILayout.Button(NameOf(idx), _row, GUILayout.ExpandWidth(true))) OpenClip(idx);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawPlayer()
        {
            float W = Screen.width, H = Screen.height;
            // top bar
            GUILayout.BeginArea(new Rect(12, 10, W - 24, 34));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← Kembali", GUILayout.Width(100), GUILayout.Height(26))) { _selected = -1; _clip = null; }
            GUILayout.Label("  " + NameOf(_selected), _title, GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            bool isFav = _fav.Contains(FileNameOf(_selected));
            bool nf = GUILayout.Toggle(isFav, isFav ? " ★ Favorit" : " ☆ Favorit", GUILayout.Height(26), GUILayout.Width(110));
            if (nf != isFav) { if (nf) _fav.Add(FileNameOf(_selected)); else _fav.Remove(FileNameOf(_selected)); SaveFavorites(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // orbit drag area (tengah layar) — pakai event mouse
            HandleOrbit(new Rect(0, 44, W, H - 100));

            if (_error != null)
                GUI.Label(new Rect(20, 60, W - 40, 40), "Parse error: " + _error, _row);

            // bottom controls
            GUILayout.BeginArea(new Rect(12, H - 44, W - 24, 36));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⏮", GUILayout.Width(44), GUILayout.Height(28))) Step(-1);
            if (GUILayout.Button(_playing ? "⏸" : "▶", GUILayout.Width(44), GUILayout.Height(28))) _playing = !_playing;
            if (GUILayout.Button("⏭", GUILayout.Width(44), GUILayout.Height(28))) Step(1);
            GUILayout.Label("Speed", GUILayout.Width(46));
            _speed = GUILayout.HorizontalSlider(_speed, 0.25f, 3f, GUILayout.Width(200));
            int fc = _clip != null ? _clip.FrameCount : 0;
            GUILayout.Label($"{_speed:0.00}x   f:{_frame}/{fc}", _small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
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
