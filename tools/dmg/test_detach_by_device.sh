#!/bin/sh
# macos/dmg.sh never detaches a disk by its number once its own image is gone: macOS reuses the lowest free diskN, so
# the number may be another image's by then (someone's own DMG, during a local build). hdiutil is a function here, so
# nothing is attached or mounted and this runs anywhere (CI's Linux check, a Mac). On the first detach our disk goes by
# itself and that detach fails, as a runner's did; another image then takes the same disk number, and it must never be
# detached.
set -eu
here=$(cd "$(dirname "$0")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
OUT=$scratch/out
HERE=$here/macos
mkdir -p "$OUT" "$scratch/app/Contents/Resources" "$OUT/mount/x"
: > "$scratch/app/Contents/Resources/AppIcon.icns"

hdiutil() {
  verb=$1
  case $verb in
    create) for last; do :; done; : > "$last" ;;
    attach) : > "$scratch/ours"; printf '/dev/disk7\tGUID_partition_scheme\t\n/dev/disk7s1\tApple_HFS\t%s/mount\n' "$OUT" ;;
    info)
      if [ -f "$scratch/ours" ]; then printf '=======\nimage-path      : %s/stage.dmg\n/dev/disk7\tGUID_partition_scheme\t\n' "$OUT"; fi
      if [ -f "$scratch/other" ]; then printf '=======\nimage-path      : /somewhere/other.dmg\n/dev/disk7\tGUID_partition_scheme\t\n'; fi ;;
    detach)
      if [ -f "$scratch/ours" ]; then
        rm -f "$scratch/ours"; : > "$scratch/other"
        echo "hdiutil: detach failed - No such file or directory" >&2
        return 1
      fi
      : > "$scratch/other-detached" ;;
    convert)
      if [ -f "$scratch/ours" ]; then return 1; fi
      for last; do :; done; : > "$last" ;;
  esac
}
ditto() { cp -R "$1" "$2"; }
SetFile() { :; }
chflags() { :; }
osascript() { :; }

. "$here/macos/dmg.sh"
stage_dmg "$scratch/app" "Syft Notes" "$OUT/Syft-Notes.dmg"

if [ -e "$scratch/other-detached" ]; then
  echo "FAIL: dmg.sh detached another image that had taken our disk's number" >&2
  exit 1
fi
[ -e "$OUT/Syft-Notes.dmg" ] || { echo "FAIL: the DMG wasn't made" >&2; exit 1; }
echo "ok: another image on our disk's old number was left alone"
