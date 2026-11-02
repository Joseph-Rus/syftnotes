# Builds Syft Notes for Windows: a self-contained publish for x64 and arm64 (so it needs no .NET install),
# pruned to the runtime each processor needs, and, when Inno Setup 6 is installed, the one download,
# Syft-Notes-Setup.exe. The app has no role of its own: setup asks what the computer is for.
#   powershell -File windows\build.ps1 [-Out dist\windows]
# Needs the .NET SDK, and Inno Setup 6 for the installers (skipped, with a note, when it's missing:
# choco install innosetup). What this makes is not signed, so a downloaded Setup.exe gets Windows' SmartScreen
# question once (More info, then Run anyway). A release that has a signing identity then signs it and makes Setup.exe
# again, with windows\sign-release.ps1 (docs/signing.md); -NoInstaller leaves the unsigned Setup.exe out for it.
# What it makes is a student's copy: no self-test, no demo libraries, no STUDYSTASH_* test switches (TestHelpers=false,
# engine/Directory.Build.props), and the build stops if one is there anyway. -TestHelpers makes the copy CI self-tests
# instead; that one is never shipped. -Rids picks the processors (both unless told).
param([string]$Out = "dist\windows", [switch]$NoInstaller, [switch]$TestHelpers, [string[]]$Rids = @("win-x64", "win-arm64"))
$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

$Version = (Select-String -Path (Join-Path $Here "..\engine\Directory.Build.props") -Pattern '<StudyStashVersion>(.*)</StudyStashVersion>').Matches[0].Groups[1].Value
if (-not $Version) { throw "engine\Directory.Build.props has no StudyStashVersion" }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path

# D6: the transcription model is never bundled. The one model file that is: the live words' small Whisper in the
# x64 tree (models\live-whisper-*.bin, 43.5 MB), so a PC shows the first words with no download.
function Test-NoModel([string]$Dir) {
  $Bad = Get-ChildItem $Dir -Recurse -File -Filter "*.bin" -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 1MB -and $_.Name -notlike "live-whisper-*.bin" }
  if ($Bad) { throw "the published app has a bundled model: $($Bad.FullName -join ', ')" }
}

# Whisper.net.Runtime ships every platform's natives under runtimes\; a processor only needs its own tree
# (plus, for x64, the Vulkan build under runtimes\vulkan\win-x64 - arm64 has no Vulkan whisper build).
function Remove-UnneededRuntimes([string]$Dir, [string]$Rid) {
  $Runtimes = Join-Path $Dir "runtimes"
  if (-not (Test-Path $Runtimes)) { return }
  Get-ChildItem $Runtimes -Directory | Where-Object { $_.Name -notin @($Rid, "vulkan") } | Remove-Item -Recurse -Force
  $Vulkan = Join-Path $Runtimes "vulkan"
  if (Test-Path $Vulkan) {
    if ($Rid -eq "win-x64") {
      Get-ChildItem $Vulkan -Directory | Where-Object { $_.Name -ne "win-x64" } | Remove-Item -Recurse -Force
    } else {
      Remove-Item -Recurse -Force $Vulkan
    }
  }
}

# The phone app (web/) is built first, so each publish carries it beside the app as web/ (the library serves it at
# /app/). It needs Node and npm; without them the build stops rather than ship a library whose Add a phone leads
# nowhere.
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw "npm is needed to build the phone app (web/)" }
Push-Location (Join-Path $Here "..\web")
try {
  npm ci --no-audit --no-fund --silent
  if ($LASTEXITCODE -ne 0) { throw "npm ci for the phone app failed" }
  npm run build --silent
  if ($LASTEXITCODE -ne 0) { throw "building the phone app failed" }
} finally { Pop-Location }

$Helpers = if ($TestHelpers) { "true" } else { "false" }

# A student's copy has no self-test in it: its selftest.txt is the one thing only the self-test writes.
function Test-NoSelfTest([string]$Dir) {
  $Marker = [Text.Encoding]::Unicode.GetBytes("selftest.txt")
  $Bytes = [IO.File]::ReadAllBytes((Join-Path $Dir "StudyStash.dll"))
  $Text = [BitConverter]::ToString($Bytes)
  if ($Text.Contains([BitConverter]::ToString($Marker))) { throw "the student's copy in $Dir has the self-test in it" }
}

foreach ($Rid in $Rids) {
  $Dest = Join-Path $Out $Rid
  Remove-Item -Recurse -Force $Dest -ErrorAction SilentlyContinue
  dotnet publish (Join-Path $Here "..\engine\src\StudyStash.App") -c Release -r $Rid --self-contained true `
    -p:DebugType=None -p:TestHelpers=$Helpers -o $Dest --nologo -v quiet
  if ($LASTEXITCODE -ne 0) { throw "publishing $Rid failed" }
  Remove-UnneededRuntimes $Dest $Rid
  # -p:DebugType=None leaves out our own symbols, not the ones NuGet ships beside native code (libSkiaSharp.pdb alone
  # is 84 MB): nothing on a student's computer reads them.
  Get-ChildItem $Dest -Recurse -File -Filter "*.pdb" | Remove-Item -Force
  Test-NoModel $Dest
  if (-not $TestHelpers) { Test-NoSelfTest $Dest }
}

$Iscc = @((Get-Command iscc -ErrorAction SilentlyContinue).Source,
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($Iscc -and -not $NoInstaller) {
  & $Iscc "/Qp" "/DAppVersion=$Version" "/DSource=$Out" "/O$Out" (Join-Path $Here "setup.iss")
  if ($LASTEXITCODE -ne 0) { throw "the installer didn't build" }
} elseif ($NoInstaller) {
  Write-Host "-NoInstaller: no Setup.exe from this script."
} else {
  Write-Host "Inno Setup isn't installed, so there is no Setup.exe (choco install innosetup)."
}
Write-Host "Built ${Version}:"
Get-ChildItem $Out -File | Format-Table Name, Length -AutoSize
