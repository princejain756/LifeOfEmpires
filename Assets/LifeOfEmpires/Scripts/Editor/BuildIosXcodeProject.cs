using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

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
        public static void Run() => Build(iOSSdkVersion.DeviceSDK, "Builds/iOS");

        public static void RunSimulator() => Build(iOSSdkVersion.SimulatorSDK, "Builds/iOS-sim");

        private static void Build(iOSSdkVersion sdk, string outputPath)
        {
            PlayerSettings.iOS.sdkVersion = sdk;
            BuildReport report = BuildPipeline.BuildPlayer(
                EditorBuildSettings.scenes, outputPath, BuildTarget.iOS, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"iOS build failed: {report.summary.result}");

            UnityEngine.Debug.Log($"IOS_XCODE_OK {report.summary.outputPath}");
        }
    }
}
