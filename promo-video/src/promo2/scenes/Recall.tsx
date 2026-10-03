import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../../config';
import {ease, spr} from '../../anim';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {CLICK, text2} from '../config';
import {box as boxOf, BOXES} from '../Flow';
import {BarButton, FloatingBar, Grow, StageWindow, Strip, StripIcon} from '../ui';
import {FLOW_ICONS, FlowNote, useFlowStage} from './Explore';

// Test yourself (DiagramChrome's recall): every box's words hide; click a box to check it, then Knew it or Not yet
// (or Y and N on the keyboard); the strip keeps count, "5 of 8 recalled".
const TEST = 16; // Test yourself is clicked
type Turn = {id: string; click: number; grade: number; knew: boolean; key?: 'Y' | 'N'};
const TURNS: Turn[] = [
  {id: 'contract', click: 34, grade: 50, knew: true}, // graded with the strip's Knew it
  {id: 'eject', click: 62, grade: 68, knew: true, key: 'Y'},
  {id: 'slclose', click: 76, grade: 82, knew: false, key: 'N'},
  {id: 'avclose', click: 90, grade: 96, knew: true, key: 'Y'},
  {id: 'atria', click: 104, grade: 110, knew: true, key: 'Y'},
  {id: 'relax', click: 118, grade: 124, knew: true, key: 'Y'},
];

const Key: React.FC<{k: string; p: number; x: number; y: number}> = ({k, p, x, y}) => (
  <div
    style={{
      position: 'absolute',
      left: x,
      top: y,
      width: 38,
      height: 38,
      borderRadius: 9,
      background: '#F4F6F8',
      color: '#1A2230',
      boxShadow: '0 3px 0 #9AA3AE, 0 10px 20px -6px rgba(0,0,0,0.5)',
      fontFamily: fonts.ui,
      fontWeight: 700,
      fontSize: 20,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      opacity: p,
      transform: `translateY(${(1 - p) * 8}px) scale(${0.85 + 0.15 * p})`,
      zIndex: 60,
    }}
  >
    {k}
  </div>
);

export const Recall: React.FC = () => {
  const frame = useCurrentFrame();
  const fs = useFlowStage();
  const {stage} = fs;
  const testing = frame >= TEST;
  const hidden = ease(frame, TEST + 1, TEST + 10);
  const shown: Record<string, number> = {};
  const marks: Record<string, {knew: boolean; p: number}> = {};
  let open: Turn | null = null;
  for (const t of TURNS) {
    if (frame >= t.click) shown[t.id] = ease(frame, t.click + 1, t.click + 8);
    if (frame >= t.grade) marks[t.id] = {knew: t.knew, p: spr(frame, t.grade, {damping: 12, stiffness: 220, mass: 0.6})};
    // The question stays a moment after it's answered, so the click is seen to land on Knew it.
    if (frame >= t.click && frame < t.grade + (t.key ? 3 : 6)) open = t;
  }
  const knew = TURNS.filter((t) => frame >= t.grade && t.knew).length;
  const checked = TURNS.filter((t) => frame >= t.grade).length;

  const strip = testing ? (
    <Strip p={spr(frame, TEST + 1, {damping: 22, stiffness: 180, mass: 0.8})}>
      <span style={{fontWeight: 700, margin: '0 10px 0 6px'}}>{checked > 0 ? `${knew} of ${BOXES.length} recalled` : 'Test yourself'}</span>
      {open ? (
        <>
          <span style={{color: colors.text2}}>“{boxOf(open.id).title}”: did you know it?</span>
          <Grow />
          <BarButton icon="check" label="Knew it" hot={frame >= TURNS[0].grade - 8 && frame < TURNS[0].grade + 6 && open.id === TURNS[0].id} />
          <BarButton label="Not yet" />
        </>
      ) : (
        <>
          <span style={{color: colors.text2, overflow: 'hidden', textOverflow: 'ellipsis'}}>Say each hidden one to yourself, then click it to check.</span>
          <Grow />
          {stage.vertical ? null : (
            <>
              <BarButton label="Hide some" />
              <BarButton label="Start again" />
            </>
          )}
        </>
      )}
      <StripIcon name="close" />
    </Strip>
  ) : null;

  // Where the strip's Knew it is: its right end, less Not yet and Done.
  const knewAt = stage.at(stage.page.x + stage.page.w - 44 - 86 - 60, fs.stripTop + 21);
  const at = (id: string, dx = 30, dy = 6): [number, number] => {
    const p = fs.box(id);
    return [p.x + dx, p.y + dy];
  };
  const stops: [number, number, number][] = [
    [2, ...at('contract', 120, -90)],
    [TEST - 6, fs.barIcon(1).x - 4, fs.barIcon(1).y - 6],
    [TEST + 6, fs.barIcon(1).x - 4, fs.barIcon(1).y - 6],
    [TURNS[0].click - 4, ...at('contract')],
    [TURNS[0].click + 4, ...at('contract')],
    [TURNS[0].grade - 5, knewAt.x - 4, knewAt.y - 6],
    [TURNS[0].grade + 3, knewAt.x - 4, knewAt.y - 6],
  ];
  for (const t of TURNS.slice(1)) stops.push([t.click - 3, ...at(t.id)], [t.grade + 2, ...at(t.id, 34, 8)]);
  const pointerAt = (f: number) => {
    // The pointer's place at a frame, for the key that floats beside it.
    const i = stops.findIndex((s) => s[0] >= f);
    const s = stops[Math.max(0, i)];
    return {x: s[1], y: s[2]};
  };

  return (
    <AbsoluteFill>
      <Desktop clock="Tue 23 Sep  11:18">
        <StageWindow stage={stage} lit="bio">
          <FlowNote
            fs={fs}
            look={{hidden, shown, marks, ring: open ? open.id : null, dim: 0}}
            bar={<FloatingBar icons={[...FLOW_ICONS]} on={testing ? 'quiz' : null} hot={frame >= TEST - 8 && frame < TEST ? 'quiz' : null} />}
            under={strip}
          />
        </StageWindow>
        <Cursor clickVolume={CLICK} stops={stops} clicks={[TEST, ...TURNS.map((t) => t.click), TURNS[0].grade]} />
        {TURNS.filter((t) => t.key).map((t) => {
          const p = ease(frame, t.grade - 4, t.grade) * (1 - ease(frame, t.grade + 6, t.grade + 10));
          const at = pointerAt(t.grade);
          return p > 0 ? <Key key={t.id} k={t.key!} p={p} x={at.x + 34} y={at.y + 26} /> : null;
        })}
      </Desktop>
      {TURNS.map((t) => (
        <React.Fragment key={t.id}>
          {t.key ? <Sfx at={t.grade - 1} name="key2" volume={0.35} /> : null}
          {t.knew ? <Sfx at={t.grade + 1} name="ding" volume={0.22} /> : <Sfx at={t.grade + 1} name="pop" volume={0.18} />}
        </React.Fragment>
      ))}
      <Sfx at={TEST + 2} name="whoosh" volume={0.08} />
      <Title text={text2.recall as [string, string]} delay={8} />
    </AbsoluteFill>
  );
};
