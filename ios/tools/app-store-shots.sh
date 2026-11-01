#!/bin/bash
# The App Store's pictures, from a simulator in the sample classes: never a real library, never the screen of the Mac
# it runs on (only `xcrun simctl`, headless; the Simulator app is never opened).
#
#   ios/tools/app-store-shots.sh -u <simulator udid> -o <folder> [-a <StudyStash.app>] iphone|ipad
#
# Without -a it builds the Debug app first (into ios/build/dd). The app is put on afresh, the status bar says 9:41 with
# a full battery, and the app's own clock says 9:32 that day (tools/fakeclock.c, loaded into the app's process for the
# pictures only), so Up next's "Calculus II, in 9 minutes" is at 9:41 too. Each screen is kept once it has settled
# (three frames alike) or after its time limit, without an alpha channel (App Store Connect refuses one).
# APPSTORE.md says which simulator fills which of App Store Connect's slots.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
ios="$(dirname "$here")"
bundle=app.studystash.ios
udid="" out="" app=""
while getopts "u:o:a:" f; do
  case $f in u) udid=$OPTARG ;; o) out=$OPTARG ;; a) app=$OPTARG ;; *) exit 2 ;; esac
done
shift $((OPTIND - 1))
kind=${1:-}
[[ -n "$udid" && -n "$out" && ( "$kind" == iphone || "$kind" == ipad ) ]] || { sed -n '2,11p' "$0" >&2; exit 2; }

mkdir -p "$ios/build" "$out"
if [[ -z "$app" ]]; then
  (cd "$ios" && xcodegen generate >/dev/null && xcodebuild -project StudyStash.xcodeproj -scheme StudyStash -jobs 2 \
    -destination "platform=iOS Simulator,id=$udid" -derivedDataPath build/dd build >build/app-store-build.log 2>&1) \
    || { echo "build failed: see ios/build/app-store-build.log" >&2; exit 1; }
  app="$ios/build/dd/Build/Products/Debug-iphonesimulator/StudyStash.app"
fi
clock="$ios/build/fakeclock.dylib"
xcrun -sdk iphonesimulator clang -arch arm64 -mios-simulator-version-min=26.0 -dynamiclib -o "$clock" "$here/fakeclock.c"

xcrun simctl boot "$udid" 2>/dev/null || true
xcrun simctl bootstatus "$udid" -b >/dev/null
# A new simulator's keyboard first shows its slide-to-type introduction over the recorder: as if it had been seen.
xcrun simctl spawn "$udid" defaults write com.apple.Preferences DidShowContinuousPathIntroduction -bool true
# Nothing left on the phone from before (a notebook, a recording).
xcrun simctl uninstall "$udid" "$bundle" 2>/dev/null || true
xcrun simctl install "$udid" "$app"
xcrun simctl privacy "$udid" grant microphone "$bundle"
xcrun simctl ui "$udid" appearance light
xcrun simctl status_bar "$udid" override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 \
  --cellularMode active --cellularBars 4 --batteryState discharging --batteryLevel 100

# One picture: <name> <time limit> <launch arguments...>.
shoot() {
  local name=$1 limit=$2; shift 2
  local tmp; tmp=$(mktemp -d)
  xcrun simctl terminate "$udid" "$bundle" >/dev/null 2>&1 || true
  SIMCTL_CHILD_DYLD_INSERT_LIBRARIES="$clock" \
  SIMCTL_CHILD_FAKE_CLOCK_OFFSET=$(( $(date -j -f '%Y-%m-%d %H:%M:%S' "$(date +%Y-%m-%d) 09:32:05" +%s) - $(date +%s) )) \
    xcrun simctl launch "$udid" "$bundle" "$@" >/dev/null
  local start=$SECONDS prev="" same=0 n=0 last=""
  while (( SECONDS < start + limit )); do
    local f="$tmp/$n.png"
    xcrun simctl io "$udid" screenshot --type=png "$f" >/dev/null 2>&1 || continue
    local h; h=$(md5 -q "$f")
    if [[ "$h" == "$prev" ]]; then same=$((same + 1)); else same=0; fi
    prev=$h last=$f n=$((n + 1))
    # The start splash holds still for a moment too: settled counts only after it has gone.
    (( SECONDS - start >= 6 && same >= 2 )) && break
  done
  ffmpeg -loglevel error -y -i "$last" -pix_fmt rgb24 "$out/$name.png"
  rm -rf "$tmp"
  echo "$out/$name.png"
}

b=(-demo -quiet -ask-agreed yes -as-paired -fresh-notebooks)
nb=(-demo -quiet -ask-agreed yes -as-paired)
q='How do I choose u in integration by parts?'
typed=$'Integration by parts, part 2\n- tabular method when u is x² or higher\n- if the integral comes back, move it left and solve\n- on the exam: ∫ eˣ sin x dx'

# Taken in this order so the recording and the notebook (both kept on the phone) come last; named in the story's.
if [[ $kind == iphone ]]; then
  shoot 01-home 20 "${b[@]}" -tab home
  shoot 03-lecture 20 "${b[@]}" -open lecture:demo-calc-ibp
  shoot 04-ask 25 "${b[@]}" -tab ask -ask "$q"
  shoot 05-flashcards 25 "${b[@]}" -show study:demo-calc-ibp
  shoot 06-due 20 "${b[@]}" -tab due
  shoot 07-search 20 "${b[@]}" -search integration
  shoot 02-record 14 "${b[@]}" -show record -record-typed "$typed"
  shoot 08-notebook 20 "${nb[@]}" -show notebook
else
  # Upright, an iPad's sidebar floats over the page and greys it: the tabs along the top instead.
  b+=(-tab-bar)
  shoot 01-home 20 "${b[@]}" -tab home
  shoot 02-lecture 20 "${b[@]}" -open lecture:demo-calc-ibp
  shoot 04-ask 25 "${b[@]}" -tab ask -ask "$q"
  shoot 05-due 20 "${b[@]}" -tab due
  shoot 06-flashcards 25 "${b[@]}" -show study:demo-calc-ibp
  shoot 03-notebook 20 "${nb[@]}" -show notebook -notebook-sidebar
fi

xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl status_bar "$udid" clear 2>/dev/null || true
xcrun simctl shutdown "$udid" 2>/dev/null || true
