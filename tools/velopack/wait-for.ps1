# Waits, for up to -Seconds, on a file: until it holds a line matching -Pattern, or (with -Gone) until it no longer
# exists. It wakes on the file system's own change events, not on a guessed pause, and stops with exit code 1 when the
# time is up. For the Velopack bridge test (.github/workflows/velopack-bridge.yml), on Windows, macOS and Linux (pwsh).
#   pwsh tools/velopack/wait-for.ps1 -Path report.txt -Pattern "^.* relaunched " -Seconds 300
#   pwsh tools/velopack/wait-for.ps1 -Path "C:\...\Study Stash" -Gone -Seconds 180
param([Parameter(Mandatory)][string]$Path, [string]$Pattern, [switch]$Gone, [int]$Seconds = 300)
$ErrorActionPreference = "Stop"
if (-not $Gone -and -not $Pattern) { throw "wait-for.ps1: give -Pattern or -Gone" }

function Done {
  if ($Gone) { return -not (Test-Path -LiteralPath $Path) }
  return (Test-Path -LiteralPath $Path) -and [bool](Select-String -LiteralPath $Path -Pattern $Pattern -Quiet)
}

# Watch the nearest folder that exists (the file, or the folder being removed, may not be there yet or any more).
$watch = Split-Path -Parent $Path
while ($watch -and -not (Test-Path -LiteralPath $watch)) { $watch = Split-Path -Parent $watch }
$watcher = [IO.FileSystemWatcher]::new($watch)
$watcher.IncludeSubdirectories = $true
$started = [DateTime]::UtcNow
$deadline = $started.AddSeconds($Seconds)
try {
  while (-not (Done)) {
    $left = [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds
    if ($left -le 0) {
      Write-Host "wait-for.ps1: after $Seconds s, $Path $(if ($Gone) { 'is still there' } else { "has no line matching '$Pattern'" })"
      exit 1
    }
    # A change wakes this at once; the cap only covers an event the watcher can miss (macOS coalesces them).
    $null = $watcher.WaitForChanged([IO.WatcherChangeTypes]::All, [Math]::Min($left, 2000))
  }
} finally {
  $watcher.Dispose()
}
Write-Host ("wait-for.ps1: {0} after {1:N1} s" -f $(if ($Gone) { "$Path is gone" } else { "$Path matched" }), ([DateTime]::UtcNow - $started).TotalSeconds)
