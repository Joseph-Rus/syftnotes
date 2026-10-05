import React from 'react';
import {AbsoluteFill} from 'remotion';
import {linearTiming, TransitionSeries} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {SfxName, SfxSetProvider} from '../components/Sfx';
import {night} from '../promo2/scenes/Bookends';
import {StageFitContext} from '../promo2/ui';
import {Cut, DemoScene} from '../demo/config';
import {Opener} from '../demo/scenes/Opener';
import {Record} from '../demo/scenes/Record';
import {Notes} from '../demo/scenes/Notes';
import {Explore} from '../demo/scenes/Explore';
import {Plots} from '../demo/scenes/Plots';
import {Drawings} from '../demo/scenes/Drawings';
import {Ask} from '../demo/scenes/Ask';
import {Canvas} from '../demo/scenes/Canvas';
import {Phone} from '../demo/scenes/Phone';
import {Settings} from '../demo/scenes/Settings';
import {End} from '../demo/scenes/End';
import {cutOf, FIT, SCENES, TRANSITION} from './config';
import {ReelTitle, SafeGuides} from './Title';

const SCENE: Partial<Record<DemoScene, React.FC<{cut?: Cut}>>> = {
  opener: Opener,
  record: Record,
  notes: Notes,
  explore: Explore,
  plots: Plots,
  drawings: Drawings,
  ask: Ask,
  canvas: Canvas,
  phone: Phone,
  settings: Settings,
  end: End,
};
const CUTS = Object.fromEntries(SCENES.map((s) => [s.id, cutOf(s.id)])) as Record<DemoScene, Cut>;

// Picture only: the pointer's own clicks (the shared Cursor) are silenced too, so the reel has no sound at all.
const NAMES: SfxName[] = ['click', 'key1', 'key2', 'key3', 'pop', 'whoosh', 'marker', 'chime-record', 'chime-filed', 'tick', 'ding'];
const SILENT = {folder: 'audio/sfx2', gain: Object.fromEntries(NAMES.map((n) => [n, null]))};

/**
 * The Instagram reel (src/reel/plan.json): the demo's own scenes, each cut shorter and laid inside Instagram's safe
 * area, with the reel's own headlines. Picture only (rendered with --muted, so the file has no audio stream at all).
 * `guides` shades where Instagram draws over a reel, for checking a frame in the Studio.
 */
export const Reel: React.FC<{guides?: boolean}> = ({guides = false}) => (
  <SfxSetProvider value={SILENT}>
    <StageFitContext.Provider value={FIT}>
      <AbsoluteFill style={{backgroundColor: night.ground}}>
        <TransitionSeries>
          {SCENES.flatMap((s, i) => {
            const Scene = SCENE[s.id]!;
            return [
              ...(i > 0 ? [<TransitionSeries.Transition key={`t-${s.id}`} presentation={fade()} timing={linearTiming({durationInFrames: TRANSITION})} />] : []),
              <TransitionSeries.Sequence key={s.id} durationInFrames={s.duration}>
                <Scene cut={CUTS[s.id]} />
                {s.title ? <ReelTitle text={s.title} at={s.titleAt} /> : null}
              </TransitionSeries.Sequence>,
            ];
          })}
        </TransitionSeries>
        {guides ? <SafeGuides /> : null}
      </AbsoluteFill>
    </StageFitContext.Provider>
  </SfxSetProvider>
);
