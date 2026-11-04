#!/usr/bin/env python3
"""The Velopack bridge test's stand-in for GitHub (.github/workflows/velopack-bridge.yml), on a throwaway runner only:
    python3 -I tools/velopack/fake-release.py <served-folder> <version> <base-url>
writes <served-folder>/release.json in the shape of GitHub's /releases/latest answer (tag, page, and every file in the
folder as an asset at <base-url>/<name>), and SHA256SUMS.txt for them, as a release has. The app under test reads it
through STUDYSTASH_RELEASES_URL (test helpers only); the folder is served by `python3 -m http.server`."""
import hashlib
import json
import os
import sys

folder, version, base = sys.argv[1], sys.argv[2], sys.argv[3].rstrip("/")
names = sorted(n for n in os.listdir(folder) if os.path.isfile(os.path.join(folder, n)) and n not in ("release.json", "SHA256SUMS.txt"))
with open(os.path.join(folder, "SHA256SUMS.txt"), "w", encoding="utf-8") as sums:
    for n in names:
        h = hashlib.sha256()
        with open(os.path.join(folder, n), "rb") as f:
            for block in iter(lambda: f.read(1 << 20), b""):
                h.update(block)
        sums.write(f"{h.hexdigest()}  {n}\n")
assets = [{"name": n, "browser_download_url": f"{base}/{n}"} for n in names + ["SHA256SUMS.txt"]]
release = {"tag_name": f"v{version}", "html_url": f"{base}/release.json", "assets": assets}
with open(os.path.join(folder, "release.json"), "w", encoding="utf-8") as f:
    json.dump(release, f, indent=2)
print(f"fake-release.py: v{version} with {len(names)} files at {base}")
