using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BvhBrowser.Editor
{
    /// <summary>
    /// Build BvhBrowser.app (Mac) — app terpisah dari LiaVA. Meng-set scripting define
    /// BVH_BROWSER (mengaktifkan BvhBrowserApp + menonaktifkan auto-bootstrap LiaVA),
    /// membangun scene boot kosong (semua dibuat via kode), lalu me-restore identity LiaVA.
    ///
    /// Jalankan (batchmode):
    ///   Unity -batchmode -quit -projectPath &lt;proj&gt; \
    ///     -executeMethod BvhBrowser.Editor.BvhBrowserMacBuildScript.BuildBvh -logFile &lt;log&gt;
    /// </summary>
    public static class BvhBrowserMacBuildScript
    {
        private const string OutDir = "/Users/lendra/Documents/codeV/BvhBrowser/build-mac";
        private const string AppName = "BvhBrowser.app";
        private const string BootScene = "Assets/Scenes/BvhBoot.unity";
        private const string Define = "BVH_BROWSER";

        public static void BuildBvh()
        {
            var std = NamedBuildTarget.Standalone;

            // --- simpan identity LiaVA untuk di-restore ---
            string oldDefines = PlayerSettings.GetScriptingDefineSymbols(std);
            string oldProduct = PlayerSettings.productName;
            string oldBundle = PlayerSettings.applicationIdentifier;

            try
            {
                // Backend & grafis (samakan dengan LiaVA yang terbukti jalan di Mac)
                PlayerSettings.SetScriptingBackend(std, ScriptingImplementation.Mono2x);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,
                    new[] { GraphicsDeviceType.Metal });
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultIsNativeResolution = false;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 800;
                PlayerSettings.resizableWindow = true;

                // Identity app BVH
                PlayerSettings.productName = "BvhBrowser";
                PlayerSettings.applicationIdentifier = "com.l3n.bvhbrowser";

                // Define BVH_BROWSER (mengaktifkan BvhBrowserApp, mematikan bootstrap LiaVA)
                var defs = new List<string>((oldDefines ?? "").Split(';'));
                if (!defs.Contains(Define)) defs.Add(Define);
                PlayerSettings.SetScriptingDefineSymbols(std, string.Join(";", defs));

                EnsureLineShaders();
                EnsureBootScene();

                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { BootScene },
                    locationPathName = Path.Combine(OutDir, AppName),
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                Directory.CreateDirectory(OutDir);

                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    Debug.Log($"[BvhBuild] ✓ BvhBrowser build success. {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:F1}s → {opts.locationPathName}");
                else
                    Debug.LogError($"[BvhBuild] ✗ Build {s.result} ({s.totalErrors} error)");
            }
            finally
            {
                // Restore identity LiaVA supaya build LiaVA berikutnya tak terganggu.
                PlayerSettings.SetScriptingDefineSymbols(std, oldDefines);
                PlayerSettings.productName = oldProduct;
                PlayerSettings.applicationIdentifier = oldBundle;
                AssetDatabase.SaveAssets();
            }
        }

        private static void EnsureBootScene()
        {
            if (File.Exists(BootScene)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, BootScene);
        }

        // Pastikan shader penting tak di-strip: line (Sprites/Default) + VRM/URP untuk Kohaku.
        private static void EnsureLineShaders()
        {
            var names = new[]
            {
                "Sprites/Default", "Unlit/Color",
                "LiaVA/UnlitSolid", "UniGLTF/UniUnlit", "VRM/MToon", "VRM10/MToon10",
                "Standard", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit",
            };
            var included = new List<Shader>();
            foreach (var n in names) { var sh = Shader.Find(n); if (sh != null) included.Add(sh); }

            var so = new SerializedObject(UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            foreach (var sh in included)
            {
                bool found = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { found = true; break; }
                if (!found)
                {
                    arr.arraySize++;
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                }
            }
            so.ApplyModifiedProperties();
        }
    }
}
