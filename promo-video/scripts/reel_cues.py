#!/usr/bin/env python3
"""The reel's ElevenLabs prompt sheet: ELEVENLABS-reel.md, from its plan (src/reel/plan.json), whose times are final.

    npm run reel-cues        (python3 scripts/reel_cues.py)

At the top Adam's whole script, one block to paste into Text to Speech, with his settings; then each sound effect's
Sound Effects prompt, length and file name; then when each line starts and each effect plays in out/reel-silent.mp4,
for placing them in an editor. Frames are counted as the video counts them (src/reel/config.ts): round(seconds × 30).
"""
import json
import math
import pathlib
import sys

sys.dont_write_bytecode = True
from demo_cues import cell, clock, secs  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
PLAN = ROOT / 'src' / 'reel' / 'plan.json'
OUT = ROOT / 'ELEVENLABS-reel.md'
NAMES = {
    'opener': 'Opener', 'record': 'Record', 'notes': 'Notes', 'explore': 'Explore', 'plots': 'Plots',
    'drawings': 'Drawings', 'ask': 'Ask', 'canvas': 'Canvas', 'phone': 'Phone', 'settings': 'Settings', 'end': 'End card',
}


def build():
    plan = json.loads(PLAN.read_text())
    fps = plan['fps']
    frame = lambda s: round(s * fps)
    total = frame(plan['end'])
    scenes = plan['scenes']
    order = [s['id'] for s in scenes]
    start = {s['id']: frame(s['start']) for s in scenes}
    nxt = {sid: (start[order[i + 1]] if i + 1 < len(order) else total) for i, sid in enumerate(order)}
    sounds = {s['sfx']: s for s in plan['sounds']}
    effects = sorted((dict(e, frame=frame(e['at']), plays=math.ceil(e['len'] * fps - 1e-9) if e.get('len') else None)
                      for e in plan['effects']), key=lambda e: e['frame'])
    sa = plan['safeArea']

    L = []
    w = L.append
    w('# ElevenLabs prompts: the Instagram reel')
    w('')
    w(f'For **`out/reel-silent.mp4`**: 1080×1920, {fps} fps, **{clock(total, fps)}** ({total} frames), picture only (no '
      'audio stream). The times below are its final timeline.')
    w('')

    # ---- Adam ----
    w('## 1. Adam\'s script (Text to Speech)')
    w('')
    w('**Voice:** Adam ("Engaging, Friendly and Bright") · **Model:** Multilingual v2 · **Speed:** 0.95 · '
      '**Stability:** 85% · **Similarity:** 75% · **Style:** 40%')
    w('')
    w('Paste it as one block; each line is its own paragraph, so Adam pauses between them. Then cut the take into its '
      f'{len(scenes)} lines and place each at its time in part 3.')
    w('')
    w('```text')
    w('\n\n'.join(s['line'] for s in scenes))
    w('```')
    w('')

    # ---- Effects ----
    w('## 2. Sound effects (Sound Effects)')
    w('')
    w('Soft, well under the voice; no bells or chimes anywhere. Paste the prompt, set the length, generate a few and keep '
      'the softest, cleanest one. They are the same nine files as the demo\'s.')
    w('')
    used = {sfx: [e for e in effects if e['sfx'] == sfx] for sfx in sounds}
    for sfx, snd in sounds.items():
        if not used[sfx]:
            continue
        n = len(used[sfx])
        w(f'### `{snd["file"]}`')
        w('')
        w(f'**Length:** {snd["length"]} · used {n} time{"s" if n != 1 else ""}')
        w('')
        w('```text')
        w(snd['prompt'])
        w('```')
        w('')

    # ---- Timestamps ----
    w('## 3. Where everything goes')
    w('')
    w(f'Times are when each one **starts**, as m:ss.s and as the frame at {fps} fps. A line\'s **window** is how long it '
      'can run before the next scene starts.')
    w('')
    w('### Adam\'s lines')
    w('')
    w('| # | Starts | Frame | Window | Scene | Line |')
    w('|---|---|---|---|---|---|')
    for i, s in enumerate(scenes):
        vo = frame(s['vo'])
        w(f'| {i + 1} | {clock(vo, fps)} | {vo} | {secs(nxt[s["id"]] - vo, fps)} | {NAMES[s["id"]]} | {cell(s["line"])} |')
    w('')
    w('### Sound effects')
    w('')
    w('| Starts | Frame | File | Moment |')
    w('|---|---|---|---|')
    for e in effects:
        plays = f' (let it play {secs(e["plays"], fps)}, then fade)' if e['plays'] else ''
        w(f'| {clock(e["frame"], fps)} | {e["frame"]} | `{sounds[e["sfx"]]["file"]}` | {NAMES[e["scene"]]}: {cell(e["what"])}{plays} |')
    w('')
    w('Scenes start at: ' + ', '.join(f'{NAMES[sid]} {clock(start[sid], fps)}' for sid in order) + f'; the video ends at '
      f'{clock(total, fps)}. If you add any text in the editor, keep it out of the top {sa["top"]} px, the bottom '
      f'{sa["bottom"]} px and the right {sa["right"]} px, where Instagram draws its own.')
    w('')
    return '\n'.join(L)


if __name__ == '__main__':
    OUT.write_text(build())
    print(f'written: {OUT.relative_to(ROOT)}')
