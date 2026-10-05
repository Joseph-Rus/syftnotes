import React from 'react';
import {AbsoluteFill} from 'remotion';
import {colors, fonts} from '../config';
import {Wallpaper} from '../components/Layout';
import {Logo} from '../components/Logo';
import {Recorder} from '../demo/scenes/Record';

// The third video's picture for a shared link (1200×630): the same desktop; on the left what the demo shows, on the
// right the recorder at the end of a BIO 110 lecture, its live words in.
export const KofiCard3: React.FC = () => (
  <AbsoluteFill style={{fontFamily: fonts.ui, color: colors.text}}>
    <div style={{position: 'absolute', left: 0, top: 0, width: 1200, height: 900}}>
      <Wallpaper />
    </div>
    <div style={{position: 'absolute', left: 64, top: 70, width: 560}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 16}}>
        <Logo size={58} />
        <span style={{fontFamily: fonts.display, fontSize: 31, fontWeight: 600, letterSpacing: '-0.02em', color: colors.onDesk}}>Study Stash</span>
      </div>
      <div style={{marginTop: 54, fontFamily: fonts.display, fontSize: 60, fontWeight: 700, lineHeight: 1.04, letterSpacing: '-0.03em', color: colors.onDesk}}>
        Record the lecture. Get the notes.
      </div>
      <div style={{marginTop: 24, fontSize: 23, lineHeight: 1.42, color: 'rgba(224, 229, 255, 0.82)', width: 520}}>
        Words as they’re said, notes filed by class, diagrams you can explore, Ask, Canvas and your phone. Free for Mac and
        Windows.
      </div>
    </div>
    <div style={{position: 'absolute', left: 752, top: 48, transform: 'scale(1.02)', transformOrigin: '0 0'}}>
      <Recorder
        f={0}
        elapsed="49:58"
        waiting={false}
        stopPress={0}
        lines={[
          {time: '47:40', text: 'When the ventricles relax, the pressure falls and the AV valves open again.'},
          {time: '48:31', text: 'So the valves closing are what you hear: lub, then dub.'},
          {time: '49:12', text: 'For Thursday, read chapter 12, the conduction system.'},
          {time: '49:56', text: 'That’s it for today.'},
        ]}
      />
    </div>
  </AbsoluteFill>
);
