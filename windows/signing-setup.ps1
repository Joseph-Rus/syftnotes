# What a CI rehearsal of Windows signing signs with, and its clean-up (docs/signing.md).
#   powershell -File windows\signing-setup.ps1 -SelfSigned   a throwaway self-signed code-signing certificate, handed to
#                                                            windows\sign.ps1 as a .pfx in the environment (GITHUB_ENV)
#   powershell -File windows\signing-setup.ps1 -Cleanup      takes it out of the certificate store again
# It proves the signing steps (every file signed, Setup.exe and the uninstaller too, and a signed installer installing,
# updating and uninstalling) and nothing more: Windows trusts none of it, and none of it is ever published.
param([switch]$SelfSigned, [switch]$Cleanup)
$ErrorActionPreference = "Stop"
$Work = Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }) "study-stash-rehearsal"

function Add-Env([string]$Name, [string]$Value) {
  if ($env:GITHUB_ENV) { Add-Content -Path $env:GITHUB_ENV -Value "$Name=$Value" } else { Write-Output "$Name=$Value" }
}

if ($SelfSigned) {
  New-Item -ItemType Directory -Force -Path $Work | Out-Null
  $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Study Stash rehearsal (self-signed, not a real signature)" `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddDays(2) `
    -CertStoreLocation Cert:\CurrentUser\My
  $password = [Guid]::NewGuid().ToString("N") + "aA1!"
  if ($env:GITHUB_ACTIONS) { Write-Host "::add-mask::$password" }
  $pfx = Join-Path $Work "rehearsal.pfx"
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
  $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx))
  Remove-Item $pfx -Force
  if ($env:GITHUB_ACTIONS) { Write-Host "::add-mask::$base64" }
  Add-Env "WINDOWS_CERT_PFX_BASE64" $base64
  Add-Env "WINDOWS_CERT_PASSWORD" $password
  Add-Env "STUDYSTASH_REHEARSAL_THUMBPRINT" $cert.Thumbprint
  Write-Host "Rehearsing with a throwaway self-signed certificate ($($cert.Thumbprint)): nothing signed here is a real signature."
} elseif ($Cleanup) {
  if ($env:STUDYSTASH_REHEARSAL_THUMBPRINT) {
    Remove-Item "Cert:\CurrentUser\My\$($env:STUDYSTASH_REHEARSAL_THUMBPRINT)" -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path $Work) { Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue }
  Write-Host "The rehearsal's certificate is gone."
} else {
  throw "usage: signing-setup.ps1 -SelfSigned | -Cleanup"
}
