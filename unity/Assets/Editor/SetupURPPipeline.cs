using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Editor utility untuk setup URP pipeline + project settings VRM Assistant Android.
/// Trigger: Menu VRMAssistant > Setup URP Pipeline.
///
/// PENTING: Unity factory `UniversalRenderPipelineAsset.Create()` kadang menghasilkan
/// asset tanpa m_Script reference saat dipanggil dari context selain menu native Unity.
/// Script ini menggunakan SerializedObject untuk menjamin m_Script ter-link dengan benar.
/// </summary>
public static class SetupURPPipeline
{
    private const string SettingsFolder = "Assets/Settings";
    private const string RendererDataPath = "Assets/Settings/LiaVA_RendererData.asset";
    private const string PipelineAssetPath = "Assets/Settings/LiaVA_URPAsset.asset";

    [MenuItem("VRMAssistant/Setup URP Pipeline")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
            AssetDatabase.CreateFolder("Assets", "Settings");

        // 1. Buat UniversalRendererData via factory + ensure m_Script ter-set
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, RendererDataPath);
        EnsureMonoScriptLink(rendererData, RendererDataPath);

        // 2. Buat UniversalRenderPipelineAsset via factory + ensure m_Script ter-set
        var pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
        EnsureMonoScriptLink(pipelineAsset, PipelineAssetPath);

        // 3. Konfigurasi pipeline asset via SerializedObject (paling reliable)
        var so = new SerializedObject(pipelineAsset);
        var renderScale = so.FindProperty("m_RenderScale");
        if (renderScale != null) renderScale.floatValue = 1.0f;
        var supportsHDR = so.FindProperty("m_SupportsHDR");
        if (supportsHDR != null) supportsHDR.boolValue = false;
        var msaa = so.FindProperty("m_MSAA");
        if (msaa != null) msaa.intValue = 1;
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 4. Assign ke GraphicsSettings + semua Quality levels
        GraphicsSettings.defaultRenderPipeline = pipelineAsset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipelineAsset;
        }

        // 5. Project Settings — Rendering
        PlayerSettings.preserveFramebufferAlpha = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // 6. Project Settings — Android
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.l3n.liaVA");
        PlayerSettings.productName = "Lia VA";
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // 7. UaaL
        EditorUserBuildSettings.exportAsGoogleAndroidProject = true;

        AssetDatabase.SaveAssets();

        Debug.Log("[VRMAssistant] URP Pipeline setup selesai!");
        Debug.Log($"[VRMAssistant] Pipeline asset: {PipelineAssetPath}");
        Debug.Log("[VRMAssistant] preserveFramebufferAlpha=true, colorSpace=Linear, exportAsGoogleAndroidProject=true");
        Debug.Log("[VRMAssistant] Android: bundleID=com.l3n.liaVA, minSDK=26, IL2CPP, ARM64");
    }

    /// <summary>
    /// Force-link m_Script reference pada ScriptableObject asset.
    /// Workaround untuk Unity 6 URP 17.x di mana asset kadang dibuat tanpa script link.
    /// </summary>
    private static void EnsureMonoScriptLink(ScriptableObject obj, string path)
    {
        var so = new SerializedObject(obj);
        var scriptProp = so.FindProperty("m_Script");
        if (scriptProp == null) return;

        var monoScript = MonoScript.FromScriptableObject(obj);
        if (monoScript != null)
        {
            scriptProp.objectReferenceValue = monoScript;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            AssetDatabase.SaveAssetIfDirty(obj);
            Debug.Log($"[VRMAssistant] m_Script linked: {path} → {monoScript.name}");
        }
        else
        {
            Debug.LogWarning($"[VRMAssistant] Tidak bisa resolve MonoScript untuk {path}");
        }
    }
}
