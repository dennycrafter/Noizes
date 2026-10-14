# Install test: installs a new Noizes build over an older release the way a person
# upgrades, with the old copy still running in the tray, then checks that every
# setting survived and the settings window opens with them loaded.
#
#   pwsh scripts/install-test.ps1 -NewInstaller installer\Output\Noizes-x-setup.exe -OldTags v1.2.0,v1.3.0-beta.2
#
# Old installers come from the repo's GitHub releases (needs GH_TOKEN for gh).
# It wipes %APPDATA%\Noizes and %LOCALAPPDATA%\Noizes, so run it on a CI machine only.

param(
  [Parameter(Mandatory = $true)][string]$NewInstaller,
  [string[]]$OldTags = @('v1.2.0', 'v1.3.0-beta.2')
)

$ErrorActionPreference = 'Stop'
$NewInstaller = (Resolve-Path $NewInstaller).Path
. "$PSScriptRoot/test-helpers.ps1"

$installDir = Join-Path $env:LOCALAPPDATA 'Noizes'
$installedExe = Join-Path $installDir 'Noizes.exe'
$silent = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')

# a config shaped like a real v1.2 user's: custom sounds, volumes, GitHub, quiet hours, uptime
$seed = [ordered]@{
  Port = 7351
  StartWithWindows = $true
  OnlyWhenUnfocused = $false
  Quiet = [ordered]@{ Enabled = $true; Start = '23:30'; End = '07:15'; AllowAlarms = $false }
  GitHub = [ordered]@{ Token = 'test-token-not-real'; Username = 'someone'; PollSeconds = 30; Repos = @('someone/app', 'someone/site') }
  UptimeUrls = @('https://example.com/health')
  Countdown = [ordered]@{ Enabled = $true; TargetLocal = '2030-01-01T09:00:00' }
  Features = [ordered]@{ ClaudeDesktopWatcher = $true }
  Events = [ordered]@{
    'claude-code-done'    = [ordered]@{ Enabled = $true;  Volume = 80; SoundPath = 'C:\Sounds\Victory Royale.mp3'; FocusApps = @() }
    'gh-pr-merged'        = [ordered]@{ Enabled = $true;  Volume = 60; SoundPath = 'C:\Sounds\Level Up.mp3'; FocusApps = @() }
    'claude-desktop-done' = [ordered]@{ Enabled = $true;  Volume = 55; SoundPath = ''; FocusApps = @('claude') }
    'gh-star'             = [ordered]@{ Enabled = $false; Volume = 25; SoundPath = ''; FocusApps = @() }
  }
}

function Wipe {
  Stop-Noizes
  foreach ($d in @($dataDir, $installDir)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
}

function Install([string]$path, [string]$label) {
  $p = Start-Process $path -ArgumentList $silent -Wait -PassThru
  Report "$label-installer-exit" ($p.ExitCode -eq 0) "exit code $($p.ExitCode)"
  Report "$label-exe-present" (Test-Path $installedExe) $installedExe
}

function Compare-Seed([string]$label) {
  if (-not (Test-Path $cfgPath)) { Report "$label-config-kept" $false 'config.json is gone'; return }
  $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
  $problems = @()
  foreach ($k in @('Port', 'StartWithWindows', 'OnlyWhenUnfocused')) {
    if ($cfg.$k -ne $seed[$k]) { $problems += "$k is $($cfg.$k), was $($seed[$k])" }
  }
  foreach ($k in @('Enabled', 'Start', 'End', 'AllowAlarms')) {
    if ($cfg.Quiet.$k -ne $seed.Quiet[$k]) { $problems += "Quiet.$k changed" }
  }
  foreach ($k in @('Token', 'Username', 'PollSeconds')) {
    if ($cfg.GitHub.$k -ne $seed.GitHub[$k]) { $problems += "GitHub.$k changed" }
  }
  if (($cfg.GitHub.Repos -join ',') -ne ($seed.GitHub.Repos -join ',')) { $problems += 'GitHub.Repos changed' }
  if (($cfg.UptimeUrls -join ',') -ne ($seed.UptimeUrls -join ',')) { $problems += 'UptimeUrls changed' }
  if ($cfg.Countdown.Enabled -ne $seed.Countdown.Enabled) { $problems += 'Countdown.Enabled changed' }
  if ($cfg.Features.ClaudeDesktopWatcher -ne $seed.Features.ClaudeDesktopWatcher) { $problems += 'Features.ClaudeDesktopWatcher changed' }
  foreach ($id in $seed.Events.Keys) {
    $want = $seed.Events[$id]
    $got = $cfg.Events.$id
    if ($null -eq $got) { $problems += "Events.$id is gone"; continue }
    foreach ($k in @('Enabled', 'Volume', 'SoundPath')) {
      if ($got.$k -ne $want[$k]) { $problems += "Events.$id.$k is '$($got.$k)', was '$($want[$k])'" }
    }
    if (($got.FocusApps -join ',') -ne ($want.FocusApps -join ',')) { $problems += "Events.$id.FocusApps changed" }
  }
  $detail = if ($problems.Count -eq 0) { 'every seeded setting is unchanged' } else { $problems -join '; ' }
  Report "$label-settings-kept" ($problems.Count -eq 0) $detail
  Report "$label-new-fields-added" ($null -ne $cfg.Window -and $null -ne $cfg.PSObject.Properties['Muted']) 'Window and Muted present after the upgrade'
}

# ---------------------------------------------------------------- run

$work = Join-Path ([IO.Path]::GetTempPath()) ('noizes-old-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null

foreach ($tag in $OldTags) {
  $label = "upgrade-from-$tag"
  Write-Host "==== $label ===="
  Wipe

  $dl = Join-Path $work $tag
  New-Item -ItemType Directory $dl -Force | Out-Null
  gh release download $tag --repo $env:GITHUB_REPOSITORY --pattern 'Noizes-*-setup.exe' --dir $dl --clobber
  $old = Get-ChildItem $dl -Filter 'Noizes-*-setup.exe' | Select-Object -First 1
  if (-not $old) { Report "$label-download" $false "no installer attached to $tag"; continue }

  # the old version, set up and running in the tray, like a real PC before an upgrade
  Install $old.FullName "$label-old"
  New-Item -ItemType Directory $dataDir -Force | Out-Null
  $seed | ConvertTo-Json -Depth 10 | Set-Content $cfgPath -Encoding UTF8
  Start-Process $installedExe | Out-Null
  Start-Sleep -Seconds 8
  Report "$label-old-running" ((Get-NoizesPid) -ne 0) 'old version running in the tray before the upgrade'

  # upgrade over it while it runs; the installer closes the old copy itself
  Install $NewInstaller "$label-new"
  Start-Sleep -Seconds 2
  Stop-Noizes # a silent install never relaunches; the real installer's last page does that next

  # the installer's "Launch Noizes" step: the window must open with settings loaded
  $loads = Count-Loads
  Start-Process $installedExe | Out-Null
  $h = Wait-Window 60
  Check-Window "$label-first-open" $h ($loads + 1)
  if ($h -ne [IntPtr]::Zero) { Close-Window $h }
  Start-Sleep -Seconds 2
  Compare-Seed $label
  Stop-Noizes
}

Write-Host ''
Write-Host '---- Noizes log ----'
if (Test-Path $logPath) { Get-Content $logPath | Select-Object -Last 40 }

if ($script:fails.Count -gt 0) {
  Write-Host "INSTALL TEST: $($script:fails.Count) failed: $($script:fails -join ', ')"
  exit 1
}
Write-Host 'INSTALL TEST: all cases passed'
Write-Host "::notice title=Install test passed::Upgrades from $($OldTags -join ' and ') kept every setting and opened the window with settings loaded"
exit 0
