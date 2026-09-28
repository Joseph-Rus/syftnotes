import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, spr} from '../anim';
import {Paper, useVertical} from '../components/Layout';
import {Logo} from '../components/Logo';

/** Where to get it, held long enough to read. */
export const Cta: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const icon = spr(frame, 0, {damping: 16, stiffness: 130, mass: 0.8});
  const dot = spr(frame, 10, bouncy);
  const name = spr(frame, 6, {damping: 24, stiffness: 130, mass: 1});
  const line = spr(frame, 16, {damping: 26, stiffness: 120, mass: 1});
  const link = spr(frame, 26, {damping: 26, stiffness: 120, mass: 1});
  const underline = ease(frame, 34, 58);
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: vertical ? 54 : 40}}>
        <div style={{display: 'flex', alignItems: 'center', gap: vertical ? 30 : 40, flexDirection: vertical ? 'column' : 'row'}}>
          <div style={{transform: `scale(${0.8 + 0.2 * icon})`}}>
            <Logo size={vertical ? 240 : 180} s={icon} dot={dot} />
          </div>
          <div
            style={{
              opacity: name,
              transform: `translateY(${(1 - name) * 20}px)`,
              fontFamily: fonts.display,
              fontWeight: 700,
              fontSize: vertical ? 124 : 136,
              letterSpacing: '-0.03em',
              color: colors.ink,
            }}
          >
            {text.name}
          </div>
        </div>
        <div style={{opacity: line, transform: `translateY(${(1 - line) * 14}px)`, fontFamily: fonts.display, fontWeight: 500, letterSpacing: '-0.015em', fontSize: vertical ? 56 : 50, color: '#7A8497'}}>
          {text.cta}
        </div>
        <div style={{opacity: link, transform: `translateY(${(1 - link) * 14}px)`, fontFamily: fonts.ui, fontWeight: 600, fontSize: vertical ? 54 : 50, color: colors.lagoonDeep, letterSpacing: '-0.01em'}}>
          {text.link}
          <div style={{height: 3, marginTop: 8, borderRadius: 2, background: colors.lagoon, transform: `scaleX(${underline})`, transformOrigin: 'left center'}} />
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
