using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LiaVA.Editor
{
    /// <summary>
    /// One-shot Android export untuk Lia VA. Output ke LiaVA-Android/unityLibrary
    /// supaya Gradle bisa langsung build APK tanpa Unity-side dialog.
    ///
    /// Trigger:
    /// - Menu: "Lia VA > Build Android (Export)" — manual click
    /// - Batch: Unity -batchmode -executeMethod LiaVA.Editor.LiaVABuildScript.BuildAndroidLibrary
    /// </summary>
    public static class LiaVABuildScript
    {
        // Unity export menghasilkan PROJECT PENUH (root gradle + launcher + unityLibrary + shared)
        // di path ini. Modul library asli ada di <OutputPath>/unityLibrary — dipindah ke
        // android/unityLibrary oleh restore.sh (JANGAN export langsung ke android/unityLibrary).
        private const string OutputPath = "/Users/lendra/Documents/codeV/LiaVA/android/unityexport_tmp";
        private const string SceneToBuild = "Assets/Scenes/Main.unity";

        [MenuItem("Lia VA/Build Android (Export to LiaVA-Android)")]
        public static void BuildAndroidLibrary()
        {
            // Pastikan platform = Android
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[LiaVABuild] Switching platform to Android…");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            // Export As Android Library = on
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;

            // Build options
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { SceneToBuild },
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = OutputPath,
                options = BuildOptions.AcceptExternalModificationsToPlayer  // export library, not APK
            };

            Debug.Log($"[LiaVABuild] Starting Android library export → {OutputPath}");
            var report = BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[LiaVABuild] ✓ Export success. Size: {summary.totalSize / 1024 / 1024} MB. " +
                          $"Time: {summary.totalTime.TotalSeconds:F1}s");
                Debug.Log($"[LiaVABuild] Next: cd {Path.GetDirectoryName(OutputPath)} && ./gradlew :launcher:assembleDebug");
            }
            else
            {
                Debug.LogError($"[LiaVABuild] ✗ Export failed: {summary.result} — {summary.totalErrors} errors");
            }
        }
    }
}
