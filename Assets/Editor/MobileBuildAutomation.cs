using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Riverworks.Editor
{
    /// <summary>Reproducible mobile player settings and command-line build entry points.</summary>
    public static class MobileBuildAutomation
    {
        private const string ApplicationId = "studio.riverworks.city";
        private const string AndroidDirectory = "Builds/Android";

        [MenuItem("Riverworks/Mobile/Build Android APK")]
        public static void BuildAndroidApk()
        {
            ConfigureAndroid(exportProject: false);
            BuildAndroid(Path.GetFullPath(Path.Combine(AndroidDirectory, "Riverworks.apk")), BuildOptions.None);
        }

        [MenuItem("Riverworks/Mobile/Export Android Gradle project")]
        public static void ExportAndroidGradleProject()
        {
            ConfigureAndroid(exportProject: true);
            BuildAndroid(Path.GetFullPath(Path.Combine(AndroidDirectory, "GradleProject")), BuildOptions.AcceptExternalModificationsToPlayer);
        }

        private static void ConfigureAndroid(bool exportProject)
        {
            BuildAutomation.Prepare();

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.bundleVersionCode = 6;
            PlayerSettings.Android.minifyDebug = false;
            PlayerSettings.Android.minifyRelease = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = exportProject;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = false;

            AssetDatabase.SaveAssets();
        }

        private static void BuildAndroid(string outputPath, BuildOptions options)
        {
            BuildAutomation.VerifySaveRoundTrip();
            SaveValidationChecks.Run();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AndroidDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BuildAutomation.ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = options
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);

            Directory.CreateDirectory("Artifacts");
            File.WriteAllText(
                "Artifacts/android-build-result.txt",
                "SUCCESS\nUnity " + Application.unityVersion +
                "\nOutput " + outputPath +
                "\nMode " + (EditorUserBuildSettings.exportAsGoogleAndroidProject ? "GradleExport" : "APK") +
                "\nArchitecture ARM64\nBackend IL2CPP\nMinSdk 26" +
                "\nBytes " + report.summary.totalSize +
                "\nWarnings " + report.summary.totalWarnings +
                "\nErrors " + report.summary.totalErrors + "\n");
            Debug.Log("RIVERWORKS_ANDROID_BUILD_SUCCESS " + outputPath);
        }
    }
}
