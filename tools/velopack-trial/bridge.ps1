# The bridge from an Inno Setup install of Syft Notes to a Velopack one, as a prototype for the Velopack trial
# (docs/LIBRARIES.md, phase 5; .github/workflows/velopack-trial.yml runs it on a throwaway Windows runner). In the
# bridge release, the old updater (StudyStash.Core/Updater.cs) would do these same steps from a detached process once
# the app has quit, in place of running the next Inno Setup.exe:
#   1. run Velopack's Setup.exe --silent (it installs into %LocalAppData%\SyftNotes and doesn't open the app);
#   2. point this account's Run entries (start at login, the background library) at the new program, because the
#      Inno uninstaller deletes every one that still names the old folder;
#   3. keep a copy of the Start-menu entry Velopack made, as it has the same name as Inno's;
#   4. run the Inno uninstaller silently, and wait until the old folder is gone;
#   5. put the Start-menu entry back if the uninstaller took it.
# The new app must not be started before step 4 ends: the uninstaller ends every StudyStash.exe (taskkill /im).
#   pwsh tools/velopack-trial/bridge.ps1 -Setup SyftNotes-win-x64-Setup.exe -Report bridge.txt
param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$Report, [int]$Seconds = 300)
$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
function Say([string]$Line) { Add-Content -LiteralPath $Report -Value $Line; Write-Host $Line }
function Target([string]$Lnk) { (New-Object -ComObject WScript.Shell).CreateShortcut($Lnk).TargetPath }

$InnoKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}_is1"
$inno = Get-ItemProperty -Path $InnoKey -ErrorAction SilentlyContinue
if (-not $inno) { throw "bridge.ps1: no Inno Setup install of Syft Notes on this account" }
$old = $inno.InstallLocation.TrimEnd('\')
$oldExe = Join-Path $old "StudyStash.exe"
$new = Join-Path $env:LOCALAPPDATA "SyftNotes"
$newExe = Join-Path $new "current\StudyStash.exe"
$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Syft Notes.lnk"
Say "old install: $old ($($inno.DisplayVersion))"
$clock = [Diagnostics.Stopwatch]::StartNew()

# 1. Velopack's Setup, silently.
$setupLog = Join-Path ([IO.Path]::GetDirectoryName((Resolve-Path -LiteralPath $Report).Path)) "bridge-setup.log"
$p = Start-Process -FilePath $Setup -ArgumentList "--silent", "--log", "`"$setupLog`"" -PassThru
$null = $p.Handle # keeps the exit code readable once it has ended
if (-not $p.WaitForExit($Seconds * 1000)) { $p.Kill(); throw "bridge.ps1: Velopack's Setup didn't finish in $Seconds s" }
Say "velopack setup: exit $($p.ExitCode) after $([int]$clock.Elapsed.TotalSeconds) s"
if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $newExe)) { throw "bridge.ps1: Velopack's Setup didn't install $newExe" }

# 2. The Run entries that name the old program now name the new one.
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$values = Get-ItemProperty -Path $run -ErrorAction SilentlyContinue
foreach ($name in @($values.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" } | ForEach-Object Name)) {
  $data = [string]$values.$name
  if ($data.IndexOf($oldExe, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    $fixed = $data.Replace($oldExe, $newExe, [StringComparison]::OrdinalIgnoreCase)
    Set-ItemProperty -Path $run -Name $name -Value $fixed
    Say "run entry '$name' now starts $newExe"
  }
}

# 3. Velopack's Start-menu entry, kept.
$kept = $null
if ((Test-Path -LiteralPath $lnk) -and (Target $lnk) -like "$new\*") {
  $kept = Join-Path ([IO.Path]::GetTempPath()) "syft-notes-velopack.lnk"
  Copy-Item -LiteralPath $lnk -Destination $kept -Force
  Say "start menu entry after velopack setup: $(Target $lnk)"
} else {
  Say "start menu entry after velopack setup: $(if (Test-Path -LiteralPath $lnk) { Target $lnk } else { 'none' })"
}

# 4. The Inno uninstaller. unins000.exe starts a copy of itself from the temp folder and ends at once, so what is waited
# for is the old folder going.
$uninstaller = ($inno.UninstallString -replace '^"([^"]+)".*$', '$1')
$u = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru
$null = $u.Handle
if (-not $u.WaitForExit($Seconds * 1000)) { throw "bridge.ps1: the Inno uninstaller didn't start in $Seconds s" }
& pwsh -NoProfile -File (Join-Path $Here "wait-for.ps1") -Path $old -Gone -Seconds $Seconds
if ($LASTEXITCODE -ne 0) { throw "bridge.ps1: the old folder $old is still there" }
Say "inno uninstall: done after $([int]$clock.Elapsed.TotalSeconds) s; old folder gone"
Say "inno uninstall entry: $(if (Test-Path -Path $InnoKey) { 'still there' } else { 'gone' })"

# 5. The Start-menu entry, back if the uninstaller took it.
if (-not (Test-Path -LiteralPath $lnk)) {
  if ($kept) {
    Copy-Item -LiteralPath $kept -Destination $lnk -Force
    Say "start menu entry: the Inno uninstaller deleted Velopack's (same name); put back"
  } else {
    Say "start menu entry: none, and no copy to put back"
  }
} else {
  Say "start menu entry: kept by the Inno uninstaller"
}
if ($kept) { Remove-Item -LiteralPath $kept -Force -ErrorAction SilentlyContinue }
Say "bridge: finished in $([int]$clock.Elapsed.TotalSeconds) s"
