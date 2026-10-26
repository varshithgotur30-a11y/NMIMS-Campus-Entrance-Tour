using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;

public class MetaQuestBuildScript
{
    [MenuItem("VR Tour/Build Meta Quest APK")]
    public static void BuildAPK()
    {
        string buildsDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        if (!Directory.Exists(buildsDir))
        {
            Directory.CreateDirectory(buildsDir);
        }

        string apkPath = Path.Combine(buildsDir, "HPC_LAB_VRTour.apk");
        string logPath = Path.Combine(Directory.GetCurrentDirectory(), "build_result.txt");

        File.WriteAllText(logPath, $"[BUILDING] Starting build at {DateTime.Now} to {apkPath}...\n");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new string[] { "Assets/Scenes/SampleScene.unity" };
        buildPlayerOptions.locationPathName = apkPath;
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.targetGroup = BuildTargetGroup.Android;
        buildPlayerOptions.options = BuildOptions.None;

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            string successMsg = $"[SUCCESS] Meta Quest APK built successfully!\nPath: {apkPath}\nSize: {summary.totalSize / (1024 * 1024)} MB\nTime: {summary.totalTime.TotalSeconds:F1} seconds\nFinished: {DateTime.Now}\n";
            File.WriteAllText(logPath, successMsg);
            Debug.Log(successMsg);
        }
        else
        {
            string errorMsg = $"[FAILED] Build failed with result: {summary.result}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nTime: {summary.totalTime.TotalSeconds:F1} seconds\n";
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    {
                        errorMsg += $" - {msg.content}\n";
                    }
                }
            }
            File.WriteAllText(logPath, errorMsg);
            Debug.LogError(errorMsg);
        }
    }

    public static void BuildCommandLine()
    {
        BuildAPK();
    }
}
