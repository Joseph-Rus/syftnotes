#!/bin/sh
# Takes Syft Notes off this account: the app, its menu entry, its icon, `syft-notes`, starting at login and running in
# the background. Your lectures and notes stay where they are (Documents/Study Stash, and ~/.study-stash for the app's
# own settings).
set -eu
DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
CONFIG=${XDG_CONFIG_HOME:-"$HOME/.config"}
DEST=${SYFT_NOTES_DIR:-"$DATA/syft-notes"}

# Syft Notes in the background ("Answer your phone when Syft Notes is closed"): its systemd unit or autostart entry.
for f in "$CONFIG"/systemd/user/syft-notes-background*.service; do
  [ -e "$f" ] || continue
  systemctl --user disable --now "$(basename "$f")" 2>/dev/null || true
  rm -f "$f"
done
systemctl --user daemon-reload 2>/dev/null || true
rm -f "$CONFIG"/autostart/syft-notes-background*.desktop
pkill -f "$DEST/StudyStash" 2>/dev/null || true
rm -f "$DATA/applications/syft-notes.desktop" "$DATA/icons/hicolor/256x256/apps/syft-notes.png" "$HOME/.local/bin/syft-notes"
rm -f "$CONFIG"/autostart/com.study-stash.app*.desktop
rm -rf "$DEST"
printf '%s\n' "Syft Notes is uninstalled. Your lectures and notes are still where they were."
