#!/usr/bin/env bash
# The App tests as two processes at once. Avalonia's headless platform has one UI thread per process, so the tests in
# one process can't run in parallel; two processes can. The classes named in engine/tests/StudyStash.App.Tests/slow-lane.txt
# run in one, everything else (new classes too) in the other, so the two take about as long.
#
#   tools/run-app-tests.sh <results folder> [more dotnet test arguments]
#
# Each process writes its own .trx file there (App-slow.trx, App-rest.trx) for .github/scripts/test-summary.py, and its
# own log (printed, one after the other, when both are done). The exit code is non-zero if either failed.
set -u
if [ $# -lt 1 ]; then echo "usage: $0 <results folder> [dotnet test arguments]" >&2; exit 2; fi
results=$1
shift
cd "$(dirname "$0")/.." || exit 2
project=engine/tests/StudyStash.App.Tests
mkdir -p "$results"

slow=""
rest=""
while IFS= read -r line || [ -n "$line" ]; do
  line=${line%%#*}
  line=$(printf '%s' "$line" | tr -d '[:space:]')
  [ -z "$line" ] && continue
  slow+="${slow:+|}FullyQualifiedName~.${line}."
  rest+="${rest:+&}FullyQualifiedName!~.${line}."
done < "$project/slow-lane.txt"
if [ -z "$slow" ]; then echo "slow-lane.txt names no class" >&2; exit 2; fi

dotnet build "$project" || exit 1

run() { # <lane> <filter>
  STUDYSTASH_TEST_LANE=$1 dotnet test "$project" --no-build --filter "$2" \
    --logger "trx;LogFileName=App-$1.trx" --results-directory "$results" "${@:3}" > "$results/App-$1.log" 2>&1
}
run slow "$slow" "$@" &
slow_pid=$!
run rest "$rest" "$@" &
rest_pid=$!
wait "$slow_pid"
slow_status=$?
wait "$rest_pid"
rest_status=$?

for lane in slow rest; do
  echo "::group::App tests, the $lane lane"
  cat "$results/App-$lane.log"
  echo "::endgroup::"
done
echo "App tests: slow lane exit $slow_status, rest lane exit $rest_status"
[ "$slow_status" -eq 0 ] && [ "$rest_status" -eq 0 ]
