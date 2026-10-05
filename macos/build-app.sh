#!/bin/sh
# Builds "Study Stash.app": one universal bundle (D1: macos/launcher.c hosts .NET in-process, so the same running
# executable works on Apple silicon and Intel), then the one download, Study-Stash.dmg. The app has no role of its own:
# setup asks what the computer is for, and Settings can change it later (docs/one-download.md).
#   sh macos/build-app.sh [out-dir]      default: dist/mac
# Needs the .NET 10 SDK and the Xcode command line tools (clang, lipo, codesign, hdiutil, PlistBuddy, iconutil,
# xcrun swift, vtool, ditto, SetFile, osascript). Publishing both architectures downloads their runtime packs on first use.
# What this makes is signed ad hoc. A release that has an Apple Developer ID then signs it properly, notarizes it and
# makes the DMG again, with macos/sign-release.sh (docs/signing.md); nothing here depends on that.
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=${1:-"$ROOT/dist/mac"}
VERSION=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$ROOT/engine/Directory.Build.props")
[ -n "$VERSION" ] || { echo "build-app.sh: no StudyStashVersion in engine/Directory.Build.props" >&2; exit 1; }

APP="$OUT/Study Stash.app"
MACOS="$APP/Contents/MacOS"

rm -rf "$OUT"
mkdir -p "$MACOS" "$APP/Contents/Resources"

# The phone app (web/) is built first, so each publish carries it beside the app as web/ (the library serves it at
# /app/). It needs Node and npm; without them the build stops rather than ship a library whose Add a phone leads
# nowhere.
command -v npm >/dev/null || { echo "build-app.sh: npm is needed to build the phone app (web/)" >&2; exit 1; }
(cd "$ROOT/web" && npm ci --no-audit --no-fund --silent && npm run build --silent)
[ -f "$ROOT/web/dist/index.html" ] || { echo "build-app.sh: the phone app didn't build (web/dist)" >&2; exit 1; }

# One self-contained publish per architecture; the two trees can't be lipo-merged (System.Private.CoreLib and the
# R2R framework assemblies differ), so both ship whole and macos/launcher.c picks its tree at run time (D1).
for arch in arm64 x64; do
  dotnet publish "$ROOT/engine/src/StudyStash.App" -c Release -r "osx-$arch" --self-contained true \
    -p:DebugType=None -o "$MACOS/$arch" --nologo -v quiet
  # Whisper.net ships every platform's native libraries; each tree keeps only its own.
  for d in "$MACOS/$arch"/runtimes/*; do
    [ "$(basename "$d")" = "macos-$arch" ] || rm -rf "$d"
  done
done
rm -f "$MACOS/x64/ggml-metal.metal"   # no Metal on Intel

# D6: the Whisper model is never bundled.
big=$(find "$OUT" -name '*.bin' -size +1M -o -iname 'ggml-*.bin')
if [ -n "$big" ]; then
  echo "build-app.sh: a model file ended up in the bundle:" >&2
  echo "$big" >&2
  exit 1
fi

# LSMinimumSystemVersion: the highest minos any Mach-O in either tree asks for (today .NET says 12.0, Avalonia's
# native libraries say less, and Whisper's dylibs decide). vtool -show-build reports every arch slice's
# LC_BUILD_VERSION (modern) or LC_VERSION_MIN_MACOSX (older) load command.
tree_minos() {
  find "$1" -type f \( -perm -u+x -o -name '*.dylib' \) 2>/dev/null | while IFS= read -r f; do
    file "$f" | grep -q 'Mach-O' || continue
    vtool -show-build "$f" 2>/dev/null
  done | awk '
    /cmd LC_BUILD_VERSION/     { mode = "build"; next }
    /cmd LC_VERSION_MIN_MACOSX/ { mode = "min"; next }
    mode == "build" && /minos/     { print $2; mode = ""; next }
    mode == "min"   && /^ *version/ { print $2; mode = ""; next }
  '
}
MINOS=$( { tree_minos "$MACOS/arm64"; tree_minos "$MACOS/x64"; } | sort -t. -k1,1n -k2,2n | tail -1)
[ -n "$MINOS" ] || MINOS=12.0

# The launcher: the one universal binary in the bundle, built for both trees' minimum OS (D1).
clang -O2 -Wall -Werror -arch arm64 -arch x86_64 -mmacosx-version-min="$MINOS" -o "$MACOS/StudyStash" "$HERE/launcher.c"

# Icon: the cream "S." (macos/make_icon.swift draws every size from its one mark). Light only: an asset catalog's
# dark appearance is dropped for Mac app icons (macOS 26's dark and tinted icons need an Icon Composer file).
xcrun swift "$HERE/make_icon.swift" "$OUT/AppIcon.iconset"
iconutil -c icns "$OUT/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$OUT/AppIcon.iconset"

# Info.plist, PkgInfo.
sed -e "s/__VERSION__/$VERSION/g" -e "s/__MINOS__/$MINOS/g" \
  "$HERE/Info.plist" > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Sign inside-out (D1): every file in each per-arch tree first, ad hoc with the hardened runtime, then the launcher,
# then the bundle itself with its entitlements. `codesign --deep` alone can skip a Mach-O with no executable bit, so
# every one is found and signed explicitly — and not only the ones `file` calls Mach-O: some of .NET's own managed
# assemblies (the runtime's R2R-compiled framework dlls) embed native code for their target architecture inside what
# is otherwise a PE container, which recent macOS's --deep verification also treats as nested code needing its own
# signature. `codesign` signs those too (falling back to its generic, non-Mach-O signing format), so the simplest
# correct rule is to sign every regular file, not just the ones that look like a Mach-O from the outside.
sign_tree() {
  find "$1" -type f | while IFS= read -r f; do
    codesign --force --options runtime --sign - "$f"
  done
}
sign_tree "$MACOS/arm64"
sign_tree "$MACOS/x64"
codesign --force --options runtime --sign - "$MACOS/StudyStash"
codesign --force --options runtime --entitlements "$HERE/StudyStash.entitlements" --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

echo "Built Study Stash $VERSION (universal, min $MINOS):"
du -sh "$APP"

# The DMG (macos/dmg.sh): a stage folder with the app plus a link to /Applications, with the app's icon on the disk.
# shellcheck source=macos/dmg.sh
. "$HERE/dmg.sh"
stage_dmg "$APP" "Study Stash" "$OUT/Study-Stash.dmg"

echo "DMG:"
du -sh "$OUT/Study-Stash.dmg"
