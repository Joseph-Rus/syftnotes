import {SfxSet} from '../components/Sfx';

// The second video's sound effects: few, soft and well under the voice (SFX2.md). Its own files, in
// public/audio/sfx2 (made by `npm run sfx2`, or yours from the ElevenLabs website dropped into sfx2/), each at the
// same peak; these gains set how loud each plays, times the volume a scene asks for. No bells, no ticks: an effect
// mapped to null is left out wherever a scene asks for it.
export const SFX2: SfxSet = {
  folder: 'audio/sfx2',
  gain: {
    click: 0.1, // the pointer's clicks: barely there (about −34 dBFS at their peak)
    key1: 0.18, // the Y and N keys in Test yourself
    key2: 0.18,
    key3: 0.18,
    pop: 0.33, // something settling into place: a pinned card, the diagrams added, each new kind of diagram
    whoosh: 0.56, // the cross-fades between scenes, and the two minutes passing
    marker: 0.22, // a highlighter under a word
    tick: null,
    ding: null,
    'chime-filed': null,
    'chime-record': null,
  },
};
