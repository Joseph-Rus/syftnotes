#!/bin/sh
# Takes Syft Notes off this account: the app, its menu entry, its icon, `syft-notes` and starting at login. Your
# lectures and notes stay where they are (Documents/Study Stash, and ~/.study-stash for the app's own settings).
set -eu
DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
CONFIG=${XDG_CONFIG_HOME:-"$HOME/.config"}
DEST=${SYFT_NOTES_DIR:-"$DATA/syft-notes"}

pkill -f "$DEST/StudyStash" 2>/dev/null || true
rm -f "$DATA/applications/syft-notes.desktop" "$DATA/icons/hicolor/256x256/apps/syft-notes.png" "$HOME/.local/bin/syft-notes"
rm -f "$CONFIG"/autostart/com.study-stash.app*.desktop
rm -rf "$DEST"
printf '%s\n' "Syft Notes is uninstalled. Your lectures and notes are still where they were."
