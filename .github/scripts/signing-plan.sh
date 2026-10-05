#!/usr/bin/env bash
# Whether this ci run signs its installers, decided once, in the version job (see the header of ci.yml and
# docs/signing.md). Signing is switched on by GitHub secrets, and only for a run that builds installers (a release, or a
# rehearsal of one); with no secrets, every run builds today's ad hoc signed Mac app and unsigned Setup.exe.
#   signing-plan.sh plan      writes mac_signing, mac_notarize and win_signing to $GITHUB_OUTPUT
#   signing-plan.sh release   the publish job's last look: the installers it is about to publish are signed as this run
#                             was told to sign them, and none is a rehearsal's
# The environment names each secret, with _SET = true when it exists (the secrets' values never come here), plus
# EVENT_NAME, INSTALLERS and TEST_SIGNING for plan; and RUN_ID, REPO, GH_TOKEN, PUBLISH, REUSE, EXPECT_MAC, EXPECT_WIN
# for release.
#   *_signing   off (ad hoc / unsigned, as ever), real (the Developer ID and the Windows identity) or rehearsal (the
#               by-hand run with "test_signing": throwaway self-signed identities, which prove the signing steps and
#               nothing more, and are never published)
#   mac_notarize  true when the App Store Connect key is there too
set -u

out() { echo "$1=$2" >> "${GITHUB_OUTPUT:-/dev/stdout}"; }
say() { echo "$*"; if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "$*" >> "$GITHUB_STEP_SUMMARY"; fi; }
fail() { echo "::error::$*"; exit 1; }

# none, partial or complete: are all the secrets of a group there, none, or only some (a mistake, which stops the run
# before it ships something half-signed)?
group() {
  local set=0 missing=() n v
  for n in "$@"; do
    v="${n}_SET"
    if [ "${!v:-}" = true ]; then set=$((set + 1)); else missing+=("$n"); fi
  done
  if [ "$set" = 0 ]; then echo none; elif [ "${#missing[@]}" = 0 ]; then echo complete; else echo "partial (missing ${missing[*]})"; fi
}
need_whole() { # <what> <state>
  case "$2" in partial*) fail "$1: only some of the secrets are set, $2. Set them all or none (docs/signing.md)." ;; esac
}

plan() {
  local mac=off notarize=false win=off
  : "${EVENT_NAME:?}"
  if [ "${INSTALLERS:-}" = true ] && { [ "$EVENT_NAME" = push ] || [ "$EVENT_NAME" = workflow_dispatch ]; }; then
    if [ "$EVENT_NAME" = workflow_dispatch ] && [ "${TEST_SIGNING:-}" = true ]; then
      mac=rehearsal
      win=rehearsal
    else
      local cert notary azure pfx
      cert=$(group MACOS_CERT_P12_BASE64 MACOS_CERT_PASSWORD APPLE_TEAM_ID)
      notary=$(group APPLE_NOTARY_KEY_P8_BASE64 APPLE_NOTARY_KEY_ID APPLE_NOTARY_ISSUER_ID)
      azure=$(group AZURE_TENANT_ID AZURE_CLIENT_ID AZURE_CLIENT_SECRET WINDOWS_SIGNING_ENDPOINT WINDOWS_SIGNING_ACCOUNT WINDOWS_SIGNING_PROFILE)
      pfx=$(group WINDOWS_CERT_PFX_BASE64 WINDOWS_CERT_PASSWORD)
      need_whole "Mac certificate" "$cert"
      need_whole "Apple notary key" "$notary"
      need_whole "Azure Artifact Signing" "$azure"
      need_whole "Windows certificate file" "$pfx"
      if [ "$cert" = complete ]; then
        mac=real
        if [ "$notary" = complete ]; then
          notarize=true
        else
          echo "::warning::The Mac app is signed but has no notary key, so it won't be notarized and macOS will still ask (docs/signing.md)."
        fi
      elif [ "$notary" = complete ]; then
        fail "There is an Apple notary key but no Developer ID certificate (docs/signing.md)."
      fi
      if [ "$azure" = complete ] || [ "$pfx" = complete ]; then win=real; fi
    fi
  fi
  say "### Signing"
  say "- Mac: $mac$([ "$mac" = real ] && { [ "$notarize" = true ] && echo ", notarized" || echo ", not notarized"; })"
  say "- Windows: $win"
  if [ "$mac" = off ] && [ "$win" = off ] && [ "${INSTALLERS:-}" = true ]; then
    say "- no signing secrets here, so these installers are ad hoc signed (Mac) and unsigned (Windows), as ever"
  fi
  out mac_signing "$mac"
  out mac_notarize "$notarize"
  out win_signing "$win"
}

# How a run signed what it built, from the marker artifacts it left (signing-mac-<how>, signing-windows-<how>); a run
# from before there were any signed nothing.
built() { # <mac|windows>
  local names how
  names=$(gh api "repos/$REPO/actions/runs/$RUN_ID/artifacts" --paginate --jq '.artifacts[] | select(.expired == false) | .name') \
    || fail "couldn't list run $RUN_ID's artifacts"
  how=$(printf '%s\n' "$names" | sed -n "s/^signing-$1-//p" | head -n 1)
  echo "${how:-off}"
}

release() {
  : "${RUN_ID:?}" "${REPO:?}" "${EXPECT_MAC:?}" "${EXPECT_WIN:?}"
  local os expect how bad=0
  for os in mac windows; do
    if [ "$os" = mac ]; then expect=$EXPECT_MAC; else expect=$EXPECT_WIN; fi
    how=$(built "$os")
    say "- $os installer: signed as $how (this run expects $expect)"
    if [ "$how" = rehearsal ] && [ "${PUBLISH:-}" = true ]; then
      echo "::error::The $os installer was signed by a rehearsal's throwaway identity; it is never published."
      bad=1
    elif { [ "${PUBLISH:-}" = true ] || [ "${REUSE:-}" = true ]; } && [ "$how" != "$expect" ]; then
      echo "::error::The $os installer was built signed as '$how', but this run signs it as '$expect'. Run the rehearsal again, so its installers are the ones that are published."
      bad=1
    fi
  done
  [ "$bad" = 0 ] || exit 1
}

case "${1:-}" in
  plan) plan ;;
  release) release ;;
  *) fail "usage: signing-plan.sh plan|release" ;;
esac
