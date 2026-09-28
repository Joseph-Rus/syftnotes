import React from 'react';
import {colors, fonts} from '../config';
import {ClassDot, TrafficLights, Win} from './Layout';

export type SidebarRow = {name: string; color: string; count: number; lit?: boolean; pulse?: number};

/** The library window's sidebar in dark mode, as MacLibrary draws it: a glass card of classes with their counts. */
export const Sidebar: React.FC<{rows: SidebarRow[]; top?: React.ReactNode; footer?: string}> = ({rows, top, footer = 'Library connected'}) => (
  <div style={{width: 240, display: 'flex', flexDirection: 'column', padding: '0 0 12px 12px', flexShrink: 0}}>
    <TrafficLights style={{padding: '22px 12px 18px'}} />
    <div
      style={{
        flex: 1,
        borderRadius: 18,
        background: colors.pane,
        border: `1px solid ${colors.edge}`,
        padding: '14px 10px 16px',
        display: 'flex',
        flexDirection: 'column',
        fontFamily: fonts.ui,
      }}
    >
      {top}
      <div style={{fontSize: 14, fontWeight: 700, color: colors.text2, padding: '0 12px 8px'}}>Classes</div>
      {rows.map((r) => (
        <div
          key={r.name}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            padding: '9px 12px',
            borderRadius: 11,
            fontSize: 16.5,
            fontWeight: r.lit ? 700 : 500,
            background: r.lit ? colors.hover : 'transparent',
            border: r.lit ? `1px solid ${colors.edge}` : '1px solid transparent',
          }}
        >
          <ClassDot color={r.color} size={9} />
          <div style={{flex: 1}}>{r.name}</div>
          <div style={{color: r.pulse ? colors.lagoonBright : colors.text2, fontWeight: r.pulse ? 700 : 400, transform: `scale(${1 + (r.pulse ?? 0) * 0.3})`}}>{r.count}</div>
        </div>
      ))}
      <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '14px 12px', fontSize: 16.5}}>
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke={colors.lagoonBright} strokeWidth="2">
          <rect x="3.5" y="4" width="17" height="16" rx="3" />
          <path d="M3.5 13h4.5l1.5 2.5h5L16 13h4.5" strokeLinejoin="round" />
        </svg>
        <div style={{flex: 1}}>Unsorted</div>
        <div style={{color: colors.text2}}>2</div>
      </div>
      <div style={{flex: 1}} />
      <div style={{display: 'flex', alignItems: 'center', gap: 8, padding: '0 12px', fontSize: 13, color: colors.text2}}>
        <ClassDot color="#22C55E" size={7} />
        {footer}
      </div>
    </div>
  </div>
);

/** The library window: sidebar, a list, and the page. On a phone-shaped frame the sidebar (and, if asked, the list) steps aside. */
export const LibraryWindow: React.FC<{
  w: number;
  h: number;
  sidebar?: React.ReactNode;
  list?: React.ReactNode;
  listWidth?: number;
  children: React.ReactNode;
}> = ({w, h, sidebar, list, listWidth = 290, children}) => (
  <Win w={w} h={h} style={{display: 'flex'}}>
    {sidebar}
    {list ? (
      <div style={{width: listWidth, borderRight: `1px solid ${colors.line}`, padding: '0 12px', flexShrink: 0}}>
        {sidebar ? <div style={{height: 28}} /> : <TrafficLights style={{padding: '22px 10px 8px'}} />}
        {list}
      </div>
    ) : null}
    <div style={{flex: 1, position: 'relative', minWidth: 0}}>
      {!sidebar && !list ? <TrafficLights style={{padding: '22px 24px'}} /> : null}
      {children}
    </div>
  </Win>
);

/** A row in a list column: a title, a line under it, and something on the right. The chosen one is Lagoon teal. */
export const ListRow: React.FC<{title: string; sub: string; color: string; right?: string; lit?: boolean; accent?: boolean; style?: React.CSSProperties}> = ({
  title,
  sub,
  color,
  right,
  lit,
  accent,
  style,
}) => (
  <div
    style={{
      padding: '11px 12px',
      borderRadius: 13,
      background: lit ? colors.lagoon : 'transparent',
      boxShadow: lit ? 'inset 0 0 0 1px rgba(255,255,255,0.18)' : 'none',
      color: colors.text,
      ...style,
    }}
  >
    <div style={{display: 'flex', fontSize: 16.5, fontWeight: 700, gap: 10}}>
      <div style={{flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{title}</div>
      {right ? <div style={{fontSize: 14, fontWeight: accent ? 700 : 500, color: lit ? 'rgba(255,255,255,0.85)' : accent ? colors.lagoonBright : colors.text2}}>{right}</div> : null}
    </div>
    <div style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: 4, fontSize: 13.5, color: lit ? 'rgba(255,255,255,0.88)' : colors.text2}}>
      <ClassDot color={lit ? '#FFFFFF' : color} size={7} />
      {sub}
    </div>
  </div>
);

export const ListHead: React.FC<{title: string; sub: string}> = ({title, sub}) => (
  <div style={{padding: '0 12px 6px'}}>
    <div style={{fontFamily: fonts.display, fontSize: 24, fontWeight: 700, letterSpacing: '-0.02em'}}>{title}</div>
    <div style={{fontSize: 13.5, color: colors.text2, marginTop: 2}}>{sub}</div>
  </div>
);

export const Group: React.FC<{name: string; accent?: boolean}> = ({name, accent}) => (
  <div style={{fontSize: 13.5, fontWeight: 700, color: accent ? colors.lagoonBright : colors.text2, padding: '16px 12px 4px'}}>{name}</div>
);
