using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Drives an Android build + on-device launch from the Editor rather than the GUI's own
/// Build and Run — see [[xreal-hello-world-workflow]]. Same launcher-activity workaround as
/// XREALHelloWorldSetup: Unity's BuildOptions.AutoRunPlayer fails to find XREAL's launcher
/// activity because its manifest parser trips over the multiple intent-filter categories on
/// ai.nreal.activitylife.NRXRActivity, even on an install that otherwise succeeded — so this
/// launches manually via adb instead of relying on it.
/// </summary>
public static class SonicSnowBuild
{
    const string LauncherActivity = "ai.nreal.activitylife.NRXRActivity";

    [MenuItem("Sonic Snow/XREAL/Build And Run On Device")]
    public static void BuildAndRun()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Android/SonicSnow.apk",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log($"Sonic Snow build result: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}, size={summary.totalSize} bytes");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[{step.name}] {msg.content}");
            return;
        }

        InstallAndLaunch(report.summary.outputPath);
    }

    static void InstallAndLaunch(string apkPath)
    {
        var editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
        var adbPath = Path.Combine(editorDir, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        if (!File.Exists(adbPath))
        {
            Debug.LogWarning($"[SonicSnowBuild] adb not found at {adbPath}; APK built to {apkPath} but not installed.");
            return;
        }

        var (devicesOut, _) = RunAdb(adbPath, "devices");
        var deviceLines = devicesOut.Split('\n')
            .Skip(1)
            .Select(l => l.Trim())
            .Where(l => l.EndsWith("device"))
            .ToArray();

        if (deviceLines.Length == 0)
        {
            Debug.LogWarning($"[SonicSnowBuild] No adb device connected — APK built to {apkPath} but not installed. " +
                              "Connect the Beam Pro (USB, or wireless adb per [[xreal-beam-pro-wireless-adb]]) and re-run " +
                              "Sonic Snow > XREAL > Install And Launch to finish deploying this build.");
            return;
        }

        // A wireless-adb connect leaves both the USB serial and the <ip>:5555 entry listed
        // simultaneously (see [[xreal-beam-pro-wireless-adb]]) — adb then refuses any command
        // with "more than one device/emulator". Prefer the wireless target when both are present
        // (glasses stay attached to the Beam Pro's one USB-C port; USB is only for adb setup),
        // and always pass -s so this is immune to that ambiguity regardless of cable state.
        var serials = deviceLines.Select(l => l.Split('\t', ' ')[0]).ToArray();
        var target = serials.FirstOrDefault(s => s.Contains(":")) ?? serials[0];
        Debug.Log($"[SonicSnowBuild] Targeting device {target} ({serials.Length} connected: {string.Join(", ", serials)})");

        var (installOut, installErr) = RunAdb(adbPath, $"-s {target} install -r \"{apkPath}\"");
        Debug.Log($"[SonicSnowBuild] install: {installOut}{installErr}");

        if (!installOut.Contains("Success"))
        {
            Debug.LogError("[SonicSnowBuild] Install failed — not attempting launch.");
            return;
        }

        var appId = PlayerSettings.applicationIdentifier;

        bool installed = false;
        for (int i = 0; i < 20 && !installed; i++)
        {
            var (stdout, _) = RunAdb(adbPath, $"-s {target} shell pm path {appId}");
            if (!string.IsNullOrWhiteSpace(stdout))
                installed = true;
            else
                Thread.Sleep(500);
        }

        if (!installed)
        {
            Debug.LogWarning($"[SonicSnowBuild] Package {appId} not found on device after waiting; skipping auto-launch.");
            return;
        }

        var (launchOut, launchErr) = RunAdb(adbPath, $"-s {target} shell am start -n {appId}/{LauncherActivity}");
        Debug.Log($"[SonicSnowBuild] auto-launch: {launchOut}{launchErr}");
    }

    // Re-runs just the install/launch half against the last build — for after connecting the
    // device once BuildAndRun already reported "no adb device connected".
    [MenuItem("Sonic Snow/XREAL/Install And Launch")]
    public static void InstallAndLaunchLastBuild()
    {
        const string apkPath = "Builds/Android/SonicSnow.apk";
        if (!File.Exists(apkPath))
        {
            Debug.LogError($"[SonicSnowBuild] No APK at {apkPath} — run Build And Run On Device first.");
            return;
        }
        InstallAndLaunch(apkPath);
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
        proc.WaitForExit(15000);
        return (stdout, stderr);
    }
}
