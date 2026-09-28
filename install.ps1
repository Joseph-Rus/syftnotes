# Study Stash installer (Windows PowerShell only; on a Mac, run install.sh instead).
#     irm https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.ps1 | iex
# The same app for every computer: when it opens, setup asks whether it's just this computer, your laptop or your
# library. (An old STUDYSTASH_ROLE is still accepted, and changes nothing.)
#
# Downloads the newest release's Windows installer (one Setup.exe for x64 and Arm64), checks it
# against the release's SHA256SUMS.txt when there is one, and runs it quietly: your account only,
# no admin rights. Safe to rerun: installing over an existing install is an in-place upgrade that keeps
# what it was set up as.
#
#   STUDYSTASH_SETUP=<path>   install this Setup.exe instead of downloading one (development, CI)
& {
  $ErrorActionPreference = "Stop"
  $ProgressPreference = "SilentlyContinue"  # the default progress bar slows downloads to a crawl
  $Slug = "Joseph-Rus/study-stash"

  # One download for every role: setup asks.
  $Name = "Study-Stash-Setup.exe"

  function Fail($Message) {
    Write-Host ""
    Write-Host "Study Stash install failed: $Message" -ForegroundColor Red
    throw $Message
  }

  # Older releases connected a laptop with an address and password passed as env vars; Study
  # Stash's own setup asks for them now, in the app, so this just points you at them.
  $Addr = $env:STUDYSTASH_SERVER
  if ($Addr) { Write-Host "Type this address in setup: $Addr" }

  $Setup = $env:STUDYSTASH_SETUP
  if (-not $Setup) {
    Write-Host "Downloading $Name..."
    $Setup = Join-Path $env:TEMP $Name
    try { Invoke-WebRequest "https://github.com/$Slug/releases/latest/download/$Name" -OutFile $Setup -UseBasicParsing }
    catch { Fail "couldn't download $Name ($($_.Exception.Message))" }

    $Sums = Join-Path $env:TEMP "SHA256SUMS.txt"
    $HaveSums = $false
    try {
      Invoke-WebRequest "https://github.com/$Slug/releases/latest/download/SHA256SUMS.txt" -OutFile $Sums -UseBasicParsing
      $HaveSums = $true
    } catch { } # this release has no checksums file: install unchecked
    if ($HaveSums) {
      $Line = Select-String -Path $Sums -Pattern ([regex]::Escape($Name) + '$') | Select-Object -First 1
      $Want = if ($Line) { ($Line.Line -split "\s+")[0] } else { $null }
      $Got = (Get-FileHash $Setup -Algorithm SHA256).Hash.ToLower()
      if (-not $Want -or $Want -ne $Got) { Fail "the download didn't match its checksum, so nothing was installed" }
    }
  }

  Write-Host "Installing Study Stash..."
  $Proc = Start-Process -FilePath $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru -Wait
  if ($Proc.ExitCode -ne 0) { Fail "the installer exited with code $($Proc.ExitCode)" }

  $Exe = Join-Path $env:LOCALAPPDATA "Programs\Study Stash\StudyStash.exe"
  Write-Host "Installed Study Stash."
  if (Test-Path $Exe) { Start-Process $Exe }
}
