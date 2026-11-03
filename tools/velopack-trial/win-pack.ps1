# The Velopack trial's Windows packing (.github/workflows/velopack-trial.yml, docs/LIBRARIES.md phase 5): two
# versions, <version>-trial.1 and -trial.2 (the second differs only in its version, so a delta exists), for win-x64
# and win-arm64, from windows\build.ps1's publish (with the test helpers, which carry the trial's commands). Every file
# is signed through windows\sign.ps1, the script releases already sign with, as vpk's --signTemplate: whichever
# identity the environment holds (here the rehearsal's throwaway self-signed certificate, from
# windows\signing-setup.ps1 -SelfSigned; in a release, Azure Artifact Signing). What it makes goes to out\:
# releases\<rid>\ (the feed), v1\<rid>\ (version 1's Setup.exe, which version 2's packing replaces) and sizes.txt.
#   pwsh tools/velopack-trial/win-pack.ps1
$ErrorActionPreference = "Stop"
$Root = (Get-Location).Path
$Out = Join-Path $Root "out"
$Props = Join-Path $Root "engine\Directory.Build.props"
$Base = (Select-String -Path $Props -Pattern '<StudyStashVersion>(.*)</StudyStashVersion>').Matches[0].Groups[1].Value
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Sizes = Join-Path $Out "sizes.txt"
$SignTemplate = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Root\windows\sign.ps1`" {{file...}}"

foreach ($n in 1, 2) {
  $v = "$Base-trial.$n"
  (Get-Content $Props) -replace '<StudyStashVersion>.*</StudyStashVersion>', "<StudyStashVersion>$v</StudyStashVersion>" | Set-Content $Props
  & .\windows\build.ps1 -Out "dist\v$n" -TestHelpers -NoInstaller
  foreach ($rid in "win-x64", "win-arm64") {
    $tree = Join-Path $Root "dist\v$n\$rid"
    # What Inno Setup writes beside the app, and what the app reads to know it's installed (Windows' notifications
    # need it): the package carries it.
    "[app]`r`nversion=$v`r`n" | Set-Content -NoNewline (Join-Path $tree "study-stash.ini")
    $clock = [Diagnostics.Stopwatch]::StartNew()
    vpk pack --packId SyftNotes --packVersion $v --packDir $tree --mainExe StudyStash.exe --runtime $rid --channel $rid `
      --packTitle "Syft Notes" --packAuthors "Syft Notes" --icon (Join-Path $Root "assets\study-stash.ico") `
      --shortcuts StartMenuRoot --aumid StudyStash.App --signTemplate $SignTemplate --signParallel 20 `
      --outputDir (Join-Path $Out "releases\$rid")
    if ($LASTEXITCODE -ne 0) { throw "vpk pack $rid $v failed" }
    Add-Content $Sizes "windows pack-seconds $rid v$n $([int]$clock.Elapsed.TotalSeconds) (signing included)"
    if ($n -eq 1) {
      New-Item -ItemType Directory -Force -Path (Join-Path $Out "v1\$rid") | Out-Null
      Copy-Item (Join-Path $Out "releases\$rid\*-Setup.exe") (Join-Path $Out "v1\$rid\")
    }
  }
}
foreach ($rid in "win-x64", "win-arm64") {
  Get-ChildItem (Join-Path $Out "releases\$rid") -File | ForEach-Object { Add-Content $Sizes "windows file $rid $($_.Name) $($_.Length)" }
  $setup = Get-ChildItem (Join-Path $Out "releases\$rid\*-Setup.exe")
  $sig = Get-AuthenticodeSignature -FilePath $setup.FullName
  Add-Content $Sizes "windows signature $rid $($setup.Name): $($sig.Status) by $($sig.SignerCertificate.Subject)"
}
Get-Content $Sizes
