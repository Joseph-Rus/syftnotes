import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../../config';
import {ease, spr} from '../../anim';
import {Paper, useVertical} from '../../components/Layout';
import {Marked} from '../../components/Marked';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';

// Quiet and light, said the way it was measured (the app's CPU benchmark on an M-series Mac): waiting, it uses under
// half a percent of one core; recording takes about a quarter of what it did. No promise about battery life.

const Row: React.FC<{p: number; label: string; value: string; vertical: boolean; children: React.ReactNode}> = ({p, label, value, vertical, children}) => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      gap: vertical ? 24 : 36,
      width: vertical ? 900 : 1180,
      padding: vertical ? '24px 30px' : '22px 34px',
      borderRadius: 22,
      background: 'rgba(255,255,255,0.62)',
      border: '1px solid rgba(25,38,63,0.08)',
      boxShadow: '0 18px 40px -26px rgba(25,38,63,0.35)',
      opacity: Math.min(1, p * 1.4),
      transform: `translateY(${(1 - p) * 24}px)`,
      boxSizing: 'border-box',
    }}
  >
    <div style={{flex: 1, minWidth: 0}}>
      <div style={{fontFamily: fonts.display, fontWeight: 600, fontSize: vertical ? 36 : 34, color: colors.ink, letterSpacing: '-0.015em'}}>{label}</div>
      <div style={{fontFamily: fonts.ui, fontSize: vertical ? 27 : 26, color: colors.ink2, marginTop: 4}}>{value}</div>
    </div>
    {children}
  </div>
);

export const Quiet: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const main = spr(frame, 2, {damping: 26, stiffness: 120, mass: 1});
  const r1 = spr(frame, 18, {damping: 24, stiffness: 120, mass: 1});
  const r2 = spr(frame, 30, {damping: 24, stiffness: 120, mass: 1});
  const note = spr(frame, 44, {damping: 26, stiffness: 120, mass: 1});
  const trace = ease(frame, 22, 60);
  const shrink = ease(frame, 40, 70);
  const W = vertical ? 300 : 360;
  // A processor trace that barely leaves the floor.
  const points = Array.from({length: 61}, (_, i) => {
    const y = 0.035 + 0.02 * Math.abs(Math.sin(i * 1.7) * Math.cos(i * 0.6)) + (i === 37 ? 0.05 : 0);
    return `${(i / 60) * W},${70 - y * 70}`;
  });
  return (
    <Paper>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: vertical ? 34 : 30, padding: vertical ? '0 72px' : 0}}>
        <div
          style={{
            opacity: main,
            transform: `translateY(${(1 - main) * 20}px)`,
            fontFamily: fonts.display,
            fontWeight: 600,
            fontSize: vertical ? 92 : 84,
            lineHeight: 1.08,
            letterSpacing: '-0.028em',
            color: colors.ink,
            textAlign: 'center',
            marginBottom: vertical ? 30 : 18,
          }}
        >
          {text2.quiet.before}
          <Marked p={ease(frame, 14, 30)}>{text2.quiet.highlight}</Marked>
          {text2.quiet.after}
        </div>
        <Row p={r1} label={text2.quietIdle[0]} value={text2.quietIdle[1]} vertical={vertical}>
          <svg width={W} height={74} viewBox={`0 0 ${W} 74`}>
            <line x1="0" y1="70" x2={W} y2="70" stroke="rgba(25,38,63,0.18)" strokeWidth="2" />
            <polyline points={points.join(' ')} fill="none" stroke={colors.lagoon} strokeWidth="3" strokeLinejoin="round" pathLength={1} strokeDasharray={1} strokeDashoffset={1 - trace} />
          </svg>
        </Row>
        <Row p={r2} label={text2.quietRecord[0]} value={text2.quietRecord[1]} vertical={vertical}>
          <div style={{width: W, display: 'flex', flexDirection: 'column', gap: 10, fontFamily: fonts.ui, fontSize: 18, color: colors.ink2}}>
            <div style={{display: 'flex', alignItems: 'center', gap: 12}}>
              <div style={{width: 58}}>before</div>
              <div style={{flex: 1, height: 16, borderRadius: 8, background: 'rgba(25,38,63,0.16)'}} />
            </div>
            <div style={{display: 'flex', alignItems: 'center', gap: 12}}>
              <div style={{width: 58, color: colors.ink, fontWeight: 600}}>now</div>
              <div style={{flex: 1, height: 16}}>
                <div style={{width: `${100 - 75 * shrink}%`, height: 16, borderRadius: 8, background: colors.lagoon}} />
              </div>
            </div>
          </div>
        </Row>
        <div style={{opacity: note, fontFamily: fonts.ui, fontSize: vertical ? 24 : 22, color: '#7A8497', marginTop: 4}}>{text2.quietNote}</div>
      </AbsoluteFill>
      <Sfx at={13} name="marker" volume={0.28} />
    </Paper>
  );
};
