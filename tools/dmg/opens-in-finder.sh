#!/bin/sh
# Opens a built DMG the way a student does (downloaded by a browser, so quarantined, then double-clicked) and fails
# unless it mounts, Finder shows its window, and Finder doesn't crash. Every Mac DMG from 0.22.0 to 0.23.2 crashed
# Finder when opened on macOS 26 ("-[__NSCFData count]: unrecognized selector", its saved layout held data where
# Finder reads dictionaries), and nothing in CI ever opened one. For CI's Mac, which has a logged-in desktop with Finder
# running; don't run it on a Mac someone is using (it opens a Finder window there).
#   sh tools/dmg/opens-in-finder.sh dist/mac/Syft-Notes.dmg
set -u
work=$(mktemp -d)
copy="$work/$(basename "$1")"
cp "$1" "$copy"
xattr -w com.apple.quarantine "0083;$(printf %x "$(date +%s)");Safari;" "$copy"

# Finder's own windows on screen, one name a line ("?" where the system keeps the name from us). CoreGraphics, read in
# this process: nothing is asked of Finder, which could hang or need a permission.
finder_windows() {
  osascript -l JavaScript -e 'ObjC.import("CoreGraphics");
    ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1, 0)))
      .filter(w => w.kCGWindowOwnerName === "Finder" && w.kCGWindowLayer === 0)
      .map(w => w.kCGWindowName || "?").join("\n")'
}

finder=$(pgrep -x Finder) || { echo "Finder isn't running here, so the DMG can't be opened as a student does" >&2; exit 1; }
windows_before=$(finder_windows | grep -c .)
ls /Volumes > "$work/volumes-before"
touch "$work/marker"
since=$(date '+%Y-%m-%d %H:%M:%S')
open "$copy"

# Up to 60 s for the disk to mount and its window to show; Finder relaunching (a crash) ends the wait at once.
vol="" window="" waited=0
while [ "$waited" -lt 60 ] && [ "$(pgrep -x Finder)" = "$finder" ]; do
  [ -n "$vol" ] || { new=$(ls /Volumes | grep -vxF -f "$work/volumes-before" | head -n 1); [ -z "$new" ] || vol="/Volumes/$new"; }
  if [ -n "$vol" ]; then
    names=$(finder_windows)
    if printf '%s\n' "$names" | grep -qxF "${vol#/Volumes/}" || [ "$(printf '%s\n' "$names" | grep -c .)" -gt "$windows_before" ]; then
      window=yes
      break
    fi
  fi
  waited=$((waited + 1))
  sleep 1
done

failed=0
fail() { echo "::error::$*"; failed=1; }
now=$(pgrep -x Finder)
[ "$now" = "$finder" ] || fail "Finder crashed or quit while the DMG opened (it was pid $finder, now ${now:-not running})"
reports=$(find "$HOME/Library/Logs/DiagnosticReports" /Library/Logs/DiagnosticReports -maxdepth 1 \
  \( -name 'Finder*.ips' -o -name 'Finder*.hang' -o -name 'Finder*.spin' \) -newer "$work/marker" 2>/dev/null)
[ -z "$reports" ] || fail "Finder left a crash or hang report: $reports"
if log show --start "$since" --predicate 'process == "Finder"' --style compact 2>/dev/null | grep -F "unrecognized selector"; then
  fail "Finder's log says \"unrecognized selector\" since the DMG was opened"
fi
[ -n "$vol" ] || fail "the DMG didn't mount in 60 s"
[ -n "$vol" ] && [ -z "$window" ] && fail "Finder showed no window for $vol in 60 s; its windows: $(finder_windows | tr '\n' ',')"
[ -z "$vol" ] || hdiutil detach -quiet "$vol" || hdiutil detach -quiet -force "$vol"
[ "$failed" = 0 ] && echo "Finder opened $(basename "$1") (quarantined, as downloaded) at $vol, showed its window after ${waited} s, and didn't crash"
exit "$failed"
