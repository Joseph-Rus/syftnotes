import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, rise, spr} from '../anim';
import {Headline, Paper, useVertical} from '../components/Layout';

/** A laptop whose screen holds a lock: the audio and notes never leave it. */
const Laptop: React.FC<{frame: number; size: number}> = ({frame, size}) => {
  const draw = ease(frame, 2, 26);
  const lock = spr(frame, 18, bouncy);
  const line = {fill: 'none', stroke: colors.navy, strokeWidth: 7, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, pathLength: 1, strokeDasharray: 1, strokeDashoffset: 1 - draw};
  return (
    <svg width={size} height={size * 0.7} viewBox="0 0 300 210">
      <rect x="50" y="20" width="200" height="135" rx="14" {...line} />
      <path d="M20 180h260l-18 16H38z" {...line} />
      <g transform={`translate(150 88) scale(${lock})`} opacity={Math.min(1, lock * 2)}>
        <rect x="-26" y="-6" width="52" height="42" rx="9" fill={colors.lagoon} />
        <path d="M-15 -6v-13a15 15 0 0 1 30 0v13" fill="none" stroke={colors.lagoon} strokeWidth="8" strokeLinecap="round" />
        <circle cx="0" cy="13" r="5" fill="#fff" />
      </g>
    </svg>
  );
};

export const Proof: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: vertical ? 50 : 40, padding: 80}}>
        <div style={rise(spr(frame, 0), 30)}>
          <Laptop frame={frame} size={vertical ? 360 : 280} />
        </div>
        <Headline text={text.proof} size={vertical ? 100 : 92} color={colors.ink} align="center" delay={6} maxWidth={vertical ? 900 : 1760} />
        <div style={{display: 'flex', gap: 22, flexWrap: 'wrap', justifyContent: 'center'}}>
          {text.proofChips.map((chip, i) => {
            const p = spr(frame, 34 + i * 7, bouncy);
            return (
              <div
                key={chip}
                style={{
                  transform: `scale(${p})`,
                  opacity: Math.min(1, p * 2),
                  padding: '16px 34px',
                  borderRadius: 999,
                  border: `3px solid ${colors.lagoon}`,
                  color: colors.lagoon,
                  background: '#fff',
                  fontFamily: fonts.ui,
                  fontWeight: 700,
                  fontSize: vertical ? 44 : 40,
                }}
              >
                {chip}
              </div>
            );
          })}
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
