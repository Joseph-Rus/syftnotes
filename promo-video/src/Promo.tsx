import React from 'react';
import {AbsoluteFill, Audio, interpolate, staticFile} from 'remotion';
import {linearTiming, TransitionSeries} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {colors, durations, music, musicVolume, TOTAL_FRAMES, TRANSITION} from './config';
import {Sfx} from './components/Sfx';
import {Hook} from './scenes/Hook';
import {Reveal} from './scenes/Reveal';
import {RecordNotes} from './scenes/RecordNotes';
import {Diagrams} from './scenes/Diagrams';
import {AskNotes} from './scenes/AskNotes';
import {CanvasDue} from './scenes/CanvasDue';
import {Proof} from './scenes/Proof';
import {Cta} from './scenes/Cta';

const SCENES: [keyof typeof durations, React.FC][] = [
  ['hook', Hook],
  ['reveal', Reveal],
  ['record', RecordNotes],
  ['diagrams', Diagrams],
  ['ask', AskNotes],
  ['canvas', CanvasDue],
  ['proof', Proof],
  ['cta', Cta],
];

/**
 * The whole video. Every scene cross-fades into the next: the desktop stays put while what's open on it changes,
 * as it would in the app. The same scenes lay themselves out for landscape (1920×1080) or vertical (1080×1920).
 */
export const Promo: React.FC = () => (
  <AbsoluteFill style={{backgroundColor: colors.paper}}>
    <TransitionSeries>
      {SCENES.flatMap(([key, Scene], i) => [
        ...(i > 0 ? [<TransitionSeries.Transition key={`t-${key}`} presentation={fade()} timing={linearTiming({durationInFrames: TRANSITION})} />] : []),
        <TransitionSeries.Sequence key={key} durationInFrames={durations[key]}>
          <Scene />
        </TransitionSeries.Sequence>,
      ])}
    </TransitionSeries>

    {/* A whoosh under each scene change, peaking halfway through the cross-fade. */}
    {SCENES.slice(1).map(([key], i) => {
      const start = SCENES.slice(0, i + 1).reduce((f, [k]) => f + durations[k] - TRANSITION, 0);
      return <Sfx key={key} at={start - 4} name="whoosh" volume={0.18} />;
    })}

    {/* ♪ The music: public/audio/music.wav, made by scripts/make_audio.py. Set `music` in src/config.ts to use another. */}
    {music ? (
      <Audio
        src={staticFile(music)}
        volume={(f) =>
          interpolate(f, [0, 15, TOTAL_FRAMES - 30, TOTAL_FRAMES], [0, musicVolume, musicVolume, 0], {
            extrapolateLeft: 'clamp',
            extrapolateRight: 'clamp',
          })
        }
      />
    ) : null}
  </AbsoluteFill>
);
