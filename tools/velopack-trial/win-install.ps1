# The Velopack trial's Windows install, update and uninstall (.github/workflows/velopack-trial.yml), on a throwaway
# runner only. Reads what the packing job made, in out\: v1\<rid>\ (version 1's Setup.exe) and releases\<rid>\ (the
# feed, with version 2 and its delta). Results go to results\.
#   pwsh tools/velopack-trial/win-install.ps1 -Rid win-x64
param([Parameter(Mandatory)][string]$Rid)
$ErrorActionPreference = "Stop"
$Root = (Get-Location).Path
$Res = Join-Path $Root "results"
New-Item -ItemType Directory -Force -Path (Join-Path $Res "logs") | Out-Null
$Results = Join-Path $Res "$Rid.txt"
$Wait = Join-Path $Root "tools\velopack-trial\wait-for.ps1"
$Base = (Select-String -Path "engine\Directory.Build.props" -Pattern '<StudyStashVersion>(.*)</StudyStashVersion>').Matches[0].Groups[1].Value
$V1 = "$Base-trial.1"; $V2 = "$Base-trial.2"
$Feed = Join-Path $Root "out\releases\$Rid"
$App = Join-Path $env:LOCALAPPDATA "SyftNotes"
$Exe = Join-Path $App "current\StudyStash.exe"
$Lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Syft Notes.lnk"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SyftNotes"
function Say([string]$Line) { Add-Content -LiteralPath $Results -Value $Line; Write-Host $Line }
function Target([string]$Path) { (New-Object -ComObject WScript.Shell).CreateShortcut($Path).TargetPath }
function Run([string]$File, [string[]]$Arguments, [int]$Seconds) {
  $p = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
  $null = $p.Handle
  if (-not $p.WaitForExit($Seconds * 1000)) { $p.Kill(); throw "$File didn't finish in $Seconds s" }
  return $p.ExitCode
}

# Settings and lectures that must outlive install, update and uninstall.
$docs = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Study Stash"
$markers = @((Join-Path $env:USERPROFILE ".study-stash\velopack-trial.txt"), (Join-Path $docs "velopack-trial.txt"))
foreach ($m in $markers) {
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $m) | Out-Null
  [Guid]::NewGuid().ToString() | Set-Content -LiteralPath $m
}
$before = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }

# Install version 1, silently.
$setup = (Get-ChildItem "out\v1\$Rid\*-Setup.exe").FullName
$clock = [Diagnostics.Stopwatch]::StartNew()
$code = Run $setup @("--silent", "--log", (Join-Path $Res "logs\$Rid-setup.log")) 300
Say "$Rid install: Setup.exe --silent exit $code in $([int]$clock.Elapsed.TotalSeconds) s"
if ($code -ne 0 -or -not (Test-Path -LiteralPath $Exe)) { throw "version 1 isn't installed at $Exe" }
Say "$Rid install path: $App -> $((Get-ChildItem -LiteralPath $App -Name) -join ', ')"
Say "$Rid start menu: $(if (Test-Path -LiteralPath $Lnk) { Target $Lnk } else { 'none' }); desktop: $(if (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Syft Notes.lnk')) { 'yes' } else { 'none' })"
$entry = Get-ItemProperty -Path $UninstallKey -ErrorAction SilentlyContinue
Say "$Rid apps list: $(if ($entry) { "$($entry.DisplayName) $($entry.DisplayVersion), quiet uninstall: $($entry.QuietUninstallString)" } else { 'no entry' })"
Say "$Rid running after a silent install: $(@(Get-Process -Name StudyStash -ErrorAction SilentlyContinue).Count) StudyStash process(es)"
$printed = (& $Exe version | Out-String).Trim()
Say "$Rid installed version: $printed"
if ($printed -ne $V1) { throw "installed version '$printed', expected $V1" }
$signed = Get-AuthenticodeSignature -FilePath $Exe
Say "$Rid StudyStash.exe signature: $($signed.Status) by $($signed.SignerCertificate.Subject)"

# Update to version 2 through the app's own Velopack update: it hands off to Update.exe and quits; Update.exe applies
# the update and starts the new version, which writes its line to the report.
$report = Join-Path $Res "$Rid-update-report.txt"
$clock.Restart()
$code = Run $Exe @("velopack-trial-update", $Feed, $report) 300
Say "$Rid update: the command exited $code after $([int]$clock.Elapsed.TotalSeconds) s"
& pwsh -NoProfile -File $Wait -Path $report -Pattern " relaunched " -Seconds 300
$relaunched = $LASTEXITCODE -eq 0
Say "$Rid update: $(if ($relaunched) { "relaunched after $([int]$clock.Elapsed.TotalSeconds) s" } else { 'NO relaunch within 300 s' })"
Get-Content -LiteralPath $report | ForEach-Object { Say "$Rid report: $_" }
Get-ChildItem $App -Recurse -Filter "*.log" -ErrorAction SilentlyContinue | Copy-Item -Destination (Join-Path $Res "logs") -ErrorAction SilentlyContinue
Get-ChildItem $App -Recurse -Filter "*.log" -ErrorAction SilentlyContinue | Select-String -Pattern "delta" | Select-Object -Last 4 | ForEach-Object { Say "$Rid log: $($_.Line.Trim())" }
$printed = (& $Exe version | Out-String).Trim()
Say "$Rid after the update $Exe runs: $printed; start menu: $(if (Test-Path -LiteralPath $Lnk) { Target $Lnk } else { 'none' })"
if (-not $relaunched -or $printed -ne $V2) { throw "the update to $V2 didn't take" }

# Uninstall, silently, the way Windows' Apps list would (its quiet command).
$entry = Get-ItemProperty -Path $UninstallKey
$clock.Restart()
$code = Run "cmd.exe" @("/c", $entry.QuietUninstallString) 300
& pwsh -NoProfile -File $Wait -Path (Join-Path $App "current") -Gone -Seconds 120
Say "$Rid uninstall: exit $code after $([int]$clock.Elapsed.TotalSeconds) s; left in $App : $(if (Test-Path -LiteralPath $App) { (Get-ChildItem -LiteralPath $App -Recurse -Name) -join ', ' } else { 'nothing (folder gone)' })"
Say "$Rid uninstall: start menu $(if (Test-Path -LiteralPath $Lnk) { 'STILL THERE' } else { 'gone' }); apps list $(if (Test-Path -Path $UninstallKey) { 'STILL THERE' } else { 'gone' })"
$after = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
if (Compare-Object $before $after) { throw "settings or lectures changed" }
Say "$Rid uninstall clean: ~/.study-stash and Documents untouched"
Get-ChildItem $env:TEMP -Filter "velopack*.log" -ErrorAction SilentlyContinue | Copy-Item -Destination (Join-Path $Res "logs") -ErrorAction SilentlyContinue
