#!/bin/sh
# The Velopack bridge test on Linux (.github/workflows/velopack-bridge.yml), on a throwaway runner only (a virtual
# display, Xvfb, runs the app). Every step a student's computer goes through, in order:
#   1. the released Syft Notes before the bridge (OLD_TARBALL, the published 0.22.1 one, put in place by its own
#      install.sh), set up as a student's is: start at login, the background library, Claude Code and ChatGPT (Codex)
#      connected, a library with a class, settings and lectures;
#   2. the update to the bridge release as that copy's own updater does it (Updater.cs, ApplyLinuxAsync: the tarball
#      unpacked beside it, checked, swapped in, and the app opened again);
#   3. the bridge copy, on its own once idle, moving over to the AppImage (Bridge.cs): ~/Applications/SyftNotes.AppImage,
#      the applications menu and `syft-notes` pointed at it, the old folder gone;
#   4. checked: start at login, the background library and the AI connections (each started as its app would, and
#      asked for the library's classes) name the AppImage; settings and lectures untouched;
#   5. a later update, Velopack to Velopack (V2_FEED, with a delta), by the app's own updater;
#   6. `syft-notes --uninstall`: nothing of the app left, settings and lectures still there.
#   OLD_TARBALL=... BRIDGE_TARBALL=... V1_FEED=... V2_FEED=... V1=... V2=... RID=linux-x64 sh tools/velopack/bridge-linux.sh
set -u
ROOT=$(pwd)
RES="$ROOT/results"
mkdir -p "$RES"
RESULTS="$RES/linux.txt"
WAIT="pwsh -NoProfile -File $ROOT/tools/velopack/wait-for.ps1"
PORT=8765
failed=0
say() { echo "$*" | tee -a "$RESULTS"; }
check() { if [ "$1" = 0 ]; then say "ok: $2"; else say "FAILED: $2"; failed=1; fi; }
waitfor() { $WAIT -Path "$1" -Pattern "$2" -Seconds "$3"; check $? "$4"; }

DATA="$HOME/.local/share"
OLD="$DATA/syft-notes"
OLD_PROGRAM="$OLD/StudyStash"
IMAGE="$HOME/Applications/SyftNotes.AppImage"
H="$HOME/.study-stash"
LOGS="$H/logs"
AUTOSTART="$HOME/.config/autostart"
CLAUDE="$HOME/.claude.json"
CODEX="$HOME/.codex/config.toml"
DOCS="$HOME/Documents/Study Stash"

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
trap 'kill $SERVER 2>/dev/null; cp "$LOGS"/*.log "${TMPDIR:-/tmp}"/velopack*.log "$RES/" 2>/dev/null' EXIT
export STUDYSTASH_RELEASES_URL="http://127.0.0.1:$PORT/release.json" STUDYSTASH_VELOPACK_FEED="http://127.0.0.1:$PORT/" STUDYSTASH_UPDATE_AFTER=5
quit_app() {
  pkill -f "StudyStash" 2>/dev/null || true
  timeout 60 pidwait -f "[S]tudyStash" 2>/dev/null || true
}

# --- 1. the release before the bridge, as a student has it
mkdir -p "$RES/old"
tar -xzf "$OLD_TARBALL" -C "$RES/old"
STUDYSTASH_NO_OPEN=1 sh "$RES/old/syft-notes/install.sh" > /dev/null
say "before: $("$OLD_PROGRAM" version) at $OLD"
"$OLD_PROGRAM" init > /dev/null
mkdir -p "$DOCS" "$AUTOSTART" "$(dirname "$CODEX")"
head -c 64 /dev/urandom | base64 > "$H/bridge-test.txt"
head -c 64 /dev/urandom | base64 > "$DOCS/bridge-test.txt"
sha256sum "$H/bridge-test.txt" "$DOCS/bridge-test.txt" > "$RES/markers.sha256"
# What the app itself writes for these (Desktop.StartAtLogin, BackgroundLibrary.Register, ClaudeSetup.Connect).
printf '[Desktop Entry]\nType=Application\nName=Syft Notes\nExec="%s" --background\nNoDisplay=true\n' "$OLD_PROGRAM" > "$AUTOSTART/com.study-stash.app.desktop"
printf '[Desktop Entry]\nType=Application\nName=Syft Notes background\nExec="%s" --home "%s" helper --keep-alive\nNoDisplay=true\n' "$OLD_PROGRAM" "$H" > "$AUTOSTART/syft-notes-background.desktop"
printf '{"mcpServers":{"syft-notes":{"type":"stdio","command":"%s","args":["mcp","--client","claude-code"]}}}\n' "$OLD_PROGRAM" > "$CLAUDE"
printf '[mcp_servers.syft-notes]\ncommand = "%s"\nargs = ["mcp", "--client", "codex"]\n' "$OLD_PROGRAM" > "$CODEX"

# --- 2. the old copy's own update to the bridge release (Updater.cs, ApplyLinuxAsync, step for step)
fresh="$DATA/.syft-notes.new"
rm -rf "$fresh" && mkdir -p "$fresh"
tar -xzf "$BRIDGE_TARBALL" -C "$fresh" --strip-components=1
ini=$(sed -n 's/^version *= *//p' "$fresh/study-stash.ini")
runs=$("$fresh/StudyStash" version)
[ "$ini" = "$V1" ] && [ "$runs" = "$V1" ]; check $? "the old updater would take the bridge tarball (study-stash.ini says $ini, it runs $runs)"
mv "$OLD" "$OLD.old" && mv "$fresh" "$OLD" && rm -rf "$OLD.old"
nohup "$OLD_PROGRAM" --background > "$RES/app-1.out" 2>&1 &

# --- 3. the bridge copy moves itself over to the AppImage once idle
waitfor "$DATA/applications/syft-notes.desktop" "SyftNotes.AppImage" 600 "the bridge copy moved itself over to the AppImage and into the menu"
waitfor "$CODEX" "SyftNotes.AppImage" 240 "the AppImage, on its first start, put the AI connections right"
grep -h "\[update\]\|\[moved\]\|\[menu\]" "$LOGS/app.log" | sed 's/^/  log: /' | tee -a "$RESULTS"
say "FUSE: the AppImage $(grep -q 'needs FUSE' "$LOGS/app.log" && echo "said it needs FUSE" || echo "ran as the runner comes")"

# --- 4. where everything stands
[ ! -e "$OLD" ]; check $? "the tarball's folder is gone"
[ "$("$IMAGE" version | tail -n 1)" = "$V1" ]; check $? "$IMAGE runs $V1"
[ "$(readlink "$HOME/.local/bin/syft-notes")" = "$IMAGE" ]; check $? "syft-notes on the PATH is the AppImage"
grep -qF "$IMAGE" "$AUTOSTART/com.study-stash.app.desktop"; check $? "start at login opens the AppImage"
# The background library: registered again from the AppImage, or taken away by the app itself (never turned on in
# this test's settings); never left naming the old program.
! grep -qsF "$OLD_PROGRAM" "$AUTOSTART/syft-notes-background.desktop" "$HOME/.config/systemd/user/"syft-notes-background*.service; check $? "the background library's registration doesn't name the tarball's program"
grep -qF "$IMAGE" "$CLAUDE"; check $? "Claude Code's connection starts the AppImage"
sha256sum -c "$RES/markers.sha256" > /dev/null; check $? "settings (~/.study-stash) and lectures (Documents/Study Stash) are untouched"
python3 -I "$ROOT/tools/velopack/wait-port.py" 8787 30 || { "$IMAGE" serve > "$RES/serve.log" 2>&1 & python3 -I "$ROOT/tools/velopack/wait-port.py" 8787 120; }
check $? "the library runs from the AppImage"
python3 -I "$ROOT/tools/velopack/mcp-check.py" "$CLAUDE" "Example 101" 60; check $? "Claude Code's connection reaches the library and lists its class"
python3 -I "$ROOT/tools/velopack/mcp-check.py" "$CODEX" "Example 101" 60; check $? "ChatGPT's (Codex's) connection reaches the library and lists its class"

# --- 5. a later release, Velopack to Velopack, installed by the app's own updater once idle
publish "$V2_FEED" "$V2"
quit_app
nohup "$IMAGE" --background > "$RES/app-2.out" 2>&1 &
waitfor "$LOGS/app.log" "Syft Notes $V2 starting" 600 "the AppImage updated itself to $V2 through Velopack and opened again"
[ "$("$IMAGE" version | tail -n 1)" = "$V2" ]; check $? "$IMAGE is $V2, in the same place"
grep -h "\[update\]" "$LOGS/app.log" | tail -n 6 | sed 's/^/  log: /' | tee -a "$RESULTS"

# --- 6. the uninstall
quit_app
"$IMAGE" --uninstall | tee -a "$RESULTS"
for f in "$IMAGE" "$HOME/.local/bin/syft-notes" "$DATA/applications/syft-notes.desktop" "$DATA/icons/hicolor/256x256/apps/syft-notes.png" \
         "$AUTOSTART/com.study-stash.app.desktop" "$AUTOSTART/syft-notes-background.desktop"; do
  [ ! -e "$f" ]; check $? "uninstalled: $f is gone"
done
sha256sum -c "$RES/markers.sha256" > /dev/null; check $? "settings and lectures are still there, untouched"
exit $failed
