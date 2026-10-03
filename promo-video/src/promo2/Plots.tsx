import React from 'react';
import {colors, fonts} from '../config';
import {useTextKey} from './DrawnScale';
import {Icon} from './ui';

// Two of the app's plots (Core/Rich/PlotDesign.cs), computed here exactly as the app computes them and drawn in its
// dark look (the "plots" screenshot tests): the sigmoid with its steepness slider, and gradient descent on a long,
// narrow bowl with its learning-rate slider. Same ranges, same functions, same starting point.
//
//   title The sigmoid squashes any score into (0, 1)
//   x -6 to 6 "z (the score)"   y -0.05 to 1.05 "σ(z)"   param k = 1 from 0.2 to 5 "steepness k"
//   σ(z) = 1 / (1 + e^(-k z)) "σ(kz)"   y = 0.5 dashed "threshold 0.5"   point (0, σ(0)) "σ(0) = 0.5"
//
//   title Gradient descent on a long, narrow bowl
//   x -3 to 3 "w₁"   y -2 to 2 "w₂"   param η = 0.17 from 0.01 to 0.21 "learning rate η"
//   heat L(w_1, w_2) = w_1^2 + 5 w_2^2 "loss"   descent L from (-2.6, 1.5) rate η steps 25 "gradient descent"

export const SIGMOID = {k: {from: 0.2, to: 5, start: 1}};
export const DESCENT = {eta: {from: 0.01, to: 0.21, start: 0.17}};

const P = {
  grid: 'rgba(255,255,255,0.07)',
  axis: 'rgba(255,255,255,0.32)',
  tick: '#86909A',
  label: '#A3ACB4',
  ink: '#EEF2F4',
  curve: '#4E92EA',
  path: '#E9762E',
  w: 620,
};
const F = {fontFamily: fonts.ui};
const fmt = (v: number) => {
  const r = Math.round(v * 100) / 100;
  return (r < 0 ? '−' : '') + Math.abs(r).toString();
};
const tickText = (v: number, digits = 0) => (v < 0 ? '−' : '') + Math.abs(v).toFixed(digits);

/** The sigmoid at steepness k, drawn in the plot's 620 × 410 units. */
export const SigmoidPlot: React.FC<{k: number; scale: number}> = ({k, scale}) => {
  const X0 = 28;
  const X1 = 606;
  const Y0 = 44.42;
  const Y1 = 368.42;
  const sx = (z: number) => X0 + ((z + 6) / 12) * (X1 - X0);
  const sy = (y: number) => Y1 - ((y + 0.05) / 1.1) * (Y1 - Y0);
  const curve = Array.from({length: 241}, (_, i) => {
    const z = -6 + (12 * i) / 240;
    return `${i ? 'L' : 'M'}${sx(z).toFixed(2)},${sy(1 / (1 + Math.exp(-k * z))).toFixed(2)}`;
  }).join('');
  const textKey = useTextKey();
  return (
    <svg key={textKey} width={P.w * scale} height={410 * scale} viewBox="0 0 620 410" style={{display: 'block', overflow: 'visible'}}>
      <defs>
        <clipPath id="sig-area">
          <rect x={X0} y={Y0} width={X1 - X0} height={Y1 - Y0} />
        </clipPath>
      </defs>
      <text x="0" y="13.84" {...F} fontSize="13.5" fontWeight={700} fill={P.ink}>The sigmoid squashes any score into (0, 1)</text>
      <text x="0" y="36.53" {...F} fontSize="12" fill={P.label}>σ(z)</text>
      {Array.from({length: 13}, (_, i) => (
        <line key={`v${i}`} x1={sx(i - 6)} y1={Y0} x2={sx(i - 6)} y2={Y1} stroke={P.grid} />
      ))}
      {Array.from({length: 11}, (_, i) => (
        <line key={`h${i}`} x1={X0} y1={sy(i / 10)} x2={X1} y2={sy(i / 10)} stroke={P.grid} />
      ))}
      <line x1={X0} y1={sy(0)} x2={X1} y2={sy(0)} stroke={P.axis} />
      <line x1={sx(0)} y1={Y0} x2={sx(0)} y2={Y1} stroke={P.axis} />
      <line x1={X0} y1={sy(0.5)} x2={X1} y2={sy(0.5)} stroke="#8D969E" strokeWidth={1.4} strokeDasharray="4.2 3.5" />
      <path d={curve} fill="none" stroke={P.curve} strokeWidth={2.4} strokeLinejoin="round" clipPath="url(#sig-area)" />
      <circle cx={sx(0)} cy={sy(0.5)} r={4.2} fill="#FFFFFF" stroke="#0F1A21" strokeWidth={1.4} />
      {Array.from({length: 11}, (_, i) => (
        <text key={`yt${i}`} x="2.85" y={sy(i / 10) + 4} {...F} fontSize="11" fill={P.tick}>{(i / 10).toFixed(1)}</text>
      ))}
      {Array.from({length: 13}, (_, i) => (
        <text key={`xt${i}`} x={sx(i - 6)} y="384.7" textAnchor="middle" {...F} fontSize="11" fill={P.tick}>{tickText(i - 6)}</text>
      ))}
      <text x={X1} y="403.58" textAnchor="end" {...F} fontSize="12" fill={P.label}>z (the score)</text>
      <text x={X1} y={sy(0.5) - 7} textAnchor="end" {...F} fontSize="12" fill={P.label}>threshold 0.5</text>
      <text x={sx(0) - 8} y={sy(0.5) - 8} textAnchor="end" {...F} fontSize="12" fill={P.ink}>σ(0) = 0.5</text>
      <text x={sx(3.3)} y={sy(1) - 6} {...F} fontSize="12" fill={P.ink}>σ(kz)</text>
    </svg>
  );
};

/** Gradient descent from (−2.6, 1.5) for 25 steps at rate η on L = w₁² + 5w₂², in the plot's 620 × 430 units. */
export const descentPath = (eta: number) => {
  const pts: [number, number][] = [[-2.6, 1.5]];
  for (let i = 0; i < 25; i++) {
    const [a, b] = pts[pts.length - 1];
    pts.push([a - eta * 2 * a, b - eta * 10 * b]);
  }
  return pts;
};

const heat = (L: number) => {
  // Dark at the bottom of the bowl, light blue up its sides, as the app's dark heat map.
  const t = Math.min(1, Math.sqrt(L / 29));
  const a = [22, 33, 56];
  const b = [160, 205, 243];
  const c = a.map((v, i) => Math.round(v + (b[i] - v) * t));
  return `rgb(${c.join(',')})`;
};

export const DescentPlot: React.FC<{eta: number; scale: number}> = ({eta, scale}) => {
  const X0 = 34;
  const X1 = 606;
  const Y0 = 68.62;
  const Y1 = 388.62;
  const sx = (x: number) => X0 + ((x + 3) / 6) * (X1 - X0);
  const sy = (y: number) => Y1 - ((y + 2) / 4) * (Y1 - Y0);
  const rx = (L: number) => (Math.sqrt(L) / 6) * (X1 - X0);
  const ry = (L: number) => (Math.sqrt(L / 5) / 4) * (Y1 - Y0);
  const levels = Array.from({length: 48}, (_, i) => 30 * (1 - i / 48) ** 1.6 + 0.05);
  const pts = descentPath(eta);
  const d = pts.map(([a, b], i) => `${i ? 'L' : 'M'}${sx(a).toFixed(2)},${sy(b).toFixed(2)}`).join('');
  const inside = pts.filter(([a, b]) => Math.abs(a) <= 3 && Math.abs(b) <= 2);
  const textKey = useTextKey();
  return (
    <svg key={textKey} width={P.w * scale} height={430 * scale} viewBox="0 0 620 430" style={{display: 'block', overflow: 'visible'}}>
      <defs>
        <clipPath id="gd-area">
          <rect x={X0} y={Y0} width={X1 - X0} height={Y1 - Y0} />
        </clipPath>
        <linearGradient id="gd-swatch" x1="0" x2="1">
          <stop offset="0" stopColor={heat(0)} />
          <stop offset="1" stopColor={heat(29)} />
        </linearGradient>
      </defs>
      <text x="0" y="13.84" {...F} fontSize="13.5" fontWeight={700} fill={P.ink}>Gradient descent on a long, narrow bowl</text>
      <rect x="0" y="27.5" width="18" height="11" rx="2" fill="url(#gd-swatch)" />
      <text x="24" y="36.53" {...F} fontSize="12" fill={P.label}>loss</text>
      <line x1="68" y1="33" x2="88" y2="33" stroke={P.path} strokeWidth={2} />
      <text x="94" y="36.53" {...F} fontSize="12" fill={P.label}>gradient descent</text>
      <text x="0" y="60.73" {...F} fontSize="12" fill={P.label}>w₂</text>
      <g clipPath="url(#gd-area)">
        <rect x={X0} y={Y0} width={X1 - X0} height={Y1 - Y0} fill={heat(30)} />
        {levels.map((L) => (
          <ellipse key={L} cx={sx(0)} cy={sy(0)} rx={rx(L)} ry={ry(L)} fill={heat(L)} />
        ))}
        {[0.5, 1.5, 3, 5, 7.5, 10.5, 14, 18, 22.5, 27.5].map((L) => (
          <ellipse key={`c${L}`} cx={sx(0)} cy={sy(0)} rx={rx(L)} ry={ry(L)} fill="none" stroke="rgba(255,255,255,0.28)" strokeWidth={0.9} />
        ))}
        <line x1={X0} y1={sy(0)} x2={X1} y2={sy(0)} stroke="rgba(255,255,255,0.25)" />
        <line x1={sx(0)} y1={Y0} x2={sx(0)} y2={Y1} stroke="rgba(255,255,255,0.25)" />
        <path d={d} fill="none" stroke={P.path} strokeWidth={2} strokeLinejoin="round" />
        {inside.map(([a, b], i) => (
          <circle key={i} cx={sx(a)} cy={sy(b)} r={i === 0 ? 5 : 3.6} fill={i === 0 ? '#1A1F24' : P.path} stroke={i === 0 ? P.path : '#1A1F24'} strokeWidth={1.4} />
        ))}
      </g>
      {[-2, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 2].map((v) => (
        <text key={`y${v}`} x="2.8" y={sy(v) + 4} {...F} fontSize="11" fill={P.tick}>{tickText(v, 1)}</text>
      ))}
      {[-3, -2, -1, 0, 1, 2, 3].map((v) => (
        <text key={`x${v}`} x={sx(v)} y="404.9" textAnchor="middle" {...F} fontSize="11" fill={P.tick}>{tickText(v)}</text>
      ))}
      <text x={X1} y="423.78" textAnchor="end" {...F} fontSize="12" fill={P.label}>w₁</text>
    </svg>
  );
};

/** A plot's slider, as PlotSlider draws it: its name, the track filled to the knob, the value, and Play. */
export const SLIDER = {labelW: 120, valueW: 64, gap: 16, h: 40};
export const SliderRow: React.FC<{label: string; pos: number; value: number; width: number; held?: boolean}> = ({label, pos, value, width, held}) => {
  const track = width - SLIDER.labelW - SLIDER.valueW - SLIDER.gap;
  return (
    <div style={{display: 'flex', alignItems: 'center', height: SLIDER.h, width, fontFamily: fonts.ui, fontSize: 14.5, color: colors.text}}>
      <div style={{width: SLIDER.labelW, color: '#C3CAD0', whiteSpace: 'nowrap'}}>{label}</div>
      <div style={{position: 'relative', width: track, height: 20, marginRight: SLIDER.gap}}>
        <div style={{position: 'absolute', left: 0, right: 0, top: 8, height: 5, borderRadius: 3, background: 'rgba(255,255,255,0.12)'}} />
        <div style={{position: 'absolute', left: 0, width: track * pos, top: 8, height: 5, borderRadius: 3, background: colors.lagoon}} />
        <div
          style={{
            position: 'absolute',
            left: track * pos - 10,
            top: 0,
            width: 20,
            height: 20,
            borderRadius: 10,
            background: '#E4E7EA',
            boxShadow: held ? `0 0 0 6px rgba(52,193,189,0.25), 0 2px 6px rgba(0,0,0,0.5)` : '0 2px 6px rgba(0,0,0,0.5)',
          }}
        />
      </div>
      <div style={{width: SLIDER.valueW, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 8, fontWeight: 700, fontVariantNumeric: 'tabular-nums'}}>
        {fmt(value)}
        <Icon name="play_arrow" size={15} />
      </div>
    </div>
  );
};
/** Where a slider's knob is along its row, for the pointer. */
export const knobX = (width: number, pos: number) => SLIDER.labelW + (width - SLIDER.labelW - SLIDER.valueW - SLIDER.gap) * pos;
