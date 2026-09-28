import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {spr} from '../anim';
import {Caption, ClassDot, Cursor, Desktop, useVertical} from '../components/Layout';
import {Group, LibraryWindow, ListHead, ListRow, Sidebar} from '../components/Sidebar';

// Canvas in the library, as in docs/images/canvas-due.png.
const PICK = 40; // "Lab 3" is clicked

const RUBRIC: [string, number][] = [
  ['Correct traces', 10],
  ['Stack diagram at the deepest point', 6],
  ['Return values labelled', 4],
];

const DueIcon = () => (
  <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke={colors.lagoonDeep} strokeWidth="2" strokeLinecap="round">
    <rect x="3.5" y="5" width="17" height="15" rx="3" />
    <path d="M8 3v4M16 3v4M3.5 10h17" />
  </svg>
);

const Detail: React.FC<{frame: number}> = ({frame}) => {
  const f = frame - PICK;
  const p = spr(f, 2, {damping: 26, stiffness: 150, mass: 0.9});
  return (
    <div style={{padding: '34px 40px 0', opacity: p, transform: `translateY(${(1 - p) * 10}px)`}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
        <ClassDot color={colors.cs} size={8} /> CS 101 · Assignment
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 36, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Lab 3: recursion traces</div>
      <div style={{display: 'flex', marginTop: 20, borderRadius: 16, background: 'rgba(14,149,148,0.08)', overflow: 'hidden'}}>
        {[
          ['Due', '30 Sep, 11:59 PM'],
          ['Points', '20'],
          ['Status', 'To do'],
        ].map(([k, v], i) => (
          <div key={k} style={{flex: i ? 1 : 1.6, padding: '13px 18px', borderLeft: i ? '1px solid rgba(255,255,255,0.9)' : 'none', whiteSpace: 'nowrap'}}>
            <div style={{fontSize: 13.5, color: colors.text2}}>{k}</div>
            <div style={{fontSize: 17.5, fontWeight: 600, marginTop: 3}}>{v}</div>
          </div>
        ))}
      </div>
      <div style={{fontSize: 19, fontWeight: 700, marginTop: 24}}>Instructions</div>
      <div style={{fontFamily: fonts.serif, fontSize: 19, lineHeight: 1.5, marginTop: 6, color: '#2B3342'}}>
        Trace factorial(4) and fib(5) by hand. Draw the call stack at its deepest point.
      </div>
      <div style={{fontSize: 19, fontWeight: 700, marginTop: 22}}>Rubric</div>
      {RUBRIC.map(([name, pts], i) => {
        const r = spr(f, 18 + i * 7, {damping: 24, stiffness: 150, mass: 0.9});
        const shown = Math.round(interpolate(r, [0, 1], [0, pts], {extrapolateRight: 'clamp'}));
        return (
          <div key={name} style={{opacity: r, display: 'flex', padding: '12px 0', borderTop: `1px solid ${colors.line}`, fontSize: 16.5}}>
            <div style={{flex: 1, fontWeight: 500}}>{name}</div>
            <div style={{color: colors.text2, fontVariantNumeric: 'tabular-nums'}}>{shown} pts</div>
          </div>
        );
      })}
      <div style={{opacity: spr(f, 44), marginTop: 18, padding: '15px 18px', borderRadius: 16, background: 'rgba(16,24,40,0.045)'}}>
        <div style={{fontSize: 16.5, fontWeight: 600}}>
          Nothing handed in yet <span style={{fontWeight: 400, color: colors.text2, marginLeft: 8}}>Due in 5 days</span>
        </div>
        <div style={{fontSize: 15.5, color: colors.lagoonDeep, marginTop: 5, fontWeight: 600}}>Hand it in on Canvas ↗</div>
      </div>
    </div>
  );
};

export const CanvasDue: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  // On a phone-shaped frame: the Due list and the assignment, laid out as on a laptop and shown 1.2 times as big.
  const z = vertical ? 1.2 : 1;
  const w = vertical ? 820 : 1320;
  const h = vertical ? 820 : 800;
  const left = (width - w * z) / 2;
  const top = vertical ? 250 : 66;
  const win = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const chosen = frame >= PICK;
  const row = (i: number) => spr(frame, 6 + i * 5, {damping: 24, stiffness: 150, mass: 0.9});
  const rowStyle = (i: number): React.CSSProperties => ({opacity: row(i), transform: `translateY(${(1 - row(i)) * 10}px)`});
  const listW = vertical ? 300 : 310;
  const list = (
    <>
      <ListHead title="Due" sub="3 to hand in · synced 10:24" />
      <Group name="Overdue" />
      <ListRow style={rowStyle(0)} title="Reading response" sub="HIST 210 · Was due Mon" color={colors.hist} right="Missing" accent />
      <Group name="This week" />
      <ListRow style={rowStyle(1)} title="Quiz 3 practice" sub="CALC II · Tomorrow, 9:00 AM" color={colors.calc} right="To do" />
      <ListRow style={rowStyle(2)} title="Lab 3: recursion traces" sub="CS 101 · Tue 30 Sep" color={colors.cs} right="To do" lit={chosen} />
      <Group name="Handed in" />
      <ListRow style={rowStyle(3)} title="Problem set 4" sub="CS 101 · Graded Mon" color={colors.cs} right="18/20" />
    </>
  );
  const sidebar = vertical ? undefined : (
    <Sidebar
      footer="Canvas synced 10:24"
      top={
        <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '10px 12px', marginBottom: 10, borderRadius: 11, background: 'rgba(16,24,40,0.065)', fontSize: 17, fontWeight: 600}}>
          <DueIcon />
          <div style={{flex: 1}}>Due</div>
          <div style={{color: colors.text2, fontWeight: 400}}>3</div>
        </div>
      }
      rows={[
        {name: 'CS 101', color: colors.cs, count: 12},
        {name: 'BIO 110', color: colors.bio, count: 9},
        {name: 'CALC II', color: colors.calc, count: 11},
        {name: 'NURS 210', color: colors.health, count: 8},
      ]}
    />
  );
  // Lab 3's row in the list column.
  const lab = {x: left + ((vertical ? 0 : 252) + 150) * z, y: top + (20 + 60 + 34 + 64 + 34 + 64 + 30) * z};
  return (
    <AbsoluteFill>
      <Desktop>
        <div style={{position: 'absolute', left: left / z, top: top / z, zoom: z, opacity: win, transform: `translateY(${(1 - win) * 30}px) scale(${0.98 + 0.02 * win})`}}>
          <LibraryWindow w={w} h={h} sidebar={sidebar} list={list} listWidth={listW}>
            {chosen ? <Detail frame={frame} /> : null}
          </LibraryWindow>
        </div>
        <Cursor
          stops={[
            [12, lab.x + 380, lab.y + 300],
            [PICK - 4, lab.x, lab.y],
            [PICK + 30, lab.x, lab.y],
            [PICK + 70, lab.x + 520, lab.y + 280],
          ]}
          clicks={[PICK]}
        />
      </Desktop>
      <Caption text={text.canvas} delay={10} />
    </AbsoluteFill>
  );
};
