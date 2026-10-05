import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame} from 'remotion';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {prog} from '../../promo2/scenes/Bookends';
import {DESCENT, DescentPlot, knobX, SIGMOID, SigmoidPlot, SLIDER, SliderRow} from '../../promo2/Plots';
import {FloatingBar, H2, Serif, useStage} from '../../promo2/ui';
import {cue, cueLength, titles} from '../config';
import {DemoWindow} from '../chrome';

// Formulas you can drag: CS 340's notes on logistic regression, the second video's two plots (Core/Rich/PlotDesign.cs,
// recomputed every frame for the slider's value). The steepness is dragged from 1 to 5 and the sigmoid sharpens into a
// step; the page scrolls to gradient descent, whose learning rate is dragged down to 0.05 (a smooth path) and up to
// 0.21, where the path zigzags and flies off the bowl (it diverges past 0.2).

const curve = Easing.bezier(0.45, 0, 0.2, 1); // the pointer's own ease (Cursor)
const valueAt = (f: number, keys: [number, number][]) =>
  interpolate(f, keys.map((k) => k[0]), keys.map((k) => k[1]), {easing: curve, extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
const posK = (k: number) => (k - SIGMOID.k.from) / (SIGMOID.k.to - SIGMOID.k.from);
const posEta = (e: number) => (e - DESCENT.eta.from) / (DESCENT.eta.to - DESCENT.eta.from);

export const Plots: React.FC = () => {
  const frame = useCurrentFrame();
  const stage = useStage({vh: 790});
  const {page, vertical, h} = stage;
  const enter = prog(frame, 0, 18);

  const K0 = cue('plots-k');
  const K1 = K0 + cueLength('plots-k');
  const E0 = cue('plots-eta');
  const E1 = E0 + cueLength('plots-eta');
  const SCROLL: [number, number] = [K1 + 18, K1 + 38];
  // Each slider's knob over time: [frame, value]. The pointer holds the knob, so it moves exactly with it.
  const K: [number, number][] = [
    [K0, SIGMOID.k.start],
    [K1, 5],
  ];
  const ETA: [number, number][] = [
    [E0, DESCENT.eta.start],
    [E0 + 18, 0.05],
    [E0 + 24, 0.05],
    [E1, 0.21],
  ];

  const top = vertical ? 70 : 66;
  const s = Math.min(page.w / 620, vertical ? 1.2 : 1.36, (h - top - 150) / 430);
  const plotLeft = (page.w - 620 * s) / 2;
  const rowW = 620 * s;
  const lines = vertical ? 112 : 84;
  const plot1 = top + lines;
  const slider1 = plot1 + 410 * s + 8;
  const sec2 = slider1 + SLIDER.h + (vertical ? 110 : 96); // far enough that the first slider scrolls right out, under the toolbar
  const plot2 = sec2 + lines;
  const slider2 = plot2 + 430 * s + 8;
  const scrollTo = sec2 - top;
  const scroll = interpolate(frame, SCROLL, [0, scrollTo], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});

  const k = valueAt(frame, K);
  const eta = valueAt(frame, ETA);
  const holdingK = frame >= K0 - 2 && frame <= K1 + 2;
  const holdingEta = frame >= E0 - 2 && frame <= E1 + 2;

  const knob = (row: number, pos: number, scrolled: number) => stage.at(page.x + plotLeft + knobX(rowW, pos), row + SLIDER.h / 2 - scrolled);
  const k0 = knob(slider1, posK(SIGMOID.k.start), 0);
  const k1 = knob(slider1, posK(5), 0);
  const e = (v: number) => knob(slider2, posEta(v), scrollTo);
  const stops: [number, number, number][] = [
    [6, k0.x + 300, k0.y - 260],
    [K0 - 8, k0.x, k0.y],
    ...K.map(([f, v]) => [f, knob(slider1, posK(v), 0).x, k0.y] as [number, number, number]),
    [K1 + 8, k1.x, k1.y],
    [SCROLL[1] - 6, k1.x - 60, k1.y - 120],
    [E0 - 8, e(DESCENT.eta.start).x, e(DESCENT.eta.start).y],
    ...ETA.map(([f, v]) => [f, e(v).x, e(v).y] as [number, number, number]),
    [E1 + 30, e(0.21).x + 40, e(0.21).y - 90],
  ];
  const bar = (p: number) => <FloatingBar icons={['edit', 'chat', 'schedule', 'open_in_full']} p={p} />;
  return (
    <AbsoluteFill>
      <Desktop clock="Mon 29 Sep  09:52">
        <DemoWindow stage={stage} lit="ml" enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: 0, width: page.w, transform: `translateY(${-scroll}px)`}}>
            <div style={{position: 'absolute', top, left: 0, right: 0}}>
              <H2>The sigmoid</H2>
              <Serif style={{marginTop: 6}}>Drag the steepness: a larger k makes the step from 0 to 1 sharper, and σ(0) stays 0.5.</Serif>
            </div>
            <div style={{position: 'absolute', top: plot1, left: plotLeft}}>
              <SigmoidPlot k={k} scale={s} />
            </div>
            <div style={{position: 'absolute', top: plot1 - 6, right: 0}}>{bar(prog(frame, 14, 22) * (1 - prog(frame, SCROLL[0], SCROLL[0] + 6)))}</div>
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
            <div style={{position: 'absolute', top: plot2 - 6, right: 0}}>{bar(prog(frame, SCROLL[1] - 4, SCROLL[1] + 4))}</div>
            <div style={{position: 'absolute', top: slider2, left: plotLeft}}>
              <SliderRow label="learning rate η" pos={posEta(eta)} value={eta} width={rowW} held={holdingEta} />
            </div>
          </div>
        </DemoWindow>
        <Cursor softPress stops={stops} clicks={[K0 - 2, E0 - 2]} />
      </Desktop>
      <Title text={titles.plots!} delay={8} />
    </AbsoluteFill>
  );
};
