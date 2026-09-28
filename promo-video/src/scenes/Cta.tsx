import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, rise, spr} from '../anim';
import {Paper, useVertical} from '../components/Layout';
import {Logo} from '../components/Logo';

/** The ask, held long enough to read: the name, what it costs, and where to get it. */
export const Cta: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const icon = spr(frame, 0, {damping: 14, stiffness: 130, mass: 0.8});
  const dot = spr(frame, 10, bouncy);
  const name = spr(frame, 6);
  const line = spr(frame, 16);
  const link = spr(frame, 26);
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: vertical ? 56 : 44}}>
        <div style={{display: 'flex', alignItems: 'center', gap: vertical ? 30 : 44, flexDirection: vertical ? 'column' : 'row'}}>
          <div style={{transform: `scale(${0.75 + 0.25 * icon})`}}>
            <Logo size={vertical ? 260 : 200} s={icon} dot={dot} />
          </div>
          <div
            style={{
              ...rise(name, 40),
              fontFamily: fonts.display,
              fontWeight: 700,
              fontSize: vertical ? 132 : 150,
              letterSpacing: '-0.03em',
              color: colors.ink,
            }}
          >
            {text.name}
          </div>
        </div>
        <div style={{...rise(line, 30), fontFamily: fonts.serif, fontSize: vertical ? 60 : 56, color: colors.ink2, textAlign: 'center', padding: '0 60px'}}>
          {text.cta}
        </div>
        <div
          style={{
            ...rise(link, 30),
            display: 'flex',
            alignItems: 'center',
            gap: 18,
            padding: vertical ? '24px 40px' : '22px 44px',
            borderRadius: 999,
            background: colors.lagoon,
            color: '#fff',
            fontFamily: fonts.ui,
            fontWeight: 600,
            fontSize: vertical ? 42 : 44,
            boxShadow: '0 20px 50px -18px rgba(0, 143, 144, 0.7)',
          }}
        >
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 4v11M7 10.5l5 5 5-5M5 19.5h14" />
          </svg>
          {text.link}
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
