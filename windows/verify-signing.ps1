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
  if (-not $sig.TimeStamperCertificate) { throw "$f has no secure timestamp" }
  if ($Rehearsal) {
    if ($sig.SignerCertificate.Thumbprint -ne $env:STUDYSTASH_REHEARSAL_THUMBPRINT) { throw "$f isn't signed by the rehearsal's certificate" }
  } elseif ($sig.Status -ne "Valid") {
    throw "$f has a signature Windows doesn't accept: $($sig.Status) $($sig.StatusMessage)"
  }
  Write-Host "$(Split-Path -Leaf $f): signed by $($sig.SignerCertificate.Subject), $($sig.Status)"
}
