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
  // The app's light windows.
  text: '#1D2330',
  text2: '#667085',
  text3: '#9AA1AE',
  line: 'rgba(16, 24, 40, 0.08)',
  window: 'rgba(248, 251, 252, 0.97)',
  pane: 'rgba(255, 255, 255, 0.72)',
  // The desktop behind the app, as in the app's screenshots.
  wallpaper: ['#B8D0F3', '#D5E5F8', '#D6CFF4'],
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
  // The serif the app writes notes in, and the website's headings (New York on a Mac, then Charter or Georgia).
  serif: '"New York", Charter, "Iowan Old Style", Georgia, serif',
};

export const text = {
  hook: ['Hours of lectures every week.', "Notes you'll never reread."],
  name: 'Study Stash',
  tagline: {before: 'Your lectures, ', highlight: 'written up', after: ' and filed by class.'},
  record: 'Record once. The notes write themselves.',
  diagrams: 'Diagrams and formulas, drawn for you.',
  ask: 'Ask your notes, with Claude or OpenAI.',
  canvas: 'Canvas, right next to your notes.',
  // True whichever AI writes the notes: Whisper transcribes on the computer, so the audio never leaves it.
  proof: {before: 'Your recordings stay on ', highlight: 'your computer', after: '.'},
  proofLine: 'Free and open source, for Mac and Windows.',
  cta: 'Free for Mac and Windows.',
  link: 'study-stash-app.web.app',
};

export const TRANSITION = 12;

export const durations = {
  hook: 96,
  reveal: 105,
  record: 186,
  diagrams: 186,
  ask: 165,
  canvas: 150,
  proof: 96,
  cta: 135,
};

// Total length: every scene, less the seven cross-fades between them.
export const TOTAL_FRAMES = Object.values(durations).reduce((a, b) => a + b, 0) - 7 * TRANSITION;

// Background music: put a track in public/ (e.g. public/music.mp3) and set this to its file name.
// It fades in over half a second and out over the last second. Scene changes fall roughly every 5–6 seconds.
export const music: string | null = null;
export const musicVolume = 0.7;
