import React from 'react';
import {AbsoluteFill} from 'remotion';
import {colors, fonts, text} from '../config';
import {DrawnIcon, Glimpse, NightGround, night} from '../promo2/scenes/Bookends';
import {COLUMN} from './config';

// The reel's cover (npm run reelcover → out/reel-cover.png): the name, what it does in a few words, and the pinned
// diagram with the drone behind it, all inside Instagram's safe area and inside the 3:4 the profile grid shows of it.

export const ReelCover: React.FC = () => {
  const mid = (COLUMN.left + COLUMN.right) / 2;
  return (
    <AbsoluteFill>
      <NightGround dim={0.5} glow={{x: mid, y: 1120, r: 820, a: 0.9}} />

      <Glimpse g={{kind: 'drone', x: 650, y: 1000, s: 0.74, z: 0.7, at: 0}} f={34} cx={mid} cy={1000} drift={0} />
      <Glimpse g={{kind: 'diagram', x: 452, y: 1200, s: 0.86, z: 1, at: 0}} f={34} cx={mid} cy={1000} drift={0} />

      <div style={{position: 'absolute', left: COLUMN.left, top: COLUMN.top + 30, display: 'flex', alignItems: 'center', gap: 26}}>
        <DrawnIcon size={118} trace={1} tile={1} s={1} ink={1} dot={1} sheen={0} />
        <div style={{fontFamily: fonts.display, fontWeight: 700, fontSize: 70, letterSpacing: '-0.03em', color: colors.onDesk, whiteSpace: 'nowrap'}}>{text.name}</div>
      </div>

      <div style={{position: 'absolute', left: COLUMN.left, top: COLUMN.top + 200, width: COLUMN.right - COLUMN.left, fontFamily: fonts.display, fontWeight: 700, fontSize: 100, lineHeight: 1.04, letterSpacing: '-0.035em'}}>
        <div style={{color: colors.onDesk}}>Record the lecture.</div>
        <div style={{color: night.teal}}>Get the notes.</div>
      </div>
      <div style={{position: 'absolute', left: COLUMN.left, top: COLUMN.top + 440, fontFamily: fonts.display, fontWeight: 600, fontSize: 44, letterSpacing: '-0.015em', color: night.soft, whiteSpace: 'nowrap'}}>
        {text.cta}
      </div>
    </AbsoluteFill>
  );
};
