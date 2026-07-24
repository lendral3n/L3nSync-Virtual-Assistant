using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VRM;

namespace VRMAssistant.Core
{
    /// <summary>
    /// Async loader VRM model. Mendukung Android (UnityWebRequest) dan Editor (direct path).
    /// Fire OnModelLoaded setelah VRM berhasil di-load + ShowMeshes selesai.
    /// </summary>
    public class VRMModelLoader : MonoBehaviour
    {
        [Header("VRM Settings")]
        // Default = Kohaku kimono putih-biru (pilihan Lendra). "dress" = varian hitam.
        [SerializeField] private string vrmFileName = "Kohaku_1.10_VRM.vrm";
        [SerializeField] private bool loadOnStart = true;

        // Karakter yang tersedia di StreamingAssets — alias pendek untuk command dari Kotlin
        private static readonly System.Collections.Generic.Dictionary<string, string> CharacterAliases =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "dress", "Kohaku_dress_1.10_VRM.vrm" },
                { "kimono", "Kohaku_1.10_VRM.vrm" },
            };

        private const string PrefsKeyCharacter = "liava_character_file";

        public GameObject LoadedModel { get; private set; }
        public Animator ModelAnimator { get; private set; }
        public VRMBlendShapeProxy BlendShapeProxy { get; private set; }

        public event Action<GameObject> OnModelLoaded;
        public event Action<string> OnLoadFailed;

        private bool _isLoading;

        private async void Start()
        {
            // Target 120fps untuk gerakan halus di display high-refresh (Xiaomi 17 Pro Max 120Hz).
            // vSync off supaya targetFrameRate berlaku di Android. Trade-off battery didokumentasi README.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;

            // Karakter default = kimono (pilihan Lendra). Dipaksa di sini supaya nilai
            // serialized di scene TIDAK bisa meng-override balik ke dress. Kalau user
            // pernah switch, pilihan terakhir dipulihkan dari PlayerPrefs.
            var saved = PlayerPrefs.GetString(PrefsKeyCharacter, "");
            vrmFileName = !string.IsNullOrEmpty(saved) ? saved : "Kohaku_1.10_VRM.vrm";

            if (loadOnStart) await LoadModelAsync();
        }

        /// <summary>
        /// Ganti karakter saat runtime. Menerima alias ("dress"/"kimono") atau nama file .vrm.
        /// Model lama di-destroy, model baru di-load, OnModelLoaded fire ulang sehingga
        /// semua controller (orchestrator, VMD, VRMA, movement) re-wire otomatis.
        /// </summary>
        public async Task SwitchCharacterAsync(string nameOrFile)
        {
            if (_isLoading) { Debug.LogWarning("[VRMModelLoader] Masih loading, switch diabaikan"); return; }

            string file = CharacterAliases.TryGetValue(nameOrFile?.Trim() ?? "", out var mapped)
                ? mapped : nameOrFile;
            if (string.IsNullOrEmpty(file)) return;
            if (LoadedModel != null && file == vrmFileName)
            {
                Debug.Log("[VRMModelLoader] Karakter sudah aktif: " + file);
                return;
            }

            vrmFileName = file;
            PlayerPrefs.SetString(PrefsKeyCharacter, file);
            PlayerPrefs.Save();

            if (LoadedModel != null)
            {
                Debug.Log("[VRMModelLoader] Destroy model lama untuk switch → " + file);
                Destroy(LoadedModel);
                LoadedModel = null;
                ModelAnimator = null;
                BlendShapeProxy = null;
            }

            await LoadModelAsync();
        }

        public async Task LoadModelAsync()
        {
            _isLoading = true;
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, vrmFileName);
                byte[] bytes;

#if UNITY_ANDROID && !UNITY_EDITOR
                bytes = await LoadBytesAndroidAsync(path);
#else
                if (!File.Exists(path))
                {
                    string err = $"[VRMModelLoader] File tidak ditemukan: {path}";
                    Debug.LogError(err);
                    OnLoadFailed?.Invoke(err);
                    return;
                }
                bytes = File.ReadAllBytes(path);
#endif

                Debug.Log($"[VRMModelLoader] Loaded {bytes.Length} bytes, parsing VRM...");

                // UniVRM 0.131 API — pakai VrmUtility.LoadBytesAsync untuk runtime VRM loading.
                // Mengembalikan RuntimeGltfInstance dengan ShowMeshes() + Root accessor.
                var instance = await VrmUtility.LoadBytesAsync(path, bytes, awaitCaller: new UniGLTF.RuntimeOnlyAwaitCaller());
                instance.ShowMeshes();

                LoadedModel = instance.Root;
                LoadedModel.transform.SetParent(transform, false);
                LoadedModel.transform.localPosition = Vector3.zero;
                LoadedModel.transform.localRotation = Quaternion.identity;

                // URP shader fallback: VRM ship dengan MToon shader Built-in only,
                // di URP pipeline akan render magenta. Convert ke URP/Lit pakai texture VRM.
                ConvertMaterialsToURP(LoadedModel);

                ModelAnimator = LoadedModel.GetComponent<Animator>();
                BlendShapeProxy = LoadedModel.GetComponent<VRMBlendShapeProxy>();

                Debug.Log($"[VRMModelLoader] VRM loaded: {LoadedModel.name}");
                OnModelLoaded?.Invoke(LoadedModel);
            }
            catch (Exception e)
            {
                string err = $"[VRMModelLoader] Gagal load: {e.Message}";
                Debug.LogError(err);
                OnLoadFailed?.Invoke(err);
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Convert MToon (Built-in only) → URP/Unlit untuk VRM yang di-load runtime di URP pipeline.
        ///
        /// SEMUA material pakai Unlit (bukan Lit) + shadow cast/receive OFF:
        /// model anime punya shading yang sudah di-bake di tekstur; URP/Lit menambah
        /// bayangan dinamis (rambut men-shadow wajah, gradasi N·L ikut gerakan kepala)
        /// yang terlihat kasar/jelek — fix 2026-07-22, verified visual.
        /// </summary>
        private void ConvertMaterialsToURP(GameObject model)
        {
            var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpUnlit == null)
            {
                Debug.LogWarning("[VRMModelLoader] URP/Unlit shader tidak ditemukan, skip conversion.");
                return;
            }
            // Shader opaque yang memaksa alpha=1 → badan tetap terlihat di overlay transparan.
            // Fallback ke URP/Unlit kalau custom shader tidak ke-build.
            var solidUnlit = Shader.Find("LiaVA/UnlitSolid") ?? urpUnlit;

            int converted = 0;
            int outlineDisabled = 0;
            int opaqueSolid = 0, transparentCount = 0;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[VRMModelLoader] solidUnlit shader = {(solidUnlit != null ? solidUnlit.name : "NULL")}");

            foreach (var rend in renderers)
            {
                // Matikan dynamic shadow — anime model shading-nya baked di tekstur
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;

                // Skip MToon outline pass — second material di renderer punya shader VRM/MToonOutline
                // yang tidak punya equivalent URP, lebih baik disable outline pass dengan trim materials
                var mats = rend.sharedMaterials;
                bool hasOutline = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var shaderName = m.shader != null ? m.shader.name : "";

                    if (shaderName.Contains("Outline"))
                    {
                        hasOutline = true;
                        continue;
                    }

                    // Capture properti texture utama dari MToon sebelum ganti shader
                    Texture mainTex = null;
                    Color baseColor = Color.white;

                    if (m.HasProperty("_MainTex")) mainTex = m.GetTexture("_MainTex");
                    if (mainTex == null && m.HasProperty("_BaseMap")) mainTex = m.GetTexture("_BaseMap");
                    if (m.HasProperty("_Color")) baseColor = m.GetColor("_Color");

                    bool isTransparent = m.HasProperty("_Mode") && m.GetFloat("_Mode") > 0.5f;
                    isTransparent |= shaderName.ToLower().Contains("transparent");

                    // Opaque → shader solid (alpha dipaksa 1, terlihat di overlay transparan).
                    // Transparent (rambut/rok tepi) → URP/Unlit transparent untuk soft edge.
                    m.shader = isTransparent ? urpUnlit : solidUnlit;
                    if (isTransparent) transparentCount++; else opaqueSolid++;

                    if (mainTex != null) m.SetTexture("_BaseMap", mainTex);
                    m.SetColor("_BaseColor", new Color(baseColor.r, baseColor.g, baseColor.b, 1.0f));

                    if (isTransparent)
                    {
                        m.SetFloat("_Surface", 1f); // Transparent
                        m.SetOverrideTag("RenderType", "Transparent");
                        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        m.SetInt("_ZWrite", 0);
                        m.SetInt("_ColorMask", 15); // Ensure alpha is written
                        m.renderQueue = 3000;
                        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    }
                    else
                    {
                        // Opaque path: Force alpha writing to 1.0
                        m.SetFloat("_Surface", 0f); // Opaque
                        m.SetOverrideTag("RenderType", "Opaque");
                        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        m.SetInt("_ZWrite", 1);
                        m.SetInt("_ColorMask", 15); // Ensure alpha is written
                        m.renderQueue = 2000;
                        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        m.DisableKeyword("_ALPHATEST_ON");
                        m.DisableKeyword("_ALPHABLEND_ON");
                        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    }

                    converted++;
                }

                // Trim outline pass material slot kalau ada
                if (hasOutline)
                {
                    var trimmed = new System.Collections.Generic.List<Material>();
                    foreach (var m in mats)
                    {
                        if (m == null || (m.shader != null && m.shader.name.Contains("Outline"))) continue;
                        trimmed.Add(m);
                    }
                    rend.sharedMaterials = trimmed.ToArray();
                    outlineDisabled++;
                }
            }

            Debug.Log($"[VRMModelLoader] URP shader conversion: {converted} materials converted " +
                      $"(opaque-solid={opaqueSolid}, transparent={transparentCount}), {outlineDisabled} outline pass disabled.");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private Task<byte[]> LoadBytesAndroidAsync(string path)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            StartCoroutine(LoadBytesCoroutine(path, tcs));
            return tcs.Task;
        }

        private System.Collections.IEnumerator LoadBytesCoroutine(
            string path, TaskCompletionSource<byte[]> tcs)
        {
            using (var req = UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    tcs.SetException(new Exception(req.error));
                else
                    tcs.SetResult(req.downloadHandler.data);
            }
        }
#endif
    }
}
