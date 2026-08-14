using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Batch-mode build + deploy for the XREAL port, mirroring xreal-hello-world's
/// XREALHelloWorldSetup — with two differences learned building this project specifically:
///
/// 1. Unity's own AutoRunPlayer launch fails against XREAL's manifest (multiple intent-filter
///    categories confuse its parser), so this launches manually via adb instead.
/// 2. AutoRunPlayer also triggers a "Prepare For Build" device-availability preflight that
///    targets whatever device serial Unity last remembers (persisted outside the repo, in
///    Library/EditorUserBuildSettings.asset and/or EditorPrefs) — on this project that was a
///    stale phone serial from an earlier ARCore build, and it hard-fails the whole build before
///    compiling anything if that device isn't currently connected. Skipping AutoRunPlayer
///    entirely and doing install + launch ourselves via adb sidesteps that preflight.
/// See the xreal-unity-sdk-integration memory for the general background on both.
/// </summary>
static class XREALSonicSnowBuild
{
    const string LauncherActivity = "ai.nreal.activitylife.NRXRActivity";
    const string ApkPath = "Builds/Android/SonicSnow.apk";

    [MenuItem("XREAL/Build And Run On Device")]
    public static void BuildAndRun()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log($"Sonic Snow XREAL build result: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}, size={summary.totalSize} bytes");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[{step.name}] {msg.content}");
            return;
        }

        InstallAndLaunch();
    }

    static void InstallAndLaunch()
    {
        var editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
        var adbPath = Path.Combine(editorDir, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        if (!File.Exists(adbPath))
        {
            Debug.LogWarning($"[XREAL] adb not found at {adbPath}; skipping install/launch.");
            return;
        }

        var (installOut, installErr) = RunAdb(adbPath, $"install -r \"{Path.GetFullPath(ApkPath)}\"");
        Debug.Log($"[XREAL] adb install: {installOut}{installErr}");

        if (!installOut.Contains("Success"))
        {
            Debug.LogWarning("[XREAL] adb install did not report success; skipping launch.");
            return;
        }

        var appId = PlayerSettings.applicationIdentifier;
        var (launchOut, launchErr) = RunAdb(adbPath, $"shell am start -n {appId}/{LauncherActivity}");
        Debug.Log($"[XREAL] auto-launch: {launchOut}{launchErr}");
    }

    static (string stdout, string stderr) RunAdb(string adbPath, string arguments)
    {
        var psi = new ProcessStartInfo(adbPath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(10000);
        return (stdout, stderr);
    }
}
