import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../../config';
import {ease, spr} from '../../anim';
import {Desktop, Title, useVertical} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';
import {ui} from '../ui';

// More kinds than flowcharts, each drawn as the app draws it in dark mode (the "new kinds" screenshot test): a state
// diagram whose accepting state is a double circle, a sequence diagram of logging in, a timeline, a mind map. Each
// plays the way the app steps through it: the automaton reads "01", the messages go one at a time, the events and
// branches arrive in order.

const T = {fontFamily: fonts.ui, fill: ui.boxText};
const clamp = (x: number) => Math.max(0, Math.min(1, x));
const ARROW = (id: string, color: string) => (
  <marker id={id} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0 0.8L10 5L0 9.2z" fill={color} />
  </marker>
);

const Automaton: React.FC<{f: number}> = ({f}) => {
  const build = ease(f, 0, 16);
  // Reading "0" then "1": q0 → q1 → q2, which accepts.
  const s1 = ease(f, 22, 30);
  const s2 = ease(f, 40, 48);
  const accept = spr(f, 50, {damping: 12, stiffness: 160, mass: 0.7});
  const at = f < 22 ? 'q0' : f < 40 ? 'q1' : 'q2';
  const node = (id: string, x: number, y: number, double = false) => {
    const on = at === id;
    return (
      <g key={id} opacity={build}>
        {on ? <circle cx={x} cy={y} r={double ? 44 : 39} fill="none" stroke={ui.accentGlow} strokeWidth={6} /> : null}
        <circle cx={x} cy={y} r={34} fill={ui.box} stroke={on ? ui.accent : ui.boxEdge} strokeWidth={on ? 2.2 : 1.2} />
        {double ? <circle cx={x} cy={y} r={39} fill="none" stroke={on ? ui.accent : ui.boxEdge} strokeWidth={on ? 2.2 : 1.2} /> : null}
        <text x={x} y={y + 6} textAnchor="middle" {...T} fontWeight={700} fontSize="17">{id}</text>
      </g>
    );
  };
  const edge = (d: string, label: string, lx: number, ly: number, lit = 0) => (
    <g opacity={build}>
      <path d={d} fill="none" stroke={lit > 0.5 ? ui.accent : ui.arrow} strokeWidth={lit > 0.5 ? 2.4 : 1.5} markerEnd={lit > 0.5 ? 'url(#k-on)' : 'url(#k-head)'} />
      <text x={lx} y={ly} textAnchor="middle" {...T} fill={lit > 0.5 ? ui.accent : ui.boxSub} fontSize="15">{label}</text>
    </g>
  );
  return (
    <svg viewBox="0 0 600 300" width="100%" height="100%">
      <defs>{ARROW('k-head', ui.arrow)}{ARROW('k-on', ui.accent)}</defs>
      <g opacity={build}>
        <circle cx={34} cy={215} r={9} fill="#1E5F5E" stroke={ui.accent} strokeWidth={1.5} />
        <path d="M44 215 L110 215" stroke={ui.arrow} strokeWidth={1.5} markerEnd="url(#k-head)" />
      </g>
      {edge('M150 181 C 136 140, 176 140, 162 181', '1', 156, 134)}
      {edge('M177 195 L 302 101', '0', 228, 136, s1 * (1 - s2))}
      {edge('M318 46 C 304 4, 344 4, 332 46', '0', 358, 26)}
      {edge('M356 105 L 470 186', '1', 425, 132, s2)}
      {edge('M486 180 C 470 120, 420 90, 364 84', '0', 466, 112)}
      {edge('M470 220 L 190 220', '1', 330, 244)}
      {node('q0', 150, 215)}
      {node('q1', 330, 80)}
      {node('q2', 510, 215, true)}
      <text x={510} y={290} textAnchor="middle" {...T} fill={ui.accent} fontWeight={600} fontSize="14" opacity={accept}>
        accepts “…01”
      </text>
    </svg>
  );
};

const Sequence: React.FC<{f: number}> = ({f}) => {
  const build = ease(f, 0, 14);
  const X = {Browser: 110, Server: 300, Database: 490};
  const msgs: [from: keyof typeof X, to: keyof typeof X, label: string, dashed: boolean][] = [
    ['Browser', 'Server', 'POST /login', false],
    ['Server', 'Database', 'look up the user', false],
    ['Database', 'Server', 'password hash', true],
    ['Server', 'Browser', '200 OK + session cookie', true],
  ];
  const step = Math.floor((f - 14) / 13);
  return (
    <svg viewBox="0 0 600 330" width="100%" height="100%">
      <defs>{ARROW('s-head', ui.arrow)}{ARROW('s-on', ui.accent)}</defs>
      {(Object.keys(X) as (keyof typeof X)[]).map((k) => (
        <g key={k} opacity={build}>
          <line x1={X[k]} y1={58} x2={X[k]} y2={322} stroke={ui.arrow} strokeWidth={1.3} strokeDasharray="5 4" opacity={0.7} />
          <rect x={X[k] - 62} y={16} width={124} height={42} rx={8} fill={ui.box} stroke={ui.boxEdge} />
          <text x={X[k]} y={43} textAnchor="middle" {...T} fontWeight={700} fontSize="15">{k}</text>
        </g>
      ))}
      {msgs.map(([a, b, label, dashed], i) => {
        const p = ease(f, 14 + i * 13, 22 + i * 13);
        const on = step === i;
        const y = 104 + i * 58;
        const x2 = X[a] + (X[b] - X[a]) * p;
        return p > 0 ? (
          <g key={label}>
            <line x1={X[a]} y1={y} x2={x2 - Math.sign(X[b] - X[a]) * 3} y2={y} stroke={on ? ui.accent : ui.arrow} strokeWidth={on ? 2.4 : 1.5} strokeDasharray={dashed ? '6 4' : undefined} markerEnd={on ? 'url(#s-on)' : 'url(#s-head)'} />
            <text x={(X[a] + X[b]) / 2} y={y - 10} textAnchor="middle" {...T} fill={on ? ui.accent : ui.boxSub} fontSize="14" opacity={clamp(p * 2 - 0.6)}>
              {label}
            </text>
          </g>
        ) : null;
      })}
    </svg>
  );
};

const Timeline: React.FC<{f: number}> = ({f}) => {
  const axis = ease(f, 0, 18);
  const events: [string, string[]][] = [
    ['1847', ['Semmelweis has', 'doctors wash', 'their hands']],
    ['1857', ['Pasteur shows', 'microbes cause', 'fermentation']],
    ['1867', ['Lister sterilises', 'wounds with', 'carbolic acid']],
    ['1882', ['Koch identifies', 'the TB', 'bacterium']],
  ];
  return (
    <svg viewBox="0 0 640 260" width="100%" height="100%">
      <defs>{ARROW('t-head', ui.arrow)}</defs>
      <path d={`M60 60 L${60 + 540 * axis} 60`} stroke={ui.arrow} strokeWidth={1.6} markerEnd={axis > 0.95 ? 'url(#t-head)' : undefined} />
      {events.map(([year, lines], i) => {
        const x = 92 + i * 152;
        const p = spr(f, 8 + i * 9, {damping: 14, stiffness: 180, mass: 0.7});
        return (
          <g key={year} opacity={clamp(p * 1.5)} transform={`translate(${x} 60) scale(${0.7 + 0.3 * p}) translate(${-x} -60)`}>
            <rect x={x - 42} y={42} width={84} height={36} rx={18} fill="#24403F" stroke="#4E9E97" />
            <text x={x} y={66} textAnchor="middle" {...T} fontWeight={700} fontSize="15">{year}</text>
            <line x1={x} y1={78} x2={x} y2={100} stroke={ui.arrow} strokeWidth={1.3} />
            <rect x={x - 68} y={100} width={136} height={80} rx={9} fill={ui.box} stroke={ui.boxEdge} />
            {lines.map((l, j) => (
              <text key={l} x={x} y={124 + j * 20} textAnchor="middle" {...T} fontWeight={600} fontSize="14">{l}</text>
            ))}
          </g>
        );
      })}
    </svg>
  );
};

const MindMap: React.FC<{f: number}> = ({f}) => {
  const hub = spr(f, 0, {damping: 14, stiffness: 160, mass: 0.7});
  const branches: [string, string, string, string][] = [
    ['Epithelial', 'Covers surfaces', '#4C7BE0', '#1A2848'],
    ['Connective', 'Supports and binds', '#3FAE6A', '#163824'],
    ['Muscle', 'Contracts', '#D9A030', '#3A2A0C'],
    ['Nervous', 'Signals', '#9A7BE6', '#2B2346'],
  ];
  return (
    <svg viewBox="0 0 640 320" width="100%" height="100%">
      {branches.map(([name, leaf, edge, fill], i) => {
        const y = 40 + i * 80;
        const p = ease(f, 8 + i * 6, 22 + i * 6);
        const q = ease(f, 18 + i * 6, 30 + i * 6);
        return (
          <g key={name}>
            <path d={`M170 160 C 230 160, 220 ${y}, 290 ${y}`} fill="none" stroke="#C9D1D6" strokeWidth={2} pathLength={1} strokeDasharray={1} strokeDashoffset={1 - p} />
            <g opacity={clamp(p * 2 - 0.8)}>
              <rect x={290} y={y - 17} width={124} height={34} rx={17} fill={fill} stroke={edge} strokeWidth={1.5} />
              <text x={352} y={y + 5} textAnchor="middle" {...T} fontWeight={700} fontSize="14.5">{name}</text>
            </g>
            <path d={`M414 ${y} C 440 ${y}, 436 ${y}, 462 ${y}`} fill="none" stroke="#C9D1D6" strokeWidth={1.6} opacity={q} />
            <g opacity={q}>
              <rect x={462} y={y - 16} width={164} height={32} rx={10} fill={ui.box} stroke={ui.boxEdge} />
              <text x={544} y={y + 5} textAnchor="middle" {...T} fontWeight={600} fontSize="13.5">{leaf}</text>
            </g>
          </g>
        );
      })}
      <g transform={`translate(110 160) scale(${hub})`}>
        <circle r={60} fill="#24403F" stroke="#4E9E97" strokeWidth={1.5} />
        <text y={6} textAnchor="middle" {...T} fontWeight={700} fontSize="16">Tissue types</text>
      </g>
    </svg>
  );
};

const CARDS: {title: string; kind: string; Draw: React.FC<{f: number}>}[] = [
  {title: 'Strings ending in 01', kind: 'State diagram', Draw: Automaton},
  {title: 'Logging in', kind: 'Sequence diagram', Draw: Sequence},
  {title: 'Germ theory', kind: 'Timeline', Draw: Timeline},
  {title: 'Tissue types', kind: 'Mind map', Draw: MindMap},
];
export const KIND_AT = [18, 45, 72, 99]; // on the beat and the off-beat, as the narration names each // each card lands as the narration names it

export const Kinds: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  // Landscape: two by two where the window would be. Phone: one above the other.
  const gap = vertical ? 18 : 20;
  const area = vertical ? {x: 72, y: 400, w: width - 144, h: 1300} : {x: 300, y: 196, w: 1320, h: 800};
  const cols = vertical ? 1 : 2;
  const rows = vertical ? 4 : 2;
  const cw = (area.w - gap * (cols - 1)) / cols;
  const ch = (area.h - gap * (rows - 1)) / rows;
  return (
    <AbsoluteFill>
      <Desktop clock="Tue 23 Sep  11:19">
        {CARDS.map(({title, kind, Draw}, i) => {
          const at = KIND_AT[i];
          const p = spr(frame, at, {damping: 20, stiffness: 150, mass: 0.8});
          const c = i % cols;
          const r = Math.floor(i / cols);
          return (
            <div
              key={title}
              style={{
                position: 'absolute',
                left: area.x + c * (cw + gap),
                top: area.y + r * (ch + gap),
                width: cw,
                height: ch,
                borderRadius: 20,
                background: colors.window,
                border: `1px solid ${colors.edge}`,
                boxShadow: '0 0 0 0.5px rgba(0,0,0,0.6), 0 30px 70px -24px rgba(0, 0, 20, 0.7)',
                fontFamily: fonts.ui,
                color: colors.text,
                padding: vertical ? '18px 24px 14px' : '22px 28px 18px',
                boxSizing: 'border-box',
                display: 'flex',
                flexDirection: 'column',
                opacity: Math.min(1, p * 1.5),
                transform: `translateY(${(1 - p) * 40}px) scale(${0.94 + 0.06 * p})`,
              }}
            >
              <div style={{display: 'flex', alignItems: 'center', gap: 12}}>
                <div style={{fontFamily: fonts.display, fontSize: vertical ? 25 : 23, fontWeight: 600, letterSpacing: '-0.01em'}}>{title}</div>
                <div style={{fontSize: vertical ? 16 : 14.5, fontWeight: 600, color: colors.lagoonBright, padding: '3px 10px', borderRadius: 999, background: 'rgba(52,193,189,0.12)'}}>{kind}</div>
              </div>
              <div style={{flex: 1, minHeight: 0, marginTop: 8}}>
                <Draw f={frame - at - 4} />
              </div>
            </div>
          );
        })}
      </Desktop>
      {KIND_AT.map((f) => (
        <Sfx key={f} at={f + 2} name="pop" volume={0.22} />
      ))}
      <Sfx at={KIND_AT[0] + 54} name="ding" volume={0.18} />
      <Title text={text2.kinds as [string, string]} delay={4} width={vertical ? undefined : 1580} />
    </AbsoluteFill>
  );
};

