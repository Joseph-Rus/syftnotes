import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, spr} from '../anim';
import {BELOW_TITLE, Title, ClassDot, Cursor, Desktop, useVertical} from '../components/Layout';
import {Group, LibraryWindow, ListHead, ListRow, Sidebar} from '../components/Sidebar';
import {Sfx} from '../components/Sfx';

const SWITCH = 88; // CALC II is clicked
const INK = colors.text;
const MUTED = colors.text2;

// Nursing: the organs of the torso, front view, labelled one by one, as a drawing in the notes (the app shows SVG).
type Organ = {name: string; d: string; fill: string; label: [x: number, y: number]; to: [x: number, y: number]; side: 'left' | 'right'};
const TORSO =
  'M268 0 L268 32 C248 46 198 52 164 72 C148 82 148 112 156 150 C166 210 184 280 190 330 C192 372 178 430 172 482 ' +
  'L428 482 C422 430 408 372 410 330 C416 280 434 210 444 150 C452 112 452 82 436 72 C402 52 352 46 332 32 L332 0';
const ORGANS: Organ[] = [
  {name: 'Lungs', side: 'left', fill: '#E88FA2', label: [120, 150], to: [214, 168],
    d: 'M284 102 C252 92 206 112 196 162 C189 206 191 246 206 263 C231 272 263 263 283 251 C291 201 291 141 284 102 Z M316 102 C348 92 394 112 404 162 C411 206 409 246 394 263 C371 272 349 263 331 256 C346 236 341 216 323 206 C317 171 314 131 316 102 Z'},
  {name: 'Heart', side: 'right', fill: '#E0516A', label: [478, 206], to: [346, 212],
    d: 'M298 196 C296 178 318 172 328 188 C340 174 364 182 360 204 C356 226 332 242 318 254 C304 240 292 216 298 196 Z'},
  {name: 'Liver', side: 'left', fill: '#C0664E', label: [120, 290], to: [214, 290],
    d: 'M188 274 C220 262 282 262 320 271 C332 274 333 287 319 294 C281 307 235 320 204 313 C188 306 182 286 188 274 Z'},
  {name: 'Stomach', side: 'right', fill: '#EDA47E', label: [478, 300], to: [392, 300],
    d: 'M342 270 C374 262 404 273 406 299 C408 324 386 340 361 338 C345 336 336 324 344 313 C360 301 351 291 337 289 C331 281 333 271 342 270 Z'},
  {name: 'Kidneys', side: 'left', fill: '#A56CC0', label: [120, 344], to: [236, 342],
    d: 'M226 320 C240 314 254 326 252 344 C250 362 234 368 226 358 C233 350 233 332 226 320 Z M374 320 C360 314 346 326 348 344 C350 362 366 368 374 358 C367 350 367 332 374 320 Z'},
  {name: 'Large intestine', side: 'left', fill: 'none', label: [120, 424], to: [226, 424],
    d: 'M226 456 L226 394 C226 382 236 376 250 376 L350 376 C364 376 374 382 374 394 L374 442 C374 458 358 464 342 458'},
  {name: 'Small intestine', side: 'right', fill: 'none', label: [478, 404], to: [330, 402],
    d: 'M258 400 C282 390 302 410 326 398 S354 408 345 422 C330 434 302 418 278 428 S254 444 270 452'},
  {name: 'Bladder', side: 'right', fill: '#E7C96B', label: [478, 470], to: [318, 469],
    d: 'M282 469 C282 459 318 459 318 469 C318 479 282 479 282 469 Z'},
];

const Body: React.FC<{frame: number}> = ({frame}) => (
  <svg width={600} height={486} viewBox="0 0 600 486" style={{display: 'block', margin: '18px auto 0', overflow: 'visible'}}>
    <path d={TORSO} fill="rgba(255,255,255,0.035)" stroke="rgba(255,255,255,0.34)" strokeWidth="2" strokeLinejoin="round" opacity={ease(frame, 0, 10)} />
    <path d="M300 22 L300 96 M300 96 L276 116 M300 96 L324 116" stroke="#C9D2DE" strokeWidth="6" strokeLinecap="round" fill="none" opacity={0.55 * ease(frame, 4, 12)} />
    {ORGANS.map((o, i) => {
      const p = spr(frame, 4 + i * 2.5, {damping: 20, stiffness: 150, mass: 0.8});
      const tube = o.fill === 'none';
      return (
        <path
          key={o.name}
          d={o.d}
          fill={tube ? 'none' : o.fill}
          stroke={tube ? (o.name === 'Large intestine' ? '#CE9160' : '#E9BC8E') : 'rgba(255,255,255,0.35)'}
          strokeWidth={tube ? (o.name === 'Large intestine' ? 22 : 12) : 1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
          opacity={Math.min(1, p * 1.3) * (tube ? 0.9 : 0.88)}
        />
      );
    })}
    {ORGANS.map((o, i) => {
      const start = LABELS_FROM + i * LABEL_EVERY;
      const line = ease(frame, start, start + 9);
      const word = spr(frame, start + 6, {damping: 22, stiffness: 170, mass: 0.8});
      const [lx, ly] = o.label;
      const [tx, ty] = o.to;
      const edge = o.side === 'left' ? lx + 8 : lx - 8;
      return (
        <g key={o.name}>
          <line x1={edge} y1={ly} x2={edge + (tx - edge) * line} y2={ly + (ty - ly) * line} stroke="#DDE4EE" strokeWidth="1.6" opacity={line > 0 ? 0.85 : 0} />
          <circle cx={tx} cy={ty} r="4" fill="#FFFFFF" opacity={line >= 0.98 ? 1 : 0} />
          <text
            x={o.side === 'left' ? lx : lx}
            y={ly + 5.5}
            textAnchor={o.side === 'left' ? 'end' : 'start'}
            fontFamily={fonts.ui}
            fontSize="16.5"
            fontWeight={600}
            fill={INK}
            opacity={word}
            transform={`translate(${(1 - word) * (o.side === 'left' ? 10 : -10)} 0)`}
          >
            {o.name}
          </text>
        </g>
      );
    })}
  </svg>
);
const LABELS_FROM = 24;
const LABEL_EVERY = 6;

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
  const h = interpolate(frame, [60, 84], [1.1, 0.02], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const slope = 2 + h;
  const line = (x: number) => 1 + slope * (x - 1);
  const curve = Array.from({length: 61}, (_, k) => {
    const x = GX0 + ((GX1 - GX0) * k) / 60;
    return `${px(x)},${py(x * x)}`;
  }).join(' L');
  const label = ease(frame, 82, 92);
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
      title="Organs of the torso"
      by="Claude Code"
      summary="Where each major organ sits, seen from the front, and what's around it."
    >
      <Body frame={frame} />
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
      <ListRow title="Organs of the torso" sub="Thu 25 Sep · 1 h 04 min" color={colors.health} lit />
      <ListRow title="Vital signs" sub="Tue 23 Sep · 58 min" color={colors.health} />
      <Group name="Last week" />
      <ListRow title="The nursing process" sub="Thu 18 Sep · 1 h 02 min" color={colors.health} />
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
      {ORGANS.map((o, i) => (
        <Sfx key={o.name} at={LABELS_FROM + i * LABEL_EVERY + 7} name="pop" volume={0.16} />
      ))}
      <Sfx at={SWITCH + 14} name="pop" volume={0.2} />
      <Title text={text.diagrams as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};
