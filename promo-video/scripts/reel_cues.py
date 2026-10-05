#!/usr/bin/env python3
"""The reel's cue sheet: CUES-reel.md, from its plan (src/reel/plan.json), whose times are final.

    npm run reel-cues        (python3 scripts/reel_cues.py)

Like the demo's (scripts/demo_cues.py): every scene's start and what's on screen, every line of Adam's (its exact words,
when it starts and how long it may run), and every sound effect (its file and the moment it marks), in one table in
order, then grouped by file with each effect's ElevenLabs prompt and length. Frames are counted as the video counts
them (src/reel/config.ts): round(seconds × 30).
"""
import json
import math
import pathlib

from demo_cues import cell, clock, secs

ROOT = pathlib.Path(__file__).resolve().parent.parent
PLAN = ROOT / 'src' / 'reel' / 'plan.json'
OUT = ROOT / 'CUES-reel.md'
NAMES = {
    'opener': 'Opener', 'record': 'Record', 'notes': 'Notes first', 'explore': 'Explore', 'plots': 'Plots',
    'drawings': 'Drawings', 'ask': 'Ask', 'canvas': 'Canvas', 'phone': 'Phone', 'settings': 'Settings', 'end': 'End card',
}


def build():
    plan = json.loads(PLAN.read_text())
    fps, tr = plan['fps'], plan['transition']
    frame = lambda s: round(s * fps)
    total = frame(plan['end'])
    scenes = plan['scenes']
    order = [s['id'] for s in scenes]
    start = {s['id']: frame(s['start']) for s in scenes}
    vo = {s['id']: frame(s['vo']) for s in scenes}
    nxt = {sid: (start[order[i + 1]] if i + 1 < len(order) else total) for i, sid in enumerate(order)}
    sounds = {s['sfx']: s for s in plan['sounds']}
    effects = [dict(e, frame=frame(e['at']), plays=math.ceil(e['len'] * fps - 1e-9) if e.get('len') else None) for e in plan['effects']]
    sa = plan['safeArea']
    headline = lambda sc: f' Headline: “{sc["title"][0]} {sc["title"][1]}”' if sc.get('title') else ''

    L = []
    w = L.append
    w('# Cue sheet: the Instagram reel, picture only')
    w('')
    w(f'For **`out/reel-silent.mp4`** (1080×1920, {fps} fps, no audio stream): the reel\'s picture alone, for laying Adam\'s '
      'narration and the sound effects under it in an editor. Made by `npm run render:reel:silent`.')
    w('')
    w(f'> **Safe area, for whoever edits.** Instagram draws over the top {sa["top"]} px (its bar), the bottom {sa["bottom"]} px '
      f'(the caption, the audio line and the buttons) and the right {sa["right"]} px (like, comment, share) of the '
      f'{sa["width"]}×{sa["height"]} frame. Every headline and everything that matters in the reel sits inside, in x 0–'
      f'{sa["width"] - sa["right"]}, y {sa["top"]}–{sa["height"] - sa["bottom"]}. Keep anything you add (captions, stickers, '
      'text) in there too. `ReelGuides` in the Studio (`npm run studio`) plays the reel with those areas shaded.')
    w('')
    w(f'- **Total length: {clock(total, fps)}** ({total} frames). Instagram\'s limit for this is under a minute.')
    w('- Generated from `src/reel/plan.json` by `npm run reel-cues`. Don\'t edit it by hand: change the plan, run it '
      'again and render again. The plan\'s times are final: nothing in the reel is stretched to fit a line, so the '
      'picture never moves under what you lay by this sheet.')
    w(f'- Every time is when the cue **starts**, as m:ss.s and as the frame at {fps} fps (frame ÷ {fps} = seconds).')
    w('- A line\'s **window** is the time from its start to the next scene\'s start: the most it can run. Leave a breath '
      '(about 0.3 s) at its end. Each line is meant to start about 0.3 s into its scene.')
    w('- The effects are the demo\'s nine files (same names, same prompts; no bells anywhere). Every pointer click has a '
      'click, every tap on the phone a tap. A *plays* time is how long that effect should last (a drag, the typing): '
      'trim or fade it there.')
    w('- No music is cued. If you add some, keep it well under the voice and fade it out over the last two seconds.')
    w('')

    # ---- Everything, in order ----
    rows = []
    for i, sid in enumerate(order):
        sc = scenes[i]
        rows.append((start[sid], 0, f'**Scene {i + 1}: {NAMES[sid]}**', f'{sc.get("shows", "")}.{headline(sc)}'))
    for e in effects:
        snd = sounds[e['sfx']]
        plays = f' (plays {secs(e["plays"], fps)})' if e['plays'] else ''
        rows.append((e['frame'], 2, f'Effect `{snd["file"]}`', f'{e["what"]}{plays}'))
    for i, sid in enumerate(order):
        rows.append((vo[sid], 3, f'**Adam, line {i + 1}**', f'“{scenes[i]["line"]}” Window {secs(nxt[sid] - vo[sid], fps)}, until {clock(nxt[sid], fps)}.'))
    rows.sort(key=lambda r: (r[0], r[1]))
    w('## Everything, in order')
    w('')
    w('| Time | Frame | Cue | What |')
    w('|---|---|---|---|')
    for f, _, cue, what in rows:
        w(f'| {clock(f, fps)} | {f} | {cell(cue)} | {cell(what)} |')
    w(f'| {clock(total, fps)} | {total} | **The end** | The last frame of the end card. |')
    w('')

    # ---- Adam's lines ----
    w('## Adam\'s lines')
    w('')
    w('| # | Scene | Starts | Frame | Window | Line | How to say it |')
    w('|---|---|---|---|---|---|---|')
    for i, sid in enumerate(order):
        sc = scenes[i]
        w(f'| {i + 1} | {NAMES[sid]} | {clock(vo[sid], fps)} | {vo[sid]} | {secs(nxt[sid] - vo[sid], fps)} | '
          f'{cell(sc["line"])} | {cell(sc.get("direction", ""))} |')
    w('')
    w('Adam\'s settings, as the demo: Adam ("Engaging, Friendly and Bright"), Multilingual v2, speed 0.95, stability 85%, '
      'similarity 75%, style 40% (VOICEOVER3.md). The reel moves faster than the demo, so if a line runs past its window, '
      'try speed 1.0 for that line rather than cutting words.')
    w('')
    w('All eleven lines, as one script (a pause between each):')
    w('')
    for sc in scenes:
        w(f'> {sc["line"]}')
        w('>')
    L.pop()
    w('')

    # ---- Effects by file ----
    w('## Sound effects, by file')
    w('')
    w('All soft, well under the voice. No bells and no chimes anywhere. On the ElevenLabs website: Sound Effects, paste '
      'the prompt, set the length, generate a few and keep the softest, cleanest one. If you already made them for the '
      'demo, they are the same files.')
    w('')
    for sfx, snd in sounds.items():
        hits = [e for e in effects if e['sfx'] == sfx]
        if not hits:
            continue
        w(f'### `{snd["file"]}`, {len(hits)} time{"s" if len(hits) != 1 else ""}')
        w('')
        w(f'**Prompt:** {snd["prompt"]}  ')
        w(f'**Length:** {snd["length"]}')
        w('')
        w('| Time | Frame | Moment |')
        w('|---|---|---|')
        for e in hits:
            plays = f' (plays {secs(e["plays"], fps)})' if e['plays'] else ''
            w(f'| {clock(e["frame"], fps)} | {e["frame"]} | {cell(e["what"])}{plays} |')
        w('')

    # ---- Scenes ----
    w('## Scenes')
    w('')
    w('| # | Scene | Starts | Frame | Length | Headline | On screen |')
    w('|---|---|---|---|---|---|---|')
    for i, sid in enumerate(order):
        sc = scenes[i]
        title = f'{sc["title"][0]} {sc["title"][1]}' if sc.get('title') else '(none)'
        w(f'| {i + 1} | {NAMES[sid]} | {clock(start[sid], fps)} | {start[sid]} | {secs(nxt[sid] - start[sid], fps)} | '
          f'{cell(title)} | {cell(sc.get("shows", ""))} |')
    w('')
    w(f'Each scene cross-fades into the next over {tr} frames ({tr / fps:.1f} s), starting at the next scene\'s time above. '
      f'The reel ends at {clock(total, fps)}.')
    w('')
    return '\n'.join(L)


if __name__ == '__main__':
    OUT.write_text(build())
    print(f'cues: {OUT.relative_to(ROOT)}')
