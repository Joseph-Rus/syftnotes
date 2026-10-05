import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors} from '../../config';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {prog} from '../../promo2/scenes/Bookends';
import {around, BOXES, box as boxOf, boxAt, EDGES, Flow, FLOW_H, FLOW_W, FlowLook} from '../../promo2/Flow';
import {BarButton, FloatingBar, Grow, H2, PinActions, Serif, Strip, StripIcon, useStage} from '../../promo2/ui';
import {cue, titles} from '../config';
import {DemoWindow} from '../chrome';

// Every diagram is alive (DiagramChrome, as the second video showed it): the pointer rests on "Ventricles contract"
// and it lights up with its arrows and neighbours while the rest dims; a click pins it (Explain this, Quiz me, Where was
// this said?, Zoom in). Step through, then Next. Then Test yourself hides the words; a box is clicked to check it, and
// Knew it marks it: "1 of 8 recalled".

/** Where the cardiac cycle sits with the note scrolled to it, under the toolbar, and where its pieces are on screen. */
export const useFlowStage = () => {
  const stage = useStage({vh: 790});
  const {page, vertical} = stage;
  const top = vertical ? 70 : 66;
  const flowTop = top + 92;
  const s = Math.min(page.w / FLOW_W, (stage.h - flowTop - 90) / FLOW_H, vertical ? 1.1 : 1.3);
  const flowLeft = (page.w - FLOW_W * s) / 2;
  const stripTop = flowTop + FLOW_H * s + 16;
  const onScreen = (x: number, y: number) => stage.at(page.x + flowLeft + x * s, flowTop + y * s);
  const box = (id: string) => {
    const b = boxAt(id);
    return onScreen(b.x, b.y);
  };
  const barLeft = page.w - 112;
  const barTop = flowTop - 46;
  const barIcon = (i: number) => stage.at(page.x + barLeft + 5 + 16 + i * 34, barTop + 5 + 15);
  const stripAt = (x: number) => stage.at(page.x + x, stripTop + 21);
  return {stage, top, flowTop, s, flowLeft, stripTop, onScreen, box, barLeft, barTop, barIcon, stripAt};
};

const ICONS = ['play_circle', 'quiz', 'open_in_full'] as const;

export const Explore: React.FC = () => {
  const f = useCurrentFrame();
  const fs = useFlowStage();
  const {stage} = fs;
  const enter = prog(f, 0, 18);

  const HOVER = 44;
  const PIN = cue('explore-pin');
  const STEPS = cue('explore-steps');
  const NEXT = cue('explore-next');
  const TEST = cue('explore-test');
  const CHECK = cue('explore-check');
  const KNEW = cue('explore-knew');

  const pinned = f >= PIN && f < STEPS;
  const stepping = f >= STEPS && f < TEST;
  const testing = f >= TEST;
  const step = f < NEXT ? 1 : 2;
  const cur = BOXES[step - 1];

  let look: FlowLook = {build: 1};
  if (!stepping && !testing) {
    const lit = around('contract');
    look = {lit, dim: prog(f, HOVER, HOVER + 8), accent: pinned ? lit.edges : [], ring: pinned ? 'contract' : null, ringP: prog(f, PIN, PIN + 6)};
  } else if (stepping) {
    look = {
      lit: {boxes: BOXES.slice(0, step).map((b) => b.id), edges: EDGES.slice(0, Math.max(0, step - 1)).map((e) => e.id)},
      dim: 1,
      accent: step > 1 ? [EDGES[0].id] : [],
      ring: cur.id,
    };
  } else {
    const checked = f >= CHECK;
    look = {
      hidden: prog(f, TEST + 1, TEST + 10),
      shown: checked ? {contract: prog(f, CHECK + 1, CHECK + 8)} : {},
      marks: f >= KNEW ? {contract: {knew: true, p: prog(f, KNEW, KNEW + 8)}} : {},
      ring: checked && f < KNEW + 6 ? 'contract' : null,
      dim: 0,
    };
  }

  const pinBox = boxAt('contract');
  const pinLeft = fs.flowLeft + (pinBox.x - pinBox.w / 2) * fs.s;
  const pinTop = fs.flowTop - fs.top + (pinBox.y + pinBox.h / 2) * fs.s + 10;
  const card = cue('explore-card');
  const explainAt = stage.at(stage.page.x + pinLeft + 60, fs.flowTop + (pinBox.y + pinBox.h / 2) * fs.s + 30);
  const actions = pinned ? (
    <div style={{position: 'absolute', left: pinLeft, top: pinTop, zIndex: 5}}>
      <PinActions p={prog(f, card - 1, card + 7)} hot={f >= card + 14 && f < STEPS - 10 ? 'explain' : undefined} />
    </div>
  ) : null;

  const pressed = (c: number) => f >= c - 2 && f < c + 6;
  const knew = f >= KNEW ? 1 : 0;
  const strip = stepping ? (
    <Strip p={prog(f, STEPS + 1, STEPS + 9)}>
      <StripIcon name="chevron_left" />
      <StripIcon name="chevron_right" hot={pressed(NEXT)} />
      <StripIcon name="play_arrow" />
      <span style={{fontWeight: 700, margin: '0 10px 0 8px'}}>
        Step {step} of {BOXES.length}
      </span>
      <span style={{color: colors.text2, overflow: 'hidden', textOverflow: 'ellipsis'}}>
        {cur.title}: {cur.sub}
      </span>
      <Grow />
      <StripIcon name="close" />
    </Strip>
  ) : testing ? (
    <Strip p={prog(f, TEST + 1, TEST + 9)}>
      <span style={{fontWeight: 700, margin: '0 10px 0 6px', minWidth: 112, flexShrink: 0, fontVariantNumeric: 'tabular-nums'}}>{knew ? `1 of ${BOXES.length} recalled` : 'Test yourself'}</span>
      {f >= CHECK && f < KNEW + 6 ? (
        <>
          <span style={{color: colors.text2}}>“{boxOf('contract').title}”: did you know it?</span>
          <Grow />
          <BarButton icon="check" label="Knew it" hot={f >= KNEW - 8 && f < KNEW + 6} />
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

  const box = fs.box('contract');
  const tip = (p: {x: number; y: number}, dx = 0, dy = 0): [number, number] => [p.x + dx, p.y + dy];
  // The strip's Knew it: its right end, less Not yet and the close button.
  const knewAt = fs.stripAt(stage.page.w - 44 - 86 - 60);
  const nextAt = fs.stripAt(9 + 15 + 34);

  return (
    <AbsoluteFill>
      <Desktop clock="Tue 23 Sep  10:57">
        <DemoWindow stage={stage} lit="bio" counts={{bio: 10}} enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: fs.top, width: stage.page.w, height: stage.h}}>
            <H2>The cycle, step by step</H2>
            <Serif style={{marginTop: 6}}>Every beat runs through these eight steps, then starts again.</Serif>
            <div style={{position: 'absolute', left: fs.flowLeft, top: fs.flowTop - fs.top}}>
              <Flow look={look} scale={fs.s} />
            </div>
            <div style={{position: 'absolute', left: fs.barLeft, top: fs.barTop - fs.top}}>
              <FloatingBar icons={[...ICONS]} p={prog(f, 16, 24)} on={stepping ? 'play_circle' : testing ? 'quiz' : null} hot={pressed(STEPS) ? 'play_circle' : pressed(TEST) ? 'quiz' : null} />
            </div>
            {strip ? <div style={{position: 'absolute', left: 0, right: 0, top: fs.stripTop - fs.top}}>{strip}</div> : null}
            {actions}
          </div>
        </DemoWindow>
        <Cursor
          softPress
          stops={[
            [6, ...tip(box, 260, 230)],
            [HOVER, ...tip(box, 30, 4)],
            [PIN - 6, ...tip(box, 34, 6)],
            [PIN + 6, ...tip(box, 34, 6)],
            [card + 12, ...tip(explainAt, 0, -6)],
            [card + 22, ...tip(explainAt, 0, -6)],
            [STEPS - 6, ...tip(fs.barIcon(0), -4, -6)],
            [STEPS + 6, ...tip(fs.barIcon(0), -4, -6)],
            [NEXT - 6, ...tip(nextAt, -4, -6)],
            [NEXT + 6, ...tip(nextAt, -4, -6)],
            [TEST - 6, ...tip(fs.barIcon(1), -4, -6)],
            [TEST + 4, ...tip(fs.barIcon(1), -4, -6)],
            [CHECK - 5, ...tip(box, 30, 6)],
            [CHECK + 4, ...tip(box, 30, 6)],
            [KNEW - 5, knewAt.x - 4, knewAt.y - 6],
            [KNEW + 6, knewAt.x - 4, knewAt.y - 6],
            [KNEW + 50, knewAt.x - 320, knewAt.y - 250],
          ]}
          clicks={[PIN, STEPS, NEXT, TEST, CHECK, KNEW]}
        />
      </Desktop>
      <Title text={titles.explore!} delay={8} />
    </AbsoluteFill>
  );
};
