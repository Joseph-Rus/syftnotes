import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../../config';
import {ClassDot, Cursor, Desktop, Title} from '../../components/Layout';
import {Group, ListHead} from '../../components/Sidebar';
import {inOut, prog, px} from '../../promo2/scenes/Bookends';
import {useStage} from '../../promo2/ui';
import {cue, titles} from '../config';
import {DemoWindow, LectureRow} from '../chrome';

// Canvas in the library (MacCanvasDue and MacAssignment, the "mac-09-canvas-due-app" shot): Due in the sidebar, what's
// due soonest first with the late one marked Missing, and an assignment opened: when it's due, its points and status,
// the instructions, the rubric, and where it stands.

const RUBRIC: [string, number][] = [
  ['Correct traces', 10],
  ['Stack diagram at the deepest point', 6],
  ['Return values labelled', 4],
];

const Right: React.FC<{children: React.ReactNode; accent?: boolean; lit?: boolean}> = ({children, accent, lit}) => (
  <div style={{fontSize: 14, fontWeight: accent ? 700 : 500, color: lit ? 'rgba(255,255,255,0.88)' : accent ? colors.lagoonBright : colors.text2, whiteSpace: 'nowrap'}}>{children}</div>
);

const DueList: React.FC<{f: number; chosen: boolean}> = ({f, chosen}) => {
  const row = (i: number): React.CSSProperties => {
    const p = prog(f, 4 + i * 4, 18 + i * 4);
    return {opacity: p, transform: `translateY(${px((1 - p) * 10)}px)`};
  };
  return (
    <>
      <ListHead title="Due" sub="3 to hand in · synced 10:24" />
      <Group name="Overdue" accent />
      <LectureRow style={row(0)} title="Lab 2 write-up" meta="Was due Mon 22 Sep, 11:59 PM · BIO 110" color={colors.bio} right={<Right accent>Missing</Right>} />
      <Group name="This week" />
      <LectureRow style={row(1)} title="Quiz 3 practice" meta="Tomorrow, 9:00 AM · CALC II" color={colors.calc} right={<Right>Tomorrow</Right>} />
      <LectureRow style={row(2)} title="Lab 3: recursion traces" meta="Tue 30 Sep, 11:59 PM · CS 101" color={colors.cs} lit={chosen} right={<Right lit={chosen}>In 5 days</Right>} />
      <Group name="Handed in" />
      <LectureRow style={row(3)} title="Osmosis lab report" meta="Submitted Wed 24 Sep · BIO 110" color={colors.bio} right={<Right>Submitted</Right>} />
      <LectureRow style={row(4)} title="Problem set 4" meta="Graded Mon 22 Sep · CS 101" color={colors.cs} right={<Right>18/20</Right>} />
    </>
  );
};

const Detail: React.FC<{f: number; from: number}> = ({f, from}) => {
  const p = prog(f, from + 2, from + 14);
  return (
    <div style={{opacity: p, transform: `translateY(${px((1 - p) * 10)}px)`}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
        <ClassDot color={colors.cs} size={8} /> CS 101 · Assignment
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Lab 3: recursion traces</div>
      <div style={{display: 'flex', marginTop: 18, borderRadius: 16, background: colors.card, border: `1px solid ${colors.edge}`, overflow: 'hidden'}}>
        {[
          ['Due', 'Tue 30 Sep, 11:59 PM'],
          ['Points', '20'],
          ['Status', 'To do'],
        ].map(([k, v], i) => (
          <div key={k} style={{flex: i ? 1 : 1.6, padding: '13px 18px', borderLeft: i ? `1px solid ${colors.line}` : 'none', whiteSpace: 'nowrap'}}>
            <div style={{fontSize: 13.5, color: colors.text2}}>{k}</div>
            <div style={{fontSize: 17.5, fontWeight: 700, marginTop: 3}}>{v}</div>
          </div>
        ))}
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 22}}>Instructions</div>
      <div style={{fontFamily: fonts.serif, fontSize: 18.5, lineHeight: 1.5, marginTop: 6, color: colors.serifInk}}>
        Trace factorial(4) and fib(5) by hand. For each one, draw the call stack at its deepest point and write down what every call returns. Hand in a single PDF.
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 20}}>Rubric</div>
      {RUBRIC.map(([name, pts], i) => {
        const r = prog(f, from + 16 + i * 6, from + 28 + i * 6);
        const n = Math.round(interpolate(r, [0, 1], [0, pts]));
        return (
          <div key={name} style={{opacity: r, display: 'flex', padding: '11px 0', borderTop: `1px solid ${colors.line}`, fontSize: 16.5}}>
            <div style={{flex: 1, fontWeight: 600}}>{name}</div>
            <div style={{color: colors.text2, fontVariantNumeric: 'tabular-nums'}}>{n} pts</div>
          </div>
        );
      })}
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 18}}>Your submission</div>
      <div style={{opacity: prog(f, from + 34, from + 46), marginTop: 10, padding: '15px 18px', borderRadius: 16, background: colors.card, border: `1px solid ${colors.edge}`}}>
        <div style={{fontSize: 16.5, fontWeight: 700}}>
          Nothing handed in yet <span style={{fontWeight: 400, color: colors.text2, marginLeft: 8, fontSize: 15}}>Due in 5 days</span>
        </div>
        <div style={{fontSize: 15.5, color: colors.lagoonBright, marginTop: 5, fontWeight: 600}}>Hand it in on Canvas ↗</div>
      </div>
    </div>
  );
};

export const Canvas: React.FC = () => {
  const f = useCurrentFrame();
  const stage = useStage({list: true});
  const {vertical, page} = stage;
  const enter = prog(f, 0, 18);
  const OPEN = cue('canvas-open');
  const chosen = f >= OPEN;
  const listW = stage.list;
  // Lab 3's row, on the screen: in the list column (landscape) or in the page (phone-shaped).
  const rowY = 62 + 52 + 37 + 66 + 37 + 66 + 33;
  const lab = vertical ? stage.at(page.x + 160, (vertical ? 70 : 62) + rowY - 62) : stage.at(stage.sidebar + listW / 2 - 20, rowY);
  const swap = vertical ? prog(f, OPEN + 2, OPEN + 12, inOut) : 0;
  return (
    <AbsoluteFill>
      <Desktop clock="Thu 25 Sep  10:24">
        <DemoWindow stage={stage} lit="due" tools={chosen ? 'canvas' : 'plain'} list={vertical ? undefined : <DueList f={f} chosen={chosen} />} enter={enter}>
          {vertical ? (
            <>
              {f < OPEN + 12 ? (
                <div style={{position: 'absolute', left: stage.pad - 12, top: 70, width: page.w + 24, opacity: 1 - swap}}>
                  <DueList f={f} chosen={chosen} />
                </div>
              ) : null}
              {f >= OPEN ? (
                <div style={{position: 'absolute', left: stage.pad, top: 70, width: page.w, opacity: swap, transform: `translateX(${px((1 - swap) * 40)}px)`}}>
                  <Detail f={f} from={OPEN} />
                </div>
              ) : null}
            </>
          ) : chosen ? (
            <div style={{position: 'absolute', left: stage.pad, top: 62, width: page.w}}>
              <Detail f={f} from={OPEN} />
            </div>
          ) : null}
        </DemoWindow>
        <Cursor
          softPress
          stops={[
            [12, lab.x + 380, lab.y + 300],
            [OPEN - 6, lab.x, lab.y],
            [OPEN + 20, lab.x, lab.y],
            [OPEN + 70, lab.x + 520, lab.y + 280],
          ]}
          clicks={[OPEN]}
        />
      </Desktop>
      <Title text={titles.canvas!} delay={10} />
    </AbsoluteFill>
  );
};
