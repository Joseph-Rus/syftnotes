import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {fonts} from '../../config';
import {Desktop, Title, useVertical} from '../../components/Layout';
import {inOut, prog, px} from '../../promo2/scenes/Bookends';
import {Flow} from '../../promo2/Flow';
import {DrawnScale} from '../../promo2/DrawnScale';
import {Cut, cueIn, titles} from '../config';
import {G, Glyph} from '../chrome';

// The library on a phone: Study Stash's own web app (web/src: TabBar, Home, LectureScreen, Due, Ask), in its dark
// Lagoon tokens (theme/themes.ts darkTokens), inside a phone. The Library tab; a tap on a lecture opens its notes (with
// its diagram, which the phone draws too); a tap on Due; a tap on Ask.

// The phone app's dark tokens for the Lagoon theme, as darkTokens() works them out.
const T = {
  ground: 'oklch(0.16 0.018 210)',
  raised: 'oklch(0.255 0.018 210)',
  glass: 'rgba(34,34,38,0.86)',
  fg: '#f5f5f7',
  fg2: 'rgba(255,255,255,0.62)',
  fg3: 'rgba(255,255,255,0.34)',
  sep: 'rgba(255,255,255,0.09)',
  accent: 'oklch(0.66 0.12 195)',
  accentText: 'oklch(0.8 0.096 195)',
  accentTint: 'oklch(0.7 0.12 195 / 0.22)',
  red: '#ff453a',
  press: 'rgba(255,255,255,0.08)',
  shadow: '0 1px 2px rgba(0,0,0,0.3), 0 4px 16px rgba(0,0,0,0.25)',
};
// Each class's dot, from its place in the library (classPalette in themes.ts).
const DOT = {
  cs: 'oklch(0.62 0.14 250)',
  bio: 'oklch(0.64 0.14 155)',
  calc: 'oklch(0.6 0.14 295)',
  ml: 'oklch(0.66 0.12 200)',
};

const SW = 393; // the screen, in points
const SH = 852;
const font = fonts.ui;

const Dot: React.FC<{color: string; size?: number}> = ({color, size = 10}) => <span style={{width: size, height: size, borderRadius: size / 2, background: color, flexShrink: 0, display: 'inline-block'}} />;

const Section: React.FC<{title: string; red?: boolean; children: React.ReactNode}> = ({title, red, children}) => (
  <div style={{padding: '0 16px', marginBottom: 22}}>
    <div style={{margin: '0 0 6px 14px', fontSize: 13, fontWeight: 600, letterSpacing: '0.03em', textTransform: 'uppercase', color: red ? T.red : T.fg2}}>{title}</div>
    <div style={{background: T.raised, borderRadius: 14, overflow: 'hidden', boxShadow: T.shadow}}>{children}</div>
  </div>
);

const Row: React.FC<{lead?: React.ReactNode; title: string; sub?: string; detail?: React.ReactNode; first?: boolean; bar?: string; topics?: string; pressed?: number; accentTitle?: boolean}> = ({lead, title, sub, detail, first, bar, topics, pressed = 0, accentTitle}) => (
  <div style={{position: 'relative', display: 'flex', alignItems: 'center', gap: 12, minHeight: 48, padding: bar ? '11px 14px 11px 18px' : '11px 14px', background: pressed > 0 ? `rgba(255,255,255,${px(0.08 * pressed)})` : 'transparent', boxSizing: 'border-box'}}>
    {first ? null : <div style={{position: 'absolute', top: 0, left: 14, right: 0, height: 0.5, background: T.sep}} />}
    {bar ? <div style={{position: 'absolute', left: 0, top: 10, bottom: 10, width: 4, borderRadius: '0 3px 3px 0', background: bar}} /> : null}
    {lead}
    <div style={{flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2}}>
      <div style={{fontSize: 16, fontWeight: accentTitle ? 600 : 500, color: accentTitle ? T.accentText : T.fg, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{title}</div>
      {sub ? <div style={{fontSize: 13, color: T.fg2, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{sub}</div> : null}
      {topics ? <div style={{fontSize: 12, color: T.fg3, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{topics}</div> : null}
    </div>
    {detail}
    <G name="chevron" size={16} color={T.fg3} />
  </div>
);

const State: React.FC<{children: React.ReactNode; missing?: boolean}> = ({children, missing}) => (
  <span style={{fontSize: 13, color: missing ? T.red : T.fg2, fontWeight: missing ? 600 : 400, whiteSpace: 'nowrap'}}>{children}</span>
);

const LargeTitle: React.FC<{title: string; sub: string}> = ({title, sub}) => (
  <div style={{padding: '4px 16px 10px'}}>
    <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, lineHeight: 1.15, letterSpacing: '-0.02em'}}>{title}</div>
    <div style={{marginTop: 4, color: T.fg2, fontSize: 15}}>{sub}</div>
  </div>
);

const NavBar: React.FC<{left?: React.ReactNode; right?: React.ReactNode; title?: string}> = ({left, right, title}) => (
  <div style={{height: 44, display: 'flex', alignItems: 'center', padding: '0 10px', gap: 4}}>
    <div style={{flex: 1.4, display: 'flex'}}>{left}</div>
    <div style={{flex: 2, textAlign: 'center', fontSize: 17, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'}}>{title}</div>
    <div style={{flex: 1.4, display: 'flex', justifyContent: 'flex-end'}}>{right}</div>
  </div>
);

type Tab = 'library' | 'due' | 'ask' | 'search' | 'settings';
const TABS: [Tab, string, Glyph][] = [
  ['library', 'Library', 'books'],
  ['due', 'Due', 'checklist'],
  ['ask', 'Ask', 'sparkle'],
  ['search', 'Search', 'search'],
  ['settings', 'Settings', 'gear'],
];

const TabBar: React.FC<{on: Tab}> = ({on}) => (
  <div style={{position: 'absolute', left: 0, right: 0, bottom: 0, display: 'flex', paddingBottom: 30, background: T.glass, borderTop: `0.5px solid ${T.sep}`}}>
    {TABS.map(([t, label, glyph]) => (
      <div key={t} style={{flex: 1, height: 50, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 2, color: t === on ? T.accentText : T.fg3, fontSize: 11, fontWeight: 500}}>
        <div style={{position: 'relative'}}>
          <G name={glyph} size={24} color={t === on ? T.accentText : T.fg3} />
          {t === 'due' ? <div style={{position: 'absolute', top: -4, left: 14, minWidth: 17, height: 17, padding: '0 5px', boxSizing: 'border-box', borderRadius: 9, background: T.red, color: '#fff', fontSize: 11, fontWeight: 600, lineHeight: '17px', textAlign: 'center'}}>1</div> : null}
        </div>
        {label}
      </div>
    ))}
  </div>
);

// ---- The four screens ----------------------------------------------------------------------------------------------

const Library: React.FC<{press: number}> = ({press}) => (
  <>
    <NavBar right={<G name="plus" size={26} color={T.accentText} width={1.7} />} />
    <LargeTitle title="Library" sub="Sam’s library" />
    <Section title="Due soon">
      <Row first lead={<Dot color={DOT.bio} />} title="Lab 2 write-up" sub="BIO 110" detail={<State missing>Mon 22 Sep</State>} />
      <Row lead={<Dot color={DOT.calc} />} title="Quiz 3 practice" sub="CALC II" detail={<State>Tomorrow, 9:00 AM</State>} />
      <Row lead={<Dot color={DOT.cs} />} title="Lab 3: recursion traces" sub="CS 101" detail={<State>Tue 30 Sep</State>} />
      <Row title="All 3 to hand in" accentTitle />
    </Section>
    <Section title="Recent lectures">
      <Row first bar={DOT.bio} title="The cardiac cycle" sub="BIO 110 · Tue 23 Sep · 50 min" topics="Cardiac cycle · Heart sounds · Heart valves" pressed={press} />
      <Row bar={DOT.cs} title="Recursion and the call stack" sub="CS 101 · Mon 22 Sep · 50 min" topics="Recursion · Call stack · Base case" />
      <Row bar={DOT.calc} title="Series convergence tests" sub="CALC II · Mon 22 Sep · 50 min" topics="Ratio test · Comparison test" />
      <Row bar={DOT.ml} title="Logistic regression" sub="CS 340 · Mon 22 Sep · 50 min" topics="Sigmoid · Gradient descent" />
    </Section>
  </>
);

const Lecture: React.FC = () => (
  <>
    <NavBar
      left={
        <span style={{display: 'flex', alignItems: 'center', gap: 1, color: T.accentText, fontSize: 17}}>
          <span style={{transform: 'scaleX(-1)', display: 'flex'}}>
            <G name="chevron" size={24} color={T.accentText} width={2.2} />
          </span>
          BIO 110
        </span>
      }
      right={<G name="paperclip" size={22} color={T.accentText} />}
    />
    <div style={{padding: '6px 16px 14px'}}>
      <div style={{display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 600, color: T.fg2, marginBottom: 6}}>
        <Dot color={DOT.bio} size={8} /> BIO 110
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 26, fontWeight: 700, lineHeight: 1.2, letterSpacing: '-0.015em'}}>The cardiac cycle</div>
      <div style={{marginTop: 6, color: T.fg2, fontSize: 15}}>Tue 23 Sep · 50 min</div>
      <div style={{display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 12}}>
        {['Cardiac cycle', 'Heart sounds', 'Heart valves'].map((t) => (
          <span key={t} style={{padding: '5px 11px', borderRadius: 999, background: T.accentTint, color: T.fg3, fontSize: 12, fontWeight: 500, fontFamily: 'Menlo, monospace'}}>
            {t}
          </span>
        ))}
      </div>
    </div>
    <div style={{padding: '0 16px', fontFamily: fonts.serif, fontSize: 17, lineHeight: 1.5, color: T.fg}}>
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 700, margin: '4px 0 6px'}}>Summary</div>
      <div>One heartbeat is one cycle: the atria fill and squeeze, the ventricles contract and eject, then relax and refill.</div>
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 700, margin: '16px 0 10px'}}>The cycle, step by step</div>
      <DrawnScale by={0.56}>
        <div style={{transform: 'scale(0.56)', transformOrigin: '0 0', height: 452 * 0.56, width: 640 * 0.56}}>
          <Flow look={{build: 1}} scale={1} />
        </div>
      </DrawnScale>
    </div>
  </>
);

const Due: React.FC = () => (
  <>
    <NavBar />
    <LargeTitle title="Due" sub="3 to hand in · synced 2 min ago" />
    <div style={{padding: '0 16px', marginBottom: 22}}>
      <div style={{position: 'relative', display: 'flex', flexDirection: 'column', gap: 4, padding: '16px 40px 16px 16px', borderRadius: 18, background: T.accent, color: '#fff', boxShadow: T.shadow}}>
        <span style={{fontSize: 12, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.06em', opacity: 0.8}}>Next up</span>
        <span style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 700, lineHeight: 1.25, marginTop: 2}}>Quiz 3 practice</span>
        <span style={{display: 'flex', alignItems: 'center', gap: 6, fontSize: 14, opacity: 0.9}}>
          <span style={{width: 8, height: 8, borderRadius: 4, background: DOT.calc, boxShadow: '0 0 0 1.5px rgba(255,255,255,0.7)'}} /> CALC II
        </span>
        <span style={{fontSize: 14, marginTop: 4}}>
          <strong>Tomorrow, 9:00 AM</strong> · in 22 hours · 10 pts
        </span>
        <div style={{position: 'absolute', right: 14, top: '50%', transform: 'translateY(-50%)', opacity: 0.7}}>
          <G name="chevron" size={18} color="#fff" />
        </div>
      </div>
    </div>
    <Section title="Overdue" red>
      <Row first lead={<Dot color={DOT.bio} />} title="Lab 2 write-up" sub="BIO 110 · Mon 22 Sep, 11:59 PM" detail={<State missing>Missing</State>} />
    </Section>
    <Section title="This week">
      <Row first lead={<Dot color={DOT.calc} />} title="Quiz 3 practice" sub="CALC II · Tomorrow, 9:00 AM" detail={<State>To do</State>} />
      <Row lead={<Dot color={DOT.cs} />} title="Lab 3: recursion traces" sub="CS 101 · Tue 30 Sep, 11:59 PM" detail={<State>To do</State>} />
    </Section>
    <Section title="Handed in this week">
      <Row first title="Show 2 handed in" />
    </Section>
  </>
);

const Ask: React.FC = () => (
  <>
    <NavBar right={<G name="trash" size={20} color={T.accentText} />} />
    <LargeTitle title="Ask" sub="Answers from your own notes and lectures" />
    <div style={{padding: '4px 16px', display: 'flex', flexDirection: 'column', gap: 10}}>
      <div style={{alignSelf: 'flex-end', maxWidth: 290, padding: '10px 14px', borderRadius: 18, background: T.accent, color: '#fff', fontSize: 16, lineHeight: 1.35}}>What do the two heart sounds mean?</div>
      <div style={{alignSelf: 'flex-start', maxWidth: 330, padding: '12px 14px', borderRadius: 18, background: T.raised, fontSize: 16, lineHeight: 1.4}}>
        They’re valves closing. “Lub” is the AV valves shutting as the ventricles contract; “dub” is the semilunar valves shutting as they relax.
        <div style={{display: 'flex', alignItems: 'center', gap: 6, marginTop: 10, padding: '6px 10px', borderRadius: 10, background: 'rgba(255,255,255,0.06)', fontSize: 13}}>
          <Dot color={DOT.bio} size={7} />
          <span style={{fontWeight: 600}}>The cardiac cycle</span>
          <span style={{color: T.fg2}}>Tue 23 Sep · 48:31</span>
        </div>
      </div>
    </div>
    {/* The composer, above the tab bar. */}
    <div style={{position: 'absolute', left: 0, right: 0, bottom: 80, padding: '8px 16px 10px', background: T.glass, borderTop: `0.5px solid ${T.sep}`}}>
      <div style={{display: 'flex', gap: 6, overflow: 'hidden', marginBottom: 8}}>
        {[['All classes', null], ['BIO 110', DOT.bio], ['CS 101', DOT.cs], ['CALC II', DOT.calc]].map(([name, dot], i) => (
          <span key={name as string} style={{display: 'inline-flex', alignItems: 'center', gap: 5, padding: '6px 11px', borderRadius: 999, fontSize: 13, fontWeight: 500, whiteSpace: 'nowrap', background: i === 0 ? T.accentTint : 'rgba(255,255,255,0.08)', color: i === 0 ? T.accentText : T.fg}}>
            {dot ? <Dot color={dot as string} size={7} /> : null}
            {name}
          </span>
        ))}
      </div>
      <div style={{display: 'flex', alignItems: 'center', gap: 8}}>
        <div style={{flex: 1, height: 38, borderRadius: 19, background: 'rgba(255,255,255,0.08)', display: 'flex', alignItems: 'center', padding: '0 14px', fontSize: 16, color: T.fg3}}>Ask about your notes</div>
        <div style={{width: 34, height: 34, borderRadius: 17, background: 'rgba(255,255,255,0.12)', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
          <G name="up" size={18} color={T.fg3} />
        </div>
      </div>
    </div>
  </>
);

const StatusBar: React.FC = () => (
  <div style={{position: 'absolute', left: 0, right: 0, top: 0, height: 54, display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '4px 34px 0 46px', fontSize: 17, fontWeight: 600, zIndex: 5}}>
    <span>10:31</span>
    <span style={{display: 'flex', alignItems: 'center', gap: 6}}>
      <svg width="18" height="12" viewBox="0 0 18 12" fill="#fff">
        <rect x="0" y="8" width="3" height="4" rx="1" />
        <rect x="5" y="5.5" width="3" height="6.5" rx="1" />
        <rect x="10" y="3" width="3" height="9" rx="1" />
        <rect x="15" y="0" width="3" height="12" rx="1" />
      </svg>
      <svg width="17" height="12" viewBox="0 0 26 19" fill="none" stroke="#fff" strokeWidth="2.6" strokeLinecap="round">
        <path d="M13 17.6l3.1-3.7a4.8 4.8 0 0 0-6.2 0z" fill="#fff" stroke="none" />
        <path d="M6.9 10.8a9.2 9.2 0 0 1 12.2 0M3.3 6.6a14.6 14.6 0 0 1 19.4 0" />
      </svg>
      <svg width="27" height="13" viewBox="0 0 27 13">
        <rect x="0.5" y="0.5" width="23" height="12" rx="3.5" fill="none" stroke="rgba(255,255,255,0.45)" />
        <rect x="2" y="2" width="17" height="9" rx="2" fill="#fff" />
        <path d="M25 4.5v4" stroke="rgba(255,255,255,0.45)" strokeWidth="1.6" strokeLinecap="round" />
      </svg>
    </span>
  </div>
);

/** A touch on the glass: a soft ring where the finger landed, fading. */
const Touch: React.FC<{f: number; at: number; x: number; y: number}> = ({f, at, x, y}) => {
  const p = prog(f, at - 4, at);
  const out = prog(f, at + 2, at + 14);
  const o = p * (1 - out);
  if (o <= 0) return null;
  const r = 22 + 10 * out;
  return <div style={{position: 'absolute', left: x - r, top: y - r, width: r * 2, height: r * 2, borderRadius: r, background: `rgba(255,255,255,${px(0.28 * o)})`, border: `2px solid rgba(255,255,255,${px(0.5 * o)})`, zIndex: 20}} />;
};

// The reel's phone: under its headline and above Instagram's caption, centred in the safe area (clear of the buttons
// down the right).
export const REEL_PHONE = {top: 412, bottom: 1490, centre: 508};

export const Phone: React.FC<{cut?: Cut}> = ({cut}) => {
  const f = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const LECTURE = cueIn(cut, 'phone-lecture');
  const DUE = cueIn(cut, 'phone-due');
  const ASK = cut ? 100000 : cueIn(cut, 'phone-ask'); // the reel stays on Due

  const bezel = 13;
  const PW = SW + bezel * 2;
  const PH = SH + bezel * 2;
  const z = cut ? Math.floor(((REEL_PHONE.bottom - REEL_PHONE.top) / PH) * 1000) / 1000 : vertical ? 1.7 : 0.98;
  const left = cut ? Math.round(REEL_PHONE.centre - (PW * z) / 2) : Math.round((width - PW * z) / 2);
  const top = cut ? REEL_PHONE.top : vertical ? 330 : 200;
  const enter = prog(f, 0, cut ? 16 : 22, inOut);

  const push = prog(f, LECTURE + 2, LECTURE + 14, inOut);
  const screen = f < LECTURE + 2 ? 'library' : f < DUE ? 'lecture' : f < ASK ? 'due' : 'ask';
  const tab: Tab = screen === 'lecture' || screen === 'library' ? 'library' : (screen as Tab);
  const flash = (at: number) => prog(f, at, at + 4);
  const press = Math.max(0, 1 - Math.abs(f - LECTURE) / 4);

  // Where the taps land, in the screen's points.
  const lectureRow = {x: 150, y: 54 + 44 + 72 + 4 * 52 + 22 + 20 + 34};
  const tabAt = (i: number) => ({x: (SW / 5) * (i + 0.5), y: SH - 30 - 25});

  return (
    <AbsoluteFill>
      <Desktop clock="Thu 25 Sep  10:31">
        <AbsoluteFill style={{background: 'rgba(3,4,26,0.45)'}} />
        <div style={{position: 'absolute', left, top: top + px((1 - enter) * 60), opacity: enter, transform: `scale(${z})`, transformOrigin: '0 0'}}>
          <DrawnScale by={z}>
            <div style={{width: PW, height: PH, borderRadius: 64, background: '#0B0B0D', boxShadow: '0 0 0 2px #2E3036, 0 0 0 3px #15161A, 0 60px 120px -30px rgba(0,0,12,0.9)', position: 'relative'}}>
              <div style={{position: 'absolute', left: bezel, top: bezel, width: SW, height: SH, borderRadius: 52, overflow: 'hidden', background: T.ground, color: T.fg, fontFamily: font}}>
                <StatusBar />
                <div style={{position: 'absolute', left: (SW - 124) / 2, top: 11, width: 124, height: 36, borderRadius: 18, background: '#000', zIndex: 6}} />
                <div style={{position: 'absolute', inset: 0, paddingTop: 54}}>
                  {screen === 'library' || (screen === 'lecture' && push < 1) ? (
                    <div style={{position: 'absolute', inset: '54px 0 0 0', transform: `translateX(${px(-push * SW * 0.3)}px)`, opacity: 1 - 0.4 * push}}>
                      <Library press={press} />
                    </div>
                  ) : null}
                  {screen === 'lecture' ? (
                    <div style={{position: 'absolute', inset: '54px 0 0 0', background: T.ground, transform: `translateX(${px((1 - push) * SW)}px)`, boxShadow: '-10px 0 30px rgba(0,0,0,0.35)'}}>
                      <Lecture />
                    </div>
                  ) : null}
                  {screen === 'due' ? (
                    <div style={{position: 'absolute', inset: '54px 0 0 0', opacity: flash(DUE)}}>
                      <Due />
                    </div>
                  ) : null}
                  {screen === 'ask' ? (
                    <div style={{position: 'absolute', inset: '54px 0 0 0', opacity: flash(ASK)}}>
                      <Ask />
                    </div>
                  ) : null}
                </div>
                <TabBar on={tab} />
                <div style={{position: 'absolute', left: (SW - 134) / 2, bottom: 8, width: 134, height: 5, borderRadius: 3, background: 'rgba(255,255,255,0.7)', zIndex: 6}} />
                <Touch f={f} at={LECTURE} x={lectureRow.x} y={lectureRow.y} />
                <Touch f={f} at={DUE} x={tabAt(1).x} y={tabAt(1).y} />
                <Touch f={f} at={ASK} x={tabAt(2).x} y={tabAt(2).y} />
              </div>
            </div>
          </DrawnScale>
        </div>
      </Desktop>
      {cut ? null : <Title text={titles.phone!} delay={10} />}
    </AbsoluteFill>
  );
};
