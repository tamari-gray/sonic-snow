<#
.SYNOPSIS
    Captures a full Android logcat from the Beam Pro across one Sonic Snow reproduction run,
    for sending to XREAL support re: the First Person View capture stutter.

.DESCRIPTION
    Two modes.

    TETHERED (default) — logcat streams over adb to the PC for a fixed duration. Simple, gives
    you a live countdown, but the Beam Pro must stay within adb reach of the PC the whole time.

    ON-DEVICE (-OnDevice / -Stop) — logcat runs detached ON the Beam Pro, writing to internal
    storage. adb is needed only to start it and to collect it afterwards, so you can walk out of
    Wi-Fi range, film for as long as you like, come back, and pull the file. Use this for outdoor
    filming. Measured cost on this device: ~16 KB/s, so roughly 1 MB per minute of capture.

    Both modes follow https://docs.xreal.com/Frequently%20Asked%20Questions#7-capturing-debug-logs-with-adb-logcat
    with three additions their steps omit, all of which matter for THIS bug:

      * 'logcat -c' first. Their steps pipe straight to a file without clearing, so you ship them
        however many megabytes of unrelated boot/system noise happened to still be in the ring
        buffer. Clearing means every line in the file is from the reproduction. (This matters more
        than it sounds: 'logcat -f' dumps the entire existing buffer before it starts tailing, so
        an uncleared 16M buffer lands in the file as a 4 MB prologue.)
      * '-G 16M' to enlarge the ring buffer. The FPV pipeline is chatty and a long session at full
        verbosity can otherwise wrap the buffer and silently drop the START of the session — which
        is exactly where the VideoCapture init sequence lives.
      * '-b all' rather than the default main/system/crash, so the 'events' buffer is included.
        For a timing bug, the frame/activity timing records in there are worth having.

    Order is deliberate: the app is force-stopped and the buffer cleared BEFORE logging starts,
    then the app is launched INSIDE the capture window, so the whole VideoCapture startup
    handshake ('Created VideoCapture Instance!' -> 'Started Video Capture Mode!' ->
    'OnScreenCaptureGranted' -> 'Started Recording Video!') is in the log rather than preceding it.

.NOTES
    The Beam Pro has ONE USB-C port, shared between the glasses tether and the PC. The glasses MUST
    be attached to reproduce this (the app exits without them), so the PC cannot be on USB at the
    same time — this has to run over wireless adb. Set that up once per Beam Pro boot, while still
    on USB:

        adb tcpip 5555
        adb connect <beam-pro-ip>:5555      # IP via: adb shell ip addr show wlan0

    then unplug USB and attach the glasses. TCP mode is a device-side property that survives
    changing Wi-Fi networks, so after joining a phone hotspot you only need to 'adb connect' the
    new IP — not redo 'adb tcpip' over USB. The wireless link does drop on its own after ~10-30 min
    idle though, and that cannot be revived from the PC side: if 'adb connect' fails outright,
    reconnect USB briefly and redo the tcpip step.

    Do NOT kill logcat by name on this device. XREAL's SDK runs its own logcat process
    (EnableAutoLogcat in XREALSettings.asset) and a broad 'pkill logcat' takes it out too. -Stop
    kills only the PID this script recorded when it started.

.EXAMPLE
    .\Tools\capture-logcat.ps1 -Serial 192.168.88.13:5555 -Duration 120
    Tethered: records for 120s with a countdown, then saves and checks the log.

.EXAMPLE
    .\Tools\capture-logcat.ps1 -Serial 192.168.88.13:5555 -OnDevice
    .\Tools\capture-logcat.ps1 -Serial 192.168.88.13:5555 -Stop
    Untethered: start, unplug and go film, then come back and collect.
#>

[CmdletBinding(DefaultParameterSetName = 'Tethered')]
param(
    # Wireless adb target, e.g. 192.168.88.13:5555. Optional if exactly one device is connected.
    [string] $Serial,

    # Tethered only: seconds to record. XREAL asked for 30-60s of reproduction; allow extra for
    # app launch and the calibration screen, which has a 45s timeout of its own.
    [Parameter(ParameterSetName = 'Tethered')]
    [ValidateRange(10, 600)]
    [int] $Duration = 120,

    # Start a detached on-device capture and return immediately, so adb can go away.
    [Parameter(ParameterSetName = 'OnDeviceStart', Mandatory = $true)]
    [switch] $OnDevice,

    # Stop a detached on-device capture, pull the log, and check it.
    [Parameter(ParameterSetName = 'OnDeviceStop', Mandatory = $true)]
    [switch] $Stop,

    [string] $Output = "logcat.txt",

    [string] $PackageName = "com.unity.template.ar_mobile",

    # Matches SonicSnowBuild.LauncherActivity — XREAL's own activity, not a Unity one.
    [string] $LauncherActivity = "ai.nreal.activitylife.NRXRActivity",

    # Skip launching the app, if you would rather start it by hand inside the window.
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'

$DeviceLog = '/data/local/tmp/sonicsnow-logcat.txt'
$DevicePid = '/data/local/tmp/sonicsnow-logcat.pid'

# --- Locate adb -------------------------------------------------------------------------------
# Same resolution order the build scripts use: Unity's bundled platform-tools, then PATH.
$adb = $null
$unityAdb = Join-Path ${env:ProgramFiles} 'Unity\Hub\Editor\6000.4.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
if (Test-Path $unityAdb) {
    $adb = $unityAdb
} else {
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $onPath) { $adb = $onPath.Source }
}
if ($null -eq $adb) {
    throw "adb not found. Looked in Unity's platform-tools ($unityAdb) and on PATH."
}

# --- Resolve the target device ----------------------------------------------------------------
# A wireless connect leaves BOTH the USB serial and the <ip>:5555 entry listed, and adb then
# refuses any command with "more than one device/emulator" — so be explicit about which one.
# 'adb devices' is TAB-separated ("<serial>\tdevice"), so the separator before the state is
# whitespace. Matching \sdevice$ also keeps this from picking up "offline"/"unauthorized" rows.
$deviceLines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' }
$devices = @($deviceLines | ForEach-Object { ($_ -split '\s+')[0] })

if ($devices.Count -eq 0) {
    throw "No adb device connected. The glasses need the Beam Pro's USB-C port, so set up wireless adb first (see the notes at the top of this script)."
}
if (-not $Serial) {
    if ($devices.Count -gt 1) {
        $deviceList = $devices -join ', '
        throw "Multiple adb devices ($deviceList). Re-run with -Serial to pick one - prefer the wireless ip:5555 entry so the glasses can stay attached."
    }
    $Serial = $devices[0]
}
Write-Host "adb    : $adb"
Write-Host "device : $Serial"
$adbArgs = @('-s', $Serial)

$outPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))

# Shared post-run check. A run where recording never started produces a log that shows XREAL
# nothing, and finding that out now beats finding out after they reply asking for a usable one.
function Test-CaptureMarkers {
    param([string] $Path)

    $markers = @(
        'Created VideoCapture Instance',
        'Started Video Capture Mode',
        'OnScreenCaptureGranted',
        'Started Recording Video'
    )
    Write-Host "`nCapture handshake:"
    $missing = 0
    foreach ($m in $markers) {
        if (Select-String -Path $Path -SimpleMatch -Pattern $m -Quiet) {
            Write-Host "  [found]   $m" -ForegroundColor Green
        } else {
            Write-Host "  [MISSING] $m" -ForegroundColor Red
            $missing++
        }
    }
    if ($missing -gt 0) {
        Write-Warning "$missing marker(s) missing - recording probably never started, so this log will not show XREAL the stutter. Check the calibration screen reached 'AR recording setup' and that the capture prompt was allowed, then re-run."
    } else {
        Write-Host "`nAll markers present. Ready to send to XREAL." -ForegroundColor Green
    }
}

function Show-Summary {
    param([string] $Path)

    if (-not (Test-Path $Path)) { throw "No log file was produced at $Path." }
    $lines = (Get-Content $Path | Measure-Object -Line).Lines
    $sizeMb = [math]::Round((Get-Item $Path).Length / 1MB, 2)
    Write-Host "`nCaptured $lines lines / $sizeMb MB -> $Path" -ForegroundColor Green
    Test-CaptureMarkers -Path $Path
}

# ==============================================================================================
# ON-DEVICE: STOP
# ==============================================================================================
if ($Stop) {
    $recordedPid = (& $adb @adbArgs shell "cat $DevicePid 2>/dev/null").Trim()
    if (-not $recordedPid) {
        throw "No recorded capture PID at $DevicePid on the device. Was -OnDevice ever started (and has the device rebooted since)?"
    }

    Write-Host "Stopping on-device capture (pid $recordedPid) ..."
    # By PID, never by name: XREAL's SDK runs its own logcat and a broad pkill kills that too.
    & $adb @adbArgs shell "kill $recordedPid 2>/dev/null; sleep 1; echo stopped" | Out-Null

    $size = (& $adb @adbArgs shell "stat -c %s $DeviceLog 2>/dev/null").Trim()
    if (-not $size -or $size -eq '0') {
        throw "On-device log at $DeviceLog is missing or empty."
    }
    Write-Host "Pulling $([math]::Round([int64]$size / 1MB, 2)) MB ..."

    if (Test-Path $outPath) { Remove-Item $outPath -Force }
    & $adb @adbArgs pull $DeviceLog $outPath | Out-Null

    & $adb @adbArgs shell "rm -f $DeviceLog $DevicePid" | Out-Null
    Show-Summary -Path $outPath
    return
}

# ==============================================================================================
# PREPARE (shared by tethered and on-device start)
# ==============================================================================================
Write-Host "`nStopping $PackageName ..."
& $adb @adbArgs shell am force-stop $PackageName

Write-Host "Enlarging log ring buffer to 16M ..."
& $adb @adbArgs logcat -G 16M

Write-Host "Clearing existing log buffer ..."
# -c against a just-resized buffer occasionally reports "failed to clear"; harmless, and the
# capture is still clean because nothing has been written since the resize.
try { & $adb @adbArgs logcat -b all -c } catch { Write-Warning "Buffer clear reported an error; continuing." }

# ==============================================================================================
# ON-DEVICE: START
# ==============================================================================================
if ($OnDevice) {
    & $adb @adbArgs shell "rm -f $DeviceLog $DevicePid" | Out-Null

    # nohup + redirect so the process survives this adb shell closing; $! records the PID so
    # -Stop can kill exactly this one.
    $startCmd = "nohup logcat -b all -v threadtime -f $DeviceLog > /dev/null 2>&1 & echo `$! > $DevicePid"
    & $adb @adbArgs shell $startCmd | Out-Null
    Start-Sleep -Seconds 2

    $recordedPid = (& $adb @adbArgs shell "cat $DevicePid 2>/dev/null").Trim()
    $alive = (& $adb @adbArgs shell "kill -0 $recordedPid 2>/dev/null && echo yes").Trim()
    if ($alive -ne 'yes') {
        throw "On-device logcat failed to stay running (pid '$recordedPid')."
    }
    Write-Host "On-device capture running as pid $recordedPid -> $DeviceLog" -ForegroundColor Green

    if (-not $NoLaunch) {
        Write-Host "Launching $PackageName ..."
        & $adb @adbArgs shell am start -n "$PackageName/$LauncherActivity" | Out-Null
    }

    Write-Host ""
    Write-Host "=== CAPTURE IS RUNNING ON THE DEVICE ===" -ForegroundColor Yellow
    Write-Host "adb is no longer needed. Unplug, go film for as long as you like (~1 MB/min)."
    Write-Host "Let calibration reach 'AR recording setup', allow the capture prompt, and make"
    Write-Host "sure the stutter is clearly visible in the footage."
    Write-Host ""
    Write-Host "When you are back in range, collect it with:" -ForegroundColor Cyan
    Write-Host "  .\Tools\capture-logcat.ps1 -Serial $Serial -Stop" -ForegroundColor Cyan
    return
}

# ==============================================================================================
# TETHERED
# ==============================================================================================
if (Test-Path $outPath) { Remove-Item $outPath -Force }

Write-Host "`nStarting capture -> $outPath"
$logcatArgs = $adbArgs + @('logcat', '-b', 'all', '-v', 'threadtime')
$proc = Start-Process -FilePath $adb -ArgumentList $logcatArgs `
                      -RedirectStandardOutput $outPath -NoNewWindow -PassThru

Start-Sleep -Milliseconds 700
if ($proc.HasExited) {
    throw "logcat exited immediately (code $($proc.ExitCode)). Check the device is still reachable: adb -s $Serial get-state"
}

if (-not $NoLaunch) {
    Write-Host "Launching $PackageName ..."
    & $adb @adbArgs shell am start -n "$PackageName/$LauncherActivity" | Out-Null
}

Write-Host ""
Write-Host "=== REPRODUCE NOW ===" -ForegroundColor Yellow
Write-Host "Put the glasses on, let calibration run to the 'AR recording setup' row, allow the"
Write-Host "screen-capture prompt, and move around until the stutter is clearly visible."
Write-Host "Stay within adb range - if the device goes out of range the log just stops."
Write-Host ""

for ($i = $Duration; $i -gt 0; $i--) {
    Write-Progress -Activity "Capturing logcat" -Status "$i s remaining" -PercentComplete ((($Duration - $i) / $Duration) * 100)
    Start-Sleep -Seconds 1
}
Write-Progress -Activity "Capturing logcat" -Completed

Write-Host "Stopping capture ..."
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Start-Sleep -Milliseconds 500

Show-Summary -Path $outPath
