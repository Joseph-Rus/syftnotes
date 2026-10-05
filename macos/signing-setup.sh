#!/bin/sh
# Sets up, and tears down, what macos/sign-release.sh signs with, on a CI runner (docs/signing.md).
#   sh macos/signing-setup.sh import       the Developer ID Application certificate, from MACOS_CERT_P12_BASE64 and
#                                          MACOS_CERT_PASSWORD (and APPLE_TEAM_ID, to pick the right one), into a
#                                          keychain of its own that exists only for this run
#   sh macos/signing-setup.sh selfsigned   a throwaway self-signed identity instead, for a rehearsal: it proves the
#                                          signing steps, not Apple's notarization, and signs nothing real
#   sh macos/signing-setup.sh notary       the App Store Connect API key, from APPLE_NOTARY_KEY_P8_BASE64,
#                                          APPLE_NOTARY_KEY_ID and APPLE_NOTARY_ISSUER_ID
#   sh macos/signing-setup.sh cleanup      deletes all of it and puts the keychain search list back
# What sign-release.sh needs is written to $GITHUB_ENV (to the output, when that isn't set). Secrets are never printed.
set -eu

DIR="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/study-stash-signing"
KC="$DIR/signing.keychain-db"
fail() { echo "signing-setup.sh: $*" >&2; exit 1; }
# macOS has no timeout command, and the security tool can wait for a password nobody can type: a call that takes too long
# is given up on (the runner is thrown away after the run), not waited for until the job's own timeout.
limit() { perl -e 'alarm shift; exec @ARGV' "$@"; }
emit() {
  case "$2" in *'
'*) fail "$1 has a line break in it" ;; esac
  if [ -n "${GITHUB_ENV:-}" ]; then echo "$1=$2" >> "$GITHUB_ENV"; else echo "$1=$2"; fi
}

make_keychain() {
  mkdir -p "$DIR"
  chmod 700 "$DIR"
  # A password for this run only, never printed (and masked, should something print it).
  kcpw=$(/usr/bin/openssl rand -hex 24)
  [ -z "${GITHUB_ACTIONS:-}" ] || echo "::add-mask::$kcpw"
  security list-keychains -d user | tr -d '"' | sed 's/^ *//' > "$DIR/searchlist"
  security create-keychain -p "$kcpw" "$KC"
  security set-keychain-settings -lut 21600 "$KC"
  security unlock-keychain -p "$kcpw" "$KC"
  # The new keychain goes first in the search list, so codesign finds the identity; cleanup puts the old list back.
  set --
  while IFS= read -r line; do set -- "$@" "$line"; done < "$DIR/searchlist"
  security list-keychains -d user -s "$KC" "$@"
}

# Lets codesign use the key with no prompt, which nobody could answer on a runner.
allow_codesign() {
  security set-key-partition-list -S apple-tool:,apple: -s -k "$kcpw" "$KC" > /dev/null
}

case "${1:-}" in
  import)
    : "${MACOS_CERT_P12_BASE64:?}" "${MACOS_CERT_PASSWORD:?}"
    make_keychain
    printf '%s' "$MACOS_CERT_P12_BASE64" | base64 -D > "$DIR/cert.p12"
    security import "$DIR/cert.p12" -k "$KC" -P "$MACOS_CERT_PASSWORD" -T /usr/bin/codesign -T /usr/bin/security > /dev/null
    rm -f "$DIR/cert.p12"
    allow_codesign
    # The certificate's chain, in case the .p12 left the intermediate out: signing builds a chain to Apple's root.
    if curl -fsSL --retry 3 -o "$DIR/DeveloperIDG2CA.cer" https://www.apple.com/certificateauthority/DeveloperIDG2CA.cer; then
      security import "$DIR/DeveloperIDG2CA.cer" -k "$KC" > /dev/null 2>&1 || true
    fi
    list=$(security find-identity -v -p codesigning "$KC" | grep '"Developer ID Application: ' || true)
    if [ -n "${APPLE_TEAM_ID:-}" ]; then list=$(printf '%s\n' "$list" | grep "($APPLE_TEAM_ID)\"" || true); fi
    [ -n "$list" ] || fail "no usable \"Developer ID Application\" certificate${APPLE_TEAM_ID:+ for team $APPLE_TEAM_ID} in the .p12 (is it an Apple Development one, or the wrong password?). What the keychain holds: $(security find-identity -p codesigning "$KC" | grep -c ')') identities."
    emit STUDYSTASH_SIGN_KEYCHAIN "$KC"
    emit STUDYSTASH_SIGN_IDENTITY "$(printf '%s\n' "$list" | head -n 1 | awk '{print $2}')"
    echo "Signing with: $(printf '%s\n' "$list" | head -n 1 | sed 's/.*"\(.*\)".*/\1/')"
    ;;
  selfsigned)
    make_keychain
    cat > "$DIR/openssl.cnf" <<'CNF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Study Stash rehearsal (self-signed, not a real signature)
OU = REHEARSAL
O = Study Stash rehearsal
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
CNF
    /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 2 -config "$DIR/openssl.cnf" \
      -keyout "$DIR/key.pem" -out "$DIR/cert.pem" 2> /dev/null
    /usr/bin/openssl pkcs12 -export -inkey "$DIR/key.pem" -in "$DIR/cert.pem" -out "$DIR/identity.p12" -passout "pass:$kcpw"
    security import "$DIR/identity.p12" -k "$KC" -P "$kcpw" -T /usr/bin/codesign -T /usr/bin/security > /dev/null
    rm -f "$DIR/key.pem" "$DIR/identity.p12"
    allow_codesign
    # codesign only offers an identity whose certificate the system trusts: trust this one, for code signing only.
    # This is for a runner that's thrown away after the run (sudo needs no password there), never a Mac of your own.
    limit 120 sudo -n security add-trusted-cert -d -r trustRoot -p codeSign -k "$KC" "$DIR/cert.pem" \
      || fail "couldn't make the system trust the rehearsal's identity for code signing (a runner with passwordless sudo is needed)"
    list=$(security find-identity -v -p codesigning "$KC" | grep ')' | grep -v 'valid identities' || true)
    [ -n "$list" ] || { security find-identity -p codesigning "$KC" >&2; fail "the self-signed identity isn't usable for code signing"; }
    emit STUDYSTASH_SIGN_KEYCHAIN "$KC"
    emit STUDYSTASH_SIGN_IDENTITY "$(printf '%s\n' "$list" | head -n 1 | awk '{print $2}')"
    emit STUDYSTASH_SIGN_REHEARSAL 1
    echo "Rehearsing with a throwaway self-signed identity: nothing signed here is a real signature."
    ;;
  notary)
    : "${APPLE_NOTARY_KEY_P8_BASE64:?}" "${APPLE_NOTARY_KEY_ID:?}" "${APPLE_NOTARY_ISSUER_ID:?}"
    mkdir -p "$DIR"
    chmod 700 "$DIR"
    printf '%s' "$APPLE_NOTARY_KEY_P8_BASE64" | base64 -D > "$DIR/notary-key.p8"
    chmod 600 "$DIR/notary-key.p8"
    emit STUDYSTASH_NOTARY_KEY "$DIR/notary-key.p8"
    emit STUDYSTASH_NOTARY_KEY_ID "$APPLE_NOTARY_KEY_ID"
    emit STUDYSTASH_NOTARY_ISSUER_ID "$APPLE_NOTARY_ISSUER_ID"
    ;;
  cleanup)
    if [ -f "$DIR/searchlist" ]; then
      echo "Putting the keychain search list back..."
      set --
      while IFS= read -r line; do set -- "$@" "$line"; done < "$DIR/searchlist"
      limit 30 security list-keychains -d user -s "$@" || echo "(couldn't put the search list back)"
    fi
    if [ -f "$KC" ]; then
      echo "Deleting the signing keychain..."
      limit 30 security delete-keychain "$KC" || echo "(couldn't delete the signing keychain)"
    fi
    if [ -f "$DIR/cert.pem" ]; then
      echo "Taking the rehearsal's trust away..."
      limit 30 sudo -n security remove-trusted-cert -d "$DIR/cert.pem" || echo "(couldn't take the trust away; this runner is thrown away after the run)"
    fi
    for f in cert.p12 cert.pem key.pem identity.p12 openssl.cnf DeveloperIDG2CA.cer notary-key.p8 searchlist; do
      rm -f "$DIR/$f"
    done
    rmdir "$DIR" 2> /dev/null || true
    echo "The signing keychain and keys are gone."
    ;;
  *)
    fail "usage: sh macos/signing-setup.sh import|selfsigned|notary|cleanup"
    ;;
esac
