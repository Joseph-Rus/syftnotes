import React from 'react';
import {AbsoluteFill, Audio, interpolate, staticFile} from 'remotion';
import {linearTiming, springTiming, TransitionSeries} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {slide} from '@remotion/transitions/slide';
import {wipe} from '@remotion/transitions/wipe';
import {colors, durations, music, musicVolume, TOTAL_FRAMES, TRANSITION} from './config';
import {Hook} from './scenes/Hook';
import {Reveal} from './scenes/Reveal';
import {RecordNotes} from './scenes/RecordNotes';
import {Diagrams} from './scenes/Diagrams';
import {AskNotes} from './scenes/AskNotes';
import {CanvasDue} from './scenes/CanvasDue';
import {Proof} from './scenes/Proof';
import {Cta} from './scenes/Cta';

const glide = springTiming({config: {damping: 200}, durationInFrames: TRANSITION});

/** The whole video. The same scenes lay themselves out for landscape (1920×1080) or vertical (1080×1920). */
export const Promo: React.FC = () => (
  <AbsoluteFill style={{backgroundColor: colors.night}}>
    <TransitionSeries>
      <TransitionSeries.Sequence durationInFrames={durations.hook}>
        <Hook />
      </TransitionSeries.Sequence>
      {/* No transition here: the reveal opens its paper out of the hook's night itself. */}
      <TransitionSeries.Sequence durationInFrames={durations.reveal}>
        <Reveal />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={wipe({direction: 'from-right'})} timing={glide} />
      <TransitionSeries.Sequence durationInFrames={durations.record}>
        <RecordNotes />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={slide({direction: 'from-right'})} timing={glide} />
      <TransitionSeries.Sequence durationInFrames={durations.diagrams}>
        <Diagrams />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={slide({direction: 'from-right'})} timing={glide} />
      <TransitionSeries.Sequence durationInFrames={durations.ask}>
        <AskNotes />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={slide({direction: 'from-right'})} timing={glide} />
      <TransitionSeries.Sequence durationInFrames={durations.canvas}>
        <CanvasDue />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={wipe({direction: 'from-bottom'})} timing={glide} />
      <TransitionSeries.Sequence durationInFrames={durations.proof}>
        <Proof />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={fade()} timing={linearTiming({durationInFrames: TRANSITION})} />
      <TransitionSeries.Sequence durationInFrames={durations.cta}>
        <Cta />
      </TransitionSeries.Sequence>
    </TransitionSeries>

    {/* ♪ Background music goes here: set `music` in src/config.ts to a file in public/ (e.g. 'music.mp3'). */}
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
