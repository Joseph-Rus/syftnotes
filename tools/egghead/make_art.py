"""Draws Egghead Notes' character art from Eli's own drawing (drawing.json: the pen strokes of egghead.goodnotes).

    python3 tools/egghead/make_art.py

The egghead, the chicklet and the thought cloud are Eli's strokes as drawn. The other doodles reuse his egg, glasses
and bow tie and add a few marks of their own (closed eyes, a crack, spirals, a magnifier, a fried egg), drawn with a
slightly wobbly pen at his line weight so they sit beside his. Writes:

    assets/egghead/<name>.svg and <name>-dark.svg    each doodle, for the desktop app (Controls/Doodle.cs)
    assets/egghead/mark.json                          the head and its pen-stroke shadow, for macos/make_icon.swift's icons
    ios/.../Assets.xcassets/Doodles                   the same doodles as image sets, for the iPhone app's Doodle
    web/src/ui/doodles.ts                             the doodles' paths, drawn inline by <Doodle> in the web app

Only numpy. Run it again after changing a doodle; nothing it writes is edited by hand.
"""
import json
import math
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
drawing = json.load(open(os.path.join(HERE, 'drawing.json')))
parts = drawing['parts']
strokes = {s['id']: s for k in ('egghead', 'chicklet', 'cloud') for s in drawing[k]}

# Line weight: his GoodNotes pen is about 0.55 pt on a 118-pt egg; drawn a little bolder so it holds at 100 px.
WEIGHT = 2.3
INK_W = 0.55

# Plain like his drawing: ink on an eggshell egg, just off white so he holds up on a white or cream page (the fried
# egg's yolk is the one colour).
LIGHT = {'ink': '#1d1d1f', 'fill': '#f3ead9', 'white': '#fffdf8', 'yolk': '#f6cf7e'}
DARK = {'ink': '#ebe4d6', 'fill': '#34343a', 'white': '#34343a', 'yolk': '#8a6a32'}


def his(*names):
    """Eli's strokes for the named parts (or stroke ids)."""
    out = []
    for n in names:
        for i in (parts[n] if isinstance(n, str) else [n]):
            s = strokes[i]
            out.append({'w': s['w'], 'pts': np.array(s['pts'], float)})
    return out


def centre(name):
    return np.concatenate([s['pts'] for s in his(name)]).mean(0)


def hull(p):
    """Convex hull (monotone chain), for the egg's fill."""
    p = sorted(map(tuple, p))
    def half(seq):
        h = []
        for q in seq:
            while len(h) >= 2 and (h[-1][0] - h[-2][0]) * (q[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (q[0] - h[-2][0]) <= 0:
                h.pop()
            h.append(q)
        return h
    lo, hi = half(p), half(reversed(p))
    return np.array(lo[:-1] + hi[:-1])


# ---- a hand's pen: an even line, nudged sideways by slow noise, the way a pen drifts ----

def resample(p, step=0.5):
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))]
    if d[-1] == 0:
        return p
    t = np.linspace(0, d[-1], max(2, int(d[-1] / step)))
    return np.c_[np.interp(t, d, p[:, 0]), np.interp(t, d, p[:, 1])]


def pen(p, seed, amp=0.3, w=INK_W):
    p = resample(np.asarray(p, float))
    n = len(p)
    if n < 3:
        return {'w': w, 'pts': p}
    rng = np.random.RandomState(seed)
    length = np.linalg.norm(np.diff(p, axis=0), axis=1).sum()
    t = np.linspace(0, 1, n)
    wobble = sum(amp / k * np.sin(2 * math.pi * (k * max(1, length / 30)) * t + rng.uniform(0, 6.3)) for k in (1, 2, 3))
    tang = np.gradient(p, axis=0)
    tang /= np.maximum(1e-9, np.linalg.norm(tang, axis=1))[:, None]
    normal = np.c_[-tang[:, 1], tang[:, 0]]
    return {'w': w * rng.uniform(0.9, 1.1), 'pts': p + normal * wobble[:, None]}


def arc(c, rx, ry, a0, a1, n=40):
    a = np.linspace(math.radians(a0), math.radians(a1), n)
    return np.c_[c[0] + rx * np.cos(a), c[1] + ry * np.sin(a)]


def loop(c, r, seed):
    """A circle as a hand closes one: a little past the start, the radius drifting."""
    a0 = np.random.RandomState(seed).uniform(0, 360)
    a = np.linspace(math.radians(a0), math.radians(a0 + 385), 60)
    rr = r * (1 + 0.07 * np.linspace(-1, 1, 60))
    return np.c_[c[0] + rr * np.cos(a), c[1] + rr * np.sin(a)]


def spiral(c, r, turns=2.2):
    a = np.linspace(0, 2 * math.pi * turns, 90)
    rr = r * a / a[-1]
    return np.c_[c[0] + rr * np.cos(a), c[1] + rr * np.sin(a)]


def blob(c, r, seed, lobes=((3, 0.12), (5, 0.07))):
    rng = np.random.RandomState(seed)
    a = np.linspace(0, 2 * math.pi, 120)
    rr = r * (1 + sum(k * np.sin(f * a + rng.uniform(0, 6.3)) for f, k in lobes))
    return np.c_[c[0] + rr * np.cos(a), c[1] + rr * np.sin(a) * 0.8]


# ---- where his face is ----

EGG = np.concatenate([s['pts'] for s in his('egg')])
EGG_HULL = hull(EGG)
EYE_L, EYE_R = (np.array(strokes[i]['pts']).mean(0) for i in parts['eyes'])
MOUTH = centre('mouth')
TOP = EGG[:, 1].min()
HEAD = ['egg', 'glasses', 'bow']


def doodle_hello():
    skip = set(parts['bubbles'])
    ink = [{'w': s['w'], 'pts': np.array(s['pts'], float)} for s in drawing['egghead'] if s['id'] not in skip]
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_head():
    return {'fills': [('fill', EGG_HULL)], 'ink': his(*HEAD, 'eyes', 'brows', 'mouth')}


def doodle_chicklet():
    # Drawn about a quarter the egghead's size with the same pen, so its line is thinned to match once scaled up.
    ink = [{'w': s['w'] * 0.45, 'pts': np.array(s['pts'], float)} for s in drawing['chicklet']]
    return {'fills': [], 'ink': ink}


def doodle_thinking():
    ink = his(*HEAD, 'eyes', 'brows', 'mouth')
    # His cloud (π, Σ, ∫ and friends), smaller, just off the top of his head on the right, two bubbles leading to it.
    cloud = [np.array(s['pts'], float) for s in drawing['cloud']]
    allc = np.concatenate(cloud)
    mid = (allc.min(0) + allc.max(0)) / 2
    to = np.array([EGG[:, 0].max() + 26, TOP - 30])
    ink += [{'w': s['w'] * 0.85, 'pts': (p - mid) * 0.7 + to} for s, p in zip(drawing['cloud'], cloud)]
    right = EGG[:, 0].max()
    ink.append(pen(loop([right - 2, TOP + 1], 1.6, 80), 80, amp=0.03))
    ink.append(pen(loop([right + 4, TOP - 6], 2.4, 81), 81, amp=0.05))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_sleepy():
    ink = his(*HEAD)
    for k, e in enumerate((EYE_L, EYE_R)):
        ink.append(pen(arc(e + [0, -1.5], 5, 3.2, 15, 165), 10 + k))
    ink.append(pen(loop(MOUTH + [0, 2], 3, 3), 3))
    x, y = EGG[:, 0].max() + 6, TOP + 4
    for k, (s, dx, dy) in enumerate(((11, 0, 0), (7, 14, -15))):
        z = np.array([[0, 0], [s, 0], [0, s], [s, s]]) + [x + dx, y + dy]
        ink.append(pen(z, 20 + k, amp=0.15, w=INK_W * 1.3))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_cracked():
    ink = his(*HEAD, 'eyes')
    for k, (e, sgn) in enumerate(((EYE_L, 1), (EYE_R, -1))):
        ink.append(pen([e + [-6 * sgn, -8], e + [5 * sgn, -12]], 30 + k, amp=0.2))
    ink.append(pen(MOUTH + np.c_[np.linspace(-7, 7, 30), 1.4 * np.sin(np.linspace(0, 3 * math.pi, 30))], 33, amp=0.1))
    # The crack: in from the egg's right side below the glasses, jagged, heavier than the rest.
    side = EGG[(EGG[:, 1] > MOUTH[1] - 2) & (EGG[:, 1] < MOUTH[1] + 4)]
    edge = side[np.argmax(side[:, 0])]
    crack = np.array([[0, 0], [-6, -3], [-9, 3], [-15, -1], [-18, 4], [-23, 2]]) + edge + [1, 0]
    ink.append(pen(crack, 34, amp=0.08, w=INK_W * 1.5))
    x0 = EGG[:, 0].max() + 6
    drop = np.r_[[[x0, TOP + 10]], arc([x0, TOP + 19], 3.6, 3.6, -40, 220, 30), [[x0, TOP + 10]]]
    ink.append(pen(drop, 35, amp=0.05, w=INK_W * 1.2))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_scrambled():
    ink = his(*HEAD)
    for k, e in enumerate((EYE_L, EYE_R)):
        ink.append(pen(spiral(e + [0, -1], 7), 40 + k, amp=0.12, w=INK_W * 1.2))
    ink.append(pen(MOUTH + np.c_[np.linspace(-8, 8, 40), 1.8 * np.sin(np.linspace(0, 4 * math.pi, 40))], 43, amp=0.1))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_search():
    ink = his(*HEAD, 'eyes', 'brows', 'mouth')
    right = EGG[:, 0].max()
    lens = np.array([right + 10, MOUTH[1] + 4])
    ink.append(pen(loop(lens, 12, 50), 50, amp=0.15, w=INK_W * 1.3))
    ink.append(pen([lens + [8.5, 8.5], lens + [20, 21]], 51, amp=0.1, w=INK_W * 2.2))
    ink.append(pen([[right - 3, MOUTH[1] + 22], [right + 4, MOUTH[1] + 21], lens + [-3, 11.5]], 52, amp=0.2))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink}


def doodle_overeasy():
    """A fried egg, sunny side up, glasses on its yolk: all caught up, assignments over easy."""
    c = np.array([0.0, 0.0])
    white = blob(c, 46, 60)
    yolk_c = c + [5, -4]
    yolk = loop(yolk_c, 17, 61)
    ink = [pen(np.r_[white, white[:3]], 60, amp=0.25), pen(yolk, 61, amp=0.12)]
    for k, dx in enumerate((-7, 7)):
        lens = np.array([[-5, -4], [5, -4.5], [5.3, 4], [-4.7, 4.3], [-5, -4]]) + yolk_c + [dx, -3]
        ink.append(pen(lens, 62 + k, amp=0.1, w=INK_W * 1.3))
        ink.append(pen(loop(yolk_c + [dx + 0.5, -2.5], 0.7, 64 + k), 64 + k, amp=0.02, w=INK_W * 1.6))
    ink.append(pen([yolk_c + [-2, -4], yolk_c + [2, -4.3]], 66, amp=0.05))
    ink.append(pen(arc(yolk_c + [0.5, 5], 5, 3, 20, 160), 67, amp=0.08))
    for k, (x, y) in enumerate(((-40, -36), (36, -40), (44, 30))):
        ink.append(pen([[x, y], [x + 3 * np.sign(x), y - 4]], 70 + k, amp=0.05))
    return {'fills': [('white', white), ('yolk', yolk)], 'ink': ink}


def rotate(p, about, deg):
    a = math.radians(deg)
    r = np.array([[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]])
    return (np.asarray(p) - about) @ r.T + about


def doodle_writing():
    """Busy writing notes: eyes down on a notepad, his own pencil in his hand. The pencil and hand move."""
    ink = his(*HEAD, 'mouth')
    for k, e in enumerate((EYE_L, EYE_R)):
        ink.append(pen(loop(e + [2.5, 4.5], 1.1, 90 + k), 90 + k, amp=0.02, w=INK_W * 2.2))
    right, bottom = EGG[:, 0].max(), EGG[:, 1].max()
    pad = np.array([[right - 18, bottom - 30], [right + 26, bottom - 34], [right + 30, bottom + 2], [right - 15, bottom + 5]])
    ink.append(pen(np.r_[pad, pad[:1]], 92, amp=0.15))
    for k, y in enumerate((-24, -17, -10)):
        a, b = pad[0] + [5, y + 30 + 2], pad[0] + [30 - 6 * k, y + 28 + 2]
        ink.append(pen([a, b], 93 + k, amp=0.25, w=INK_W * 0.8))
    # His pencil (point up in the drawing) turned to write, its point on the pad's last line.
    pencil = his('pencil')
    allp = np.concatenate([s['pts'] for s in pencil])
    mid = (allp.min(0) + allp.max(0)) / 2
    tip_was = allp[np.argmin(allp[:, 1])]
    tip_at = pad[0] + [30, 20]
    shift = tip_at - rotate([tip_was], mid, 205)[0]
    # Turned, its point on the pad, and drawn at a little over half his size so it fits his hand.
    moving = [{'w': s['w'], 'pts': (rotate(s['pts'], mid, 205) + shift - tip_at) * 0.55 + tip_at} for s in pencil]
    top = (rotate([[mid[0], allp[:, 1].max()]], mid, 205)[0] + shift - tip_at) * 0.55 + tip_at
    grip = tip_at + (top - tip_at) * 0.35
    shoulder = np.array([right - 3, MOUTH[1] + 8])
    moving.append(pen([shoulder, (shoulder + grip) / 2 + [2, 5], grip], 96, amp=0.2))
    moving.append(pen(loop(grip, 2.4, 97), 97, amp=0.05))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink, 'moving': moving}


def doodle_listening():
    """Recording: headphones on, eyes shut, smiling; the sound comes in from both sides (the sound moves)."""
    ink = his(*HEAD)
    for k, e in enumerate((EYE_L, EYE_R)):
        ink.append(pen(arc(e + [0, 1.5], 4.6, 3.4, 195, 345), 100 + k))
    ink.append(pen(arc(MOUTH + [1, -1.5], 6, 3.6, 25, 155), 102))
    left, right = EGG[:, 0].min(), EGG[:, 0].max()
    cx, cy = (left + right) / 2, EYE_L[1] - 2
    ink.append(pen(arc([cx, cy], (right - left) / 2 + 4, cy - TOP + 9, 192, 348, 60), 103, amp=0.25, w=INK_W * 1.6))
    for k, x in enumerate((left - 2, right + 2)):
        ink.append(pen([[x, cy - 9], [x, cy + 9]], 104 + k, amp=0.05, w=INK_W * 9))
    moving = []
    for k, (x, sgn) in enumerate(((left - 10, -1), (right + 10, 1))):
        for j, r in enumerate((6, 11)):
            a0 = 145 if sgn < 0 else -35
            moving.append(pen(arc([x - sgn * 6, cy], r, r * 1.2, a0, a0 + 70, 20), 106 + 2 * k + j, amp=0.08))
    return {'fills': [('fill', EGG_HULL)], 'ink': ink, 'moving': moving}


DOODLES = {
    'hello': doodle_hello, 'head': doodle_head, 'chicklet': doodle_chicklet, 'thinking': doodle_thinking,
    'sleepy': doodle_sleepy, 'cracked': doodle_cracked, 'scrambled': doodle_scrambled, 'search': doodle_search,
    'overeasy': doodle_overeasy, 'writing': doodle_writing, 'listening': doodle_listening,
}


# ---- writing them out ----

def thin(p, min_step=0.45):
    keep = [p[0]]
    for q in p[1:-1]:
        if np.linalg.norm(q - keep[-1]) >= min_step:
            keep.append(q)
    keep.append(p[-1])
    return np.array(keep)


def path_d(p, close=False):
    p = thin(p)
    d = 'M' + ' '.join(f'{x:.1f} {y:.1f}' for x, y in p[:1]) + 'L' + ' '.join(f'{x:.1f} {y:.1f}' for x, y in p[1:])
    return d + ('Z' if close else '')


def build(name):
    art = DOODLES[name]()
    art.setdefault('moving', [])
    allp = np.concatenate([s['pts'] for s in art['ink'] + art['moving']] + [f for _, f in art['fills']])
    pad = 3
    lo, hi = allp.min(0) - pad, allp.max(0) + pad
    shift = -lo
    fills = [(kind, path_d(f + shift, close=True)) for kind, f in art['fills']]
    inks = [(path_d(s['pts'] + shift), round(max(0.35, s['w']) * WEIGHT, 2)) for s in art['ink']]
    moving = [(path_d(s['pts'] + shift), round(max(0.35, s['w']) * WEIGHT, 2)) for s in art['moving']]
    w, h = hi - lo
    return {'vb': f'0 0 {w:.0f} {h:.0f}', 'w': w, 'h': h, 'fills': fills, 'inks': inks, 'moving': moving}


def svg(d, colors):
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{d["vb"]}" width="{d["w"]:.0f}" height="{d["h"]:.0f}">']
    for kind, path in d['fills']:
        out.append(f'<path d="{path}" fill="{colors[kind]}"/>')
    out.append(f'<g fill="none" stroke="{colors["ink"]}" stroke-linecap="round" stroke-linejoin="round">')
    for path, w in d['inks']:
        out.append(f'<path d="{path}" stroke-width="{w}"/>')
    if d['moving']:
        out.append('<g id="moving">')
        for path, w in d['moving']:
            out.append(f'<path d="{path}" stroke-width="{w}"/>')
        out.append('</g>')
    out.append('</g></svg>')
    return '\n'.join(out) + '\n'


def main():
    art_dir = os.path.join(ROOT, 'assets', 'egghead')
    os.makedirs(art_dir, exist_ok=True)
    built = {name: build(name) for name in DOODLES}
    for name, d in built.items():
        open(os.path.join(art_dir, f'{name}.svg'), 'w').write(svg(d, LIGHT))
        open(os.path.join(art_dir, f'{name}-dark.svg'), 'w').write(svg(d, DARK))

    # The iPhone app's: each doodle an image set with its dark drawing, kept as vectors.
    ios = os.path.join(ROOT, 'ios', 'StudyStash', 'Resources', 'Assets.xcassets', 'Doodles')
    os.makedirs(ios, exist_ok=True)
    open(os.path.join(ios, 'Contents.json'), 'w').write('{\n  "info" : { "author" : "xcode", "version" : 1 },\n  "properties" : { "provides-namespace" : false }\n}\n')
    for name, d in built.items():
        folder = os.path.join(ios, f'doodle-{name}.imageset')
        os.makedirs(folder, exist_ok=True)
        open(os.path.join(folder, f'{name}.svg'), 'w').write(svg(d, LIGHT))
        open(os.path.join(folder, f'{name}-dark.svg'), 'w').write(svg(d, DARK))
        open(os.path.join(folder, 'Contents.json'), 'w').write(
            '{\n  "images" : [\n'
            f'    {{ "filename" : "{name}.svg", "idiom" : "universal" }},\n'
            f'    {{ "appearances" : [ {{ "appearance" : "luminosity", "value" : "dark" }} ], "filename" : "{name}-dark.svg", "idiom" : "universal" }}\n'
            '  ],\n  "info" : { "author" : "xcode", "version" : 1 },\n'
            '  "properties" : { "preserves-vector-representation" : true, "template-rendering-intent" : "original" }\n}\n')

    # The icons' head: his strokes in his own page points; make_icon.swift fits them to the tile.
    def strokes_of(*names):
        return [{'w': s['w'], 'pts': [[round(x, 2), round(y, 2)] for x, y in thin(s['pts'], 0.3)]} for s in his(*names)]
    # The menu bar's: too small for his sketchy passes (they blur to grey), so one clean line each, taken from his:
    # the egg's outline smoothed, each lens as the box its strokes fill, the eyes where he put them.
    def box(*ids):
        p = np.concatenate([np.array(strokes[i]['pts']) for i in ids])
        return [round(v, 1) for v in (*p.min(0), *p.max(0))]
    smooth = EGG_HULL
    for _ in range(3):  # Chaikin: corners cut, the hull's facets gone
        nxt = np.roll(smooth, -1, axis=0)
        smooth = np.c_[0.75 * smooth + 0.25 * nxt, 0.25 * smooth + 0.75 * nxt].reshape(-1, 2)
    menubar = {
        'egg': [[round(x, 2), round(y, 2)] for x, y in thin(smooth, 1.0)],
        'lenses': [box(62, 152), box(88, 7)],
        'eyes': [[round(v, 1) for v in EYE_L], [round(v, 1) for v in EYE_R]],
    }
    # The app icon's shadow: four loose pen strokes under the egg, shorter as they go down.
    left, right, bottom = EGG[:, 0].min(), EGG[:, 0].max(), EGG[:, 1].max()
    cx, half = (left + right) / 2, (right - left) / 2
    rng = np.random.RandomState(7)
    shadow = []
    for k in range(4):
        y, w = bottom + 3 + k * 3.4, half * (1.0 - 0.2 * k)
        line = pen([[cx - w, y], [cx + w, y + rng.uniform(-0.6, 0.6)]], 300 + k, amp=0.35, w=INK_W * 0.7)
        shadow.append({'w': round(line['w'], 3), 'pts': [[round(x, 2), round(y, 2)] for x, y in thin(line['pts'], 0.3)]})
    mark = {
        'shadow': shadow,
        'menubar': menubar,
        'egg': [[round(x, 2), round(y, 2)] for x, y in EGG_HULL],
        'full': strokes_of(*HEAD, 'eyes', 'brows', 'mouth'),
        'small': strokes_of('egg', 'glasses'),
    }
    json.dump(mark, open(os.path.join(art_dir, 'mark.json'), 'w'), separators=(',', ':'))

    ts = ['// Egghead Notes\' doodles, drawn from Eli\'s egghead (tools/egghead/make_art.py writes this file; don\'t edit it).',
          '// Each is a viewBox, its fills (the egg in --egg-fill, a fried egg\'s white and yolk in --egg-white and --egg-yolk),',
          '// its ink strokes, and'
          '// the strokes that move while he\'s busy (a pencil writing, sound coming in).',
          '',
          'export type DoodleName = ' + ' | '.join(f"'{n}'" for n in built) + ';',
          '',
          'export interface DoodleArt {',
          '  vb: string;',
          "  fills: ['fill' | 'white' | 'yolk', string][];",
          '  inks: [string, number][];',
          '  moving: [string, number][];',
          '}',
          '',
          'export const DOODLES: Record<DoodleName, DoodleArt> = {']
    for name, d in built.items():
        ts.append(f'  {name}: {{')
        ts.append(f"    vb: '{d['vb']}',")
        ts.append('    fills: [' + ', '.join(f"['{k}', '{p}']" for k, p in d['fills']) + '],')
        ts.append('    inks: [')
        for p, w in d['inks']:
            ts.append(f"      ['{p}', {w}],")
        ts.append('    ],')
        ts.append('    moving: [' + ''.join(f"\n      ['{p}', {w}]," for p, w in d['moving']) + ('\n    ],' if d['moving'] else '],'))
        ts.append('  },')
    ts.append('};')
    open(os.path.join(ROOT, 'web', 'src', 'ui', 'doodles.ts'), 'w').write('\n'.join(ts) + '\n')
    print('wrote', len(built), 'doodles to', os.path.relpath(art_dir, ROOT))


if __name__ == '__main__':
    main()
