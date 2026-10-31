#!/bin/sh
# Syft Notes installer for a Mac or Linux (on Windows, run install.ps1 instead).
#     curl -fsSL https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.sh | sh
# The same app for every computer: when it opens, setup asks whether it's just this computer, your laptop or your
# library. (An old "library" or "laptop" argument is still accepted, and changes nothing.)
#
# Downloads the newest release's Mac installer (a signed DMG holding one universal app, for Apple
# silicon and Intel), checks it against the release's SHA256SUMS.txt when there is one, and drags
# the app into Applications. Safe to rerun: it replaces an older Study Stash.app only once the new
# copy is fully in place.
#
#
# On Linux it downloads the release's tarball for this processor (x64 or arm64), checks it the same way, and runs the
# install.sh inside it, which puts the app in ~/.local/share/syft-notes with a menu entry and an icon (no root needed).
#
#   STUDYSTASH_DMG=<path>          install this DMG instead of downloading one (development, CI)
#   STUDYSTASH_TARBALL=<path>      Linux: install this tarball instead of downloading one
#   STUDYSTASH_APPLICATIONS=<dir>  install here instead of /Applications (or ~/Applications)
#   STUDYSTASH_NO_OPEN=1           don't open the app once it's installed
set -eu

SLUG=Joseph-Rus/study-stash

say() { printf '%s\n' "$*"; }
fail() {
  say "Syft Notes install failed: $*" >&2
  exit 1
}

# Downloads the latest release's $1 to $2, checked against its SHA256SUMS.txt when the release has one.
fetch() {
  command -v curl >/dev/null 2>&1 || fail "curl is missing"
  say "Downloading $1..."
  curl -fsSL -o "$2" "https://github.com/$SLUG/releases/latest/download/$1" \
    || fail "couldn't download $1 (https://github.com/$SLUG/releases/latest)"
  if curl -fsSL -o "$2.sums" "https://github.com/$SLUG/releases/latest/download/SHA256SUMS.txt" 2>/dev/null; then
    WANT=$(awk -v n="$1" '$2 == n { print $1 }' "$2.sums")
    if command -v sha256sum >/dev/null 2>&1; then GOT=$(sha256sum "$2" | awk '{ print $1 }'); else GOT=$(shasum -a 256 "$2" | awk '{ print $1 }'); fi
    [ -n "$WANT" ] && [ "$WANT" = "$GOT" ] || fail "the download didn't match its checksum, so nothing was installed"
  fi
}

case "$(uname -s)" in
  Darwin) ;;
  Linux)
    case "$(uname -m)" in
      x86_64 | amd64) NAME=Syft-Notes-linux-x64.tar.gz ;;
      aarch64 | arm64) NAME=Syft-Notes-linux-arm64.tar.gz ;;
      *) fail "there's no Syft Notes for this processor ($(uname -m)) yet" ;;
    esac
    WORK=$(mktemp -d "${TMPDIR:-/tmp}/syft-notes-install.XXXXXX")
    trap 'rm -rf "$WORK"' EXIT
    TARBALL="${STUDYSTASH_TARBALL:-}"
    if [ -z "$TARBALL" ]; then
      TARBALL="$WORK/$NAME"
      fetch "$NAME" "$TARBALL"
    fi
    tar -xzf "$TARBALL" -C "$WORK" || fail "couldn't unpack $NAME"
    [ -f "$WORK/syft-notes/install.sh" ] || fail "$NAME has no syft-notes/install.sh in it"
    sh "$WORK/syft-notes/install.sh"
    exit 0
    ;;
  *)
    echo "Syft Notes runs on a Mac, Linux or a Windows PC. On Windows, run install.ps1 instead." >&2
    exit 1
    ;;
esac

# One download for every role: setup asks. A role argument or STUDYSTASH_ROLE from an older line is ignored.
NAME=Syft-Notes.dmg

# Older releases connected a laptop with an address and password passed as env vars; Study
# Stash's own setup asks for them now, in the app, so this just points you at them.
ADDR="${STUDYSTASH_SERVER:-}"
[ -n "$ADDR" ] && say "Type this address in setup: $ADDR"

WORK=$(mktemp -d "${TMPDIR:-/tmp}/study-stash-install.XXXXXX")
cleanup() {
  hdiutil detach "$WORK/mount" -quiet >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

DMG="${STUDYSTASH_DMG:-}"
if [ -z "$DMG" ]; then
  DMG="$WORK/$NAME"
  fetch "$NAME" "$DMG"
fi

say "Opening $NAME..."
mkdir -p "$WORK/mount"
hdiutil attach -nobrowse -readonly -noautoopen -mountpoint "$WORK/mount" "$DMG" >/dev/null \
  || fail "couldn't open $NAME"
[ -d "$WORK/mount/Study Stash.app" ] || fail "$NAME has no Study Stash.app in it"

DEST="${STUDYSTASH_APPLICATIONS:-}"
if [ -z "$DEST" ]; then
  DEST=/Applications
  [ -w "$DEST" ] || DEST="$HOME/Applications"
fi
mkdir -p "$DEST"
say "Installing into $DEST..."
rm -rf "$DEST/Study Stash.app.new"
ditto "$WORK/mount/Study Stash.app" "$DEST/Study Stash.app.new" || fail "couldn't copy the app into $DEST"
rm -rf "$DEST/Study Stash.app"
mv "$DEST/Study Stash.app.new" "$DEST/Study Stash.app"

say "Installed Syft Notes in $DEST."
if [ "${STUDYSTASH_NO_OPEN:-}" != 1 ]; then
  open "$DEST/Study Stash.app"
fi
