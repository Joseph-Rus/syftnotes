# The move from an Inno Setup install of Syft Notes to a Velopack one (docs/LIBRARIES.md, phase 5, the bridge release).
# The app (StudyStash.Core/Bridge.cs) writes this script to a temp folder beside Velopack's Setup.exe, which it has
# downloaded and checked, starts it hidden with Windows PowerShell 5.1 (every Windows has it), and quits. Proven first
# by tools/velopack-trial/bridge.ps1; CI's bridge job runs this very file the way the app does.
#   1. wait for the app (-WaitPid) to quit, and stop what still runs from the old folder (the background library);
#   2. run Velopack's Setup.exe --silent: it installs into %LocalAppData%\SyftNotes and opens nothing;
#   3. point this account's Run entries (start at login, the background library), and every shortcut (Start menu,
#      desktop, taskbar pins) that starts the old program, at the new one: the Inno uninstaller deletes Run entries
#      that still name its folder, and a pin to a deleted program shows a blank icon;
#   4. keep a copy of Velopack's Start-menu entry, which has the same name as Inno's;
#   5. run the Inno uninstaller silently and wait until the old folder is gone (it ends every StudyStash.exe, so the
#      new app isn't started before this);
#   6. put the Start-menu entry back if the uninstaller took it, and open the new app (--background: no window).
# If Velopack's Setup fails, nothing else happens: the old copy opens again as it was. Settings (~\.study-stash),
# lectures and Documents are never touched. Everything it does goes to -Log.
#   powershell -NoProfile -ExecutionPolicy Bypass -File bridge.ps1 -Setup <Setup.exe> -WaitPid <pid> -Log <file>
# Whatever happens, an app opens at the end: the new copy if it's installed, or else the old one (-OldExe), and a move
# that fails is written to -FailedFile (-Tag), so the app doesn't try it again for a week (Bridge.cs).
param(
  [Parameter(Mandatory)][string]$Setup,
  [Parameter(Mandatory)][string]$Log,
  [int]$WaitPid = 0,
  [int]$Seconds = 300,
  [string]$InnoKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}_is1",
  [string]$OldExe = "",
  [string]$FailedFile = "",
  [string]$Tag = "",
  [string]$OpenArgs = "--background",
  [switch]$NoOpen
)
$ErrorActionPreference = "Stop"
# Never in a folder the move deletes: Windows keeps a folder something is working in.
Set-Location -LiteralPath ([IO.Path]::GetTempPath())
function Say([string]$Line) { Add-Content -LiteralPath $Log -Value ("{0:yyyy-MM-dd HH:mm:ss} [bridge] {1}" -f (Get-Date), $Line) }
$new = Join-Path $env:LOCALAPPDATA "SyftNotes"
$newExe = Join-Path $new "current\StudyStash.exe"
$moved = $false
$old = $null
# The end, whatever happened. Once Velopack's copy is installed it is the one that opens, even if the old copy didn't
# uninstall all the way (its folder or its Setup entry still there): the new copy never moves again, and the old one,
# if it's ever opened, finds the move remembered as failed (-FailedFile) instead of doing it all over again. Before
# that point nothing has changed, so the old copy opens, and the failure is remembered the same way.
function Finish {
  $leftover = (Test-Path -Path $InnoKey) -or ($script:old -and (Test-Path -LiteralPath $script:old))
  if ((-not $script:moved -or $leftover) -and $FailedFile -and $Tag) {
    Set-Content -LiteralPath $FailedFile -Value $Tag -ErrorAction SilentlyContinue
    Say "remembered the move to $Tag as unfinished ($(if ($script:moved) { 'the old copy is still partly there' } else { 'nothing was moved' }))"
  }
  if ($NoOpen) { return }
  $open = if ($script:moved -and (Test-Path -LiteralPath $newExe)) { $newExe } elseif ($OldExe -and (Test-Path -LiteralPath $OldExe)) { $OldExe } elseif (Test-Path -LiteralPath $newExe) { $newExe } else { $null }
  if ($open) { Say "opening $open"; Start-Process -FilePath $open -ArgumentList $OpenArgs -WorkingDirectory (Split-Path -Parent $open) } else { Say "no copy of the app to open" }
}
try {
$Shell = New-Object -ComObject WScript.Shell
function Target([string]$Lnk) { $Shell.CreateShortcut($Lnk).TargetPath }
# The same path, whatever its letters' case (PowerShell 5.1's String.Replace has no comparison argument).
function Swap([string]$Text, [string]$Old, [string]$New) { [regex]::Replace($Text, [regex]::Escape($Old), $New.Replace('$', '$$'), "IgnoreCase") }
function Has([string]$Text, [string]$Part) { $Text -and $Text.IndexOf($Part, [StringComparison]::OrdinalIgnoreCase) -ge 0 }

$inno = Get-ItemProperty -Path $InnoKey -ErrorAction SilentlyContinue
if (-not $inno -or -not $inno.InstallLocation) { throw "no Inno Setup install of Syft Notes on this account; nothing to move" }
$old = ([string]$inno.InstallLocation).TrimEnd('\')
$oldExe = Join-Path $old "StudyStash.exe"
$lnk = Join-Path ([Environment]::GetFolderPath("Programs")) "Syft Notes.lnk"
$clock = [Diagnostics.Stopwatch]::StartNew()
Say "moving $old ($($inno.DisplayVersion)) to $new"

try {
  # 1. The app quits by itself once it has started this; then nothing may run from the old folder.
  if ($WaitPid -gt 0) {
    $app = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
    if ($app -and -not $app.WaitForExit(120000)) { Say "the app didn't quit in 120 s; ending it"; Stop-Process -Id $WaitPid -Force -ErrorAction SilentlyContinue }
  }
  $left = @(Get-CimInstance Win32_Process -Filter "Name = 'StudyStash.exe'" -ErrorAction SilentlyContinue | Where-Object { Has $_.ExecutablePath $old })
  foreach ($p in $left) { Say "stopping $($p.ProcessId), still running from the old folder"; Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
  foreach ($p in $left) { Wait-Process -Id $p.ProcessId -Timeout 60 -ErrorAction SilentlyContinue }

  # 2. Velopack's Setup, silently.
  $setupLog = [IO.Path]::ChangeExtension($Log, ".setup.log")
  $p = Start-Process -FilePath $Setup -ArgumentList "--silent", "--log", "`"$setupLog`"" -PassThru
  $null = $p.Handle # keeps the exit code readable once it has ended
  if (-not $p.WaitForExit($Seconds * 1000)) { $p.Kill(); throw "Velopack's Setup didn't finish in $Seconds s" }
  if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $newExe)) { throw "Velopack's Setup ended with $($p.ExitCode) and no $newExe (its log: $setupLog)" }
  Say "Velopack's Setup installed $newExe after $([int]$clock.Elapsed.TotalSeconds) s"
} catch {
  # Nothing has changed yet: the old copy opens again, as it was (Finish, below).
  Say "the move didn't start: $($_.Exception.Message)"
  throw
}

# 3. Run entries and shortcuts that start the old program start the new one.
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$values = Get-ItemProperty -Path $run -ErrorAction SilentlyContinue
if ($values) {
  foreach ($name in @($values.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" } | ForEach-Object { $_.Name })) {
    $data = [string]$values.$name
    if (Has $data $oldExe) {
      Set-ItemProperty -Path $run -Name $name -Value (Swap $data $oldExe $newExe)
      Say "the Run entry '$name' now starts the new copy"
    }
  }
}
$places = @(
  [Environment]::GetFolderPath("Programs"),
  [Environment]::GetFolderPath("Desktop"),
  (Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
  (Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu")
)
foreach ($place in $places) {
  if (-not (Test-Path -LiteralPath $place)) { continue }
  foreach ($f in @(Get-ChildItem -LiteralPath $place -Filter "*.lnk" -File -ErrorAction SilentlyContinue)) {
    # Velopack's own Start-menu entry already starts the new copy; Inno's has the same name and is handled in 4-6.
    if ($f.FullName -eq $lnk) { continue }
    try {
      $s = $Shell.CreateShortcut($f.FullName)
      if (-not (Has $s.TargetPath $oldExe)) { continue }
      $s.TargetPath = $newExe
      $s.WorkingDirectory = Split-Path -Parent $newExe
      if (Has $s.IconLocation $old) { $s.IconLocation = "$newExe,0" }
      $s.Save()
      Say "the shortcut $($f.FullName) now starts the new copy"
    } catch {
      Say "couldn't change the shortcut $($f.FullName): $($_.Exception.Message)"
    }
  }
}

# 4. Velopack's Start-menu entry, kept.
$kept = $null
if ((Test-Path -LiteralPath $lnk) -and (Has (Target $lnk) $new)) {
  $kept = Join-Path ([IO.Path]::GetTempPath()) "syft-notes-velopack-$PID.lnk"
  Copy-Item -LiteralPath $lnk -Destination $kept -Force
}

# From here the new copy is installed and is the one that opens, whatever else goes wrong (Finish).
$moved = $true

# 5. The Inno uninstaller. unins000.exe starts a copy of itself from the temp folder and ends at once, so what is waited
# for is the old folder going (on its own change events, with a deadline).
$uninstaller = ([string]$inno.UninstallString -replace '^"([^"]+)".*$', '$1')
try {
  $u = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru -WorkingDirectory ([IO.Path]::GetTempPath())
  $null = $u.Handle
  if (-not $u.WaitForExit($Seconds * 1000)) { Say "the Inno uninstaller didn't start in $Seconds s" }
} catch {
  Say "couldn't run the Inno uninstaller: $($_.Exception.Message)"
}
$watch = Split-Path -Parent $old
$watcher = New-Object IO.FileSystemWatcher $watch
$watcher.IncludeSubdirectories = $false
$deadline = (Get-Date).AddSeconds($Seconds)
try {
  while ((Test-Path -LiteralPath $old) -and (Get-Date) -lt $deadline) {
    # A change wakes this at once; the cap only covers an event the watcher can miss.
    $null = $watcher.WaitForChanged([IO.WatcherChangeTypes]::All, 2000)
  }
} finally { $watcher.Dispose() }
if (Test-Path -LiteralPath $old) { Say "the old folder $old is still there after $Seconds s" } else { Say "the old copy is uninstalled" }

# 6. The Start-menu entry, back if the uninstaller took it; then the new copy opens, as an update's would.
if (-not (Test-Path -LiteralPath $lnk) -and $kept) {
  Copy-Item -LiteralPath $kept -Destination $lnk -Force
  Say "put Velopack's Start-menu entry back"
}
if ($kept) { Remove-Item -LiteralPath $kept -Force -ErrorAction SilentlyContinue }
Say "done in $([int]$clock.Elapsed.TotalSeconds) s"
} catch {
  Say "the move stopped: $($_.Exception.Message)"
} finally {
  Finish
  # Velopack's Setup.exe and this script, downloaded for the move, go (this script is already loaded).
  Remove-Item -LiteralPath (Split-Path -Parent $Setup) -Recurse -Force -ErrorAction SilentlyContinue
}
if (-not $moved) { exit 1 }
exit 0
