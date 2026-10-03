import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame} from 'remotion';
import {ease, spr} from '../../anim';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';
import {DESCENT, DescentPlot, knobX, SIGMOID, SigmoidPlot, SLIDER, SliderRow} from '../Plots';
import {FloatingBar, H2, Serif, StageWindow, useStage} from '../ui';

// Formulas that move: CS 340's notes on logistic regression. The steepness slider is dragged from 1 to 5 and the
// sigmoid sharpens into a step; the page scrolls to gradient descent, whose learning rate is dragged down to 0.05
// (a smooth path) and up to 0.21, where the path zigzags and then flies off the bowl (it diverges past 0.2).
// Every curve and path is computed for the slider's value at that frame.

const SCROLL: [number, number] = [100, 118];
// Each slider's knob over time: [frame, value]. The pointer holds the knob, so it moves exactly with it.
const K: [number, number][] = [
  [36, SIGMOID.k.start],
  [92, 5],
];
const ETA: [number, number][] = [
  [128, DESCENT.eta.start],
  [148, 0.05],
  [156, 0.05],
  [198, 0.21],
];
const curve = Easing.bezier(0.45, 0, 0.2, 1); // the pointer's own ease (Cursor)
const valueAt = (f: number, keys: [number, number][]) =>
  interpolate(f, keys.map((k) => k[0]), keys.map((k) => k[1]), {easing: curve, extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
const posK = (k: number) => (k - SIGMOID.k.from) / (SIGMOID.k.to - SIGMOID.k.from);
const posEta = (e: number) => (e - DESCENT.eta.from) / (DESCENT.eta.to - DESCENT.eta.from);

export const Plots: React.FC = () => {
  const frame = useCurrentFrame();
  const stage = useStage({vh: 790});
  const {page, vertical, h} = stage;
  const enter = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const top = vertical ? 64 : 26;
  const s = Math.min(page.w / 620, vertical ? 1.2 : 1.4, (h - top - 150) / 430);
  const plotLeft = (page.w - 620 * s) / 2;
  const rowW = 620 * s;
  const lines = vertical ? 112 : 84; // the heading and a line of notes (two on a phone)
  const plot1 = top + lines;
  const slider1 = plot1 + 410 * s + 8;
  const sec2 = slider1 + SLIDER.h + (vertical ? 96 : 44); // far enough that the first slider scrolls right out
  const plot2 = sec2 + lines;
  const slider2 = plot2 + 430 * s + 8;
  const scrollTo = sec2 - top;
  const scroll = interpolate(frame, SCROLL, [0, scrollTo], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});

  const k = valueAt(frame, K);
  const eta = valueAt(frame, ETA);
  const holdingK = frame >= K[0][0] - 2 && frame <= K[K.length - 1][0] + 2;
  const holdingEta = frame >= ETA[0][0] - 2 && frame <= ETA[ETA.length - 1][0] + 2;

  // The knobs on the screen.
  const knob = (row: number, pos: number, scrolled: number) => stage.at(page.x + plotLeft + knobX(rowW, pos), row + SLIDER.h / 2 - scrolled);
  const k0 = knob(slider1, posK(SIGMOID.k.start), 0);
  const k1 = knob(slider1, posK(5), 0);
  const e = (v: number) => knob(slider2, posEta(v), scrollTo);
  const stops: [number, number, number][] = [
    [6, k0.x + 300, k0.y - 260],
    [K[0][0] - 8, k0.x, k0.y],
    ...K.map(([f, v]) => [f, knob(slider1, posK(v), 0).x, k0.y] as [number, number, number]),
    [K[1][0] + 8, k1.x, k1.y],
    [SCROLL[1], k1.x - 60, k1.y - 120],
    [ETA[0][0] - 8, e(DESCENT.eta.start).x, e(DESCENT.eta.start).y],
    ...ETA.map(([f, v]) => [f, e(v).x, e(v).y] as [number, number, number]),
    [ETA[ETA.length - 1][0] + 30, e(0.21).x + 40, e(0.21).y - 90],
  ];
  // A soft tick as the value moves, every few frames of each drag.
  const ticks: number[] = [];
  for (const keys of [K, ETA])
    for (let i = 1; i < keys.length; i++)
      if (keys[i][1] !== keys[i - 1][1]) for (let f = keys[i - 1][0] + 2; f < keys[i][0] - 1; f += 4) ticks.push(f);

  const bar = (p: number) => <FloatingBar icons={['edit', 'chat', 'schedule', 'open_in_full']} p={p} />;
  return (
    <AbsoluteFill>
      <Desktop clock="Mon 29 Sep  09:52">
        <StageWindow stage={stage} lit="ml" enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: 0, width: page.w, transform: `translateY(${-scroll}px)`}}>
            <div style={{position: 'absolute', top, left: 0, right: 0}}>
              <H2>The sigmoid</H2>
              <Serif style={{marginTop: 6}}>Drag the steepness: a larger k makes the step from 0 to 1 sharper, and σ(0) stays 0.5.</Serif>
            </div>
            <div style={{position: 'absolute', top: plot1, left: plotLeft}}>
              <SigmoidPlot k={k} scale={s} />
            </div>
            <div style={{position: 'absolute', top: plot1 - 6, right: 0}}>{bar(ease(frame, 14, 22) * (1 - ease(frame, SCROLL[0], SCROLL[0] + 6)))}</div>
            <div style={{position: 'absolute', top: slider1, left: plotLeft}}>
              <SliderRow label="steepness k" pos={posK(k)} value={k} width={rowW} held={holdingK} />
            </div>
            <div style={{position: 'absolute', top: sec2, left: 0, right: 0}}>
              <H2>Gradient descent</H2>
              <Serif style={{marginTop: 6}}>With η a little too large the path zigzags across the narrow direction; past 0.2 it diverges.</Serif>
            </div>
            <div style={{position: 'absolute', top: plot2, left: plotLeft}}>
              <DescentPlot eta={eta} scale={s} />
            </div>
            <div style={{position: 'absolute', top: plot2 - 6, right: 0}}>{bar(ease(frame, SCROLL[1] - 4, SCROLL[1] + 4))}</div>
            <div style={{position: 'absolute', top: slider2, left: plotLeft}}>
              <SliderRow label="learning rate η" pos={posEta(eta)} value={eta} width={rowW} held={holdingEta} />
            </div>
          </div>
        </StageWindow>
        <Cursor stops={stops} clicks={[K[0][0] - 2, ETA[0][0] - 2]} />
      </Desktop>
      {ticks.map((f) => (
        <Sfx key={f} at={f} name="tick" volume={0.16} />
      ))}
      <Sfx at={SCROLL[0]} name="whoosh" volume={0.08} />
      <Title text={text2.plots as [string, string]} delay={8} />
    </AbsoluteFill>
  );
};
