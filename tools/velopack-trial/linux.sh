#!/bin/sh
# The Velopack trial on Linux (.github/workflows/velopack-trial.yml, docs/LIBRARIES.md phase 5), on a throwaway runner:
# packs <version>-trial.1 and -trial.2 for linux-x64 as AppImages (from linux/build.sh's publish), then installs
# version 1 (an AppImage in ~/Applications), updates it to version 2 through the app's own Velopack update, and
# uninstalls it, checking that ~/.study-stash and Documents are untouched. Results go to out/ (sizes.txt, results.txt).
#   sh tools/velopack-trial/linux.sh
set -eu
ROOT=$(pwd)
OUT="$ROOT/out"
PROPS="$ROOT/engine/Directory.Build.props"
WAIT="pwsh -NoProfile -File $ROOT/tools/velopack-trial/wait-for.ps1"
BASE=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$PROPS")
V1="$BASE-trial.1"
V2="$BASE-trial.2"
mkdir -p "$OUT/v1"
RESULTS="$OUT/results.txt"
say() { echo "$*" | tee -a "$RESULTS"; }
size() { stat -c%s "$1"; }

for n in 1 2; do
  v="$BASE-trial.$n"
  sed -i "s:<StudyStashVersion>.*</StudyStashVersion>:<StudyStashVersion>$v</StudyStashVersion>:" "$PROPS"
  STUDYSTASH_LINUX_RIDS=linux-x64 sh linux/build.sh "$ROOT/dist/linux-v$n"
  echo "linux today-tarball v$n $(size "$ROOT/dist/linux-v$n/Syft-Notes-linux-x64.tar.gz")" >> "$OUT/sizes.txt"
  t0=$(date +%s)
  vpk pack --packId SyftNotes --packVersion "$v" --packDir "$ROOT/dist/linux-v$n/linux-x64/syft-notes" \
    --mainExe StudyStash --runtime linux-x64 --channel linux-x64 --packTitle "Syft Notes" --packAuthors "Syft Notes" \
    --icon "$ROOT/assets/icon.png" --outputDir "$OUT/releases/linux-x64"
  echo "linux pack-seconds linux-x64 v$n $(( $(date +%s) - t0 ))" >> "$OUT/sizes.txt"
  [ "$n" != 1 ] || cp "$OUT/releases/linux-x64/"*.AppImage "$OUT/v1/"
done
for f in "$OUT/releases/linux-x64"/*; do echo "linux file linux-x64 $(basename "$f") $(size "$f")" >> "$OUT/sizes.txt"; done

# Settings and lectures that must outlive install, update and uninstall.
mkdir -p "$HOME/.study-stash" "$HOME/Documents/Study Stash"
head -c 64 /dev/urandom | base64 > "$HOME/.study-stash/velopack-trial.txt"
head -c 64 /dev/urandom | base64 > "$HOME/Documents/Study Stash/velopack-trial.txt"
sha256sum "$HOME/.study-stash/velopack-trial.txt" "$HOME/Documents/Study Stash/velopack-trial.txt" > "$OUT/markers.sha256"

# Install: an AppImage is the program itself, so installing is putting it somewhere and making it executable.
APP="$HOME/Applications/SyftNotes.AppImage"
mkdir -p "$HOME/Applications"
cp "$OUT/v1/"*.AppImage "$APP"
chmod +x "$APP"
if printed=$("$APP" version 2>&1 | tail -n 1) && [ "$printed" = "$V1" ]; then say "linux: v1 runs with this runner's FUSE as it is: $printed"; else
  say "linux: v1 doesn't run as the runner comes: $(echo "$printed" | tail -n 2 | tr '\n' ' ')"
  sudo apt-get install -y -qq libfuse2t64 > /dev/null
  printed=$("$APP" version | tail -n 1)
  say "linux: with libfuse2t64 installed it runs: $printed"
fi
[ "$printed" = "$V1" ] || { say "linux: FAILED - installed version is '$printed', not $V1"; exit 1; }
say "linux: install path $APP ($(size "$APP") bytes)"
say "linux: menu entries Velopack wrote: $(ls "$HOME/.local/share/applications" 2>/dev/null | grep -ci syft || true)"

# Update: the app's own Velopack update, from the folder of releases.
REPORT="$OUT/update-report.txt"
t0=$(date +%s)
"$APP" velopack-trial-update "$OUT/releases/linux-x64" "$REPORT" || say "linux: the update command exited $?"
$WAIT -Path "$REPORT" -Pattern " relaunched " -Seconds 300 || { cat "$REPORT"; say "linux: FAILED - no relaunch"; exit 1; }
say "linux: update to v2 and relaunch took $(( $(date +%s) - t0 )) s"
cat "$REPORT" | tee -a "$RESULTS"
grep -q " relaunched version=$V2 " "$REPORT" || { say "linux: FAILED - the relaunched copy isn't $V2"; exit 1; }
printed=$("$APP" version | tail -n 1)
[ "$printed" = "$V2" ] || { say "linux: FAILED - after the update it says '$printed'"; exit 1; }
say "linux: after the update the same path runs $printed"
grep -hi "delta" /tmp/velopack*.log 2>/dev/null | tail -n 5 | sed 's/^/linux log: /' | tee -a "$RESULTS" || true

# Uninstall: remove the AppImage (Velopack has no uninstaller on Linux) and its package cache.
rm -f "$APP"
rm -rf /var/tmp/velopack/SyftNotes "$HOME/.cache/velopack/SyftNotes" 2>/dev/null || true
sha256sum -c "$OUT/markers.sha256" > /dev/null && say "linux: uninstall clean; ~/.study-stash and Documents untouched" \
  || { say "linux: FAILED - settings or lectures changed"; exit 1; }
mkdir -p "$OUT/logs"
cp /tmp/velopack*.log "$OUT/logs/" 2>/dev/null || true
