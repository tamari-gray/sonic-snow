<#
.SYNOPSIS
    Produces a reduced companion to a full logcat capture, for pasting into or attaching to a
    support thread where a 16 MB file is unwieldy.

.DESCRIPTION
    Two reductions, applied in order:

      1. TIME WINDOW — keeps only the capture session, detected automatically from the
         FirstPersonStreammingCast lifecycle markers ("Created VideoCapture Instance!" through
         "Stopped Video Capture Mode!"), padded by -PadSeconds either side so the surrounding
         context survives.

      2. NOISE TAGS — drops log tags that are provably unrelated to the capture path and that
         dominate the line count. On this device qdgralloc alone contributes ~8,500 identical
         "getInterlacedFlag: getMetaData returned -22" lines.

    This is a DENYLIST, not an allowlist: anything not explicitly named as noise is kept. That
    way an unfamiliar tag that turns out to matter is never silently dropped, which is the failure
    mode that makes a filtered log worse than useless in a support thread.

    Send this ALONGSIDE the full log, never instead of it. It exists to show someone where to
    look, not to decide for them what is relevant.

.EXAMPLE
    .\Tools\strip-logcat.ps1
    .\Tools\strip-logcat.ps1 -Input logcat.txt -Output logcat-stripped.txt -PadSeconds 10
#>

[CmdletBinding()]
param(
    [Alias('Input')]
    [string] $InputPath = "logcat.txt",

    [Alias('Output')]
    [string] $OutputPath = "logcat-stripped.txt",

    # Seconds of context kept either side of the capture session.
    [int] $PadSeconds = 5
)

$ErrorActionPreference = 'Stop'

# Tags that are high-volume and unrelated to the RGB-camera / capture / render path. Bluetooth,
# the Wi-Fi location stack, lockscreen UI, and the Qualcomm gralloc metadata warning.
$NoiseTags = @(
    'qdgralloc'
    'bt_btif', 'bt_btm', 'bt_l2cap', 'bt_stack', 'bt_hci', 'bt_a2dp'
    'vendor.qti.bluetooth@1.0-ibs_handler'
    'LOWI-9.0.1.69'
    'KeyguardIndication', 'KeyguardUpdateMonitor'
    'AlarmManager'
    'PrimesLoggerHolder'
)

$inFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $InputPath))
$outFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
if (-not (Test-Path $inFull)) { throw "Input log not found: $inFull" }

Write-Host "Reading $inFull ..."
$lines = Get-Content $inFull
Write-Host "  $($lines.Count) lines in"

# --- Locate the capture session ---------------------------------------------------------------
function Get-Stamp {
    param([string] $Line)
    if ($Line -match '^(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})\.(\d{3})') {
        # Year is absent from logcat's default format; any fixed year works since we only ever
        # compare stamps within a single capture.
        return [datetime]::new(2000, [int]$matches[1], [int]$matches[2],
                               [int]$matches[3], [int]$matches[4], [int]$matches[5], [int]$matches[6])
    }
    return $null
}

$startLine = $lines | Where-Object { $_ -match 'Created VideoCapture Instance' } | Select-Object -First 1
$endLine   = $lines | Where-Object { $_ -match 'Stopped Video Capture Mode|Stopped Recording Video' } | Select-Object -Last 1

if (-not $startLine) { throw "No 'Created VideoCapture Instance' marker found - is this a capture log?" }

$start = (Get-Stamp $startLine).AddSeconds(-$PadSeconds)
if ($endLine) {
    $end = (Get-Stamp $endLine).AddSeconds($PadSeconds)
} else {
    Write-Warning "No capture-stop marker found; keeping everything after the start marker."
    $end = [datetime]::MaxValue
}

Write-Host "  session window: $($start.ToString('MM-dd HH:mm:ss.fff')) -> $($end.ToString('MM-dd HH:mm:ss.fff'))"

# --- Filter -----------------------------------------------------------------------------------
$noiseSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$NoiseTags, [StringComparer]::OrdinalIgnoreCase)

$kept = [System.Collections.Generic.List[string]]::new()
$droppedTime = 0
$droppedNoise = 0

foreach ($line in $lines) {
    $stamp = Get-Stamp $line
    if ($null -ne $stamp -and ($stamp -lt $start -or $stamp -gt $end)) { $droppedTime++; continue }

    if ($line -match '^\S+\s+\S+\s+\d+\s+\d+\s+\w\s+([^:]+):') {
        if ($noiseSet.Contains($matches[1].Trim())) { $droppedNoise++; continue }
    }
    $kept.Add($line)
}

# --- Write ------------------------------------------------------------------------------------
$header = @(
    "# Reduced extract of $([System.IO.Path]::GetFileName($inFull)) - send alongside the full log, not instead of it."
    "# Kept: the FirstPersonStreammingCast capture session +/- ${PadSeconds}s."
    "# Removed: $droppedTime lines outside that window, $droppedNoise lines from unrelated"
    "#          high-volume tags ($($NoiseTags -join ', '))."
    "# Nothing else was filtered - this is a denylist, so unrecognised tags are preserved."
    "#"
)
$header + $kept | Set-Content -Path $outFull -Encoding utf8

$inMb = [math]::Round((Get-Item $inFull).Length / 1MB, 2)
$outMb = [math]::Round((Get-Item $outFull).Length / 1MB, 2)
Write-Host "`n  $($kept.Count) lines out -> $outFull" -ForegroundColor Green
Write-Host "  $inMb MB -> $outMb MB" -ForegroundColor Green

# Re-assert the markers survived. A reduction that drops the evidence is worse than no reduction.
Write-Host "`nEvidence check:"
foreach ($m in @('Created VideoCapture Instance', 'Started Video Capture Mode', 'OnScreenCaptureGranted', 'Started Recording Video', 'rgb recv', 'cameraResolution')) {
    $n = (Select-String -Path $outFull -SimpleMatch -Pattern $m).Count
    if ($n -gt 0) {
        Write-Host ("  [{0,4}]  {1}" -f $n, $m) -ForegroundColor Green
    } else {
        Write-Host ("  [   0]  {0}  <- LOST" -f $m) -ForegroundColor Red
    }
}
