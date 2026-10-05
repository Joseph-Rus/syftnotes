# Signs files with the code-signing identity this environment holds (docs/signing.md), with a secure timestamp.
#   powershell -File windows\sign.ps1 [-Method] [file ...]
# -Method prints which identity is configured (azure, pfx or none) and signs nothing. Written for Windows PowerShell 5.1,
# because Inno Setup's SignTool (windows\setup.iss) runs it as `powershell.exe`, once for Setup.exe and once for the
# uninstaller; windows\sign-release.ps1 runs it for the app's own files.
#
#   azure  Azure Artifact Signing (until early 2026 called Trusted Signing), Microsoft's managed signing: signtool with
#          Microsoft's dlib, which signs with a certificate that never leaves Azure. The environment has
#          AZURE_TENANT_ID, AZURE_CLIENT_ID and AZURE_CLIENT_SECRET (a service principal that may sign with the
#          account's certificate profile), and WINDOWS_SIGNING_ENDPOINT (https://eus.codesigning.azure.net, or the
#          region's own), WINDOWS_SIGNING_ACCOUNT and WINDOWS_SIGNING_PROFILE.
#   pfx    a certificate file: signtool with WINDOWS_CERT_PFX_BASE64 (the .pfx, in base64) and WINDOWS_CERT_PASSWORD.
#          CI's rehearsal signs this way with a throwaway self-signed certificate. A purchased OV certificate can be used
#          the same way only if its CA still hands out the key as a file (most now keep it on a hardware token or in
#          their cloud, which this can't use).
# Nothing here prints a secret.
param(
  [switch]$Method,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Files
)
$ErrorActionPreference = "Stop"

# Windows PowerShell started from PowerShell 7 (a pwsh CI step, and Inno's SignTool under it) inherits 7's module
# folders first, and then can't load 7's Microsoft.PowerShell.Security for Get-AuthenticodeSignature ("the module could
# not be loaded"). Load its own, by path.
if ($PSVersionTable.PSEdition -eq "Desktop") {
  Import-Module (Join-Path $PSHOME "Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1")
}

function Test-Env([string[]]$Names) {
  foreach ($n in $Names) { if (-not [Environment]::GetEnvironmentVariable($n)) { return $false } }
  return $true
}
function Get-Method {
  if (Test-Env @("AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET", "WINDOWS_SIGNING_ENDPOINT", "WINDOWS_SIGNING_ACCOUNT", "WINDOWS_SIGNING_PROFILE")) { return "azure" }
  if (Test-Env @("WINDOWS_CERT_PFX_BASE64", "WINDOWS_CERT_PASSWORD")) { return "pfx" }
  return "none"
}

$Kind = Get-Method
if ($Method) { Write-Output $Kind; return }
if ($Kind -eq "none") { throw "sign.ps1: no signing identity in the environment (docs/signing.md)" }
if (-not $Files -or $Files.Count -eq 0) { throw "sign.ps1: no files to sign" }

$Work = Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }) "study-stash-signing"
New-Item -ItemType Directory -Force -Path $Work | Out-Null
# Inno Setup runs this with no console to show anything on, so a failure is also written here, for sign-release.ps1 to print.
$Log = Join-Path $Work "sign.log"

# The newest 64-bit signtool.exe of the Windows SDK (Artifact Signing needs 10.0.2261.755 or newer).
function Find-SignTool {
  $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
  $all = @(Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' })
  if ($all.Count -eq 0) { throw "sign.ps1: no signtool.exe under $kits (install the Windows SDK)" }
  $best = $all | Sort-Object { $v = $_.VersionInfo; [version]::new($v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart) } -Descending | Select-Object -First 1
  return $best.FullName
}
$Pfx = $null
try {
  $SignTool = Find-SignTool

  if ($Kind -eq "azure") {
    # Microsoft's dlib for signtool, from NuGet (a .nupkg is a zip).
    $Version = if ($env:ARTIFACT_SIGNING_CLIENT_VERSION) { $env:ARTIFACT_SIGNING_CLIENT_VERSION } else { "1.0.128" }
    $Tools = Join-Path $Work "artifact-signing-client-$Version"
    $Dlib = Get-ChildItem $Tools -Recurse -Filter Azure.CodeSigning.Dlib.dll -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    if (-not $Dlib) {
      $zip = "$Tools.zip"
      Invoke-WebRequest -UseBasicParsing -Uri "https://www.nuget.org/api/v2/package/Microsoft.ArtifactSigning.Client/$Version" -OutFile $zip
      Expand-Archive -Path $zip -DestinationPath $Tools -Force
      Remove-Item $zip -Force
      $Dlib = Get-ChildItem $Tools -Recurse -Filter Azure.CodeSigning.Dlib.dll | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
      if (-not $Dlib) { throw "sign.ps1: Azure.CodeSigning.Dlib.dll isn't in Microsoft.ArtifactSigning.Client $Version" }
    }
    $MetaPath = Join-Path $Work "artifact-signing-metadata.json"
    # Only the service principal's own credentials (AZURE_CLIENT_*) are tried; Microsoft's list of the others to skip.
    $skip = @("ManagedIdentityCredential", "WorkloadIdentityCredential", "SharedTokenCacheCredential", "VisualStudioCredential", "VisualStudioCodeCredential",
      "AzureCliCredential", "AzurePowerShellCredential", "AzureDeveloperCliCredential", "InteractiveBrowserCredential")
    @{ Endpoint = $env:WINDOWS_SIGNING_ENDPOINT; CodeSigningAccountName = $env:WINDOWS_SIGNING_ACCOUNT; CertificateProfileName = $env:WINDOWS_SIGNING_PROFILE; ExcludeCredentials = $skip } |
      ConvertTo-Json | Set-Content -Path $MetaPath -Encoding ASCII
    $Timestamp = "http://timestamp.acs.microsoft.com"
    $Identity = @("/dlib", $Dlib.FullName, "/dmdf", $MetaPath)
  } else {
    $Pfx = Join-Path $Work "signing-certificate.pfx"
    [IO.File]::WriteAllBytes($Pfx, [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_BASE64))
    $Timestamp = if ($env:WINDOWS_TIMESTAMP_URL) { $env:WINDOWS_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
    $Identity = @("/f", $Pfx, "/p", $env:WINDOWS_CERT_PASSWORD)
  }

  # A few files to a signtool, so a command line stays short; each batch is tried again, as the timestamp server (and
  # Azure) now and then don't answer.
  for ($i = 0; $i -lt $Files.Count; $i += 20) {
    $batch = @($Files[$i..([Math]::Min($i + 19, $Files.Count - 1))])
    for ($attempt = 1; ; $attempt++) {
      $said = & $SignTool sign /q /fd SHA256 /tr $Timestamp /td SHA256 @Identity @batch 2>&1 | Out-String
      if ($LASTEXITCODE -eq 0) { break }
      if ($attempt -ge 3) { throw "signtool failed on $($batch -join ', '): $said" }
      Start-Sleep -Seconds (10 * $attempt)
    }
  }
  # signtool's success is the proof for most files; for an exe or dll, Windows is asked as well. (Get-AuthenticodeSignature
  # can't read the temporary files Inno Setup signs, which have no such extension.)
  foreach ($f in $Files) {
    if ($f -match '\.(exe|dll)$' -and -not (Get-AuthenticodeSignature -FilePath $f).SignerCertificate) { throw "$f has no signature after signing" }
  }
  Write-Host "sign.ps1: signed $($Files.Count) file(s) ($Kind)"
} catch {
  Add-Content -Path $Log -Value "sign.ps1 ($Kind) failed for $($Files -join ' '): $($_.Exception.Message)"
  [Console]::Error.WriteLine("sign.ps1: $($_.Exception.Message)")
  exit 1
} finally {
  if ($Pfx) { Remove-Item $Pfx -Force -ErrorAction SilentlyContinue }
}
