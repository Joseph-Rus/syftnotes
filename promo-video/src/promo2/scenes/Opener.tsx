import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../../config';
import {bouncy, ease, spr} from '../../anim';
import {Paper, useVertical} from '../../components/Layout';
import {Logo} from '../../components/Logo';
import {Marked} from '../../components/Marked';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';

/** The name, as the first video's reveal shows it, with what's new: a small "New" tag and the tagline. */
export const Opener: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const icon = spr(frame, 0, {damping: 14, stiffness: 140, mass: 0.8});
  const dot = spr(frame, 12, bouncy);
  const name = spr(frame, 8, {damping: 24, stiffness: 130, mass: 1});
  const tag = spr(frame, 16, bouncy);
  const line = spr(frame, 20, {damping: 26, stiffness: 120, mass: 1});
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 34, padding: vertical ? '0 64px' : 0}}>
        <div style={{transform: `scale(${0.8 + 0.2 * icon})`}}>
          <Logo size={vertical ? 250 : 200} s={icon} dot={dot} />
        </div>
        <div style={{display: 'flex', alignItems: 'center', gap: 24, opacity: name, transform: `translateY(${(1 - name) * 24}px)`}}>
          <div style={{fontFamily: fonts.display, fontWeight: 700, fontSize: vertical ? 118 : 116, letterSpacing: '-0.03em', color: colors.ink, lineHeight: 1}}>
            {text.name}
          </div>
          <div
            style={{
              fontFamily: fonts.ui,
              fontWeight: 700,
              fontSize: vertical ? 30 : 28,
              color: '#FFFFFF',
              background: colors.lagoon,
              borderRadius: 999,
              padding: '8px 18px 9px',
              transform: `scale(${tag}) rotate(${(1 - tag) * -8}deg)`,
              opacity: Math.min(1, tag * 2),
              letterSpacing: '0.01em',
            }}
          >
            New
          </div>
        </div>
        <div
          style={{
            opacity: line,
            transform: `translateY(${(1 - line) * 16}px)`,
            fontFamily: fonts.display,
            fontWeight: 500,
            letterSpacing: '-0.015em',
            fontSize: vertical ? 56 : 50,
            color: colors.ink2,
            textAlign: 'center',
            maxWidth: vertical ? 900 : 1500,
            lineHeight: 1.25,
          }}
        >
          {text2.opener.before}
          <Marked p={ease(frame, 40, 60)}>{text2.opener.highlight}</Marked>
          {text2.opener.after}
        </div>
      </AbsoluteFill>
      <Sfx at={16} name="pop" volume={0.3} />
      <Sfx at={39} name="marker" volume={0.3} />
    </Paper>
  );
};
