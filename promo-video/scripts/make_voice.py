#!/usr/bin/env python3
"""The narration: makes each line of src/voiceover.json, recuts the video to fit it, and remakes the music.

With ElevenLabs (your own account; the key stays in your shell, it is never printed or saved):

    export ELEVENLABS_API_KEY=...                  # from elevenlabs.io → Profile → API keys
    npm run voice -- --list-voices                 # your voices, with their ids
    npm run voice -- --voice <voice id>            # all eight lines, then the recut and the music
    npm run voice -- --voice <voice id> --takes 3  # three takes of each; pick with --pick record=2,ask=3

From the ElevenLabs website instead (no API key): generate the whole script as one take, download it, and it is split
into the eight lines at the pauses between them; or download a file per line, named after the line (hook.mp3, …):

    npm run voice -- --from-take ~/Downloads/narration.mp3
    npm run voice -- --from-files ~/Downloads/lines/     # hook.mp3, reveal.mp3, record.mp3, …

Without either, a scratch narration in the Mac's own voice, so the cut can be checked first:

    npm run voice -- --scratch

The second video (VOICEOVER2.md) works the same way with --promo 2, or `npm run voice2 -- …`: its script is
src/promo2/voiceover.json, its lines go to public/audio/vo2/, and its recut to src/promo2/timeline.json.

Best practice from ElevenLabs' docs, followed here: one model, voice, set of voice settings and seed for every line;
each line conditioned on the ones before it (request stitching: previous_request_ids, up to three, oldest first; not
on eleven_v3) and on the text after it (next_text), so separate clips sound like one read; pacing from punctuation
(ellipses, dashes) rather than SSML breaks, which v3 and later ignore; WAV at 48 kHz to match the video.
"""
import argparse
import json
import math
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

import numpy as np
from scipy.io import wavfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
# Each video's files: its script, what was made, its recut, its lines, and its scene lengths. --promo picks one.
PROMOS = {
    '1': dict(script='src/voiceover.json', voice='src/voice.json', timeline='src/timeline.json', out='public/audio/vo', config='src/config.ts'),
    '2': dict(script='src/promo2/voiceover.json', voice='src/promo2/voice.json', timeline='src/promo2/timeline.json',
              out='public/audio/vo2', config='src/promo2/config.ts'),
}
SCRIPT = VOICE_JSON = TIMELINE = OUT = CONFIG = None  # set by use()


def use(promo):
    global SCRIPT, VOICE_JSON, TIMELINE, OUT, CONFIG
    f = PROMOS[promo]
    SCRIPT, VOICE_JSON, TIMELINE = ROOT / f['script'], ROOT / f['voice'], ROOT / f['timeline']
    OUT, CONFIG = ROOT / f['out'], ROOT / f['config']


use('1')
API = 'https://api.elevenlabs.io/v1'
SR = 48000
FPS = 30
BEAT = 18  # frames: 100 BPM
TAIL = 0.45  # seconds of breath after a line, before the cross-fade into the next scene
CTA_HOLD = 1.6  # seconds the ending holds after its last word


# ---- ElevenLabs ------------------------------------------------------------------------------------------------

def key():
    k = os.environ.get('ELEVENLABS_API_KEY', '').strip()
    if not k:
        sys.exit('Set ELEVENLABS_API_KEY in your shell first (elevenlabs.io → Profile → API keys). It is only sent to ElevenLabs.')
    return k


def call(method, path, body=None, query=''):
    req = urllib.request.Request(f'{API}{path}{query}', method=method, data=json.dumps(body).encode() if body else None)
    req.add_header('xi-api-key', key())
    if body:
        req.add_header('Content-Type', 'application/json')
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return r.read(), r.headers
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors='replace')[:400]
        raise RuntimeError(f'ElevenLabs said {e.code}: {detail}') from None


def list_voices():
    data, _ = call('GET', '/voices')
    for v in json.loads(data).get('voices', []):
        labels = ', '.join(f'{k} {val}' for k, val in (v.get('labels') or {}).items())
        print(f"{v['voice_id']}  {v['name']:<24} {labels}")


def speak(voice, text, model, settings, seed, previous_ids, next_text):
    body = {'text': text, 'model_id': model, 'voice_settings': settings, 'seed': seed, 'apply_text_normalization': 'auto'}
    if next_text:
        body['next_text'] = next_text
    if previous_ids and model != 'eleven_v3':
        body['previous_request_ids'] = previous_ids[-3:]
    last = None
    for fmt in ('wav_48000', 'mp3_44100_192', 'mp3_44100_128'):  # the best the plan allows
        try:
            audio, headers = call('POST', f'/text-to-speech/{voice}', body, f'?output_format={fmt}')
            return audio, fmt, headers.get('request-id')
        except RuntimeError as e:
            last = e
            if 'output_format' not in str(e) and 'format' not in str(e).lower() and 'tier' not in str(e).lower():
                raise
    raise last


# ---- The Mac's own voice, for a scratch cut ---------------------------------------------------------------------

def say(text, path):
    installed = subprocess.run(['say', '-v', '?'], capture_output=True, text=True).stdout
    voice = next((v for v in ('Ava (Premium)', 'Zoe (Premium)', 'Samantha') if v in installed), None)
    aiff = path.with_suffix('.aiff')
    subprocess.run(['say', *(['-v', voice] if voice else []), '-r', '172', '-o', str(aiff), text], check=True)
    to_wav(aiff, path)
    aiff.unlink()
    return voice or 'the default voice'


# ---- Clean-up: every clip trimmed, at the same loudness, 48 kHz stereo ---------------------------------------------

def to_wav(src, dst):
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', str(src), '-ar', str(SR), '-ac', '2', str(dst)], check=True)


def tidy(path):
    sr, x = wavfile.read(path)
    x = x.astype(float) / (32768.0 if x.dtype == np.int16 else 1.0)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    mono = np.abs(x).mean(1)
    win = int(0.01 * sr)
    level = np.convolve(mono, np.ones(win) / win, 'same')
    loud = np.where(level > 10 ** (-42 / 20))[0]
    if len(loud):
        a = max(0, loud[0] - int(0.04 * sr))
        b = min(len(x), loud[-1] + int(0.12 * sr))
        x = x[a:b]
    rms = np.sqrt(np.mean(x ** 2)) + 1e-9
    x = x * (10 ** (-19 / 20) / rms)
    peak = np.max(np.abs(x))
    if peak > 0.89:
        x = x * 0.89 / peak
    wavfile.write(path, sr, (x * 32767).astype(np.int16))
    return len(x) / sr


# ---- A take from the website: split at the pauses between lines ---------------------------------------------------

def split_take(path, lines, given=None):
    """Cuts one continuous read into its lines: at the seven pauses that best fit where each line should end, judged by
    how many words come before it, preferring longer pauses."""
    with tempfile.TemporaryDirectory() as d:
        wav = pathlib.Path(d) / 'take.wav'
        to_wav(path, wav)
        sr, x = wavfile.read(wav)
    x = x.astype(float) / 32768.0
    mono = np.abs(x).mean(1)
    hop = int(0.01 * sr)
    level = 20 * np.log10(np.sqrt(np.convolve(mono ** 2, np.ones(hop) / hop, 'same'))[::hop] + 1e-9)
    quiet = level < level.max() - 32
    speech = np.where(~quiet)[0]
    first, last = speech[0], speech[-1]
    runs, i = [], first
    while i <= last:
        if quiet[i]:
            j = i
            while j <= last and quiet[j]:
                j += 1
            if j - i >= 16:  # 160 ms or more
                runs.append(((i + j) / 2, j - i))
            i = j
        else:
            i += 1
    if given:  # exact cuts, in seconds, between the lines (e.g. from a transcript's word timings)
        cuts = [first - 5] + [c * 100 for c in given] + [last + 5]
        for n, line in enumerate(lines):
            a, b = int(max(0, cuts[n]) * hop), int(cuts[n + 1] * hop)
            wavfile.write(OUT / f"{line['id']}.wav", sr, (x[a:b] * 32767).astype(np.int16))
            print(f"  split {line['id']:<9} at {a / sr:6.2f}–{b / sr:6.2f} s")
        return
    words = [len(l['text'].split()) for l in lines]
    ends = np.cumsum(words)[:-1] / sum(words)
    need = len(lines) - 1
    if len(runs) < need:
        sys.exit(f'Found only {len(runs)} pauses in the take; the lines need {need}. Put each line in its own paragraph and generate again.')
    span = last - first
    cost = lambda k, r: abs((runs[r][0] - first) / span - ends[k]) * 12 - math.log(runs[r][1])
    best = [[math.inf] * len(runs) for _ in range(need)]
    back = [[-1] * len(runs) for _ in range(need)]
    for r in range(len(runs)):
        best[0][r] = cost(0, r)
    for k in range(1, need):
        for r in range(k, len(runs)):
            for q in range(k - 1, r):
                c = best[k - 1][q] + cost(k, r)
                if c < best[k][r]:
                    best[k][r], back[k][r] = c, q
    r = min(range(len(runs)), key=lambda r: best[need - 1][r])
    cuts = []
    for k in range(need - 1, -1, -1):
        cuts.append(runs[r][0])
        r = back[k][r]
    cuts = [first - 5] + sorted(cuts) + [last + 5]
    for n, line in enumerate(lines):
        a, b = int(max(0, cuts[n]) * hop), int(cuts[n + 1] * hop)
        wavfile.write(OUT / f"{line['id']}.wav", sr, (x[a:b] * 32767).astype(np.int16))
        print(f"  split {line['id']:<9} at {a / sr:6.2f}–{b / sr:6.2f} s")


def from_files(folder, lines):
    folder = pathlib.Path(folder).expanduser()
    for line in lines:
        found = next((p for ext in ('wav', 'mp3', 'm4a', 'aac', 'flac') for p in folder.glob(f"{line['id']}*.{ext}")), None)
        if not found:
            sys.exit(f"No file for {line['id']} in {folder} (name it {line['id']}.mp3 or .wav).")
        to_wav(found, OUT / f"{line['id']}.wav")


# ---- The recut: each scene long enough for its line, still starting on a beat -------------------------------------

def base_durations():
    src = CONFIG.read_text()
    block = re.search(r'export const baseDurations = \{(.*?)\};', src, re.S).group(1)
    return {k: int(v) for k, v in re.findall(r'(\w+):\s*(\d+)', block)}


def fit(lines, seconds):
    base = base_durations()
    transition = int(re.search(r'export const TRANSITION = (\d+);', CONFIG.read_text()).group(1))
    ids = list(base)
    at = {l['id']: l['at'] for l in lines}
    out = {}
    for i, sid in enumerate(ids):
        last = i == len(ids) - 1
        d = base[sid]
        if sid in seconds:
            need = at[sid] + seconds[sid] + (CTA_HOLD if last else TAIL)
            d = max(d, math.ceil(need * FPS) + (0 if last else transition))
        # Scenes after the first start on a beat: every length but the last is a whole number of beats plus the
        # cross-fade; the last is whole beats, so the video ends on one too.
        if last:
            d = math.ceil(d / BEAT) * BEAT
        else:
            d = math.ceil((d - transition) / BEAT) * BEAT + transition
        out[sid] = d
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--voice', default=os.environ.get('ELEVENLABS_VOICE_ID'), help='ElevenLabs voice id')
    p.add_argument('--model', help='override the model in src/voiceover.json')
    p.add_argument('--takes', type=int, default=1, help='takes of each line (alternates are kept beside the chosen one)')
    p.add_argument('--pick', default='', help='which take to use, e.g. record=2,ask=3')
    p.add_argument('--scratch', action='store_true', help="a scratch narration in the Mac's own voice")
    p.add_argument('--from-take', help='one continuous read downloaded from the website, split into the lines')
    p.add_argument('--from-files', help='a folder with a file per line from the website: hook.mp3, reveal.mp3, …')
    p.add_argument('--cuts', help='with --from-take: where each line ends, in seconds, e.g. 4.39,9.06,… (if the automatic split is off)')
    p.add_argument('--line', action='append', default=[], help='one line from its own file, e.g. --line handwriting=~/Downloads/handwriting.mp3')
    p.add_argument('--list-voices', action='store_true')
    p.add_argument('--promo', choices=sorted(PROMOS), default='1', help="which video: 1 (the first) or 2 (what's new)")
    p.add_argument('--no-music', action='store_true', help="don't remake the music afterwards")
    a = p.parse_args()
    use(a.promo)

    if a.list_voices:
        return list_voices()
    script = json.loads(SCRIPT.read_text())
    lines = script['lines']
    OUT.mkdir(parents=True, exist_ok=True)
    picks = dict(kv.split('=') for kv in a.pick.split(',') if '=' in kv)
    seconds, request_ids = {}, []
    source = 'scratch'
    if not (a.scratch or a.voice or a.from_take or a.from_files):
        sys.exit('Give a voice: --voice <id> (see --list-voices), --from-take <file>, --from-files <folder>, or --scratch.')
    model = a.model or script['model']
    singles = dict(kv.split('=', 1) for kv in a.line)
    if a.from_take:
        # Lines added after the take was recorded ("inTake": false) come from --line, or go without a voice.
        split_take(pathlib.Path(a.from_take).expanduser(), [l for l in lines if l.get('inTake', True)], [float(c) for c in a.cuts.split(',')] if a.cuts else None)
    elif a.from_files:
        from_files(a.from_files, lines)
    for lid, path in singles.items():
        to_wav(pathlib.Path(path).expanduser(), OUT / f'{lid}.wav')
    if a.from_take:
        lines = [l for l in lines if l.get('inTake', True) or l['id'] in singles]
        for l in script['lines']:
            if l not in lines:
                (OUT / f"{l['id']}.wav").unlink(missing_ok=True)
                print(f"  {l['id']:<9} no voice yet (not in the take; add it with --line {l['id']}=<file>)")

    for i, line in enumerate(lines):
        final = OUT / f"{line['id']}.wav"
        if a.from_take or a.from_files or line['id'] in singles:
            source, who = 'elevenlabs website', pathlib.Path(a.from_take or a.from_files).name
        elif a.scratch:
            who = say(line['text'], final)
        else:
            source = 'elevenlabs'
            nxt = lines[i + 1]['text'] if i + 1 < len(lines) else ''
            chosen = int(picks.get(line['id'], 1))
            kept_id = None
            for take in range(1, a.takes + 1):
                try:
                    audio, fmt, rid = speak(a.voice, line['text'], model, script['voiceSettings'], script['seed'] + take - 1, request_ids, nxt)
                except RuntimeError as e:
                    if 'model' in str(e).lower() and model != script['fallbackModel']:
                        print(f"  {model} isn't available here; using {script['fallbackModel']}")
                        model = script['fallbackModel']
                        audio, fmt, rid = speak(a.voice, line['text'], model, script['voiceSettings'], script['seed'] + take - 1, request_ids, nxt)
                    else:
                        raise
                path = final if take == chosen else OUT / f"{line['id']}.take{take}.wav"
                with tempfile.NamedTemporaryFile(suffix='.wav' if fmt.startswith('wav') else '.mp3', delete=False) as tmp:
                    tmp.write(audio)
                to_wav(tmp.name, path)
                os.unlink(tmp.name)
                if take == chosen:
                    kept_id = rid
                if path != final:
                    tidy(path)
            if kept_id:
                request_ids.append(kept_id)
            who = a.voice
        seconds[line['id']] = round(tidy(final), 3)
        print(f"  {line['id']:<9} {seconds[line['id']]:5.2f} s  {line['text']}")

    VOICE_JSON.write_text(json.dumps({'source': source, 'voice': who, 'model': None if a.scratch else model, 'seconds': seconds}, indent=2) + '\n')
    durations = fit(lines, seconds)
    TIMELINE.write_text(json.dumps({'durations': durations}, indent=2) + '\n')
    total = sum(durations.values()) - (len(durations) - 1) * int(re.search(r'export const TRANSITION = (\d+);', CONFIG.read_text()).group(1))
    print(f"recut: {total / FPS:.1f} s — " + ', '.join(f'{k} {v / FPS:.1f}s' for k, v in durations.items()))
    if not a.no_music:
        subprocess.run([sys.executable, str(ROOT / 'scripts' / 'make_audio.py'), '--promo', a.promo], check=True)


if __name__ == '__main__':
    main()
