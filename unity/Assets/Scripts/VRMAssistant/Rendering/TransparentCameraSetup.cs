using UnityEngine;

namespace VRMAssistant.Rendering
{
    /// <summary>
    /// Setup camera untuk transparent overlay rendering di Android UaaL.
    /// Wajib: SolidColor clear + alpha 0, no HDR/MSAA/post-processing untuk performa + correctness.
    /// preserveFramebufferAlpha=true di PlayerSettings adalah pre-requisite.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TransparentCameraSetup : MonoBehaviour
    {
        [Header("Transparent Settings")]
        [Tooltip("Background RGBA. a=0 fully transparent kotak (preserveFramebufferAlpha=true menjaga character alpha=1 tetap visible). a=0.15 fallback bila framebuffer alpha tidak preserved.")]
        [SerializeField] private Color clearColor = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Enable bila ingin force apply settings setiap Awake (default true).")]
        [SerializeField] private bool applyOnAwake = true;

        private Camera _cam;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (applyOnAwake) ApplySettings();
        }

        /// <summary>Apply transparent rendering settings ke camera.</summary>
        public void ApplySettings()
        {
            if (_cam == null) _cam = GetComponent<Camera>();

            // 1. Clear flags & background.
            // FORCE alpha=0 regardless of serialized clearColor — defense terhadap scene
            // serialization yang kadang ke-revert ke alpha 0.15 saat Editor reload.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            var forcedClear = new Color(clearColor.r, clearColor.g, clearColor.b, 0f);
            _cam.backgroundColor = forcedClear;

            // 2. Disable HDR (alpha tidak preserved di HDR di Android)
            _cam.allowHDR = false;

            // 3. Disable MSAA (Android transparent layer tidak butuh, hemat fillrate)
            _cam.allowMSAA = false;

            // 4. Disable Dynamic Resolution (untuk konsistensi alpha)
            _cam.allowDynamicResolution = false;

            // 5. Render mode standard
            _cam.depthTextureMode = DepthTextureMode.None;

            Debug.Log($"[TransparentCameraSetup] Camera '{_cam.name}' configured for transparent overlay.");
        }
    }
}
