#!/usr/bin/env python3
"""The second video's drawings: the app's own labelled illustrations (art2/svg, drawn by Claude for Study Stash),
turned into TypeScript modules the video can draw inline, so a part can light up, dim or wear a ring.

    python3 scripts/art2.py

Every id gets the drawing's name in front (drone-flight-controller, …), so two drawings on screen at once never share
a gradient, and the scenes can style a part by its id. Writes src/promo2/art/<name>.ts.
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
ART = {'drone': 'after-drone.svg', 'hand': 'after-hand.svg'}

for name, file in ART.items():
    svg = (ROOT / 'art2' / 'svg' / file).read_text()
    view = re.search(r'viewBox="([^"]+)"', svg).group(1)
    inner = re.sub(r'^<svg[^>]*>|</svg>\s*$', '', svg.strip())
    inner = re.sub(r'<title>[^<]*</title>', '', inner, count=1)  # the drawing's own title: no tooltip in a video
    inner = re.sub(r'\bid="([^"]+)"', lambda m: f'id="{name}-{m.group(1)}"', inner)
    inner = re.sub(r'url\(#([^)]+)\)', lambda m: f'url(#{name}-{m.group(1)})', inner)
    inner = re.sub(r'href="#([^"]+)"', lambda m: f'href="#{name}-{m.group(1)}"', inner)
    parts = re.findall(rf'<g id="{name}-([^"]+)"><title>([^<]*)</title><desc>([^<]*)</desc>', inner)
    out = ROOT / 'src' / 'promo2' / 'art' / f'{name}.ts'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(
        f'// Made by scripts/art2.py from art2/svg/{file} (the app\'s own drawing). Don\'t edit by hand.\n'
        f'export const viewBox = {json.dumps(view)};\n'
        f'export const parts: Record<string, {{name: string; desc: string}}> = {json.dumps({p: {"name": t, "desc": d} for p, t, d in parts}, ensure_ascii=False)};\n'
        f'export const inner = {json.dumps(inner, ensure_ascii=False)};\n'
    )
    print(f'{name}: {len(parts)} parts, {len(inner) // 1024} KB → {out.relative_to(ROOT)}')
