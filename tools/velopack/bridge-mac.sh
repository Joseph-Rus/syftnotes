#!/bin/sh
# The Velopack bridge test on a Mac (.github/workflows/velopack-bridge.yml), on a throwaway runner only: it installs
# into /Applications, writes LaunchAgents and opens the app. Every step a student's Mac goes through, in order:
#   1. the released Syft Notes before the bridge (OLD_DMG, the published 0.22.1 one: "Study Stash.app"), set up as a
#      student's is: start at login, the background library, Claude Desktop and ChatGPT (Codex) connected, a library
#      with a class, settings and lectures;
#   2. the update to the bridge release as that copy's own updater does it (Updater.cs, ApplyMacAsync: the DMG mounted,
#      the bundle of its own name copied out of it, through the hidden Study Stash.app link, checked, swapped in, and
#      the app opened again);
#   3. the bridge copy renaming itself to Syft Notes.app on that first start, and putting right what names the old
#      program;
#   4. checked: one app, Syft Notes.app, signed and running the bridge version; no Study Stash.app beside it; start at
#      login, the background library and the AI connections (each started as its app would, and asked for the
#      library's classes) name the new place; settings and lectures untouched;
#   5. a later update, Velopack to Velopack (V2_FEED, with a delta), by the app's own updater;
#   6. the uninstall a Mac has (the app to the Trash): what's left of it is listed.
# The copies under test have the test helpers, only so that they ask this runner's stand-in for GitHub and look for an
# update 5 seconds after starting.
#   OLD_DMG=... BRIDGE_DMG=... V1_FEED=... V2_FEED=... V1=... V2=... sh tools/velopack/bridge-mac.sh
set -u
ROOT=$(pwd)
RES="$ROOT/results"
mkdir -p "$RES"
RESULTS="$RES/mac.txt"
WAIT="pwsh -NoProfile -File $ROOT/tools/velopack/wait-for.ps1"
PORT=8765
failed=0
say() { echo "$*" | tee -a "$RESULTS"; }
check() { if [ "$1" = 0 ]; then say "ok: $2"; else say "FAILED: $2"; failed=1; fi; }
waitfor() { $WAIT -Path "$1" -Pattern "$2" -Seconds "$3"; check $? "$4"; }

OLD_APP="/Applications/Study Stash.app"
APP="/Applications/Syft Notes.app"
PROGRAM="$APP/Contents/MacOS/StudyStash"
H="$HOME/.study-stash"
LOGS="$H/logs"
AGENTS="$HOME/Library/LaunchAgents"
CLAUDE="$HOME/Library/Application Support/Claude/claude_desktop_config.json"
CODEX="$HOME/.codex/config.toml"
DOCS="$HOME/Documents/Study Stash"

# The stand-in for GitHub: a folder served on this runner with a release and the osx Velopack feed. Apps opened by
# launchd (open, Velopack's relaunch) get the test switches from launchctl setenv: this throwaway runner's only.
SERVED="$ROOT/served"
mkdir -p "$SERVED"
publish() {
  rm -f "$SERVED"/*
  cp "$1"/* "$SERVED/"
  python3 -I "$ROOT/tools/velopack/fake-release.py" "$SERVED" "$2" "http://127.0.0.1:$PORT"
}
publish "$V1_FEED" "$V1"
python3 -m http.server "$PORT" --bind 127.0.0.1 --directory "$SERVED" > "$RES/http.log" 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null; cp "$LOGS"/*.log "$HOME/Library/Logs/"velopack*.log "$RES/" 2>/dev/null' EXIT
for kv in "STUDYSTASH_RELEASES_URL=http://127.0.0.1:$PORT/release.json" "STUDYSTASH_VELOPACK_FEED=http://127.0.0.1:$PORT/" "STUDYSTASH_UPDATE_AFTER=5"; do
  launchctl setenv "${kv%%=*}" "${kv#*=}"
  export "${kv?}"
done
quit_app() { # every copy of the app and its library, then wait for them to go
  pkill -f "/Contents/MacOS/StudyStash" 2>/dev/null || true
  pwsh -NoProfile -Command 'Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue' || true
}
mount() { mkdir -p "$2" && hdiutil attach -nobrowse -readonly -noautoopen -mountpoint "$2" "$1" > /dev/null; }

# --- 1. the release before the bridge, as a student has it
mount "$OLD_DMG" "$RES/mnt-old"
ditto "$RES/mnt-old/Study Stash.app" "$OLD_APP"
hdiutil detach -quiet "$RES/mnt-old"
OLD_PROGRAM="$OLD_APP/Contents/MacOS/StudyStash"
say "before: $("$OLD_PROGRAM" version) at $OLD_APP"
"$OLD_PROGRAM" init > /dev/null
mkdir -p "$DOCS" "$AGENTS" "$(dirname "$CLAUDE")" "$(dirname "$CODEX")"
head -c 64 /dev/urandom | base64 > "$H/bridge-test.txt"
head -c 64 /dev/urandom | base64 > "$DOCS/bridge-test.txt"
shasum -a 256 "$H/bridge-test.txt" "$DOCS/bridge-test.txt" > "$RES/markers.sha256"
# What the app itself writes for these (Desktop.StartAtLogin, BackgroundLibrary.Register, ClaudeSetup.Connect).
agent() { # label, then the program's arguments
  label=$1; shift
  { printf '<?xml version="1.0" encoding="UTF-8"?>\n<plist version="1.0"><dict><key>Label</key><string>%s</string><key>ProgramArguments</key><array>' "$label"
    for a in "$@"; do printf '<string>%s</string>' "$a"; done
    printf '</array><key>RunAtLoad</key><true/></dict></plist>\n'; } > "$AGENTS/$label.plist"
}
agent com.study-stash.app "$OLD_PROGRAM" --background
agent com.study-stash.background "$OLD_PROGRAM" --home "$H" helper --keep-alive
printf '{"mcpServers":{"syft-notes":{"command":"%s","args":["mcp","--client","claude-desktop"]}}}\n' "$OLD_PROGRAM" > "$CLAUDE"
printf '[mcp_servers.syft-notes]\ncommand = "%s"\nargs = ["mcp", "--client", "codex"]\n' "$OLD_PROGRAM" > "$CODEX"

# --- 2. the old copy's own update to the bridge release (Updater.cs, ApplyMacAsync, step for step)
mount "$BRIDGE_DMG" "$RES/mnt-bridge"
fresh="/Applications/.Study Stash.app.new"
rm -rf "$fresh"
ditto "$RES/mnt-bridge/Study Stash.app" "$fresh"
hdiutil detach -quiet "$RES/mnt-bridge"
plist=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$fresh/Contents/Info.plist")
codesign --verify --deep --strict "$fresh"; signed=$?
runs=$("$fresh/Contents/MacOS/StudyStash" version)
[ "$plist" = "$V1" ] && [ "$signed" = 0 ] && [ "$runs" = "$V1" ]
check $? "the old updater would take the bridge DMG's app (its Info.plist says $plist, codesign $signed, it runs $runs)"
mv "$OLD_APP" "$OLD_APP.old" && mv "$fresh" "$OLD_APP" && rm -rf "$OLD_APP.old"
open "$OLD_APP" --args --background

# --- 3. the bridge copy renames itself and opens again as Syft Notes.app; that start puts the rest right
waitfor "$CLAUDE" "Syft Notes.app" 240 "the bridge copy renamed itself and, opened again, put the AI connections right"
grep -h "\[rename\]\|\[moved\]" "$LOGS/app.log" | sed 's/^/  log: /' | tee -a "$RESULTS"

# --- 4. where everything stands
[ ! -e "$OLD_APP" ]; check $? "no Study Stash.app is left in /Applications"
[ "$("$PROGRAM" version)" = "$V1" ]; check $? "Syft Notes.app runs $V1"
codesign --verify --deep --strict "$APP"; check $? "Syft Notes.app's signature is valid"
[ -x "$APP/Contents/MacOS/UpdateMac" ]; check $? "Syft Notes.app is Velopack's bundle (UpdateMac)"
grep -qF "$PROGRAM" "$AGENTS/com.study-stash.app.plist"; check $? "start at login opens Syft Notes.app"
grep -qF "$PROGRAM" "$AGENTS/com.study-stash.background.plist"; check $? "the background library runs from Syft Notes.app"
grep -qF "$PROGRAM" "$CODEX"; check $? "ChatGPT's (Codex's) connection starts Syft Notes.app"
shasum -a 256 -c "$RES/markers.sha256" > /dev/null; check $? "settings (~/.study-stash) and lectures (Documents/Study Stash) are untouched"
# The background library, registered again from the new place, is what answers; started here if it isn't yet.
python3 -I "$ROOT/tools/velopack/wait-port.py" 8787 30 || { "$PROGRAM" serve > "$RES/serve.log" 2>&1 & python3 -I "$ROOT/tools/velopack/wait-port.py" 8787 120; }
check $? "the library runs from Syft Notes.app"
python3 -I "$ROOT/tools/velopack/mcp-check.py" "$CLAUDE" "Example 101" 60; check $? "Claude Desktop's connection reaches the library and lists its class"
python3 -I "$ROOT/tools/velopack/mcp-check.py" "$CODEX" "Example 101" 60; check $? "ChatGPT's (Codex's) connection reaches the library and lists its class"

# --- 5. a later release, Velopack to Velopack, installed by the app's own updater once idle
publish "$V2_FEED" "$V2"
quit_app
open "$APP" --args --background
waitfor "$LOGS/app.log" "Syft Notes $V2 starting" 600 "the app updated itself to $V2 through Velopack and opened again"
[ "$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP/Contents/Info.plist")" = "$V2" ]; check $? "Syft Notes.app is $V2, in the same place"
codesign --verify --deep --strict "$APP"; check $? "its signature is still valid after the update"
grep -h "\[update\]" "$LOGS/app.log" | tail -n 6 | sed 's/^/  log: /' | tee -a "$RESULTS"
grep -hi "delta" "$HOME/Library/Logs/"velopack*.log 2>/dev/null | tail -n 3 | sed 's/^/  velopack: /' | tee -a "$RESULTS"

# --- 6. the uninstall a Mac has: quit, and the app to the Trash
quit_app
launchctl bootout "gui/$(id -u)/com.study-stash.background" 2>/dev/null || true
rm -rf "$APP"
[ ! -e "$APP" ]; check $? "uninstalled: Syft Notes.app is gone"
shasum -a 256 -c "$RES/markers.sha256" > /dev/null; check $? "settings and lectures are still there, untouched"
say "left after the Trash, as with any Mac app: $(ls "$AGENTS" | grep -c study-stash) LaunchAgent(s); Velopack's download cache $([ -d "$HOME/Library/Caches/velopack/SyftNotes" ] && du -sh "$HOME/Library/Caches/velopack/SyftNotes" | cut -f1 || echo none)"
for kv in STUDYSTASH_RELEASES_URL STUDYSTASH_VELOPACK_FEED STUDYSTASH_UPDATE_AFTER; do launchctl unsetenv "$kv"; done
exit $failed
