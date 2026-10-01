#!/usr/bin/env bash
# Live vs after class on one real lecture, with each model: what it costs while the lecture records, how long the
# student waits once it stops, and how the transcripts differ. See Program.cs for what each number is.
#
#   engine/tools/TranscribeBench/run.sh ~/.study-stash/recordings/rec-….wav [out-dir]
#
# FROM and LENGTH pick the slice (seconds; 600 and 720: twelve minutes from ten minutes in), MODELS the models (all
# must be downloaded; default this Mac's own, Parakeet and large-v3), SPEED how fast the recording plays (1: real
# time, which live needs to mean anything). Runs one after another, about an hour and a half with the defaults:
# leave the computer alone meanwhile, plugged in or not, but the same for every run.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
wav="$1"
out="${2:-bench-results}"
mkdir -p "$out"
dotnet build -c Release "$here" >/dev/null
bench=(dotnet "$here/bin/Release/net10.0/TranscribeBench.dll")
for model in ${MODELS:-large-v3-turbo-q5 parakeet-v3 large-v3}; do
  for mode in live after whole; do
    # Parakeet takes a few minutes of sound at a time at most; a whole lecture at once is Whisper's experiment.
    [ "$mode" = whole ] && [ "$model" = parakeet-v3 ] && continue
    echo "$(date +%H:%M:%S) $model $mode"
    "${bench[@]}" "$wav" --model "$model" --mode "$mode" --from "${FROM:-600}" --length "${LENGTH:-720}" \
      --speed "${SPEED:-1}" --out "$out" >/dev/null
  done
done
"${bench[@]}" table "$out"
# Words different from large-v3 written down after class, the most accurate here.
[ -f "$out/large-v3-after.txt" ] && "${bench[@]}" compare "$out/large-v3-after.txt" "$out"/*.txt
