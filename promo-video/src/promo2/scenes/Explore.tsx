import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors} from '../../config';
import {ease, spr} from '../../anim';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {CLICK, text2} from '../config';
import {settle} from '../settle';
import {around, BOXES, boxAt, EDGES, Flow, FLOW_H, FLOW_W, FlowLook} from '../Flow';
import {FloatingBar, Grow, H2, PinActions, Serif, StageWindow, Strip, StripIcon, useStage} from '../ui';

/** Where the cardiac cycle sits when the note is scrolled to it, and where its pieces are on the screen. */
export const useFlowStage = () => {
  const stage = useStage({vh: 770});
  const {page, vertical} = stage;
  const top = vertical ? 64 : 26; // the section's heading, in the page
  const flowTop = top + 92;
  const s = Math.min(page.w / FLOW_W, (stage.h - flowTop - 90) / FLOW_H, vertical ? 1.1 : 1.34);
  const flowLeft = (page.w - FLOW_W * s) / 2;
  const stripTop = flowTop + FLOW_H * s + 16;
  /** A point of the diagram (its own units), on the screen. */
  const onScreen = (x: number, y: number) => stage.at(page.x + flowLeft + x * s, flowTop + y * s);
  const box = (id: string) => {
    const b = boxAt(id);
    return onScreen(b.x, b.y);
  };
  const barLeft = page.w - 112; // the toolbar, over the figure's top right
  const barTop = flowTop - 46;
  const barIcon = (i: number) => stage.at(page.x + barLeft + 5 + 16 + i * 34, barTop + 5 + 15);
  const stripIcon = (i: number) => stage.at(page.x + 9 + 15 + i * 34, stripTop + 21);
  return {stage, top, flowTop, s, flowLeft, stripTop, onScreen, box, barLeft, barTop, barIcon, stripIcon};
};

/** The note, scrolled to its diagram: the section heading, a line of the notes, the diagram, and whatever is under it. */
export const FlowNote: React.FC<{fs: ReturnType<typeof useFlowStage>; look: FlowLook; bar?: React.ReactNode; under?: React.ReactNode; over?: React.ReactNode}> = ({fs, look, bar, under, over}) => (
  <div style={{position: 'absolute', left: fs.stage.pad, top: fs.top, width: fs.stage.page.w, height: fs.stage.h}}>
    <H2>The cycle, step by step</H2>
    <Serif style={{marginTop: 6}}>Every beat runs through these eight steps, then starts again.</Serif>
    <div style={{position: 'absolute', left: fs.flowLeft, top: fs.flowTop - fs.top}}>
      <Flow look={look} scale={fs.s} />
    </div>
    {bar ? <div style={{position: 'absolute', left: fs.barLeft, top: fs.barTop - fs.top}}>{bar}</div> : null}
    {under ? <div style={{position: 'absolute', left: 0, right: 0, top: fs.stripTop - fs.top}}>{under}</div> : null}
    {over}
  </div>
);

export const FLOW_ICONS = ['play_circle', 'quiz', 'open_in_full'] as const;

// The pointer's story: it comes to rest on a box (it lights up with its arrows and neighbours, the rest dims), clicks
// to pin it (its arrows go Lagoon; Explain this, Quiz me, Where was this said? and Zoom in appear), then steps
// through the cycle: Step through, Next twice, and Play.
const HOVER = 28;
const PIN = 76;
const STEPS = 112;
const NEXT1 = 130;
const NEXT2 = 146;
const PLAY = 162; // then a step on every beat: the scene starts on one, so 180, 198, …
const EVERY = 18; // playing, a step a beat

export const Explore: React.FC = () => {
  const frame = useCurrentFrame();
  const fs = useFlowStage();
  const {stage} = fs;
  const enter = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});

  const pinned = frame >= PIN && frame < STEPS;
  const stepping = frame >= STEPS;
  const step = !stepping ? 0 : frame < NEXT1 ? 1 : frame < NEXT2 ? 2 : frame < PLAY + EVERY ? 3 : Math.min(8, 3 + Math.floor((frame - PLAY) / EVERY));
  const playing = frame >= PLAY;
  const cur = BOXES[step - 1];

  let look: FlowLook = {build: 1};
  if (!stepping) {
    const lit = around('contract');
    look = {
      lit,
      dim: ease(frame, HOVER, HOVER + 8),
      accent: pinned ? lit.edges : [],
      ring: pinned ? 'contract' : null,
      ringP: ease(frame, PIN, PIN + 6),
    };
  } else {
    const into = EDGES[(step - 2 + EDGES.length) % EDGES.length];
    look = {
      lit: {boxes: BOXES.slice(0, step).map((b) => b.id), edges: [...EDGES.slice(0, Math.max(0, step - 1)).map((e) => e.id)]},
      dim: 1,
      accent: step > 1 ? [into.id] : [],
      ring: cur.id,
    };
  }

  const pinBox = boxAt('contract');
  const pinLeft = fs.flowLeft + (pinBox.x - pinBox.w / 2) * fs.s;
  const pinTop = fs.flowTop - fs.top + (pinBox.y + pinBox.h / 2) * fs.s + 10;
  const actions = pinned ? (
    <div style={{position: 'absolute', left: pinLeft, top: pinTop, zIndex: 5}}>
      <PinActions p={settle(frame, PIN + 1, {damping: 20, stiffness: 200, mass: 0.7})} hot={frame >= 92 && frame < 106 ? 'explain' : undefined} />
    </div>
  ) : null;

  const strip = stepping ? (
    <Strip p={settle(frame, STEPS + 1, {damping: 22, stiffness: 180, mass: 0.8})}>
      <StripIcon name="chevron_left" />
      <StripIcon name="chevron_right" hot={(frame >= NEXT1 - 2 && frame < NEXT1 + 6) || (frame >= NEXT2 - 2 && frame < NEXT2 + 6)} />
      <StripIcon name={playing ? 'pause' : 'play_arrow'} hot={frame >= PLAY - 2 && frame < PLAY + 6} />
      <span style={{fontWeight: 700, margin: '0 10px 0 8px'}}>
        Step {step} of {BOXES.length}
      </span>
      <span style={{color: colors.text2, overflow: 'hidden', textOverflow: 'ellipsis'}}>
        {cur.title}: {cur.sub}
      </span>
      <Grow />
      <StripIcon name="close" />
    </Strip>
  ) : null;

  const box = fs.box('contract');
  const tip = (p: {x: number; y: number}, dx = 0, dy = 0): [number, number] => [p.x + dx, p.y + dy];
  const explainAt = stage.at(stage.page.x + pinLeft + 60, fs.flowTop + (pinBox.y + pinBox.h / 2) * fs.s + 10 + 20);
  return (
    <AbsoluteFill>
      <Desktop clock="Tue 23 Sep  11:17">
        <StageWindow stage={stage} lit="bio" enter={enter}>
          <FlowNote
            fs={fs}
            look={look}
            bar={<FloatingBar icons={[...FLOW_ICONS]} p={ease(frame, 16, 24)} on={stepping ? 'play_circle' : null} hot={frame >= STEPS - 10 && frame < STEPS ? 'play_circle' : null} />}
            under={strip}
            over={actions}
          />
        </StageWindow>
        <Cursor softPress clickVolume={CLICK}
          stops={[
            [6, ...tip(box, 260, 230)],
            [HOVER, ...tip(box, 30, 4)],
            [PIN - 6, ...tip(box, 34, 6)],
            [PIN + 8, ...tip(box, 34, 6)],
            [92, ...tip(explainAt, 0, -6)],
            [102, ...tip(explainAt, 0, -6)],
            [STEPS - 6, ...tip(fs.barIcon(0), -4, -6)],
            [STEPS + 6, ...tip(fs.barIcon(0), -4, -6)],
            [NEXT1 - 6, ...tip(fs.stripIcon(1), -4, -6)],
            [NEXT2 + 6, ...tip(fs.stripIcon(1), -4, -6)],
            [PLAY - 4, ...tip(fs.stripIcon(2), -4, -6)],
            [PLAY + 14, ...tip(fs.stripIcon(2), -4, -6)],
            [PLAY + 40, ...tip(fs.stripIcon(2), 120, 90)],
          ]}
          clicks={[PIN, STEPS, NEXT1, NEXT2, PLAY]}
        />
      </Desktop>
      <Sfx at={PIN + 2} name="pop" volume={0.24} />
      <Title text={text2.explore as [string, string]} delay={8} />
    </AbsoluteFill>
  );
};
