#!/bin/sh
# Proves a built "Study Stash.app" actually works (D5): its windows open, the pretend microphone records, and
# Whisper (Metal on Apple silicon, from inside the bundle) hears the speech. Also what CI runs on Windows, against
# StudyStash.exe directly, to the same contract.
#   sh macos/selftest.sh "<path>/Study Stash.app" [out-dir]
# STUDYSTASH_WHISPER_MODEL must already be set, to a small model file (the tiny one, so this stays fast and needs no
# download). STUDYSTASH_ARCH=x86_64 runs the bundle's Intel tree through Rosetta instead of natively.
# STUDYSTASH_SELFTEST_FLOW=one-computer proves a first run as a student has it: the one-button welcome's Get started
# makes the app's own library, and the lecture is recorded, written down and given its notes all on this computer.
# Without it, the welcome gives way to setup by hand for a laptop with a library elsewhere (the frozen two-computer setup).
# STUDYSTASH_SELFTEST_BACKGROUND=1 (with one-computer; CI's throwaway Macs only, as it registers a real LaunchAgent) ends
# the run by linking a stand-in phone and turning on "Answer your phone when Syft Notes is closed"; once the app has
# quit, the program runs once more (STUDYSTASH_SELFTEST_AFTER_QUIT=1) to check the library still answers the phone,
# then takes the LaunchAgent away and checks nothing is left (background.txt).
set -eu

APP=${1:?"usage: sh macos/selftest.sh \"<path>/Study Stash.app\" [out-dir]"}
HERE=$(cd "$(dirname "$0")" && pwd)
OUT=${2:-"${TMPDIR:-/tmp}/study-stash-selftest-$(date +%s)"}
: "${STUDYSTASH_WHISPER_MODEL:?"selftest.sh: set STUDYSTASH_WHISPER_MODEL to a Whisper model file (the tiny one, for speed)"}"

SPEECH="$HERE/../engine/tests/StudyStash.Core.Tests/Fixtures/speech.wav"
[ -f "$SPEECH" ] || { echo "selftest.sh: can't find speech.wav at $SPEECH" >&2; exit 1; }
BIN="$APP/Contents/MacOS/StudyStash"
[ -x "$BIN" ] || { echo "selftest.sh: no program at $BIN" >&2; exit 1; }

rm -rf "$OUT"
HOME_DIR="$OUT/home"
mkdir -p "$HOME_DIR"
rm -f "$OUT/selftest.txt"

# A clean environment: no DOTNET_ROOT, so the run proves the self-contained bundle needs no .NET install on the Mac.
run_app() {
  env -u DOTNET_ROOT \
    STUDYSTASH_SELFTEST="$OUT" \
    STUDYSTASH_MIC_FILE="$SPEECH" \
    STUDYSTASH_MIC_SPEED="${STUDYSTASH_MIC_SPEED:-4}" \
    STUDYSTASH_SELFTEST_FLOW="${STUDYSTASH_SELFTEST_FLOW:-}" \
    STUDYSTASH_WHISPER_MODEL="$STUDYSTASH_WHISPER_MODEL" \
    STUDYSTASH_MODEL_FILE="$STUDYSTASH_WHISPER_MODEL" \
    "$@" --home "$HOME_DIR"
}

# perl's alarm is the time limit: it survives the exec into the app (and through arch), so the clock is the kernel's
# and nothing here sleeps or polls. SIGALRM ends the app, which is exit status 142 below.
limit=480
if [ "${STUDYSTASH_ARCH:-}" = "x86_64" ]; then
  run_app perl -e 'alarm shift; exec @ARGV' "$limit" arch -x86_64 "$BIN" &
else
  run_app perl -e 'alarm shift; exec @ARGV' "$limit" "$BIN" &
fi
pid=$!

code=0
wait "$pid" || code=$?
if [ "$code" = 142 ]; then
  echo "selftest.sh: Syft Notes didn't exit within $limit s" >&2
  exit 1
fi

# The app has quit: the background library, if this run made one, must still answer the phone, and then go cleanly.
# It runs even when the app's own run failed, so a LaunchAgent it registered is always taken away again.
bcode=0
if [ "${STUDYSTASH_SELFTEST_BACKGROUND:-}" = 1 ]; then
  env -u DOTNET_ROOT STUDYSTASH_SELFTEST="$OUT" STUDYSTASH_SELFTEST_AFTER_QUIT=1 \
    perl -e 'alarm shift; exec @ARGV' 300 "$BIN" --home "$HOME_DIR" || bcode=$?
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
  exit 1
fi
