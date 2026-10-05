import React from 'react';
import {useVideoConfig} from 'remotion';
import {colors, fonts} from '../config';
import {COLUMN, useVertical} from '../components/Layout';
import {px} from '../promo2/scenes/Bookends';
import {ui} from '../promo2/ui';
import {G, Glyph} from './chrome';

// The Settings window as the app draws it on a Mac set up as "Just this Mac" ("mac-settings-one-computer",
// "mac-13-ai-engines-app", "mac-settings-recording-after", "mac-14-ai-tool-access-app", dark): the sections of This Mac
// and Your library down the side, a page on the right with its title, its lede and grouped rows. Drawn at a Mac's own
// sizes and shown a quarter larger (landscape) or 1.18 times (on a phone-shaped frame, where it's taller and nothing
// needs to scroll).

export const SW = 900;
export const SIDE = 212;
export const CX = 260; // where a page's content starts
export const CW = 600; // and its width
const line = 'rgba(255,255,255,0.09)';

export type Section =
  | 'General' | 'Appearance' | 'Shortcuts' | 'Recording' | 'Calendars' | 'Connection'
  | 'Library' | 'Classes' | 'Notes and sorting' | 'AI engines' | 'AI tool access' | 'Canvas' | 'Folders' | 'Phone';

const THIS_MAC: [Section, Glyph][] = [
  ['General', 'tune'],
  ['Appearance', 'palette'],
  ['Shortcuts', 'keyboard'],
  ['Recording', 'mic'],
  ['Calendars', 'calendar'],
  ['Connection', 'link'],
];
const YOUR_LIBRARY: [Section, Glyph][] = [
  ['Library', 'library'],
  ['Classes', 'book'],
  ['Notes and sorting', 'sort'],
  ['AI engines', 'sparkle'],
  ['AI tool access', 'hub'],
  ['Canvas', 'school'],
  ['Folders', 'folder'],
  ['Phone', 'phone'],
];

/** Where a section's row is in the sidebar (its centre, in the window's points). */
export const sideRow = (s: Section) => {
  const a = THIS_MAC.findIndex(([n]) => n === s);
  if (a >= 0) return {x: 100, y: 101 + a * 34};
  const b = YOUR_LIBRARY.findIndex(([n]) => n === s);
  return {x: 100, y: 340 + b * 34};
};

const Side: React.FC<{on: Section; h: number; hot?: Section | null}> = ({on, h, hot}) => (
  <div style={{position: 'absolute', left: 8, top: 53, width: SIDE, height: h - 53 - 9, borderRadius: 16, background: 'rgba(255,255,255,0.035)', border: `1px solid ${line}`, boxSizing: 'border-box'}}>
    {[['This Mac', THIS_MAC, 19], ['Your library', YOUR_LIBRARY, 258]].map(([head, items, top]) => (
      <div key={head as string} style={{position: 'absolute', left: 10, right: 10, top: top as number}}>
        <div style={{fontSize: 11.5, fontWeight: 600, color: colors.text3, padding: '0 10px 8px'}}>{head as string}</div>
        {(items as [Section, Glyph][]).map(([name, glyph]) => (
          <div key={name} style={{display: 'flex', alignItems: 'center', gap: 10, height: 32, marginBottom: 2, padding: '0 10px', borderRadius: 9, background: name === on ? 'rgba(255,255,255,0.10)' : name === hot ? 'rgba(255,255,255,0.05)' : 'transparent', fontSize: 13.5, fontWeight: name === on ? 700 : 500}}>
            <G name={glyph} size={15} color={name === on ? colors.lagoonBright : colors.text2} width={1.8} />
            {name}
          </div>
        ))}
      </div>
    ))}
  </div>
);

/** Settings: the window, its sidebar, and the page (scrolled up by `scroll` points). */
export const SettingsWindow: React.FC<{on: Section; h: number; scroll?: number; hot?: Section | null; children: React.ReactNode}> = ({on, h, scroll = 0, hot, children}) => (
  <div style={{width: SW, height: h, position: 'relative', borderRadius: 18, background: colors.window, border: `1px solid ${colors.edge}`, boxShadow: '0 0 0 0.5px rgba(0,0,0,0.6), 0 40px 90px -24px rgba(0,0,20,0.7)', overflow: 'hidden', fontFamily: fonts.ui, color: colors.text}}>
    <div style={{position: 'absolute', left: CX - 8, top: 46, right: 0, bottom: 0, overflow: 'hidden'}}>
      <div style={{position: 'absolute', left: 8, top: -46 - scroll, width: CW + 20}}>{children}</div>
    </div>
    {/* The title bar: what scrolls goes under it. */}
    <div style={{position: 'absolute', left: 0, right: 0, top: 0, height: 46, background: colors.window}} />
    <div style={{position: 'absolute', left: 20, top: 20, display: 'flex', gap: 8}}>
      {['#FF5F57', '#4A4F55', '#4A4F55'].map((c, i) => (
        <div key={i} style={{width: 12, height: 12, borderRadius: 6, background: c}} />
      ))}
    </div>
    <div style={{position: 'absolute', left: 0, right: 0, top: 18, textAlign: 'center', fontSize: 13, fontWeight: 700, color: '#B9C0C6'}}>Settings</div>
    <Side on={on} h={h} hot={hot} />
  </div>
);

/** Where the window sits and how big: under the headline (landscape), or larger on a phone-shaped frame. */
export const useSettingsStage = () => {
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const z = vertical ? 1.18 : 1.25;
  const h = vertical ? 1100 : 690;
  const left = vertical ? Math.round((width - SW * z) / 2) : COLUMN;
  const top = vertical ? 420 : 200;
  /** A point of the window (its own points) on the screen. */
  const at = (x: number, y: number) => ({x: left + x * z, y: top + y * z});
  return {vertical, z, h, left, top, at};
};

export const PageTitle: React.FC<{title: string; lede: string; right?: React.ReactNode}> = ({title, lede, right}) => (
  <div style={{paddingTop: 62}}>
    <div style={{display: 'flex', alignItems: 'center'}}>
      <div style={{flex: 1, fontSize: 26, fontWeight: 700, letterSpacing: '-0.015em'}}>{title}</div>
      {right}
    </div>
    <div style={{fontSize: 13.5, lineHeight: 1.5, color: colors.text2, marginTop: 8, width: 530}}>{lede}</div>
  </div>
);

export const Head: React.FC<{children: React.ReactNode; style?: React.CSSProperties}> = ({children, style}) => (
  <div style={{fontSize: 13.5, fontWeight: 700, padding: '0 4px', margin: '22px 0 8px', ...style}}>{children}</div>
);

export const GroupBox: React.FC<{children: React.ReactNode; style?: React.CSSProperties}> = ({children, style}) => (
  <div style={{width: CW, borderRadius: 12, background: 'rgba(255,255,255,0.05)', overflow: 'hidden', ...style}}>{children}</div>
);

export const Sep: React.FC = () => <div style={{height: 1, background: line, margin: '0 16px'}} />;

export const RowText: React.FC<{title: string; sub?: React.ReactNode}> = ({title, sub}) => (
  <div style={{flex: 1, minWidth: 0, paddingRight: 12}}>
    <div style={{fontSize: 13.5, fontWeight: 500}}>{title}</div>
    {sub ? <div style={{fontSize: 11.5, lineHeight: 1.4, color: colors.text2, marginTop: 2}}>{sub}</div> : null}
  </div>
);

export const SetRow: React.FC<{children: React.ReactNode; h?: number}> = ({children, h = 57}) => (
  <div style={{display: 'flex', alignItems: 'center', minHeight: h, padding: '8px 16px', boxSizing: 'border-box'}}>{children}</div>
);

/** A switch, `on` from 0 to 1. */
export const Switch: React.FC<{on: number; press?: number}> = ({on, press = 0}) => (
  <div style={{width: 38, height: 22, borderRadius: 11, background: on > 0.5 ? `rgba(14,149,148,${px(0.4 + 0.6 * on)})` : `rgba(255,255,255,${px(0.16 - 0.04 * on)})`, position: 'relative', flexShrink: 0, transform: `scale(${px(1 - 0.05 * press)})`}}>
    <div style={{position: 'absolute', top: 2, left: px(2 + 16 * on), width: 18, height: 18, borderRadius: 9, background: '#FFFFFF', boxShadow: '0 1px 3px rgba(0,0,0,0.35)'}} />
  </div>
);

/** A menu's button (a select): what's chosen, and the up-down chevrons. */
export const Select: React.FC<{value: string; open?: boolean; width?: number}> = ({value, open, width = 180}) => (
  <div style={{width, height: 28, borderRadius: 14, background: open ? 'rgba(255,255,255,0.14)' : 'rgba(255,255,255,0.08)', border: '1px solid rgba(255,255,255,0.10)', boxSizing: 'border-box', display: 'flex', alignItems: 'center', padding: '0 8px 0 12px', fontSize: 13, flexShrink: 0}}>
    <div style={{flex: 1}}>{value}</div>
    <G name="updown" size={14} color={colors.text2} />
  </div>
);

/** One of the app's choice cards (a transcription model, when it's written down): the chosen one wears the accent. */
export const ChoiceCard: React.FC<{title: string; tag?: string; body: string; chosen?: number; check?: boolean; style?: React.CSSProperties}> = ({title, tag, body, chosen = 0, check, style}) => (
  <div
    style={{
      width: 478,
      boxSizing: 'border-box',
      padding: '11px 15px 12px',
      borderRadius: 12,
      background: 'rgba(255,255,255,0.05)',
      border: '1px solid rgba(255,255,255,0.07)',
      boxShadow: chosen > 0 ? `0 0 0 ${px(2 * chosen)}px ${ui.accent}` : 'none',
      marginBottom: 10,
      position: 'relative',
      ...style,
    }}
  >
    <div style={{display: 'flex', alignItems: 'center', gap: 8}}>
      <div style={{fontSize: 13, fontWeight: 700}}>{title}</div>
      {tag ? <div style={{fontSize: 10.5, fontWeight: 700, color: colors.lagoonBright, background: 'rgba(43,183,178,0.18)', padding: '1px 6px', borderRadius: 5}}>{tag}</div> : null}
    </div>
    <div style={{fontSize: 12, lineHeight: 1.4, color: colors.text2, marginTop: 3, paddingRight: check ? 26 : 0}}>{body}</div>
    {check ? (
      <div style={{position: 'absolute', right: 14, top: '50%', marginTop: -8, width: 16, height: 16, borderRadius: 8, background: ui.accent, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <G name="check" size={11} color="#0B1A1C" width={3} />
      </div>
    ) : null}
  </div>
);
