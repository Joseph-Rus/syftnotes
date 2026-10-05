# Signs the Windows app and makes Study-Stash-Setup.exe around it, signed: the exe and dlls the app is made of, then
# Setup.exe and the uninstaller it installs (Inno Setup's SignTool, which runs windows\sign.ps1). A step of its own,
# after windows\build.ps1 -NoInstaller, so that nothing the build runs (npm's packages, the .NET restore) ever sees the
# signing identity (docs/signing.md).
#   powershell -File windows\sign-release.ps1 [-Out dist\windows]     (where build.ps1 published win-x64 and win-arm64)
# Needs a signing identity in the environment (windows\sign.ps1 -Method says which) and Inno Setup 6.
param([string]$Out = "dist\windows")
$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$SignScript = Join-Path $Here "sign.ps1"
$Out = (Resolve-Path $Out).Path

$Version = (Select-String -Path (Join-Path $Here "..\engine\Directory.Build.props") -Pattern '<StudyStashVersion>(.*)</StudyStashVersion>').Matches[0].Groups[1].Value
if (-not $Version) { throw "engine\Directory.Build.props has no StudyStashVersion" }
$Method = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $SignScript -Method | Out-String).Trim()
if ($Method -eq "none") { throw "no signing identity in the environment (docs/signing.md)" }
Write-Host "Signing with: $Method"

# The app's own files: every exe and dll (managed assemblies and native libraries alike) nobody has signed yet. Microsoft's
# own runtime files already carry Microsoft's signature, which stays.
foreach ($Rid in "win-x64", "win-arm64") {
  $Tree = Join-Path $Out $Rid
  if (-not (Test-Path $Tree)) { throw "no $Tree (run windows\build.ps1 first)" }
  $Unsigned = @(Get-ChildItem $Tree -Recurse -File | Where-Object { $_.Extension -in ".exe", ".dll" } |
    Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -eq "NotSigned" } | ForEach-Object { $_.FullName })
  Write-Host "$Rid : signing $($Unsigned.Count) unsigned exe and dll files"
  # Forty to a command line, which Windows limits to 32 thousand characters.
  for ($i = 0; $i -lt $Unsigned.Count; $i += 40) {
    $batch = @($Unsigned[$i..([Math]::Min($i + 39, $Unsigned.Count - 1))])
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $SignScript @batch
    if ($LASTEXITCODE -ne 0) { throw "signing the $Rid files failed" }
  }
}

$Iscc = @((Get-Command iscc -ErrorAction SilentlyContinue).Source,
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $Iscc) { throw "Inno Setup 6 isn't installed (choco install innosetup)" }
# Inno runs this command for Setup.exe and the uninstaller; $q is a quote mark and $f the (quoted) file, Inno's own.
$Quoted = if ($SignScript -match '\s') { '$q' + $SignScript + '$q' } else { $SignScript }
$SignCommand = '/Sstudystash=powershell.exe -NoProfile -ExecutionPolicy Bypass -File ' + $Quoted + ' $f'
Write-Host "Inno Setup's sign tool: $SignCommand"
& $Iscc "/Qp" "/DAppVersion=$Version" "/DSource=$Out" "/O$Out" "/DSignInstaller" $SignCommand (Join-Path $Here "setup.iss")
if ($LASTEXITCODE -ne 0) {
  $SignLog = Join-Path (Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }) "study-stash-signing") "sign.log"
  if (Test-Path $SignLog) { Write-Host "--- what the sign tool said"; Get-Content $SignLog | Write-Host }
  throw "the signed installer didn't build"
}
$Setup = Join-Path $Out "Study-Stash-Setup.exe"
if (-not (Get-AuthenticodeSignature -FilePath $Setup).SignerCertificate) { throw "$Setup isn't signed" }
Write-Host "Signed Study-Stash-Setup.exe ($Method)."
