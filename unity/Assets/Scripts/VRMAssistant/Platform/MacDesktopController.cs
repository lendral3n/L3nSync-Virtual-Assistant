using UnityEngine;
using VRMAssistant.Core;
using VRMAssistant.Behavior;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace VRMAssistant.Platform
{
    /// <summary>
    /// Desktop-mascot mode untuk macOS standalone. Mengubah jendela Unity jadi overlay
    /// transparan fullscreen selalu-di-atas (via plugin native LiaWindow.bundle), lalu:
    ///   - Lia roam ujung-ke-ujung di "lantai" bawah layar (CharacterMovementController).
    ///   - HIT-TEST: klik tembus ke desktop di area kosong, TAPI jendela menangkap klik
    ///     saat kursor di atas Lia → Lia selalu bisa disentuh, area kosong tidak nge-block.
    ///   - Tap di Lia → reaksi (CommandReceiver.OnTapReaction).
    ///
    /// Auto-dibuat via RuntimeInitializeOnLoadMethod (tanpa perlu edit scene). Di Editor /
    /// platform non-macOS: no-op total (tidak ada plugin/overlay).
    /// </summary>
    public class MacDesktopController : MonoBehaviour
    {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        const string DLL = "LiaWindow";
        [DllImport(DLL)] static extern void LiaWindow_MakeOverlay();
        [DllImport(DLL)] static extern void LiaWindow_KeepTransparent();
        [DllImport(DLL)] static extern void LiaWindow_SetClickThrough(int enabled);
        [DllImport(DLL)] static extern float LiaWindow_FreeFloorX(float bandTopFromTop);
        [DllImport(DLL)] static extern float LiaWindow_MouseX();
        [DllImport(DLL)] static extern float LiaWindow_MouseY();
        [DllImport(DLL)] static extern float LiaWindow_ScreenWidth();
        [DllImport(DLL)] static extern float LiaWindow_ScreenHeight();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("MacDesktopController");
            go.AddComponent<MacDesktopController>();
            DontDestroyOnLoad(go);
        }

        private VRMModelLoader _loader;
        private CharacterMovementController _movement;
        private Component _commandReceiver;         // via reflection (assembly sama)
        private System.Reflection.MethodInfo _onTap;

        private Camera _cam;
        private Renderer[] _renderers;
        private bool _overlayReady;
        private bool _clickThrough = true;          // default: tembus
        private float _mouseScale = 1f;             // points(macOS) → pixels(Unity Retina)
        private float _hitPadPx = 36f;              // perluasan area sentuh biar gampang
        private float _lastRecompute;
        private GameObject _scaledModel;            // model yang sudah di-skala
        private float _appliedScale = -1f;
        private float _scaleT;                      // timer animasi grow-in
        private float _nextRoam;                    // waktu roam berikutnya (window-aware)

        private void Start()
        {
            Application.runInBackground = true;     // WAJIB: mascot tetap animasi walau tidak fokus
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;      // gerakan mulus 120Hz (sesuai permintaan)

            _cam = Camera.main;
            EnforceTransparentCamera();

            // Tunggu jendela Unity benar-benar ada sebelum diubah jadi overlay.
            Invoke(nameof(ApplyOverlay), 0.6f);
        }

        private void EnforceTransparentCamera()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // alpha 0 → tembus ke desktop
            _cam.allowHDR = false;                              // HDR bisa clobber alpha di URP
        }

        private void ApplyOverlay()
        {
            // Overlay transparan bisa di-toggle tanpa rebuild:
            //   defaults write "unity.DefaultCompany.Lia VA" liava_overlay -int 1
            // Default OFF supaya smoothness bisa diuji di window normal dulu.
            if (PlayerPrefs.GetInt("liava_overlay", 0) != 1)
            {
                Debug.Log("[MacDesktop] Overlay OFF (window normal). Set liava_overlay=1 untuk transparan.");
                return;
            }

            // WAJIB windowed sebelum overlay — plugin set borderless; kalau fullscreen → crash.
            Screen.fullScreenMode = FullScreenMode.Windowed;
            LiaWindow_MakeOverlay();
            _overlayReady = true;
            // Beritahu panel UI: mode overlay → sembunyikan (config/chat cuma di app normal).
            VRMAssistant.Behavior.LiaInput.OverlayActive = true;
            // WAJIB set click-through ON dari awal — default NSWindow menangkap SEMUA klik
            // (ignoresMouseEvents=NO), bikin desktop tak bisa diklik sampai kursor pernah
            // lewat Lia. Set eksplisit supaya area kosong langsung tembus ke desktop.
            _clickThrough = true;
            LiaWindow_SetClickThrough(1);

            float pw = LiaWindow_ScreenWidth();
            if (pw > 1f) _mouseScale = Screen.width / pw;       // Retina: pixels/points

            // Fullscreen → bounds gerak berubah; recompute supaya roam selebar layar.
            ResolveDeps();
            if (_movement != null) _movement.RecomputeBoundsFromCamera();

            Debug.Log($"[MacDesktop] Overlay aktif. mouseScale={_mouseScale:F2} " +
                      $"screen={Screen.width}x{Screen.height}px");
        }

        private void ResolveDeps()
        {
            if (_loader == null) _loader = FindAnyObjectByType<VRMModelLoader>();
            if (_movement == null) _movement = FindAnyObjectByType<CharacterMovementController>();
            if (_commandReceiver == null)
            {
                var t = System.Type.GetType("VRMAssistant.AI.CommandReceiver, Assembly-CSharp");
                if (t != null)
                {
                    _commandReceiver = FindAnyObjectByType(t) as Component;
                    _onTap = t.GetMethod("OnTapReaction");
                }
            }
            if (_renderers == null && _loader != null && _loader.LoadedModel != null)
                _renderers = _loader.LoadedModel.GetComponentsInChildren<Renderer>();
        }

        private void Update()
        {
            if (!_overlayReady) return;

            // Re-assert transparansi tiap frame — layer Metal Unity dibuat setelah frame
            // pertama & bisa di-reset opaque tiap frame, jadi harus terus di-set.
            LiaWindow_KeepTransparent();

            ResolveDeps();
            if (_cam == null) { _cam = Camera.main; return; }

            ApplyMascotScaleIfNeeded();  // kecilkan Lia di mode overlay (mascot bawah layar)
            RoamToFreeSpotIfDue();       // jalan sendiri ke area kosong (atas wallpaper)

            // Model bisa re-load (switch karakter) → refresh renderer list.
            if ((_renderers == null || _renderers.Length == 0) && _loader != null && _loader.LoadedModel != null)
                _renderers = _loader.LoadedModel.GetComponentsInChildren<Renderer>();

            // Kursor global (points, origin bawah-kiri) → pixel Unity.
            float mx = LiaWindow_MouseX() * _mouseScale;
            float my = LiaWindow_MouseY() * _mouseScale;

            // Publish ke AlivenessController (look-at/kejar kursor) — Unity tak dapat mouse
            // sendiri saat overlay click-through.
            VRMAssistant.Behavior.LiaInput.ScreenPos = new Vector2(mx, my);

            // Overlay = karakter bersih (tanpa UI), jadi capture HANYA di atas Lia.
            // Area kosong tetap click-through ke desktop.
            bool over = CursorOverLia(mx, my);

            // Toggle click-through HANYA saat berubah (hindari flood main-queue).
            if (over == _clickThrough)   // over=true → butuh capture (clickThrough=false)
            {
                _clickThrough = !over;
                LiaWindow_SetClickThrough(_clickThrough ? 1 : 0);
            }

            // Saat kursor di Lia & jendela menangkap event → klik = reaksi sentuh.
            if (over && Input.GetMouseButtonDown(0))
            {
                _onTap?.Invoke(_commandReceiver, new object[] { "" });
            }
        }

        /// <summary>AABB layar dari semua renderer Lia + padding → true kalau kursor di dalamnya.</summary>
        private bool CursorOverLia(float px, float py)
        {
            if (_renderers == null || _renderers.Length == 0) return false;

            Bounds b = default;
            bool has = false;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) return false;

            Vector3 c = b.center, e = b.extents;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                var sp = _cam.WorldToScreenPoint(corner);
                if (sp.z < 0f) continue; // di belakang kamera
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
            }
            if (minX > maxX) return false;

            // Perkecil area tangkap ke INTI badan — AABB VRM termasuk rambut/rok/lengan
            // panjang yang menjulur, bikin box jauh lebih besar dari silhuet → klik di dekat
            // Lia (area rambut kosong) ikut ke-block. Shrink ke tengah: lebar 50%, tinggi 78%.
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            float halfW = (maxX - minX) * 0.5f * 0.50f;
            float halfH = (maxY - minY) * 0.5f * 0.78f;
            return px >= cx - halfW && px <= cx + halfW
                && py >= cy - halfH && py <= cy + halfH;
        }

        /// <summary>
        /// Kecilkan Lia di mode overlay (mascot bawah layar, bukan raksasa fullscreen).
        /// Bisa di-tune tanpa rebuild: defaults write com.l3n.liaVA liava_mascot_scale -float 0.4
        /// Skala di transform root VRM (pivot ≈ kaki → kaki tetap di lantai, tinggi mengecil).
        /// </summary>
        private const float ScaleInDur = 0.55f;

        private void ApplyMascotScaleIfNeeded()
        {
            if (_loader == null || _loader.LoadedModel == null) return;
            float target = Mathf.Clamp(PlayerPrefs.GetFloat("liava_mascot_scale", 0.4f), 0.1f, 1f);
            var t = _loader.LoadedModel.transform;

            // Model baru → mulai animasi GROW-IN halus dari kecil (hindari "terhempas": dulu
            // load skala 1.0 lalu snap ke 0.4 = seolah mengkerut jatuh). Muncul seperti summon.
            if (_loader.LoadedModel != _scaledModel)
            {
                _scaledModel = _loader.LoadedModel;
                _appliedScale = target;
                _scaleT = 0f;
                t.localScale = Vector3.one * (target * 0.02f);
                if (_movement != null) _movement.RecomputeBoundsFromCamera();
                Debug.Log($"[MacDesktop] Mascot grow-in → {target}");
            }

            if (_scaleT < ScaleInDur)
            {
                _scaleT += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_scaleT / ScaleInDur));
                t.localScale = Vector3.one * Mathf.Lerp(target * 0.02f, target, k);
            }
            else if (Mathf.Abs(t.localScale.x - target) > 0.001f)
            {
                t.localScale = Vector3.one * target;   // pref di-tune runtime → sesuaikan
                _appliedScale = target;
            }
        }

        /// <summary>
        /// Roam sadar-window: tiap ~10-16s, kalau tidak sedang jalan, Lia pindah ke celah
        /// terlebar yang BEBAS window app (di atas wallpaper) — bukan menutupi jendela.
        /// </summary>
        private void RoamToFreeSpotIfDue()
        {
            if (Time.time < _nextRoam) return;
            _nextRoam = Time.time + Random.Range(10f, 16f);
            if (_movement == null || _movement.IsWalking || _loader == null || _loader.LoadedModel == null) return;

            // Pita bawah layar (tempat Lia berdiri): 45% bawah. FreeFloorX pakai points top-left.
            float bandTop = LiaWindow_ScreenHeight() * 0.55f;
            float freeXpts = LiaWindow_FreeFloorX(bandTop);
            if (freeXpts < 0f) return; // tak ada ruang kosong → tetap di tempat

            var pos = _loader.LoadedModel.transform.position;
            Vector3 curScreen = _cam.WorldToScreenPoint(pos);         // px
            float targetXpx = freeXpts * _mouseScale;                 // points → px
            Vector3 world = _cam.ScreenToWorldPoint(new Vector3(targetXpx, curScreen.y, Mathf.Abs(curScreen.z)));

            // Sudah di dekat titik bebas → jangan "jalan kosong" (yang cuma memicu pose).
            if (Mathf.Abs(world.x - pos.x) < 0.15f) return;

            _movement.WalkTo(new Vector3(world.x, pos.y, pos.z));
            Debug.Log($"[MacDesktop] Roam ke area kosong: screenX(pts)={freeXpts:F0} → worldX={world.x:F2}");
        }
#endif
    }
}
