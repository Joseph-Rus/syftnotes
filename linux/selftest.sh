#!/bin/sh
# Proves a built Syft Notes for Linux actually works (D5), to the same contract as macos/selftest.sh: its windows open,
# the pretend microphone records, and Whisper (on the processor, from inside the folder) hears the speech.
#   sh linux/selftest.sh <syft-notes folder> [out-dir]
# STUDYSTASH_WHISPER_MODEL must already be set, to a small model file (the tiny one, so this stays fast and needs no
# download). With no DISPLAY it runs under xvfb-run, so it needs Xvfb there (CI's ubuntu runner, apt install xvfb).
# STUDYSTASH_SELFTEST_FLOW=one-computer proves a first run as a student has it: Get started makes the app's own library,
# and the lecture is recorded, written down and given its notes all on this computer.
# STUDYSTASH_SELFTEST_BACKGROUND=1 (with one-computer; CI's throwaway computers only, as it registers a real systemd
# --user unit or autostart entry) ends the run by linking a stand-in phone and turning on "Answer your phone when Syft
# Notes is closed"; once the app has quit, the program runs once more (STUDYSTASH_SELFTEST_AFTER_QUIT=1) to check the
# library still answers the phone, then takes the registration away and checks nothing is left (background.txt).
set -eu

APP=${1:?"usage: sh linux/selftest.sh <syft-notes folder> [out-dir]"}
HERE=$(cd "$(dirname "$0")" && pwd)
OUT=${2:-"${TMPDIR:-/tmp}/syft-notes-selftest-$(date +%s)"}
: "${STUDYSTASH_WHISPER_MODEL:?"selftest.sh: set STUDYSTASH_WHISPER_MODEL to a Whisper model file (the tiny one, for speed)"}"

SPEECH="$HERE/../engine/tests/StudyStash.Core.Tests/Fixtures/speech.wav"
[ -f "$SPEECH" ] || { echo "selftest.sh: can't find speech.wav at $SPEECH" >&2; exit 1; }
BIN="$APP/StudyStash"
[ -x "$BIN" ] || { echo "selftest.sh: no program at $BIN" >&2; exit 1; }

rm -rf "$OUT"
HOME_DIR="$OUT/home"
mkdir -p "$HOME_DIR"

# No display of its own (a CI runner, a container): a virtual one for the length of the run.
X=""
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
  command -v xvfb-run >/dev/null || { echo "selftest.sh: no display, and no xvfb-run to make one" >&2; exit 1; }
  X="xvfb-run -a -s -screen\ 0\ 1440x900x24"
fi

# A clean environment: no DOTNET_ROOT, so the run proves the self-contained folder needs no .NET install. timeout is the
# time limit: it ends the app with SIGTERM (exit status 124) if it hasn't finished.
limit=480
code=0
env -u DOTNET_ROOT \
  STUDYSTASH_SELFTEST="$OUT" \
  STUDYSTASH_MIC_FILE="$SPEECH" \
  STUDYSTASH_MIC_SPEED="${STUDYSTASH_MIC_SPEED:-4}" \
  STUDYSTASH_SELFTEST_FLOW="${STUDYSTASH_SELFTEST_FLOW:-}" \
  STUDYSTASH_WHISPER_MODEL="$STUDYSTASH_WHISPER_MODEL" \
  STUDYSTASH_MODEL_FILE="$STUDYSTASH_WHISPER_MODEL" \
  sh -c "$X timeout -k 20 $limit \"\$0\" --home \"\$1\"" "$BIN" "$HOME_DIR" || code=$?
if [ "$code" = 124 ]; then
  echo "selftest.sh: Syft Notes didn't exit within $limit s" >&2
  exit 1
fi

# The app has quit: the background library, if this run made one, must still answer the phone, and then go cleanly.
# It runs even when the app's own run failed, so a registration it made is always taken away again. No window.
bcode=0
if [ "${STUDYSTASH_SELFTEST_BACKGROUND:-}" = 1 ]; then
  env -u DOTNET_ROOT STUDYSTASH_SELFTEST="$OUT" STUDYSTASH_SELFTEST_AFTER_QUIT=1 \
    timeout -k 20 300 "$BIN" --home "$HOME_DIR" || bcode=$?
fi

TXT="$OUT/selftest.txt"
[ -f "$TXT" ] || { echo "selftest.sh: Syft Notes exited ($code) without writing selftest.txt" >&2; exit 1; }
cat "$TXT"

last=$(tail -n 1 "$TXT")
ok=true
[ "$code" = 0 ] || ok=false
[ "$last" = "ok" ] || ok=false
grep -qiE "heard nothing|no model|recording didn't start|FAILED" "$TXT" && ok=false
if [ "${STUDYSTASH_SELFTEST_BACKGROUND:-}" = 1 ]; then
  echo "--- background.txt"
  cat "$OUT/background.txt" 2>/dev/null || echo "(none)"
  [ "$bcode" = 0 ] && [ "$(tail -n 1 "$OUT/background.txt" 2>/dev/null)" = "ok" ] || ok=false
fi

if [ "$ok" = true ]; then
  echo "selftest.sh: passed ($OUT)"
else
  echo "selftest.sh: failed (exit=$code, last line: $last)" >&2
  [ -f "$HOME_DIR/logs/app.log" ] && { echo "--- app.log"; tail -n 60 "$HOME_DIR/logs/app.log"; } >&2
  exit 1
fi
