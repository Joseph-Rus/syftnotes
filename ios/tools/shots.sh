#!/bin/bash
# Pictures of the app's screens in a simulator, light and dark, with the demo library: never a real one, never the
# screen of the Mac it runs on (only `xcrun simctl`, headless).
#
#   ios/tools/shots.sh -u <simulator udid> -o <folder> [-a <StudyStash.app>] [-s <seconds>] [--light|--dark] [screen ...]
#
# Without -a it builds the app first (xcodegen, then xcodebuild -jobs 2 into ios/build/dd). With no screens named it
# takes them all (`-l` lists them). A screen is one of the names below, or `name=<launch arguments>` for one of your
# own, e.g. `due-week='-tab due -week'`. Each picture is <folder>/<name>-light.png and <name>-dark.png. Works the same
# on an iPad simulator. The simulator is booted for the pictures and shut down after (`-k` keeps it running).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
ios="$(dirname "$here")"
bundle=app.studystash.ios

# name|launch arguments (after -demo -quiet -ask-agreed yes; "-unpaired" screens get no -demo, and a screen that says
# -ask-agreed itself keeps its own)
screens=(
  "home|-tab home"
  "class|-open 'class:Calculus II'"
  "lecture|-open lecture:demo-calc-ibp"
  "lecture-transcript|-open lecture:demo-calc-ibp -pane transcript"
  "lecture-files|-open lecture:demo-calc-ibp -pane files"
  "assignment|-open 'assignment:Calculus II:1'"
  "due|-tab due"
  "ask|-tab ask"
  "ask-empty|-tab ask -ask-new"
  "ask-typing|-tab ask -ask-new -ask-typing 'What is the difference between SN1 and SN2?'"
  "ask-waiting-drive|-tab ask -ask-new -ask 'What did we cover in Calculus II this week?' -ask-waiting -ask-drive"
  "ask-writing|-tab ask -ask-new -ask 'How do I choose u in integration by parts?' -ask-waiting -ask-stage writing"
  "ask-answer|-tab ask -ask 'How do I choose u in integration by parts?'"
  "ask-error|-tab ask -ask-new -ask 'What did we cover in Calculus II this week?' -ask-fail -ask-drive"
  "ask-chats|-tab ask -ask-new -ask-chats"
  "ask-consent|-tab ask -ask-new -ask-agreed no -ask 'How do I choose u in integration by parts?'"
  "ask-thinking-chatgpt|-tab ask -ask-new -ask 'What is due next?' -ask-waiting -ask-stage reading -ask-engine codex"
  "ask-thinking-gemini|-tab ask -ask-new -ask 'What is due next?' -ask-waiting -ask-stage reading -ask-engine gemini"
  "ask-thinking-ollama|-tab ask -ask-new -ask 'What is due next?' -ask-waiting -ask-stage reading -ask-engine ollama"
  "ask-sheet|-show ask:lecture:demo-calc-ibp"
  "search|-tab search"
  "search-results|-search integration"
  "settings|-tab settings"
  "uses|-show uses"
  "record|-show record"
  "record-sent|-show record -stop-after 3"
  "add-files|-show add-files"
  "import-audio|-show import-audio"
  "goodnotes|-show goodnotes"
  "notebook|-show notebook"
  "study|-show study:demo-calc-ibp"
  "quiz|-show quiz:demo-calc-ibp"
  "widgets|-show widgets"
  "components-1|-show components:1"
  "components-2|-show components:2"
  "components-3|-show components:3"
  "components-4|-show components:4"
  "pair|-unpaired"
  "pair-code|-unpaired -pair-code"
)

udid="" out="" app="" wait=4 modes=(light dark) keep=0 picked=()
while (($#)); do
  case "$1" in
    -u) udid="$2"; shift 2 ;;
    -o) out="$2"; shift 2 ;;
    -a) app="$2"; shift 2 ;;
    -s) wait="$2"; shift 2 ;;
    -k) keep=1; shift ;;
    --light) modes=(light); shift ;;
    --dark) modes=(dark); shift ;;
    -l) for s in "${screens[@]}"; do printf '%-20s %s\n' "${s%%|*}" "${s#*|}"; done; exit 0 ;;
    -h|--help) sed -n '2,11p' "$0"; exit 0 ;;
    *) picked+=("$1"); shift ;;
  esac
done
[[ -n "$udid" && -n "$out" ]] || { sed -n '2,11p' "$0" >&2; exit 2; }
mkdir -p "$out"

if [[ -z "$app" ]]; then
  (cd "$ios" && xcodegen generate >/dev/null && xcodebuild -project StudyStash.xcodeproj -scheme StudyStash -jobs 2 \
    -destination "platform=iOS Simulator,id=$udid" -derivedDataPath build/dd build >build/shots-build.log 2>&1) \
    || { echo "build failed: see ios/build/shots-build.log" >&2; exit 1; }
  app="$ios/build/dd/Build/Products/Debug-iphonesimulator/StudyStash.app"
fi

# The launch arguments of a screen by name, or of name=args.
args_of() {
  local want="$1"
  if [[ "$want" == *=* ]]; then echo "${want#*=}"; return; fi
  for s in "${screens[@]}"; do [[ "${s%%|*}" == "$want" ]] && { echo "${s#*|}"; return; }; done
  echo "no screen called $want (-l lists them)" >&2; return 1
}

[[ ${#picked[@]} -gt 0 ]] || for s in "${screens[@]}"; do picked+=("${s%%|*}"); done

xcrun simctl boot "$udid" 2>/dev/null || true
xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl install "$udid" "$app"
xcrun simctl privacy "$udid" grant microphone "$bundle" 2>/dev/null || true
xcrun simctl status_bar "$udid" override --time 9:41 --dataNetwork wifi --wifiBars 3 --cellularBars 4 \
  --batteryState charged --batteryLevel 100 2>/dev/null || true

for mode in "${modes[@]}"; do
  xcrun simctl ui "$udid" appearance "$mode"
  for want in "${picked[@]}"; do
    name="${want%%=*}"
    line="$(args_of "$want")"
    eval "extra=($line)"
    base=(-demo -quiet -ask-agreed yes)
    [[ " ${extra[*]} " == *" -ask-agreed "* ]] && base=(-demo -quiet)
    [[ " ${extra[*]} " == *" -unpaired "* ]] && base=(-quiet)
    xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
    xcrun simctl launch "$udid" "$bundle" "${base[@]}" "${extra[@]}" >/dev/null
    pause="$wait"
    [[ "$line" == *record* || "$line" == *study* || "$line" == *quiz* || "$line" == *-ask* ]] && pause=$((wait + 3))
    sleep "$pause"
    xcrun simctl io "$udid" screenshot --type=png "$out/$name-$mode.png" >/dev/null 2>&1
    echo "$out/$name-$mode.png"
  done
done

xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl status_bar "$udid" clear 2>/dev/null || true
((keep)) || xcrun simctl shutdown "$udid" 2>/dev/null || true
