import React from 'react';
import {AbsoluteFill, Audio, interpolate, Sequence, staticFile} from 'remotion';
import {linearTiming, TransitionSeries} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {colors, duckTo, FPS, voiceVolume} from '../config';
import {Sfx, SfxSetProvider} from '../components/Sfx';
import {SFX2} from './sound';
import {durations2, music2, musicVolume2, Scene2, sceneStart2, TOTAL_FRAMES2, TRANSITION, voiceLines2} from './config';
import {Intro2} from './scenes/Intro2';
import {NotesFirst} from './scenes/NotesFirst';
import {Explore} from './scenes/Explore';
import {Recall} from './scenes/Recall';
import {Kinds} from './scenes/Kinds';
import {Drawings} from './scenes/Drawings';
import {Plots} from './scenes/Plots';
import {Quiet} from './scenes/Quiet';
import {Cta2, Proof2} from './scenes/Outro2';

// Where each narration line plays, in frames of the whole video.
const VOICE = voiceLines2.map((l) => {
  const from = sceneStart2(l.id) + Math.round(l.at * FPS);
  return {...l, from, to: from + Math.round(l.seconds * FPS)};
});

/** How loud the music is at frame `f`: its level, dipping under the voice, fading in and out at the ends. */
const musicAt = (f: number) => {
  const ends = interpolate(f, [0, 8, TOTAL_FRAMES2 - 30, TOTAL_FRAMES2], [0, 1, 1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const under = VOICE.reduce((m, v) => {
    const d = interpolate(f, [v.from - 6, v.from + 2, v.to - 2, v.to + 10], [0, 1, 1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
    return Math.max(m, d);
  }, 0);
  return musicVolume2 * ends * (1 - (1 - duckTo) * under);
};

// The order a student lives it: the notes, then playing with what's in them, then what's under the hood, and the
// same promise and ending as the first video, opened and closed on the night desktop (scenes/Intro2, scenes/Outro2).
const SCENES: [Scene2, React.FC][] = [
  ['opener', Intro2],
  ['notes', NotesFirst],
  ['explore', Explore],
  ['recall', Recall],
  ['kinds', Kinds],
  ['drawings', Drawings],
  ['plots', Plots],
  ['quiet', Quiet],
  ['proof', Proof2],
  ['cta', Cta2],
];

/** The second video: what's new. Same desktop, window, pointer, titles and cross-fades as the first; softer effects. */
export const Promo2: React.FC = () => (
  <SfxSetProvider value={SFX2}>
  <AbsoluteFill style={{backgroundColor: colors.paper}}>
    <TransitionSeries>
      {SCENES.flatMap(([key, Scene], i) => [
        ...(i > 0 ? [<TransitionSeries.Transition key={`t-${key}`} presentation={fade()} timing={linearTiming({durationInFrames: TRANSITION})} />] : []),
        <TransitionSeries.Sequence key={key} durationInFrames={durations2[key]}>
          <Scene />
        </TransitionSeries.Sequence>,
      ])}
    </TransitionSeries>

    {/* A whoosh under each scene change, peaking halfway through the cross-fade. */}
    {SCENES.slice(1).map(([key]) => (
      <Sfx key={key} at={sceneStart2(key) - 4} name="whoosh" volume={0.18} />
    ))}

    {/* ♪ public/audio/music2.wav: the first video's score, recut to this one by `npm run audio2`. */}
    {music2 ? <Audio src={staticFile(music2)} volume={musicAt} /> : null}

    {/* The narration: src/promo2/voiceover.json, made by `npm run voice2` (ElevenLabs, or the Mac's voice with --scratch). */}
    {VOICE.map((v) => (
      <Sequence key={v.id} from={v.from} durationInFrames={v.to - v.from + 6} layout="none">
        <Audio src={staticFile(`audio/vo2/${v.id}.wav`)} volume={voiceVolume} />
      </Sequence>
    ))}
  </AbsoluteFill>
  </SfxSetProvider>
);
