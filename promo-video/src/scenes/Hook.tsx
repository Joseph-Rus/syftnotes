import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {ease, spr} from '../anim';
import {Paper, TrafficLights, useVertical, Win} from '../components/Layout';

// What a term of lectures looks like without Study Stash: a pile of untitled recordings.
const RECORDINGS = Array.from({length: 18}, (_, i) => {
  const n = 31 - i;
  const days = ['Tue 23 Sep', 'Mon 22 Sep', 'Fri 19 Sep', 'Thu 18 Sep', 'Wed 17 Sep', 'Tue 16 Sep'];
  const mins = [72, 58, 75, 52, 64, 74, 55, 70, 61, 68];
  const m = mins[i % mins.length];
  return {name: `New Recording ${n}`, day: days[Math.floor(i / 3) % days.length], length: `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}:${String((i * 17) % 60).padStart(2, '0')}`};
});

const Recordings: React.FC<{frame: number}> = ({frame}) => {
  const scroll = ease(frame, 10, 96) * 520;
  return (
    <Win w={560} h={640}>
      <TrafficLights />
      <div style={{padding: '0 28px'}}>
        <div style={{fontFamily: fonts.display, fontSize: 30, fontWeight: 700, letterSpacing: '-0.02em'}}>Recordings</div>
        <div style={{fontSize: 15, color: colors.text2, marginTop: 4}}>31 recordings · no notes</div>
      </div>
      <div style={{position: 'absolute', top: 128, left: 0, right: 0, bottom: 0, overflow: 'hidden'}}>
        <div style={{transform: `translateY(${-scroll}px)`}}>
          {RECORDINGS.map((r, i) => (
            <div key={r.name} style={{display: 'flex', alignItems: 'center', gap: 16, padding: '15px 28px', borderTop: `1px solid ${colors.line}`, opacity: Math.min(1, spr(frame, i * 2) * 1.4)}}>
              <svg width="30" height="30" viewBox="0 0 24 24">
                <circle cx="12" cy="12" r="11" fill="rgba(16,24,40,0.06)" />
                <path d="M10 8l6 4-6 4z" fill={colors.text2} />
              </svg>
              <div style={{flex: 1}}>
                <div style={{fontSize: 18, fontWeight: 600}}>{r.name}</div>
                <div style={{fontSize: 14, color: colors.text2, marginTop: 2}}>{r.day}</div>
              </div>
              <div style={{fontSize: 15, color: colors.text2, fontVariantNumeric: 'tabular-nums'}}>{r.length}</div>
            </div>
          ))}
        </div>
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 0, height: 120, background: `linear-gradient(180deg, rgba(248,251,252,0), ${colors.window})`}} />
      </div>
    </Win>
  );
};

export const Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const first = spr(frame, 2, {damping: 26, stiffness: 120, mass: 1});
  const second = spr(frame, 38, {damping: 26, stiffness: 120, mass: 1});
  const win = spr(frame, 0, {damping: 24, stiffness: 110, mass: 1});
  const line: React.CSSProperties = {fontFamily: fonts.display, fontWeight: 600, fontSize: vertical ? 84 : 66, lineHeight: 1.06, letterSpacing: '-0.028em'};
  return (
    <Paper>
      <AbsoluteFill
        style={{
          flexDirection: vertical ? 'column' : 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: vertical ? 80 : 80,
          padding: vertical ? '0 72px' : '0 110px',
        }}
      >
        <div style={{width: vertical ? '100%' : 1040}}>
          <div style={{...line, color: second > 0.05 ? '#7A8497' : colors.ink, opacity: first, transform: `translateY(${(1 - first) * 20}px)`}}>
            {text.hook[0]}
          </div>
          <div style={{...line, color: colors.ink, marginTop: 18, opacity: second, transform: `translateY(${(1 - second) * 20}px)`}}>{text.hook[1]}</div>
        </div>
        <div style={{opacity: win, transform: `translateY(${(1 - win) * 40}px) rotate(${(1 - win) * 2}deg)`, zoom: vertical ? 1.5 : 1}}>
          <Recordings frame={frame} />
        </div>
      </AbsoluteFill>
    </Paper>
  );
};
