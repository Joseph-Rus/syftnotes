#!/bin/sh
# Notarization, with Apple's notary service, for macos/sign-release.sh.
#   sh macos/notarize.sh submit <file.zip|file.dmg>   sends it, waits for Apple's verdict, and stops unless it's Accepted
#   sh macos/notarize.sh staple <app|dmg>             staples the ticket to it (and checks it took)
# Reads the App Store Connect API key from STUDYSTASH_NOTARY_KEY (the .p8 file's path), STUDYSTASH_NOTARY_KEY_ID and
# STUDYSTASH_NOTARY_ISSUER_ID. Nothing here prints them.
set -eu

action=${1:?"usage: sh macos/notarize.sh submit|staple <path>"}
target=${2:?"usage: sh macos/notarize.sh submit|staple <path>"}

case "$action" in
  submit)
    : "${STUDYSTASH_NOTARY_KEY:?}" "${STUDYSTASH_NOTARY_KEY_ID:?}" "${STUDYSTASH_NOTARY_ISSUER_ID:?}"
    echo "Notarizing $(basename "$target") (Apple usually answers in a few minutes; a new account's first ones can take much longer)..."
    result=$(mktemp "${TMPDIR:-/tmp}/notary.XXXXXX")
    trap 'rm -f "$result"' EXIT
    # notarytool exits nonzero for a rejection as well as for a lost connection; the verdict below says which.
    xcrun notarytool submit "$target" --key "$STUDYSTASH_NOTARY_KEY" --key-id "$STUDYSTASH_NOTARY_KEY_ID" \
      --issuer "$STUDYSTASH_NOTARY_ISSUER_ID" --wait --timeout "${STUDYSTASH_NOTARY_TIMEOUT:-45m}" \
      --output-format json > "$result" || true
    status=$(plutil -extract status raw -o - "$result" 2>/dev/null || echo "no answer")
    id=$(plutil -extract id raw -o - "$result" 2>/dev/null || echo "")
    echo "Apple's verdict on $(basename "$target"): $status${id:+ (submission $id)}"
    if [ "$status" != "Accepted" ]; then
      if [ -n "$id" ]; then
        echo "Apple's log for it:"
        xcrun notarytool log "$id" --key "$STUDYSTASH_NOTARY_KEY" --key-id "$STUDYSTASH_NOTARY_KEY_ID" \
          --issuer "$STUDYSTASH_NOTARY_ISSUER_ID" || true
      else
        cat "$result" || true
      fi
      exit 1
    fi
    ;;
  staple)
    # The ticket can take a moment to reach the CDN stapler reads it from, right after an acceptance.
    attempt=1
    until xcrun stapler staple "$target"; do
      [ "$attempt" -lt 5 ] || { echo "notarize.sh: couldn't staple $target" >&2; exit 1; }
      attempt=$((attempt + 1))
      sleep 20
    done
    xcrun stapler validate "$target"
    ;;
  *)
    echo "usage: sh macos/notarize.sh submit|staple <path>" >&2
    exit 1
    ;;
esac
