<#
.SYNOPSIS
    Captures a full Android logcat from the Beam Pro across one Sonic Snow reproduction run,
    for sending to XREAL support re: the First Person View capture stutter.

.DESCRIPTION
    Follows https://docs.xreal.com/Frequently%20Asked%20Questions#7-capturing-debug-logs-with-adb-logcat
    with three additions their steps omit, all of which matter for THIS bug:

      * 'adb logcat -c' first. Their steps pipe straight to a file without clearing, so you ship
        them however many megabytes of unrelated boot/system noise happened to still be in the
        ring buffer. Clearing means every line in the file is from the reproduction.
      * '-G 16M' to enlarge the ring buffer. The FPV pipeline is chatty and a 60s session at full
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

    then unplug USB and attach the glasses. The wireless link also drops on its own after ~10-30
    min idle, and cannot be revived from the PC side — if 'adb connect' fails outright, reconnect
    USB briefly and redo the tcpip step.

.EXAMPLE
    .\tools\capture-logcat.ps1 -Serial 192.168.1.42:5555 -Duration 60
#>

[CmdletBinding()]
param(
    # Wireless adb target, e.g. 192.168.1.42:5555. Optional if exactly one device is connected.
    [string] $Serial,

    # Seconds to record. XREAL asked for 30-60s of reproduction.
    [ValidateRange(10, 600)]
    [int] $Duration = 60,

    [string] $Output = "logcat.txt",

    [string] $PackageName = "com.unity.template.ar_mobile",

    # Matches SonicSnowBuild.LauncherActivity — XREAL's own activity, not a Unity one.
    [string] $LauncherActivity = "ai.nreal.activitylife.NRXRActivity",

    # Skip launching the app, if you would rather start it by hand inside the window.
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'

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
Write-Host "adb: $adb"

# --- Resolve the target device ----------------------------------------------------------------
# A wireless connect leaves BOTH the USB serial and the <ip>:5555 entry listed, and adb then
# refuses any command with "more than one device/emulator" — so be explicit about which one.
$deviceLines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\Sdevice$' }
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
if ($Serial -notmatch ':\d+$') {
    Write-Warning "Target '$Serial' looks like a USB connection. The glasses need that port to reproduce this bug — if the app exits with 'connect glasses', switch to wireless adb."
}
Write-Host "device: $Serial"
$adbArgs = @('-s', $Serial)

# --- Prepare ----------------------------------------------------------------------------------
Write-Host "`nStopping $PackageName ..."
& $adb @adbArgs shell am force-stop $PackageName

Write-Host "Enlarging log ring buffer to 16M ..."
& $adb @adbArgs logcat -G 16M

Write-Host "Clearing existing log buffer ..."
# -c against a just-resized buffer occasionally reports "failed to clear"; harmless, and the
# capture is still clean because nothing has been written since the resize.
try { & $adb @adbArgs logcat -b all -c } catch { Write-Warning "Buffer clear reported an error; continuing." }

# --- Capture ----------------------------------------------------------------------------------
$outPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))
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
Write-Host "screen-capture prompt, and ride/walk far enough that the stutter is clearly visible."
Write-Host ""

for ($i = $Duration; $i -gt 0; $i--) {
    Write-Progress -Activity "Capturing logcat" -Status "$i s remaining" -PercentComplete ((($Duration - $i) / $Duration) * 100)
    Start-Sleep -Seconds 1
}
Write-Progress -Activity "Capturing logcat" -Completed

# --- Stop and summarise -----------------------------------------------------------------------
Write-Host "Stopping capture ..."
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Start-Sleep -Milliseconds 500

if (-not (Test-Path $outPath)) { throw "No log file was produced at $outPath." }

$lines = (Get-Content $outPath | Measure-Object -Line).Lines
$sizeMb = [math]::Round((Get-Item $outPath).Length / 1MB, 2)
Write-Host "`nCaptured $lines lines / $sizeMb MB -> $outPath" -ForegroundColor Green

# Sanity check: if the capture handshake is missing, the run did not actually exercise the bug and
# the log is not worth sending. Better to find that out now than after XREAL replies asking for it.
$markers = @(
    'Created VideoCapture Instance',
    'Started Video Capture Mode',
    'OnScreenCaptureGranted',
    'Started Recording Video'
)
Write-Host "`nCapture handshake:"
$missing = 0
foreach ($m in $markers) {
    $hit = Select-String -Path $outPath -SimpleMatch -Pattern $m -Quiet
    if ($hit) {
        Write-Host "  [found]   $m" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $m" -ForegroundColor Red
        $missing++
    }
}
if ($missing -gt 0) {
    Write-Warning "$missing marker(s) missing — recording probably never started, so this log will not show XREAL the stutter. Check the calibration screen reached 'AR recording setup' and that the capture prompt was allowed, then re-run."
} else {
    Write-Host "`nAll markers present. Ready to send to XREAL." -ForegroundColor Green
}
