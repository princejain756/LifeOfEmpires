using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeOfEmpires.Editor
{
    /// <summary>
    /// CLI entry points for producing the Xcode project:
    ///   device:    -executeMethod LifeOfEmpires.Editor.BuildIosXcodeProject.Run
    ///   simulator: -executeMethod LifeOfEmpires.Editor.BuildIosXcodeProject.RunSimulator
    /// Output lands in Builds/iOS or Builds/iOS-sim (git-ignored). Each method pins
    /// PlayerSettings.iOS.sdkVersion explicitly because the setting persists across runs.
    /// </summary>
    public static class BuildIosXcodeProject
    {
        private const string AppIconSource = "Assets/LifeOfEmpires/Resources/UI/app_icon.png";

        public static void Run() => Build(iOSSdkVersion.DeviceSDK, "Builds/iOS");

        public static void RunSimulator() => Build(iOSSdkVersion.SimulatorSDK, "Builds/iOS-sim");

        private static void Build(iOSSdkVersion sdk, string outputPath)
        {
            PlayerSettings.iOS.sdkVersion = sdk;
            BuildReport report = BuildPipeline.BuildPlayer(
                EditorBuildSettings.scenes, outputPath, BuildTarget.iOS, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"iOS build failed: {report.summary.result}");

            StampAppIcon(Path.Combine(report.summary.outputPath, "Unity-iPhone/Images.xcassets/AppIcon.appiconset"));

            UnityEngine.Debug.Log($"IOS_XCODE_OK {report.summary.outputPath}");
        }

        /// <summary>
        /// Re-render the generated app icon at every size the exported AppIcon set declares, so the
        /// shipped icon is the painted crest rather than the default Unity cube. Skipped silently
        /// when the art pass has not produced an icon yet.
        /// </summary>
        private static void StampAppIcon(string appIconSetDir)
        {
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconSource);
            string contentsPath = Path.Combine(appIconSetDir, "Contents.json");
            if (source == null || !File.Exists(contentsPath)) return;

            string contents = File.ReadAllText(contentsPath);
            var entries = System.Text.RegularExpressions.Regex.Matches(
                contents,
                "\\\"filename\\\"\\s*:\\s*\\\"(?<file>[^\\\"]+)\\\"[^}]*?\\\"scale\\\"\\s*:\\s*\\\"(?<scale>[0-9]+)x\\\"[^}]*?\\\"size\\\"\\s*:\\s*\\\"(?<size>[0-9.]+)x[0-9.]+\\\"");
            if (entries.Count == 0) return;

            foreach (System.Text.RegularExpressions.Match entry in entries)
            {
                if (!float.TryParse(entry.Groups["size"].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float points)) continue;
                if (!int.TryParse(entry.Groups["scale"].Value, out int scale)) continue;

                int pixels = Mathf.Max(16, Mathf.RoundToInt(points * scale));
                byte[] png = RenderPng(source, pixels, pixels);
                File.WriteAllBytes(Path.Combine(appIconSetDir, entry.Groups["file"].Value), png);
            }
        }

        private static byte[] RenderPng(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                // RGB24: App Store rejects icons that carry an alpha channel.
                var output = new Texture2D(width, height, TextureFormat.RGB24, false);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                output.Apply();
                RenderTexture.active = previous;
                byte[] png = output.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(output);
                return png;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
