#!/bin/sh
# stage_dmg, the one place a Syft Notes DMG is made. Sourced (never run) by macos/build-app.sh, which makes the
# first, ad hoc signed DMG, and by macos/sign-release.sh, which makes it again around the Developer ID signed and
# notarized app. The caller sets OUT (a scratch folder these stage and mount under) and calls
#   stage_dmg "<path>/Syft Notes.app" "Syft Notes" "<path>/Syft-Notes.dmg"
# The disk holds "Syft Notes.app", a link to /Applications, and a hidden link "Study Stash.app" to the app: an older
# copy's updater (0.22 and before) looks on the disk for the bundle by the name it has itself, Study Stash.app, and
# copies it out through the link (ditto follows it); the new app then renames itself (MacRename.cs). Finder never
# shows the hidden link, so a student sees one app to drag.

# The disks (/dev/diskN) that have the image "$1" attached, one per line, whether or not a volume of it is mounted.
# `hdiutil info` may name the image by its real path (/tmp is /private/tmp), so that spelling is matched too.
dmg_disks() {
  dmg_real="$(cd "$(dirname "$1")" 2>/dev/null && pwd -P)/$(basename "$1")"
  hdiutil info | DMG_A=$1 DMG_B=$dmg_real awk '
    /^=+$/ { mine = 0; next }
    /^image-path[ \t]*:/ { path = $0; sub(/^image-path[ \t]*:[ \t]*/, "", path); mine = (path == ENVIRON["DMG_A"] || path == ENVIRON["DMG_B"]); next }
    mine && /^\/dev\/disk[0-9]+[ \t]/ { print $1 }
  '
}

# Waits, for $2 seconds at most, until no disk has the image "$1" attached. With a third argument, every disk still
# holding it is made to let go (detach -force) on each look. A disk whose volume is gone can still hold the image open,
# and convert can't open an image that is open ("Resource temporarily unavailable", CI's 0.23.3 rehearsal). Returns 1
# if the image is still attached when the time is up.
dmg_free() {
  free_deadline=$(($(date +%s) + $2))
  while :; do
    free_disks=$(dmg_disks "$1")
    if [ -z "$free_disks" ]; then
      return 0
    fi
    if [ "$(date +%s)" -ge "$free_deadline" ]; then
      echo "$1 is still attached as: $free_disks" >&2
      return 1
    fi
    if [ $# -ge 3 ]; then
      for free_disk in $free_disks; do
        hdiutil detach -force "$free_disk" || true
      done
    fi
    sleep 1 # the poll's interval (as Poll.UntilAsync's `every`); the deadline above ends it
  done
}

# hdiutil, tried up to three times. On a busy Mac (CI's, now and then) it fails once and works on a second try:
# two release rehearsals stopped here, after the app was built and signed, with nothing in the log, because it runs
# quietly. So the first try is quiet, and a try after a failure says everything it does. Between tries `sync` returns
# once the disk has taken every write still pending (what a busy volume or image is usually still doing), and then the
# wait is on what the next try needs, $hdi_image (set by the caller) as the image: a detach waits for it to be no longer
# attached, a convert makes any disk still holding it let go.
hdi() {
  hdi_verb=$1
  shift
  hdi_try=1
  while :; do
    if [ "$hdi_try" -eq 1 ]; then
      hdiutil "$hdi_verb" -quiet "$@" && return 0
    else
      hdiutil "$hdi_verb" "$@" && return 0
    fi
    if [ "$hdi_try" -ge 3 ]; then
      echo "hdiutil $hdi_verb failed three times" >&2
      return 1
    fi
    echo "hdiutil $hdi_verb didn't work (try $hdi_try of 3); trying again once the disk has caught up" >&2
    hdi_try=$((hdi_try + 1))
    sync
    case $hdi_verb in
      detach) dmg_free "$hdi_image" 10 || true ;;
      convert) dmg_free "$hdi_image" 30 force || true ;;
    esac
  done
}

# The DMG: a stage folder with the app plus a link to /Applications. The disk shows the app's icon when it opens
# (.VolumeIcon.icns, flagged on the volume, which only a mounted read-write copy can take), and the .dmg file itself
# wears it too.
stage_dmg() {
  app_path=$1
  volname=$2
  dmg=$3
  stage="$OUT/stage"
  rw="$OUT/stage.dmg"
  mnt="$OUT/mount"
  hdi_image=$rw
  rm -rf "$stage" "$rw" "$mnt"
  mkdir -p "$stage" "$mnt"
  ditto "$app_path" "$stage/Syft Notes.app"
  ln -s /Applications "$stage/Applications"
  ln -s "Syft Notes.app" "$stage/Study Stash.app"
  # How the disk opens (tools/dmg/make_ds_store.py): one small window, the app on the left and the Applications
  # folder on the right, so the one thing to do is to drag the left one onto the right one. A saved Finder layout
  # (a .DS_Store), so no Finder scripting runs while the DMG is built.
  cp "$HERE/dmg-layout.DS_Store" "$stage/.DS_Store" || true
  # An empty no_log asks the system not to keep a file-event log on the disk, as other disk images do.
  mkdir -p "$stage/.fseventsd"
  : > "$stage/.fseventsd/no_log"
  cp "$app_path/Contents/Resources/AppIcon.icns" "$stage/.VolumeIcon.icns"
  SetFile -c icnC "$stage/.VolumeIcon.icns" 2>/dev/null || true
  # HFS+, as other disk images are and as Finder's saved layout expects: hdiutil's own default is APFS, which macOS
  # logged "Not HFS" for, and the disk's window never opened.
  hdi create -volname "$volname" -fs HFS+ -srcfolder "$stage" -ov -format UDRW "$rw"
  # The disk this attaches is kept (the first /dev/diskN line): a mount point can be gone while the disk still holds the
  # image, and a detach by mount point then fails with "No such file or directory".
  if attach_out=$(hdiutil attach -nobrowse -noverify -mountpoint "$mnt" "$rw"); then
    dev=$(printf '%s\n' "$attach_out" | awk '/^\/dev\/disk[0-9]+[ \t]/ { print $1; exit }')
    SetFile -a C "$mnt" 2>/dev/null || true
    # Hidden on the mounted disk itself, so the flag is the disk's own.
    chflags -h hidden "$mnt/Study Stash.app"
    # A volume something is still looking at won't let go: asked three times, then made to.
    hdi detach "${dev:-$mnt}" || hdiutil detach -force "${dev:-$mnt}" || echo "the disk is still attached; letting go of it below" >&2
  fi
  # Nothing may still have the image open when it's converted, however the disk went.
  dmg_free "$rw" 30 force || true
  rm -f "$dmg"
  hdi convert "$rw" -format UDZO -ov -o "$dmg"
  osascript -l JavaScript -e 'ObjC.import("AppKit"); function run(a) { $.NSWorkspace.sharedWorkspace.setIconForFileOptions($.NSImage.alloc.initWithContentsOfFile(a[0]), a[1], 0); }' \
    "$app_path/Contents/Resources/AppIcon.icns" "$dmg" >/dev/null 2>&1 || true
  rm -rf "$stage" "$rw" "$mnt"
}
