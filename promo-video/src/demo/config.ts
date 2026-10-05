import plan from './plan.json';
import timeline from './timeline.json';

// The third video: a full demo of Study Stash, about 2 minutes 12 seconds.
//
// src/demo/plan.json is the contract it is built to: when each scene starts, when its line of narration starts, and
// every sound effect's moment. `npm run demo-audio` fits the narration to it and writes src/demo/timeline.json, the
// final placement (a scene whose line runs long is stretched on the beat, and everything after it moves). The scenes
// read their own moments from the plan, relative to their own start, so a stretch never pulls a click off its moment.

export const FPS = 30;
export const TRANSITION = timeline.transition; // the cross-fade between scenes, in frames

export type DemoScene =
  | 'opener' | 'setup' | 'record' | 'notes' | 'explore' | 'plots' | 'drawings'
  | 'ask' | 'canvas' | 'phone' | 'settings' | 'aiapps' | 'promise' | 'end';

type PlanScene = {id: DemoScene; start: number; vo: number; line: string};
type PlanEffect = {key: string; scene: DemoScene; sfx: string; at: number; len?: number; listed: boolean};
const scenes = plan.scenes as PlanScene[];
const effects = plan.effects as PlanEffect[];

type Placed = {id: DemoScene; from: number; duration: number};
export const SCENES: Placed[] = (timeline.scenes as Placed[]).map((s) => ({id: s.id, from: s.from, duration: s.duration}));
export const TOTAL = timeline.total;

/** Where a scene sits in the whole video, in frames. */
export const scene = (id: DemoScene) => SCENES.find((s) => s.id === id)!;

/** A moment of the plan (an effect's key), in frames from its own scene's start. */
export const cue = (key: string) => {
  const e = effects.find((x) => x.key === key);
  if (!e) throw new Error(`No moment "${key}" in src/demo/plan.json`);
  const s = scenes.find((x) => x.id === e.scene)!;
  return Math.round((e.at - s.start) * FPS);
};

/** How long a moment lasts (a slider dragged, the keys typed), in frames. */
export const cueLength = (key: string) => {
  const e = effects.find((x) => x.key === key)!;
  return Math.round((e.len ?? 0) * FPS);
};

/** A time of the plan (seconds, as plan.json counts them) in frames from the start of scene `id`. */
export const at = (id: DemoScene, seconds: number) => {
  const s = scenes.find((x) => x.id === id)!;
  return Math.round((seconds - s.start) * FPS);
};

/** When a scene's line of narration starts, in frames from the scene's start. */
export const voAt = (id: DemoScene) => {
  const s = scenes.find((x) => x.id === id)!;
  return Math.round((s.vo - s.start) * FPS);
};

/** A scene's length in frames, with the cross-fade into the next. */
export const length = (id: DemoScene) => scene(id).duration;

export type Voice = {id: DemoScene; from: number; frames: number; file: string};
export const VOICE = timeline.voice as Voice[];
export type Effect = {key: string; sfx: string; from: number; frames: number | null; listed: boolean; file: string};
export const EFFECTS = timeline.effects as Effect[];
export const MUSIC: string | null = timeline.music ?? null;

// The headline over each scene: the first part in white, the rest in grey (the first two videos' Title).
export const titles: Partial<Record<DemoScene, [string, string]>> = {
  // Each fits on one line over a landscape window (about 46 characters in all).
  setup: ['Guided setup.', 'Your AI walks you through it.'],
  record: ['Press record.', 'Words show up as they’re said.'],
  notes: ['Notes, written for you.', 'Filed by class.'],
  explore: ['Diagrams you explore.', 'Point, pin, step, test.'],
  plots: ['Graphs you can drag.', 'See what each number does.'],
  drawings: ['Labelled drawings.', 'Of what a lecture describes.'],
  ask: ['Ask your lectures.', 'Answers from what was said.'],
  canvas: ['Canvas, connected.', 'See what’s due next.'],
  phone: ['On your phone, too.', 'Notes, what’s due, and Ask.'],
  settings: ['Make it yours.', 'Rich notes, fast mode, quiet.'],
  aiapps: ['Your AI apps, connected.', 'In one click.'],
};
