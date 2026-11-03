#!/bin/sh
# The Velopack trial's Mac packing (.github/workflows/velopack-trial.yml, docs/LIBRARIES.md phase 5): two versions,
# <version>-trial.1 and -trial.2 (the second differs only in its version, so a delta exists), each packed three ways:
#   osx-universal  the universal "Study Stash.app" macos/build-app.sh makes (launcher.c picks the arm64 or x64 tree),
#                  signed inside out by macos/sign-release.sh, then packed as a prebuilt bundle with --signDisableDeep
#                  (vpk adds UpdateMac and sq.version and signs those and the bundle);
#   osx-arm64, osx-x64  one publish per processor, bundled and signed (--deep) by vpk itself.
# Signed with STUDYSTASH_SIGN_IDENTITY in STUDYSTASH_SIGN_KEYCHAIN (macos/signing-setup.sh selfsigned: a throwaway
# identity, nothing real, and nothing notarized). What it makes goes to out/: releases/<channel>/ (the feed),
# v1/<channel>/ (version 1's installers, which version 2's packing replaces) and sizes.txt.
#   sh tools/velopack-trial/mac-pack.sh
set -eu
ROOT=$(pwd)
OUT="$ROOT/out"
PROPS="$ROOT/engine/Directory.Build.props"
: "${STUDYSTASH_SIGN_IDENTITY:?}" "${STUDYSTASH_SIGN_KEYCHAIN:?}"
BASE=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$PROPS")
mkdir -p "$OUT"
size() { stat -f%z "$1"; }

for n in 1 2; do
  v="$BASE-trial.$n"
  sed -i '' "s:<StudyStashVersion>.*</StudyStashVersion>:<StudyStashVersion>$v</StudyStashVersion>:" "$PROPS"
  echo "=== $v: the universal app, as today"
  t0=$(date +%s)
  sh macos/build-app.sh "$ROOT/dist/mac"
  STUDYSTASH_SIGN_REHEARSAL=1 sh macos/sign-release.sh "$ROOT/dist/mac"
  echo "mac today-dmg v$n $(size "$ROOT/dist/mac/Syft-Notes.dmg") (built and signed in $(( $(date +%s) - t0 )) s)" >> "$OUT/sizes.txt"

  echo "=== $v: packing the universal bundle as it is"
  t0=$(date +%s)
  vpk pack --packId SyftNotes --packVersion "$v" --packDir "$ROOT/dist/mac/Study Stash.app" --mainExe StudyStash \
    --channel osx-universal --packTitle "Study Stash" --packAuthors "Syft Notes" \
    --signAppIdentity "$STUDYSTASH_SIGN_IDENTITY" --keychain "$STUDYSTASH_SIGN_KEYCHAIN" --signDisableDeep \
    --signEntitlements "$ROOT/macos/StudyStash.entitlements" --outputDir "$OUT/releases/osx-universal"
  echo "mac pack-seconds osx-universal v$n $(( $(date +%s) - t0 ))" >> "$OUT/sizes.txt"

  icns="$ROOT/dist/mac/Study Stash.app/Contents/Resources/AppIcon.icns"
  minos=$(/usr/libexec/PlistBuddy -c "Print :LSMinimumSystemVersion" "$ROOT/dist/mac/Study Stash.app/Contents/Info.plist")
  for arch in arm64 x64; do
    echo "=== $v: osx-$arch on its own"
    pub="$ROOT/dist/osx-$arch"
    rm -rf "$pub"
    dotnet publish engine/src/StudyStash.App -c Release -r "osx-$arch" --self-contained true -p:DebugType=None \
      -o "$pub" --nologo -v quiet
    for d in "$pub"/runtimes/*; do [ "$(basename "$d")" = "macos-$arch" ] || rm -rf "$d"; done
    [ "$arch" = arm64 ] || rm -f "$pub/ggml-metal.metal"
    find "$pub" -name '*.pdb' -delete
    sed -e "s/__VERSION__/$v/g" -e "s/__MINOS__/$minos/g" "$ROOT/macos/Info.plist" > "$ROOT/dist/Info-$arch.plist"
    t0=$(date +%s)
    vpk pack --packId SyftNotes --packVersion "$v" --packDir "$pub" --mainExe StudyStash --runtime "osx-$arch" \
      --channel "osx-$arch" --packTitle "Syft Notes" --packAuthors "Syft Notes" --icon "$icns" \
      --bundleId com.study-stash.app --plist "$ROOT/dist/Info-$arch.plist" \
      --signAppIdentity "$STUDYSTASH_SIGN_IDENTITY" --keychain "$STUDYSTASH_SIGN_KEYCHAIN" \
      --signEntitlements "$ROOT/macos/StudyStash.entitlements" --outputDir "$OUT/releases/osx-$arch"
    echo "mac pack-seconds osx-$arch v$n $(( $(date +%s) - t0 ))" >> "$OUT/sizes.txt"
  done

  if [ "$n" = 1 ]; then
    for c in osx-universal osx-arm64 osx-x64; do
      mkdir -p "$OUT/v1/$c"
      cp "$OUT/releases/$c/"*-Setup.pkg "$OUT/releases/$c/"*-Portable.zip "$OUT/v1/$c/"
    done
  fi
done

for c in osx-universal osx-arm64 osx-x64; do
  for f in "$OUT/releases/$c"/*; do
    echo "mac file $c $(basename "$f") $(size "$f")" >> "$OUT/sizes.txt"
  done
done
# What a student's Installer shows: the .pkg's Distribution (title, where it may install, which processors).
pkgutil --expand "$OUT/v1/osx-universal/"*-Setup.pkg "$ROOT/dist/pkg-expanded"
cp "$ROOT/dist/pkg-expanded/Distribution" "$OUT/osx-universal-Distribution.xml"
cat "$OUT/sizes.txt"
