import React from 'react';
import {colors, fonts} from '../config';
import {ClassDot, TrafficLights, Win} from './Layout';

export type SidebarRow = {name: string; color: string; count: number; lit?: boolean; pulse?: number};

/** The library window's sidebar, as in the light look: the classes with their counts, and Unsorted. */
export const Sidebar: React.FC<{rows: SidebarRow[]; top?: React.ReactNode; footer?: string}> = ({rows, top, footer = 'Library connected'}) => (
  <div
    style={{
      width: 240,
      margin: 12,
      marginRight: 0,
      borderRadius: 18,
      background: 'rgba(255,255,255,0.78)',
      border: `1px solid ${colors.line}`,
      boxShadow: '0 1px 2px rgba(16,24,40,0.04)',
      padding: '8px 10px 16px',
      display: 'flex',
      flexDirection: 'column',
      fontFamily: fonts.ui,
    }}
  >
    <TrafficLights style={{padding: '12px 10px 16px'}} />
    {top}
    <div style={{fontSize: 14, fontWeight: 600, color: colors.text3, padding: '0 12px 8px'}}>Classes</div>
    {rows.map((r) => (
      <div
        key={r.name}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '10px 12px',
          borderRadius: 11,
          fontSize: 17,
          fontWeight: 500,
          background: r.lit ? 'rgba(16,24,40,0.065)' : 'transparent',
        }}
      >
        <ClassDot color={r.color} size={9} />
        <div style={{flex: 1, fontWeight: r.lit ? 600 : 500}}>{r.name}</div>
        <div style={{color: r.pulse ? colors.lagoon : colors.text2, fontWeight: r.pulse ? 700 : 400, transform: `scale(${1 + (r.pulse ?? 0) * 0.3})`}}>{r.count}</div>
      </div>
    ))}
    <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '14px 12px', fontSize: 17}}>
      <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke={colors.text2} strokeWidth="2">
        <path d="M4 13h4l2 3h4l2-3h4M4 13l2-8h12l2 8v6H4z" strokeLinejoin="round" />
      </svg>
      <div style={{flex: 1}}>Unsorted</div>
      <div style={{color: colors.text2}}>2</div>
    </div>
    <div style={{flex: 1}} />
    <div style={{display: 'flex', alignItems: 'center', gap: 8, padding: '0 12px', fontSize: 13, color: colors.text2}}>
      <ClassDot color="#22A45D" size={7} />
      {footer}
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
        {sidebar ? <div style={{height: 20}} /> : <TrafficLights style={{padding: '20px 10px 6px'}} />}
        {list}
      </div>
    ) : null}
    <div style={{flex: 1, position: 'relative', minWidth: 0}}>
      {!sidebar && !list ? <TrafficLights /> : null}
      {children}
    </div>
  </Win>
);

/** A row in a list column: a title, a line under it, and something on the right. */
export const ListRow: React.FC<{title: string; sub: string; color: string; right?: string; lit?: boolean; accent?: boolean; style?: React.CSSProperties}> = ({
  title,
  sub,
  color,
  right,
  lit,
  accent,
  style,
}) => (
  <div style={{padding: '11px 12px', borderRadius: 13, background: lit ? colors.lagoon : 'transparent', color: lit ? '#fff' : colors.text, ...style}}>
    <div style={{display: 'flex', fontSize: 16.5, fontWeight: 600, gap: 10}}>
      <div style={{flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{title}</div>
      {right ? <div style={{fontSize: 14, fontWeight: accent ? 700 : 500, color: lit ? '#fff' : accent ? colors.lagoon : colors.text2}}>{right}</div> : null}
    </div>
    <div style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: 4, fontSize: 13.5, color: lit ? 'rgba(255,255,255,0.88)' : colors.text2}}>
      <ClassDot color={lit ? '#fff' : color} size={7} />
      {sub}
    </div>
  </div>
);

export const ListHead: React.FC<{title: string; sub: string}> = ({title, sub}) => (
  <div style={{padding: '0 12px 6px'}}>
    <div style={{fontFamily: fonts.display, fontSize: 26, fontWeight: 700, letterSpacing: '-0.02em'}}>{title}</div>
    <div style={{fontSize: 13.5, color: colors.text2, marginTop: 2}}>{sub}</div>
  </div>
);

export const Group: React.FC<{name: string}> = ({name}) => (
  <div style={{fontSize: 13.5, fontWeight: 600, color: colors.text3, padding: '16px 12px 4px'}}>{name}</div>
);
