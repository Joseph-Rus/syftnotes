import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../config';
import {rise, spr} from '../anim';

export const useVertical = () => {
  const {width, height} = useVideoConfig();
  return height > width;
};

/** The dark desk the app sits on: night blue, with a slow teal and violet glow, as behind the app's screenshots. */
export const Night: React.FC<{children?: React.ReactNode}> = ({children}) => {
  const frame = useCurrentFrame();
  const drift = Math.sin(frame / 60) * 40;
  return (
    <AbsoluteFill style={{backgroundColor: colors.night, overflow: 'hidden'}}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(60% 55% at ${78 + drift / 20}% 8%, rgba(120, 84, 200, 0.30), transparent 70%),
            radial-gradient(55% 60% at ${12 - drift / 30}% 100%, rgba(0, 143, 144, 0.26), transparent 70%),
            linear-gradient(180deg, #101B30 0%, ${colors.night} 100%)`,
        }}
      />
      {children}
    </AbsoluteFill>
  );
};

/** The warm paper of the icon. */
export const Paper: React.FC<{children?: React.ReactNode}> = ({children}) => (
  <AbsoluteFill
    style={{
      background: `radial-gradient(70% 60% at 50% 40%, #FFFDF8 0%, ${colors.paper} 55%, ${colors.paper2} 100%)`,
      overflow: 'hidden',
    }}
  >
    {children}
  </AbsoluteFill>
);

/** A headline whose words spring up one after another. Never more than about eight words. */
export const Headline: React.FC<{
  text: string;
  delay?: number;
  size?: number;
  color?: string;
  align?: 'left' | 'center';
  maxWidth?: number;
}> = ({text, delay = 0, size = 88, color = colors.nightInk, align = 'left', maxWidth}) => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        fontFamily: fonts.display,
        fontWeight: 700,
        fontSize: size,
        lineHeight: 1.04,
        letterSpacing: '-0.025em',
        color,
        textAlign: align,
        maxWidth,
        display: 'flex',
        flexWrap: 'wrap',
        justifyContent: align === 'center' ? 'center' : 'flex-start',
        columnGap: '0.24em',
      }}
    >
      {text.split(' ').map((w, i) => (
        <span key={i} style={{display: 'inline-block', ...rise(spr(frame, delay + i * 3), size * 0.5)}}>
          {w}
        </span>
      ))}
    </div>
  );
};

/**
 * A feature scene: the headline beside the app (landscape) or above it (vertical). The mockup is designed at
 * `w` × `h` and scaled to fit the room it has.
 */
export const Feature: React.FC<{
  headline: string;
  w: number;
  h: number;
  children: React.ReactNode;
  opacity?: number;
}> = ({headline, w, h, children, opacity = 1}) => {
  const vertical = useVertical();
  const {width, height} = useVideoConfig();
  if (vertical) {
    const top = 560;
    const boxW = width - 100;
    const boxH = height - top - 140;
    const scale = Math.min(boxW / w, boxH / h);
    return (
      <Night>
        <AbsoluteFill style={{opacity}}>
          <div style={{position: 'absolute', top: 210, left: 70, right: 70}}>
            <Headline text={headline} size={96} align="center" />
          </div>
          <div
            style={{
              position: 'absolute',
              top: top + (boxH - h * scale) / 2,
              left: (width - w * scale) / 2,
              width: w,
              height: h,
              transform: `scale(${scale})`,
              transformOrigin: 'top left',
            }}
          >
            {children}
          </div>
        </AbsoluteFill>
      </Night>
    );
  }
  const boxW = 1080;
  const boxH = height - 180;
  const scale = Math.min(boxW / w, boxH / h);
  return (
    <Night>
      <AbsoluteFill style={{opacity}}>
        <div style={{position: 'absolute', left: 120, top: 0, bottom: 0, width: 620, display: 'flex', alignItems: 'center'}}>
          <Headline text={headline} size={84} maxWidth={620} />
        </div>
        <div
          style={{
            position: 'absolute',
            left: width - 90 - w * scale,
            top: (height - h * scale) / 2,
            width: w,
            height: h,
            transform: `scale(${scale})`,
            transformOrigin: 'top left',
          }}
        >
          {children}
        </div>
      </AbsoluteFill>
    </Night>
  );
};

/** A dark glass panel, as the app's windows and panels are drawn in dark mode. */
export const Glass: React.FC<{style?: React.CSSProperties; children?: React.ReactNode; radius?: number}> = ({
  style,
  children,
  radius = 26,
}) => (
  <div
    style={{
      background: 'linear-gradient(180deg, rgba(34, 46, 70, 0.92), rgba(20, 29, 47, 0.94))',
      border: `1.5px solid ${colors.glassEdge}`,
      borderRadius: radius,
      boxShadow: '0 2px 6px rgba(0,0,0,0.25), 0 40px 90px -30px rgba(0,0,0,0.7)',
      color: colors.nightInk,
      fontFamily: fonts.ui,
      overflow: 'hidden',
      ...style,
    }}
  >
    {children}
  </div>
);

/** A window's title bar with the three traffic lights. */
export const TrafficLights: React.FC = () => (
  <div style={{display: 'flex', gap: 9, padding: '20px 22px'}}>
    {['#FF5F57', '#FEBC2E', '#28C840'].map((c) => (
      <div key={c} style={{width: 14, height: 14, borderRadius: 7, background: c}} />
    ))}
  </div>
);

/** A class dot and name, as the sidebar and lists show them. */
export const ClassDot: React.FC<{color: string; size?: number}> = ({color, size = 10}) => (
  <div style={{width: size, height: size, borderRadius: size / 2, background: color, flexShrink: 0}} />
);
