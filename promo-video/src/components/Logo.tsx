import React from 'react';
import {colors, fonts} from '../config';

/**
 * The app icon, redrawn: a navy "S" with its full stop, on the warm paper rounded square.
 * `s` animates the S (0 → 1) and `dot` the full stop dropping into place (0 → 1, may overshoot).
 */
export const Logo: React.FC<{size: number; s?: number; dot?: number; shadow?: boolean; flat?: boolean; ink?: string}> = ({
  size,
  s = 1,
  dot = 1,
  shadow = true,
  flat = false,
  ink = colors.text,
}) => {
  const id = React.useId().replace(/:/g, '');
  if (flat) {
    // As a menu bar icon: the S and its full stop in the menu's ink, no tile.
    return (
      <svg width={size} height={size} viewBox="40 20 190 210">
        <text x="124" y="200" textAnchor="middle" fontFamily={fonts.display} fontWeight={700} fontSize="200" fill={ink} stroke={ink} strokeWidth="8" strokeLinejoin="round">
          S
        </text>
        <circle cx="197" cy="188" r="13" fill="none" stroke={ink} strokeWidth="9" />
      </svg>
    );
  }
  return (
    <svg width={size} height={size} viewBox="0 0 256 256" style={{overflow: 'visible'}}>
      <defs>
        <linearGradient id={`bg${id}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#FBF8F2" />
          <stop offset="1" stopColor="#ECE6DA" />
        </linearGradient>
        <filter id={`sh${id}`} x="-30%" y="-30%" width="160%" height="170%">
          <feDropShadow dx="0" dy="14" stdDeviation="16" floodColor="#0D1626" floodOpacity="0.28" />
        </filter>
      </defs>
      <rect
        x="8"
        y="8"
        width="240"
        height="240"
        rx="56"
        fill={`url(#bg${id})`}
        stroke="rgba(25,38,63,0.10)"
        strokeWidth="2"
        filter={shadow ? `url(#sh${id})` : undefined}
      />
      <g
        style={{
          transformOrigin: '124px 132px',
          transform: `scale(${0.6 + 0.4 * s})`,
          opacity: Math.min(1, s * 1.5),
        }}
      >
        <text
          x="124"
          y="200"
          textAnchor="middle"
          fontFamily={fonts.display}
          fontWeight={700}
          fontSize="200"
          fill={colors.navy}
          stroke={colors.navy}
          strokeWidth="8"
          strokeLinejoin="round"
        >
          S
        </text>
      </g>
      <circle
        cx="197"
        cy={188 - (1 - dot) * 120}
        r="12"
        fill="#FBF8F2"
        stroke={colors.navy}
        strokeWidth="4.5"
        opacity={Math.min(1, dot * 3)}
      />
    </svg>
  );
};
