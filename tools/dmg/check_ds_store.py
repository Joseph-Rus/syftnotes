#!/usr/bin/env python3
"""Checks macos/dmg-layout.DS_Store, the Finder layout the Syft Notes disk opens with: the window settings (bwsp) and
the icon view settings (icvp) must be dictionaries, as Finder reads them, and the two icons must be placed. A layout
written with its dictionaries encoded twice reads back as a data blob, and Finder then never opens the disk's window.
Needs `pip install ds_store`:  python3 -I tools/dmg/check_ds_store.py macos/dmg-layout.DS_Store"""
import sys

from ds_store import DSStore

with DSStore.open(sys.argv[1], "r") as d:
    entries = {(e.filename, e.code): e.value for e in d}
for code, key in ((b"bwsp", "WindowBounds"), (b"icvp", "iconSize")):
    value = entries.get((".", code))
    if not isinstance(value, dict) or key not in value:
        sys.exit(f"{code.decode()} in the disk's layout isn't a dictionary with {key}: {value!r:.80}")
for name in ("Syft Notes.app", "Applications"):
    if not isinstance(entries.get((name, b"Iloc")), tuple):
        sys.exit(f"{name} has no place in the disk's layout")
print("the disk's layout holds dictionaries")
