import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, spr} from '../anim';
import {Paper, useVertical} from '../components/Layout';
import {Logo} from '../components/Logo';
import {Marked} from '../components/Marked';

/** The name: the icon arrives, its full stop drops into place, and the tagline gets the website's highlighter. */
export const Reveal: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const icon = spr(frame, 2, {damping: 14, stiffness: 140, mass: 0.8});
  const dot = spr(frame, 16, bouncy);
  const name = spr(frame, 12, {damping: 24, stiffness: 130, mass: 1});
  const line = spr(frame, 24, {damping: 26, stiffness: 120, mass: 1});
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 36}}>
        <div style={{transform: `scale(${0.8 + 0.2 * icon})`}}>
          <Logo size={vertical ? 280 : 230} s={icon} dot={dot} />
        </div>
        <div
          style={{
            opacity: name,
            transform: `translateY(${(1 - name) * 24}px)`,
            fontFamily: fonts.display,
            fontWeight: 700,
            fontSize: vertical ? 128 : 124,
            letterSpacing: '-0.03em',
            color: colors.ink,
            lineHeight: 1,
          }}
        >
          {text.name}
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
            maxWidth: vertical ? 900 : 1400,
            lineHeight: 1.25,
          }}
        >
          {text.tagline.before}
          <Marked p={ease(frame, 44, 64)}>{text.tagline.highlight}</Marked>
          {text.tagline.after}
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
