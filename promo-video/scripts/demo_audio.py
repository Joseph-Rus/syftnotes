#!/usr/bin/env python3
"""The third video's sound (the demo): its narration, effects and music, fitted to src/demo/plan.json.

    npm run demo-audio                        (python3 scripts/demo_audio.py; needs numpy, scipy and ffmpeg)
    npm run demo-audio -- --scratch           the Mac's own voice even if elevenlabs3/voice.mp3 is there
    npm run demo-audio -- --voice ~/take.mp3  another take
    npm run demo-audio -- --cuts 5.2,11.9,…   where lines 2…14 start in the take, in seconds (if the split is off)
    python3 scripts/demo_audio.py --test-split <take> <script.json>   only split a take, into a temporary folder

What it uses, from elevenlabs3/ (anything missing is made here instead):
  voice.mp3        ONE take of all 14 lines, a pause between each. Whisper (scripts/wordtimes, .NET 10) finds every
                   word; each line is cut in the silence just before its first word and keeps its whole tail.
  music.mp3        the music, trimmed to the video with a 2 s fade-out. Without it, the synthesised score at 120 BPM.
  sfx-<name>.mp3   whoosh-soft, click, record-start, pop, shimmer, slider, keys, tap, swell (.wav, .m4a … also work).

What it writes: public/audio/demo/vo/<scene>.wav, public/audio/demo/sfx/<name>.wav, public/audio/demo/music.wav and
src/demo/timeline.json, the final times the video plays (and CUES.md, its cue sheet). The plan is a contract: a scene is only lengthened (by whole
beats, 0.5 s) when its line doesn't fit, and everything after it moves by the same amount, effects included.
"""
import argparse
import difflib
import hashlib
import json
import math
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

try:
    import numpy as np
    from scipy.io import wavfile
except ImportError:  # the shell's python3 may lack numpy; the Mac's own has it
    if pathlib.Path('/usr/bin/python3').exists() and sys.executable != '/usr/bin/python3':
        os.execv('/usr/bin/python3', ['/usr/bin/python3', *sys.argv])
    raise

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import make_audio as ma  # noqa: E402  the score and the second video's soft effects

if not shutil.which('ffmpeg') and pathlib.Path('/opt/homebrew/bin/ffmpeg').exists():
    os.environ['PATH'] = '/opt/homebrew/bin:' + os.environ.get('PATH', '')

ROOT = ma.ROOT
PLAN = ROOT / 'src' / 'demo' / 'plan.json'
TIMELINE = ROOT / 'src' / 'demo' / 'timeline.json'
MINE = ROOT / 'elevenlabs3'
OUT = ROOT / 'public' / 'audio' / 'demo'
WORDTIMES = ROOT / 'scripts' / 'wordtimes'
MODEL = pathlib.Path.home() / '.study-stash' / 'models' / 'ggml-large-v3.bin'
SR = ma.SR
EXTS = ('mp3', 'wav', 'm4a', 'aac', 'flac', 'ogg')

LEVEL = -18.0      # each line's loudness, dB RMS (as the second video's)
PEAK = 0.89        # and its peak, at most
BEAT = 0.5         # seconds: 120 BPM, 15 frames
ROOM = 0.4         # seconds a line needs after it, before the next scene starts
END_HOLD = 2.0     # and the last line, before the video ends
HOP = 0.005        # the level is measured every 5 ms, over 20 ms
LEAD = 0.03        # a line is cut this long before its first sound
TAIL = 0.25        # and keeps this long after its last (above the noise floor), then a short fade
SAY_VOICES = ('Reed (English (US))', 'Daniel')
SAY_RATE = 170     # words a minute: about Adam's pace
SFX_PEAK = ma.SFX2_PEAK
SFX_LONGEST = {'whoosh-soft': 1.5, 'click': 0.2, 'record-start': 1.2, 'pop': 0.5, 'shimmer': 2.5, 'slider': 2.5,
               'keys': 3.0, 'tap': 0.25, 'swell': 6.0}  # seconds kept of a file of Joey's
ROLES = dict(reveal='opener', record='record', claps='explore', proof='promise', cta='end')
PROMPT = 'Study Stash: lectures, notes and diagrams. Canvas, Claude, Codex, Gemini.'  # spellings and punctuation


# ---- Sound in and out ------------------------------------------------------------------------------------------

def find(stem):
    return next((p for ext in EXTS for p in MINE.glob(f'{stem}.{ext}')), None)


def load(path, rate=SR, channels=2):
    """Any audio file as float samples (frames × channels), through ffmpeg."""
    with tempfile.TemporaryDirectory() as d:
        wav = pathlib.Path(d) / 'in.wav'
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', str(path), '-ar', str(rate), '-ac', str(channels),
                        '-c:a', 'pcm_s16le', str(wav)], check=True)
        _, x = wavfile.read(wav)
    x = x.astype(float) / 32768.0
    return x if x.ndim == 2 else x[:, None]


def save(path, x):
    """48 kHz stereo 16-bit, as it is (no levelling)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))


def levels(x):
    """dB RMS of x (frames × channels) over 20 ms, every 5 ms; frame k is centred on k·HOP s."""
    mono = x.mean(1) if x.ndim == 2 else x
    c = np.concatenate([[0.0], np.cumsum(mono ** 2)])
    h, w = int(HOP * SR), int(0.02 * SR)
    centre = np.arange(0, len(mono), h)
    a, b = np.clip(centre - w // 2, 0, len(mono)), np.clip(centre + w // 2, 0, len(mono))
    return 10 * np.log10((c[b] - c[a]) / np.maximum(b - a, 1) + 1e-10)


def floors(db):
    """The take's noise floor, what counts as quiet, and the low bar a line's tail is kept down to."""
    floor, top = np.percentile(db, 5), db.max()
    return floor, max(floor + 6, top - 60), max(floor + 8, top - 65)


def finish(y, tail_db=None):
    """A line as it's played: its trailing silence trimmed (only past TAIL), faded, at LEVEL dB RMS, peak ≤ PEAK."""
    db = levels(y)
    if tail_db is not None:
        loud = np.where(db > tail_db)[0]
        if len(loud):
            y = y[: min(len(y), int((loud[-1] * HOP + 0.01 + TAIL) * SR))]
    y = y.copy()
    n = min(len(y), int(0.01 * SR))
    y[:n] *= np.linspace(0, 1, n)[:, None]  # 10 ms in
    n = min(len(y), int(0.04 * SR))
    y[len(y) - n:] *= np.linspace(1, 0, n)[:, None]  # 40 ms out, over what is by now near-silence
    y = y * (10 ** (LEVEL / 20) / (np.sqrt(np.mean(y ** 2)) + 1e-9))
    return y * min(1.0, PEAK / (np.max(np.abs(y)) + 1e-9))


def tail_level(y, sec=0.15):
    return 20 * np.log10(np.sqrt(np.mean(y[-int(sec * SR):] ** 2)) + 1e-9)


# ---- The scratch voice: the Mac's own --------------------------------------------------------------------------

def say_voice():
    installed = subprocess.run(['say', '-v', '?'], capture_output=True, text=True).stdout
    return next((v for v in SAY_VOICES if re.search(rf'^{re.escape(v)}\s', installed, re.M)), None)


def say(text, voice):
    with tempfile.TemporaryDirectory() as d:
        aiff = pathlib.Path(d) / 'line.aiff'
        subprocess.run(['say', *(['-v', voice] if voice else []), '-r', str(SAY_RATE), '-o', str(aiff), text], check=True)
        return load(aiff)


def scratch_lines(scenes):
    voice = say_voice()
    out = {}
    for s in scenes:
        x = say(s['line'], voice)
        db = levels(x)
        floor, quiet, tail = floors(db)
        first = int(np.argmax(db > quiet))
        out[s['id']] = finish(x[int(max(0, first * HOP - LEAD) * SR):], tail)
    return out, f"scratch: macOS say, {voice or 'the default voice'}, {SAY_RATE} wpm"


# ---- A take: where every word is, from Whisper ---------------------------------------------------------------------

def dotnet():
    """A dotnet with the .NET 10 SDK: $DOTNET, the one on the PATH, or the usual places."""
    for c in (os.environ.get('DOTNET'), shutil.which('dotnet'), '~/study-stash-night/dotnet10/dotnet',
              '/usr/local/share/dotnet/dotnet', '~/.dotnet/dotnet', '/opt/homebrew/bin/dotnet'):
        if not c or not pathlib.Path(c).expanduser().exists():
            continue
        c = str(pathlib.Path(c).expanduser())
        sdks = subprocess.run([c, '--list-sdks'], capture_output=True, text=True).stdout
        if any(int(v.split('.')[0]) >= 10 for v in re.findall(r'^(\d+)\.\d+', sdks, re.M)):
            return c
    sys.exit('Splitting a take needs the .NET 10 SDK (for scripts/wordtimes). Set DOTNET=/path/to/dotnet, or give --cuts.')


def word_times(take, model=MODEL):
    """[{word, start, end}] for the take, from scripts/wordtimes; kept in the temp folder, so a rerun is instant."""
    model = pathlib.Path(model).expanduser()
    if not model.exists():
        sys.exit(f'No Whisper model at {model}. Give --cuts instead.')
    key = hashlib.sha1(pathlib.Path(take).read_bytes() + model.name.encode() + PROMPT.encode()).hexdigest()[:16]
    cache = pathlib.Path(tempfile.gettempdir()) / 'study-stash-demo-audio' / f'{key}.json'
    if cache.exists():
        return json.loads(cache.read_text())
    cache.parent.mkdir(parents=True, exist_ok=True)
    dn = dotnet()
    dll = WORDTIMES / 'bin' / 'Release' / 'net10.0' / 'WordTimes.dll'
    newest = max(p.stat().st_mtime for p in (WORDTIMES / 'Program.cs', WORDTIMES / 'WordTimes.csproj'))
    if not dll.exists() or dll.stat().st_mtime < newest:
        subprocess.run([dn, 'build', '-c', 'Release', str(WORDTIMES), '-nologo', '-v', 'q'], check=True, stdout=sys.stderr)
    with tempfile.TemporaryDirectory() as d:
        wav = pathlib.Path(d) / 'take16k.wav'
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', str(take), '-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', str(wav)], check=True)
        out = pathlib.Path(d) / 'words.json'
        subprocess.run([dn, str(dll), str(wav), str(model), '--out', str(out), '--prompt', PROMPT], check=True)
        cache.write_text(out.read_text())
    return json.loads(cache.read_text())


def norm(text):
    return re.sub(r'[^a-z0-9]', '', text.lower())


def align(lines, words):
    """For each line, (first, last) index into `words`, matched letter by letter, so a word heard as two (or two as one,
    or a little misheard) doesn't throw it."""
    script, line_of = '', []
    for k, text in enumerate(lines):
        t = norm(text)
        script += t
        line_of += [k] * len(t)
    heard, word_of = '', []
    for i, w in enumerate(words):
        t = norm(w['word'])
        heard += t
        word_of += [i] * len(t)
    found = [[] for _ in lines]
    for a, b, n in difflib.SequenceMatcher(None, script, heard, autojunk=False).get_matching_blocks():
        if n >= 3:  # one or two letters in common is chance
            for j in range(n):
                found[line_of[a + j]].append(word_of[b + j])
    spans, last = [], -1
    for k, f in enumerate(found):
        f = [i for i in f if i > last]
        if not f:
            sys.exit(f'Line {k + 1} ("{lines[k][:40]}…") isn\'t in what Whisper heard. Check the take, or give --cuts.')
        spans.append((f[0], f[-1]))
        last = f[-1]
    return spans


def cut_before(db, lo, hi, quiet_floor, first=False):
    """Where to cut before a line: in the longest stretch of quiet (≥ 50 ms) between `lo` (just after the previous
    line's last word starts) and `hi` (a little after Whisper's time for this line's first word, which runs late more
    often than early), LEAD before the sound after it starts. That stretch is the pause between the lines: a comma's
    pause or a stop consonant inside a word is shorter. A stretch running on past `hi` is followed to its end.
    Returns (seconds, ok)."""
    i0, i1 = max(0, int(lo / HOP)), min(len(db), int(hi / HOP) + 1)
    seg = db[i0:i1]
    quiet_db = max(seg.min() + 6, quiet_floor)
    quiet = seg <= quiet_db
    runs, i = [], 0
    while i < len(quiet):
        if quiet[i]:
            j = i
            while j < len(quiet) and quiet[j]:
                j += 1
            if j - i >= int(0.05 / HOP) or (first and i == 0 and i0 == 0):
                runs.append((i0 + i, i0 + j))
            i = j
        else:
            i += 1
    if not runs:
        return (0.0 if first else (i0 + int(np.argmin(seg))) * HOP), first
    a, b = max(runs, key=lambda r: (r[1] - r[0], r[1]))
    while b < len(db) and db[b] <= quiet_db:  # quiet on past the window: the sound starts later than Whisper said
        b += 1
    return max(a * HOP, (b - 1) * HOP + 0.01 - LEAD), True


def split(take, lines, cuts=None):
    """One take → a list of lines: each from the silence just before its first word up to the next line's cut, with
    its trailing silence trimmed only past TAIL. Prints where each was cut and why."""
    x = load(take)
    db = levels(x)
    floor, quiet_floor, tail_db = floors(db)
    dur = len(x) / SR
    warn = []
    if cuts:
        if len(cuts) != len(lines) - 1:
            sys.exit(f'--cuts needs {len(lines) - 1} times (where lines 2…{len(lines)} start); got {len(cuts)}.')
        first = int(np.argmax(db > quiet_floor))
        starts = [max(0.0, first * HOP - LEAD)] + list(cuts)
        heard = [None] * len(lines)
    else:
        words = word_times(take)
        spans = align(lines, words)
        heard = [(words[a], words[b]) for a, b in spans]  # each line's first and last word
        starts = []
        for k, (fw, _) in enumerate(heard):
            lo = max(starts[-1] + 0.2, heard[k - 1][1]['start'] + 0.05, fw['start'] - 3.0) if k else 0.0
            at, ok = cut_before(db, lo, fw['start'] + 0.2, quiet_floor, first=k == 0)
            if not ok:
                warn.append(f"no pause found before line {k + 1}; cut at its quietest point, {db[int(at / HOP)] - floor:.0f} dB over the floor")
            starts.append(at)
    out = []
    for k, at in enumerate(starts):
        end = starts[k + 1] if k + 1 < len(starts) else dur
        y = finish(x[int(at * SR):int(end * SR)], tail_db)
        level_at = db[min(len(db) - 1, int(at / HOP))]
        if at > 0 and level_at > max(floor + 15, quiet_floor + 6):
            warn.append(f'line {k + 1} is cut where there is sound ({level_at - floor:.0f} dB over the floor) at {at:.2f} s')
        if heard[k]:
            fw = heard[k][0]
            if at > fw['start'] + 0.15 or (k and at < heard[k - 1][1]['start']):
                warn.append(f"line {k + 1}'s cut at {at:.2f} s may be among the words (Whisper has \"{fw['word']}\" at {fw['start']:.2f} s): check it")
        out.append(dict(at=at, until=at + len(y) / SR, audio=y, heard=heard[k]))
    print(f'take: {take} ({dur:.1f} s); noise floor {floor:.0f} dB, quiet below {quiet_floor:.0f} dB, tails kept down to {tail_db:.0f} dB')
    print(f"  {'line':<10}{'cut at':>8}{'ends':>8}{'1st word':>9}{'length':>8}  first … last word")
    for lid, o in zip(lines.ids, out):
        h = o['heard']
        words_txt = f"{h[0]['word']} … {h[1]['word']}" if h else '(from --cuts)'
        first_at = f"{h[0]['start']:.2f}" if h else '-'
        print(f"  {lid:<10}{o['at']:8.2f}{o['until']:8.2f}{first_at:>9}{len(o['audio']) / SR:7.2f}s  {words_txt}")
    for w in warn:
        print(f'  WARNING: {w}')
    return out


class Lines(list):
    """The lines' texts, carrying their ids for the report."""
    def __init__(self, items):
        super().__init__(t for _, t in items)
        self.ids = [i for i, _ in items]


# ---- Effects ----------------------------------------------------------------------------------------------------

def seeded(seed, fn, *a, **k):
    """Runs one of make_audio's builders on its own random numbers, so nothing else it makes changes."""
    keep, ma.rng = ma.rng, np.random.default_rng(seed)
    try:
        return fn(*a, **k)
    finally:
        ma.rng = keep


def made_effects():
    """Soft, rounded, dry placeholders; no bells or chimes anywhere."""
    base = seeded(7, ma.sfx2)  # the second video's whoosh, click, pop and key
    own = np.random.default_rng(41)
    t_ = ma.times
    out = {'whoosh-soft': base['whoosh'], 'click': base['click'], 'pop': base['pop']}

    # Recording starts: a soft, low "thup" that falls a little in pitch, a breath of air on its front, no ring.
    t = t_(0.3)
    ph = 2 * np.pi * np.cumsum(260 + 160 * np.exp(-t / 0.025)) / SR
    body = (np.sin(ph) + 0.15 * np.sin(2 * ph)) * (1 - np.exp(-t / 0.005)) * np.exp(-t / 0.07)
    air = ma.bandpass(own.standard_normal(len(t)), 300, 1500) * np.exp(-t / 0.01) * 0.12
    out['record-start'] = ma.lowpass(body + air, 1400, 3)

    # The diagrams arrive: air that rises and glitters, ~1 s, a different noise in each ear; no tones.
    sec = 1.1
    t = t_(sec)
    env = np.where(t < 0.7, (t / 0.7) ** 1.6, np.cos(np.pi / 2 * np.clip((t - 0.7) / (sec - 0.7), 0, 1)) ** 2)
    grains = np.zeros(len(t))
    hits = own.random(len(t)) < 45 / SR  # ~45 tiny glints a second
    grains[hits] = own.uniform(0.3, 1.0, hits.sum())
    win = np.hanning(int(0.018 * SR))
    glint = np.convolve(grains, win / win.max(), 'same')
    glint = 0.55 + 0.45 * glint / (glint.max() + 1e-9)
    sides = [seeded(s, ma.swept_noise, sec, np.geomspace(2200, 7000, 32), 1.0) for s in (51, 52)]
    out['shimmer'] = ma.lowpass(ma.highpass(np.stack(sides, 1) * (env * glint)[:, None], 1600), 9000)

    # A knob dragged: ~2 s of soft friction, band-passed noise that wanders with the hand, in and out gently.
    sec = 2.0
    t = t_(sec)
    walk = np.cumsum(own.standard_normal(48))
    centres = 1000 * 2 ** (0.6 * (walk - walk.mean()) / (np.abs(walk - walk.mean()).max() + 1e-9))
    fr = seeded(53, ma.swept_noise, sec, centres, 1.1)
    grit = np.abs(ma.lowpass(own.standard_normal(len(t)), 70))
    grit = 0.6 + 0.4 * grit / (grit.max() + 1e-9)
    sway = 0.78 + 0.22 * np.sin(2 * np.pi * 4.3 * t + own.uniform(0, 6.3))
    edge = np.minimum(np.clip(t / 0.25, 0, 1), np.clip((sec - t) / 0.45, 0, 1)) ** 2
    out['slider'] = ma.lowpass(fr * grit * sway * edge, 3200)

    # Typing: 16 soft key taps over ~1.9 s, uneven, with a couple of word gaps.
    gaps = own.uniform(0.075, 0.13, 15)
    gaps[[4, 9]] += own.uniform(0.07, 0.12, 2)
    at = np.concatenate([[0.0], np.cumsum(gaps)]) * (1.8 / gaps.sum())
    keys = np.zeros(int(1.95 * SR))
    for a in at:
        k = base['key']
        stretch = own.uniform(0.92, 1.08)  # each key a hair higher or lower
        k = np.interp(np.arange(0, len(k), stretch), np.arange(len(k)), k)
        k = ma.lowpass(k, own.uniform(2200, 3400)) * own.uniform(0.55, 1.0)
        i = int(a * SR)
        keys[i:i + len(k)] += k[: len(keys) - i]
    out['keys'] = keys

    # A phone tap: lower and rounder than the click.
    t = t_(0.07)
    out['tap'] = ma.lowpass(np.sin(2 * np.pi * 430 * t) * np.exp(-t / 0.008) * (1 - np.exp(-t / 0.0015)) * 0.7
                            + np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.012) * 0.22
                            + ma.bandpass(own.standard_normal(len(t)), 400, 1800) * np.exp(-t / 0.0012) * 0.15, 1800)

    # The end card: a warm Dmaj9 pad that swells for ~1 s and dies away over ~3.5 s, darker as it fades.
    sec = 5.0
    t = t_(sec)
    pad = np.zeros((len(t), 2))
    for m in (50, 57, 61, 64, 66, 69):
        f = ma.hz(m)
        for ch, cents in ((0, -6), (1, 6)):
            ff = f * 2 ** (cents / 1200)
            pad[:, ch] += (0.4 * ma.signal.sawtooth(2 * np.pi * ff * t + own.uniform(0, 6.3))
                           + 0.25 * ma.signal.sawtooth(2 * np.pi * ff * 1.004 * t + own.uniform(0, 6.3))
                           + 0.6 * np.sin(2 * np.pi * ff * t + own.uniform(0, 6.3)))
    env = np.where(t < 1.0, np.sin(np.pi / 2 * t) ** 2, np.exp(-(t - 1.0) / 1.15)) * np.clip((sec - t) / 0.5, 0, 1)
    bright = ma.lowpass(pad, 1500, 2) * env[:, None]
    dark = ma.lowpass(pad, 550, 2) * env[:, None]
    mixer = np.clip(env, 0, 1)[:, None]
    out['swell'] = seeded(54, ma.reverb, bright * mixer + dark * (1 - mixer), 0.3, 2.4, 0.6)
    return out


def write_effects():
    made = made_effects()
    print('effects:')
    for name, longest in SFX_LONGEST.items():
        found = find(f'sfx-{name}')
        x = ma.own_sound(found, longest) if found else made[name]
        if x.ndim == 1:
            x = np.stack([x, x], 1)
        ma.write(OUT / 'sfx' / f'{name}.wav', x * SFX_PEAK / (np.max(np.abs(x)) + 1e-9))
        print(f"  {name:<13} {len(x) / SR:5.2f} s  {'from ' + found.name if found else 'made here'}")


# ---- Music --------------------------------------------------------------------------------------------------------

def write_music(starts, total):
    found = find('music')
    if found:
        x = load(found)[: int(total * SR)]
        if len(x) < int(total * SR):
            print(f'  WARNING: {found.name} is {len(x) / SR:.1f} s, shorter than the video ({total:.1f} s)')
        t = np.arange(len(x)) / SR
        x = x * np.clip((total - t) / 2.0, 0, 1)[:, None]
        save(OUT / 'music.wav', x * PEAK / (np.max(np.abs(x)) + 1e-9))
        return f'elevenlabs3/{found.name}'
    ma.BEAT = BEAT  # 120 BPM: every scene in the plan starts on a beat
    mix = seeded(7, ma.music, starts, total, roles=ROLES, bells=False)
    ma.write(OUT / 'music.wav', mix)
    return 'made here: make_audio.music() at 120 BPM, no bells'


# ---- The timeline -------------------------------------------------------------------------------------------------

def fit(plan, seconds):
    """Final scene starts: a scene whose line doesn't fit is lengthened by whole beats and everything after moves."""
    fps, beat = plan['fps'], round(BEAT * plan['fps'])
    scenes = plan['scenes']
    shift, rows = 0, []
    for i, s in enumerate(scenes):
        last = i + 1 == len(scenes)
        nxt = plan['end'] if last else scenes[i + 1]['start']
        need = (s['vo'] + seconds[s['id']] + (END_HOLD if last else ROOM) - nxt) * fps  # frames over
        stretch = math.ceil(need / beat - 1e-6) * beat if need > 1e-6 else 0
        rows.append(dict(id=s['id'], planned=s['start'], start_f=round(s['start'] * fps) + shift, vo_f=round(s['vo'] * fps) + shift,
                         stretch_f=stretch, room=(nxt - s['vo'] - seconds[s['id']]) + stretch / fps))
        shift += stretch
    return rows, round(plan['end'] * fps) + shift


def write_timeline(plan, rows, total_f, seconds, voice_source, music_source):
    fps, tr = plan['fps'], plan['transition']
    shift_of = {r['id']: r['start_f'] - round(r['planned'] * fps) for r in rows}
    scenes = []
    for i, r in enumerate(rows):
        dur = (rows[i + 1]['start_f'] - r['start_f'] + tr) if i + 1 < len(rows) else total_f - r['start_f']
        scenes.append({'id': r['id'], 'from': r['start_f'], 'duration': dur, 'start': round(r['start_f'] / fps, 3),
                       'stretchedBy': round(r['stretch_f'] / fps, 3)})
    voice = [{'id': r['id'], 'from': r['vo_f'], 'frames': math.ceil(seconds[r['id']] * fps - 1e-9),
              'seconds': round(seconds[r['id']], 3), 'file': f"audio/demo/vo/{r['id']}.wav"} for r in rows]
    effects = [{'key': e['key'], 'sfx': e['sfx'], 'from': round(e['at'] * fps) + shift_of[e['scene']],
                'frames': math.ceil(e['len'] * fps - 1e-9) if e.get('len') else None, 'listed': e['listed'],
                'file': f"audio/demo/sfx/{e['sfx']}.wav"} for e in plan['effects']]
    head = {'generated': 'by scripts/demo_audio.py from src/demo/plan.json: don\'t edit by hand', 'fps': fps,
            'transition': tr, 'beatFrames': round(BEAT * fps), 'total': total_f, 'voiceSource': voice_source,
            'musicSource': music_source}
    rows_json = lambda xs: '[\n' + ',\n'.join('    ' + json.dumps(x) for x in xs) + '\n  ]'
    body = ',\n'.join(f'  {json.dumps(k)}: {json.dumps(v)}' for k, v in head.items())
    body += f',\n  "scenes": {rows_json(scenes)},\n  "voice": {rows_json(voice)},\n  "effects": {rows_json(effects)},\n'
    body += '  "music": "audio/demo/music.wav"'
    TIMELINE.write_text('{\n' + body + '\n}\n')
    return effects


def report(plan, rows, total_f, seconds, effects):
    fps = plan['fps']
    print(f"\n{'scene':<10}{'planned':>8}{'final':>8}{'VO at':>8}{'line':>7}{'ends':>8}{'room':>7}{'stretched':>10}")
    for r in rows:
        vo = r['vo_f'] / fps
        print(f"{r['id']:<10}{r['planned']:8.2f}{r['start_f'] / fps:8.2f}{vo:8.2f}{seconds[r['id']]:6.2f}s"
              f"{vo + seconds[r['id']]:8.2f}{r['room']:6.2f}s{r['stretch_f'] / fps:9.1f}s")
    print('\neffects (final times):')
    for e, p in zip(effects, plan['effects']):
        moved = e['from'] / fps - p['at']
        print(f"  {e['key']:<20}{e['sfx']:<14}{e['from'] / fps:7.2f}s" + (f'  (+{moved:.1f})' if moved else '')
              + (f"  for {e['frames'] / fps:.1f}s" if e['frames'] else '') + ('' if e['listed'] else '  (not listed)'))
    print(f'\ntotal: {total_f} frames, {total_f / fps:.2f} s (planned {plan["end"]:.1f} s)')


# ---- Main ---------------------------------------------------------------------------------------------------------

def script_lines(path):
    """The lines of a plan.json (scenes) or a voiceover.json (lines), as a Lines list."""
    d = json.loads(pathlib.Path(path).read_text())
    if 'scenes' in d:
        return Lines([(s['id'], s['line']) for s in d['scenes']])
    return Lines([(l['id'], l['text']) for l in d['lines'] if l.get('inTake', True)])


def test_split(take, script, out_dir):
    lines = script_lines(script)
    out = split(pathlib.Path(take).expanduser(), lines)
    out_dir = pathlib.Path(out_dir or tempfile.mkdtemp(prefix='demo-split-'))
    out_dir.mkdir(parents=True, exist_ok=True)
    print(f'\n  {"line":<10}{"last 150 ms":>12}')
    for lid, o in zip(lines.ids, out):
        save(out_dir / f'{lid}.wav', o['audio'])
        print(f"  {lid:<10}{tail_level(o['audio']):9.1f} dB")
    print(f'lines written to {out_dir}')


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--voice', help='a take to split instead of elevenlabs3/voice.mp3')
    p.add_argument('--scratch', action='store_true', help="the Mac's own voice, even if there is a take")
    p.add_argument('--cuts', help='where lines 2…14 start in the take, in seconds, comma-separated (skips Whisper)')
    p.add_argument('--test-split', nargs=2, metavar=('TAKE', 'SCRIPT_JSON'), help='only split a take into a temp folder')
    p.add_argument('--out-dir', help='with --test-split: where the lines go (default: a new temp folder)')
    a = p.parse_args()
    if a.test_split:
        return test_split(*a.test_split, a.out_dir)

    plan = json.loads(PLAN.read_text())
    scenes = plan['scenes']
    take = pathlib.Path(a.voice).expanduser() if a.voice else find('voice')
    if take and not a.scratch:
        lines = Lines([(s['id'], s['line']) for s in scenes])
        cuts = [float(c) for c in a.cuts.split(',')] if a.cuts else None
        audio = {s['id']: o['audio'] for s, o in zip(scenes, split(take, lines, cuts))}
        voice_source = str(take.relative_to(ROOT)) if take.is_relative_to(ROOT) else take.name
    else:
        audio, voice_source = scratch_lines(scenes)
        print(f'voice: {voice_source}')
    seconds = {}
    for sid, y in audio.items():
        save(OUT / 'vo' / f'{sid}.wav', y)
        seconds[sid] = len(y) / SR

    rows, total_f = fit(plan, seconds)
    write_effects()
    starts = {r['id']: r['start_f'] / plan['fps'] for r in rows}
    music_source = write_music(starts, total_f / plan['fps'])
    print(f'music: {music_source}')
    effects = write_timeline(plan, rows, total_f, seconds, voice_source, music_source)
    report(plan, rows, total_f, seconds, effects)
    # The cue sheet for laying the sound under the picture-only render, from the timeline just written.
    import demo_cues
    demo_cues.OUT.write_text(demo_cues.build())
    print(f'cues: {demo_cues.OUT.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
