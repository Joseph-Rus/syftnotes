import React from 'react';
import {AbsoluteFill, Easing, Img, interpolate, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../config';
import {spr} from '../anim';
import {Sfx} from './Sfx';

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

/**
 * The desktop picture, painted in code in the style of a macOS wallpaper: a night-blue sky that glows violet at the
 * horizon, one glossy wave of blue silk sweeping up to the right with a bright edge, and soft dunes out of focus.
 */
export const Wallpaper: React.FC = () => {
  const d = colors.desk;
  // The wave's top edge: low on the left, rising in an S to the top right.
  const edge = 'M-40 760 C 300 700, 520 640, 760 470 S 1260 150, 1980 170';
  return (
    <AbsoluteFill>
      <svg width="100%" height="100%" viewBox="0 0 1920 1080" preserveAspectRatio="xMidYMid slice" style={{position: 'absolute', inset: 0}}>
        <defs>
          <linearGradient id="dsky" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor={d.sky[0]} />
            <stop offset="0.22" stopColor={d.sky[1]} />
            <stop offset="0.42" stopColor={d.sky[2]} />
            <stop offset="0.55" stopColor={d.sky[3]} />
            <stop offset="1" stopColor={d.sky[2]} />
          </linearGradient>
          <linearGradient id="dwave" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor={d.wave[1]} />
            <stop offset="0.45" stopColor={d.wave[0]} />
            <stop offset="1" stopColor={d.wave[2]} />
          </linearGradient>
          <linearGradient id="drim" x1="0" y1="1" x2="1" y2="0">
            <stop offset="0" stopColor={d.rim} stopOpacity="0.1" />
            <stop offset="0.35" stopColor={d.rim} stopOpacity="0.95" />
            <stop offset="0.7" stopColor={d.rim} stopOpacity="0.55" />
            <stop offset="1" stopColor={d.rim} stopOpacity="0.2" />
          </linearGradient>
          <linearGradient id="ddune" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor={d.dune[0]} />
            <stop offset="1" stopColor={d.dune[1]} />
          </linearGradient>
          <filter id="blur40" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="40" /></filter>
          <filter id="blur18" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="18" /></filter>
          <filter id="blur6" x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="6" /></filter>
          <filter id="blur2"><feGaussianBlur stdDeviation="1.6" /></filter>
          <clipPath id="dwaveclip"><path d={`${edge} L1980 1120 L-40 1120 Z`} /></clipPath>
        </defs>
        <rect x="-40" y="-40" width="2000" height="1160" fill="url(#dsky)" />
        {/* The violet glow along the horizon, and far hills out of focus. */}
        <ellipse cx="560" cy="600" rx="900" ry="120" fill="#7C62E6" opacity="0.45" filter="url(#blur40)" />
        <path d="M-40 640 C 120 560, 260 560, 420 610 S 700 650, 820 600 L 820 1120 L -40 1120 Z" fill={d.far} opacity="0.85" filter="url(#blur18)" />
        {/* The silk: its body, folds catching the light, and a bright edge. */}
        <path d={`${edge} L1980 1120 L-40 1120 Z`} fill="url(#dwave)" />
        <g clipPath="url(#dwaveclip)">
          <path d="M-40 850 C 360 790, 640 700, 900 540 S 1400 250, 1980 280" fill="none" stroke="#6E86FF" strokeWidth="70" opacity="0.28" filter="url(#blur18)" />
          <path d="M600 900 C 900 760, 1200 560, 1980 470" fill="none" stroke="#0B1270" strokeWidth="120" opacity="0.45" filter="url(#blur40)" />
          <path d="M-40 980 C 420 900, 820 820, 1200 640 S 1700 420, 1980 430" fill="none" stroke="#8FA2FF" strokeWidth="40" opacity="0.22" filter="url(#blur18)" />
          <ellipse cx="1180" cy="720" rx="420" ry="190" fill="#5B74F0" opacity="0.35" filter="url(#blur40)" />
        </g>
        <path d={edge} fill="none" stroke="url(#drim)" strokeWidth="3" filter="url(#blur2)" />
        <path d={edge} fill="none" stroke="url(#drim)" strokeWidth="14" opacity="0.35" filter="url(#blur6)" />
        {/* Dunes in the foreground, out of focus. */}
        <path d="M-40 1120 C -40 900, 160 820, 380 860 S 640 1020, 700 1120 Z" fill="url(#ddune)" opacity="0.8" filter="url(#blur40)" />
        <path d="M1560 1120 C 1600 900, 1760 780, 1980 740 L 1980 1120 Z" fill="#A4ABDF" opacity="0.75" filter="url(#blur18)" />
      </svg>
    </AbsoluteFill>
  );
};

export const MENU_H = 38;
/** Where Study Stash's icon sits in the menu bar. */
// The right of the menu bar, laid out from the right edge as macOS does: the app's icon, then the system's.
// Each item has a fixed slot, so nothing can run into anything else.
const INK = colors.onDesk; // macOS draws the menu bar in white over a dark picture
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
export const MenuBar: React.FC<{lit?: boolean; clock?: string}> = ({lit = false, clock = 'Tue 23 Sep  10:02'}) => {
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
        background: 'rgba(8,10,40,0.18)',
        fontFamily: fonts.ui,
        fontSize: 17,
        color: INK,
      }}
    >
      <div style={{position: 'absolute', left: 26, top: 0, height: MENU_H, display: 'flex', alignItems: 'center', gap: 25}}>
        {/* Study Stash lives in the menu bar only, so the menus are those of the app in front: Finder. */}
        <span style={{fontWeight: 700}}>Finder</span>
        {wide ? ['File', 'Edit', 'View', 'Go', 'Window', 'Help'].map((m) => <span key={m}>{m}</span>) : null}
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
            background: key === 'app' && lit ? 'rgba(255,255,255,0.24)' : 'transparent',
            fontVariantNumeric: 'tabular-nums',
            whiteSpace: 'nowrap',
          }}
        >
          {key === 'app' ? <Img src={staticFile('mark-36.png')} style={{height: 21, filter: 'invert(1)'}} /> : key === 'clock' ? clock : <MenuGlyph kind={key} />}
        </div>
      ))}
    </div>
  );
};

/** The desktop: wallpaper, whatever is open, and the menu bar on top. */
export const Desktop: React.FC<{children?: React.ReactNode; lit?: boolean; clock?: string}> = ({children, lit, clock}) => (
  <AbsoluteFill style={{overflow: 'hidden'}}>
    <Wallpaper />
    {children}
    <MenuBar lit={lit} clock={clock} />
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
      <span style={{color: colors.onDesk, opacity: p, display: 'inline-block', transform: `translateY(${(1 - p) * 14}px)`}}>{text[0]}</span>{' '}
      <span style={{color: colors.onDeskSoft, opacity: q, display: 'inline-block', transform: `translateY(${(1 - q) * 14}px)`}}>{text[1]}</span>
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
      border: `1px solid ${colors.edge}`,
      borderRadius: 22,
      boxShadow: '0 0 0 0.5px rgba(0,0,0,0.6), 0 40px 90px -24px rgba(0, 0, 20, 0.7)',
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
export const Cursor: React.FC<{stops: [number, number, number][]; clicks?: number[]; hideAfter?: number; clickVolume?: number}> = ({stops, clicks = [], hideAfter, clickVolume = 0.55}) => {
  const frame = useCurrentFrame();
  if (hideAfter !== undefined && frame > hideAfter) return null;
  const fs = stops.map((s) => s[0]);
  const opts = {easing: Easing.bezier(0.45, 0, 0.2, 1), extrapolateLeft: 'clamp' as const, extrapolateRight: 'clamp' as const};
  const x = interpolate(frame, fs, stops.map((s) => s[1]), opts);
  const y = interpolate(frame, fs, stops.map((s) => s[2]), opts);
  const down = clicks.some((c) => frame >= c && frame < c + 5);
  const appear = interpolate(frame, [fs[0], fs[0] + 6], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  return (
    <>
    {clicks.map((c) => (
      <Sfx key={c} at={c} name="click" volume={clickVolume} />
    ))}
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
    </>
  );
};
