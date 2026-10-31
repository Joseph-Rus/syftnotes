#!/usr/bin/env python3
"""The website's pictures of the app, from what SiteShots drew (engine/tests/StudyStash.App.Tests/SiteShots.cs).

    STUDYSTASH_SITE=/tmp/site-shots dotnet test engine/tests/StudyStash.App.Tests --filter SiteShots
    python3 tools/site_pictures.py /tmp/site-shots

Rounds the whole window's corners (the Mac's 18 points, 36 pixels at 2x) so the page shows round them, and writes
every picture as WebP into site/assets/img: <name>.webp for the light look, <name>-dark.webp for the dark. Needs
Pillow and cwebp (brew install webp).
"""
import pathlib
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw

SITE = pathlib.Path(__file__).resolve().parent.parent / "site" / "assets" / "img"
WINDOWS = {"home": 36, "setup": 36}  # pictures of the whole window, and the radius of its corners in pixels
NAMES = ["home", "record", "notes", "formulas", "graph", "heart", "cycle", "due", "ask", "ai-apps", "setup", "first-notes"]


def rounded(im, radius):
    """The picture with its corners cut round, drawn at 4x and brought down so the edge is smooth."""
    w, h = im.size
    mask = Image.new("L", (w * 4, h * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w * 4 - 1, h * 4 - 1), radius * 4, fill=255)
    out = im.convert("RGBA")
    out.putalpha(mask.resize((w, h), Image.LANCZOS))
    return out


def main(shots):
    shots = pathlib.Path(shots)
    with tempfile.TemporaryDirectory() as tmp:
        for name in NAMES:
            for theme, suffix in (("light", ""), ("dark", "-dark")):
                im = Image.open(shots / f"{name}-{theme}.png")
                im = rounded(im, WINDOWS[name]) if name in WINDOWS else im.convert("RGB")
                png = pathlib.Path(tmp) / f"{name}{suffix}.png"
                im.save(png)
                out = SITE / f"{name}{suffix}.webp"
                subprocess.run(["cwebp", "-quiet", "-q", "88", "-m", "6", "-alpha_q", "100", str(png), "-o", str(out)], check=True)
                print(f"{out.relative_to(SITE.parent.parent.parent)}  {im.size[0]}x{im.size[1]}  {out.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "/tmp/site-shots")
