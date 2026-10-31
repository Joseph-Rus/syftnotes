#!/bin/sh
# stage_dmg, the one place a Syft Notes DMG is made. Sourced (never run) by macos/build-app.sh, which makes the
# first, ad hoc signed DMG, and by macos/sign-release.sh, which makes it again around the Developer ID signed and
# notarized app. The caller sets OUT (a scratch folder these stage and mount under) and calls
#   stage_dmg "<path>/Study Stash.app" "Syft Notes" "<path>/Syft-Notes.dmg"

# The DMG: a stage folder with the app plus a link to /Applications. The disk shows the app's icon when it opens
# (.VolumeIcon.icns, flagged on the volume, which only a mounted read-write copy can take), and the .dmg file itself
# wears it too.
# hdiutil, tried up to three times. On a busy Mac (CI's, now and then) it fails once and works a few seconds later:
# two release rehearsals stopped here, after the app was built and signed, with nothing in the log, because it runs
# quietly. So the first try is quiet, and a try after a failure says everything it does.
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
    echo "hdiutil $hdi_verb didn't work (try $hdi_try of 3); trying again in 5 seconds" >&2
    hdi_try=$((hdi_try + 1))
    sleep 5
  done
}

stage_dmg() {
  app_path=$1
  volname=$2
  dmg=$3
  stage="$OUT/stage"
  rw="$OUT/stage.dmg"
  mnt="$OUT/mount"
  rm -rf "$stage" "$rw" "$mnt"
  mkdir -p "$stage" "$mnt"
  ditto "$app_path" "$stage/Study Stash.app"
  ln -s /Applications "$stage/Applications"
  cp "$app_path/Contents/Resources/AppIcon.icns" "$stage/.VolumeIcon.icns"
  SetFile -c icnC "$stage/.VolumeIcon.icns" 2>/dev/null || true
  hdi create -volname "$volname" -srcfolder "$stage" -ov -format UDRW "$rw"
  if hdiutil attach -quiet -nobrowse -mountpoint "$mnt" "$rw"; then
    SetFile -a C "$mnt" 2>/dev/null || true
    # A volume something is still looking at won't let go: asked three times, then made to. One that's no longer
    # mounted is done ("No such file or directory": CI's runner sometimes lets go of it by itself, 0.18.5's run).
    hdi detach "$mnt" || hdiutil detach -force "$mnt" || ! mount | grep -qF " on $mnt ("
  fi
  rm -f "$dmg"
  hdi convert "$rw" -format UDZO -ov -o "$dmg"
  osascript -l JavaScript -e 'ObjC.import("AppKit"); function run(a) { $.NSWorkspace.sharedWorkspace.setIconForFileOptions($.NSImage.alloc.initWithContentsOfFile(a[0]), a[1], 0); }' \
    "$app_path/Contents/Resources/AppIcon.icns" "$dmg" >/dev/null 2>&1 || true
  rm -rf "$stage" "$rw" "$mnt"
}
