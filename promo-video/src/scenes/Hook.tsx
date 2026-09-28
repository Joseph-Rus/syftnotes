import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame} from 'remotion';
import {colors, fonts, text} from '../config';
import {ease, rise, snappy, spr} from '../anim';
import {ClassDot, Headline, Night, useVertical} from '../components/Layout';

// A week of lectures piling up behind the words.
const LECTURES: [string, string, string][] = [
  ['Recursion and the call stack', '1 h 12 min', colors.cs],
  ['Membranes and osmosis', '58 min', colors.bio],
  ['Series convergence tests', '1 h 15 min', colors.calc],
  ['The Treaty of Versailles', '52 min', colors.hist],
  ['The cardiac cycle', '1 h 04 min', colors.health],
  ['Stack frames and scope', '1 h 14 min', colors.cs],
  ['Cell transport, part 1', '55 min', colors.bio],
  ['Limits and continuity', '1 h 10 min', colors.calc],
];

const Card: React.FC<{i: number; title: string; time: string; color: string; vertical: boolean}> = ({
  i,
  title,
  time,
  color,
  vertical,
}) => {
  const frame = useCurrentFrame();
  const p = spr(frame, i * 5, {damping: 14, stiffness: 120, mass: 0.9});
  const col = i % 2;
  const row = Math.floor(i / 2);
  const x = vertical ? (col === 0 ? -210 : 210) : (col === 0 ? -380 : 380) + (row % 2 ? 60 : -60);
  const y = vertical ? -560 + row * 300 : -300 + row * 175;
  const tilt = ((i * 37) % 11) - 5;
  return (
    <div
      style={{
        position: 'absolute',
        left: '50%',
        top: '50%',
        width: 430,
        marginLeft: -215,
        transform: `translate(${x}px, ${y + (1 - p) * -700}px) rotate(${tilt * p}deg)`,
        opacity: 0.5 * Math.min(1, p * 2),
        background: 'rgba(30, 42, 66, 0.9)',
        border: `1.5px solid ${colors.glassEdge}`,
        borderRadius: 18,
        padding: '20px 24px',
        fontFamily: fonts.ui,
        color: colors.nightInk,
        filter: 'blur(1.5px)',
      }}
    >
      <div style={{display: 'flex', alignItems: 'center', gap: 12, fontSize: 24, fontWeight: 600}}>
        <ClassDot color={color} size={12} />
        {title}
      </div>
      <div style={{marginTop: 8, marginLeft: 24, fontSize: 20, color: colors.nightInk2}}>{time}</div>
    </div>
  );
};

export const Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const swap = 46;
  const first = spr(frame, 2, snappy);
  const away = ease(frame, swap - 4, swap + 8);
  const size = vertical ? 104 : 118;
  return (
    <Night>
      {LECTURES.map(([title, time, color], i) => (
        <Card key={i} i={i} title={title} time={time} color={color} vertical={vertical} />
      ))}
      {/* A soft pool of night behind the words keeps them readable over the cards. */}
      <AbsoluteFill style={{background: 'radial-gradient(45% 35% at 50% 50%, rgba(13,22,38,0.92), rgba(13,22,38,0) 100%)'}} />
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', padding: vertical ? 70 : 160}}>
        {frame < swap + 8 ? (
          <div
            style={{
              ...rise(first, 60),
              opacity: Math.min(1, first * 1.4) * (1 - away),
              transform: `translateY(${(1 - first) * 60 - away * 90}px)`,
            }}
          >
            <Headline text={text.hook[0]} size={size} align="center" />
          </div>
        ) : null}
        {frame >= swap ? (
          <div style={{position: 'absolute', padding: vertical ? 70 : 160}}>
            <Headline text={text.hook[1]} size={size} align="center" delay={swap} />
          </div>
        ) : null}
      </AbsoluteFill>
      <AbsoluteFill
        style={{backgroundColor: colors.night, opacity: interpolate(frame, [0, 6], [1, 0], {extrapolateRight: 'clamp'})}}
      />
    </Night>
  );
};
