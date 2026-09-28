import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, spr} from '../anim';
import {BELOW_TITLE, Title, ClassDot, Cursor, Desktop, useVertical} from '../components/Layout';
import {Group, LibraryWindow, ListHead, ListRow, Sidebar} from '../components/Sidebar';

const SWITCH = 88; // CALC II is clicked
const INK = colors.text;
const MUTED = colors.text2;

// Nursing: the nursing process (ADPIE), five steps round a ring, as the app lays out a cycle.
const STEPS = ['Assessment', 'Diagnosis', 'Planning', 'Implementation', 'Evaluation'];
const STEP_NOTES = ['Collect the data', 'Name the problems', 'Set the goals', 'Carry out the care', 'Check the outcomes'];
const CX = 300;
const CY = 230;
const RX = 200;
const RY = 172;
const at = (i: number) => {
  const a = -Math.PI / 2 + (i * 2 * Math.PI) / STEPS.length;
  return {x: CX + RX * Math.cos(a), y: CY + RY * Math.sin(a), a};
};
const box = (i: number) => {
  const k = i % STEPS.length;
  const {x, y} = at(k);
  return {x, y, w: 184, h: 62};
};
const inside = (x: number, y: number, b: ReturnType<typeof box>, margin = 12) => Math.abs(x - b.x) <= b.w / 2 + margin && Math.abs(y - b.y) <= b.h / 2 + margin;
const ARCS = STEPS.map((_, i) => {
  const from = box(i);
  const to = box(i + 1);
  const a0 = at(i).a;
  const all = Array.from({length: 241}, (_, k) => {
    const a = a0 + ((2 * Math.PI) / STEPS.length) * (k / 240);
    return [CX + RX * Math.cos(a), CY + RY * Math.sin(a)];
  });
  const pts = all.filter(([x, y]) => !inside(x, y, from) && !inside(x, y, to));
  const end = pts[pts.length - 1];
  const prev = pts[Math.max(0, pts.length - 4)];
  return {d: `M${pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' L')}`, end, angle: (Math.atan2(end[1] - prev[1], end[0] - prev[0]) * 180) / Math.PI};
});

const Cycle: React.FC<{frame: number}> = ({frame}) => (
  <svg width={600} height={470} viewBox="0 0 600 470" style={{display: 'block', margin: '6px auto 0'}}>
    {ARCS.map((arc, i) => {
      const p = ease(frame, 24 + i * 8, 38 + i * 8);
      return (
        <g key={i}>
          <path d={arc.d} fill="none" stroke="#7F8BA0" strokeWidth="2.5" pathLength={1} strokeDasharray="1" strokeDashoffset={1 - p} strokeLinecap="round" />
          <path d="M-10,-6.5 L0,0 L-10,6.5" transform={`translate(${arc.end[0]},${arc.end[1]}) rotate(${arc.angle})`} fill="none" stroke="#7F8BA0" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" opacity={p >= 0.98 ? 1 : 0} />
        </g>
      );
    })}
    {STEPS.map((s, i) => {
      const b = box(i);
      const p = spr(frame, 14 + i * 8, bouncy);
      const first = i === 0;
      return (
        <g key={s} transform={`translate(${b.x},${b.y}) scale(${p})`} opacity={Math.min(1, p * 2)}>
          <rect x={-b.w / 2} y={-b.h / 2} width={b.w} height={b.h} rx={first ? b.h / 2 : 12} fill={first ? 'rgba(52,193,189,0.16)' : '#172431'} stroke={first ? colors.lagoonBright : colors.edge} strokeWidth="1.8" />
          <text x={0} y={-3} textAnchor="middle" fontFamily={fonts.ui} fontSize="17.5" fontWeight={600} fill={INK}>
            {s}
          </text>
          <text x={0} y={18} textAnchor="middle" fontFamily={fonts.ui} fontSize="14" fontWeight={400} fill={MUTED}>
            {STEP_NOTES[i]}
          </text>
        </g>
      );
    })}
  </svg>
);

// Math: the derivative, typeset, and the secant closing in on the tangent as h goes to 0.
const Formula: React.FC<{frame: number}> = ({frame}) => {
  const part = (i: number): React.CSSProperties => {
    const p = spr(frame, 12 + i * 5, {damping: 24, stiffness: 140, mass: 1});
    return {opacity: p, transform: `translateY(${(1 - p) * 12}px)`};
  };
  const bar = ease(frame, 24, 38);
  return (
    <div style={{display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontFamily: fonts.serif, fontSize: 38, color: INK, marginTop: 18}}>
      <span style={{...part(0), fontStyle: 'italic'}}>f ′(x)</span>
      <span style={part(1)}>=</span>
      <span style={{...part(2), display: 'inline-flex', flexDirection: 'column', alignItems: 'center', lineHeight: 1}}>
        <span>lim</span>
        <span style={{fontSize: 21, fontStyle: 'italic', marginTop: 6}}>h→0</span>
      </span>
      <span style={{...part(3), display: 'inline-flex', flexDirection: 'column', alignItems: 'center'}}>
        <span style={{fontStyle: 'italic', padding: '0 6px 6px'}}>f(x + h) − f(x)</span>
        <span style={{height: 2, alignSelf: 'stretch', background: INK, transform: `scaleX(${bar})`}} />
        <span style={{fontStyle: 'italic', paddingTop: 4}}>h</span>
      </span>
    </div>
  );
};

const GX0 = -0.6;
const GX1 = 2.3;
const GY0 = -0.4;
const GY1 = 4.6;
const GW = 520;
const GH = 300;
const px = (x: number) => ((x - GX0) / (GX1 - GX0)) * GW;
const py = (y: number) => GH - ((y - GY0) / (GY1 - GY0)) * GH;

const Graph: React.FC<{frame: number}> = ({frame}) => {
  const draw = ease(frame, 30, 58);
  const secant = ease(frame, 58, 66);
  const h = interpolate(frame, [64, 90], [1.1, 0.02], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const slope = 2 + h;
  const line = (x: number) => 1 + slope * (x - 1);
  const curve = Array.from({length: 61}, (_, k) => {
    const x = GX0 + ((GX1 - GX0) * k) / 60;
    return `${px(x)},${py(x * x)}`;
  }).join(' L');
  const label = ease(frame, 88, 98);
  return (
    <svg width={GW} height={GH} style={{display: 'block', margin: '22px auto 0', overflow: 'visible'}}>
      <defs>
        <clipPath id="plot-light">
          <rect x="0" y="0" width={GW} height={GH} />
        </clipPath>
      </defs>
      <line x1={px(GX0)} y1={py(0)} x2={px(GX1)} y2={py(0)} stroke="rgba(255,255,255,0.22)" strokeWidth="1.6" />
      <line x1={px(0)} y1={py(GY0)} x2={px(0)} y2={py(GY1)} stroke="rgba(255,255,255,0.22)" strokeWidth="1.6" />
      <path clipPath="url(#plot-light)" d={`M${curve}`} fill="none" stroke="#A78BFA" strokeWidth="3.5" pathLength={1} strokeDasharray="1" strokeDashoffset={1 - draw} strokeLinecap="round" />
      <g clipPath="url(#plot-light)" opacity={secant}>
        <line x1={px(-0.6)} y1={py(line(-0.6))} x2={px(2.3)} y2={py(line(2.3))} stroke={colors.lagoonBright} strokeWidth="3" />
      </g>
      <g opacity={secant}>
        <circle cx={px(1)} cy={py(1)} r="7" fill={colors.lagoonBright} />
        <circle cx={px(1 + h)} cy={py((1 + h) ** 2)} r="7" fill={colors.window} stroke={colors.lagoonBright} strokeWidth="2.5" />
        <text x={px(1 + h / 2) + 8} y={py(0) + 30} fontFamily={fonts.serif} fontStyle="italic" fontSize="24" fill={MUTED} opacity={1 - label}>
          h
        </text>
      </g>
      <text x={px(1.3)} y={py(1) + 40} fontFamily={fonts.ui} fontSize="20" fontWeight={600} fill={colors.lagoonBright} opacity={label}>
        slope = 2
      </text>
    </svg>
  );
};

const Page: React.FC<{cls: string; color: string; meta: string; title: string; by: string; summary: string; children: React.ReactNode; frame: number}> = ({
  cls,
  color,
  meta,
  title,
  by,
  summary,
  children,
  frame,
}) => {
  const p = spr(frame, 0, {damping: 26, stiffness: 150, mass: 0.9});
  return (
    <div style={{padding: '30px 40px 0', opacity: p, transform: `translateY(${(1 - p) * 10}px)`}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: MUTED}}>
        <ClassDot color={color} size={8} /> {cls} · {meta}
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 36, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>{title}</div>
      <div style={{display: 'inline-flex', marginTop: 14, padding: 3, borderRadius: 999, background: colors.card, border: `1px solid ${colors.edge}`, fontSize: 14.5, fontWeight: 600}}>
        <div style={{padding: '6px 18px', borderRadius: 999, background: 'rgba(255,255,255,0.12)'}}>Notes</div>
        <div style={{padding: '6px 18px', color: colors.text2}}>Transcript</div>
      </div>
      <div style={{display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 16}}>
        <span style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600}}>Summary</span>
        <span style={{fontSize: 14, color: colors.text3}}>Written by {by}</span>
      </div>
      <div style={{fontFamily: fonts.serif, fontSize: 19.5, lineHeight: 1.5, marginTop: 8, color: colors.serifInk}}>{summary}</div>
      {children}
    </div>
  );
};

export const Diagrams: React.FC = () => {
  const frame = useCurrentFrame();
  const {width, height} = useVideoConfig();
  const vertical = useVertical();
  const math = frame >= SWITCH;
  // On a phone-shaped frame: just the page, laid out as on a laptop and shown 1.2 times as big.
  const z = vertical ? 1.2 : 1;
  const w = vertical ? 784 : 1320;
  const h = vertical ? 820 : 800;
  const left = (width - w * z) / 2;
  const top = vertical ? 430 : BELOW_TITLE;
  const win = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const health = (
    <Page
      frame={frame}
      cls="NURS 210"
      color={colors.health}
      meta="Thu 25 Sep · 1 h 04 min"
      title="The nursing process"
      by="Claude Code"
      summary="Five steps a nurse repeats with every patient, and each one feeds the next: assess, diagnose, plan, implement, evaluate."
    >
      <Cycle frame={frame} />
    </Page>
  );
  const calc = (
    <Page
      frame={frame - SWITCH}
      cls="CALC II"
      color={colors.calc}
      meta="Wed 24 Sep · 1 h 10 min"
      title="The derivative"
      by="Codex"
      summary="The slope of the tangent: the limit of the secant's slope as the second point closes in."
    >
      <Formula frame={frame - SWITCH} />
      <Graph frame={frame - SWITCH} />
    </Page>
  );
  const sidebar = vertical ? undefined : (
    <Sidebar
      rows={[
        {name: 'CS 101', color: colors.cs, count: 12},
        {name: 'BIO 110', color: colors.bio, count: 9},
        {name: 'CALC II', color: colors.calc, count: 11, lit: math},
        {name: 'NURS 210', color: colors.health, count: 8, lit: !math},
        {name: 'HIST 210', color: colors.hist, count: 7},
      ]}
    />
  );
  const list = vertical ? undefined : math ? (
    <>
      <ListHead title="CALC II" sub="11 lectures" />
      <Group name="This week" />
      <ListRow title="The derivative" sub="Wed 24 Sep · 1 h 10 min" color={colors.calc} lit />
      <ListRow title="Limits and continuity" sub="Mon 22 Sep · 1 h 12 min" color={colors.calc} />
      <Group name="Last week" />
      <ListRow title="Series convergence tests" sub="Wed 17 Sep · 1 h 15 min" color={colors.calc} />
    </>
  ) : (
    <>
      <ListHead title="NURS 210" sub="8 lectures" />
      <Group name="This week" />
      <ListRow title="The nursing process" sub="Thu 25 Sep · 1 h 04 min" color={colors.health} lit />
      <ListRow title="Vital signs" sub="Tue 23 Sep · 58 min" color={colors.health} />
      <Group name="Last week" />
      <ListRow title="Patient safety" sub="Thu 18 Sep · 1 h 02 min" color={colors.health} />
    </>
  );
  // Where CALC II sits in the sidebar: below the traffic lights and the "Classes" label, the third row.
  const calcRow = {x: left + 12 + 112, y: top + 194};
  return (
    <AbsoluteFill>
      <Desktop>
        <div style={{position: 'absolute', left: left / z, top: top / z, zoom: z, opacity: win, transform: `translateY(${(1 - win) * 30}px) scale(${0.98 + 0.02 * win})`}}>
          <LibraryWindow w={w} h={h} sidebar={sidebar} list={list}>
            {math ? calc : health}
          </LibraryWindow>
        </div>
        {vertical ? null : (
          <Cursor
            stops={[
              [50, calcRow.x + 420, calcRow.y + 260],
              [80, calcRow.x, calcRow.y],
              [110, calcRow.x, calcRow.y],
              [140, calcRow.x + 760, calcRow.y + 330],
            ]}
            clicks={[SWITCH]}
          />
        )}
      </Desktop>
      <Title text={text.diagrams as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};
