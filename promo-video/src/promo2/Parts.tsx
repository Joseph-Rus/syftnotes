import React from 'react';
import {continueRender, delayRender} from 'remotion';
import {colors, fonts} from '../config';
import {useTextKey} from './DrawnScale';
import * as drone from './art/drone';
import * as hand from './art/hand';
import {BarButton, ui} from './ui';

// The app's own labelled drawings (art2/svg, made into modules by scripts/art2.py), drawn inline so a part can light
// up as PartsExplorer lights it in dark mode: the part under the pointer keeps its colour and gets an accent ring, its
// label goes white, and the rest of the drawing and its labels dim.

export const ART = {drone, hand};
export type ArtName = keyof typeof ART;

export const artSize = (name: ArtName) => {
  const [, , w, h] = ART[name].viewBox.split(' ').map(Number);
  return {w, h};
};

export type PartsLook = {hot?: string | null; dim?: number; ring?: number};

// Labels dim by their colours' alpha, never by a group's opacity: Chrome sometimes clipped an see-through group of
// text to stale bounds when frames were rendered in parallel, taking a label's first letter with it.
const css = (n: ArtName, {hot = null, dim = 0, ring = 0}: PartsLook) => {
  const a = 1 - 0.62 * dim;
  return `
  #${n}-labels text { fill: #D3D9DE; fill-opacity: ${a}; }
  #${n}-labels text[font-size="12"] { fill: #8C979F; }
  #${n}-labels polyline { stroke: rgba(222, 228, 234, ${0.42 * a}); }
  #${n}-labels circle { fill: rgba(222, 228, 234, ${0.7 * a}); }
  #${n}-art > g { opacity: ${a}; }
  ${hot ? `
  #${n}-art > g#${n}-${hot} { opacity: 1; ${ring > 0.01 ? `filter: url(#${n}-ring);` : ''} }
  #${n}-labels > g#${n}-label-${hot} text { fill: #FFFFFF; fill-opacity: 1; }
  #${n}-labels > g#${n}-label-${hot} polyline { stroke: ${ui.accent}; }
  #${n}-labels > g#${n}-label-${hot} circle { fill: ${ui.accent}; }` : ''}
`;
};

// The drawings' labels are in the computer's Helvetica Neue, as the app draws them. Wait for it before the first frame:
// laid out in a stand-in font first, a centred label could keep the stand-in's narrower bounds and lose its first letter.
const LABEL_FONT = 'Helvetica Neue';
const useLabelFont = () => {
  const [handle] = React.useState(() => delayRender(`${LABEL_FONT} for the drawings' labels`));
  React.useEffect(() => {
    Promise.all([document.fonts.load(`14px "${LABEL_FONT}"`), document.fonts.load(`12px "${LABEL_FONT}"`)])
      .catch(() => undefined)
      .then(() => continueRender(handle));
  }, [handle]);
};

/** A drawing at `scale`, styled by `look`. */
export const Drawing: React.FC<{name: ArtName; scale: number; look?: PartsLook}> = ({name, scale, look = {}}) => {
  useLabelFont();
  const art = ART[name];
  const {w, h} = artSize(name);
  const inner = React.useMemo(() => ({__html: art.inner}), [art]);
  const textKey = useTextKey();
  return (
    <svg key={textKey} width={w * scale} height={h * scale} viewBox={art.viewBox} fontFamily="Helvetica Neue, Arial, sans-serif" style={{display: 'block', overflow: 'visible'}}>
      <style>{css(name, look)}</style>
      <defs>
        <filter id={`${name}-ring`} x="-30%" y="-30%" width="160%" height="160%">
          <feMorphology in="SourceAlpha" operator="dilate" radius={2.2 * (look.ring ?? 0)} result="grown" />
          <feFlood floodColor={ui.accent} />
          <feComposite in2="grown" operator="in" result="ring" />
          <feGaussianBlur in="ring" stdDeviation="2.4" result="glow" />
          <feMerge>
            <feMergeNode in="glow" />
            <feMergeNode in="ring" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
      <g dangerouslySetInnerHTML={inner} />
    </svg>
  );
};

/** A pinned part's card: its name, what the drawing says of it, and what you can do with it (PartsChrome). */
export const PartCard: React.FC<{name: ArtName; part: string; p: number; width?: number; hot?: string}> = ({name, part, p, width = 420, hot}) => {
  const info = ART[name].parts[part];
  return (
    <div
      style={{
        width,
        background: ui.bar,
        border: `1px solid ${ui.barEdge}`,
        borderRadius: 12,
        boxShadow: '0 14px 34px -10px rgba(0,0,0,0.7), 0 0 0 0.5px rgba(0,0,0,0.5)',
        padding: '12px 12px 6px',
        fontFamily: fonts.ui,
        color: colors.text,
        opacity: Math.min(1, p * 1.5),
        transform: `translateY(${(1 - p) * -8}px) scale(${0.96 + 0.04 * p})`,
        transformOrigin: 'top left',
      }}
    >
      <div style={{fontSize: 16, fontWeight: 700, padding: '0 4px'}}>{info.name}</div>
      <div style={{fontSize: 14, lineHeight: 1.4, color: colors.text2, marginTop: 4, padding: '0 4px'}}>{info.desc}</div>
      <div style={{display: 'flex', flexWrap: 'wrap', marginTop: 6, marginLeft: -4}}>
        <BarButton icon="chat" label="Explain this" hot={hot === 'explain'} />
        <BarButton icon="quiz" label="Quiz me" hot={hot === 'quiz'} />
        <BarButton icon="graphic_eq" label="Where was this said?" hot={hot === 'where'} />
        <BarButton icon="add" label="Zoom in" hot={hot === 'zoom'} />
      </div>
    </div>
  );
};
