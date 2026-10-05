#!/bin/sh
# Signs the Mac app with a real identity, notarizes it and makes the DMG again around it: what turns the ad hoc build
# of macos/build-app.sh into the installer a student opens with no trip to Privacy & Security. It's a step of its own,
# after the build, so that nothing the build runs (npm's packages, the .NET restore) ever sees the signing key.
#   sh macos/sign-release.sh [out-dir]      default: dist/mac (where build-app.sh made Study Stash.app)
# STUDYSTASH_SIGN_IDENTITY   the certificate to sign with: its hash or its name ("Developer ID Application: ...")
# STUDYSTASH_SIGN_KEYCHAIN   the keychain holding it, when it isn't one of the user's own
# STUDYSTASH_NOTARY_KEY, STUDYSTASH_NOTARY_KEY_ID, STUDYSTASH_NOTARY_ISSUER_ID
#                            an App Store Connect API key; with all three the app and the DMG are notarized and
#                            stapled, without them they're only signed (and Gatekeeper still asks)
# STUDYSTASH_SIGN_REHEARSAL=1  the identity is a throwaway self-signed one (CI's rehearsal): it has no Team ID, so
#                            the app keeps the ad hoc build's disable-library-validation, and nothing is notarized
# STUDYSTASH_LIBRARY_VALIDATION=off  keep disable-library-validation on a real Developer ID too, should an app
#                            ever need it (docs/signing.md)
# docs/signing.md says how to get all of these. Run by hand with a certificate in the login keychain it signs the
# same way: STUDYSTASH_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" sh macos/sign-release.sh
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=${1:-"$ROOT/dist/mac"}
APP="$OUT/Study Stash.app"
MACOS="$APP/Contents/MacOS"
fail() { echo "sign-release.sh: $*" >&2; exit 1; }

[ -d "$APP" ] || fail "no $APP (build it first: sh macos/build-app.sh)"
STUDYSTASH_SIGN_IDENTITY=${STUDYSTASH_SIGN_IDENTITY:-}
[ -n "$STUDYSTASH_SIGN_IDENTITY" ] || fail "STUDYSTASH_SIGN_IDENTITY isn't set"
export STUDYSTASH_SIGN_IDENTITY
export STUDYSTASH_SIGN_KEYCHAIN="${STUDYSTASH_SIGN_KEYCHAIN:-}"
rehearsal=${STUDYSTASH_SIGN_REHEARSAL:-}
notarize=false
if [ -n "${STUDYSTASH_NOTARY_KEY:-}" ] || [ -n "${STUDYSTASH_NOTARY_KEY_ID:-}" ] || [ -n "${STUDYSTASH_NOTARY_ISSUER_ID:-}" ]; then
  [ -n "${STUDYSTASH_NOTARY_KEY:-}" ] && [ -n "${STUDYSTASH_NOTARY_KEY_ID:-}" ] && [ -n "${STUDYSTASH_NOTARY_ISSUER_ID:-}" ] \
    || fail "the notary key needs all three of STUDYSTASH_NOTARY_KEY, STUDYSTASH_NOTARY_KEY_ID and STUDYSTASH_NOTARY_ISSUER_ID"
  [ -z "$rehearsal" ] || fail "a rehearsal's self-signed identity can't be notarized"
  notarize=true
fi

# Every file that can be code, inside out: the per-architecture trees (and a Frameworks folder, were one ever added)
# before the launcher, the launcher before the bundle. `codesign --deep` skips a Mach-O with no executable bit and
# the R2R .NET assemblies that embed native code, so every regular file is signed on its own, as build-app.sh does,
# only now with the real identity, the hardened runtime and a timestamp; several at once, as the timestamp server is
# the slow part. Resources (the icon, Info.plist) are sealed by the bundle's own signature.
echo "Signing the files in the app with ${STUDYSTASH_SIGN_IDENTITY}..."
for dir in "$MACOS" "$APP/Contents/Frameworks"; do
  [ -d "$dir" ] || continue
  find "$dir" -type f ! -path "$MACOS/StudyStash" -print0 | xargs -0 -n 25 -P 6 sh "$HERE/codesign-files.sh"
done
sh "$HERE/codesign-files.sh" "$MACOS/StudyStash"

# Library validation (the hardened runtime's rule that the app loads only libraries from Apple or its own team) can be
# left on only where the app's own libraries carry its Team ID: a Developer ID does, a throwaway self-signed identity
# doesn't, and then the app needs the ad hoc build's exemption to run at all.
team=$(codesign -dvv "$MACOS/StudyStash" 2>&1 | sed -n 's/^TeamIdentifier=//p')
case "$team" in "" | "not set") team="" ;; esac
entitlements="$HERE/StudyStash.developer-id.entitlements"
if [ -z "$team" ]; then
  [ -n "$rehearsal" ] || fail "this identity has no Team ID, so it isn't a Developer ID certificate (an Apple Development one, say, or a self-signed one)"
  echo "The rehearsal's identity has no Team ID: keeping disable-library-validation, as the ad hoc build has."
  entitlements="$HERE/StudyStash.entitlements"
elif [ "${STUDYSTASH_LIBRARY_VALIDATION:-on}" = off ]; then
  echo "STUDYSTASH_LIBRARY_VALIDATION=off: keeping disable-library-validation although the Team ID is $team."
  entitlements="$HERE/StudyStash.entitlements"
else
  echo "Team ID $team: library validation stays on (no disable-library-validation)."
fi
codesign --force --options runtime --timestamp --entitlements "$entitlements" \
  ${STUDYSTASH_SIGN_KEYCHAIN:+--keychain "$STUDYSTASH_SIGN_KEYCHAIN"} --sign "$STUDYSTASH_SIGN_IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

# Every Mach-O anywhere in the bundle (not only the ones signed above) carries the same Team ID, or, if there's none,
# the same certificate: one that was missed, or signed by another identity, would be refused when notarized, and with
# library validation on, refused when loaded.
problems="$OUT/.signing-problems"
: > "$problems"
find "$APP" -type f | while IFS= read -r f; do
  file "$f" | grep -q 'Mach-O' || continue
  info=$(codesign -dvv "$f" 2>&1)
  printf '%s\n' "$info" | grep -q '^Authority=' || echo "not signed by a certificate: $f" >> "$problems"
  [ -z "$team" ] || printf '%s\n' "$info" | grep -q "^TeamIdentifier=$team\$" || echo "another team's signature: $f" >> "$problems"
done
if [ -s "$problems" ]; then
  cat "$problems" >&2
  rm -f "$problems"
  fail "some Mach-O files in the bundle aren't signed by this identity (listed above)"
fi
rm -f "$problems"

if [ "$notarize" = true ]; then
  zip="$OUT/notarize.zip"
  rm -f "$zip"
  ditto -c -k --keepParent "$APP" "$zip"
  sh "$HERE/notarize.sh" submit "$zip"
  rm -f "$zip"
  sh "$HERE/notarize.sh" staple "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
fi

# The DMG again, now around the signed (and stapled) app, signed itself, then notarized and stapled the same way.
# shellcheck source=macos/dmg.sh
. "$HERE/dmg.sh"
stage_dmg "$APP" "Study Stash" "$OUT/Study-Stash.dmg"
codesign --force --timestamp ${STUDYSTASH_SIGN_KEYCHAIN:+--keychain "$STUDYSTASH_SIGN_KEYCHAIN"} \
  --sign "$STUDYSTASH_SIGN_IDENTITY" "$OUT/Study-Stash.dmg"
codesign --verify --verbose=2 "$OUT/Study-Stash.dmg"
if [ "$notarize" = true ]; then
  sh "$HERE/notarize.sh" submit "$OUT/Study-Stash.dmg"
  sh "$HERE/notarize.sh" staple "$OUT/Study-Stash.dmg"
fi

echo "Signed Study Stash.app and Study-Stash.dmg$([ "$notarize" = true ] && echo ", notarized and stapled" || echo " (not notarized)")."
