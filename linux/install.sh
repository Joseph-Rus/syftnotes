#!/bin/sh
# Puts the Syft Notes in this folder on this account: the app in ~/.local/share/syft-notes (where it updates itself),
# Syft Notes in the applications menu with its icon, and `syft-notes` on the PATH (~/.local/bin). Nothing needs root.
# Safe to rerun: an older copy is replaced only once the new one is fully in place.
#   sh install.sh                     (from the unpacked syft-notes/ folder)
#   SYFT_NOTES_DIR=<dir>              install here instead of ~/.local/share/syft-notes
#   STUDYSTASH_NO_OPEN=1              don't open the app once it's installed
set -eu

SRC=$(cd "$(dirname "$0")" && pwd)
DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
DEST=${SYFT_NOTES_DIR:-"$DATA/syft-notes"}
BIN="$HOME/.local/bin"

say() { printf '%s\n' "$*"; }
fail() {
  say "Syft Notes install failed: $*" >&2
  exit 1
}

[ -x "$SRC/StudyStash" ] || fail "there's no StudyStash program beside install.sh"
VERSION=$("$SRC/StudyStash" version 2>/dev/null) || fail "this copy doesn't run on this computer ($(uname -m))"

# What recording and the app's windows need from the system. Said, not installed: the package names differ by
# distribution, and nothing here runs as root.
command -v pactl >/dev/null 2>&1 || command -v pw-cli >/dev/null 2>&1 \
  || say "Note: recording needs PipeWire or PulseAudio (most desktops have one running)."

# Syft Notes in the background ("Answer your phone when Syft Notes is closed") runs the copy in $DEST: it stops before
# the files are replaced, and starts again on the new ones. One without a systemd of its own (an autostart entry) is
# started again by the app when it opens, or at the next login.
CONFIG=${XDG_CONFIG_HOME:-"$HOME/.config"}
units=""
for f in "$CONFIG"/systemd/user/syft-notes-background*.service; do
  [ -e "$f" ] && grep -qF "$DEST/StudyStash" "$f" && units="$units $(basename "$f")"
done
for u in $units; do systemctl --user stop "$u" 2>/dev/null || true; done
pkill -f "$DEST/StudyStash .*helper" 2>/dev/null || true

if [ "$SRC" != "$DEST" ]; then
  mkdir -p "$(dirname "$DEST")"
  NEW="$DEST.new.$$"
  rm -rf "$NEW"
  cp -R "$SRC" "$NEW"
  if [ -e "$DEST" ]; then
    rm -rf "$DEST.old"
    mv "$DEST" "$DEST.old"
  fi
  mv "$NEW" "$DEST" || { [ -e "$DEST.old" ] && mv "$DEST.old" "$DEST"; fail "couldn't move the app into $DEST"; }
  rm -rf "$DEST.old"
fi

mkdir -p "$DATA/applications" "$DATA/icons/hicolor/256x256/apps" "$BIN"
cp "$DEST/syft-notes.png" "$DATA/icons/hicolor/256x256/apps/syft-notes.png"
sed "s|^Exec=StudyStash|Exec=\"$DEST/StudyStash\"|" "$DEST/syft-notes.desktop" > "$DATA/applications/syft-notes.desktop"
ln -sf "$DEST/StudyStash" "$BIN/syft-notes"
for u in $units; do systemctl --user start "$u" 2>/dev/null || true; done
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$DATA/applications" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true

say "Syft Notes $VERSION is installed in $DEST."
say "Open it from your applications menu, or run: syft-notes"
if [ "${STUDYSTASH_NO_OPEN:-}" != 1 ] && [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
  nohup "$DEST/StudyStash" >/dev/null 2>&1 &
fi
