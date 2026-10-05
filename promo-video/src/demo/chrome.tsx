import React from 'react';
import {colors, fonts} from '../config';
import {ClassDot, TrafficLights, Win} from '../components/Layout';
import {Sidebar, SidebarRow} from '../components/Sidebar';
import {Logo} from '../components/Logo';
import {DrawnScale} from '../promo2/DrawnScale';
import {useStage} from '../promo2/ui';

// The demo's pieces of the app as it is now (the screenshot tests in engine/tests/StudyStash.App.Tests, dark): the
// library window's toolbar (sidebar, back and forward; folder, share and delete; search and Settings), Home and Due
// above the classes, and the notifications in the corner. Drawn at the first two videos' size (a 1320-wide window,
// text about a quarter larger than on a Mac) so they sit with the second video's diagrams, drawings and plots.

/** The app's Material Symbols that the demo needs, redrawn as outlines on a 24-unit grid. */
export type Glyph =
  | 'sidebar' | 'back' | 'forward' | 'move' | 'share' | 'trash' | 'search' | 'gear' | 'home' | 'due' | 'open'
  | 'mic' | 'stop' | 'pause' | 'shrink' | 'sparkle' | 'updown' | 'up' | 'check' | 'desktop' | 'terminal' | 'code'
  | 'hub' | 'tune' | 'palette' | 'keyboard' | 'calendar' | 'link' | 'library' | 'book' | 'sort' | 'school' | 'folder'
  | 'phone' | 'close' | 'chevron' | 'books' | 'checklist' | 'paperclip' | 'plus';

export const G: React.FC<{name: Glyph; size?: number; color?: string; width?: number}> = ({name, size = 18, color = colors.text, width = 1.9}) => {
  const p = {fill: 'none', stroke: color, strokeWidth: width, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const};
  const g = (() => {
    switch (name) {
      case 'sidebar':
        return (<><rect x="3.5" y="4.5" width="17" height="15" rx="3" {...p} /><path d="M14.5 4.5v15" {...p} /></>);
      case 'back':
        return <path d="M19 12H5.5M11 6l-6 6 6 6" {...p} />;
      case 'forward':
        return <path d="M5 12h13.5M13 6l6 6-6 6" {...p} />;
      case 'move':
        return (<><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" {...p} /><path d="M9.5 13.5h6M13 11l2.5 2.5L13 16" {...p} /></>);
      case 'share':
        return (<><path d="M8.5 9.5H7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7a2 2 0 0 0-2-2h-1.5" {...p} /><path d="M12 14V3.5M8.5 7 12 3.5 15.5 7" {...p} /></>);
      case 'trash':
        return (<><path d="M4.5 6.5h15M9.5 6.5V4.5h5v2M6.5 6.5l1 13a1.5 1.5 0 0 0 1.5 1.4h6a1.5 1.5 0 0 0 1.5-1.4l1-13" {...p} /><path d="M10.2 10.5v6.5M13.8 10.5v6.5" {...p} /></>);
      case 'search':
        return (<><circle cx="10.5" cy="10.5" r="6" {...p} /><path d="M15 15l5 5" {...p} /></>);
      case 'gear':
        return (<><circle cx="12" cy="12" r="3" {...p} /><path d="M12 2.8v2.6M12 18.6v2.6M3.9 7.3l2.3 1.3M17.8 15.4l2.3 1.3M3.9 16.7l2.3-1.3M17.8 8.6l2.3-1.3" {...p} /><circle cx="12" cy="12" r="6.6" {...p} /></>);
      case 'home':
        return (<><path d="M4.5 10.5 12 4.5l7.5 6v8.5a1.5 1.5 0 0 1-1.5 1.5h-3.5v-6h-5v6H6a1.5 1.5 0 0 1-1.5-1.5z" {...p} /></>);
      case 'due':
      case 'calendar':
        return (<><rect x="3.5" y="5" width="17" height="15.5" rx="3" {...p} /><path d="M8 3v4M16 3v4M3.5 10h17" {...p} />{name === 'due' ? <path d="M9 15l2 2 4-4" {...p} /> : null}</>);
      case 'open':
        return (<><path d="M13.5 4.5h6v6M19.5 4.5 11 13" {...p} /><path d="M17.5 13.5v4a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2h4" {...p} /></>);
      case 'mic':
        return (<><rect x="9" y="3.5" width="6" height="11" rx="3" {...p} /><path d="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v2.5" {...p} /></>);
      case 'stop':
        return <rect x="6.5" y="6.5" width="11" height="11" rx="2" fill={color} />;
      case 'pause':
        return (<><rect x="6.5" y="5.5" width="3.8" height="13" rx="1.2" fill={color} /><rect x="13.7" y="5.5" width="3.8" height="13" rx="1.2" fill={color} /></>);
      case 'shrink':
        return <path d="M4.5 19.5l6-6M10.5 18.5v-5h-5M19.5 4.5l-6 6M13.5 5.5v5h5" {...p} />;
      case 'sparkle':
        return (<><path d="M10 3.5l1.5 4.6a2.8 2.8 0 0 0 1.8 1.8l4.6 1.5-4.6 1.5a2.8 2.8 0 0 0-1.8 1.8L10 19.3l-1.5-4.6a2.8 2.8 0 0 0-1.8-1.8L2.1 11.4l4.6-1.5a2.8 2.8 0 0 0 1.8-1.8z" fill={color} stroke="none" /><path d="M19 15l.6 1.8a1.3 1.3 0 0 0 .8.8l1.8.6-1.8.6a1.3 1.3 0 0 0-.8.8L19 21.4l-.6-1.8a1.3 1.3 0 0 0-.8-.8l-1.8-.6 1.8-.6a1.3 1.3 0 0 0 .8-.8z" fill={color} stroke="none" /></>);
      case 'updown':
        return <path d="M8 9.5l4-4 4 4M8 14.5l4 4 4-4" {...p} />;
      case 'up':
        return <path d="M12 19V5.5M6 11l6-6 6 6" {...p} />;
      case 'check':
        return <path d="M5 12.5l4.3 4.3L19 7.2" {...p} />;
      case 'desktop':
        return (<><rect x="3.5" y="4.5" width="17" height="12" rx="2" {...p} /><path d="M9 20h6M12 16.5V20" {...p} /></>);
      case 'terminal':
        return (<><rect x="3.5" y="4.5" width="17" height="15" rx="2.5" {...p} /><path d="M7.5 10l2.5 2-2.5 2M12 14.5h4" {...p} /></>);
      case 'code':
        return <path d="M9 7l-5 5 5 5M15 7l5 5-5 5" {...p} />;
      case 'hub':
        return (<><circle cx="12" cy="12" r="2.4" {...p} /><circle cx="5" cy="6" r="1.8" {...p} /><circle cx="19" cy="6" r="1.8" {...p} /><circle cx="5" cy="18" r="1.8" {...p} /><circle cx="19" cy="18" r="1.8" {...p} /><path d="M6.4 7.2l3.7 3.2M17.6 7.2l-3.7 3.2M6.4 16.8l3.7-3.2M17.6 16.8l-3.7-3.2" {...p} /></>);
      case 'tune':
        return <path d="M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4" {...p} />;
      case 'palette':
        return (<><path d="M12 3.5a8.5 8.5 0 1 0 0 17c1.2 0 1.6-.9 1.2-1.8-.5-1.1.2-2.2 1.4-2.2h2.2a3.7 3.7 0 0 0 3.7-3.7A8.5 8.5 0 0 0 12 3.5z" {...p} /><circle cx="7.8" cy="11" r="1" fill={color} /><circle cx="10.5" cy="7.3" r="1" fill={color} /><circle cx="15" cy="8" r="1" fill={color} /></>);
      case 'keyboard':
        return (<><rect x="2.5" y="6" width="19" height="12" rx="2" {...p} /><path d="M6 10h.01M9 10h.01M12 10h.01M15 10h.01M18 10h.01M7.5 14h9" {...p} /></>);
      case 'link':
        return <path d="M9.5 14.5l5-5M8 11l-2 2a3.5 3.5 0 0 0 5 5l2-2M16 13l2-2a3.5 3.5 0 0 0-5-5l-2 2" {...p} />;
      case 'library':
        return (<><rect x="4" y="4" width="16" height="16" rx="2.5" {...p} /><path d="M4 9.5h16M4 15h16M7 7h.01M7 12.3h.01" {...p} /></>);
      case 'book':
        return (<><path d="M6 4.5h11.5v15H7.5A1.5 1.5 0 0 1 6 18z" {...p} /><path d="M6 17.5a1.5 1.5 0 0 1 1.5-1.5h10" {...p} /></>);
      case 'sort':
        return <path d="M4.5 7h11M4.5 12h8M4.5 17h5M17.5 11v8M15 16.5l2.5 2.5 2.5-2.5" {...p} />;
      case 'school':
        return (<><path d="M2.5 9.5 12 5l9.5 4.5L12 14z" {...p} /><path d="M6.5 11.5v4c1.5 1.6 3.5 2.4 5.5 2.4s4-.8 5.5-2.4v-4" {...p} /></>);
      case 'folder':
        return <path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" {...p} />;
      case 'phone':
        return (<><rect x="6.5" y="2.5" width="11" height="19" rx="2.5" {...p} /><path d="M11 18.5h2" {...p} /></>);
      case 'close':
        return <path d="M6.5 6.5l11 11M17.5 6.5l-11 11" {...p} />;
      case 'chevron':
        return <path d="M9.5 6.5L15 12l-5.5 5.5" {...p} />;
      case 'books':
        return (<><rect x="3.5" y="4" width="4.5" height="16" rx="1" {...p} /><rect x="9.5" y="4" width="4.5" height="16" rx="1" {...p} /><path d="M15.6 5.2l3.9-1 3 15.2-3.9 1z" {...p} /></>);
      case 'checklist':
        return <path d="M4 7l1.6 1.6L8.8 5.4M4 15.5l1.6 1.6 3.2-3.2M11.5 7.5h8.5M11.5 16h8.5" {...p} />;
      case 'paperclip':
        return <path d="M16.5 7.5l-7.6 7.6a2 2 0 0 0 2.8 2.8l8-8a4 4 0 0 0-5.6-5.6l-8.2 8.2a6 6 0 0 0 8.5 8.5l6.6-6.6" {...p} />;
      case 'plus':
        return (<><circle cx="12" cy="12" r="9" {...p} /><path d="M12 8v8M8 12h8" {...p} /></>);
    }
  })();
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" style={{display: 'block', flexShrink: 0}}>
      {g}
    </svg>
  );
};

// ---- The library window ----------------------------------------------------------------------------------------

export const tool: React.CSSProperties = {
  height: 40,
  borderRadius: 20,
  background: 'rgba(255,255,255,0.055)',
  border: '1px solid rgba(255,255,255,0.11)',
  boxShadow: '0 1px 0 rgba(255,255,255,0.05) inset',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  boxSizing: 'border-box',
  color: colors.text,
  fontFamily: fonts.ui,
};

const Round: React.FC<{glyph: Glyph}> = ({glyph}) => (
  <div style={{...tool, width: 40}}>
    <G name={glyph} size={19} />
  </div>
);

const Pair: React.FC<{glyphs: Glyph[]}> = ({glyphs}) => (
  <div style={{...tool, gap: 14, padding: '0 12px'}}>
    {glyphs.map((g, i) => (
      <G key={i} name={g} size={19} color={g === 'forward' ? colors.text2 : colors.text} />
    ))}
  </div>
);

/** What the toolbar's right side holds: a lecture's folder, share and delete; Canvas's Open in Canvas; or just search. */
export type Tools = 'lecture' | 'canvas' | 'plain';

/** The library window's toolbar, over its top: the sidebar's toggle, back and forward, and on the right what the page
 * offers, then search and Settings. */
export const Toolbar: React.FC<{w: number; tools: Tools; sidebar: boolean}> = ({w, tools, sidebar}) => (
  <>
    <div style={{position: 'absolute', left: sidebar ? 200 : 96, top: 10, display: 'flex', gap: 8, zIndex: 3}}>
      <Round glyph="sidebar" />
      <Pair glyphs={['back', 'forward']} />
    </div>
    <div style={{position: 'absolute', left: 0, width: w - 16, top: 10, display: 'flex', justifyContent: 'flex-end', gap: 8, zIndex: 3}}>
      {tools === 'lecture' ? <Pair glyphs={['move', 'share', 'trash']} /> : null}
      {tools === 'canvas' ? (
        <div style={{...tool, gap: 9, padding: '0 16px', fontSize: 15.5, fontWeight: 600}}>
          <G name="open" size={17} /> Open in Canvas
        </div>
      ) : null}
      <Round glyph="search" />
      <Round glyph="gear" />
    </div>
  </>
);

// The demo's library: one Mac, six classes.
export const LIBRARY = {
  cs: {name: 'CS 101', color: colors.cs, count: 12},
  bio: {name: 'BIO 110', color: colors.bio, count: 9},
  calc: {name: 'CALC II', color: colors.calc, count: 11},
  ml: {name: 'CS 340', color: '#14A3B8', count: 10},
  engr: {name: 'ENGR 120', color: colors.hist, count: 6},
  nurs: {name: 'NURS 210', color: colors.health, count: 8},
};
export type ClassId = keyof typeof LIBRARY;

const NavRow: React.FC<{glyph: Glyph; label: string; count?: number; lit?: boolean}> = ({glyph, label, count, lit}) => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      padding: '9px 12px',
      marginBottom: 2,
      borderRadius: 11,
      fontSize: 16.5,
      fontWeight: lit ? 700 : 500,
      background: lit ? colors.hover : 'transparent',
      border: lit ? `1px solid ${colors.edge}` : '1px solid transparent',
    }}
  >
    <G name={glyph} size={18} color={lit ? colors.text : colors.text2} />
    <div style={{flex: 1}}>{label}</div>
    {count !== undefined ? <div style={{color: colors.text2, fontWeight: 400}}>{count}</div> : null}
  </div>
);

/** The sidebar as the app has it now: Home (and Due, with Canvas), then the classes, Unsorted and the library's state. */
export const DemoSidebar: React.FC<{lit: ClassId | 'home' | 'due' | null; pulse?: number; counts?: Partial<Record<ClassId, number>>; due?: boolean; footer?: string}> = ({
  lit,
  pulse,
  counts = {},
  due = true,
  footer = 'Library connected',
}) => {
  const rows: SidebarRow[] = (Object.keys(LIBRARY) as ClassId[]).map((k) => ({
    ...LIBRARY[k],
    count: counts[k] ?? LIBRARY[k].count,
    lit: k === lit,
    pulse: k === lit ? pulse : undefined,
  }));
  return (
    <Sidebar
      rows={rows}
      footer={footer}
      top={
        <>
          <NavRow glyph="home" label="Home" lit={lit === 'home'} />
          {due ? <NavRow glyph="due" label="Due" count={3} lit={lit === 'due'} /> : null}
          <div style={{height: 8}} />
        </>
      }
    />
  );
};

type Stage = ReturnType<typeof useStage>;

/** The library window at the stage's place (promo2's useStage): sidebar and toolbar (landscape), an optional list,
 * and the page. On a phone-shaped frame just the page, with its traffic lights. */
export const DemoWindow: React.FC<{
  stage: Stage;
  lit: ClassId | 'home' | 'due' | null;
  pulse?: number;
  counts?: Partial<Record<ClassId, number>>;
  list?: React.ReactNode;
  tools?: Tools;
  enter?: number;
  children: React.ReactNode;
}> = ({stage, lit, pulse, counts, list, tools = 'lecture', enter = 1, children}) => {
  const z = stage.z * (0.98 + 0.02 * enter);
  return (
    <div
      style={{
        position: 'absolute',
        left: stage.left,
        top: stage.top,
        opacity: enter,
        transformOrigin: '0 0',
        transform: `translate(${(stage.w * stage.z * (1 - (0.98 + 0.02 * enter))) / 2}px, ${(1 - enter) * 30}px) scale(${z})`,
      }}
    >
      <DrawnScale by={z}>
        <Win w={stage.w} h={stage.h} style={{display: 'flex'}}>
          {stage.vertical ? null : <DemoSidebar lit={lit} pulse={pulse} counts={counts} />}
          {stage.list ? (
            <div style={{width: stage.list, borderRight: `1px solid ${colors.line}`, padding: '0 12px', flexShrink: 0}}>
              <div style={{height: 62}} />
              {list}
            </div>
          ) : null}
          <div style={{flex: 1, position: 'relative', minWidth: 0, overflow: 'hidden'}}>
            {stage.vertical ? <TrafficLights style={{padding: '22px 24px', position: 'absolute', left: 0, top: 0}} /> : null}
            {children}
          </div>
          {stage.vertical ? null : <Toolbar w={stage.w} tools={tools} sidebar />}
        </Win>
      </DrawnScale>
    </div>
  );
};

// ---- Notifications ---------------------------------------------------------------------------------------------

/** One of the app's notifications, as the "mac-12-toast" shots draw them: its icon, a title, a line, and a button. */
export const Toast: React.FC<{title: string; body: string; button?: string; when?: string; width?: number; style?: React.CSSProperties}> = ({title, body, button, when, width = 444, style}) => (
  <div
    style={{
      width,
      boxSizing: 'border-box',
      padding: '15px 16px 15px 15px',
      borderRadius: 18,
      background: 'rgba(46, 46, 49, 0.97)',
      border: '1px solid rgba(255,255,255,0.10)',
      boxShadow: '0 24px 50px -16px rgba(0,0,0,0.7), 0 0 0 0.5px rgba(0,0,0,0.6)',
      fontFamily: fonts.ui,
      color: colors.text,
      display: 'flex',
      gap: 14,
      ...style,
    }}
  >
    <Logo size={40} shadow={false} />
    <div style={{flex: 1, minWidth: 0}}>
      <div style={{display: 'flex', alignItems: 'baseline'}}>
        <div style={{flex: 1, fontSize: 16, fontWeight: 700}}>{title}</div>
        {when ? <div style={{fontSize: 13.5, color: colors.text3}}>{when}</div> : null}
      </div>
      <div style={{fontSize: 15, lineHeight: 1.38, color: colors.text2, marginTop: 3}}>{body}</div>
      {button ? (
        <div style={{display: 'flex', justifyContent: 'flex-end', marginTop: 10}}>
          <div style={{padding: '6px 15px', borderRadius: 999, background: 'rgba(255,255,255,0.12)', fontSize: 15, fontWeight: 600}}>{button}</div>
        </div>
      ) : null}
    </div>
  </div>
);

/** A row in the demo's list column (the app's LectureRow): title, date and length, and the first line of the notes. */
export const LectureRow: React.FC<{title: string; meta: string; snippet?: string; right?: React.ReactNode; color?: string; lit?: boolean; style?: React.CSSProperties}> = ({
  title,
  meta,
  snippet,
  right,
  color,
  lit,
  style,
}) => (
  <div
    style={{
      padding: '12px 14px',
      borderRadius: 14,
      background: lit ? colors.lagoon : 'transparent',
      boxShadow: lit ? 'inset 0 0 0 1px rgba(255,255,255,0.18)' : 'none',
      color: colors.text,
      ...style,
    }}
  >
    <div style={{display: 'flex', fontSize: 16.5, fontWeight: 700, gap: 10}}>
      <div style={{flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{title}</div>
      {right}
    </div>
    <div style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: 3, fontSize: 14, color: lit ? 'rgba(255,255,255,0.88)' : colors.text2}}>
      {color ? <ClassDot color={lit ? '#FFFFFF' : color} size={7} /> : null}
      {meta}
    </div>
    {snippet ? <div style={{marginTop: 5, fontSize: 14, lineHeight: 1.4, color: lit ? 'rgba(255,255,255,0.9)' : colors.text2}}>{snippet}</div> : null}
  </div>
);
