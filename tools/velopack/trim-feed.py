#!/usr/bin/env python3
"""Leaves only this version's Velopack files in a channel's output folder (docs/LIBRARIES.md, phase 5):
    python3 -I tools/velopack/trim-feed.py <folder> <channel> <version>
`vpk download github` puts the last release's full package there so that `vpk pack` can make a delta from it. Each
GitHub release then carries only its own files: this version's full and delta packages, and a releases.<channel>.json
that lists just those. Velopack's GitHub source reads the feeds of the last releases together, so a copy several
versions behind still finds every delta on its way."""
import json
import os
import sys

folder, channel, version = sys.argv[1], sys.argv[2], sys.argv[3]
feed_path = os.path.join(folder, f"releases.{channel}.json")
with open(feed_path, encoding="utf-8-sig") as f:
    feed = json.load(f)
keep = [a for a in feed.get("Assets", []) if a.get("Version") == version]
if not any(a.get("Type") == "Full" for a in keep):
    sys.exit(f"trim-feed.py: {feed_path} has no full package for {version}")
names = {a["FileName"] for a in keep}
for name in os.listdir(folder):
    if name.endswith(".nupkg") and name not in names:
        os.remove(os.path.join(folder, name))
        print(f"trim-feed.py: removed {name}, the earlier release's (it stays in that release)")
feed["Assets"] = keep
with open(feed_path, "w", encoding="utf-8") as f:
    json.dump(feed, f, indent=2)
for a in keep:
    print(f"trim-feed.py: {channel} {a['Type']} {a['FileName']} {a.get('Size')}")
