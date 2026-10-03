import React from 'react';
import {AbsoluteFill, Easing, interpolate} from 'remotion';
import {colors, fonts} from '../../config';
import {Wallpaper} from '../../components/Layout';
import {around, Flow} from '../Flow';
import {Drawing} from '../Parts';
import {DESCENT, DescentPlot, SIGMOID, SigmoidPlot, SliderRow} from '../Plots';
import {FloatingBar, H2, PinActions} from '../ui';

// What the second video's opening and ending are made of: the night desktop, dimmed or full; lines of words that rise
// out of a mask; the app's icon drawn a stroke at a time; glimpses of what's new (a pinned diagram, the drone, the
// sigmoid with its slider, gradient descent) as cards at different depths; and the marks for Mac and Windows.
//
// Steady by construction: everything is a pure function of the frame, every move eases to an exact stop (no springs,
// so nothing overshoots or creeps), and lengths that move are rounded to a hundredth of a pixel.

export const outQuint = Easing.bezier(0.22, 1, 0.36, 1);
export const inOut = Easing.bezier(0.65, 0, 0.35, 1);
export const inCubic = Easing.bezier(0.32, 0, 0.67, 0);

/** 0 → 1 between frames a and b, eased, held at 0 before and at exactly 1 after. */
export const prog = (f: number, a: number, b: number, easing = outQuint) =>
  interpolate(f, [a, b], [0, 1], {easing, extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});

/** A length rounded to a hundredth of a pixel. */
export const px = (v: number) => Math.round(v * 100) / 100;

export const night = {
  ground: colors.desk.sky[0],
  soft: colors.onDeskSoft,
  teal: colors.lagoonBright,
};

/**
 * The night-blue desktop the film plays on. `dim` lays the night over it (0: the desktop as the app scenes show it,
 * 1: deep enough for words to sit on), `black` hides it altogether, `zoom` scales it about its centre.
 */
export const NightGround: React.FC<{dim: number; black?: number; zoom?: number; glow?: {x: number; y: number; r: number; a: number}}> = ({
  dim,
  black = 0,
  zoom = 1,
  glow,
}) => (
  <AbsoluteFill style={{background: night.ground}}>
    <AbsoluteFill style={{transform: `scale(${px(zoom * 10000) / 10000})`}}>
      <Wallpaper />
    </AbsoluteFill>
    <AbsoluteFill
      style={{
        opacity: dim,
        background: 'linear-gradient(180deg, rgba(3,4,26,0.9) 0%, rgba(5,6,38,0.74) 46%, rgba(6,7,44,0.66) 70%, rgba(3,4,26,0.86) 100%)',
      }}
    />
    {glow ? (
      <AbsoluteFill
        style={{
          opacity: glow.a,
          background: `radial-gradient(circle ${glow.r}px at ${glow.x}px ${glow.y}px, rgba(52,193,189,0.30) 0%, rgba(43,90,200,0.12) 45%, rgba(3,4,26,0) 100%)`,
        }}
      />
    ) : null}
    <AbsoluteFill style={{opacity: black, background: night.ground}} />
  </AbsoluteFill>
);

/** A line of words rising out of a mask: hidden at p = 0, in place at p = 1. */
export const Mask: React.FC<{p: number; children: React.ReactNode; style?: React.CSSProperties}> = ({p, children, style}) => (
  <div style={{overflow: 'hidden', padding: '0.06em 0.08em 0.16em', margin: '-0.06em -0.08em -0.16em', ...style}}>
    <div style={{transform: `translateY(${px((1 - p) * 112)}%)`, opacity: Math.min(1, p * 1.8)}}>{children}</div>
  </div>
);

// The icon's rounded square, as one path that starts at the top centre, so its outline can be drawn from there.
const TILE = 'M128 8 H192 A56 56 0 0 1 248 64 V192 A56 56 0 0 1 192 248 H64 A56 56 0 0 1 8 192 V64 A56 56 0 0 1 64 8 Z';
const S_LENGTH = 1000; // a little more than the S's outline, in the icon's units

/**
 * Study Stash's icon (src/components/Logo.tsx), put together: a line of light draws its rounded square both ways from
 * the top, the paper fills in, the S is drawn and then inked, the full stop drops into place, and a sheen crosses it.
 */
export const DrawnIcon: React.FC<{size: number; trace: number; tile: number; s: number; ink: number; dot: number; sheen: number}> = ({
  size,
  trace,
  tile,
  s,
  ink,
  dot,
  sheen,
}) => {
  const id = React.useId().replace(/:/g, '');
  const line = Math.min(1, trace * 4) * (1 - tile * 0.92);
  return (
    <svg width={size} height={size} viewBox="0 0 256 256" style={{display: 'block', overflow: 'visible'}}>
      <defs>
        <linearGradient id={`bg${id}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#FBF8F2" />
          <stop offset="1" stopColor="#ECE6DA" />
        </linearGradient>
        {/* The tile's shadow alone, so it can come in under the paper as the paper spreads. */}
        <filter id={`sh${id}`} x="-40%" y="-40%" width="180%" height="190%">
          <feGaussianBlur in="SourceAlpha" stdDeviation="22" />
          <feOffset dy="18" />
          <feComponentTransfer>
            <feFuncA type="linear" slope="0.55" />
          </feComponentTransfer>
        </filter>
        <filter id={`gl${id}`} x="-20%" y="-20%" width="140%" height="140%">
          <feGaussianBlur stdDeviation="5" result="b" />
          <feMerge>
            <feMergeNode in="b" />
            <feMergeNode in="b" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
        <clipPath id={`cl${id}`}>
          <path d={TILE} />
        </clipPath>
        <linearGradient id={`sn${id}`} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="#FFFFFF" stopOpacity="0" />
          <stop offset="0.5" stopColor="#FFFFFF" stopOpacity="0.7" />
          <stop offset="1" stopColor="#FFFFFF" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={TILE} fill="#000" filter={`url(#sh${id})`} opacity={tile} />
      {/* The paper, growing out from the middle to fill the outline. */}
      {tile > 0 ? (
        <path
          d={TILE}
          fill={`url(#bg${id})`}
          stroke="rgba(25,38,63,0.10)"
          strokeWidth="2"
          opacity={Math.min(1, tile * 4)}
          transform={tile < 1 ? `translate(128 128) scale(${px((0.2 + 0.8 * tile) * 1000) / 1000}) translate(-128 -128)` : undefined}
        />
      ) : null}
      {/* The line of light, both ways round from the top centre, meeting at the bottom. */}
      <g filter={`url(#gl${id})`} opacity={line} fill="none" stroke="#C4FFF9" strokeWidth="3.5" strokeLinecap="round">
        <path d={TILE} pathLength={1} strokeDasharray={`${px(trace * 0.5 * 100) / 100} 2`} />
        <path d={TILE} pathLength={1} strokeDasharray={`${px(trace * 0.5 * 100) / 100} 2`} transform="translate(256 0) scale(-1 1)" />
      </g>
      <text
        x="124"
        y="200"
        textAnchor="middle"
        fontFamily={fonts.display}
        fontWeight={700}
        fontSize="200"
        fill={colors.navy}
        fillOpacity={ink}
        stroke={colors.navy}
        strokeWidth="8"
        strokeLinejoin="round"
        strokeDasharray={`${px(s * S_LENGTH)} ${S_LENGTH}`}
        opacity={s > 0 ? 1 : 0}
      >
        S
      </text>
      <circle cx="197" cy={px(188 - (1 - dot) * 70)} r="12" fill="#FBF8F2" stroke={colors.navy} strokeWidth="4.5" opacity={Math.min(1, dot * 2.5)} />
      {sheen > 0 && sheen < 1 ? (
        <g clipPath={`url(#cl${id})`}>
          <rect x={px(-260 + sheen * 520)} y="-60" width="110" height="380" fill={`url(#sn${id})`} transform="rotate(18 128 128)" opacity="0.65" />
        </g>
      ) : null}
    </svg>
  );
};

// ---------------------------------------------------------------------------------------------------------------
// Glimpses of what's new, drawn by the app's own pieces (the same diagram, drawing and plots as the scenes).

export type GlimpseKind = 'diagram' | 'drone' | 'sigmoid' | 'descent';

const PAD = 22;
/** Each card's size before it's scaled: its padding around what it shows. */
export const GLIMPSE: Record<GlimpseKind, {w: number; h: number}> = {
  diagram: {w: 640 + PAD * 2, h: 40 + 452 + 44 + PAD * 2},
  drone: {w: 720 + PAD * 2, h: 40 + 380 + PAD * 2},
  sigmoid: {w: 620 + PAD * 2, h: 410 + 14 + 40 + PAD * 2},
  descent: {w: 620 + PAD * 2, h: 430 + 14 + 40 + PAD * 2},
};

const pinned = around('contract');

/** What a card shows. `play` (0 → 1) moves the sigmoid's slider, as the plots scene does. */
const GlimpseBody: React.FC<{kind: GlimpseKind; play: number}> = ({kind, play}) => {
  switch (kind) {
    case 'diagram':
      return (
        <>
          <H2 style={{height: 40}}>The cycle, step by step</H2>
          <div style={{position: 'relative'}}>
            <Flow look={{build: 1, lit: pinned, dim: 1, accent: pinned.edges, ring: 'contract', ringP: 1}} scale={1} />
            <div style={{position: 'absolute', left: 31, top: 431}}>
              <PinActions p={1} />
            </div>
            <div style={{position: 'absolute', right: 0, top: -46}}>
              <FloatingBar icons={['play_circle', 'quiz', 'open_in_full']} on="play_circle" />
            </div>
          </div>
        </>
      );
    case 'drone':
      return (
        <>
          <H2 style={{height: 40}}>The quadcopter, side on</H2>
          <Drawing name="drone" scale={1} look={{hot: 'flight-controller', dim: 1, ring: 1}} />
        </>
      );
    case 'sigmoid': {
      const {from, to, start} = SIGMOID.k;
      const k = start + (3.2 - start) * play;
      return (
        <>
          <SigmoidPlot k={k} scale={1} />
          <div style={{height: 14}} />
          <SliderRow label="steepness k" pos={(k - from) / (to - from)} value={k} width={620} held={play > 0 && play < 1} />
        </>
      );
    }
    case 'descent': {
      const {from, to, start} = DESCENT.eta;
      return (
        <>
          <DescentPlot eta={start} scale={1} />
          <div style={{height: 14}} />
          <SliderRow label="learning rate η" pos={(start - from) / (to - from)} value={start} width={620} />
        </>
      );
    }
  }
};

/** One card, where it settles: its centre, its scale, how near it is (1 nearest), and when it arrives. */
export type Placement = {kind: GlimpseKind; x: number; y: number; s: number; z: number; at: number};

/**
 * A card floating in from depth: it comes out from behind the centre of the frame (`cx`, `cy`), sharpens as it nears,
 * then drifts slowly outwards, nearer cards faster. `leave` (0 → 1) sends it on past the edges of the frame.
 */
export const Glimpse: React.FC<{
  g: Placement;
  f: number;
  cx: number;
  cy: number;
  leave?: number;
  drift?: number;
  light?: number;
  play?: [number, number]; // when the sigmoid's slider moves, from the card's arrival
}> = ({g, f, cx, cy, leave = 0, drift = 0.32, light = 1, play = [8, 46]}) => {
  const {w, h} = GLIMPSE[g.kind];
  const a = prog(f, g.at, g.at + 34);
  const dx = g.x - cx;
  const dy = g.y - cy;
  const n = Math.hypot(dx, dy) || 1;
  const pace = 0.45 + 0.55 * g.z; // parallax: the near cards move more
  const along = -90 * (1 - a) + drift * pace * Math.max(0, f - g.at) + 300 * leave * leave * pace;
  const scale = g.s * (0.9 + 0.1 * a) * (1 + 0.18 * leave * g.z);
  const blur = (1 - g.z) * 2.2 + (1 - a) * 10;
  const bright = (0.5 + 0.5 * g.z) * light;
  return (
    <div
      style={{
        position: 'absolute',
        left: Math.round(g.x - w / 2),
        top: Math.round(g.y - h / 2),
        width: w,
        height: h,
        transform: `translate(${px((dx / n) * along)}px, ${px((dy / n) * along)}px) scale(${px(scale * 10000) / 10000})`,
        opacity: px(a * (1 - leave) * 1000) / 1000,
        filter: `blur(${px(blur)}px) brightness(${px(bright * 1000) / 1000})`,
      }}
    >
      <div
        style={{
          width: w,
          height: h,
          boxSizing: 'border-box',
          padding: PAD,
          borderRadius: 22,
          background: colors.window,
          border: `1px solid ${colors.edge}`,
          boxShadow: '0 0 0 0.5px rgba(0,0,0,0.6), 0 60px 120px -30px rgba(0,0,12,0.9)',
          fontFamily: fonts.ui,
          color: colors.text,
          overflow: 'hidden',
        }}
      >
        <GlimpseBody kind={g.kind} play={prog(f, g.at + play[0], g.at + play[1], inOut)} />
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------------------------------------------
// Marks drawn in the app's line style: no other company's logos.

/** The Mac's command key, ⌘. */
export const CommandMark: React.FC<{size: number; color: string}> = ({size, color}) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="1.8" strokeLinejoin="round">
    <path d="M9 6 V18 A3 3 0 1 1 6 15 H18 A3 3 0 1 1 15 18 V6 A3 3 0 1 1 18 9 H6 A3 3 0 1 1 9 6 Z" />
  </svg>
);

/** Four panes of a window. */
export const PanesMark: React.FC<{size: number; color: string}> = ({size, color}) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="1.8" strokeLinejoin="round">
    <rect x="3.5" y="3.5" width="7.6" height="7.6" rx="1.4" />
    <rect x="12.9" y="3.5" width="7.6" height="7.6" rx="1.4" />
    <rect x="3.5" y="12.9" width="7.6" height="7.6" rx="1.4" />
    <rect x="12.9" y="12.9" width="7.6" height="7.6" rx="1.4" />
  </svg>
);

/**
 * A laptop, drawn a line at a time, with a recording on its screen and a padlock that closes over it.
 * `draw` traces the laptop, `wave` raises the recording's bars, `lock` brings the padlock, `shut` closes it.
 */
export const LockedLaptop: React.FC<{width: number; draw: number; wave: number; lock: number; shut: number}> = ({width, draw, wave, lock, shut}) => {
  const line = '#DCE3FF';
  const teal = night.teal;
  const bars = [16, 30, 52, 38, 66, 42, 56, 28, 14];
  const base = draw > 0 ? prog(draw, 0.35, 1, inOut) : 0;
  return (
    <svg width={width} height={(width * 250) / 360} viewBox="0 0 360 250" style={{display: 'block', overflow: 'visible'}}>
      <defs>
        <filter id="lap-glow" x="-30%" y="-30%" width="160%" height="160%">
          <feGaussianBlur stdDeviation="6" result="b" />
          <feMerge>
            <feMergeNode in="b" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
      <g fill="none" stroke={line} strokeWidth="5" strokeLinecap="round" strokeLinejoin="round">
        {/* The screen, from the top centre both ways; then the deck and its notch. */}
        <path d="M180 18 H290 Q306 18 306 34 V176 H54 V34 Q54 18 70 18 Z" pathLength={1} strokeDasharray={`${px(draw * 100) / 100} 2`} />
        <path d="M14 194 H346 Q344 214 322 214 H38 Q16 214 14 194 Z" pathLength={1} strokeDasharray={`${px(base * 100) / 100} 2`} />
        <path d="M150 194 Q152 202 162 202 H198 Q208 202 210 194" opacity={base} />
      </g>
      {/* The recording, on the screen. */}
      <g stroke={teal} strokeWidth="7" strokeLinecap="round">
        {bars.map((b, i) => {
          const p = prog(wave, i * 0.06, 0.5 + i * 0.06, outQuint);
          const hh = px((b / 2) * p);
          const x = 120 + i * 15;
          return <line key={i} x1={x} y1={97 - hh} x2={x} y2={97 + hh} opacity={p > 0 ? 1 : 0} />;
        })}
      </g>
      {/* The padlock, over the screen's corner. */}
      <g transform={`translate(296 168) scale(${px((0.7 + 0.3 * lock) * 1000) / 1000})`} opacity={lock}>
        <circle r="36" fill="#0A0F2E" stroke={teal} strokeWidth="4" filter="url(#lap-glow)" />
        <path d={`M-10 ${px(-6 - (1 - shut) * 9)} V${px(-13 - (1 - shut) * 9)} A10 10 0 0 1 10 ${px(-13 - (1 - shut) * 9)} V-6`} fill="none" stroke={teal} strokeWidth="5" strokeLinecap="round" />
        <rect x="-16" y="-7" width="32" height="25" rx="6" fill={teal} />
        <circle cx="0" cy="4" r="3.4" fill="#0A0F2E" />
      </g>
    </svg>
  );
};
