#!/bin/sh
# The Velopack trial's Mac install, update and uninstall (.github/workflows/velopack-trial.yml), on a throwaway runner
# only: it runs installers, opens the app and changes /Applications. Reads what mac-pack.sh made, in out/.
#   sh tools/velopack-trial/mac-install.sh arm64|x64
# Scenarios, each ending with the app removed:
#   A  the universal .pkg, installed for this user only (installer -target CurrentUserHomeDirectory, no admin), then
#      updated to version 2 by the app's own Velopack update;
#   B  (arm64 only) the universal .pkg installed for all users (sudo installer -target /), then the same update, to
#      see whether a bundle the installer owns as root can be updated without an admin password;
#   C  the universal bundle copied into /Applications by this user (what dragging it out of a DMG does), first checked
#      the way today's updater (Updater.ApplyMacAsync) checks a new bundle, then updated;
#   D  this processor's own package (osx-<arch>) copied into /Applications, then updated.
set -u
ARCH=${1:?"usage: mac-install.sh arm64|x64"}
ROOT=$(pwd)
OUT="$ROOT/out"
RES="$ROOT/results"
mkdir -p "$RES/logs"
RESULTS="$RES/mac-$ARCH.txt"
WAIT="pwsh -NoProfile -File $ROOT/tools/velopack-trial/wait-for.ps1"
BASE=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$ROOT/engine/Directory.Build.props")
V1="$BASE-trial.1"
V2="$BASE-trial.2"
failed=0
say() { echo "$*" | tee -a "$RESULTS"; }
fail() { say "FAILED: $*"; failed=1; }

# Settings and lectures that must outlive everything below.
mkdir -p "$HOME/.study-stash" "$HOME/Documents/Study Stash"
head -c 64 /dev/urandom | base64 > "$HOME/.study-stash/velopack-trial.txt"
head -c 64 /dev/urandom | base64 > "$HOME/Documents/Study Stash/velopack-trial.txt"
shasum -a 256 "$HOME/.study-stash/velopack-trial.txt" "$HOME/Documents/Study Stash/velopack-trial.txt" > "$RES/markers.sha256"

# The app a .pkg opens when it finishes (its postinstall script), closed again: nothing else of it runs here.
close_app() {
  pkill -x StudyStash 2>/dev/null || true
  pwsh -NoProfile -Command 'Get-Process -Name StudyStash -ErrorAction SilentlyContinue | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue' || true
}
verify() { # what codesign says of a bundle, in a word
  if codesign --verify --deep --strict "$1" > "$RES/codesign.out" 2>&1; then echo "valid"; else echo "INVALID ($(tail -n 1 "$RES/codesign.out"))"; fi
}
# update APP FEED LABEL EXPECTED: the app's own Velopack update, then the version it reports
update() {
  app=$1; feed=$2; label=$3; expect=$4
  report="$RES/$label-report.txt"
  rm -f "$report"
  t0=$(date +%s)
  "$app/Contents/MacOS/StudyStash" velopack-trial-update "$feed" "$report"
  say "$label: update command exited $?"
  if $WAIT -Path "$report" -Pattern " relaunched " -Seconds 240; then
    say "$label: update and relaunch took $(( $(date +%s) - t0 )) s"
  else
    say "$label: no relaunch within 240 s"
  fi
  sed "s/^/$label report: /" "$report" | tee -a "$RESULTS"
  close_app
  now=$("$app/Contents/MacOS/StudyStash" version 2>&1)
  say "$label: after the update $app runs $now (owner $(stat -f%Su "$app"); codesign $(verify "$app"))"
  [ "$now" = "$expect" ] || fail "$label: expected $expect after the update, got $now"
  ls "$app/Contents/MacOS" | tr '\n' ' ' | sed "s/^/$label: Contents\/MacOS: /" | tee -a "$RESULTS"; echo >> "$RESULTS"
}

# --- A: the universal .pkg, for this user only
PKG=$(ls "$OUT/v1/osx-universal/"*-Setup.pkg)
say "A: $(installer -pkginfo -pkg "$PKG" 2>&1 | head -n 1); domains: $(installer -dominfo -pkg "$PKG" 2>&1 | tr '\n' ' ')"
if installer -pkg "$PKG" -target CurrentUserHomeDirectory > "$RES/A-installer.txt" 2>&1; then
  close_app
  APP=$(ls -d "$HOME/Applications/"*.app /Applications/"Study Stash.app" 2>/dev/null | head -n 1)
  say "A: installed at $APP (owner $(stat -f%Su "$APP"); codesign $(verify "$APP")); version $("$APP/Contents/MacOS/StudyStash" version)"
  update "$APP" "$OUT/releases/osx-universal" A "$V2"
  rm -rf "$APP"
else
  fail "A: the user-only install failed: $(tail -n 3 "$RES/A-installer.txt" | tr '\n' ' ')"
fi
rm -rf "$HOME/Library/Caches/velopack/SyftNotes"

# --- B: the universal .pkg, for all users
if [ "$ARCH" = arm64 ]; then
  if sudo installer -pkg "$PKG" -target / > "$RES/B-installer.txt" 2>&1; then
    close_app
    APP="/Applications/Study Stash.app"
    say "B: installed at $APP (owner $(stat -f%Su:%Sg "$APP"); MacOS folder owner $(stat -f%Su "$APP/Contents/MacOS"))"
    # Expected to be refused: the bundle is root's, and a silent update may not ask for a password.
    update "$APP" "$OUT/releases/osx-universal" B "$V2"
    grep -hi "permission\|elevat" "$HOME/Library/Logs/"velopack*.log 2>/dev/null | tail -n 4 | sed 's/^/B log: /' | tee -a "$RESULTS"
    sudo rm -rf "$APP"
  else
    fail "B: the all-users install failed: $(tail -n 3 "$RES/B-installer.txt" | tr '\n' ' ')"
  fi
  rm -rf "$HOME/Library/Caches/velopack/SyftNotes"
fi

# --- C: the universal bundle, dragged into /Applications (and checked as today's updater checks a DMG's bundle)
APP="/Applications/Study Stash.app"
ditto -x -k "$(ls "$OUT/v1/osx-universal/"*-Portable.zip)" /Applications/
plist=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP/Contents/Info.plist")
say "C: today's updater would see: Info.plist $plist; codesign $(verify "$APP"); version $("$APP/Contents/MacOS/StudyStash" version); under Rosetta $(arch -x86_64 "$APP/Contents/MacOS/StudyStash" version 2>&1 | tail -n 1)"
update "$APP" "$OUT/releases/osx-universal" C "$V2"
rm -rf "$APP" "$HOME/Library/Caches/velopack/SyftNotes"

# --- D: this processor's own package
APP="/Applications/Syft Notes.app"
ditto -x -k "$(ls "$OUT/v1/osx-$ARCH/"*-Portable.zip)" /Applications/
say "D: osx-$ARCH bundle (vpk's own --deep signing): codesign $(verify "$APP"); version $("$APP/Contents/MacOS/StudyStash" version 2>&1 | tail -n 1)"
update "$APP" "$OUT/releases/osx-$ARCH" D "$V2"
rm -rf "$APP" "$HOME/Library/Caches/velopack/SyftNotes"

# Nothing of the student's was touched.
if shasum -a 256 -c "$RES/markers.sha256" > /dev/null; then say "uninstall clean: ~/.study-stash and Documents untouched"; else fail "settings or lectures changed"; fi
cp "$HOME/Library/Logs/"velopack*.log /tmp/velopack*.log "$RES/logs/" 2>/dev/null || true
exit $failed
