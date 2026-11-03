# The Velopack trial's bridge test (.github/workflows/velopack-trial.yml), on a throwaway Windows runner only: today's
# Inno Setup install (dist\inno\Syft-Notes-Setup.exe, from windows\build.ps1) set up the way a student's is (start at
# login on, the background library on, Claude Desktop connected, settings and lectures), then tools\velopack-trial\
# bridge.ps1 moves it to Velopack (out\v1\win-x64\*-Setup.exe), and every piece is checked. Results go to results\.
#   pwsh tools/velopack-trial/win-bridge.ps1
$ErrorActionPreference = "Stop"
$Root = (Get-Location).Path
$Res = Join-Path $Root "results"
New-Item -ItemType Directory -Force -Path (Join-Path $Res "logs") | Out-Null
$Results = Join-Path $Res "bridge.txt"
$failed = $false
function Say([string]$Line) { Add-Content -LiteralPath $Results -Value $Line; Write-Host $Line }
function Check([bool]$Ok, [string]$What) { if ($Ok) { Say "ok: $What" } else { Say "FAILED: $What"; $script:failed = $true } }
function Target([string]$Path) { (New-Object -ComObject WScript.Shell).CreateShortcut($Path).TargetPath }

$old = Join-Path $env:LOCALAPPDATA "Programs\Study Stash"
$oldExe = Join-Path $old "StudyStash.exe"
$newExe = Join-Path $env:LOCALAPPDATA "SyftNotes\current\StudyStash.exe"
$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Syft Notes.lnk"
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$home_ = Join-Path $env:USERPROFILE ".study-stash"

# Today's installer, silently, as a student's first install.
$p = Start-Process "dist\inno\Syft-Notes-Setup.exe" -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru
$null = $p.Handle
if (-not $p.WaitForExit(300000)) { throw "the Inno Setup.exe didn't finish in 300 s" }
Check ((Test-Path -LiteralPath $oldExe) -and (Target $lnk) -eq $oldExe) "today's Setup.exe installed $oldExe with its Start-menu entry"
Say "inno installed version: $((& $oldExe version | Out-String).Trim())"

# Set up as a student's is (what the app itself writes: Desktop.RunCommand, BackgroundLibrary.Register, ClaudeSetup).
New-Item -ItemType Directory -Force -Path $home_, (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Study Stash"), (Join-Path $env:APPDATA "Claude") | Out-Null
$markers = @((Join-Path $home_ "velopack-trial.txt"), (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Study Stash\velopack-trial.txt"))
foreach ($m in $markers) { [Guid]::NewGuid().ToString() | Set-Content -LiteralPath $m }
$before = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
Set-ItemProperty -Path $run -Name "Study Stash" -Value "`"$oldExe`" --background"
Set-ItemProperty -Path $run -Name "Syft Notes background" -Value "`"$oldExe`" --home $home_ helper --keep-alive"
$claude = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
@{ mcpServers = @{ "syft-notes" = @{ command = $oldExe; args = @("mcp", "--client", "claude-desktop") } } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $claude
# What the app writes for its notifications (WinNotifications): their name, and the studystash: link a click opens.
New-Item -Path "HKCU:\Software\Classes\AppUserModelId\StudyStash.App" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\AppUserModelId\StudyStash.App" -Name DisplayName -Value "Syft Notes"
New-Item -Path "HKCU:\Software\Classes\studystash\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\studystash\shell\open\command" -Name "(default)" -Value "`"$oldExe`" --open `"%1`""

# The bridge.
& pwsh -NoProfile -File (Join-Path $Root "tools\velopack-trial\bridge.ps1") -Setup (Get-ChildItem "out\v1\win-x64\*-Setup.exe").FullName -Report $Results
Check ($LASTEXITCODE -eq 0) "bridge.ps1 finished"
Copy-Item (Join-Path $Res "bridge-setup.log") (Join-Path $Res "logs") -ErrorAction SilentlyContinue

# Where everything stands.
Check (-not (Test-Path -LiteralPath $old)) "the old folder $old is gone"
Check (Test-Path -LiteralPath $newExe) "the app is at $newExe"
$printed = if (Test-Path -LiteralPath $newExe) { (& $newExe version | Out-String).Trim() } else { "" }
Check ($printed -like "*-trial.1") "the new copy runs: version $printed"
Check ((Test-Path -LiteralPath $lnk) -and (Target $lnk) -like "$env:LOCALAPPDATA\SyftNotes\*") "the Start-menu entry opens the new copy ($(if (Test-Path -LiteralPath $lnk) { Target $lnk } else { 'none' }))"
$values = Get-ItemProperty -Path $run
Check ([string]$values."Study Stash" -like "*$newExe*") "start at login (Run 'Study Stash') starts the new copy: $($values.'Study Stash')"
Check ([string]$values."Syft Notes background" -like "*$newExe*") "the background library (Run 'Syft Notes background') starts the new copy: $($values.'Syft Notes background')"
$apps = @(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq "Syft Notes" })
Check ($apps.Count -eq 1) "one Syft Notes in Windows' Apps list ($($apps.Count): $(($apps | ForEach-Object { "$($_.PSChildName) $($_.DisplayVersion)" }) -join '; '))"
$after = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
Check (-not (Compare-Object $before $after)) "settings (~/.study-stash) and lectures (Documents\Study Stash) are untouched"
# Not the bridge's job, written down for the first launch from the new place: Claude's connection still names the old
# program, and the notification registration the Inno uninstaller deletes.
$mcp = (Get-Content -LiteralPath $claude -Raw | ConvertFrom-Json).mcpServers."syft-notes".command
Say "first launch must rewrite: Claude Desktop's syft-notes command is $mcp ($(if (Test-Path -LiteralPath $mcp) { 'exists' } else { 'MISSING' }))"
Say "first launch must rewrite: notifications' HKCU\Software\Classes\AppUserModelId\StudyStash.App $(if (Test-Path 'HKCU:\Software\Classes\AppUserModelId\StudyStash.App') { 'still there' } else { 'DELETED by the Inno uninstaller' }); the studystash: link $(if (Test-Path 'HKCU:\Software\Classes\studystash') { 'still there' } else { 'DELETED by the Inno uninstaller' })"
if ($failed) { exit 1 }
