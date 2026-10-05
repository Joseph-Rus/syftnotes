import plan from './plan.json';
import sound from './sound.json';
import {Cut, DemoScene} from '../demo/config';
import {StageFit} from '../promo2/ui';

// The Instagram reel: a fast cut of the demo, under a minute (57.5 s), 1080×1920 at 30 fps (src/reel/plan.json is its contract,
// and its times are final). It plays the demo's own scenes, each given a Cut: its moments from the plan, and its place
// inside Instagram's safe area.

export const FPS = 30;
export const WIDTH = 1080;
export const HEIGHT = 1920;
export const TRANSITION = plan.transition; // the cross-fade between scenes, in frames

/** Where Instagram draws over a reel: its top bar, the caption and buttons along the bottom, and the like, comment and
 * share buttons down the right. Nothing that matters goes there. */
export const SAFE = {top: plan.safeArea.top, bottom: HEIGHT - plan.safeArea.bottom, right: WIDTH - plan.safeArea.right};
/** The column the reel's words and windows keep to, inside the safe area. */
export const COLUMN = {left: 72, right: 946, top: 236, bottom: 1490};

/** The library window, on every scene that shows it: as wide as the column, under the headline. */
export const FIT: StageFit = {z: (COLUMN.right - 70) / 784, left: 70, top: 420, bottom: COLUMN.bottom};

const f = (s: number) => Math.round(s * FPS);

type PlanScene = {id: DemoScene; start: number; vo: number; line: string; title: [string, string] | null; titleAt?: 'top' | 'bottom'};
type PlanMark = {key: string; scene: DemoScene; at: number; len?: number};
const scenes = plan.scenes as PlanScene[];
const marks = [...plan.effects, ...plan.moments] as PlanMark[];

export const TOTAL = f(plan.end);

export type Placed = {id: DemoScene; from: number; duration: number; vo: number; title: [string, string] | null; titleAt: 'top' | 'bottom'};
/** Each scene where it plays: its first frame, and its length with the cross-fade into the next. */
export const SCENES: Placed[] = scenes.map((s, i) => {
  const from = f(s.start);
  const last = i + 1 === scenes.length;
  const next = last ? TOTAL : f(scenes[i + 1].start);
  return {id: s.id, from, duration: next - from + (last ? 0 : TRANSITION), vo: f(s.vo) - from, title: s.title, titleAt: s.titleAt ?? 'top'};
});

/** A scene's cut: its moments (and its line's start, `vo`) in frames from its own start, and its length. */
export const cutOf = (id: DemoScene): Cut => {
  const s = SCENES.find((x) => x.id === id)!;
  const at: Record<string, number> = {vo: s.vo};
  const len: Record<string, number> = {};
  for (const m of marks) {
    if (m.scene !== id) continue;
    at[m.key] = f(m.at) - s.from;
    if (m.len) len[m.key] = f(m.len);
  }
  return {at, len, length: s.duration};
};

// The sound, once `npm run reel-audio` has made it from Joey's files (src/reel/sound.json); until then, none.
export type Voice = {id: DemoScene; from: number; frames: number; file: string};
export type Effect = {key: string; sfx: string; from: number; frames: number | null; file: string};
export const VOICE = sound.voice as Voice[];
export const EFFECTS = sound.effects as Effect[];
