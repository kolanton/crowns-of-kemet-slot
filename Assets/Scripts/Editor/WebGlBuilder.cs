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

            // Keep thrown exceptions reportable. Without them a failure in the player is
            // perfectly silent — no console entry, nothing on screen, just a scene that
            // stops responding — which costs far more than the size this saves.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
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
            // Low rather than High: the aggressive levels can strip code that is only ever
            // reached from serialized scene data, such as the UI event modules, and the
            // few megabytes saved are not worth debugging that.
            PlayerSettings.SetManagedStrippingLevel(
                NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            // 2D art, so no need to pay for HDR or linear lighting.
            PlayerSettings.colorSpace = ColorSpace.Gamma;
        }
    }
}
