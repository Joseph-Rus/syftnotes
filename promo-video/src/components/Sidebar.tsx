import React from 'react';
import {colors, fonts} from '../config';
import {ClassDot} from './Layout';

export type SidebarRow = {name: string; color: string; count: number; lit?: boolean; pulse?: number};

/** The library window's sidebar: the classes, each with its count, and Unsorted. */
export const Sidebar: React.FC<{rows: SidebarRow[]; top?: React.ReactNode; footer?: string}> = ({
  rows,
  top,
  footer = 'Library connected',
}) => (
  <div
    style={{
      width: 250,
      margin: 14,
      marginRight: 0,
      borderRadius: 20,
      background: 'rgba(255,255,255,0.04)',
      border: `1.5px solid ${colors.glassEdge}`,
      padding: '18px 12px',
      display: 'flex',
      flexDirection: 'column',
      fontFamily: fonts.ui,
    }}
  >
    {top}
    <div style={{fontSize: 15, fontWeight: 600, color: colors.nightInk3, padding: '0 12px 10px'}}>Classes</div>
    {rows.map((r) => (
      <div
        key={r.name}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '11px 12px',
          borderRadius: 12,
          fontSize: 19,
          fontWeight: 600,
          background: r.lit ? 'rgba(255,255,255,0.08)' : 'transparent',
          color: colors.nightInk,
        }}
      >
        <ClassDot color={r.color} />
        <div style={{flex: 1}}>{r.name}</div>
        <div
          style={{
            color: r.pulse ? colors.lagoonBright : colors.nightInk2,
            fontWeight: 500,
            transform: `scale(${1 + (r.pulse ?? 0) * 0.35})`,
          }}
        >
          {r.count}
        </div>
      </div>
    ))}
    <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '16px 12px', fontSize: 19, color: colors.nightInk}}>
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke={colors.lagoonBright} strokeWidth="2">
        <path d="M4 13h4l2 3h4l2-3h4M4 13l2-8h12l2 8v6H4z" strokeLinejoin="round" />
      </svg>
      <div style={{flex: 1}}>Unsorted</div>
      <div style={{color: colors.nightInk2, fontWeight: 500}}>2</div>
    </div>
    <div style={{flex: 1}} />
    <div style={{display: 'flex', alignItems: 'center', gap: 8, padding: '0 12px', fontSize: 14, color: colors.nightInk2}}>
      <ClassDot color="#3FB26F" size={8} />
      {footer}
    </div>
  </div>
);
