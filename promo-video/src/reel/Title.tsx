import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../config';
import {prog, px} from '../promo2/scenes/Bookends';
import {COLUMN, HEIGHT, SAFE, WIDTH} from './config';

/**
 * The reel's headline: two short lines, the first in white and the second in grey, big enough to read on a phone, in
 * the column inside Instagram's safe area: at its top over the app, or at its bottom (Record, where the Mac's menu bar
 * fills the top). Each line rises into place and stops there exactly (no springs).
 */
export const ReelTitle: React.FC<{text: [string, string]; at: 'top' | 'bottom'; delay?: number}> = ({text, at, delay = 6}) => {
  const f = useCurrentFrame();
  const p = prog(f, delay, delay + 14);
  const q = prog(f, delay + 5, delay + 19);
  const line = (v: number, color: string): React.CSSProperties => ({color, whiteSpace: 'nowrap', opacity: v, transform: `translateY(${px((1 - v) * 16)}px)`});
  return (
    <div
      style={{
        position: 'absolute',
        left: COLUMN.left,
        width: COLUMN.right - COLUMN.left,
        top: at === 'top' ? COLUMN.top : undefined,
        bottom: at === 'bottom' ? HEIGHT - COLUMN.bottom : undefined,
        fontFamily: fonts.display,
        fontWeight: 600,
        fontSize: 74,
        lineHeight: 1.06,
        letterSpacing: '-0.025em',
        textShadow: '0 2px 18px rgba(3, 4, 26, 0.5)',
        zIndex: 40,
      }}
    >
      <div style={line(p, colors.onDesk)}>{text[0]}</div>
      <div style={line(q, colors.onDeskSoft)}>{text[1]}</div>
    </div>
  );
};

/** Where Instagram draws over a reel, shaded (the Reel's `guides` prop in the Studio, for checking a frame). */
export const SafeGuides: React.FC = () => {
  const shade = 'rgba(255, 40, 80, 0.28)';
  return (
    <AbsoluteFill style={{pointerEvents: 'none', zIndex: 100}}>
      <div style={{position: 'absolute', left: 0, top: 0, width: WIDTH, height: SAFE.top, background: shade}} />
      <div style={{position: 'absolute', left: 0, top: SAFE.bottom, width: WIDTH, height: HEIGHT - SAFE.bottom, background: shade}} />
      <div style={{position: 'absolute', left: SAFE.right, top: SAFE.top, width: WIDTH - SAFE.right, height: SAFE.bottom - SAFE.top, background: shade}} />
      <div style={{position: 'absolute', left: COLUMN.left, top: COLUMN.top, width: COLUMN.right - COLUMN.left, height: COLUMN.bottom - COLUMN.top, boxShadow: 'inset 0 0 0 2px rgba(80, 255, 160, 0.6)'}} />
    </AbsoluteFill>
  );
};
