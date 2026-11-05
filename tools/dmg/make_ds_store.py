#!/usr/bin/env python3
"""Writes macos/dmg-layout.DS_Store: how the Syft Notes disk opens in Finder (one small window, the app on the left,
the Applications folder on the right, nothing else, no toolbar or sidebar). macos/dmg.sh copies it into the DMG's
stage folder, so building the DMG needs no Finder scripting. Needs `pip install ds_store`; run again only to change
the layout:  python3 tools/dmg/make_ds_store.py macos/dmg-layout.DS_Store
bwsp and icvp are given as plain dicts: ds_store writes them as property lists itself, so Finder reads a dictionary
(encoding them here too left Finder a data blob, and the window never opened). tools/dmg/check_ds_store.py checks it."""
import sys

from ds_store import DSStore

WIDTH, HEIGHT = 540, 360          # the window's content size
APP_AT, APPLICATIONS_AT = (150, 170), (390, 170)   # icon centres, window coordinates

out = sys.argv[1]
bwsp = {
    "WindowBounds": "{{200, 140}, {%d, %d}}" % (WIDTH, HEIGHT),
    "ContainerShowSidebar": False, "ShowPathbar": False, "ShowStatusBar": False, "ShowTabView": False,
    "ShowToolbar": False, "SidebarWidth": 0,
}
icvp = {
    "viewOptionsVersion": 1, "backgroundType": 0, "backgroundColorRed": 1.0, "backgroundColorGreen": 1.0,
    "backgroundColorBlue": 1.0, "gridOffsetX": 0.0, "gridOffsetY": 0.0, "gridSpacing": 100.0, "iconSize": 128.0,
    "arrangeBy": "none", "showIconPreview": True, "showItemInfo": False, "labelOnBottom": True, "textSize": 13.0,
    "scrollPositionX": 0.0, "scrollPositionY": 0.0,
}
with DSStore.open(out, "w+") as d:
    d["."]["vSrn"] = ("long", 1)
    d["."]["bwsp"] = bwsp
    d["."]["icvl"] = ("type", b"icnv")
    d["."]["icvp"] = icvp
    d["Syft Notes.app"]["Iloc"] = APP_AT
    d["Applications"]["Iloc"] = APPLICATIONS_AT
