import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../config';
import {spr} from '../anim';
import {Logo} from './Logo';

export const useVertical = () => {
  const {width, height} = useVideoConfig();
  return height > width;
};

/** The warm paper of the icon and the website. */
export const Paper: React.FC<{children?: React.ReactNode}> = ({children}) => (
  <AbsoluteFill style={{background: `linear-gradient(180deg, ${colors.paper} 0%, ${colors.paper2} 100%)`, overflow: 'hidden'}}>
    {children}
  </AbsoluteFill>
);

// Rolling hills, back to front, in a 1920×1080 frame: each crest is a smooth curve through these heights.
const HILLS: number[][] = [
  [560, 500, 540, 470, 520, 480],
  [650, 600, 640, 590, 620, 575],
  [760, 700, 735, 690, 720, 700],
  [860, 820, 850, 800, 835, 815],
  [960, 930, 950, 915, 945, 925],
];
const crest = (ys: number[]) => {
  const step = 1920 / (ys.length - 1);
  const pts = ys.map((y, i) => [i * step, y]);
  let d = `M-20 ${pts[0][1]}`;
  for (let i = 1; i < pts.length; i++) {
    const [x0, y0] = pts[i - 1];
    const [x1, y1] = pts[i];
    const mx = (x0 + x1) / 2;
    d += ` C${mx} ${y0} ${mx} ${y1} ${x1} ${y1}`;
  }
  return `${d} L1940 1100 L-20 1100 Z`;
};

/** The desktop picture: a pale sky over rolling hills in Study Stash's teal, drawn in code. */
export const Wallpaper: React.FC = () => {
  const [s0, s1, s2] = colors.sky;
  return (
    <AbsoluteFill>
      <svg width="100%" height="100%" viewBox="0 0 1920 1080" preserveAspectRatio="xMidYMax slice" style={{position: 'absolute', inset: 0}}>
        <defs>
          <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor={s0} />
            <stop offset="0.45" stopColor={s1} />
            <stop offset="1" stopColor={s2} />
          </linearGradient>
          <radialGradient id="sun" cx="0.78" cy="0.16" r="0.5">
            <stop offset="0" stopColor="#FFF8EC" stopOpacity="0.9" />
            <stop offset="1" stopColor="#FFF8EC" stopOpacity="0" />
          </radialGradient>
          {colors.hills.map(([top, bottom], i) => (
            <linearGradient key={i} id={`hill${i}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor={top} />
              <stop offset="1" stopColor={bottom} />
            </linearGradient>
          ))}
        </defs>
        <rect x="-20" y="-400" width="1960" height="1500" fill="url(#sky)" />
        <rect x="-20" y="-400" width="1960" height="1500" fill="url(#sun)" />
        {HILLS.map((ys, i) => (
          <path key={i} d={crest(ys)} fill={`url(#hill${i})`} />
        ))}
      </svg>
    </AbsoluteFill>
  );
};

export const MENU_H = 38;
/** Where Study Stash's icon sits in the menu bar. */
// The right of the menu bar, laid out from the right edge as macOS does: the app's icon, then the system's.
// Each item has a fixed slot, so nothing can run into anything else.
const INK = '#111418';
const SLOTS = (wide: boolean): [key: string, w: number][] =>
  [
    ['app', 34],
    ['battery', 34],
    ['wifi', 26],
    ['spotlight', 22],
    ['control', 26],
    ...(wide ? ([['clock', 168]] as [string, number][]) : []),
  ];
const slotX = (width: number) => {
  const wide = width > 1200;
  const slots = SLOTS(wide);
  const x: Record<string, number> = {};
  let right = width - 20;
  for (let i = slots.length - 1; i >= 0; i--) {
    const [key, w] = slots[i];
    x[key] = right - w;
    right -= w + (key === 'clock' ? 16 : 20);
  }
  return {x, slots};
};
/** Where Study Stash's icon sits in the menu bar (its centre). */
export const menuIconX = (width: number) => slotX(width).x.app + 17;

const MenuGlyph: React.FC<{kind: string}> = ({kind}) => {
  switch (kind) {
    case 'battery':
      return (
        <svg width="34" height="16" viewBox="0 0 34 16">
          <rect x="1" y="1.5" width="27" height="13" rx="4" fill="none" stroke={INK} strokeWidth="1.5" opacity="0.55" />
          <rect x="3.5" y="4" width="19" height="8" rx="2" fill={INK} />
          <path d="M30.5 6v4" stroke={INK} strokeWidth="2" strokeLinecap="round" opacity="0.55" />
        </svg>
      );
    case 'wifi':
      return (
        <svg width="26" height="19" viewBox="0 0 26 19">
          <path d="M13 17.6l3.1-3.7a4.8 4.8 0 0 0-6.2 0z" fill={INK} />
          <path d="M6.9 10.8a9.2 9.2 0 0 1 12.2 0M3.3 6.6a14.6 14.6 0 0 1 19.4 0" fill="none" stroke={INK} strokeWidth="2.3" strokeLinecap="round" />
        </svg>
      );
    case 'spotlight':
      return (
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" stroke={INK} strokeWidth="2.1" strokeLinecap="round">
          <circle cx="8.3" cy="8.3" r="6" />
          <path d="M12.8 12.8l5 5" />
        </svg>
      );
    case 'control':
      return (
        <svg width="24" height="20" viewBox="0 0 24 20">
          <rect x="1.5" y="1.5" width="21" height="7" rx="3.5" fill="none" stroke={INK} strokeWidth="1.7" />
          <circle cx="18" cy="5" r="2" fill={INK} />
          <rect x="1.5" y="11.5" width="21" height="7" rx="3.5" fill="none" stroke={INK} strokeWidth="1.7" />
          <circle cx="6" cy="15" r="2" fill={INK} />
        </svg>
      );
    default:
      return null;
  }
};

/** The Mac's menu bar, with Study Stash's icon among the system's. `lit` highlights the icon (its menu is open). */
export const MenuBar: React.FC<{lit?: boolean}> = ({lit = false}) => {
  const {width} = useVideoConfig();
  const wide = width > 1200;
  const {x, slots} = slotX(width);
  return (
    <div
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        height: MENU_H,
        background: 'rgba(255,255,255,0.34)',
        boxShadow: 'inset 0 -1px 0 rgba(255,255,255,0.35)',
        fontFamily: fonts.ui,
        fontSize: 17,
        color: INK,
      }}
    >
      <div style={{position: 'absolute', left: 26, top: 0, height: MENU_H, display: 'flex', alignItems: 'center', gap: 25}}>
        <span style={{fontWeight: 700}}>Study Stash</span>
        {wide ? ['File', 'Edit', 'View', 'Window', 'Help'].map((m) => <span key={m}>{m}</span>) : null}
      </div>
      {slots.map(([key, w]) => (
        <div
          key={key}
          style={{
            position: 'absolute',
            left: x[key],
            top: 4,
            width: w,
            height: MENU_H - 8,
            display: 'flex',
            alignItems: 'center',
            justifyContent: key === 'clock' ? 'flex-end' : 'center',
            borderRadius: 7,
            background: key === 'app' && lit ? 'rgba(16,24,40,0.13)' : 'transparent',
            fontVariantNumeric: 'tabular-nums',
            whiteSpace: 'nowrap',
          }}
        >
          {key === 'app' ? <Logo size={23} shadow={false} flat /> : key === 'clock' ? 'Tue 23 Sep  10:02' : <MenuGlyph kind={key} />}
        </div>
      ))}
    </div>
  );
};

/** The desktop: wallpaper, whatever is open, and the menu bar on top. */
export const Desktop: React.FC<{children?: React.ReactNode; lit?: boolean}> = ({children, lit}) => (
  <AbsoluteFill style={{overflow: 'hidden'}}>
    <Wallpaper />
    {children}
    <MenuBar lit={lit} />
  </AbsoluteFill>
);

/** Where the headline and the window's left edge line up (landscape), and where windows start below it. */
export const COLUMN = 300;
export const BELOW_TITLE = 196;

/**
 * The headline above the app, in the app's own Inter Display: the first part in ink, the rest in grey, one line
 * (or two where `width` is narrow). It settles in once and stays.
 */
export const Title: React.FC<{text: [string, string]; delay?: number; width?: number; under?: boolean}> = ({text, delay = 8, width, under = false}) => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const p = spr(frame, delay, {damping: 26, stiffness: 120, mass: 1});
  const q = spr(frame, delay + 7, {damping: 26, stiffness: 120, mass: 1});
  return (
    <div
      style={{
        position: 'absolute',
        left: vertical ? 72 : COLUMN,
        // Above the window, on the sky. `under` puts it low on a phone-shaped frame, where something fills the top.
        top: vertical && under ? undefined : vertical ? 140 : 94,
        bottom: vertical && under ? 150 : undefined,
        width: width ?? (vertical ? 936 : 1320),
        fontFamily: fonts.display,
        fontWeight: 600,
        fontSize: vertical ? 78 : 58,
        lineHeight: 1.06,
        letterSpacing: '-0.025em',
        zIndex: 40,
      }}
    >
      <span style={{color: colors.ink, opacity: p, display: 'inline-block', transform: `translateY(${(1 - p) * 14}px)`}}>{text[0]}</span>{' '}
      <span style={{color: '#7A8497', opacity: q, display: 'inline-block', transform: `translateY(${(1 - q) * 14}px)`}}>{text[1]}</span>
    </div>
  );
};

/** One of the app's light windows, with its traffic lights. */
export const Win: React.FC<{w: number; h: number; style?: React.CSSProperties; children?: React.ReactNode}> = ({w, h, style, children}) => (
  <div
    style={{
      width: w,
      height: h,
      background: colors.window,
      border: '1px solid rgba(16,24,40,0.10)',
      borderRadius: 22,
      boxShadow: '0 1px 2px rgba(16,24,40,0.06), 0 34px 80px -26px rgba(34, 52, 100, 0.42)',
      overflow: 'hidden',
      position: 'relative',
      fontFamily: fonts.ui,
      color: colors.text,
      ...style,
    }}
  >
    {children}
  </div>
);

export const TrafficLights: React.FC<{style?: React.CSSProperties}> = ({style}) => (
  <div style={{display: 'flex', gap: 9, padding: '20px 22px', ...style}}>
    {['#FF5F57', '#FEBC2E', '#28C840'].map((c) => (
      <div key={c} style={{width: 14, height: 14, borderRadius: 7, background: c, boxShadow: 'inset 0 0 0 0.5px rgba(0,0,0,0.12)'}} />
    ))}
  </div>
);

export const ClassDot: React.FC<{color: string; size?: number}> = ({color, size = 10}) => (
  <div style={{width: size, height: size, borderRadius: size / 2, background: color, flexShrink: 0}} />
);

/** The pointer, moving between `stops` ([frame, x, y]) with an ease, and pressed around each frame in `clicks`. */
export const Cursor: React.FC<{stops: [number, number, number][]; clicks?: number[]; hideAfter?: number}> = ({stops, clicks = [], hideAfter}) => {
  const frame = useCurrentFrame();
  if (hideAfter !== undefined && frame > hideAfter) return null;
  const fs = stops.map((s) => s[0]);
  const opts = {easing: Easing.bezier(0.45, 0, 0.2, 1), extrapolateLeft: 'clamp' as const, extrapolateRight: 'clamp' as const};
  const x = interpolate(frame, fs, stops.map((s) => s[1]), opts);
  const y = interpolate(frame, fs, stops.map((s) => s[2]), opts);
  const down = clicks.some((c) => frame >= c && frame < c + 5);
  const appear = interpolate(frame, [fs[0], fs[0] + 6], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  return (
    <svg
      width="34"
      height="40"
      viewBox="0 0 17 20"
      style={{
        position: 'absolute',
        left: x,
        top: y,
        opacity: appear,
        transform: `scale(${down ? 0.86 : 1})`,
        transformOrigin: 'top left',
        filter: 'drop-shadow(0 2px 3px rgba(0,0,0,0.3))',
        zIndex: 50,
      }}
    >
      <path d="M1 1 L1 16.2 L4.9 12.6 L7.6 18.6 L10.3 17.4 L7.7 11.6 L13 11.6 Z" fill="#111" stroke="#fff" strokeWidth="1.3" strokeLinejoin="round" />
    </svg>
  );
};
