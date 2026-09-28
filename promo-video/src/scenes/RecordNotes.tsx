import React from 'react';
import {interpolate, useCurrentFrame} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {ease, outro, rise, spr, typed, words} from '../anim';
import {ClassDot, Feature, Glass, TrafficLights, useVertical} from '../components/Layout';
import {Sidebar} from '../components/Sidebar';

const W = 1060;
const H = 660;
const VW = 900; // vertical: narrower and taller, so the words come out bigger
const VH = 700;
const FILED = 96; // the lecture is written up and filed

const SUMMARY =
  "A recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack.";
const POINTS = [
  'Every recursive function needs a base case.',
  'The most recent call finishes first.',
  'Tracing factorial(3) is what the midterm tests.',
];

const Waveform: React.FC<{frame: number; bars: number}> = ({frame, bars}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 6, height: 80}}>
    {Array.from({length: bars}, (_, i) => {
      const h = 14 + Math.abs(Math.sin(frame / 4 + i * 0.9) * Math.cos(frame / 9 + i * 0.37)) * 64;
      return <div key={i} style={{width: 7, height: h, borderRadius: 4, background: colors.lagoonBright, opacity: 0.55 + (h / 80) * 0.45}} />;
    })}
  </div>
);

const Recorder: React.FC<{frame: number; bars: number}> = ({frame, bars}) => {
  const press = spr(frame, 12);
  const recording = frame >= 20;
  const seconds = 10 + Math.max(0, Math.floor((frame - 20) / 6));
  const leave = ease(frame, FILED - 8, FILED + 4);
  const quote = '“…and when we hit the base case, the frames come off one by one.”';
  return (
    <div style={{padding: '40px 46px', opacity: 1 - leave, transform: `scale(${1 - leave * 0.08})`}}>
      {!recording ? (
        <div
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 14,
            background: colors.lagoon,
            borderRadius: 999,
            padding: '16px 28px',
            fontSize: 26,
            fontWeight: 700,
            transform: `scale(${1 - Math.sin(press * Math.PI) * 0.06})`,
          }}
        >
          <div style={{width: 16, height: 16, borderRadius: 8, background: '#fff'}} />
          Record · CS 101
          <span style={{fontWeight: 500, opacity: 0.8, marginLeft: 18}}>⌥⇧R</span>
        </div>
      ) : (
        <>
          <div style={{display: 'flex', alignItems: 'center', gap: 12, fontSize: 22, fontWeight: 600, color: colors.nightInk2}}>
            <div style={{width: 12, height: 12, borderRadius: 6, background: '#FF5F57', opacity: 0.6 + 0.4 * Math.abs(Math.sin(frame / 8))}} />
            Recording · CS 101
          </div>
          <div style={{display: 'flex', alignItems: 'center', gap: 34, marginTop: 18}}>
            <div style={{fontFamily: fonts.display, fontSize: 92, fontWeight: 600, fontVariantNumeric: 'tabular-nums', letterSpacing: '-0.02em'}}>
              24:{String(seconds).padStart(2, '0')}
            </div>
            <Waveform frame={frame} bars={bars} />
          </div>
          <div style={{marginTop: 26, fontFamily: fonts.serif, fontSize: 30, lineHeight: 1.4, color: colors.nightInk2, minHeight: 90, maxWidth: 680}}>
            {typed(quote, frame, 28, 44)}
          </div>
          <div style={{display: 'flex', gap: 16, marginTop: 26}}>
            {['Pause', 'Stop'].map((b, i) => (
              <div
                key={b}
                style={{
                  flex: 1,
                  textAlign: 'center',
                  padding: '16px 0',
                  borderRadius: 16,
                  fontSize: 22,
                  fontWeight: 600,
                  background: i ? colors.lagoon : 'rgba(255,255,255,0.08)',
                }}
              >
                {b}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
};

const Notes: React.FC<{frame: number}> = ({frame}) => {
  const f = frame - FILED;
  const summary = words(SUMMARY, f, 10, 24);
  return (
    <div style={{position: 'absolute', inset: 0, padding: '36px 46px'}}>
      <div style={{...rise(spr(f, 0)), display: 'flex', alignItems: 'center', gap: 10, fontSize: 18, color: colors.nightInk2}}>
        <ClassDot color={colors.cs} size={9} /> CS 101 · Tuesday 23 September · 1 h 12 min
      </div>
      <div style={{...rise(spr(f, 3)), fontFamily: fonts.display, fontSize: 40, fontWeight: 700, marginTop: 12, letterSpacing: '-0.02em'}}>
        Recursion and the call stack
      </div>
      <div style={{...rise(spr(f, 8)), display: 'flex', alignItems: 'baseline', gap: 12, marginTop: 24}}>
        <span style={{fontSize: 24, fontWeight: 700}}>Summary</span>
        <span style={{fontSize: 17, color: colors.nightInk3}}>Written by Ollama · just now</span>
      </div>
      <div style={{fontFamily: fonts.serif, fontSize: 24, lineHeight: 1.5, marginTop: 12, color: '#DCE1EA', minHeight: 72}}>
        {summary.shown}
      </div>
      <div style={{...rise(spr(f, 30)), fontSize: 22, fontWeight: 700, marginTop: 10}}>Key points</div>
      {POINTS.map((p, i) => (
        <div
          key={p}
          style={{...rise(spr(f, 34 + i * 6), 20), display: 'flex', gap: 14, fontFamily: fonts.serif, fontSize: 22, marginTop: 10, color: '#DCE1EA'}}
        >
          <span style={{color: colors.nightInk3}}>•</span>
          {p}
        </div>
      ))}
    </div>
  );
};

export const RecordNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const w = vertical ? VW : W;
  const h = vertical ? VH : H;
  const inP = spr(frame, 4);
  // "Filed in CS 101" leaves the notes and lands on CS 101 in the sidebar, whose count goes up.
  const fly = ease(frame, FILED + 2, FILED + 24);
  const chipShown = frame >= FILED - 2 && frame < FILED + 26;
  const landed = frame >= FILED + 24;
  const pulse = landed ? Math.max(0, 1 - (frame - FILED - 24) / 14) : 0;
  return (
    <Feature headline={text.record} w={w} h={h} opacity={outro(frame, durations.record)}>
      <div style={{...rise(inP, 60), width: w, height: h, position: 'relative'}}>
        <Glass style={{width: w, height: h, display: 'flex'}}>
          <Sidebar
            rows={[
              {name: 'CS 101', color: colors.cs, count: landed ? 12 : 11, lit: true, pulse},
              {name: 'BIO 110', color: colors.bio, count: 9},
              {name: 'CALC II', color: colors.calc, count: 11},
              {name: 'HLTH 120', color: colors.health, count: 8},
              {name: 'HIST 210', color: colors.hist, count: 7},
            ]}
          />
          <div style={{flex: 1, position: 'relative'}}>
            <TrafficLights />
            <div style={{position: 'absolute', inset: '40px 0 0 0'}}>
              {frame < FILED + 6 ? <Recorder frame={frame} bars={vertical ? 18 : 30} /> : null}
              {frame >= FILED ? <Notes frame={frame} /> : null}
            </div>
          </div>
        </Glass>
        {chipShown ? (
          <div
            style={{
              position: 'absolute',
              left: interpolate(fly, [0, 1], [540, 60]),
              top: interpolate(fly, [0, 1], [330, 72]),
              transform: `scale(${interpolate(fly, [0, 1], [1.15, 0.7])})`,
              opacity: interpolate(fly, [0, 0.85, 1], [1, 1, 0]),
              display: 'flex',
              alignItems: 'center',
              gap: 10,
              padding: '12px 20px',
              borderRadius: 999,
              background: colors.lagoon,
              color: '#fff',
              fontFamily: fonts.ui,
              fontSize: 22,
              fontWeight: 700,
              boxShadow: '0 12px 30px -8px rgba(0,143,144,0.6)',
            }}
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
              <path d="M5 12l5 5L19 7" />
            </svg>
            Filed in CS 101
          </div>
        ) : null}
      </div>
    </Feature>
  );
};
