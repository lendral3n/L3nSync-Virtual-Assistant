using UnityEngine;
using VRMAssistant.Core;
using VRMAssistant.Animation;

namespace VRMAssistant.Behavior
{
    /// <summary>
    /// Fitur "rasa hidup" (FlowMinds-inspired), semua di C# (jalan Mac + nanti Android):
    ///   - LOOK-AT kursor: kepala/mata Lia mengikuti kursor; kalau kursor lama nempel di
    ///     wajah → salah tingkah (lirik menghindar sebentar).
    ///   - KEJAR KURSOR: goyang kursor cepat (shake) → Lia jalan mendekat.
    ///   - SPAWN VFX: lingkaran sihir muncul di kaki saat Lia pertama muncul.
    /// Pipi-merah-saat-dielus ada di CommandReceiver.OnTapReaction (dipicu tap/hit-test).
    ///
    /// Auto-attach via RuntimeInitializeOnLoadMethod. Sumber kursor: LiaInput.ScreenPos
    /// (di-set MacDesktopController saat overlay) atau Input.mousePosition (window normal/editor).
    /// </summary>
    public class AlivenessController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if BVH_BROWSER
            return;   // build BvhBrowser terpisah — jangan auto-jalankan komponen LiaVA
#endif
            var go = new GameObject("AlivenessController");
            go.AddComponent<AlivenessController>();
            DontDestroyOnLoad(go);
        }

        private VRMModelLoader _loader;
        private CharacterMovementController _move;
        private Camera _cam;
        private bool _vfxSpawned;

        // Cursor-chase (shake detect)
        private Vector2 _lastCursor;
        private float _lastDirX;
        private int _reversals;
        private float _shakeWindowStart;
        private float _nextChaseAllowed;

        // Footstep VFX
        private float _nextStep;
        private bool _leftFoot;

        private void Start()
        {
            _cam = Camera.main;
        }

        private void Resolve()
        {
            if (_loader == null) _loader = FindAnyObjectByType<VRMModelLoader>();
            if (_move == null) _move = FindAnyObjectByType<CharacterMovementController>();
            if (_cam == null) _cam = Camera.main;
        }

        private Vector2 GetCursor()
        {
            if (LiaInput.ScreenPos.HasValue) return LiaInput.ScreenPos.Value;
            return Input.mousePosition;
        }

        private void Update()
        {
            Resolve();
            if (_cam == null) return;

            SpawnVfxOnce();

            // Cursor-following DIHAPUS (keputusan Lendra): kepala pakai eye-saccade natural
            // (dari LookAtController state Idle), badan hadap depan saat diam / arah jalan saat jalan.
            // Cursor tetap dibaca HANYA untuk fitur kejar-kursor (shake → mendekat).
            Vector2 cursor = GetCursor();
            DetectShakeChase(cursor);
            SpawnFootstepIfWalking();

            _lastCursor = cursor;
        }

        // ---------- Efek pijakan kaki saat jalan ----------
        private void SpawnFootstepIfWalking()
        {
            if (_move == null || !_move.IsWalking || _loader == null || _loader.LoadedModel == null) return;
            if (Time.time < _nextStep) return;
            _nextStep = Time.time + 0.42f;   // ritme langkah

            var t = _loader.LoadedModel.transform;
            float side = _leftFoot ? -0.06f : 0.06f;
            _leftFoot = !_leftFoot;
            // offset lateral relatif arah hadap karakter (kaki kiri/kanan)
            Vector3 footPos = t.position + t.right * side + Vector3.up * 0.02f;

            var go = new GameObject("LiaFootstep");
            go.transform.position = footPos;
            go.AddComponent<FootstepVfx>();
        }

        // ---------- KEJAR KURSOR (shake → mendekat) ----------
        private void DetectShakeChase(Vector2 cursor)
        {
            float dx = cursor.x - _lastCursor.x;
            float speed = (cursor - _lastCursor).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);

            // Hitung pembalikan arah horizontal saat gerak cepat (ciri "goyang").
            if (speed > 900f)
            {
                float dir = Mathf.Sign(dx);
                if (dir != 0 && dir != _lastDirX)
                {
                    if (Time.time - _shakeWindowStart > 0.6f) { _reversals = 0; _shakeWindowStart = Time.time; }
                    _reversals++;
                    _lastDirX = dir;
                }
            }

            if (_reversals >= 4 && Time.time > _nextChaseAllowed)
            {
                _reversals = 0;
                _nextChaseAllowed = Time.time + 3f;
                ChaseCursor(cursor);
            }
        }

        private void ChaseCursor(Vector2 cursor)
        {
            if (_move == null || _loader == null || _loader.LoadedModel == null) return;
            float depth = Mathf.Abs(_cam.transform.position.z - _loader.LoadedModel.transform.position.z);
            if (depth < 0.3f) depth = 1.6f;
            Vector3 world = _cam.ScreenToWorldPoint(new Vector3(cursor.x, cursor.y, depth));
            world.y = _move.CurrentPosition.y;
            _move.WalkTo(world);
            Debug.Log("[Aliveness] Kursor digoyang → Lia mengejar kursor");
        }

        // ---------- SPAWN VFX (magic circle) ----------
        private void SpawnVfxOnce()
        {
            if (_vfxSpawned || _loader == null || _loader.LoadedModel == null) return;
            _vfxSpawned = true;
            var feet = _loader.LoadedModel.transform.position;
            var go = new GameObject("LiaSpawnVFX");
            go.transform.position = feet + Vector3.up * 0.02f;
            go.AddComponent<MagicCircleVfx>();
        }
    }

    /// <summary>Lingkaran sihir prosedural (LineRenderer) — membesar + fade, lalu hancur. Tanpa aset.</summary>
    public class MagicCircleVfx : MonoBehaviour
    {
        private LineRenderer _lr;
        private float _t;
        private const float DUR = 1.3f;
        private const int SEG = 64;
        private Color _col = new Color(0.4f, 0.8f, 1f, 1f);

        private void Start()
        {
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.positionCount = SEG;
            _lr.widthMultiplier = 0.03f;
            _lr.numCapVertices = 2;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _lr.material = new Material(sh);
            _lr.material.color = _col;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f); // datar di lantai
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = _t / DUR;
            if (k >= 1f) { Destroy(gameObject); return; }

            float radius = Mathf.Lerp(0.05f, 0.55f, Mathf.SmoothStep(0, 1, k));
            for (int i = 0; i < SEG; i++)
            {
                float a = (i / (float)SEG) * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            transform.Rotate(0f, 0f, 90f * Time.deltaTime); // berputar
            var c = _col; c.a = 1f - k;                      // fade out
            if (_lr.material != null) _lr.material.color = c;
        }
    }

    /// <summary>Riak kecil di lantai saat kaki menyentuh (efek jalan kaki). Cepat + subtle.</summary>
    public class FootstepVfx : MonoBehaviour
    {
        private LineRenderer _lr;
        private float _t;
        private const float DUR = 0.4f;
        private const int SEG = 32;
        private Color _col = new Color(0.7f, 0.85f, 1f, 0.7f);

        private void Start()
        {
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.positionCount = SEG;
            _lr.widthMultiplier = 0.012f;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _lr.material = new Material(sh);
            _lr.material.color = _col;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f); // datar di lantai
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = _t / DUR;
            if (k >= 1f) { Destroy(gameObject); return; }
            float radius = Mathf.Lerp(0.02f, 0.16f, k);
            for (int i = 0; i < SEG; i++)
            {
                float a = (i / (float)SEG) * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            var c = _col; c.a = 0.7f * (1f - k);
            if (_lr.material != null) _lr.material.color = c;
        }
    }

    /// <summary>Holder kursor global (Unity screen px) + status mode overlay.</summary>
    public static class LiaInput
    {
        public static Vector2? ScreenPos;
        // true saat mode overlay transparan aktif → panel UI (setting/chat) disembunyikan.
        public static bool OverlayActive;
    }
}
