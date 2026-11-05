#!/bin/sh
# Velopack's package of the Mac app (docs/LIBRARIES.md, phase 5), from the bundle macos/sign-release.sh has just
# signed inside out: vpk adds its updater (Contents/MacOS/UpdateMac) and manifest (sq.version), signs those and the
# bundle, notarizes and staples the bundle when there is a notary key, and writes the feed for the osx channel (the full
# package, a delta from the last release when there is one, and releases.osx.json) to <out-dir>/velopack/osx. The
# bundle it packed replaces <out-dir>/Syft Notes.app, so the DMG made next holds exactly what Velopack updates.
#   sh macos/velopack-pack.sh <out-dir> <entitlements> <notarize: true|false>
# Uses STUDYSTASH_SIGN_IDENTITY and STUDYSTASH_SIGN_KEYCHAIN as sign-release.sh does, and STUDYSTASH_NOTARY_* to make
# the notary profile vpk asks for. Needs vpk (dotnet tool install -g vpk --version 1.2.161).
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=${1:?"usage: sh macos/velopack-pack.sh <out-dir> <entitlements> <true|false>"}
ENTITLEMENTS=${2:?}
NOTARIZE=${3:?}
APP="$OUT/Syft Notes.app"
FEED="$OUT/velopack/osx"
VERSION=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$ROOT/engine/Directory.Build.props")
KEYCHAIN=${STUDYSTASH_SIGN_KEYCHAIN:-}
command -v vpk > /dev/null || { echo "velopack-pack.sh: vpk isn't installed (dotnet tool install -g vpk --version 1.2.161)" >&2; exit 1; }

rm -rf "$FEED"
mkdir -p "$FEED"
# The last release's package, so this one comes with a delta from it (the first Velopack release has none).
# STUDYSTASH_VELOPACK_BASE: a folder of channel feeds to make the delta from instead (the bridge test's second version).
if [ -n "${STUDYSTASH_VELOPACK_BASE:-}" ]; then
  cp "$STUDYSTASH_VELOPACK_BASE/osx/"*.nupkg "$STUDYSTASH_VELOPACK_BASE/osx/releases.osx.json" "$FEED/"
else
  # With the run's token: unsigned, GitHub allows 60 calls an hour per address, which shared runners use up
  # (0.23.4 shipped with no Mac delta because of a 403 here).
  vpk download github --repoUrl https://github.com/Joseph-Rus/syftnotes --channel osx --outputDir "$FEED" ${GH_TOKEN:+--token "$GH_TOKEN"} \
    || echo "velopack-pack.sh: no earlier release on the osx channel, so no delta this time"
fi

set --
if [ "$NOTARIZE" = true ]; then
  # vpk notarizes with a stored profile; this one is made from the API key, in the run's own keychain.
  xcrun notarytool store-credentials syft-notes-notary --key "$STUDYSTASH_NOTARY_KEY" --key-id "$STUDYSTASH_NOTARY_KEY_ID" \
    --issuer "$STUDYSTASH_NOTARY_ISSUER_ID" ${KEYCHAIN:+--keychain "$KEYCHAIN"} > /dev/null
  set -- --notaryProfile syft-notes-notary
fi
vpk pack --packId SyftNotes --packVersion "$VERSION" --packDir "$APP" --mainExe StudyStash --channel osx \
  --packTitle "Syft Notes" --packAuthors "Syft Notes" --noInst \
  --signAppIdentity "$STUDYSTASH_SIGN_IDENTITY" ${KEYCHAIN:+--keychain "$KEYCHAIN"} --signDisableDeep \
  --signEntitlements "$ENTITLEMENTS" "$@" --outputDir "$FEED"
python3 -I "$ROOT/tools/velopack/trim-feed.py" "$FEED" osx "$VERSION"

# The packed bundle in place of the one it was made from: the DMG holds it.
PORTABLE="$FEED/SyftNotes-osx-Portable.zip"
[ -f "$PORTABLE" ] || { echo "velopack-pack.sh: vpk made no $PORTABLE" >&2; ls -l "$FEED" >&2; exit 1; }
rm -rf "$APP" "$OUT/velopack/unpacked"
mkdir -p "$OUT/velopack/unpacked"
ditto -x -k "$PORTABLE" "$OUT/velopack/unpacked"
[ -d "$OUT/velopack/unpacked/Syft Notes.app" ] || { echo "velopack-pack.sh: the package holds no Syft Notes.app" >&2; ls "$OUT/velopack/unpacked" >&2; exit 1; }
mv "$OUT/velopack/unpacked/Syft Notes.app" "$APP"
rm -rf "$OUT/velopack/unpacked" "$PORTABLE"
[ -x "$APP/Contents/MacOS/UpdateMac" ] || { echo "velopack-pack.sh: the bundle has no UpdateMac" >&2; exit 1; }
codesign --verify --deep --strict --verbose=2 "$APP"
if [ "$NOTARIZE" = true ]; then xcrun stapler validate "$APP"; fi
echo "Velopack's osx package of Syft Notes $VERSION:"
ls -l "$FEED"
