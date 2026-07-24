using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiaVA.Editor
{
    /// <summary>
    /// Build LiaVA sebagai app macOS standalone (desktop mascot).
    /// Source Unity SATU project dengan Android (dipakai bareng). Output .app di dalam LiaVA.
    /// Output: codeV/LiaVA/build-mac/LiaVA.app
    ///
    /// Trigger:
    /// - Menu: Lia VA > Build Mac (Desktop)
    /// - Batch: Unity -batchmode -quit -projectPath &lt;proj&gt; -executeMethod LiaVA.Editor.LiaVAMacBuildScript.BuildMac
    /// </summary>
    public static class LiaVAMacBuildScript
    {
        private const string OutputPath = "/Users/lendra/Documents/codeV/LiaVA/build-mac/LiaVA.app";
        private const string SceneToBuild = "Assets/Scenes/Main.unity";

        [MenuItem("Lia VA/Build Mac (Desktop)")]
        public static void BuildMac()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            {
                Debug.Log("[LiaVAMac] Switching platform to StandaloneOSX…");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            }

            // Mono backend → build Mac jauh lebih cepat (tanpa kompilasi IL2CPP C++).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            // microphoneUsageDescription di-set langsung di ProjectSettings.asset (WAJIB krn
            // pakai Microphone class; API PlayerSettings.microphoneUsageDescription tak ada di versi ini).

            // WAJIB windowed (bukan fullscreen macOS). Plugin overlay set styleMask=Borderless;
            // kalau window fullscreen, clear mask itu bikin app CRASH (NSGenericException).
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;

            // Izinkan HTTP polos: Ollama (gpt-oss) & STT self-host di VM Elara diakses lewat
            // http:// pada LAN ZeroTier privat (bukan internet). Tanpa ini UnityWebRequest
            // lempar "Insecure connection not allowed".
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // WAJIB Metal untuk Mac Apple Silicon (project sebelumnya di-set OpenGLCore only
            // sebagai workaround build Android — itu memblokir build Mac).
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,
                new[] { GraphicsDeviceType.Metal });

            // App icon (Mac + Android) dari Assets/Icon/AppIcon.png kalau ada.
            SetAppIcon();

            // VRMA (animation-only glTF) tetap dilewatkan MaterialFactory saat import.
            // Model utama pakai MToon, jadi shader glTF-default (UniUnlit/Standard) TIDAK
            // dipakai material manapun di scene → di-strip standalone → new Material(null)
            // → "Value cannot be null. Parameter name: Shader". Paksa masuk build.
            EnsureAlwaysIncludedShaders();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { SceneToBuild },
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = OutputPath,
                // Release: tanpa overlay Development Console on-screen (Player.log tetap ditulis
                // di ~/Library/Logs/<Company>/<Product>/Player.log untuk debugging).
                options = BuildOptions.None,
            };

            Debug.Log($"[LiaVAMac] Starting macOS build → {OutputPath}");
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                AddMicPermission(OutputPath);
                AddMacIcns(OutputPath);
                Debug.Log($"[LiaVAMac] ✓ Mac build success. {s.totalSize / 1024 / 1024} MB, {s.totalTime.TotalSeconds:F1}s → {OutputPath}");
            }
            else
                Debug.LogError($"[LiaVAMac] ✗ Mac build failed: {s.result}, {s.totalErrors} errors");
        }

        /// <summary>
        /// Generate PlayerIcon.icns dari AppIcon.png + taruh di .app (Unity referensikan
        /// PlayerIcon.icns di Info.plist tapi kadang TIDAK meng-generate file-nya di build
        /// incremental → icon jadi generic). Pakai sips + iconutil (native macOS).
        /// </summary>
        private static void AddMacIcns(string appPath)
        {
            try
            {
                string png = Path.GetFullPath("Assets/Icon/AppIcon.png");
                if (!File.Exists(png)) return;
                string dest = Path.Combine(appPath, "Contents/Resources/PlayerIcon.icns");
                string sh =
                    "set -e; ICONSET=$(mktemp -d)/Lia.iconset; mkdir -p \"$ICONSET\"; " +
                    "for s in 16 32 128 256 512; do " +
                    "sips -z $s $s '" + png + "' --out \"$ICONSET/icon_${s}x${s}.png\" >/dev/null; " +
                    "d=$((s*2)); sips -z $d $d '" + png + "' --out \"$ICONSET/icon_${s}x${s}@2x.png\" >/dev/null; done; " +
                    "iconutil -c icns \"$ICONSET\" -o '" + dest + "'";
                var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", "-c \"" + sh.Replace("\"", "\\\"") + "\"")
                { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                var p = System.Diagnostics.Process.Start(psi);
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (File.Exists(dest)) Debug.Log("[LiaVAMac] PlayerIcon.icns di-generate → " + dest);
                else Debug.LogWarning("[LiaVAMac] gagal generate icns: " + e);
            }
            catch (System.Exception ex) { Debug.LogWarning("[LiaVAMac] AddMacIcns error: " + ex.Message); }
        }

        /// <summary>Set app icon (Mac Standalone + Android) dari Assets/Icon/AppIcon.png bila ada.</summary>
        private static void SetAppIcon()
        {
            const string path = "Assets/Icon/AppIcon.png";
            if (!File.Exists(path)) { Debug.Log("[LiaVAMac] AppIcon.png belum ada, skip icon."); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { Debug.LogWarning("[LiaVAMac] AppIcon.png gagal di-load sebagai Texture2D."); return; }

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, new[] { tex }, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { tex }, IconKind.Any);
            Debug.Log("[LiaVAMac] App icon di-set (Mac + Android) dari AppIcon.png");
        }

        /// <summary>Sisipkan NSMicrophoneUsageDescription ke Info.plist .app (untuk voice chat mic).</summary>
        private static void AddMicPermission(string appPath)
        {
            try
            {
                string plist = Path.Combine(appPath, "Contents/Info.plist");
                if (!File.Exists(plist)) { Debug.LogWarning("[LiaVAMac] Info.plist tak ada, skip mic perm"); return; }
                string txt = File.ReadAllText(plist);
                if (txt.Contains("NSMicrophoneUsageDescription")) return;
                const string entry = "\t<key>NSMicrophoneUsageDescription</key>\n\t<string>Lia mendengar suaramu untuk diajak ngobrol.</string>\n";
                int idx = txt.LastIndexOf("</dict>");
                if (idx < 0) return;
                txt = txt.Insert(idx, entry);
                File.WriteAllText(plist, txt);
                Debug.Log("[LiaVAMac] NSMicrophoneUsageDescription ditambahkan ke Info.plist");
            }
            catch (System.Exception e) { Debug.LogWarning("[LiaVAMac] mic perm gagal: " + e.Message); }
        }

        /// <summary>
        /// Tambahkan shader yang dibutuhkan UniGLTF/UniVRM saat import runtime ke
        /// GraphicsSettings.m_AlwaysIncludedShaders supaya tidak di-strip di build standalone.
        /// Fix "Value cannot be null. Parameter name: Shader" pada VrmAnimationImporter.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            // Kandidat nama shader (UniGLTF fallback + MToon + URP). Yang tidak ketemu dilewati.
            string[] candidates =
            {
                "LiaVA/UnlitSolid",
                "UniGLTF/UniUnlit",
                "UniGLTF/StandardVColor",
                "VRM10/MToon10",
                "VRM/MToon",
                "Standard",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Simple Lit",
            };

            var gs = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");

            var already = new HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++)
                already.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader);

            var added = new List<string>();
            var missing = new List<string>();
            foreach (var name in candidates)
            {
                var sh = Shader.Find(name);
                if (sh == null) { missing.Add(name); continue; }
                if (already.Contains(sh)) continue;

                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                already.Add(sh);
                added.Add(name);
            }

            so.ApplyModifiedProperties();
            Debug.Log($"[LiaVAMac] Always-included shaders → added: [{string.Join(", ", added)}]" +
                      (missing.Count > 0 ? $" | not found (skip): [{string.Join(", ", missing)}]" : ""));
        }
    }
}
