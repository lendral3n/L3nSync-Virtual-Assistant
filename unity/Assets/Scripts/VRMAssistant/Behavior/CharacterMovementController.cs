using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Behavior
{
    /// <summary>
    /// Karakter Unity bergerak di world space (BUKAN window pindah).
    /// Camera fixed full-screen overlay, character walk dari point ke point.
    ///
    /// Coordinate convention:
    /// - Camera at (0, 1.05, 1.7) facing -Z, FOV 42°, ortho-feel via depth
    /// - Character spawned at world (0, 0, 0)
    /// - Wander targets: relative ke spawn point dalam world units
    /// - Screen coverage di-clamp via worldExtent (max walk distance)
    /// </summary>
    public class CharacterMovementController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private VRMModelLoader modelLoader;
        [SerializeField] private AssistantStateManager stateManager;

        [Header("World Movement (Phase 2.6 FULL mode — bounds dynamic dari kamera)")]
        [Tooltip("Initial fallback extent (X horizontal). Akan di-overwrite saat camera siap via RecomputeBoundsFromCamera().")]
        [SerializeField] private float worldExtentX = 0.55f;
        [Tooltip("Z extent (depth). 0 = walk pada plane horizontal, no forward/back motion.")]
        [SerializeField] private float worldExtentY = 0f;
        [SerializeField] private float walkSpeed = 0.4f;
        [SerializeField] private float turnSpeed = 4f;
        [Tooltip("Auto-compute bounds dari camera frustum (ScreenToWorldPoint). Recommended ON.")]
        [SerializeField] private bool autoComputeBounds = true;
        [Tooltip("Margin pixel dari edge layar saat auto-compute (supaya character tidak melewati edge)")]
        [SerializeField] private int edgeMarginPx = 80;

        [Header("State")]
        [Tooltip("Saat true, karakter bergerak ke targetWorldPos. Auto false saat sampai.")]
        [SerializeField] private bool isWalking = false;
        [SerializeField] private Vector3 targetWorldPos = Vector3.zero;
        [SerializeField] private Vector3 spawnPos = Vector3.zero;

        private Transform _characterTransform;
        private Animator _animator;

        public bool IsWalking => isWalking;
        public Vector3 CurrentPosition => _characterTransform != null ? _characterTransform.position : spawnPos;

        private void Start()
        {
            if (modelLoader == null) return;
            // SELALU subscribe (bukan else-branch) supaya character switch runtime re-wire transform
            modelLoader.OnModelLoaded += OnModelLoaded;
            if (modelLoader.LoadedModel != null) OnModelLoaded(modelLoader.LoadedModel);
        }

        private void OnDestroy()
        {
            if (modelLoader != null) modelLoader.OnModelLoaded -= OnModelLoaded;
        }

        private void OnModelLoaded(GameObject model)
        {
            _characterTransform = model.transform;
            _animator = modelLoader.ModelAnimator;
            spawnPos = _characterTransform.position;
            targetWorldPos = spawnPos;

            if (autoComputeBounds) RecomputeBoundsFromCamera();

            // ClipGestureController dulu "dipensiunkan" → tidak ada di scene → FemWalk via
            // HumanPoseHandler tak tersedia (kaki diam saat jalan). Buat saat runtime supaya
            // animasi jalan bisa main lewat mekanisme yang TERBUKTI menganimasi.
            if (FindAnyObjectByType<VRMAssistant.Animation.ClipGestureController>() == null)
            {
                gameObject.AddComponent<VRMAssistant.Animation.ClipGestureController>();
                Debug.Log("[CharMovement] ClipGestureController dibuat runtime (untuk FemWalk).");
            }

            Debug.Log($"[CharMovement] Karakter siap, spawn pos: {spawnPos}, " +
                      $"extentX={worldExtentX:F2}, extentY={worldExtentY:F2}");
        }

        /// <summary>
        /// Hitung worldExtentX dari Camera frustum — sehingga character bisa walk
        /// dari edge kiri ke edge kanan layar tanpa keluar viewport.
        /// </summary>
        public void RecomputeBoundsFromCamera()
        {
            var cam = Camera.main;
            if (cam == null || _characterTransform == null) return;

            // Pakai z & y AKTUAL karakter dari WorldToScreenPoint (robust, tak menebak depth).
            // Cara lama pakai depth tebakan bikin halfWidth≈0 → extentX mentok floor 0.2
            // → Lia tak bisa roam ke tepi layar (bug 2026-07-23).
            Vector3 charScreen = cam.WorldToScreenPoint(_characterTransform.position);
            float z = Mathf.Abs(charScreen.z);
            if (z < 0.1f) z = 1.7f;

            Vector3 leftEdge = cam.ScreenToWorldPoint(new Vector3(edgeMarginPx, charScreen.y, z));
            Vector3 rightEdge = cam.ScreenToWorldPoint(new Vector3(Screen.width - edgeMarginPx, charScreen.y, z));

            float halfWidth = Mathf.Abs(rightEdge.x - leftEdge.x) / 2f;
            worldExtentX = Mathf.Max(0.2f, halfWidth);  // floor 0.2 supaya tidak mati

            Debug.Log($"[CharMovement] Bounds recomputed: extentX={worldExtentX:F2} " +
                      $"(screen {Screen.width}x{Screen.height}, z {z:F2})");
        }

        private void Update()
        {
            if (_characterTransform == null) return;
            if (!isWalking)
            {
                // Saat diam/gestur: badan hadap ke arah CURSOR (halus, dibatasi ±35° supaya
                // tetap menghadap depan, tidak memunggungi). Kalau cursor tak ada → hadap depan.
                Quaternion desired = DesiredIdleFacing();
                if (Quaternion.Angle(_characterTransform.rotation, desired) > 0.3f)
                {
                    _characterTransform.rotation = Quaternion.Slerp(
                        _characterTransform.rotation, desired,
                        1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
                }
                return;
            }

            // BULLETPROOF tiap frame: FemWalk HARUS menguasai badan; batalkan VRMA yang menyela.
            if (_vrma != null && _vrma.IsPlaying) _vrma.StopGesture();
            if (_usingClipWalk)
            {
                if (_clip != null && !_clip.IsPlaying) _clip.PlayLoop("FemWalk"); // re-assert loop
                if (_animator != null && _animator.enabled) _animator.enabled = false;
            }
            else if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                if (!_animator.enabled) _animator.enabled = true;
                _animator.SetBool("Walking", true);
            }

            Vector3 pos = _characterTransform.position;
            Vector3 toTarget = targetWorldPos - pos;
            toTarget.y = 0; // walk di plane horizontal saja

            float dist = toTarget.magnitude;
            if (dist < 0.05f)
            {
                isWalking = false;
                NotifyWalkingState(false);
                return;
            }

            // Walk toward target (posisi saja)
            Vector3 dir = toTarget / dist;
            Vector3 step = dir * walkSpeed * Time.deltaTime;
            if (step.magnitude > dist) step = toTarget;
            _characterTransform.position += step;

            // HADAP arah jalan (profil kiri/kanan). walkFacingOffset mengoreksi sumbu-depan
            // VRM (VRM 0.x hadap -Z) supaya wajahnya benar ke arah gerak, bukan ke screen.
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
            if (flatDir.sqrMagnitude > 0.0001f)
            {
                // Offset bisa di-tune tanpa rebuild: defaults write com.l3n.liaVA liava_walk_facing -float 90
                float off = PlayerPrefs.GetFloat("liava_walk_facing", walkFacingOffset);
                float yawDeg = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg + off;
                Quaternion targetRot = Quaternion.Euler(0f, yawDeg, 0f);
                _characterTransform.rotation = Quaternion.Slerp(
                    _characterTransform.rotation, targetRot,
                    1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            }
        }

        [Header("Walk Facing")]
        [Tooltip("Offset derajat koreksi arah hadap saat jalan (VRM forward axis). Coba 0/90/180/-90.")]
        [SerializeField] private float walkFacingOffset = 90f;

        /// <summary>Walk ke world position spesifik (X clamped ke ±worldExtentX, Z = ±worldExtentY).</summary>
        public void WalkTo(Vector3 worldPos)
        {
            if (_characterTransform == null) return;
            // Clamp ke world extent
            worldPos.x = Mathf.Clamp(worldPos.x, spawnPos.x - worldExtentX, spawnPos.x + worldExtentX);
            worldPos.z = Mathf.Clamp(worldPos.z, spawnPos.z - worldExtentY, spawnPos.z + worldExtentY);
            worldPos.y = spawnPos.y; // tetap di plane

            targetWorldPos = worldPos;
            isWalking = true;
            NotifyWalkingState(true);
            Debug.Log($"[CharMovement] WalkTo {worldPos}");
        }

        /// <summary>Walk ke random titik dalam world extent.</summary>
        public void WalkToRandom()
        {
            float rx = Random.Range(-worldExtentX, worldExtentX);
            float rz = Random.Range(-worldExtentY, worldExtentY);
            WalkTo(spawnPos + new Vector3(rx, 0, rz));
        }

        /// <summary>Walk ke edge layar (peeking gaya Shimeji).</summary>
        public void WalkToEdge()
        {
            // Pilih left/right/center edge
            int edge = Random.Range(0, 3);
            float x = edge switch
            {
                0 => -worldExtentX,           // left edge
                1 => worldExtentX,            // right edge
                _ => Random.Range(-0.3f, 0.3f) // center
            };
            float z = Random.Range(-worldExtentY * 0.5f, worldExtentY * 0.5f);
            WalkTo(spawnPos + new Vector3(x, 0, z));
        }

        public void StopWalk()
        {
            isWalking = false;
            NotifyWalkingState(false);
        }

        /// <summary>Reset ke spawn position (tengah).</summary>
        public void ReturnHome()
        {
            WalkTo(spawnPos);
        }

        private VRMAssistant.Animation.VrmaPlaybackController _vrma;
        private VRMAssistant.Animation.ClipGestureController _clip;
        private bool _usingClipWalk;

        /// <summary>Hadap idle = DEPAN (ke arah user). Cursor-following dihapus (keputusan Lendra
        /// 2026-07-23: bikin moonwalk/ambigu; mascot lebih natural hadap depan saat diam).</summary>
        private Quaternion DesiredIdleFacing() => Quaternion.identity;

        private void NotifyWalkingState(bool walking)
        {
            if (_vrma == null) _vrma = FindAnyObjectByType<VRMAssistant.Animation.VrmaPlaybackController>();
            if (_clip == null) _clip = FindAnyObjectByType<VRMAssistant.Animation.ClipGestureController>();

            if (walking)
            {
                if (_vrma != null && _vrma.IsPlaying) _vrma.StopGesture();
                // UTAMAKAN FemWalk via ClipGestureController (HumanPoseHandler retarget —
                // TERBUKTI menganimasi badan, sama seperti gesture VRMA yang kelihatan).
                // Mecanim/LiaAnimator tidak reliable retarget FemWalk ke VRM runtime → kaki diam.
                _usingClipWalk = _clip != null && _clip.PlayLoop("FemWalk");
                if (_usingClipWalk)
                {
                    if (_animator != null) _animator.enabled = false;   // biar tak fight HumanPoseHandler
                }
                else if (_animator != null && _animator.runtimeAnimatorController != null)
                {
                    _animator.enabled = true;
                    _animator.SetBool("Walking", true);                 // fallback Mecanim
                }
                Debug.Log($"[CharMovement] Walking=True (clipWalk={_usingClipWalk})");
            }
            else
            {
                if (_usingClipWalk)
                {
                    if (_clip != null) _clip.StopLoop();
                    _usingClipWalk = false;
                }
                if (_animator != null && _animator.runtimeAnimatorController != null)
                {
                    _animator.enabled = true;                           // balik idle (LiaAnimator)
                    _animator.SetBool("Walking", false);
                }
                Debug.Log("[CharMovement] Walking=False → idle");
            }
        }
    }
}
