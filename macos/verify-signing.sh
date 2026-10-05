#!/bin/sh
# Checks that a built (or downloaded) Study Stash is signed the way a release is, and says what it found.
#   sh macos/verify-signing.sh "<path>/Study Stash.app" [<path>/Study-Stash.dmg] [notarized|signed|rehearsal]
# notarized (the default)  signed with a Developer ID, hardened runtime and timestamp, with library validation on, and
#                          accepted by Gatekeeper as notarized, with the ticket stapled (the DMG too, if given)
# signed                   the same without notarization: what CI checks when it has a certificate but no notary key
# rehearsal                signed by CI's throwaway self-signed identity: the signature is whole, hardened and
#                          timestamped, and Gatekeeper is not asked, as it would (rightly) refuse it
# Handy by hand on a copy you downloaded: sh macos/verify-signing.sh "/Applications/Study Stash.app"
# APPLE_TEAM_ID, when set, is the Team ID the signature must carry.
set -eu

APP=${1:?"usage: sh macos/verify-signing.sh \"<path>/Study Stash.app\" [<path>/Study-Stash.dmg] [notarized|signed|rehearsal]"}
DMG=""
MODE=notarized
shift
for arg; do
  case "$arg" in
    notarized | signed | rehearsal) MODE=$arg ;;
    *) DMG=$arg ;;
  esac
done
fail() { echo "verify-signing.sh: $*" >&2; exit 1; }

codesign --verify --deep --strict --verbose=2 "$APP"
info=$(codesign -dvv "$APP" 2>&1)
printf '%s\n' "$info" | grep -E '^(Identifier|Authority|TeamIdentifier|Timestamp|Runtime Version|CodeDirectory)' | sed 's/^/  /'
printf '%s\n' "$info" | grep -q 'flags=.*runtime' || fail "the hardened runtime isn't on"
printf '%s\n' "$info" | grep -q '^Timestamp=' || fail "no secure timestamp"
echo "  $(codesign -d -r- "$APP" 2>&1 | grep '^designated' | cut -c1-200)"

if [ "$MODE" = rehearsal ]; then
  printf '%s\n' "$info" | grep -q '^Authority=Study Stash rehearsal' || fail "not signed by the rehearsal's identity"
  [ -z "$DMG" ] || codesign --verify --verbose=2 "$DMG"
  echo "verify-signing.sh: the rehearsal's signature is whole (Gatekeeper not asked: this identity is a throwaway)."
  exit 0
fi

printf '%s\n' "$info" | grep -q '^Authority=Developer ID Application: ' || fail "not signed with a Developer ID Application certificate"
team=$(printf '%s\n' "$info" | sed -n 's/^TeamIdentifier=//p')
[ -n "$team" ] && [ "$team" != "not set" ] || fail "no Team ID in the signature"
[ -z "${APPLE_TEAM_ID:-}" ] || [ "$team" = "$APPLE_TEAM_ID" ] || fail "Team ID is $team, not $APPLE_TEAM_ID"
ents=$(codesign -d --entitlements - "$APP" 2>&1)
printf '%s\n' "$ents" | grep -q 'device.audio-input' || fail "the microphone entitlement is missing"
printf '%s\n' "$ents" | grep -q 'cs.allow-jit' || fail "allow-jit is missing"
if [ "${STUDYSTASH_LIBRARY_VALIDATION:-on}" != off ] && printf '%s\n' "$ents" | grep -q 'disable-library-validation'; then
  fail "library validation is switched off, though every library carries Team $team"
fi

if [ "$MODE" = notarized ]; then
  xcrun stapler validate "$APP"
  spctl --assess --type execute --verbose=4 "$APP" 2>&1 | sed 's/^/  /'
  spctl --assess --type execute "$APP" || fail "Gatekeeper refuses the app"
  if [ -n "$DMG" ]; then
    codesign --verify --verbose=2 "$DMG"
    xcrun stapler validate "$DMG"
    spctl --assess --type open --context context:primary-signature --verbose=4 "$DMG" 2>&1 | sed 's/^/  /'
    spctl --assess --type open --context context:primary-signature "$DMG" || fail "Gatekeeper refuses the DMG"
  fi
elif [ -n "$DMG" ]; then
  codesign --verify --verbose=2 "$DMG"
fi
echo "verify-signing.sh: signed by Team $team${MODE:+, $MODE}."
