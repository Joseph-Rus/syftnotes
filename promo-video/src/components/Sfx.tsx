import React from 'react';
import {Audio, Sequence, staticFile} from 'remotion';
import {sfxVolume} from '../config';

export type SfxName = 'click' | 'key1' | 'key2' | 'key3' | 'pop' | 'whoosh' | 'marker' | 'chime-record' | 'chime-filed' | 'tick' | 'ding';

/**
 * Which sound effects a video plays: the folder in public/ its files are in, and a gain for each (times the volume each
 * scene asks for), or null for one it leaves out. The first video's are public/audio/sfx, as they are.
 */
export type SfxSet = {folder: string; gain?: Partial<Record<SfxName, number | null>>};
const SfxSetContext = React.createContext<SfxSet>({folder: 'audio/sfx'});
/** Everything inside plays this set of effects (the second video's: src/promo2/sound.ts). */
export const SfxSetProvider = SfxSetContext.Provider;

/** A sound effect from the video's set (made by scripts/make_audio.py), played `at` frames into the scene. */
export const Sfx: React.FC<{at: number; name: SfxName; volume?: number}> = ({at, name, volume = 0.5}) => {
  const set = React.useContext(SfxSetContext);
  const gain = set.gain?.[name];
  if (gain === null) return null;
  return (
    <Sequence from={Math.round(at)} durationInFrames={75} layout="none">
      <Audio src={staticFile(`${set.folder}/${name}.wav`)} volume={volume * sfxVolume * (gain ?? 1)} />
    </Sequence>
  );
};

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
