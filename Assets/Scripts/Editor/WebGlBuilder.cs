using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Batch-mode WebGL build, tuned for a demo that has to be small enough to host as a
    /// static page and start without a progress bar nobody waits through.
    /// </summary>
    public static class WebGlBuilder
    {
        private const string OutputDirectory = "Build/WebGL";

        /// <summary>
        /// Entry point for <c>Unity.exe -quit -batchmode -executeMethod</c>. Exits non-zero
        /// on failure so the calling shell can tell whether the build actually produced
        /// anything.
        /// </summary>
        public static void Build()
        {
            try
            {
                var report = Run();
                bool succeeded = report.summary.result == BuildResult.Succeeded;

                Debug.Log($"[WebGlBuilder] {report.summary.result} in {report.summary.totalTime}, " +
                          $"{report.summary.totalSize / (1024 * 1024)} MB");

                if (Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[WebGlBuilder] threw: {exception}");
                if (Application.isBatchMode) EditorApplication.Exit(2);
            }
        }

        [MenuItem("Egyptian Slot/Build WebGL")]
        private static void BuildFromMenu()
        {
            var report = Run();
            Debug.Log($"[WebGlBuilder] {report.summary.result}, output at " +
                      $"{Path.GetFullPath(OutputDirectory)}");
        }

        private static BuildReport Run()
        {
            ApplySettings();

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "No enabled scenes in Build Settings; the build would be empty.");

            Directory.CreateDirectory(OutputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            return BuildPipeline.BuildPlayer(options);
        }

        /// <summary>
        /// Settings that matter for a hosted demo. Applied here rather than left in
        /// ProjectSettings so a fresh clone builds the same way without manual setup.
        /// </summary>
        private static void ApplySettings()
        {
            PlayerSettings.companyName = "Egyptian Slot Demo";
            PlayerSettings.productName = "Egyptian Slot";

            // Gzip over Brotli: static hosts that cannot set Content-Encoding will still
            // serve this, and Unity's loader falls back to decompressing in JavaScript.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            // The demo is 2D sprites and UI, so the extra size of exception support buys
            // nothing a browser console would not already show.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.dataCaching = true;

            // The stock minimal template: a bare canvas and loader, no Unity logo or
            // fullscreen chrome to fight with the page embedding it.
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

            // Unity 6 lets Personal licences turn the splash off. If this licence does not
            // allow it the editor overrides this silently, so the build is the only proof.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            PlayerSettings.runInBackground = false;
            PlayerSettings.SetIl2CppCompilerConfiguration(
                NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetManagedStrippingLevel(
                NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            // 2D art, so no need to pay for HDR or linear lighting.
            PlayerSettings.colorSpace = ColorSpace.Gamma;
        }
    }
}
