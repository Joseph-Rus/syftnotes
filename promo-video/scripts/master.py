#!/usr/bin/env python3
"""Masters a rendered video's sound for posting: −14 LUFS (where YouTube, Instagram and TikTok play things), its true
peak under −1 dBTP, the picture untouched.

    python3 scripts/master.py out/promo2.mp4      (npm run render2 does it after rendering)

Remotion mixes the voice, music and effects as they are, and where a loud word lands on the music the sum can just
touch full scale. This sets the level, then holds the peaks under the ceiling with a limiter that works at four times
the sample rate (so the peaks between samples are caught too); it only ever touches a few peaks. Needs ffmpeg.
"""
import json
import pathlib
import re
import subprocess
import sys

TARGET = -14.0  # LUFS, integrated
CEILING = -2.0  # dBFS for the limiter: AAC adds a few tenths on top, which keeps the true peak under −1 dBTP


def measure(path):
    """Integrated loudness (LUFS) and true peak (dBTP) of a file's sound."""
    out = subprocess.run(['ffmpeg', '-nostats', '-i', str(path), '-map', '0:a', '-af', 'ebur128=peak=true', '-f', 'null', '-'],
                         capture_output=True, text=True).stderr
    summary = out[out.rfind('Summary:'):]
    lufs = float(re.search(r'I:\s+(-?[\d.]+) LUFS', summary).group(1))
    peak = float(re.search(r'True peak:\s+Peak:\s+(-?[\d.]+|-inf) dBFS', summary).group(1))
    return lufs, peak


def master(path):
    path = pathlib.Path(path)
    lufs, peak = measure(path)
    gain = TARGET - lufs
    limit = 10 ** (CEILING / 20)
    chain = f'volume={gain:.2f}dB,aresample=192000,alimiter=limit={limit:.4f}:attack=1:release=60:level=false,aresample=48000'
    tmp = path.with_suffix('.mastering.mp4')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', str(path), '-map', '0:v', '-map', '0:a', '-c:v', 'copy',
                    '-af', chain, '-c:a', 'aac', '-b:a', '320k', '-movflags', '+faststart', str(tmp)], check=True)
    tmp.replace(path)
    after = measure(path)
    print(json.dumps({'file': str(path), 'before': {'lufs': lufs, 'truePeak': peak},
                      'after': {'lufs': after[0], 'truePeak': after[1]}}))
    if after[1] >= -1.0:
        sys.exit(f'{path}: the true peak is still {after[1]} dBTP')


if __name__ == '__main__':
    for p in sys.argv[1:]:
        master(p)
