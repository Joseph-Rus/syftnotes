import React from 'react';
import {fonts} from '../config';
import {useTextKey} from './DrawnScale';
import {ui} from './ui';

// BIO 110's diagram of the cardiac cycle, as the app paints a flowchart in dark mode (DiagramPainter): rounded boxes
// with the lecture's specifics on a smaller second line, arrows with filled heads, a dashed arrow back to the start.
// Everything is in the diagram's own units (FLOW_W × FLOW_H); the scene scales it.

export const FLOW_W = 640;
export const FLOW_H = 452;
const BW = 238;
const BH = 62;
const LX = 150;
const RX = 490;
const ROW = (r: number) => 52 + r * 112;

export type Box = {id: string; title: string; sub: string; x: number; y: number};
export const BOXES: Box[] = [
  {id: 'fill', title: 'Atria fill', sub: 'blood returns from the veins', x: LX, y: ROW(0)},
  {id: 'atria', title: 'Atria contract', sub: 'the last fifth of filling', x: LX, y: ROW(1)},
  {id: 'avclose', title: 'AV valves close', sub: 'first heart sound, “lub”', x: LX, y: ROW(2)},
  {id: 'contract', title: 'Ventricles contract', sub: 'every valve shut, pressure climbs', x: LX, y: ROW(3)},
  {id: 'eject', title: 'Blood is ejected', sub: 'about 70 mL a beat', x: RX, y: ROW(3)},
  {id: 'slclose', title: 'Semilunar valves close', sub: 'second heart sound, “dub”', x: RX, y: ROW(2)},
  {id: 'relax', title: 'Ventricles relax', sub: 'pressure falls', x: RX, y: ROW(1)},
  {id: 'avopen', title: 'AV valves open', sub: 'the ventricles refill', x: RX, y: ROW(0)},
];
export const box = (id: string) => BOXES.find((b) => b.id === id)!;

type Edge = {id: string; from: string; to: string; label?: string; dashed?: boolean};
export const EDGES: Edge[] = BOXES.map((b, i) => {
  const to = BOXES[(i + 1) % BOXES.length];
  return {id: `${b.id}>${to.id}`, from: b.id, to: to.id, ...(i === BOXES.length - 1 ? {label: 'next beat', dashed: true} : {})};
});
/** The boxes next to a box, and the arrows between them: what lights up with it. */
export const around = (id: string) => {
  const edges = EDGES.filter((e) => e.from === id || e.to === id);
  return {boxes: [id, ...edges.map((e) => (e.from === id ? e.to : e.from))], edges: edges.map((e) => e.id)};
};

const geom = (e: Edge) => {
  const a = box(e.from);
  const b = box(e.to);
  if (a.x === b.x) {
    const down = b.y > a.y;
    return {x1: a.x, y1: a.y + (down ? BH / 2 : -BH / 2), x2: b.x, y2: b.y + (down ? -BH / 2 : BH / 2)};
  }
  const right = b.x > a.x;
  return {x1: a.x + (right ? BW / 2 : -BW / 2), y1: a.y, x2: b.x + (right ? -BW / 2 : BW / 2), y2: b.y};
};

/** Where a box is, in the diagram's units: its centre, and its size. */
export const boxAt = (id: string) => ({x: box(id).x, y: box(id).y, w: BW, h: BH});

export type FlowLook = {
  build?: number; // 0 → 1: the boxes and arrows arrive
  lit?: {boxes: string[]; edges: string[]} | null; // what stays bright…
  dim?: number; // …while the rest dims by this much (0 → 1)
  accent?: string[]; // arrows in the accent colour
  ring?: string | null; // a box with the accent outline
  ringP?: number;
  hidden?: number; // 0 → 1: the words go, bars take their place (Test yourself)
  shown?: Record<string, number>; // boxes revealed while hidden, 0 → 1
  marks?: Record<string, {knew: boolean; p: number}>; // how each recalled box went
};

const Bars: React.FC<{b: Box; p: number}> = ({b, p}) => {
  const w1 = Math.min(BW - 40, b.title.length * 7.6);
  const w2 = Math.min(BW - 34, b.sub.length * 6.1);
  return (
    <g opacity={p}>
      <rect x={b.x - w1 / 2} y={b.y - 16} width={w1} height={11} rx={5.5} fill={ui.hiddenBar} />
      <rect x={b.x - w2 / 2} y={b.y + 5} width={w2} height={10} rx={5} fill={ui.hiddenBar} opacity={0.8} />
    </g>
  );
};

export const Flow: React.FC<{look: FlowLook; scale: number}> = ({look, scale}) => {
  const {build = 1, lit = null, dim = 0, accent = [], ring = null, ringP = 1, hidden = 0, shown = {}, marks = {}} = look;
  const fade = (on: boolean) => (on ? 1 : 1 - 0.72 * dim);
  const textKey = useTextKey();
  return (
    <svg key={textKey} width={FLOW_W * scale} height={FLOW_H * scale} viewBox={`0 0 ${FLOW_W} ${FLOW_H}`} style={{display: 'block', overflow: 'visible'}}>
      <defs>
        <marker id="fl-head" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7.5" markerHeight="7.5" orient="auto-start-reverse">
          <path d="M0 0.8L10 5L0 9.2z" fill={ui.arrow} />
        </marker>
        <marker id="fl-head-on" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7.5" markerHeight="7.5" orient="auto-start-reverse">
          <path d="M0 0.8L10 5L0 9.2z" fill={ui.accent} />
        </marker>
      </defs>
      {EDGES.map((e, i) => {
        const g = geom(e);
        const on = accent.includes(e.id);
        const p = Math.max(0, Math.min(1, build * 1.6 - 0.25 - i * 0.06));
        const back = e.dashed;
        const d = `M${g.x1} ${g.y1} L${g.x2} ${g.y2}`;
        return (
          <g key={e.id} opacity={fade(!lit || lit.edges.includes(e.id)) * Math.min(1, p * 2)}>
            <path
              d={d}
              fill="none"
              stroke={on ? ui.accent : ui.arrow}
              strokeWidth={on ? 2.4 : 1.5}
              strokeDasharray={back ? '5 4' : undefined}
              markerEnd={on ? 'url(#fl-head-on)' : 'url(#fl-head)'}
              pathLength={back ? undefined : 1}
              style={back ? undefined : {strokeDasharray: 1, strokeDashoffset: 1 - p}}
            />
            {e.label ? (
              <text x={(g.x1 + g.x2) / 2} y={g.y1 - 10} textAnchor="middle" fontFamily={fonts.ui} fontSize="12.5" fill={on ? ui.accent : ui.boxSub}>
                {e.label}
              </text>
            ) : null}
          </g>
        );
      })}
      {BOXES.map((b, i) => {
        const p = Math.max(0, Math.min(1, build * 1.5 - i * 0.07));
        const on = !lit || lit.boxes.includes(b.id);
        const reveal = shown[b.id] ?? 0;
        const words = Math.max(1 - hidden, reveal);
        const isRing = ring === b.id;
        const mark = marks[b.id];
        return (
          <g key={b.id} opacity={fade(on) * Math.min(1, p * 1.4)} transform={`translate(${b.x} ${b.y}) scale(${0.92 + 0.08 * p}) translate(${-b.x} ${-b.y})`}>
            {isRing ? (
              <rect x={b.x - BW / 2 - 4} y={b.y - BH / 2 - 4} width={BW + 8} height={BH + 8} rx={11} fill="none" stroke={ui.accentGlow} strokeWidth={5} opacity={ringP} />
            ) : null}
            <rect x={b.x - BW / 2} y={b.y - BH / 2} width={BW} height={BH} rx={8} fill={ui.box} stroke={isRing ? ui.accent : ui.boxEdge} strokeWidth={isRing ? 2 : 1} />
            <g opacity={words}>
              <text x={b.x} y={b.y - 4} textAnchor="middle" fontFamily={fonts.ui} fontWeight={700} fontSize="15" fill={ui.boxText}>
                {b.title}
              </text>
              <text x={b.x} y={b.y + 16} textAnchor="middle" fontFamily={fonts.ui} fontSize="12.5" fill={ui.boxSub}>
                {b.sub}
              </text>
            </g>
            <Bars b={b} p={1 - words} />
            {mark ? (
              <g transform={`translate(${b.x + BW / 2 - 2} ${b.y - BH / 2 + 2}) scale(${0.4 + 0.6 * mark.p})`} opacity={Math.min(1, mark.p * 2)}>
                <circle r="9" fill={mark.knew ? ui.knew : ui.notYet} />
                {mark.knew ? <path d="M-4 0.3l2.6 2.6L4.2-2.8" fill="none" stroke="#0F1A21" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" /> : <path d="M-4 0h8" stroke="#0F1A21" strokeWidth="2.2" strokeLinecap="round" />}
              </g>
            ) : null}
          </g>
        );
      })}
    </svg>
  );
};
