#!/bin/sh
# Velopack's package of the Linux app (docs/LIBRARIES.md, phase 5), one channel per processor, from the folder
# linux/build.sh published: the AppImage (SyftNotes-<rid>.AppImage: what a first install downloads, and what a tarball
# copy moves over to, StudyStash.Core/Bridge.cs), the full package, a delta from the last release when there is one,
# and releases.<rid>.json, in <out-dir>/velopack/<rid>. The app puts itself in the applications menu when it runs from
# an AppImage (LinuxMenu.cs); some systems need FUSE for an AppImage to open (docs/linux.md).
#   sh linux/velopack-pack.sh [out-dir]        (default: dist/linux, where build.sh made <rid>/syft-notes)
# STUDYSTASH_LINUX_RIDS as for build.sh. Needs vpk (dotnet tool install -g vpk --version 1.2.161), mksquashfs
# (squashfs-tools) and Python 3.
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=${1:-"$ROOT/dist/linux"}
OUT=$(cd "$OUT" && pwd)
RIDS=${STUDYSTASH_LINUX_RIDS:-"linux-x64 linux-arm64"}
VERSION=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$ROOT/engine/Directory.Build.props")
command -v vpk > /dev/null || { echo "velopack-pack.sh: vpk isn't installed (dotnet tool install -g vpk --version 1.2.161)" >&2; exit 1; }

for RID in $RIDS; do
  TREE="$OUT/$RID/syft-notes"
  [ -x "$TREE/StudyStash" ] || { echo "velopack-pack.sh: no $TREE (run linux/build.sh first)" >&2; exit 1; }
  # The tarball's own install scripts and menu entry have no place in the AppImage (it carries its own entry, and
  # the app writes the menu's).
  PACK="$OUT/velopack-tree/$RID"
  rm -rf "$PACK"
  mkdir -p "$(dirname "$PACK")"
  cp -R "$TREE" "$PACK"
  rm -f "$PACK/install.sh" "$PACK/uninstall.sh" "$PACK/syft-notes.desktop"
  FEED="$OUT/velopack/$RID"
  rm -rf "$FEED"
  mkdir -p "$FEED"
  # The last release's package, so this one comes with a delta from it (the first Velopack release has none).
  # STUDYSTASH_VELOPACK_BASE: a folder of channel feeds to make the delta from instead (the bridge test's second version).
  if [ -n "${STUDYSTASH_VELOPACK_BASE:-}" ]; then
    cp "$STUDYSTASH_VELOPACK_BASE/$RID/"*.nupkg "$STUDYSTASH_VELOPACK_BASE/$RID/releases.$RID.json" "$FEED/"
  else
    vpk download github --repoUrl https://github.com/Joseph-Rus/syftnotes --channel "$RID" --outputDir "$FEED" \
      || echo "velopack-pack.sh: no earlier release on the $RID channel, so no delta this time"
  fi
  vpk pack --packId SyftNotes --packVersion "$VERSION" --packDir "$PACK" --mainExe StudyStash --runtime "$RID" \
    --channel "$RID" --packTitle "Syft Notes" --packAuthors "Syft Notes" --icon "$ROOT/assets/icon.png" \
    --categories "Education;Office;AudioVideo;Audio;Recorder;" --outputDir "$FEED"
  python3 -I "$ROOT/tools/velopack/trim-feed.py" "$FEED" "$RID" "$VERSION"
  [ -f "$FEED/SyftNotes-$RID.AppImage" ] || { echo "velopack-pack.sh: vpk made no SyftNotes-$RID.AppImage" >&2; ls -l "$FEED" >&2; exit 1; }
  chmod 755 "$FEED/SyftNotes-$RID.AppImage"
  rm -rf "$PACK"
  echo "Velopack's $RID package of Syft Notes $VERSION:"
  ls -l "$FEED"
done
