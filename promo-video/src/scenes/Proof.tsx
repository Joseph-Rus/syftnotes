import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {ease, spr} from '../anim';
import {Paper, useVertical} from '../components/Layout';
import {Marked} from '../components/Marked';

/** The promise, said plainly. */
export const Proof: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const main = spr(frame, 2, {damping: 26, stiffness: 120, mass: 1});
  const sub = spr(frame, 30, {damping: 26, stiffness: 120, mass: 1});
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 40, padding: vertical ? '0 72px' : '0 160px'}}>
        <div
          style={{
            opacity: main,
            transform: `translateY(${(1 - main) * 20}px)`,
            fontFamily: fonts.serif,
            fontSize: vertical ? 92 : 96,
            lineHeight: 1.12,
            letterSpacing: '-0.015em',
            color: colors.ink,
            textAlign: 'center',
          }}
        >
          {text.proof.before}
          <Marked p={ease(frame, 22, 42)}>{text.proof.highlight}</Marked>
          {text.proof.after}
        </div>
        <div style={{opacity: sub, transform: `translateY(${(1 - sub) * 14}px)`, fontFamily: fonts.ui, fontSize: vertical ? 38 : 34, color: colors.ink2, textAlign: 'center'}}>
          {text.proofLine}
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
