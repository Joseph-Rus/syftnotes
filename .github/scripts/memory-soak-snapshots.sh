#!/bin/sh
# THROWAWAY (branch memory-soak-0.16, never merged): while the self-test's memory soak runs (STUDYSTASH_SOAK_PAUSE=1),
# takes heap snapshot A when it pauses after its warm-up and B when it pauses after its rounds, lets it go on each time,
# and once it's done reports both by type and what grew per round.
#   sh memory-soak-snapshots.sh <self-test out dir> <dotnet-gcdump> <pid of the self-test's script> <rounds>
OUT=$1
GCDUMP=$2
TEST=$3
ROUNDS=$4
HERE=$(cd "$(dirname "$0")" && pwd)
for name in A B; do
  while [ ! -f "$OUT/pause-$name" ] && kill -0 "$TEST" 2>/dev/null; do sleep 1; done
  [ -f "$OUT/pause-$name" ] || break
  sleep 1
  app=$(cat "$OUT/pause-$name")
  "$GCDUMP" collect -p "$app" -o "$OUT/$name.gcdump" | tail -1
  touch "$OUT/go-$name"
  echo "snapshot $name taken at $(date +%H:%M:%S)"
done
while kill -0 "$TEST" 2>/dev/null; do sleep 2; done
for name in A B; do
  [ -f "$OUT/$name.gcdump" ] && "$GCDUMP" report "$OUT/$name.gcdump" > "$OUT/$name.txt"
done
if [ -f "$OUT/A.txt" ] && [ -f "$OUT/B.txt" ]; then
  python3 "$HERE/memory-soak-gcdump-diff.py" "$OUT/A.txt" "$OUT/B.txt" "$ROUNDS" > "$OUT/diff.txt"
  head -45 "$OUT/diff.txt"
fi
[ -f "$OUT/soak.txt" ] && { echo "--- soak.txt"; cat "$OUT/soak.txt"; }
exit 0
