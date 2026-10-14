# Launch test: runs the real Noizes.exe the way a person uses it and checks the
# settings window shows up on screen with real settings loaded.
#
#   pwsh scripts/launch-test.ps1 -Exe path\to\Noizes.exe
#
# Cases:
#   1. first open on a fresh PC: the window opens on screen and loads settings
#   2. closing the window keeps Noizes running in the tray
#   3. opening Noizes again while it runs in the tray brings the window back
#   4. a Windows sign-in start (--background) stays quietly in the tray
#   5. then opening Noizes from the Start menu still brings the window up
#   6. a saved window position far off screen is pulled back on screen
#   7. a saved window bigger than the screen is shrunk to fit
#
# Every failure prints a GitHub annotation, and the script exits 1 if any case failed.
# It deletes and recreates %APPDATA%\Noizes, so run it on a CI machine or a test PC only.

param([Parameter(Mandatory = $true)][string]$Exe)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
. "$PSScriptRoot/test-helpers.ps1"

# ---------------------------------------------------------------- run

Stop-Noizes
if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }

# 1. first open on a fresh PC
Start-Process $Exe | Out-Null
$h = Wait-Window 60
Check-Window 'first-open' $h 1

# 2. close the window: Noizes keeps running in the tray
if ($h -ne [IntPtr]::Zero) { Close-Window $h }
Start-Sleep -Seconds 2
Report 'close-keeps-tray' ((Get-NoizesPid) -ne 0) 'Noizes still running after the window closed'
Report 'close-saves-config' (Test-Path $cfgPath) 'config.json written on close'

# 3. open Noizes again while it runs in the tray
$loads = Count-Loads
$second = Start-Process $Exe -PassThru
$h = Wait-Window 30
Report 'reopen-second-copy-exits' ($second.WaitForExit(10000)) 'the second launch handed over and exited'
Check-Window 'reopen-from-tray' $h ($loads + 1)

# 4. a Windows sign-in start stays in the tray
Stop-Noizes
Start-Process $Exe -ArgumentList '--background' | Out-Null
Start-Sleep -Seconds 10
$procId = Get-NoizesPid
Report 'signin-start-running' ($procId -ne 0) 'Noizes started in the tray'
Report 'signin-start-quiet' ($procId -ne 0 -and ([NzWin]::FindSettings($procId) -eq [IntPtr]::Zero)) 'no window popped up at sign-in'

# 5. then the Start menu brings the window up
$loads = Count-Loads
Start-Process $Exe | Out-Null
$h = Wait-Window 30
Check-Window 'open-after-signin' $h ($loads + 1)

# 6. a saved spot far off screen comes back on screen
Stop-Noizes
Set-SavedWindow -32000 -32000 1080 720
Start-Process $Exe | Out-Null
$h = Wait-Window 60
if ($h -eq [IntPtr]::Zero) { Report 'offscreen-saved' $false 'no settings window appeared' }
else { $p = Test-OnScreen $h; Report 'offscreen-saved-on-screen' $p.ok $p.text }

# 7. a saved size bigger than the screen is shrunk to fit
Stop-Noizes
Set-SavedWindow 0 0 6000 4000
Start-Process $Exe | Out-Null
$h = Wait-Window 60
if ($h -eq [IntPtr]::Zero) { Report 'oversized-saved' $false 'no settings window appeared' }
else { $p = Test-OnScreen $h; Report 'oversized-saved-on-screen' $p.ok $p.text }

Stop-Noizes

Write-Host ''
Write-Host '---- Noizes log ----'
if (Test-Path $logPath) { Get-Content $logPath | Select-Object -Last 60 }

if ($script:fails.Count -gt 0) {
  Write-Host "LAUNCH TEST: $($script:fails.Count) failed: $($script:fails -join ', ')"
  exit 1
}
Write-Host 'LAUNCH TEST: all cases passed'
Write-Host '::notice title=Launch test passed::First open, close to tray, reopen from tray, sign-in start, Start menu after sign-in, off-screen and oversized saved windows'
exit 0
