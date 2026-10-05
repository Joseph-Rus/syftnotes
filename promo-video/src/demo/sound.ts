// How loud each of the demo's sounds plays. Every effect file in public/audio/demo/sfx is written at the same peak
// (by `npm run demo-audio`, from Joey's ElevenLabs files in elevenlabs3/ or the ones made in code), so these gains
// alone set the balance: all soft, well under the voice. Setting one to 0 leaves that effect out. No bells, no chimes.
export const SFX_GAIN: Record<string, number> = {
  'whoosh-soft': 0.36, // a very soft whoosh into some scenes
  click: 0.12, // the pointer's clicks: barely there
  'record-start': 0.3, // recording starts
  pop: 0.3, // something settling into place
  shimmer: 0.3, // the diagram arriving in the notes
  slider: 0.22, // a slider's knob dragged
  keys: 0.2, // typing a question
  tap: 0.14, // a tap on the phone
  swell: 0.42, // the end card
};

export const VOICE_VOLUME = 1;
export const MUSIC_VOLUME = 0.55;
export const DUCK_TO = 0.36; // the music dips to this share of its level under the voice
