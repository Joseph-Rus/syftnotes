# Static instances of the handoff's variable font, as TTF: Avalonia loads TTF, and picks a weight by its own file.
import sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

src, out = sys.argv[1], sys.argv[2]
for weight, style in [(300, "Light"), (400, "Regular"), (500, "Medium"), (600, "SemiBold"), (700, "Bold"), (800, "ExtraBold")]:
    font = TTFont(src)
    font = instancer.instantiateVariableFont(font, {"wght": weight}, updateFontNames=True)
    font.flavor = None
    path = f"{out}/AtkinsonHyperlegibleNext-{style}.ttf"
    font.save(path)
    check = TTFont(path)
    names = {n.nameID: n.toUnicode() for n in check["name"].names if n.platformID == 3}
    print(style, check["OS/2"].usWeightClass, {k: names.get(k) for k in (1, 2, 16, 17)}, "fvar" in check)
