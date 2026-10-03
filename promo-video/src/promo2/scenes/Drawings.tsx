import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';

import {ease, spr} from '../../anim';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';
import {ArtName, artSize, Drawing, PartCard} from '../Parts';
import {ClassKey, FloatingBar, H2, Serif, StageWindow, useStage} from '../ui';

// Drawings of things: ENGR 120's quadcopter and NURS 210's hand, the app's own labelled illustrations. The pointer
// passes over the battery and rests on the flight controller (it lights with a ring, the rest dims), and a click pins
// it: its card says what it is, with Explain this, Quiz me, Where was this said? and Zoom in. Then the hand's deep
// flexor tendon, the same way.
const SWITCH = 118; // the hand's note
type Turn = {art: ArtName; cls: ClassKey; title: string; line: string; pass?: [string, number, number]; part: [string, number, number]; hover: number; pin: number};
const DRONE: Turn = {
  art: 'drone', cls: 'engr', title: 'The quadcopter, side on', line: 'Every part the lecture named, where it sits on the frame.',
  pass: ['battery', 326, 146], part: ['flight-controller', 368, 182], hover: 40, pin: 60,
};
const HAND: Turn = {
  art: 'hand', cls: 'nurs', title: 'The hand, palm up', line: 'The bones of the wrist and fingers, and the tendons that bend them.',
  part: ['fdp-tendon', 236, 236], hover: SWITCH + 34, pin: SWITCH + 52,
};

const useDrawingStage = (turn: Turn) => {
  const stage = useStage();
  const {page, vertical, h} = stage;
  const top = vertical ? 64 : 26;
  const {w: aw, h: ah} = artSize(turn.art);
  // On a phone the toolbar gets a line of its own above the figure, and a wide figure sits in the middle of the page.
  const below = top + (vertical ? 128 : 84);
  const s = Math.min(page.w / aw, (h - below - 24) / ah, vertical ? 1.2 : 1.36);
  const figTop = below + (vertical ? Math.max(0, (h - below - 24 - ah * s) / 2 - 90) : 0);
  const figLeft = (page.w - aw * s) / 2;
  const onScreen = (x: number, y: number) => stage.at(page.x + figLeft + x * s, figTop + y * s);
  return {stage, top, figTop, s, figLeft, onScreen};
};

const Figure: React.FC<{turn: Turn; f: number}> = ({turn, f}) => {
  const ds = useDrawingStage(turn);
  const {stage, s} = ds;
  const [part, px, py] = turn.part;
  const hot = f >= turn.hover ? part : turn.pass && f >= turn.hover - 18 ? turn.pass[0] : null;
  const dim = turn.pass ? ease(f, turn.hover - 18, turn.hover - 12) : ease(f, turn.hover, turn.hover + 6);
  const ring = hot === part ? ease(f, turn.hover, turn.hover + 6) : ease(f, turn.hover - 18, turn.hover - 12);
  const pinned = f >= turn.pin;
  const cardW = 420;
  const cardLeft = Math.max(0, Math.min(stage.page.w - cardW, ds.figLeft + px * s - 60));
  const cardTop = ds.figTop - ds.top + py * s + 26;
  return (
    <div style={{position: 'absolute', left: stage.pad, top: ds.top, width: stage.page.w, height: stage.h}}>
      <H2>{turn.title}</H2>
      <Serif style={{marginTop: 6}}>{turn.line}</Serif>
      <div style={{position: 'absolute', left: ds.figLeft, top: ds.figTop - ds.top}}>
        <Drawing name={turn.art} scale={s} look={{hot, dim, ring}} />
      </div>
      <div style={{position: 'absolute', right: 0, top: ds.figTop - ds.top - 50}}>
        <FloatingBar icons={['subject', 'play_circle', 'quiz', 'open_in_full']} p={ease(f, 14, 22)} />
      </div>
      {pinned ? (
        <div style={{position: 'absolute', left: cardLeft, top: cardTop}}>
          <PartCard name={turn.art} part={part} p={spr(f, turn.pin + 1, {damping: 20, stiffness: 200, mass: 0.7})} width={cardW} />
        </div>
      ) : null}
    </div>
  );
};

export const Drawings: React.FC = () => {
  const frame = useCurrentFrame();
  const enter = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const second = frame >= SWITCH;
  const turn = second ? HAND : DRONE;
  const drone = useDrawingStage(DRONE);
  const hand = useDrawingStage(HAND);
  const {stage} = drone;
  const swap = ease(frame, SWITCH - 6, SWITCH + 6);
  const d = (t: Turn, ds: typeof drone, x: number, y: number, dx = 0, dy = 0): [number, number] => {
    const p = ds.onScreen(x, y);
    return [p.x + dx, p.y + dy];
  };
  return (
    <AbsoluteFill>
      <Desktop clock="Thu 25 Sep  14:05">
        <StageWindow stage={stage} lit={turn.cls} enter={enter}>
          {frame < SWITCH + 6 ? (
            <div style={{opacity: 1 - swap}}>
              <Figure turn={DRONE} f={frame} />
            </div>
          ) : null}
          {frame >= SWITCH - 6 ? (
            <div style={{opacity: swap}}>
              <Figure turn={HAND} f={frame} />
            </div>
          ) : null}
        </StageWindow>
        <Cursor
          stops={[
            [8, ...d(DRONE, drone, 600, 330)],
            [DRONE.hover - 20, ...d(DRONE, drone, DRONE.pass![1], DRONE.pass![2], 2, 2)],
            [DRONE.hover - 4, ...d(DRONE, drone, DRONE.pass![1], DRONE.pass![2], 2, 2)],
            [DRONE.hover, ...d(DRONE, drone, DRONE.part[1], DRONE.part[2], 2, 2)],
            [DRONE.pin + 30, ...d(DRONE, drone, DRONE.part[1], DRONE.part[2], 2, 2)],
            [SWITCH + 6, ...d(HAND, hand, 470, 420)],
            [HAND.hover, ...d(HAND, hand, HAND.part[1], HAND.part[2], 2, 2)],
            [HAND.pin + 30, ...d(HAND, hand, HAND.part[1], HAND.part[2], 2, 2)],
          ]}
          clicks={[DRONE.pin, HAND.pin]}
        />
      </Desktop>
      <Sfx at={DRONE.hover - 18} name="tick" volume={0.22} />
      <Sfx at={DRONE.hover} name="tick" volume={0.25} />
      <Sfx at={DRONE.pin + 2} name="pop" volume={0.24} />
      <Sfx at={SWITCH - 4} name="whoosh" volume={0.1} />
      <Sfx at={HAND.hover} name="tick" volume={0.25} />
      <Sfx at={HAND.pin + 2} name="pop" volume={0.24} />
      <Title text={text2.drawings as [string, string]} delay={8} />
    </AbsoluteFill>
  );
};

