# Noizes helper.
# Usage:  noizes setup                (wire Claude Code + Cursor hooks and turn on the four core events; safe to run twice)
#         noizes run <command...>     (plays a sound if the command took > 30 seconds)
#         noizes test [eventId]       (plays a sound now; defaults to long-command-done)
$ErrorActionPreference = 'Continue'
$apiPort = 7351

$cfg = Join-Path $env:APPDATA "Noizes\config.json"
if (Test-Path $cfg) {
    try {
        $j = Get-Content $cfg -Raw | ConvertFrom-Json
        if ($j.Port) { $apiPort = [int]$j.Port }
    } catch { }
}

function Send-Event([string]$id) {
    try {
        Invoke-RestMethod -Method Post -Uri ("http://127.0.0.1:{0}/event/{1}" -f $apiPort, $id) -TimeoutSec 2 | Out-Null
    } catch { }
}

if ($args.Count -eq 0) {
    Write-Host "Usage: noizes setup                  (wire Claude Code + Cursor hooks and turn on the four core events; safe to run twice)"
    Write-Host "       noizes run <command...>       (sound when the command takes over 30 seconds)"
    Write-Host "       noizes test [eventId]         (play an event's sound now)"
    exit 2
}

if ($args[0] -eq 'setup') {
    # installed layout: run.ps1 sits next to Noizes.exe in {app}; repo layouts differ
    $exe = $null
    foreach ($cand in @(
        (Join-Path $PSScriptRoot 'Noizes.exe'),
        (Join-Path $PSScriptRoot '..\Noizes.exe'),
        (Join-Path $PSScriptRoot '..\publish\Noizes.exe'))) {
        if (Test-Path $cand) { $exe = $cand; break }
    }
    if (-not $exe) {
        Write-Host "[noizes] Noizes.exe not found (looked next to run.ps1 and in ..\ and ..\publish\)."
        Write-Host "[noizes] Install Noizes, or publish first: dotnet publish src/Noizes/Noizes.csproj -c Release -o publish"
        exit 1
    }
    & $exe --setup
    exit $LASTEXITCODE
}

if ($args[0] -eq 'test') {
    # one [noizes] result line: event id, port, HTTP result - silence is no longer an outcome
    $eventId = if ($args.Count -gt 1) { $args[1] } else { 'long-command-done' }
    try {
        $resp = Invoke-WebRequest -Method Post -Uri ("http://127.0.0.1:{0}/event/{1}" -f $apiPort, $eventId) -TimeoutSec 2 -UseBasicParsing
        Write-Host ("[noizes] test: event '{0}' on port {1} - HTTP {2}, sound queued" -f $eventId, $apiPort, [int]$resp.StatusCode)
    } catch {
        $code = $null
        try { $code = [int]$_.Exception.Response.StatusCode } catch { }
        if ($code) {
            Write-Host ("[noizes] test: event '{0}' on port {1} - HTTP {2} - check the event id" -f $eventId, $apiPort, $code)
        } else {
            Write-Host ("[noizes] test: event '{0}' on port {1} - no answer ({2}) - is Noizes running?" -f $eventId, $apiPort, $_.Exception.Message)
        }
    }
    exit 0
}

$argList = $args
if ($argList[0] -eq 'run' -or $argList[0] -eq '--') {
    if ($argList.Count -lt 2) { Write-Host "Nothing to run after 'run'."; exit 2 }
    $argList = $argList[1..($argList.Count - 1)]
}

$cmd = $argList[0]
$rest = @()
if ($argList.Count -gt 1) { $rest = $argList[1..($argList.Count - 1)] }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $cmd @rest
$exit = $LASTEXITCODE
$sw.Stop()
$seconds = [int]$sw.Elapsed.TotalSeconds

if ($seconds -gt 30) {
    Send-Event 'long-command-done'
    Write-Host ""
    Write-Host ("[noizes] command took {0}s - played the 'long command finished' sound" -f $seconds)
} else {
    Write-Host ""
    Write-Host ("[noizes] command took {0}s - under 30s, no sound" -f $seconds)
}
exit $exit
