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

/** The soft blue desktop the app's screenshots sit on. */
export const Wallpaper: React.FC = () => {
  const [a, b, c] = colors.wallpaper;
  return (
    <AbsoluteFill
      style={{
        background: `radial-gradient(90% 70% at 20% 10%, rgba(255,255,255,0.55), rgba(255,255,255,0) 60%),
          linear-gradient(155deg, ${a} 0%, ${b} 48%, ${c} 100%)`,
      }}
    />
  );
};

export const MENU_H = 38;
/** Where Study Stash's icon sits in the menu bar. */
export const menuIconX = (width: number) => width - (width > 1200 ? 262 : 214);

const Glyph: React.FC<{d: string; w?: number}> = ({d, w = 22}) => (
  <svg width={w} height="18" viewBox="0 0 24 18" fill="none" stroke={colors.text} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <path d={d} />
  </svg>
);

/** The Mac's menu bar, with Study Stash's icon among the others. `lit` highlights the icon (its menu is open). */
export const MenuBar: React.FC<{lit?: boolean}> = ({lit = false}) => {
  const {width} = useVideoConfig();
  const wide = width > 1200;
  return (
    <div
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        height: MENU_H,
        background: 'rgba(255,255,255,0.42)',
        borderBottom: '1px solid rgba(255,255,255,0.5)',
        display: 'flex',
        alignItems: 'center',
        padding: '0 22px',
        gap: 26,
        fontFamily: fonts.ui,
        fontSize: 17,
        color: colors.text,
      }}
    >
      <span style={{fontWeight: 700}}>Study Stash</span>
      {wide ? ['File', 'Edit', 'View', 'Window', 'Help'].map((m) => <span key={m}>{m}</span>) : null}
      <div style={{flex: 1}} />
      <div
        style={{
          position: 'absolute',
          left: menuIconX(width) - 17,
          top: 4,
          width: 34,
          height: 30,
          borderRadius: 7,
          background: lit ? 'rgba(16,24,40,0.12)' : 'transparent',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Logo size={24} shadow={false} flat />
      </div>
      <Glyph d="M2 7.5a14 14 0 0 1 20 0M5.5 11a9 9 0 0 1 13 0M9 14.5a4 4 0 0 1 6 0" />
      <svg width="30" height="16" viewBox="0 0 30 16">
        <rect x="1" y="2" width="24" height="12" rx="3.5" fill="none" stroke={colors.text} strokeWidth="1.6" />
        <rect x="3.5" y="4.5" width="15" height="7" rx="1.5" fill={colors.text} />
        <rect x="26.5" y="6" width="2" height="4" rx="1" fill={colors.text} />
      </svg>
      {wide ? <span style={{fontVariantNumeric: 'tabular-nums'}}>Tue 23 Sep  10:02</span> : null}
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

/**
 * A caption under the action, set in the app's serif like a figure caption. One line, eight words at most; it
 * settles in once and stays.
 */
export const Caption: React.FC<{text: string; delay?: number}> = ({text, delay = 8}) => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const p = spr(frame, delay, {damping: 26, stiffness: 120, mass: 1});
  return (
    <div
      style={{
        position: 'absolute',
        left: vertical ? 72 : 100,
        right: vertical ? 72 : undefined,
        bottom: vertical ? 170 : 64,
        fontFamily: fonts.serif,
        fontSize: vertical ? 70 : 60,
        lineHeight: 1.12,
        letterSpacing: '-0.012em',
        color: colors.ink,
        opacity: p,
        transform: `translateY(${(1 - p) * 18}px)`,
      }}
    >
      {text}
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
