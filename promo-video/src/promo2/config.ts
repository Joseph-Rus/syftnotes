import timeline from './timeline.json';
import voice from './voice.json';
import voiceover from './voiceover.json';

// The second video: what's new (diagrams you can play with, drawings, formulas that move, a lighter app).
// It shares the first one's palette, fonts, window, pointer, titles and sound (src/config.ts); everything that's its
// own lives here: the words, and each scene's length in frames (30 = one second).

export const text2 = {
  opener: {before: 'Your notes, now with diagrams ', highlight: 'you can play with', after: '.'},
  // Each caption is [the part in ink, the part in grey].
  notes: ['Notes first.', 'Diagrams a couple of minutes later.'],
  explore: ['Point at a box.', 'See what it connects to.'],
  recall: ['Test yourself.', 'Hide the words, then recall\u00a0them.'], // no lone last word on a phone
  kinds: ['More kinds.', 'Automata, sequences, timelines, mind maps.'],
  drawings: ['Drawings of real things.', 'Detailed and labelled.'],
  plots: ['Formulas that move.', 'Drag a slider; the curve\u00a0follows.'],
  // Measured on an M-series Mac (the app's own CPU benchmark): idle under 0.5% of one core, recording about a quarter
  // of its old cost. Said as "light" and "quiet": no promise about battery.
  quiet: {before: '', highlight: 'Light', after: ' on your computer.'},
  quietIdle: ['Waiting', 'under half a percent of one core'],
  quietRecord: ['Recording', 'about a quarter of what it used to take'],
  quietNote: 'Measured on an M-series Mac.',
};

export const TRANSITION = 12;

// The shortest each scene may be. Every scene starts on a beat of the music (100 BPM: a beat is 18 frames), so each
// length but the last is whole beats plus the cross-fade. `npm run voice2` lengthens scenes to fit the narration
// (writing src/promo2/timeline.json) and remakes the music; change a length here and run `npm run audio2`.
export const baseDurations = {
  opener: 102,
  notes: 192,
  explore: 210,
  recall: 138,
  kinds: 138,
  drawings: 192,
  plots: 228,
  quiet: 102,
  proof: 84,
  cta: 144,
};

export type Scene2 = keyof typeof baseDurations;

export const durations2: typeof baseDurations = {
  ...baseDurations,
  ...((timeline as {durations?: Partial<typeof baseDurations>}).durations ?? {}),
};

const COUNT = Object.keys(durations2).length;
export const TOTAL_FRAMES2 = Object.values(durations2).reduce((a, b) => a + b, 0) - (COUNT - 1) * TRANSITION;

export const music2: string | null = 'audio/music2.wav';
export const musicVolume2 = 0.6; // a touch above the first video's 0.55: the narration sits a little higher too
export const CLICK = 0.42; // the pointer's clicks: this video has many, close together

// The narration (src/promo2/voiceover.json, made by `npm run voice2`): each line, where it starts in its scene, how long.
export const voiceLines2 = (voiceover.lines as {id: Scene2; at: number}[])
  .map((l) => ({...l, seconds: (voice.seconds as Record<string, number>)[l.id]}))
  .filter((l) => l.seconds !== undefined);

/** The frame each scene starts on, with the cross-fades. */
export const sceneStart2 = (id: Scene2) => {
  let f = 0;
  for (const k of Object.keys(durations2) as Scene2[]) {
    if (k === id) return f;
    f += durations2[k] - TRANSITION;
  }
  return f;
};
