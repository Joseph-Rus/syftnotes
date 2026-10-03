#!/usr/bin/env python3
"""The promo's music and sound effects, made from scratch: every sound is synthesised here, nothing is sampled or
downloaded, so there is nothing to license.

    python3 scripts/make_audio.py              (or: npm run audio; needs numpy and scipy)
    python3 scripts/make_audio.py --promo 2    (or: npm run audio2) the second video's music

Writes public/audio/music.wav (music2.wav for the second video) and public/audio/sfx/*.wav. The music follows the
scene lengths in src/config.ts (src/promo2/config.ts): at 100 BPM a beat is 18 frames, and each scene starts on a beat.
Both videos share the score and the sound effects; only where its moments fall differs (PROMOS below).

The score, in D major: a warm pad over Dmaj7, Bm7, Gmaj7 and A6sus2; a plucked arpeggio from the reveal; soft drums
and a bass from the recording; claps from the diagrams; a breakdown for the promise, a riser, and a Dmaj9 to end on.
"""
import argparse
import pathlib
import re

import numpy as np
from scipy import signal
from scipy.io import wavfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / 'public' / 'audio'
SR = 48000
FPS = 30
BPM = 100
BEAT = 60 / BPM
rng = np.random.default_rng(7)


# ---- The timeline, from src/config.ts -------------------------------------------------------------------------

# Each video: where its scene lengths are, its recut to the narration, its music file, and which of its scenes carry
# the score's moments: the riser, boom and bells (reveal), the drums and bass (record), the claps (claps), the
# breakdown (proof) and the last chord (cta).
PROMOS = {
    '1': dict(config='src/config.ts', timeline='src/timeline.json', music='music.wav',
              roles=dict(reveal='reveal', record='record', claps='diagrams', proof='proof', cta='cta')),
    # The second video's score has no bells: on its opening and its ending they rang out over the voice.
    '2': dict(config='src/promo2/config.ts', timeline='src/promo2/timeline.json', music='music2.wav',
              roles=dict(reveal='opener', record='notes', claps='explore', proof='quiet', cta='cta'), bells=False),
}


def timeline(promo=PROMOS['1']):
    src = (ROOT / promo['config']).read_text()
    block = re.search(r'export const baseDurations = \{(.*?)\};', src, re.S).group(1)
    lengths = [(k, int(v)) for k, v in re.findall(r'(\w+):\s*(\d+)', block)]
    fitted = ROOT / promo['timeline']  # the recut to the narration, from make_voice.py
    if fitted.exists():
        over = __import__('json').loads(fitted.read_text()).get('durations', {})
        lengths = [(k, int(over.get(k, v))) for k, v in lengths]
    fade = int(re.search(r'export const TRANSITION = (\d+);', src).group(1))
    starts, f = {}, 0
    for k, v in lengths:
        starts[k] = f / FPS
        f += v - fade
    return starts, (f + fade) / FPS


# ---- Building blocks -------------------------------------------------------------------------------------------

def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def times(sec):
    return np.arange(int(sec * SR)) / SR


def place(bus, x, at, gain=1.0, pan=0.0):
    """Adds x (mono or stereo) into the stereo bus at `at` seconds, panned -1 (left) … 1 (right)."""
    i = int(round(at * SR))
    if i >= len(bus):
        return
    if x.ndim == 1:
        left, right = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
        x = np.stack([x * left * 1.414, x * right * 1.414], 1)
    n = min(len(x), len(bus) - i)
    bus[i:i + n] += x[:n] * gain


def lowpass(x, hz_, order=2):
    return signal.sosfilt(signal.butter(order, hz_, 'low', fs=SR, output='sos'), x, axis=0)


def highpass(x, hz_, order=2):
    return signal.sosfilt(signal.butter(order, hz_, 'high', fs=SR, output='sos'), x, axis=0)


def bandpass(x, lo, hi, order=2):
    return signal.sosfilt(signal.butter(order, [lo, hi], 'band', fs=SR, output='sos'), x, axis=0)


def reverb(x, wet, seconds=2.2, decay=0.5):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal((n, 2)) * np.exp(-t / decay)[:, None]
    ir = lowpass(ir, 5500)
    ir[: int(0.012 * SR)] = 0  # a short pre-delay
    ir /= np.sqrt(np.sum(ir ** 2, 0))
    y = np.stack([signal.fftconvolve(x[:, c], ir[:, c])[: len(x)] for c in range(2)], 1)
    return x + wet * y


def swept_noise(sec, centres, width_oct=1.0):
    """Noise whose pitch follows `centres` (Hz, sampled across its length): whooshes, risers, a marker."""
    n = int(sec * SR)
    x = rng.standard_normal(n + 2048)
    f, _, z = signal.stft(x, SR, nperseg=1024)
    c = np.interp(np.linspace(0, 1, z.shape[1]), np.linspace(0, 1, len(centres)), np.log2(centres))
    mask = np.exp(-0.5 * ((np.log2(np.maximum(f, 20))[:, None] - c[None, :]) / (width_oct / 2)) ** 2)
    _, y = signal.istft(z * mask, SR, nperseg=1024)
    y = y[:n]
    return y / (np.max(np.abs(y)) + 1e-9)


# ---- Instruments -----------------------------------------------------------------------------------------------

def pad_note(f, sec):
    t = times(sec)
    out = np.zeros((len(t), 2))
    for ch, cents in ((0, -7), (1, 7)):
        ff = f * 2 ** (cents / 1200)
        out[:, ch] = (0.55 * signal.sawtooth(2 * np.pi * ff * t + rng.uniform(0, 6.3))
                      + 0.3 * signal.sawtooth(2 * np.pi * ff * 1.003 * t + rng.uniform(0, 6.3))
                      + 0.5 * np.sin(2 * np.pi * ff * t))
    a, r = 0.45, 0.9
    env = np.minimum(1, t / a) * np.clip((sec - t) / r, 0, 1) ** 0.7
    return out * env[:, None]


def pluck(f, sec=1.1):
    t = times(sec)
    s = (np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) * np.exp(-t / 0.12)
         + 0.1 * np.sin(6 * np.pi * f * t) * np.exp(-t / 0.05))
    return s * np.exp(-t / 0.3) * (1 - np.exp(-t / 0.004))


def bell(f, sec=2.6, index=2.2, ratio=3.5):
    t = times(sec)
    mod = index * np.exp(-t / 0.35) * np.sin(2 * np.pi * f * ratio * t)
    return np.sin(2 * np.pi * f * t + mod) * np.exp(-t / 0.95) * (1 - np.exp(-t / 0.003))


def kick():
    t = times(0.5)
    ph = 2 * np.pi * np.cumsum(46 + 95 * np.exp(-t / 0.04)) / SR
    return np.sin(ph) * np.exp(-t / 0.17) + 0.25 * rng.standard_normal(len(t)) * np.exp(-t / 0.0018)


def hat(open_=False):
    t = times(0.35 if open_ else 0.08)
    return highpass(rng.standard_normal(len(t)), 7500) * np.exp(-t / (0.12 if open_ else 0.028))


def clap():
    t = times(0.4)
    x = np.zeros(len(t))
    for d in (0, 0.011, 0.023):
        k = t - d
        x += np.where(k >= 0, np.exp(-np.maximum(k, 0) / 0.006), 0)
    x += 0.5 * np.exp(-t / 0.13)
    return bandpass(rng.standard_normal(len(t)), 1100, 3600) * x


def bass_note(f, sec):
    t = times(sec)
    s = np.sin(2 * np.pi * f * t) + 0.22 * np.sin(4 * np.pi * f * t) + 0.08 * np.sin(6 * np.pi * f * t)
    return s * np.minimum(1, t / 0.008) * np.clip((sec - t) / 0.06, 0, 1) * (0.75 + 0.25 * np.exp(-t / 0.15))


def boom(sec=2.2):
    t = times(sec)
    ph = 2 * np.pi * np.cumsum(38 + 40 * np.exp(-t / 0.18)) / SR
    return np.sin(ph) * np.exp(-t / 0.7) * (1 - np.exp(-t / 0.01))


def riser(sec):
    x = swept_noise(sec, np.geomspace(300, 7000, 64), 1.3)
    t = times(sec)
    return x * (t / sec) ** 2.2


# ---- The score -------------------------------------------------------------------------------------------------

CHORDS = {  # (pad voicing, bass root, arpeggio of eight eighth notes)
    'Dmaj7': ([50, 57, 61, 66], 38, [74, 69, 73, 78, 74, 69, 76, 73]),
    'Bm7': ([47, 54, 57, 62], 35, [71, 66, 69, 74, 71, 66, 73, 69]),
    'Gmaj7': ([43, 50, 54, 59], 43, [67, 71, 74, 78, 74, 71, 69, 71]),
    'A6sus2': ([45, 52, 54, 59], 45, [69, 71, 76, 78, 76, 71, 73, 71]),
}
PROGRESSION = ['Dmaj7', 'Bm7', 'Gmaj7', 'A6sus2']


def music(starts, total, roles=PROMOS['1']['roles'], bells=True):
    starts = {**starts, **{role: starts[scene] for role, scene in roles.items()}}
    beat_of = {k: round(v / BEAT) for k, v in starts.items()}
    first_bar = beat_of['record'] % 4  # bars fall so the recording, Ask, the promise and the ending start on one
    n = int((total + 3) * SR)
    pad_dark, pad_bright, arp, bass, drums, fx = (np.zeros((n, 2)) for _ in range(6))
    end_beat = int(round(total / BEAT))
    cta = beat_of['cta']

    # Pad: a pickup on A6sus2, then the progression bar by bar, and Dmaj9 from the call to action to the end.
    bars = [(-4 + first_bar, 'A6sus2')] + [(b, PROGRESSION[((b - first_bar) // 4) % 4]) for b in range(first_bar, cta, 4)]
    for i, (b, name) in enumerate(bars):
        start = max(b, 0) * BEAT
        stop = min(bars[i + 1][0] if i + 1 < len(bars) else cta, cta) * BEAT
        for m in CHORDS[name][0]:
            note = pad_note(hz(m), stop - start + 0.9) * 0.16
            place(pad_dark, note, start)
            place(pad_bright, note, start)
    for m in [50, 57, 61, 64, 66, 69]:
        note = pad_note(hz(m), (end_beat - cta) * BEAT + 1.5) * 0.15
        place(pad_dark, note, cta * BEAT)
        place(pad_bright, note, cta * BEAT)
    pad_dark = lowpass(pad_dark, 650)
    pad_bright = lowpass(pad_bright, 2300)
    t = np.arange(n) / SR
    bright = np.clip((t - starts['reveal'] + 0.3) / 0.8, 0, 1) * np.where(t < starts['proof'], 1, 0.55)
    pad = pad_dark * (1 - bright)[:, None] + pad_bright * bright[:, None]

    # Arpeggio, eighth notes, from the bar after the reveal until the call to action; brighter once the groove is in.
    for b, name in bars:
        if b < beat_of['reveal'] or b >= cta:
            continue
        for k, m in enumerate(CHORDS[name][2]):
            at = (b + k / 2) * BEAT
            gain = 0.13 if b < beat_of['record'] else 0.2 if b < beat_of['proof'] else 0.1
            place(arp, pluck(hz(m)), at, gain, pan=0.35 if k % 2 else -0.35)

    # Drums and bass: from the recording to the promise, claps from the diagrams.
    for b in range(beat_of['record'], beat_of['proof']):
        bar_beat = (b - first_bar) % 4
        name = PROGRESSION[((b - first_bar) // 4) % 4]
        root = hz(CHORDS[name][1])
        if bar_beat in (0, 2):
            place(drums, kick(), b * BEAT, 0.85)
        if bar_beat == 2 and ((b - first_bar) // 4) % 2 == 1:
            place(drums, kick(), (b + 0.5) * BEAT, 0.55)
        if bar_beat in (1, 3) and b >= beat_of['claps']:
            place(drums, clap(), b * BEAT, 0.32, pan=0.1)
        for half in (0, 0.5):
            open_ = half == 0.5 and bar_beat == 3
            place(drums, lowpass(hat(open_), 11000), (b + half) * BEAT, 0.055 if half else 0.03, pan=0.3)
        for off, length in ((0, 0.45), (0.5, 0.2)) if bar_beat in (0, 2) else ((0.5, 0.2),):
            place(bass, bass_note(root, length * BEAT * 2), (b + off) * BEAT, 0.34)
    # The pad ducks under each kick, gently.
    duck = np.ones(n)
    for b in range(beat_of['record'], beat_of['proof']):
        if (b - first_bar) % 4 in (0, 2):
            i = int(b * BEAT * SR)
            k = np.arange(min(int(0.35 * SR), n - i))
            duck[i:i + len(k)] = np.minimum(duck[i:i + len(k)], 1 - 0.32 * np.exp(-k / (0.1 * SR)))
    pad *= duck[:, None]

    # Moments: a riser into the reveal, bells and a low boom on it; a riser into the call to action, the last chord.
    if starts['reveal'] >= 1.6:  # a riser into the reveal, when something comes before it
        place(fx, riser(1.6), starts['reveal'] - 1.6, 0.22)
    # (The bells draw on no randomness, so leaving them out changes nothing else in the score.)
    place(fx, boom(), starts['reveal'], 0.55)
    for m, d in ((74, 0), (81, 0.06)) if bells else ():
        place(fx, bell(hz(m)), starts['reveal'] + d, 0.16, pan=-0.2 if d else 0.2)
    place(fx, riser(2.0), starts['cta'] - 2.0, 0.26)
    place(fx, boom(3.0), starts['cta'], 0.6)
    for m, d in ((62, 0), (66, 0.05), (69, 0.1), (74, 0.15), (76, 0.2)) if bells else ():
        place(fx, bell(hz(m), 3.5), starts['cta'] + d, 0.12, pan=(d * 6 - 0.6))

    mix = (reverb(pad, 0.3) + reverb(arp, 0.35, decay=0.45) * 0.9 + bass + drums + reverb(fx, 0.45, decay=0.8))
    mix = highpass(mix, 28)
    mix = mix[: int(total * SR)]
    fade = np.clip((total - np.arange(len(mix)) / SR) / 1.4, 0, 1) ** 1.5
    mix *= fade[:, None]
    mix = np.tanh(mix * 1.2) / np.tanh(1.2)
    return mix / np.max(np.abs(mix)) * 0.89


# ---- Sound effects ---------------------------------------------------------------------------------------------

def sfx():
    out = {}
    t = times(0.07)
    tick = highpass(rng.standard_normal(len(t)), 2500) * np.exp(-t / 0.0016)
    out['click'] = tick * 0.9 + np.sin(2 * np.pi * 1700 * t) * np.exp(-t / 0.004) * 0.25 + np.sin(2 * np.pi * 320 * t) * np.exp(-t / 0.008) * 0.3
    for i, body in enumerate((420, 510, 610)):
        t = times(0.06)
        k = bandpass(rng.standard_normal(len(t)), 1800, 6500) * np.exp(-t / 0.0045)
        out[f'key{i + 1}'] = k * 0.8 + np.sin(2 * np.pi * body * t) * np.exp(-t / 0.009) * 0.35
    t = times(0.16)
    ph = 2 * np.pi * np.cumsum(620 + 480 * np.exp(-t / 0.018)) / SR
    out['pop'] = np.sin(ph) * np.exp(-t / 0.045) * (1 - np.exp(-t / 0.002))
    w = swept_noise(0.7, np.concatenate([np.geomspace(350, 2600, 40), np.geomspace(2600, 700, 24)]), 1.4)
    t = times(0.7)
    w = w * np.sin(np.pi * np.clip(t / 0.7, 0, 1)) ** 2
    pan = np.linspace(-0.7, 0.7, len(w))
    out['whoosh'] = np.stack([w * np.cos((pan + 1) * np.pi / 4), w * np.sin((pan + 1) * np.pi / 4)], 1) * 1.3
    t = times(0.36)
    m = swept_noise(0.36, np.geomspace(2400, 3800, 16), 1.1) * np.sin(np.pi * t / 0.36) ** 0.8
    out['marker'] = m * (0.8 + 0.2 * np.sin(2 * np.pi * 38 * t))
    rec = np.zeros(int(1.8 * SR))
    for m_, d in ((81, 0), (86, 0.1)):
        b = bell(hz(m_), 1.6, index=1.6)
        i = int(d * SR)
        rec[i:i + len(b)] += b[: len(rec) - i] * 0.5
    out['chime-record'] = rec
    filed = np.zeros(int(2.0 * SR))
    for m_, d in ((74, 0), (78, 0.075), (81, 0.15)):
        b = bell(hz(m_), 1.8, index=1.4, ratio=2.0)
        i = int(d * SR)
        filed[i:i + len(b)] += b[: len(filed) - i] * 0.45
    out['chime-filed'] = filed
    # Added for the second video, from their own seed so the effects above stay exactly as they were.
    own = np.random.default_rng(11)
    t = times(0.05)  # a slider's detent as it's dragged: tiny, dry, high
    out['tick'] = (bandpass(own.standard_normal(len(t)), 2500, 7000) * np.exp(-t / 0.0012) * 0.5
                   + np.sin(2 * np.pi * 2300 * t) * np.exp(-t / 0.006) * 0.35)
    ding = np.zeros(int(1.2 * SR))  # "Knew it": a soft, bright bell, a fifth above the record chime
    b = bell(hz(88), 1.1, index=1.1, ratio=2.0)
    ding[:len(b)] += b * 0.5
    out['ding'] = ding
    return out


def write(path, x):
    path.parent.mkdir(parents=True, exist_ok=True)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    x = x / max(1.0, np.max(np.abs(x)) / 0.95)
    wavfile.write(path, SR, (x * 32767).astype(np.int16))


# ---- The second video's effects: fewer and softer --------------------------------------------------------------
# Five sounds, all quiet, rounded and dry: no bells. Each is written at the same peak (SFX2_PEAK); how loud it plays is
# set in src/promo2/sound.ts. A file of your own in sfx2/ (click.mp3, key.mp3, pop.mp3, whoosh.mp3, marker.mp3; see
# SFX2.md) takes the place of the made one: `npm run sfx2` trims it, levels it to the same peak and puts it in
# public/audio/sfx2/, which is what the video plays.

SFX2_PEAK = 0.5
SFX2_LONGEST = dict(click=0.15, key=0.15, pop=0.4, whoosh=1.2, marker=0.8)  # seconds kept of a file of your own


def sfx2():
    own = np.random.default_rng(23)  # its own seed: the first video's effects and both scores stay as they were
    out = {}
    t = times(0.035)  # a trackpad's click, felt more than heard: a little body, the edge taken off
    out['click'] = lowpass(np.sin(2 * np.pi * 820 * t) * np.exp(-t / 0.003) * 0.6
                           + bandpass(own.standard_normal(len(t)), 900, 3200) * np.exp(-t / 0.0011) * 0.35, 3800)
    t = times(0.06)  # a soft key, low and short
    out['key'] = lowpass(bandpass(own.standard_normal(len(t)), 500, 2400) * np.exp(-t / 0.0028) * 0.6
                         + np.sin(2 * np.pi * 240 * t) * np.exp(-t / 0.012) * 0.5, 3000)
    t = times(0.13)  # something settling into place: a rounded "tok" that falls a little in pitch
    ph = 2 * np.pi * np.cumsum(340 + 180 * np.exp(-t / 0.02)) / SR
    out['pop'] = lowpass((np.sin(ph) + 0.08 * np.sin(2 * ph)) * np.exp(-t / 0.035) * (1 - np.exp(-t / 0.004)), 2500)
    w = lowpass(swept_noise(0.55, np.concatenate([np.geomspace(280, 1100, 30), np.geomspace(1100, 450, 20)]), 1.2), 3000)
    t = times(0.55)  # air moving past, low and smooth
    w = w * np.sin(np.pi * np.clip(t / 0.55, 0, 1)) ** 2
    pan = np.linspace(-0.4, 0.4, len(w))
    out['whoosh'] = np.stack([w * np.cos((pan + 1) * np.pi / 4), w * np.sin((pan + 1) * np.pi / 4)], 1)
    t = times(0.3)  # a felt-tip pen across paper
    out['marker'] = lowpass(swept_noise(0.3, np.geomspace(1200, 1800, 12), 0.9), 4500) * np.sin(np.pi * t / 0.3) ** 0.8
    return out


def own_sound(path, longest):
    """A sound from the ElevenLabs website (or anywhere): 48 kHz stereo, from its first sound, at most `longest` s."""
    import subprocess
    import tempfile
    with tempfile.TemporaryDirectory() as d:
        wav = pathlib.Path(d) / 'in.wav'
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', str(path), '-ar', str(SR), '-ac', '2', str(wav)], check=True)
        _, x = wavfile.read(wav)
    x = x.astype(float) / 32768.0
    level = np.abs(x).max(1)
    start = int(np.argmax(level > level.max() * 10 ** (-40 / 20)))
    x = x[max(0, start - int(0.002 * SR)):][: int(longest * SR)]
    n = min(len(x), int(0.03 * SR))
    x[len(x) - n:] *= np.linspace(1, 0, n)[:, None]  # no click where it's cut
    return x


def write_sfx2():
    made = sfx2()
    mine = ROOT / 'sfx2'
    for name, x in made.items():
        found = next((p for ext in ('wav', 'mp3', 'm4a', 'aac', 'flac', 'ogg') for p in mine.glob(f'{name}.{ext}')), None)
        if found:
            x = own_sound(found, SFX2_LONGEST[name])
        if x.ndim == 1:
            x = np.stack([x, x], 1)
        x = x * SFX2_PEAK / (np.max(np.abs(x)) + 1e-9)
        # The video plays key1, key2 and key3 (key taps vary); one key sound serves all three.
        for file in (('key1', 'key2', 'key3') if name == 'key' else (name,)):
            write(OUT / 'sfx2' / f'{file}.wav', x)
        print(f"sfx2: {name:<7} {'from ' + found.name if found else 'made here'}")


if __name__ == '__main__':
    args = argparse.ArgumentParser(description='The promo videos\' music and sound effects.')
    args.add_argument('--promo', choices=sorted(PROMOS), default='1', help='which video: 1 (the first) or 2 (what\'s new)')
    args.add_argument('--sfx-only', action='store_true', help='with --promo 2: only the second video\'s effects (npm run sfx2)')
    a = args.parse_args()
    promo = PROMOS[a.promo]
    if not a.sfx_only:
        starts, total = timeline(promo)
        write(OUT / promo['music'], music(starts, total, promo['roles'], promo.get('bells', True)))
        for name, x in sfx().items():
            # The first video's run makes them all. The second's only adds what's missing: its randomness runs on from
            # the music, so remaking them here would change the first video's effects by a hair.
            if promo is PROMOS['1'] or not (OUT / 'sfx' / f'{name}.wav').exists():
                write(OUT / 'sfx' / f'{name}.wav', x)
        print(f'music: {total:.2f} s; scenes start at ' + ', '.join(f'{k} {v:.1f}s (beat {v / BEAT:.1f})' for k, v in starts.items()))
        print('sfx: ' + ', '.join(sorted(p.stem for p in (OUT / 'sfx').glob('*.wav'))))
    if promo is PROMOS['2']:
        write_sfx2()  # after the music: the noise it draws on would otherwise shift the score's
