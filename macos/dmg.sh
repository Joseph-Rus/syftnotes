#!/bin/sh
# stage_dmg, the one place a Study Stash DMG is made. Sourced (never run) by macos/build-app.sh, which makes the
# first, ad hoc signed DMG, and by macos/sign-release.sh, which makes it again around the Developer ID signed and
# notarized app. The caller sets OUT (a scratch folder these stage and mount under) and calls
#   stage_dmg "<path>/Study Stash.app" "Study Stash" "<path>/Study-Stash.dmg"

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
  rm -rf "$stage" "$rw" "$mnt"
  mkdir -p "$stage" "$mnt"
  ditto "$app_path" "$stage/Study Stash.app"
  ln -s /Applications "$stage/Applications"
  cp "$app_path/Contents/Resources/AppIcon.icns" "$stage/.VolumeIcon.icns"
  SetFile -c icnC "$stage/.VolumeIcon.icns" 2>/dev/null || true
  hdiutil create -quiet -volname "$volname" -srcfolder "$stage" -ov -format UDRW "$rw"
  if hdiutil attach -quiet -nobrowse -mountpoint "$mnt" "$rw"; then
    SetFile -a C "$mnt" 2>/dev/null || true
    hdiutil detach -quiet "$mnt"
  fi
  rm -f "$dmg"
  hdiutil convert -quiet "$rw" -format UDZO -o "$dmg"
  osascript -l JavaScript -e 'ObjC.import("AppKit"); function run(a) { $.NSWorkspace.sharedWorkspace.setIconForFileOptions($.NSImage.alloc.initWithContentsOfFile(a[0]), a[1], 0); }' \
    "$app_path/Contents/Resources/AppIcon.icns" "$dmg" >/dev/null 2>&1 || true
  rm -rf "$stage" "$rw" "$mnt"
}
