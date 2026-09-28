// Everything you'd want to change in the video lives here: the words, the colours and the timing.
// Durations are in frames (30 per second). Scenes overlap by TRANSITION frames where one slides into the next.

export const FPS = 30;

export const colors = {
  // Study Stash's own palette (site/README.md, engine/src/StudyStash.App/ColourThemes.cs).
  lagoon: '#008F90', // the default accent, "Lagoon"
  lagoonBright: '#2BB5B2', // Lagoon on a dark background
  lagoonSoft: 'rgba(0, 143, 144, 0.16)',
  paper: '#FAF6EF', // the warm paper of the icon
  paper2: '#F1ECE2',
  navy: '#234077', // the icon's S
  ink: '#19263F',
  ink2: '#4A5670',
  night: '#0D1626',
  night2: '#16213A',
  nightInk: '#F3F0E8',
  nightInk2: '#A9B3C7',
  nightInk3: '#6F7B93',
  highlighter: 'rgba(251, 221, 103, 0.8)',
  glass: 'rgba(28, 38, 60, 0.72)',
  glassEdge: 'rgba(255, 255, 255, 0.09)',
  // Class colours, as the app's sidebar shows them.
  cs: '#4C8DF6',
  bio: '#3FB26F',
  calc: '#9B6BF2',
  hist: '#E39A3B',
  health: '#E0607E',
};

export const fonts = {
  display: 'Inter Display, Inter, sans-serif',
  ui: 'Inter, sans-serif',
  // Notes are set in a serif in the app (New York on a Mac, then Charter or Georgia).
  serif: '"New York", Charter, "Iowan Old Style", Georgia, serif',
};

export const text = {
  hook: ['Hours of lectures every week.', "Notes you'll never reread."],
  name: 'Study Stash',
  tagline: {before: 'Your lectures, ', highlight: 'written up', after: ' and filed by class.'},
  record: 'Record once. Notes write themselves.',
  diagrams: 'Diagrams and formulas, drawn for you.',
  ask: 'Ask your notes anything.',
  canvas: 'Canvas, right next to your notes.',
  proof: 'Everything stays on your computer.',
  proofChips: ['Free', 'Open source', 'Mac & Windows'],
  cta: 'Get it free for Mac and Windows.',
  link: 'github.com/Joseph-Rus/study-stash',
};

export const TRANSITION = 14;

export const durations = {
  hook: 96,
  reveal: 114,
  record: 174,
  diagrams: 174,
  ask: 170,
  canvas: 150,
  proof: 105,
  cta: 150,
};

// Total length: every scene, less the overlaps of the six transitions (the hook cuts straight into the reveal).
export const TOTAL_FRAMES =
  Object.values(durations).reduce((a, b) => a + b, 0) - 6 * TRANSITION;

// Background music: put a track in public/ (e.g. public/music.mp3) and set this to its file name.
// It fades in over half a second and out over the last second. Scene changes fall roughly every 5–6 seconds.
export const music: string | null = null;
export const musicVolume = 0.7;
