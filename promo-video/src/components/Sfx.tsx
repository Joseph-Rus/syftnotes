import React from 'react';
import {Audio, Sequence, staticFile} from 'remotion';
import {sfxVolume} from '../config';

export type SfxName = 'click' | 'key1' | 'key2' | 'key3' | 'pop' | 'whoosh' | 'marker' | 'chime-record' | 'chime-filed';

/** A sound effect from public/audio/sfx (made by scripts/make_audio.py), played `at` frames into the scene. */
export const Sfx: React.FC<{at: number; name: SfxName; volume?: number}> = ({at, name, volume = 0.5}) => (
  <Sequence from={Math.round(at)} durationInFrames={75} layout="none">
    <Audio src={staticFile(`audio/sfx/${name}.wav`)} volume={volume * sfxVolume} />
  </Sequence>
);

/** Key taps while `count` characters are typed from `start`, one every `every` frames. */
export const Typing: React.FC<{start: number; count: number; perSecond: number; every?: number; volume?: number}> = ({
  start,
  count,
  perSecond,
  every = 3,
  volume = 0.22,
}) => {
  const end = start + (count / perSecond) * 30;
  const taps: number[] = [];
  for (let f = start + 1; f < end; f += every) taps.push(f);
  return (
    <>
      {taps.map((f, i) => (
        <Sfx key={f} at={f} name={(['key1', 'key2', 'key3'] as const)[(i * 7) % 3]} volume={volume * (0.8 + ((i * 37) % 5) / 20)} />
      ))}
    </>
  );
};
