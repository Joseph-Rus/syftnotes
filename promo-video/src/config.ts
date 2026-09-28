import timeline from './timeline.json';
import voice from './voice.json';
import voiceover from './voiceover.json';

// Everything you'd want to change in the video lives here: the words, the colours and the timing.
// Durations are in frames (30 per second). Scenes cross-fade into each other over TRANSITION frames.

export const FPS = 30;

export const colors = {
  // Study Stash's own palette (site/README.md, engine/src/StudyStash.App/ColourThemes.cs) and its light look.
  lagoon: '#0E9594', // the teal of a chosen row
  lagoonDeep: '#007F80',
  lagoonTint: 'rgba(14, 149, 148, 0.10)',
  paper: '#FAF6EF', // the warm paper of the icon and the website
  paper2: '#F2EDE3',
  navy: '#234077', // the icon's S
  ink: '#19263F', // captions
  ink2: '#4A5670',
  highlighter: 'rgba(251, 221, 103, 0.85)',
  // The app in dark mode, as its own screenshot tests draw it (MacLibrary, MacPanel, MacQuick in dark).
  text: '#EEF2F6',
  text2: '#9AA7B6',
  text3: '#768496',
  line: 'rgba(255, 255, 255, 0.08)',
  edge: 'rgba(255, 255, 255, 0.10)',
  window: '#0F1A21',
  pane: 'rgba(255, 255, 255, 0.035)',
  hover: 'rgba(255, 255, 255, 0.09)',
  card: 'rgba(255, 255, 255, 0.055)',
  popup: 'linear-gradient(180deg, #1C2A42 0%, #162137 100%)',
  serifInk: '#E6E9EE',
  lagoonBright: '#34C1BD',
  // The desktop picture: a night-blue sky and a glossy silk wave, in the style of a macOS wallpaper.
  desk: {
    sky: ['#03041A', '#0E0F55', '#2A1E9C', '#5B45C9'],
    wave: ['#1D2BB8', '#2F46E0', '#1A249A'],
    rim: '#C9D4FF',
    dune: ['#6F7FE0', '#3444BE'],
    far: '#2B3AB0',
  },
  // Text on the desktop: white, as macOS draws the menu bar over a dark picture.
  onDesk: '#FFFFFF',
  onDeskSoft: 'rgba(214, 221, 255, 0.66)',
  // Class colours, as the sidebar shows them.
  cs: '#3B82F6',
  bio: '#22A45D',
  calc: '#8B5CF6',
  hist: '#E08A2E',
  health: '#E0527A',
};

export const fonts = {
  ui: 'Inter, sans-serif',
  display: 'Inter Display, Inter, sans-serif',
  // The serif the app writes notes in (New York on a Mac, then Charter or Georgia): only inside the app's windows.
  serif: '"New York", Charter, "Iowan Old Style", Georgia, serif',
};

export const text = {
  hook: ['Hours of lectures every week.', "Notes you'll never reread."],
  name: 'Study Stash',
  tagline: {before: 'Your lectures, ', highlight: 'written up', after: ' and filed by class.'},
  // Each caption is [the part in ink, the part in grey].
  record: ['Record once.', 'The notes write themselves.'],
  diagrams: ['Diagrams and formulas,', 'drawn for you.'],
  ask: ['Ask your notes.', 'With Claude or OpenAI.'],
  canvas: ['Canvas,', 'right next to your notes.'],
  // True whichever AI writes the notes: Whisper transcribes on the computer, so the audio never leaves it.
  proof: {before: 'Your recordings stay on ', highlight: 'your computer', after: '.'},
  proofLine: 'Free and open source, for Mac and Windows.',
  cta: 'Free for Mac and Windows.',
  link: 'study-stash-app.web.app',
};

export const TRANSITION = 12;

// The shortest each scene may be. Every scene starts on a beat of the music (100 BPM: a beat is 18 frames), so the
// cuts land on the rhythm. `npm run voice` lengthens scenes to fit the narration (writing src/timeline.json), still on
// the beat, and remakes the music; change a length here and run `npm run audio` so the music follows.
export const baseDurations = {
  hook: 102,
  reveal: 102,
  record: 192,
  diagrams: 192,
  ask: 174,
  canvas: 138,
  proof: 84,
  cta: 144,
};

export const durations: typeof baseDurations = {
  ...baseDurations,
  ...((timeline as {durations?: Partial<typeof baseDurations>}).durations ?? {}),
};

// Total length: every scene, less the seven cross-fades between them.
export const TOTAL_FRAMES = Object.values(durations).reduce((a, b) => a + b, 0) - 7 * TRANSITION;

// Music and sound effects, made from scratch by scripts/make_audio.py (`npm run audio`): nothing sampled or
// downloaded. To use a track of your own instead, put it in public/ and name it here; set it to null for silence.
export const music: string | null = 'audio/music.wav';
export const musicVolume = 0.55;
export const sfxVolume = 1; // every sound effect, together

// The narration (src/voiceover.json, made by `npm run voice`): each line, where it starts in its scene, and how long.
// The music dips to `duckTo` of its level under the voice.
export const voiceVolume = 1;
export const duckTo = 0.38;
export const voiceLines = (voiceover.lines as {id: keyof typeof baseDurations; at: number}[])
  .map((l) => ({...l, seconds: (voice.seconds as Record<string, number>)[l.id]}))
  .filter((l) => l.seconds !== undefined);

/** The frame each scene starts on, with the cross-fades. */
export const sceneStart = (id: keyof typeof baseDurations) => {
  let f = 0;
  for (const k of Object.keys(durations) as (keyof typeof baseDurations)[]) {
    if (k === id) return f;
    f += durations[k] - TRANSITION;
  }
  return f;
};
