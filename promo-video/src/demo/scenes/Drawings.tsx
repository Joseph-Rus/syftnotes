import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {prog} from '../../promo2/scenes/Bookends';
import {ArtName, artSize, Drawing, PartCard} from '../../promo2/Parts';
import {FloatingBar, H2, Serif, useStage} from '../../promo2/ui';
import {Cut, cueIn, titles} from '../config';
import {ClassId, DemoWindow} from '../chrome';

// The app's own labelled drawings (the second video's drone and hand, art2/svg): ENGR 120's quadcopter; the pointer
// passes the battery and rests on the flight controller, which lights with a ring while the rest dims, and a click pins
// it (its card: what it is, Explain this, Quiz me, Where was this said?, Zoom in). Then NURS 210's hand, its deep
// flexor tendon lit the same way.

// `lead`: how long before resting on its part the pointer passes the other one (`pass`).
type Turn = {art: ArtName; cls: ClassId; title: string; line: string; pass?: [string, number, number]; part: [string, number, number]; hover: number; pin?: number; lead?: number};

const useDrawingStage = (turn: Turn) => {
  const stage = useStage();
  const {page, vertical, h} = stage;
  const top = vertical ? 70 : 66;
  const {w: aw, h: ah} = artSize(turn.art);
  const below = top + (vertical ? 128 : 84);
  const s = Math.min(page.w / aw, (h - below - 24) / ah, vertical ? 1.2 : 1.3);
  const figTop = below + (vertical ? Math.max(0, (h - below - 24 - ah * s) / 2 - 90) : 0);
  const figLeft = (page.w - aw * s) / 2;
  const onScreen = (x: number, y: number) => stage.at(page.x + figLeft + x * s, figTop + y * s);
  return {stage, top, figTop, s, figLeft, onScreen};
};

const Figure: React.FC<{turn: Turn; f: number; card: number}> = ({turn, f, card}) => {
  const ds = useDrawingStage(turn);
  const {stage, s} = ds;
  const [part, px, py] = turn.part;
  const lead = turn.lead ?? 22;
  const hot = f >= turn.hover ? part : turn.pass && f >= turn.hover - lead ? turn.pass[0] : null;
  const dim = turn.pass ? prog(f, turn.hover - lead, turn.hover - lead + 6) : prog(f, turn.hover, turn.hover + 6);
  const ring = hot === part ? prog(f, turn.hover, turn.hover + 6) : prog(f, turn.hover - lead, turn.hover - lead + 6);
  const pinned = turn.pin !== undefined && f >= turn.pin;
  const cardW = 420;
  const cardLeft = Math.max(0, Math.min(stage.page.w - cardW, ds.figLeft + px * s - 60));
  const cardTop = ds.figTop - ds.top + py * s + 26;
  return (
    <div style={{position: 'absolute', left: stage.pad, top: ds.top, width: stage.page.w, height: stage.h}}>
      <H2>{turn.title}</H2>
      <Serif style={{marginTop: 6}}>{turn.line}</Serif>
      <div style={{position: 'absolute', left: ds.figLeft, top: ds.figTop - ds.top}}>
        <Drawing name={turn.art} scale={s} look={{hot, dim, ring}} halo="app" />
      </div>
      <div style={{position: 'absolute', right: 0, top: ds.figTop - ds.top - 50}}>
        <FloatingBar icons={['subject', 'play_circle', 'quiz', 'open_in_full']} p={prog(f, 14, 22)} />
      </div>
      {pinned ? (
        <div style={{position: 'absolute', left: cardLeft, top: cardTop}}>
          <PartCard name={turn.art} part={part} p={prog(f, card - 1, card + 8)} width={cardW} />
        </div>
      ) : null}
    </div>
  );
};

// The reel's: the drone alone, its battery passed and its flight controller pinned, a little quicker.
export const Drawings: React.FC<{cut?: Cut}> = ({cut}) => {
  const frame = useCurrentFrame();
  const enter = prog(frame, 0, cut ? 14 : 18);
  const PIN = cueIn(cut, 'drawings-pin');
  const CARD = cueIn(cut, 'drawings-card');
  const SWITCH = cut ? 100000 : PIN + 60; // the hand's note (never, in the reel)
  const lead = cut ? 14 : 22;
  const DRONE: Turn = {
    art: 'drone', cls: 'engr', title: 'The quadcopter, side on', line: 'Every part the lecture named, where it sits on the frame.',
    pass: ['battery', 326, 146], part: ['flight-controller', 368, 182], hover: PIN - (cut ? 14 : 24), pin: PIN, lead,
  };
  const HAND: Turn = {
    art: 'hand', cls: 'nurs', title: 'The hand, palm up', line: 'The bones of the wrist and fingers, and the tendons that bend them.',
    part: ['fdp-tendon', 236, 236], hover: SWITCH + 30,
  };
  const second = frame >= SWITCH;
  const turn = second ? HAND : DRONE;
  const drone = useDrawingStage(DRONE);
  const hand = useDrawingStage(HAND);
  const {stage} = drone;
  const swap = prog(frame, SWITCH - 6, SWITCH + 6);
  const d = (ds: typeof drone, x: number, y: number, dx = 0, dy = 0): [number, number] => {
    const p = ds.onScreen(x, y);
    return [p.x + dx, p.y + dy];
  };
  return (
    <AbsoluteFill>
      <Desktop clock="Thu 25 Sep  14:05">
        <DemoWindow stage={stage} lit={turn.cls} enter={enter}>
          {frame < SWITCH + 6 ? (
            <div style={{opacity: 1 - swap}}>
              <Figure turn={DRONE} f={frame} card={CARD} />
            </div>
          ) : null}
          {frame >= SWITCH - 6 ? (
            <div style={{opacity: swap}}>
              <Figure turn={HAND} f={frame} card={CARD} />
            </div>
          ) : null}
        </DemoWindow>
        <Cursor
          softPress
          stops={[
            [cut ? 2 : 8, ...d(drone, 600, 330)],
            [DRONE.hover - (cut ? 16 : 24), ...d(drone, DRONE.pass![1], DRONE.pass![2], 2, 2)],
            [DRONE.hover - (cut ? 4 : 6), ...d(drone, DRONE.pass![1], DRONE.pass![2], 2, 2)],
            [DRONE.hover, ...d(drone, DRONE.part[1], DRONE.part[2], 2, 2)],
            [PIN + 30, ...d(drone, DRONE.part[1], DRONE.part[2], 2, 2)],
            ...(cut
              ? []
              : ([
                  [SWITCH + 6, ...d(hand, 470, 420)],
                  [HAND.hover, ...d(hand, HAND.part[1], HAND.part[2], 2, 2)],
                  [HAND.hover + 60, ...d(hand, HAND.part[1], HAND.part[2], 2, 2)],
                ] as [number, number, number][])),
          ]}
          clicks={[PIN]}
        />
      </Desktop>
      {cut ? null : <Title text={titles.drawings!} delay={8} />}
    </AbsoluteFill>
  );
};
