import React from 'react';
import {useVideoConfig} from 'remotion';
import {colors, fonts} from '../config';
import {ClassDot, TrafficLights, useVertical, Win} from '../components/Layout';
import {Sidebar, SidebarRow} from '../components/Sidebar';

// The second video's pieces of the app, drawn as the app's own screenshot tests show them in dark mode: the toolbar
// that appears over a diagram, drawing or plot while the pointer is on it; the strip under it (stepping, testing
// yourself, predicting); a pinned box's actions; and the note page around them.

export const ui = {
  accent: '#2BB7B2', // the accent outline and arrows of a pinned box or a step (Lagoon, bright on dark)
  accentGlow: 'rgba(43, 183, 178, 0.35)',
  bar: '#2A3033', // a floating bar
  barEdge: 'rgba(255,255,255,0.09)',
  strip: '#1E282B', // the strip under a figure
  box: '#212A2D',
  boxEdge: 'rgba(255,255,255,0.13)',
  boxText: '#EEF2F4',
  boxSub: '#97A2AA',
  arrow: '#A3ABB1',
  hiddenBar: '#3A4549',
  knew: '#3FBF6E',
  notYet: '#E6A23C',
};

type IconName =
  | 'play_circle' | 'quiz' | 'open_in_full' | 'subject' | 'chat' | 'graphic_eq' | 'add' | 'check' | 'close'
  | 'chevron_left' | 'chevron_right' | 'play_arrow' | 'pause' | 'edit' | 'schedule' | 'refresh' | 'remove';

/** The app's Material Symbols, redrawn as outlines on a 24-unit grid. */
export const Icon: React.FC<{name: IconName; size?: number; color?: string}> = ({name, size = 18, color = '#E6EAED'}) => {
  const p = {fill: 'none', stroke: color, strokeWidth: 1.9, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const};
  const g = (() => {
    switch (name) {
      case 'play_circle':
        return (<><circle cx="12" cy="12" r="8.6" {...p} /><path d="M10.2 8.6v6.8l5.4-3.4z" fill={color} stroke="none" /></>);
      case 'quiz':
        return (<><rect x="7" y="3.5" width="13.5" height="13.5" rx="2.2" {...p} /><path d="M4 7v11.3A2.2 2.2 0 0 0 6.2 20.5H17" {...p} /><path d="M12 8.4a1.9 1.9 0 1 1 2.4 1.8c-.6.2-.9.6-.9 1.2v.3" {...p} strokeWidth={1.6} /><circle cx="13.5" cy="13.9" r="0.9" fill={color} /></>);
      case 'open_in_full':
        return <path d="M14 4h6v6M20 4l-6.5 6.5M10 20H4v-6M4 20l6.5-6.5" {...p} />;
      case 'subject':
        return <path d="M5 6.5h14M5 10.5h14M5 14.5h14M5 18.5h9" {...p} />;
      case 'chat':
        return (<><path d="M4.5 5.5h15v10.5H9l-4.5 3.5z" {...p} /><path d="M8 9.3h8M8 12.4h5.5" {...p} strokeWidth={1.6} /></>);
      case 'graphic_eq':
        return <path d="M4 10.5v3M8 7v10M12 4v16M16 7.5v9M20 10.5v3" {...p} />;
      case 'add':
        return <path d="M12 5v14M5 12h14" {...p} />;
      case 'remove':
        return <path d="M5 12h14" {...p} />;
      case 'check':
        return <path d="M5 12.5l4.3 4.3L19 7.2" {...p} />;
      case 'close':
        return <path d="M6.5 6.5l11 11M17.5 6.5l-11 11" {...p} />;
      case 'chevron_left':
        return <path d="M14.5 6.5L9 12l5.5 5.5" {...p} />;
      case 'chevron_right':
        return <path d="M9.5 6.5L15 12l-5.5 5.5" {...p} />;
      case 'play_arrow':
        return <path d="M8.5 6.5v11l8.5-5.5z" {...p} />;
      case 'pause':
        return <path d="M9 6.5v11M15 6.5v11" {...p} strokeWidth={2.4} />;
      case 'edit':
        return <path d="M5 19l1-4.2L15.6 5.2a2 2 0 0 1 2.8 0l.4.4a2 2 0 0 1 0 2.8L9.2 18 5 19z" {...p} />;
      case 'schedule':
        return (<><circle cx="12" cy="12" r="8.4" {...p} /><path d="M12 7.5V12l3 2" {...p} /></>);
      case 'refresh':
        return <path d="M18.5 12a6.5 6.5 0 1 1-2-4.7M18.8 4.8v3.6h-3.6" {...p} />;
    }
  })();
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" style={{display: 'block', flexShrink: 0}}>
      {g}
    </svg>
  );
};

const panel: React.CSSProperties = {
  background: ui.bar,
  border: `1px solid ${ui.barEdge}`,
  borderRadius: 11,
  boxShadow: '0 10px 28px -8px rgba(0,0,0,0.65), 0 0 0 0.5px rgba(0,0,0,0.5)',
  fontFamily: fonts.ui,
  color: colors.text,
};

/** The toolbar over a figure while the pointer is on it: icon buttons, one of them maybe pressed (`on`) or under the pointer (`hot`). */
export const FloatingBar: React.FC<{icons: IconName[]; on?: IconName | null; hot?: IconName | null; p?: number; style?: React.CSSProperties}> = ({icons, on, hot, p = 1, style}) => (
  <div style={{...panel, display: 'flex', gap: 2, padding: 4, opacity: p, transform: `translateY(${(1 - p) * -4}px)`, ...style}}>
    {icons.map((n) => (
      <div
        key={n}
        style={{
          width: 32,
          height: 30,
          borderRadius: 8,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: n === on ? 'rgba(255,255,255,0.14)' : n === hot ? 'rgba(255,255,255,0.08)' : 'transparent',
        }}
      >
        <Icon name={n} size={18} />
      </div>
    ))}
  </div>
);

/** A labelled button in a bar: an icon and its words. */
export const BarButton: React.FC<{icon?: IconName; label: string; hot?: boolean; strong?: boolean}> = ({icon, label, hot, strong = true}) => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      gap: 7,
      padding: '0 11px',
      height: 32,
      borderRadius: 8,
      fontSize: 14,
      fontWeight: strong ? 600 : 500,
      whiteSpace: 'nowrap',
      background: hot ? 'rgba(255,255,255,0.10)' : 'transparent',
    }}
  >
    {icon ? <Icon name={icon} size={17} /> : null}
    {label}
  </div>
);

/** A pinned box's actions, as DiagramChrome builds them: Explain this, Quiz me, Where was this said?, Zoom in. */
export const PinActions: React.FC<{p: number; hot?: string; style?: React.CSSProperties; zoomOnly?: boolean}> = ({p, hot, style}) => (
  <div style={{...panel, display: 'flex', padding: 4, gap: 1, opacity: Math.min(1, p * 1.5), transform: `translateY(${(1 - p) * -6}px) scale(${0.97 + 0.03 * p})`, transformOrigin: 'top left', ...style}}>
    <BarButton icon="chat" label="Explain this" hot={hot === 'explain'} />
    <BarButton icon="quiz" label="Quiz me" hot={hot === 'quiz'} />
    <BarButton icon="graphic_eq" label="Where was this said?" hot={hot === 'where'} />
    <BarButton icon="add" label="Zoom in" hot={hot === 'zoom'} />
  </div>
);

/** The strip under a figure: stepping, testing yourself or predicting. */
export const Strip: React.FC<{p?: number; children: React.ReactNode; style?: React.CSSProperties}> = ({p = 1, children, style}) => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      gap: 4,
      height: 42,
      padding: '0 8px',
      borderRadius: 10,
      background: ui.strip,
      border: `1px solid ${colors.line}`,
      fontFamily: fonts.ui,
      fontSize: 14.5,
      color: colors.text,
      opacity: p,
      transform: `translateY(${(1 - p) * 8}px)`,
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      ...style,
    }}
  >
    {children}
  </div>
);

export const StripIcon: React.FC<{name: IconName; hot?: boolean}> = ({name, hot}) => (
  <div style={{width: 30, height: 30, borderRadius: 7, display: 'flex', alignItems: 'center', justifyContent: 'center', background: hot ? 'rgba(255,255,255,0.12)' : 'transparent'}}>
    <Icon name={name} size={17} />
  </div>
);

export const Grow: React.FC = () => <div style={{flex: 1}} />;

/** "Written by Claude Code · Tue 11:14", and then what the diagrams are doing, as AiNotesModel's ShownByline. */
export const Byline: React.FC<{diagrams?: React.ReactNode}> = ({diagrams}) => (
  <span style={{fontSize: 14, color: colors.text3, display: 'inline-flex', alignItems: 'center', gap: 6}}>
    Written by Claude Code · Tue 11:14{diagrams ? <> · {diagrams}</> : null}
  </span>
);

/** The note page's top: class, date and length, the title, Notes | Transcript. */
export const NoteHead: React.FC<{cls: string; color: string; meta: string; title: string}> = ({cls, color, meta, title}) => (
  <>
    <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
      <ClassDot color={color} size={8} /> {cls} · {meta}
    </div>
    <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>{title}</div>
    <div style={{display: 'inline-flex', marginTop: 12, padding: 3, borderRadius: 999, background: colors.card, border: `1px solid ${colors.edge}`, fontSize: 14.5, fontWeight: 600}}>
      <div style={{padding: '6px 18px', borderRadius: 999, background: 'rgba(255,255,255,0.12)'}}>Notes</div>
      <div style={{padding: '6px 18px', color: colors.text2}}>Transcript</div>
    </div>
  </>
);

export const H2: React.FC<{children: React.ReactNode; style?: React.CSSProperties}> = ({children, style}) => (
  <div style={{fontFamily: fonts.display, fontSize: 21, fontWeight: 600, letterSpacing: '-0.01em', ...style}}>{children}</div>
);

export const Serif: React.FC<{children: React.ReactNode; style?: React.CSSProperties}> = ({children, style}) => (
  <div style={{fontFamily: fonts.serif, fontSize: 19, lineHeight: 1.5, color: colors.serifInk, ...style}}>{children}</div>
);

// The classes in the second video's library.
export const CLASSES = {
  bio: {name: 'BIO 110', color: colors.bio, count: 9},
  calc: {name: 'CALC II', color: colors.calc, count: 11},
  ml: {name: 'CS 340', color: colors.cs, count: 10},
  engr: {name: 'ENGR 120', color: colors.hist, count: 6},
  nurs: {name: 'NURS 210', color: colors.health, count: 8},
};
export type ClassKey = keyof typeof CLASSES;

export const sidebarRows = (lit: ClassKey, pulse?: number): SidebarRow[] =>
  (Object.keys(CLASSES) as ClassKey[]).map((k) => ({...CLASSES[k], lit: k === lit, pulse: k === lit ? pulse : undefined}));

/**
 * Where the note window goes and how big: on a landscape frame the library window with its sidebar (the list column
 * tucked away) under the headline; on a phone-shaped frame just the page, 1.2 times as big, as the first video does.
 * `page` is where the page's content starts in the window, and its width.
 */
export const useStage = (opts: {list?: boolean; vh?: number} = {}) => {
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const z = vertical ? 1.2 : 1;
  const w = vertical ? 784 : 1320;
  const h = vertical ? opts.vh ?? 1020 : 800; // on a phone, as tall as the figure needs
  const left = (width - w * z) / 2;
  const top = vertical ? 430 : 196;
  const sidebar = vertical ? 0 : 252;
  const list = vertical || !opts.list ? 0 : 290;
  const pad = vertical ? 40 : 48;
  const page = {x: sidebar + list + pad, w: w - sidebar - list - pad * 2};
  /** A point in the window, on the screen. */
  const at = (x: number, y: number) => ({x: left + x * z, y: top + y * z});
  return {vertical, z, w, h, left, top, sidebar, list, pad, page, at};
};

/** The library window at the stage's place: sidebar (landscape), an optional list, and the page. */
export const StageWindow: React.FC<{
  stage: ReturnType<typeof useStage>;
  lit: ClassKey;
  pulse?: number;
  list?: React.ReactNode;
  enter?: number;
  children: React.ReactNode;
}> = ({stage, lit, pulse, list, enter = 1, children}) => (
  // Scaled with a transform, not CSS zoom: under zoom, a scrolled page inside the window wasn't clipped to it.
  <div
    style={{
      position: 'absolute',
      left: stage.left,
      top: stage.top,
      opacity: enter,
      transformOrigin: '0 0',
      // Settles in from 98% of its size, about its top centre.
      transform: `translate(${(stage.w * stage.z * (1 - (0.98 + 0.02 * enter))) / 2}px, ${(1 - enter) * 30}px) scale(${stage.z * (0.98 + 0.02 * enter)})`,
    }}
  >
    <Win w={stage.w} h={stage.h} style={{display: 'flex'}}>
      {stage.vertical ? null : <Sidebar rows={sidebarRows(lit, pulse)} />}
      {stage.list ? (
        <div style={{width: stage.list, borderRight: `1px solid ${colors.line}`, padding: '0 12px', flexShrink: 0}}>
          <div style={{height: 28}} />
          {list}
        </div>
      ) : null}
      <div style={{flex: 1, position: 'relative', minWidth: 0, overflow: 'hidden'}}>
        {stage.vertical ? <TrafficLights style={{padding: '22px 24px', position: 'absolute', left: 0, top: 0}} /> : null}
        {children}
      </div>
    </Win>
  </div>
);
