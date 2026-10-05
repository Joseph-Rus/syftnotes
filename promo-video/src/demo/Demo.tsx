import React from 'react';
import {AbsoluteFill, Audio, interpolate, Sequence, staticFile} from 'remotion';
import {linearTiming, TransitionSeries} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {SfxName, SfxSetProvider} from '../components/Sfx';
import {night} from '../promo2/scenes/Bookends';
import {DemoScene, EFFECTS, MUSIC, SCENES, TOTAL, TRANSITION, VOICE} from './config';
import {DUCK_TO, MUSIC_VOLUME, SFX_GAIN, VOICE_VOLUME} from './sound';
import {Opener} from './scenes/Opener';
import {Setup} from './scenes/Setup';
import {Record} from './scenes/Record';
import {Notes} from './scenes/Notes';
import {Explore} from './scenes/Explore';
import {Plots} from './scenes/Plots';
import {Drawings} from './scenes/Drawings';
import {Ask} from './scenes/Ask';
import {Canvas} from './scenes/Canvas';
import {Phone} from './scenes/Phone';
import {Settings} from './scenes/Settings';
import {AiApps} from './scenes/AiApps';
import {Promise3} from './scenes/Promise';
import {End} from './scenes/End';

const SCENE: Record<DemoScene, React.FC> = {
  opener: Opener,
  setup: Setup,
  record: Record,
  notes: Notes,
  explore: Explore,
  plots: Plots,
  drawings: Drawings,
  ask: Ask,
  canvas: Canvas,
  phone: Phone,
  settings: Settings,
  aiapps: AiApps,
  promise: Promise3,
  end: End,
};

// Every sound comes from src/demo/timeline.json, so it lands where the plan says; the pointer's own clicks (the shared
// Cursor) are silenced here.
const NAMES: SfxName[] = ['click', 'key1', 'key2', 'key3', 'pop', 'whoosh', 'marker', 'chime-record', 'chime-filed', 'tick', 'ding'];
const SILENT = {folder: 'audio/sfx2', gain: Object.fromEntries(NAMES.map((n) => [n, null]))};

const clamp = {extrapolateLeft: 'clamp' as const, extrapolateRight: 'clamp' as const};

/** How loud the music is at frame `f`: its level, dipping under the voice, fading in and out at the ends. */
const musicAt = (f: number) => {
  const ends = interpolate(f, [0, 8, TOTAL - 45, TOTAL], [0, 1, 1, 0], clamp);
  const under = VOICE.reduce((m, v) => {
    const d = interpolate(f, [v.from - 8, v.from + 2, v.from + v.frames - 2, v.from + v.frames + 12], [0, 1, 1, 0], clamp);
    return Math.max(m, d);
  }, 0);
  return MUSIC_VOLUME * ends * (1 - (1 - DUCK_TO) * under);
};

/**
 * The demo: the night desktop, the app as it is, and the narration it was timed to (src/demo/plan.json). `silent` is the
 * picture alone (DemoSilent, DemoSilentVertical): no narration, no effects, no music, for Joey to lay his own sound
 * under, by CUES.md.
 */
export const Demo: React.FC<{silent?: boolean}> = ({silent = false}) => (
  <SfxSetProvider value={SILENT}>
    <AbsoluteFill style={{backgroundColor: night.ground}}>
      <TransitionSeries>
        {SCENES.flatMap(({id, duration}, i) => {
          const Scene = SCENE[id];
          return [
            ...(i > 0 ? [<TransitionSeries.Transition key={`t-${id}`} presentation={fade()} timing={linearTiming({durationInFrames: TRANSITION})} />] : []),
            <TransitionSeries.Sequence key={id} durationInFrames={duration}>
              <Scene />
            </TransitionSeries.Sequence>,
          ];
        })}
      </TransitionSeries>

      {silent ? null : EFFECTS.map((e) => {
        const gain = SFX_GAIN[e.sfx] ?? 0;
        if (gain <= 0) return null;
        const frames = e.frames ?? 180;
        // A sound that lasts as long as its moment (a slider dragged, a question typed) fades out as the moment ends.
        const volume = e.frames ? (f: number) => gain * interpolate(f, [frames - 6, frames], [1, 0], clamp) : gain;
        return (
          <Sequence key={e.key} from={e.from} durationInFrames={frames} layout="none">
            <Audio src={staticFile(e.file)} volume={volume} />
          </Sequence>
        );
      })}

      {MUSIC && !silent ? <Audio src={staticFile(MUSIC)} volume={musicAt} /> : null}

      {silent ? null : VOICE.map((v) => (
        <Sequence key={v.id} from={v.from} durationInFrames={v.frames + 10} layout="none">
          <Audio src={staticFile(v.file)} volume={VOICE_VOLUME} />
        </Sequence>
      ))}
    </AbsoluteFill>
  </SfxSetProvider>
);
