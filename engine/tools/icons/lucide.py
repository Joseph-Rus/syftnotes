# Writes IconPaths.cs: Lucide's outline icons (ISC) as SVG path data in a 24-unit box, under the names the app's views
# already ask for (Material's, from the look before) and the handoff's own. Run with -I; reads only the icons folder.
import sys, re
import xml.etree.ElementTree as ET

icons_dir, out = sys.argv[1], sys.argv[2]

# the app's name -> Lucide's
NAMES = {
    "add": "plus", "add_circle": "circle-plus", "add_link": "link", "arrow_back": "arrow-left", "arrow_forward": "arrow-right",
    "arrow_outward": "arrow-up-right", "arrow_upward": "arrow-up", "assignment": "clipboard-list", "attach_file": "paperclip",
    "auto_awesome": "bot", "battery_5_bar": "battery-medium", "bluetooth": "bluetooth", "book_2": "book", "bookmark": "bookmark",
    "campaign": "megaphone", "chat": "message-circle", "check": "check", "check_circle": "circle-check",
    "chevron_left": "chevron-left", "chevron_right": "chevron-right", "circle": "circle", "close": "x",
    "close_fullscreen": "minimize-2", "cloud": "cloud", "cloud_off": "cloud-off", "code": "code", "content_copy": "copy", "content_paste": "clipboard",
    "crop_square": "square", "delete": "trash-2", "description": "file-text", "desktop_windows": "monitor", "dns": "server",
    "dock_to_right": "panel-right", "download": "download", "draft": "file", "drive_file_move": "folder-input", "edit": "pencil",
    "edit_note": "notebook-pen", "error": "circle-alert", "event": "calendar", "event_upcoming": "calendar-clock",
    "expand_less": "chevron-up", "expand_more": "chevron-down", "extension": "puzzle", "fiber_manual_record": "circle",
    "filter_list": "list-filter", "folder": "folder", "folder_open": "folder-open", "forum": "messages-square", "grade": "star",
    "graphic_eq": "audio-lines", "headphones": "headphones", "history": "history", "home": "house", "hourglass_top": "hourglass", "hub": "waypoints",
    "image": "image", "inbox": "inbox", "info": "info", "ios_share": "share", "key": "key-round", "keyboard": "keyboard",
    "keyboard_arrow_up": "chevron-up", "language": "globe", "laptop_mac": "laptop", "link": "link", "link_off": "unlink",
    "local_cafe": "coffee", "lock": "lock", "logout": "log-out", "memory": "cpu", "mic": "mic", "more_horiz": "ellipsis",
    "new_releases": "badge-alert", "notes": "text", "notifications": "bell", "open_in_full": "maximize-2",
    "open_in_new": "external-link", "public": "earth", "palette": "palette", "pause": "pause", "picture_as_pdf": "file-text",
    "person_off": "user-x", "play_arrow": "play", "play_circle": "circle-play", "power_off": "power-off",
    "power_settings_new": "power", "quiz": "circle-help", "radio_button_unchecked": "circle", "refresh": "rotate-cw",
    "remove": "minus", "save_alt": "download", "schedule": "clock", "school": "graduation-cap", "search": "search",
    "send": "send", "settings": "settings", "sidebar": "panel-left", "slideshow": "presentation", "smartphone": "smartphone",
    "stop": "square", "subject": "text", "swap_horiz": "arrow-left-right", "sync": "refresh-cw", "table_chart": "table",
    "travel_explore": "compass", "terminal": "terminal", "tune": "sliders-horizontal", "undo": "undo-2", "unfold_less": "chevrons-down-up",
    "unfold_more": "chevrons-up-down", "upload": "upload", "volume_up": "volume-2", "warning": "triangle-alert", "wifi": "wifi",
    "wifi_off": "wifi-off",
}
# drawn solid when a view asks for the filled one (a shape with nothing inside it to lose)
SOLID = ["circle", "fiber_manual_record", "pause", "play_arrow", "stop"]

def num(v):
    s = f"{float(v):.3f}".rstrip("0").rstrip(".")
    return s if s else "0"

NUMBER = r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?"

def absolute_start(d):
    # In its own file a path that opens with a relative "m" starts from nowhere, so it's absolute; joined on after
    # another shape it would start from that shape's end. Open it with "M", and keep what follows the first point
    # relative, as it was ("m21 21-4.3-4.3" is "M21 21l-4.3-4.3").
    if not d.startswith("m"):
        return d
    m = re.match(rf"m\s*({NUMBER})[\s,]*({NUMBER})[\s,]*", d)
    rest = d[m.end():]
    return f"M{m.group(1)} {m.group(2)}" + (("l" + rest) if rest and (rest[0].isdigit() or rest[0] in "-+.") else rest)

def shape(el):
    tag = el.tag.split("}")[-1]
    a = el.attrib
    if tag == "path":
        return absolute_start(re.sub(r"\s+", " ", a["d"].strip()))
    if tag == "line":
        return f"M{num(a['x1'])} {num(a['y1'])}L{num(a['x2'])} {num(a['y2'])}"
    if tag in ("polyline", "polygon"):
        pts = re.split(r"[\s,]+", a["points"].strip())
        pairs = [f"{num(pts[i])} {num(pts[i + 1])}" for i in range(0, len(pts), 2)]
        return "M" + "L".join(pairs) + ("Z" if tag == "polygon" else "")
    if tag in ("circle", "ellipse"):
        cx, cy = float(a.get("cx", 0)), float(a.get("cy", 0))
        rx = float(a.get("r", a.get("rx", 0))); ry = float(a.get("r", a.get("ry", rx)))
        return f"M{num(cx - rx)} {num(cy)}A{num(rx)} {num(ry)} 0 1 0 {num(cx + rx)} {num(cy)}A{num(rx)} {num(ry)} 0 1 0 {num(cx - rx)} {num(cy)}Z"
    if tag == "rect":
        x, y, w, h = float(a.get("x", 0)), float(a.get("y", 0)), float(a["width"]), float(a["height"])
        rx = float(a.get("rx", a.get("ry", 0))); ry = float(a.get("ry", rx))
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        if rx == 0 and ry == 0:
            return f"M{num(x)} {num(y)}H{num(x + w)}V{num(y + h)}H{num(x)}Z"
        return (f"M{num(x + rx)} {num(y)}H{num(x + w - rx)}A{num(rx)} {num(ry)} 0 0 1 {num(x + w)} {num(y + ry)}V{num(y + h - ry)}"
                f"A{num(rx)} {num(ry)} 0 0 1 {num(x + w - rx)} {num(y + h)}H{num(x + rx)}A{num(rx)} {num(ry)} 0 0 1 {num(x)} {num(y + h - ry)}"
                f"V{num(y + ry)}A{num(rx)} {num(ry)} 0 0 1 {num(x + rx)} {num(y)}Z")
    raise ValueError(f"unknown element {tag}")

lines, missing = [], []
for name in sorted(NAMES):
    lucide = NAMES[name]
    try:
        root = ET.parse(f"{icons_dir}/{lucide}.svg").getroot()
    except FileNotFoundError:
        missing.append((name, lucide)); continue
    d = " ".join(shape(el) for el in root)
    lines.append(f'        ["{name}"] = "{d}",')
if missing:
    sys.exit(f"not in Lucide: {missing}")

solid = ", ".join(f'"{s}"' for s in SOLID)
open(out, "w").write(f'''namespace StudyStash.App;

/// <summary>
/// The app's icons: Lucide's outlines (ISC licence, version 0.469.0), as SVG path data in a 24-unit box, drawn as a
/// 1.5-unit round stroke in the text colour (see Controls/Icon.cs). The names are the ones the views ask for: most were
/// Material's from the look before ("arrow_back" is Lucide's arrow-left, "home" its house), and a few are new with
/// this look ("sidebar", "upload", "bookmark"). Generated from Lucide's own files; a rect, circle or line there is the
/// same shape here as a path. dock_to_right is Lucide's panel-right, which the views that ask for it turn round.
/// </summary>
public static class IconPaths
{{
    /// <summary>The icons drawn solid when a view asks for the filled one (Pause, Stop, Play, a dot): shapes with
    /// nothing inside them to lose. Any other icon is its outline either way.</summary>
    public static readonly IReadOnlySet<string> Solid = new HashSet<string> {{ {solid} }};

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {{
{chr(10).join(lines)}
    }};
}}
''')
print(len(lines), "icons")
