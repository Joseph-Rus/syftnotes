# Velopack's packages of the Windows app (docs/LIBRARIES.md, phase 5), one channel per processor (win-x64, win-arm64),
# from the trees windows\build.ps1 published (and windows\sign-release.ps1 signed): for each, Velopack's Setup.exe
# (SyftNotes-<rid>-Setup.exe: what an Inno-era copy moves over with, windows\bridge.ps1), the full package, a delta from
# the last release when there is one, and releases.<rid>.json, in <Out>\velopack\<rid>. Setup.exe makes a Start-menu
# entry only, as Inno's did, with the app's own id (StudyStash.App, its notifications'), so the window, the Start-menu
# entry and a taskbar pin are one app to Windows. Signing is whichever identity this environment holds (windows\sign.ps1 -Method): Azure
# Artifact Signing through vpk's own --azureTrustedSignFile, a certificate file through sign.ps1, or none.
#   pwsh windows\velopack-pack.ps1 [-Out dist\windows]
# Needs vpk (dotnet tool install -g vpk --version 1.2.161) and Python 3 (tools\velopack\trim-feed.py).
param([string]$Out = "dist\windows", [string[]]$Rids = @("win-x64", "win-arm64"))
$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $Here
$Out = (Resolve-Path $Out).Path
$Version = (Select-String -Path (Join-Path $Root "engine\Directory.Build.props") -Pattern '<StudyStashVersion>(.*)</StudyStashVersion>').Matches[0].Groups[1].Value
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw "vpk isn't installed (dotnet tool install -g vpk --version 1.2.161)" }

$SignScript = Join-Path $Here "sign.ps1"
$Method = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $SignScript -Method | Out-String).Trim()
$Sign = @()
if ($Method -eq "azure") {
  # Velopack's own Artifact Signing (signtool with Microsoft's dlib, bundled with vpk); it signs with the service
  # principal in AZURE_TENANT_ID, AZURE_CLIENT_ID and AZURE_CLIENT_SECRET. Only the account's names go in this file.
  $Meta = Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }) "syft-notes-artifact-signing.json"
  @{ Endpoint = $env:WINDOWS_SIGNING_ENDPOINT; CodeSigningAccountName = $env:WINDOWS_SIGNING_ACCOUNT; CertificateProfileName = $env:WINDOWS_SIGNING_PROFILE } |
    ConvertTo-Json | Set-Content -LiteralPath $Meta -Encoding utf8
  $Sign = @("--azureTrustedSignFile", $Meta)
} elseif ($Method -eq "pfx") {
  $Sign = @("--signTemplate", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$SignScript`" {{file...}}", "--signParallel", "20")
}
Write-Host "Velopack's packages, signed with: $Method"

foreach ($Rid in $Rids) {
  $Tree = Join-Path $Out $Rid
  if (-not (Test-Path $Tree)) { throw "no $Tree (run windows\build.ps1 first)" }
  # Packed from a copy, with the study-stash.ini Inno Setup writes beside the app (its version: the updater and Windows'
  # notifications read it), so the tree the Inno installer was made from stays as it was.
  $Pack = Join-Path $Out "velopack-tree\$Rid"
  Remove-Item -Recurse -Force $Pack -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Pack) | Out-Null
  Copy-Item -Recurse $Tree $Pack
  "[app]`r`nversion=$Version`r`n" | Set-Content -NoNewline -LiteralPath (Join-Path $Pack "study-stash.ini")
  $Feed = Join-Path $Out "velopack\$Rid"
  Remove-Item -Recurse -Force $Feed -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $Feed | Out-Null
  # The last release's package, so this one comes with a delta from it (the first Velopack release has none).
  # STUDYSTASH_VELOPACK_BASE: a folder of channel feeds to make the delta from instead (the bridge test's second version).
  if ($env:STUDYSTASH_VELOPACK_BASE) {
    Copy-Item (Join-Path $env:STUDYSTASH_VELOPACK_BASE "$Rid\*.nupkg"), (Join-Path $env:STUDYSTASH_VELOPACK_BASE "$Rid\releases.$Rid.json") $Feed
  } else {
    vpk download github --repoUrl https://github.com/Joseph-Rus/syftnotes --channel $Rid --outputDir $Feed
    if ($LASTEXITCODE -ne 0) { Write-Host "no earlier release on the $Rid channel, so no delta this time" }
  }
  vpk pack --packId SyftNotes --packVersion $Version --packDir $Pack --mainExe StudyStash.exe --runtime $Rid --channel $Rid `
    --packTitle "Syft Notes" --packAuthors "Syft Notes" --icon (Join-Path $Root "assets\study-stash.ico") `
    --shortcuts StartMenuRoot --aumid StudyStash.App @Sign --outputDir $Feed
  if ($LASTEXITCODE -ne 0) { throw "vpk pack $Rid failed" }
  & python -I (Join-Path $Root "tools\velopack\trim-feed.py") $Feed $Rid $Version
  if ($LASTEXITCODE -ne 0) { throw "the $Rid feed has no full package" }
  # A portable zip isn't offered to anyone.
  Get-ChildItem $Feed -Filter "*-Portable.zip" | Remove-Item -Force
  $Setup = Join-Path $Feed "SyftNotes-$Rid-Setup.exe"
  if (-not (Test-Path $Setup)) { throw "vpk made no $Setup" }
  if ($Method -ne "none" -and -not (Get-AuthenticodeSignature -FilePath $Setup).SignerCertificate) { throw "$Setup isn't signed" }
  Remove-Item -Recurse -Force $Pack
  Get-ChildItem $Feed -File | Format-Table Name, Length -AutoSize
}
