#!/bin/sh
# Builds Syft Notes for Linux: a self-contained publish for x64 and arm64 (so it needs no .NET install), pruned to the
# runtime each processor needs, each packed as the one download for it, Syft-Notes-linux-<x64|arm64>.tar.gz: a folder,
# syft-notes/, holding the app, its install.sh (which puts it in ~/.local/share/syft-notes with a menu entry and an
# icon) and its uninstall.sh. The app has no role of its own: setup asks what the computer is for.
#   sh linux/build.sh [out-dir]                    (default: dist/linux)
# STUDYSTASH_LINUX_RIDS="linux-arm64" builds just that one; STUDYSTASH_SKIP_WEB=1 uses web/dist as it is (CI builds
# it once); DOTNET picks the dotnet to run. Needs the .NET SDK, and Node with npm for the phone app (web/).
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=${1:-"$ROOT/dist/linux"}
DOTNET=${DOTNET:-dotnet}
RIDS=${STUDYSTASH_LINUX_RIDS:-"linux-x64 linux-arm64"}

VERSION=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$ROOT/engine/Directory.Build.props")
[ -n "$VERSION" ] || { echo "engine/Directory.Build.props has no StudyStashVersion" >&2; exit 1; }

mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)

# The phone app (web/) first, so each publish carries it beside the app as web/ (the library serves it at /app/).
if [ "${STUDYSTASH_SKIP_WEB:-}" != 1 ]; then
  command -v npm >/dev/null || { echo "npm is needed to build the phone app (web/)" >&2; exit 1; }
  (cd "$ROOT/web" && npm ci --no-audit --no-fund --silent && npm run build --silent)
fi

for RID in $RIDS; do
  case "$RID" in
    linux-x64 | linux-arm64) ;;
    *) echo "build.sh: $RID isn't a Linux runtime this builds" >&2; exit 1 ;;
  esac
  DEST="$OUT/$RID/syft-notes"
  rm -rf "$OUT/$RID"
  "$DOTNET" publish "$ROOT/engine/src/StudyStash.App" -c Release -r "$RID" --self-contained true \
    -p:DebugType=None -o "$DEST" --nologo -v quiet

  # Whisper.net and sherpa-onnx ship every system's native code under runtimes/; this processor keeps its own. Whisper's
  # Vulkan build goes too: on Linux it runs on the processor (HardwareProfile.WhisperCard), and a Vulkan library with
  # no driver behind it only slows the first lecture down while it fails to load.
  if [ -d "$DEST/runtimes" ]; then
    for d in "$DEST"/runtimes/*; do
      [ "$(basename "$d")" = "$RID" ] || rm -rf "$d"
    done
  fi
  # Our symbols, and the ones NuGet ships beside native code: nothing on a student's computer reads them.
  find "$DEST" -name '*.pdb' -delete

  # D6: the transcription model is never bundled. The one model file that is: the live words' small Whisper.
  bad=$(find "$DEST" -name '*.bin' -size +1M ! -name 'live-whisper-*.bin')
  [ -z "$bad" ] || { echo "the published app has a bundled model: $bad" >&2; exit 1; }

  # What makes this folder an installed copy that updates itself (Apps.LinuxInstallOf), and its version.
  printf '[app]\nversion = %s\n' "$VERSION" > "$DEST/study-stash.ini"
  cp "$HERE/install.sh" "$HERE/uninstall.sh" "$HERE/syft-notes.desktop" "$DEST/"
  cp "$ROOT/assets/icon.png" "$DEST/syft-notes.png"
  cp "$ROOT/LICENSE" "$DEST/LICENSE.txt"
  chmod 755 "$DEST/StudyStash" "$DEST/install.sh" "$DEST/uninstall.sh"

  NAME="Syft-Notes-$RID.tar.gz"
  tar -czf "$OUT/$NAME" -C "$OUT/$RID" syft-notes
  echo "Built $VERSION: $OUT/$NAME ($(du -h "$OUT/$NAME" | cut -f1))"
done
