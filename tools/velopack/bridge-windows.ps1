# The Velopack bridge test on Windows (.github/workflows/velopack-bridge.yml), on a throwaway runner only: it installs,
# updates and uninstalls the app for this account. Every step a student's computer goes through, in order:
#   1. the released Syft Notes before the bridge (-Old, the published 0.22.1 Setup.exe), set up as a student's is: start
#      at login, the background library, Claude Desktop and ChatGPT (Codex) connected, a taskbar pin, a library with a
#      class, settings and lectures;
#   2. the update to the bridge release as that copy's own updater does it (Updater.cs: the new Inno Setup.exe with the
#      same quiet, self-relaunching arguments);
#   3. the bridge copy, on its own once idle, moving over to Velopack's install (Bridge.cs, windows\bridge.ps1);
#   4. checked: the new place, start at login, the background library's entry, the Start-menu entry, the taskbar pin,
#      the AI connections (each started as its app would, and asked for the library's classes), one Syft Notes in
#      Windows' Apps list, the old install gone, settings and lectures untouched;
#   5. a later update, Velopack to Velopack (-V2Feed, with a delta), by the app's own updater;
#   6. the uninstall from Windows' Apps list: nothing of the app left, settings and lectures still there.
# The copies under test have the test helpers, only so that they ask this runner's stand-in for GitHub
# (fake-release.py, served on 127.0.0.1) and look for an update 5 seconds after starting, not 10 minutes.
param(
  [Parameter(Mandatory)][string]$Old, [Parameter(Mandatory)][string]$Bridge,
  [Parameter(Mandatory)][string]$V1Feed, [Parameter(Mandatory)][string]$V2Feed,
  [Parameter(Mandatory)][string]$V1, [Parameter(Mandatory)][string]$V2,
  [string]$Rid = "win-x64", [int]$Port = 8765
)
$ErrorActionPreference = "Stop"
$Root = (Get-Location).Path
$Res = Join-Path $Root "results"
New-Item -ItemType Directory -Force -Path $Res | Out-Null
$Results = Join-Path $Res "windows.txt"
$Wait = Join-Path $Root "tools\velopack\wait-for.ps1"
$failed = $false
function Say([string]$Line) { Add-Content -LiteralPath $Results -Value $Line; Write-Host $Line }
function Check([bool]$Ok, [string]$What) { if ($Ok) { Say "ok: $What" } else { Say "FAILED: $What"; $script:failed = $true } }
$Shell = New-Object -ComObject WScript.Shell
function Target([string]$Lnk) { if (Test-Path -LiteralPath $Lnk) { $Shell.CreateShortcut($Lnk).TargetPath } else { "" } }
function WaitFor([string]$Path, [string]$Pattern, [int]$Seconds, [string]$What) {
  & pwsh -NoProfile -File $Wait -Path $Path -Pattern $Pattern -Seconds $Seconds
  Check ($LASTEXITCODE -eq 0) $What
  return $LASTEXITCODE -eq 0
}

$oldDir = Join-Path $env:LOCALAPPDATA "Programs\Study Stash"
$oldExe = Join-Path $oldDir "StudyStash.exe"
$newRoot = Join-Path $env:LOCALAPPDATA "SyftNotes"
$newExe = Join-Path $newRoot "current\StudyStash.exe"
$lnk = Join-Path ([Environment]::GetFolderPath("Programs")) "Syft Notes.lnk"
$pin = Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Syft Notes.lnk"
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$innoKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}_is1"
$veloKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SyftNotes"
$home_ = Join-Path $env:USERPROFILE ".study-stash"
$logs = Join-Path $home_ "logs"
$claude = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
$codex = Join-Path $env:USERPROFILE ".codex\config.toml"
$docs = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Study Stash"

# The stand-in for GitHub: a folder served on this runner, holding a release (release.json, SHA256SUMS.txt) and the
# Velopack feed for this channel. The app finds it through the test helpers' switches, inherited from here.
$served = Join-Path $Root "served"
New-Item -ItemType Directory -Force -Path $served | Out-Null
function Publish([string]$Feed, [string]$Version) {
  Get-ChildItem -LiteralPath $served -File | Remove-Item -Force
  Copy-Item (Join-Path $Feed "*") $served
  & python -I (Join-Path $Root "tools\velopack\fake-release.py") $served $Version "http://127.0.0.1:$Port"
  if ($LASTEXITCODE -ne 0) { throw "fake-release.py failed" }
}
Publish $V1Feed $V1
$server = Start-Process python -ArgumentList "-m", "http.server", "$Port", "--bind", "127.0.0.1", "--directory", "`"$served`"" -PassThru -WindowStyle Hidden
$env:STUDYSTASH_RELEASES_URL = "http://127.0.0.1:$Port/release.json"
$env:STUDYSTASH_VELOPACK_FEED = "http://127.0.0.1:$Port/"
$env:STUDYSTASH_UPDATE_AFTER = "5"

try {
  # --- 1. the release before the bridge, as a student has it
  $p = Start-Process $Old -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru
  $null = $p.Handle
  if (-not $p.WaitForExit(300000)) { throw "the old Setup.exe didn't finish in 300 s" }
  Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Stop-Process -Force
  Say "before: $((& $oldExe version | Out-String).Trim()) at $oldExe"
  & $oldExe init | Out-Null
  New-Item -ItemType Directory -Force -Path $docs, (Split-Path -Parent $claude), (Split-Path -Parent $codex), (Split-Path -Parent $pin) | Out-Null
  $markers = @((Join-Path $home_ "bridge-test.txt"), (Join-Path $docs "bridge-test.txt"))
  foreach ($m in $markers) { [Guid]::NewGuid().ToString() | Set-Content -LiteralPath $m }
  $before = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
  # What the app itself writes for these (Desktop.StartAtLogin, BackgroundLibrary.Register, ClaudeSetup.Connect).
  Set-ItemProperty -Path $run -Name "Study Stash" -Value "`"$oldExe`" --background"
  Set-ItemProperty -Path $run -Name "Syft Notes background" -Value "`"$oldExe`" --home `"$home_`" helper --keep-alive"
  @{ mcpServers = @{ "syft-notes" = @{ command = $oldExe; args = @("mcp", "--client", "claude-desktop") } } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $claude
  "[mcp_servers.syft-notes]`ncommand = '$oldExe'`nargs = [`"mcp`", `"--client`", `"codex`"]`n" | Set-Content -LiteralPath $codex
  $s = $Shell.CreateShortcut($pin); $s.TargetPath = $oldExe; $s.WorkingDirectory = $oldDir; $s.Save()

  # --- 2. the old copy's own update to the bridge release: its Setup.exe, quietly, reopening the app (Updater.cs)
  $p = Start-Process $Bridge -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/relaunch=1", "/LOG=`"$Res\inno-update.log`"" -PassThru
  $null = $p.Handle
  if (-not $p.WaitForExit(300000)) { throw "the bridge's Inno Setup.exe didn't finish in 300 s" }
  Say "the old updater's step: Inno Setup.exe exit $($p.ExitCode); the app now says $((& $oldExe version | Out-String).Trim())"

  # --- 3. the bridge copy moves itself over once idle (its first look for an update, 5 s after it opened)
  $null = WaitFor (Join-Path $logs "update.log") "\[bridge\] done" 600 "the bridge copy moved itself over to Velopack's install"
  $null = WaitFor $claude "SyftNotes" 240 "the new copy, on its first start, put the AI connections right"
  Get-Content (Join-Path $logs "update.log") | Select-String "\[bridge\]|\[update\]" | ForEach-Object { Say "  log: $($_.Line)" }

  # --- 4. where everything stands
  Check (-not (Test-Path -LiteralPath $oldDir)) "the old install folder is gone"
  Check (-not (Test-Path -Path $innoKey)) "the Inno Setup uninstall entry is gone"
  Check (-not (Test-Path -LiteralPath (Join-Path $home_ "bridge-failed.txt"))) "the new copy cleared bridge-failed.txt once the old install was gone"
  $link = (Get-ItemProperty -Path "HKCU:\Software\Classes\studystash\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
  Check (-not $link -or $link -like "*$newExe*") "the studystash: link (a click on a notification) opens the new copy ($(if ($link) { $link } else { 'not registered' }))"
  Check ((Test-Path -LiteralPath $newExe) -and ((& $newExe version | Out-String).Trim() -eq $V1)) "Velopack's copy is at $newExe and runs $V1"
  $apps = @(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq "Syft Notes" })
  Check ($apps.Count -eq 1 -and $apps[0].PSChildName -eq "SyftNotes") "one Syft Notes in Windows' Apps list, Velopack's ($(($apps | ForEach-Object PSChildName) -join ', '))"
  Check ((Target $lnk) -eq $newExe) "the Start-menu entry opens the new copy ($(Target $lnk))"
  Check ((Target $pin) -eq $newExe) "the taskbar pin opens the new copy ($(Target $pin))"
  $values = Get-ItemProperty -Path $run
  Check ([string]$values."Study Stash" -like "*$newExe*") "start at login starts the new copy: $($values.'Study Stash')"
  # The background library's entry: pointed at the new copy, or taken away by the app itself (it was never turned on
  # in this test's settings); never left naming the old program.
  $bg = [string]$values."Syft Notes background"
  Check (-not $bg -or $bg -like "*$newExe*") "the background library's entry doesn't name the old program ($(if ($bg) { $bg } else { 'taken away by the app: not turned on here' }))"
  Check ((Get-Content (Join-Path $logs "update.log") -Raw) -match "Run entry 'Study Stash' now starts the new copy") "the move pointed start at login at the new copy itself"
  Check (((Get-Content -LiteralPath $claude -Raw | ConvertFrom-Json).mcpServers."syft-notes".command) -eq $newExe) "Claude Desktop's connection starts the new copy"
  Check ((Get-Content -LiteralPath $codex -Raw) -like "*SyftNotes\\current\\StudyStash.exe*" -or (Get-Content -LiteralPath $codex -Raw) -like "*SyftNotes\current\StudyStash.exe*") "ChatGPT's (Codex's) connection starts the new copy"
  $after = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
  Check (-not (Compare-Object $before $after)) "settings (~\.study-stash) and lectures (Documents\Study Stash) are untouched"
  # The library, from the new copy, read through each AI app's connection as the app would start it.
  $library = Start-Process $newExe -ArgumentList "serve" -PassThru -WindowStyle Hidden
  & python -I (Join-Path $Root "tools\velopack\wait-port.py") 8787 120
  Check ($LASTEXITCODE -eq 0) "the library runs from the new copy"
  & python -I (Join-Path $Root "tools\velopack\mcp-check.py") $claude "Example 101" 60
  Check ($LASTEXITCODE -eq 0) "Claude Desktop's connection reaches the library and lists its class"
  & python -I (Join-Path $Root "tools\velopack\mcp-check.py") $codex "Example 101" 60
  Check ($LASTEXITCODE -eq 0) "ChatGPT's (Codex's) connection reaches the library and lists its class"
  Stop-Process -Id $library.Id -Force -ErrorAction SilentlyContinue

  # --- 5. a later release, Velopack to Velopack, installed by the app's own updater once idle
  Publish $V2Feed $V2
  Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Process $newExe -ArgumentList "--background"
  $null = WaitFor (Join-Path $logs "app.log") "Syft Notes $([regex]::Escape($V2)) starting" 600 "the app updated itself to $V2 through Velopack and opened again"
  Check (((& $newExe version | Out-String).Trim()) -eq $V2) "$newExe runs $V2"
  Check ((Target $lnk) -eq $newExe) "the Start-menu entry still opens it"
  Get-Content (Join-Path $logs "app.log") | Select-String "\[update\]" | Select-Object -Last 6 | ForEach-Object { Say "  log: $($_.Line)" }
  $velopackLog = Join-Path $env:LOCALAPPDATA "velopack\velopack_SyftNotes.log"
  if (Test-Path -LiteralPath $velopackLog) { Select-String -LiteralPath $velopackLog -Pattern "delta" | Select-Object -Last 3 | ForEach-Object { Say "  velopack: $($_.Line.Trim())" } }

  # --- 6. the uninstall, as Windows' Apps list does it
  Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Stop-Process -Force
  $quiet = (Get-ItemProperty -Path $veloKey).QuietUninstallString
  $u = Start-Process "cmd.exe" -ArgumentList "/c", $quiet -PassThru
  $null = $u.Handle
  $null = $u.WaitForExit(300000)
  & pwsh -NoProfile -File $Wait -Path $newRoot -Gone -Seconds 120
  Check ($LASTEXITCODE -eq 0) "uninstalled: $newRoot is gone"
  Check (-not (Test-Path -LiteralPath $lnk)) "the Start-menu entry is gone"
  Check (-not (Test-Path -LiteralPath $pin)) "the taskbar pin is gone"
  Check (-not (Test-Path -Path $veloKey)) "the Apps list entry is gone"
  $values = Get-ItemProperty -Path $run -ErrorAction SilentlyContinue
  $left = @($values.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" -and ([string]$_.Value) -like "*SyftNotes*" } | ForEach-Object Name)
  Check ($left.Count -eq 0) "no start-at-login entry is left ($($left -join ', '))"
  Check (-not (Test-Path "HKCU:\Software\Classes\studystash")) "the studystash: link is gone"
  $after = $markers | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
  Check (-not (Compare-Object $before $after)) "settings and lectures are still there, untouched"

  # --- 7. a move whose uninstall can't finish (something holds a file in the old folder): Velopack's copy is the one
  # that opens, and the move is remembered as unfinished, so the old copy, if opened again, doesn't do it all over.
  Publish $V1Feed $V1
  # Every move writes "remembered the move ... as unfinished" (the new copy clears it once the old folder is gone), so
  # this step's waits start from a fresh log, not step 3's lines.
  Move-Item -LiteralPath (Join-Path $logs "update.log") -Destination (Join-Path $Res "update-before-step7.log") -Force
  $failedFile = Join-Path $home_ "bridge-failed.txt"
  Remove-Item -LiteralPath $failedFile -ErrorAction SilentlyContinue
  $p = Start-Process $Old -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru
  $null = $p.Handle; $null = $p.WaitForExit(300000)
  Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Stop-Process -Force
  # Blocks on an event that never comes (no timer): it only holds the file open until it's stopped below.
  $holder = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList "-NoProfile", "-Command",
    "`$f = [IO.File]::Open('$oldDir\held-open.txt', 'OpenOrCreate', 'ReadWrite', 'None'); Wait-Event -SourceIdentifier never-comes"
  $p = Start-Process $Bridge -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/relaunch=1" -PassThru
  $null = $p.Handle; $null = $p.WaitForExit(300000)
  $null = WaitFor (Join-Path $logs "update.log") "remembered the move to v$([regex]::Escape($V1)) as unfinished" 900 "a move whose old folder couldn't go is remembered as unfinished"
  $null = WaitFor (Join-Path $logs "update.log") "opening $([regex]::Escape($newExe))" 60 "and it's Velopack's copy that opens, not the old one"
  Check ((Test-Path -LiteralPath $failedFile) -and ((Get-Content -LiteralPath $failedFile -Raw).Trim() -like "v$V1*")) "bridge-failed.txt names v$V1, so the old copy won't move again for a week"
  Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue
  Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Stop-Process -Force
  if (Test-Path -Path $veloKey) { $u = Start-Process "cmd.exe" -ArgumentList "/c", (Get-ItemProperty -Path $veloKey).QuietUninstallString -PassThru; $null = $u.Handle; $null = $u.WaitForExit(300000) }
} finally {
  Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
  Copy-Item (Join-Path $logs "*.log") $Res -ErrorAction SilentlyContinue
  Get-ChildItem (Join-Path $env:LOCALAPPDATA "velopack") -Filter "*.log" -ErrorAction SilentlyContinue | Copy-Item -Destination $Res
}
if ($failed) { exit 1 }
