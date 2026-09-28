import React from 'react';
import {Easing, interpolate, useCurrentFrame} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {bouncy, ease, outro, rise, spr} from '../anim';
import {ClassDot, Feature, Glass, useVertical} from '../components/Layout';

const CARD_W = 540;
const CARD_H = 620;

const CardHead: React.FC<{color: string; cls: string; title: string}> = ({color, cls, title}) => (
  <div style={{padding: '26px 30px 0'}}>
    <div style={{display: 'flex', alignItems: 'center', gap: 10, fontSize: 17, color: colors.nightInk2}}>
      <ClassDot color={color} size={9} /> {cls}
    </div>
    <div style={{fontFamily: fonts.display, fontSize: 32, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>{title}</div>
  </div>
);

// Health: the cardiac cycle as the app draws a cycle, five steps round a ring.
const STEPS = ['Atrial systole', 'Isovolumetric contraction', 'Ventricular ejection', 'Isovolumetric relaxation', 'Ventricular filling'];
const CX = 270;
const CY = 262;
const RX = 178;
const RY = 168;
const at = (i: number) => {
  const a = -Math.PI / 2 + (i * 2 * Math.PI) / STEPS.length;
  return {x: CX + RX * Math.cos(a), y: CY + RY * Math.sin(a), a};
};
const box = (i: number) => {
  const k = i % STEPS.length;
  const {x, y} = at(k);
  return {x, y, w: k === 0 ? 180 : 164, h: STEPS[k].includes(' ') ? 62 : 44};
};
const inside = (x: number, y: number, b: ReturnType<typeof box>, margin = 12) =>
  Math.abs(x - b.x) <= b.w / 2 + margin && Math.abs(y - b.y) <= b.h / 2 + margin;

// Each arrow follows the ring from where it leaves one box to where it reaches the next.
const ARCS = STEPS.map((_, i) => {
  const from = box(i);
  const to = box(i + 1);
  const a0 = at(i).a;
  const a1 = a0 + (2 * Math.PI) / STEPS.length;
  const all = Array.from({length: 241}, (_, k) => {
    const a = a0 + ((a1 - a0) * k) / 240;
    return [CX + RX * Math.cos(a), CY + RY * Math.sin(a)];
  });
  const pts = all.filter(([x, y]) => !inside(x, y, from) && !inside(x, y, to));
  const end = pts[pts.length - 1];
  const prev = pts[Math.max(0, pts.length - 4)];
  return {d: `M${pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' L')}`, end, angle: (Math.atan2(end[1] - prev[1], end[0] - prev[0]) * 180) / Math.PI};
});

const Cycle: React.FC<{frame: number}> = ({frame}) => (
  <svg width={CARD_W} height={500} viewBox={`0 0 ${CARD_W} 500`} style={{position: 'absolute', top: 104, left: 0}}>
    {ARCS.map((arc, i) => {
      const p = ease(frame, 26 + i * 9, 40 + i * 9);
      return (
        <g key={i}>
          <path d={arc.d} fill="none" stroke={colors.nightInk2} strokeWidth="3" pathLength={1} strokeDasharray="1" strokeDashoffset={1 - p} strokeLinecap="round" />
          <path d="M-11,-7 L0,0 L-11,7" transform={`translate(${arc.end[0]},${arc.end[1]}) rotate(${arc.angle})`} fill="none" stroke={colors.nightInk2} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" opacity={p >= 0.98 ? 1 : 0} />
        </g>
      );
    })}
    {STEPS.map((s, i) => {
      const b = box(i);
      const p = spr(frame, 16 + i * 9, bouncy);
      const first = i === 0;
      const lines = s.split(' ');
      return (
        <g key={s} transform={`translate(${b.x},${b.y}) scale(${p})`} opacity={Math.min(1, p * 2)}>
          <rect x={-b.w / 2} y={-b.h / 2} width={b.w} height={b.h} rx={first ? b.h / 2 : 12} fill={first ? 'rgba(43,181,178,0.22)' : '#1D2942'} stroke={first ? colors.lagoonBright : 'rgba(255,255,255,0.22)'} strokeWidth="2" />
          {lines.map((l, k) => (
            <text key={l} x={0} y={(k - (lines.length - 1) / 2) * 22 + 6} textAnchor="middle" fontFamily={fonts.ui} fontSize="17" fontWeight={500} fill={colors.nightInk}>
              {l}
            </text>
          ))}
        </g>
      );
    })}
  </svg>
);

// Math: the derivative, typeset, and the secant closing in on the tangent as h goes to 0.
const Formula: React.FC<{frame: number}> = ({frame}) => {
  const part = (i: number): React.CSSProperties => rise(spr(frame, 18 + i * 6), 24);
  const bar = ease(frame, 30, 44);
  return (
    <div style={{display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontFamily: fonts.serif, fontSize: 36, color: colors.nightInk, marginTop: 34}}>
      <span style={{...part(0), fontStyle: 'italic'}}>f ′(x)</span>
      <span style={part(1)}>=</span>
      <span style={{...part(2), display: 'inline-flex', flexDirection: 'column', alignItems: 'center', lineHeight: 1}}>
        <span>lim</span>
        <span style={{fontSize: 22, fontStyle: 'italic', marginTop: 6}}>h→0</span>
      </span>
      <span style={{...part(3), display: 'inline-flex', flexDirection: 'column', alignItems: 'center'}}>
        <span style={{fontStyle: 'italic', padding: '0 6px 6px'}}>f(x + h) − f(x)</span>
        <span style={{height: 2.5, alignSelf: 'stretch', background: colors.nightInk, transform: `scaleX(${bar})`}} />
        <span style={{fontStyle: 'italic', paddingTop: 4}}>h</span>
      </span>
    </div>
  );
};

const GX0 = -0.6;
const GX1 = 2.3;
const GY0 = -0.4;
const GY1 = 4.6;
const GW = 470;
const GH = 300;
const px = (x: number) => ((x - GX0) / (GX1 - GX0)) * GW;
const py = (y: number) => GH - ((y - GY0) / (GY1 - GY0)) * GH;

const Graph: React.FC<{frame: number}> = ({frame}) => {
  const draw = ease(frame, 44, 74);
  const secant = ease(frame, 76, 84);
  const h = interpolate(frame, [86, 146], [1.1, 0.02], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const slope = 2 + h;
  const line = (x: number) => 1 + slope * (x - 1);
  const curve = Array.from({length: 61}, (_, k) => {
    const x = GX0 + ((GX1 - GX0) * k) / 60;
    return `${px(x)},${py(x * x)}`;
  }).join(' L');
  const label = ease(frame, 142, 156);
  return (
    <svg width={GW} height={GH} style={{display: 'block', margin: '26px auto 0', overflow: 'visible'}}>
      <defs>
        <clipPath id="plot">
          <rect x="0" y="0" width={GW} height={GH} />
        </clipPath>
      </defs>
      <line x1={px(GX0)} y1={py(0)} x2={px(GX1)} y2={py(0)} stroke="rgba(255,255,255,0.25)" strokeWidth="2" />
      <line x1={px(0)} y1={py(GY0)} x2={px(0)} y2={py(GY1)} stroke="rgba(255,255,255,0.25)" strokeWidth="2" />
      <path clipPath="url(#plot)" d={`M${curve}`} fill="none" stroke={colors.calc} strokeWidth="4" pathLength={1} strokeDasharray="1" strokeDashoffset={1 - draw} strokeLinecap="round" />
      <g clipPath="url(#plot)" opacity={secant}>
        <line x1={px(-0.6)} y1={py(line(-0.6))} x2={px(2.3)} y2={py(line(2.3))} stroke={colors.lagoonBright} strokeWidth="3.5" />
      </g>
      <g opacity={secant}>
        <circle cx={px(1)} cy={py(1)} r="8" fill={colors.lagoonBright} />
        <circle cx={px(1 + h)} cy={py((1 + h) ** 2)} r="8" fill="none" stroke={colors.lagoonBright} strokeWidth="3" />
        <text x={px(1 + h / 2) + 10} y={py(0) + 34} fontFamily={fonts.serif} fontStyle="italic" fontSize="26" fill={colors.nightInk2} opacity={1 - label}>
          h
        </text>
      </g>
      <text x={px(1.25)} y={py(1) + 44} fontFamily={fonts.ui} fontSize="22" fontWeight={600} fill={colors.lagoonBright} opacity={label}>
        slope = 2
      </text>
    </svg>
  );
};

export const Diagrams: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const gap = 36;
  const w = vertical ? CARD_W : CARD_W * 2 + gap;
  const h = vertical ? CARD_H * 2 + gap : CARD_H;
  return (
    <Feature headline={text.diagrams} w={w} h={h} opacity={outro(frame, durations.diagrams)}>
      <div style={{display: 'flex', flexDirection: vertical ? 'column' : 'row', gap}}>
        <div style={rise(spr(frame, 4), 60)}>
          <Glass style={{width: CARD_W, height: CARD_H, position: 'relative'}}>
            <CardHead color={colors.health} cls="HLTH 120 · Health" title="The cardiac cycle" />
            <Cycle frame={frame} />
          </Glass>
        </div>
        <div style={rise(spr(frame, 10), 60)}>
          <Glass style={{width: CARD_W, height: CARD_H}}>
            <CardHead color={colors.calc} cls="CALC II · Math" title="The derivative" />
            <Formula frame={frame} />
            <Graph frame={frame} />
          </Glass>
        </div>
      </div>
    </Feature>
  );
};
