using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace LifeOfEmpires.Editor
{
    /// <summary>
    /// CLI entry point for producing the Xcode project:
    ///   Unity -projectPath . -batchmode -quit -nographics -buildTarget iOS \
    ///     -executeMethod LifeOfEmpires.Editor.BuildIosXcodeProject.Run
    /// Output lands in Builds/iOS (git-ignored). Fails the batch run on any build error.
    /// </summary>
    public static class BuildIosXcodeProject
    {
        public static void Run()
        {
            BuildReport report = BuildPipeline.BuildPlayer(
                EditorBuildSettings.scenes, "Builds/iOS", BuildTarget.iOS, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"iOS build failed: {report.summary.result}");

            UnityEngine.Debug.Log($"IOS_XCODE_OK {report.summary.outputPath}");
        }
    }
}
