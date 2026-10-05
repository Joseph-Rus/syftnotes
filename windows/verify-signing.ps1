# Checks that files are signed the way a release signs them, and says whose signature it found.
#   powershell -File windows\verify-signing.ps1 [-Rehearsal] <file> [<file> ...]
# Each file carries a signature and a secure timestamp, and Windows trusts it (Status Valid). With -Rehearsal the signer
# must be the throwaway certificate windows\signing-setup.ps1 made (STUDYSTASH_REHEARSAL_THUMBPRINT), which Windows
# doesn't trust, so only the signature itself is checked. Handy by hand on a Setup.exe you downloaded.
param([switch]$Rehearsal, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)
$ErrorActionPreference = "Stop"
if (-not $Files -or $Files.Count -eq 0) { throw "usage: verify-signing.ps1 [-Rehearsal] <file> ..." }
foreach ($f in $Files) {
  $sig = Get-AuthenticodeSignature -FilePath $f
  if (-not $sig.SignerCertificate) { throw "$f isn't signed" }
  # A certificate that lasts days (Artifact Signing's) is only good for a signature that is timestamped. PowerShell can fail
  # to see an RFC 3161 timestamp that is there, so a real signature is looked at by signtool too before it is refused.
  $timestamped = [bool]$sig.TimeStamperCertificate
  if (-not $timestamped -and -not $Rehearsal) {
    $signtool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -Last 1
    if ($signtool) { $timestamped = [bool]((& $signtool.FullName verify /pa /all /v $f 2>&1 | Out-String) -match "timestamped") }
  }
  if (-not $timestamped) {
    if ($Rehearsal) { Write-Host "(warning: ${f}: PowerShell sees no timestamp)" } else { throw "$f has no secure timestamp" }
  }
  if ($Rehearsal) {
    # A library its maker signed (SkiaSharp's, say) keeps that signature, which sign.ps1 leaves alone: Windows accepts it.
    if ($sig.SignerCertificate.Thumbprint -ne $env:STUDYSTASH_REHEARSAL_THUMBPRINT -and $sig.Status -ne "Valid") { throw "$f isn't signed by the rehearsal's certificate" }
  } elseif ($sig.Status -ne "Valid") {
    throw "$f has a signature Windows doesn't accept: $($sig.Status) $($sig.StatusMessage)"
  }
  Write-Host "$(Split-Path -Leaf $f): signed by $($sig.SignerCertificate.Subject), $($sig.Status)"
}
