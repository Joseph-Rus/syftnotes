#!/usr/bin/env bash
# Runs the Canvas extension end to end in a real Chrome: a pretend Canvas, a test library, and the extension loaded
# into Chrome for Testing (branded Google Chrome no longer loads an extension from the command line).
#
#   bash engine/tests/extension-e2e.sh [--chrome-dir DIR]
#
# Chrome for Testing (stable) is downloaded into DIR once (default: ${TMPDIR:-/tmp}/studystash-cft). Set
# STUDYSTASH_E2E_CHROME to a Chrome binary to skip the download. Nothing here touches your own Chrome or its profile.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
chrome_dir="${TMPDIR:-/tmp}"
chrome_dir="${chrome_dir%/}/studystash-cft"
while [ $# -gt 0 ]; do
  case "$1" in
    --chrome-dir) chrome_dir="$2"; shift 2 ;;
    --chrome-dir=*) chrome_dir="${1#*=}"; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

platform() {
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) echo mac-arm64 ;;
    Darwin-x86_64) echo mac-x64 ;;
    Linux-x86_64) echo linux64 ;;
    *) echo "Chrome for Testing has no build for $(uname -s) $(uname -m)" >&2; exit 1 ;;
  esac
}

binary_in() {
  case "$2" in
    mac-*) echo "$1/chrome-$2/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing" ;;
    linux64) echo "$1/chrome-$2/chrome" ;;
  esac
}

if [ -z "${STUDYSTASH_E2E_CHROME:-}" ]; then
  p="$(platform)"
  bin="$(binary_in "$chrome_dir" "$p")"
  if [ ! -x "$bin" ]; then
    mkdir -p "$chrome_dir"
    json="$(curl -fsSL https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json)"
    # Channels are listed Stable first; the browser's own zip is the only one named chrome-<platform>.zip.
    url="$(printf '%s' "$json" | grep -o "https://[^\"]*/$p/chrome-$p\.zip" | head -1)"
    [ -n "$url" ] || { echo "couldn't find Chrome for Testing for $p" >&2; exit 1; }
    echo "Downloading $url"
    curl -fsSL -o "$chrome_dir/chrome.zip" "$url"
    rm -rf "$chrome_dir/chrome-$p"
    unzip -q -o "$chrome_dir/chrome.zip" -d "$chrome_dir"
    rm -f "$chrome_dir/chrome.zip"
  fi
  export STUDYSTASH_E2E_CHROME="$bin"
fi

echo "Chrome: $STUDYSTASH_E2E_CHROME"
exec dotnet test "$here/StudyStash.Core.Tests" --filter "FullyQualifiedName~ExtensionE2E" --logger "console;verbosity=detailed"
