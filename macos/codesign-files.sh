#!/bin/sh
# Signs each file named on its command line, for macos/sign-release.sh, which runs several of these side by side
# (xargs -P): every file gets the hardened runtime and, unless the identity is ad hoc ("-"), Apple's secure
# timestamp, which notarization needs and which comes from a server that now and then doesn't answer, so a failed
# signature is tried again before it stops the build. Reads STUDYSTASH_SIGN_IDENTITY (a certificate's hash or name)
# and, if the identity lives in a keychain of its own, STUDYSTASH_SIGN_KEYCHAIN. Prints nothing but a failure.
set -u
ID=${STUDYSTASH_SIGN_IDENTITY:?"codesign-files.sh: STUDYSTASH_SIGN_IDENTITY isn't set"}
KC=${STUDYSTASH_SIGN_KEYCHAIN:-}

sign() {
  f=$1
  set -- --force --options runtime
  [ "$ID" = "-" ] || set -- "$@" --timestamp
  [ -z "$KC" ] || set -- "$@" --keychain "$KC"
  codesign "$@" --sign "$ID" "$f"
}

for file; do
  attempt=1
  while :; do
    if out=$(sign "$file" 2>&1); then break; fi
    if [ "$attempt" -ge 4 ]; then
      echo "codesign-files.sh: couldn't sign $file after $attempt tries:" >&2
      echo "$out" >&2
      exit 1
    fi
    attempt=$((attempt + 1))
    sleep $((attempt * 5))
  done
done
