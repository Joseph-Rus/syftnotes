#!/usr/bin/env python3
"""The demo's cue sheet: CUES.md, from the final timeline (src/demo/timeline.json) and the plan (src/demo/plan.json).

    npm run demo-cues        (python3 scripts/demo_cues.py; `npm run demo-audio` runs it too)

It lists, at the exact time and frame they fall in the render, every scene's start (and what's on screen), every line
of Adam's (its words and how long it may run before the next scene), and every sound effect (its file, the moment it
marks, its ElevenLabs prompt and length), the optional music, all in one table in order and again grouped by file.
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
PLAN = ROOT / 'src' / 'demo' / 'plan.json'
TIMELINE = ROOT / 'src' / 'demo' / 'timeline.json'
OUT = ROOT / 'CUES.md'
NAMES = {
    'opener': 'Opener', 'setup': 'Setup', 'record': 'Record', 'notes': 'Notes first', 'explore': 'Explore',
    'plots': 'Plots', 'drawings': 'Drawings', 'ask': 'Ask', 'canvas': 'Canvas', 'phone': 'Phone',
    'settings': 'Settings', 'aiapps': 'AI apps', 'promise': 'Promise', 'end': 'End card',
}


def clock(frame, fps):
    """m:ss.s for a frame (to the nearest tenth of a second)."""
    tenths = round(frame / fps * 10)
    m, rest = divmod(tenths, 600)
    return f'{m}:{rest // 10:02d}.{rest % 10}'


def secs(frames, fps):
    return f'{frames / fps:.1f} s'


def cell(text):
    return str(text).replace('|', '\\|')


def build():
    plan = json.loads(PLAN.read_text())
    tl = json.loads(TIMELINE.read_text())
    fps, total = tl['fps'], tl['total']
    scenes = {s['id']: s for s in plan['scenes']}
    placed = tl['scenes']
    order = [s['id'] for s in placed]
    start = {s['id']: s['from'] for s in placed}
    nxt = {sid: (start[order[i + 1]] if i + 1 < len(order) else total) for i, sid in enumerate(order)}
    voice = {v['id']: v for v in tl['voice']}
    plan_fx = {e['key']: e for e in plan['effects']}
    sounds = {s['sfx']: s for s in plan['sounds']}
    music = plan.get('music')
    stretched = [(NAMES[s['id']], s['stretchedBy']) for s in placed if s.get('stretchedBy')]

    L = []
    w = L.append
    w('# Cue sheet: the demo, picture only')
    w('')
    w(f'For **`out/demo-silent-vertical.mp4`** (1080×1920, {fps} fps, no audio stream): the demo\'s picture alone, for laying '
      'Adam\'s narration and the sound effects under it in an editor. Made by `npm run render3:silent`.')
    w('')
    w(f'- **Total length: {clock(total, fps)}** ({total} frames).')
    w('- **One timeline for both orientations.** The vertical and the landscape renders are cut identically, frame for '
      'frame, from the same `src/demo/timeline.json`, so this sheet fits either.')
    w('- Generated from `src/demo/timeline.json` (the final times) and `src/demo/plan.json` (the words, moments and '
      'prompts) by `npm run demo-cues`. Don\'t edit it by hand: change the plan, run `npm run demo-audio` (which runs '
      'this too), and render again.')
    if stretched:
        parts = ', '.join(f'{n} +{s:.1f} s' for n, s in stretched)
        w(f'- The timeline is the plan\'s, except that when it was last fitted (to {tl.get("voiceSource", "a read")}) '
          f'these scenes were lengthened, on the beat, to give their line room: {parts}. Everything after a lengthened '
          'scene starts that much later than in the original list, its effects with it; the times below already include it.')
    w(f'- Every time is when the cue **starts**, as m:ss.s and as the frame at {fps} fps (frame ÷ {fps} = seconds).')
    w('- A line\'s **window** is the time from its start to the next scene\'s start: the most it can run. Leave a breath '
      '(about 0.4 s) at its end.')
    w('- Effects marked *added* are clicks and a tap the pointer makes on the way that weren\'t on the first list; they '
      'use the same file.')
    w('')

    # ---- Everything, in order ----
    rows = []
    for i, sid in enumerate(order):
        sc = scenes[sid]
        rows.append((start[sid], 0, f'**Scene {i + 1}: {NAMES[sid]}**', sc.get('shows', '')))
    if music:
        rows.append((0, 1, f'Music (optional) `{music["file"]}`', music.get('note', '')))
    for e in tl['effects']:
        pe = plan_fx[e['key']]
        snd = sounds[e['sfx']]
        plays = f' (plays {secs(e["frames"], fps)})' if e.get('frames') else ''
        tag = '' if e['listed'] else ' *added*'
        rows.append((e['from'], 2, f'Effect `{snd["file"]}`', f'{pe["what"]}{plays}{tag}'))
    for i, sid in enumerate(order):
        v = voice.get(sid)
        if not v:
            continue
        window = nxt[sid] - v['from']
        rows.append((v['from'], 3, f'**Adam, line {i + 1}**', f'“{scenes[sid]["line"]}” Window {secs(window, fps)}, until {clock(nxt[sid], fps)}.'))
    rows.sort(key=lambda r: (r[0], r[1]))
    w('## Everything, in order')
    w('')
    w('| Time | Frame | Cue | What |')
    w('|---|---|---|---|')
    for f, _, cue, what in rows:
        w(f'| {clock(f, fps)} | {f} | {cell(cue)} | {cell(what)} |')
    w('')

    # ---- Adam's lines ----
    w('## Adam\'s lines')
    w('')
    w('| # | Scene | Starts | Frame | Window | Line | How to say it |')
    w('|---|---|---|---|---|---|---|')
    for i, sid in enumerate(order):
        v = voice.get(sid)
        if not v:
            continue
        sc = scenes[sid]
        w(f'| {i + 1} | {NAMES[sid]} | {clock(v["from"], fps)} | {v["from"]} | {secs(nxt[sid] - v["from"], fps)} | '
          f'{cell(sc["line"])} | {cell(sc.get("direction", ""))} |')
    w('')
    w('Adam\'s settings, as the first two videos: Adam ("Engaging, Friendly and Bright"), Multilingual v2, speed 0.95, '
      'stability 85%, similarity 75%, style 40% (VOICEOVER3.md).')
    w('')

    # ---- Effects by file ----
    w('## Sound effects, by file')
    w('')
    w('All soft, well under the voice. No bells and no chimes anywhere. On the ElevenLabs website: Sound Effects, paste '
      'the prompt, set the length, generate a few and keep the softest, cleanest one.')
    w('')
    for sfx, snd in sounds.items():
        hits = [e for e in tl['effects'] if e['sfx'] == sfx]
        w(f'### `{snd["file"]}`, {len(hits)} time{"s" if len(hits) != 1 else ""}')
        w('')
        w(f'**Prompt:** {snd["prompt"]}  ')
        w(f'**Length:** {snd["length"]}')
        w('')
        w('| Time | Frame | Moment |')
        w('|---|---|---|')
        for e in hits:
            pe = plan_fx[e['key']]
            plays = f' (plays {secs(e["frames"], fps)})' if e.get('frames') else ''
            tag = '' if e['listed'] else ' *added*'
            w(f'| {clock(e["from"], fps)} | {e["from"]} | {cell(pe["what"])}{plays}{tag} |')
        w('')
    if music:
        w(f'### `{music["file"]}` (optional)')
        w('')
        w(f'**Prompt:** {music["prompt"]}  ')
        w(f'**Length:** the whole video, {clock(total, fps)} ({total} frames)')
        w('')
        w(f'| Time | Frame | Moment |')
        w('|---|---|---|')
        w(f'| 0:00.0 | 0 | {cell(music.get("note", ""))} |')
        w(f'| {clock(total, fps)} | {total} | The end of the video: fade it out over the last second or two. |')
        w('')

    # ---- Scenes ----
    w('## Scenes')
    w('')
    w('| # | Scene | Starts | Frame | Length | On screen |')
    w('|---|---|---|---|---|---|')
    for i, sid in enumerate(order):
        w(f'| {i + 1} | {NAMES[sid]} | {clock(start[sid], fps)} | {start[sid]} | {secs(nxt[sid] - start[sid], fps)} | '
          f'{cell(scenes[sid].get("shows", ""))} |')
    w('')
    w(f'Each scene cross-fades into the next over {tl["transition"]} frames ({tl["transition"] / fps:.1f} s), starting at '
      'the next scene\'s time above. The video ends at ' + clock(total, fps) + '.')
    w('')
    return '\n'.join(L)


if __name__ == '__main__':
    OUT.write_text(build())
    print(f'cues: {OUT.relative_to(ROOT)}')
