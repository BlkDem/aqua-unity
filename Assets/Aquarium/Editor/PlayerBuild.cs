// Player builds from the command line: -executeMethod Aquarium.EditorTools.PlayerBuild.Windows
// (add -dev for a development build with the profiler).
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static class PlayerBuild
    {
        public static void Configure()
        {
            PlayerSettings.productName = "Живой аквариум";
            PlayerSettings.companyName = "umolab";
            PlayerSettings.enableFrameTimingStats = true;   // CPU and GPU frame times for the FPS line and the benchmark
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        [MenuItem("Aquarium/Build Windows Player")]
        public static void Windows()
        {
            Configure();
            bool dev = System.Environment.GetCommandLineArgs().Contains("-dev");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = "Builds/Windows/Aquarium.exe",
                target = BuildTarget.StandaloneWindows64,
                options = (dev ? BuildOptions.Development : BuildOptions.None) | BuildOptions.CompressWithLz4HC
                    | (System.Environment.GetCommandLineArgs().Contains("-clean") ? BuildOptions.CleanBuildCache : BuildOptions.None),
            });
            var s = report.summary;
            Debug.Log($"BUILD {s.result}: {s.totalSize / 1048576} MB, {s.totalTime.TotalSeconds:0} s, {s.totalErrors} errors");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
