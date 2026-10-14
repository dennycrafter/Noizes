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
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class NzWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

  // the visible top-level "Noizes" window of the given process, or zero
  public static IntPtr FindSettings(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString() == "Noizes") { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

$dataDir = Join-Path $env:APPDATA 'Noizes'
$logPath = Join-Path $dataDir 'log.txt'
$cfgPath = Join-Path $dataDir 'config.json'
$script:fails = @()

function Report([string]$name, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Host "PASS $name - $detail" }
  else {
    Write-Host "FAIL $name - $detail"
    Write-Host "::error title=Launch test: $name::$detail"
    $script:fails += $name
  }
}

function Stop-Noizes {
  Get-Process Noizes -ErrorAction SilentlyContinue | Stop-Process -Force
  # let WebView2's browser processes for the old copy wind down before the next start
  $deadline = (Get-Date).AddSeconds(15)
  while ((Get-Process Noizes -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
  Start-Sleep -Seconds 2
}

function Get-NoizesPid {
  $p = Get-Process Noizes -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($p) { return [uint32]$p.Id } else { return [uint32]0 }
}

function Wait-Window([int]$seconds) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    $procId = Get-NoizesPid
    if ($procId -ne 0) {
      $h = [NzWin]::FindSettings($procId)
      if ($h -ne [IntPtr]::Zero) { return $h }
    }
    Start-Sleep -Milliseconds 250
  }
  return [IntPtr]::Zero
}

function Count-Loads {
  if (-not (Test-Path $logPath)) { return 0 }
  return @(Select-String -Path $logPath -SimpleMatch 'bridge: getState answered').Count
}

function Wait-Loads([int]$atLeast, [int]$seconds) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    if ((Count-Loads) -ge $atLeast) { return $true }
    Start-Sleep -Milliseconds 500
  }
  return $false
}

# on screen: inside one screen's working area, allowing for the invisible resize borders
function Test-OnScreen([IntPtr]$h) {
  $r = New-Object NzWin+RECT
  [void][NzWin]::GetWindowRect($h, [ref]$r)
  $tol = 12
  foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    $a = $s.WorkingArea
    if ($r.Left -ge $a.Left - $tol -and $r.Top -ge $a.Top - $tol -and
        $r.Right -le $a.Right + $tol -and $r.Bottom -le $a.Bottom + $tol) {
      return @{ ok = $true; text = "($($r.Left),$($r.Top))-($($r.Right),$($r.Bottom)) inside $a" }
    }
  }
  return @{ ok = $false; text = "($($r.Left),$($r.Top))-($($r.Right),$($r.Bottom)) is not inside any screen" }
}

function Check-Window([string]$name, [IntPtr]$h, [int]$loadsWanted) {
  if ($h -eq [IntPtr]::Zero) { Report $name $false 'no settings window appeared'; return }
  $minimized = [NzWin]::IsIconic($h)
  $place = Test-OnScreen $h
  Report "$name-on-screen" ($place.ok -and -not $minimized) ($place.text + $(if ($minimized) { ', minimized' } else { '' }))
  $loaded = Wait-Loads $loadsWanted 60
  Report "$name-settings-loaded" $loaded "the window asked for its settings and the app answered ($(Count-Loads) total)"
  # focus is best effort on a CI desktop, so it is reported but never fails the run
  $front = [NzWin]::GetForegroundWindow() -eq $h
  Write-Host "INFO $name in front: $front"
}

function Close-Window([IntPtr]$h) {
  [void][NzWin]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
  $deadline = (Get-Date).AddSeconds(10)
  while ((Get-Date) -lt $deadline -and [NzWin]::IsWindowVisible($h)) { Start-Sleep -Milliseconds 250 }
}

function Set-SavedWindow([int]$x, [int]$y, [int]$w, [int]$hgt) {
  $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
  $cfg.Window.X = $x; $cfg.Window.Y = $y; $cfg.Window.Width = $w; $cfg.Window.Height = $hgt
  $cfg.Window.Maximized = $false
  $cfg | ConvertTo-Json -Depth 20 | Set-Content $cfgPath -Encoding UTF8
}

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
exit 0
