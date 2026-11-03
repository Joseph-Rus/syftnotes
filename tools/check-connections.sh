#!/bin/bash
# The desktop app's connection rules (docs/ARCHITECTURE.md, "Connection rules"), counted. Each count is a pattern that
# kept sending the app back for fixes: a fixed wait, a screen keeping its own copy of the AI's state, a window built by
# hand, an error swallowed. CI fails when a count goes UP from tools/connections-baseline.txt, so new code uses the
# toolkit instead. When one goes down, it says so: lower that line in the baseline in the same change, so it can't creep
# back up.
#   tools/check-connections.sh            check
#   tools/check-connections.sh --print    print today's counts in the baseline's format
set -euo pipefail
cd "$(dirname "$0")/../engine/src/StudyStash.App"
BASELINE="../../../tools/connections-baseline.txt"

count() { grep -rEo "$@" 2>/dev/null | wc -l | tr -d ' '; }

# name|how it's counted. Paths are under engine/src/StudyStash.App; SelfTest*.cs drives the app from outside, so it's
# left out of the rules for the app's own code.
measure() {
  # Rule 11: waits go through Poll with a real condition and a timeout, never a fixed sleep.
  echo "sleeps $(grep -rEo 'Task\.Delay\(|Thread\.Sleep\(' --include='*.cs' . | grep -v -e '/SelfTest' -e '/Kit/Poll.cs' | wc -l | tr -d ' ')"
  # Rule 2: the AI's engine states are read in one place (the AI status store), not in every screen.
  echo "engine-state-strings $(grep -rEo '"(not_signed_in|not_installed|needs_login)"' --include='*.cs' . | grep -v -e '/Stores/' -e '/SelfTest' | wc -l | tr -d ' ')"
  # Rule 3: view models get what they need passed in; they never reach into Shell.
  echo "shell-in-viewmodels $(count '\bShell\.' --include='*.cs' ViewModels)"
  # Rule 5: services return results or raise events; they never call Shell.
  echo "shell-in-services $(count '\bShell\.' --include='*.cs' Services)"
  # Rule 7: windows open through Windows/AppWindows.
  echo "windows-by-hand $(grep -rEo 'new (Window|Floating)\b' --include='*.cs' . | grep -v -e '^\./Windows/' -e '/SelfTest' | wc -l | tr -d ' ')"
  # Rule 10: no error swallowed whole: catch what's expected, log the rest.
  echo "bare-catches $(grep -rEo 'catch\s*(\{|$)|catch \(Exception\)' --include='*.cs' . | grep -v '/SelfTest' | wc -l | tr -d ' ')"
  # Rule 10: work started without waiting for it goes through UiTasks.Run, which logs and tells the student.
  echo "fire-and-forget-in-shell $(grep -Eo '_ = [A-Za-z_.()]*Async\(' Shell*.cs | wc -l | tr -d ' ')"
  # Rule 8: Mac and Windows share one view where the layout is the same.
  echo "mac-views $(ls Views/Mac*.axaml | wc -l | tr -d ' ')"
  # Rule 3: one way to reach the library and the AI; nobody makes their own client.
  echo "clients-by-hand $(grep -rEo 'new (HttpClient|AiRemote|RemoteLibrary|CanvasClient)\(' --include='*.cs' . | grep -v 'LibraryApi' | wc -l | tr -d ' ')"
}

if [ "${1:-}" = "--print" ]; then measure; exit 0; fi

fail=0
while read -r name now; do
  was=$(awk -v n="$name" '$1 == n { print $2 }' "$BASELINE")
  if [ -z "$was" ]; then echo "::error::$name isn't in tools/connections-baseline.txt (add it: $name $now)"; fail=1
  elif [ "$now" -gt "$was" ]; then echo "::error::$name went up from $was to $now. Use the toolkit (CLAUDE.md, docs/ARCHITECTURE.md) instead of adding another."; fail=1
  elif [ "$now" -lt "$was" ]; then echo "$name: $now, down from $was. Lower it in tools/connections-baseline.txt so it stays down."
  else echo "$name: $now"
  fi
done < <(measure)
exit $fail
