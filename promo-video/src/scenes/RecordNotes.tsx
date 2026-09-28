import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {ease, spr, typed} from '../anim';
import {Caption, ClassDot, Cursor, Desktop, menuIconX, useVertical} from '../components/Layout';

// The menu bar dropdown, as in docs/images/menu-bar.png: Record, then what's recent.
const DROP_W = 420;
const DROP_TOP = 46;
const OPEN = 30; // the icon is clicked
const RECORD = 60; // Record is clicked
const STOP = 128; // Stop is clicked
const FILED = 158; // the new lecture is filed

const Waveform: React.FC<{frame: number}> = ({frame}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 4, height: 46, flex: 1}}>
    {Array.from({length: 22}, (_, i) => {
      const h = 6 + Math.abs(Math.sin(frame / 4 + i * 0.9) * Math.cos(frame / 9 + i * 0.37)) * 40;
      return <div key={i} style={{width: 4.5, height: h, borderRadius: 3, background: colors.lagoonDeep, opacity: 0.45 + (h / 46) * 0.55}} />;
    })}
  </div>
);

const Recent: React.FC<{name: string; sub: React.ReactNode; right: string; color: string; progress?: number; style?: React.CSSProperties}> = ({name, sub, right, color, progress, style}) => (
  <div style={{display: 'flex', gap: 12, padding: '9px 14px', ...style}}>
    <ClassDot color={color} size={9} />
    <div style={{flex: 1, marginTop: -5}}>
      <div style={{fontSize: 17, fontWeight: 500}}>{name}</div>
      <div style={{fontSize: 14, color: colors.text2, marginTop: 1}}>{sub}</div>
      {progress !== undefined ? (
        <div style={{height: 4, width: 120, borderRadius: 2, background: 'rgba(16,24,40,0.08)', marginTop: 5}}>
          <div style={{height: 4, width: 120 * progress, borderRadius: 2, background: colors.lagoon}} />
        </div>
      ) : null}
    </div>
    <div style={{fontSize: 15, color: colors.text2, marginTop: -3}}>{right}</div>
  </div>
);

const Dropdown: React.FC<{frame: number}> = ({frame}) => {
  const recording = frame >= RECORD && frame < STOP;
  const seconds = 10 + Math.max(0, Math.floor((frame - RECORD) / 6));
  const quote = '“…and when we hit the base case, the frames come off one by one.”';
  const added = spr(frame, STOP + 4, {damping: 22, stiffness: 150, mass: 0.8});
  const filed = frame >= FILED;
  return (
    <div
      style={{
        width: DROP_W,
        borderRadius: 22,
        background: 'linear-gradient(180deg, rgba(242,250,253,0.97), rgba(232,245,251,0.97))',
        border: '1px solid rgba(255,255,255,0.8)',
        boxShadow: '0 1px 2px rgba(16,24,40,0.08), 0 30px 70px -20px rgba(34,52,100,0.45)',
        padding: 14,
        fontFamily: fonts.ui,
        color: colors.text,
      }}
    >
      {recording ? (
        <div style={{background: 'rgba(255,255,255,0.85)', borderRadius: 16, padding: '14px 16px'}}>
          <div style={{display: 'flex', alignItems: 'center', gap: 8, fontSize: 15, fontWeight: 600}}>
            <div style={{width: 9, height: 9, borderRadius: 5, background: colors.lagoon, opacity: 0.5 + 0.5 * Math.abs(Math.sin(frame / 7))}} />
            Recording · CS 101
            <div style={{flex: 1}} />
            <span style={{fontWeight: 500, color: colors.text2}}>Show recorder</span>
          </div>
          <div style={{display: 'flex', alignItems: 'center', gap: 16, marginTop: 8}}>
            <div style={{fontFamily: fonts.display, fontSize: 46, fontWeight: 500, fontVariantNumeric: 'tabular-nums', letterSpacing: '-0.01em'}}>24:{String(seconds).padStart(2, '0')}</div>
            <Waveform frame={frame} />
          </div>
          <div style={{fontSize: 14.5, color: colors.text2, lineHeight: 1.4, minHeight: 42, marginTop: 4}}>{typed(quote, frame, RECORD + 8, 34)}</div>
          <div style={{display: 'flex', gap: 10, marginTop: 10}}>
            <div style={{flex: 1, textAlign: 'center', padding: '10px 0', borderRadius: 12, background: 'rgba(16,24,40,0.07)', fontSize: 16, fontWeight: 600}}>❙❙ Pause</div>
            <div style={{flex: 1, textAlign: 'center', padding: '10px 0', borderRadius: 12, background: colors.lagoon, color: '#fff', fontSize: 16, fontWeight: 600, transform: `scale(${frame >= STOP - 2 ? 0.96 : 1})`}}>■ Stop</div>
          </div>
        </div>
      ) : (
        <>
          <div style={{display: 'flex', gap: 10}}>
            <div
              style={{
                flex: 1,
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                background: colors.lagoon,
                color: '#fff',
                borderRadius: 999,
                padding: '14px 20px',
                fontSize: 18,
                fontWeight: 700,
                transform: `scale(${frame >= RECORD - 2 && frame < RECORD + 2 ? 0.96 : 1})`,
              }}
            >
              <div style={{width: 12, height: 12, borderRadius: 6, background: '#fff'}} />
              Record · CS 101
              <div style={{flex: 1}} />
              <span style={{fontWeight: 500, opacity: 0.85}}>⌥⇧R</span>
            </div>
            <div style={{width: 50, height: 50, borderRadius: 25, background: 'rgba(255,255,255,0.9)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: colors.text2, fontSize: 18}}>⌄</div>
          </div>
          <div style={{fontSize: 14, color: colors.text2, padding: '8px 10px 0'}}>From your calendar · Tue 10:00–11:15</div>
        </>
      )}
      <div style={{fontSize: 14, fontWeight: 600, color: colors.text2, padding: '14px 14px 4px'}}>Recent</div>
      {frame >= STOP ? (
        <div style={{opacity: added, transform: `translateY(${(1 - added) * -10}px)`, background: filed ? 'rgba(255,255,255,0.75)' : 'transparent', borderRadius: 12}}>
          <Recent
            name="Recursion and the call stack"
            color={colors.cs}
            right="10:02"
            sub={
              filed ? (
                <span style={{color: colors.lagoonDeep, fontWeight: 600}}>✓ Filed in CS 101</span>
              ) : (
                'Writing notes with Claude Code…'
              )
            }
          />
        </div>
      ) : null}
      <Recent name="Membranes and osmosis" color={colors.bio} right="9:40" sub={`Transcribing ${Math.min(96, 42 + Math.floor(frame / 3))}%`} progress={Math.min(0.96, 0.42 + frame / 300)} />
      <Recent name="Series convergence tests" color={colors.calc} right="Mon" sub="Filed in CALC II" />
      {frame < STOP ? <Recent name="The Treaty of Versailles" color={colors.hist} right="Mon" sub="Filed in HIST 210" /> : null}
      <div style={{display: 'flex', alignItems: 'center', gap: 10, margin: '10px 4px 0', padding: '11px 14px', borderRadius: 12, background: 'rgba(255,255,255,0.8)', fontSize: 15.5, color: colors.text2}}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={colors.text2} strokeWidth="2.4">
          <circle cx="11" cy="11" r="7" />
          <path d="M20 20l-4-4" strokeLinecap="round" />
        </svg>
        Search notes and lectures
        <div style={{flex: 1}} />
        <span style={{fontSize: 13}}>⌥Space</span>
      </div>
      <div style={{display: 'flex', alignItems: 'center', gap: 8, fontSize: 13.5, color: colors.text2, padding: '12px 14px 4px'}}>
        <ClassDot color="#22A45D" size={7} /> Library connected · Model ready
      </div>
    </div>
  );
};

export const RecordNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const iconX = menuIconX(width);
  const dropLeft = iconX - 300;
  // The camera moves in on the menu bar and its dropdown.
  // It pulls back out at the end, the dropdown closing, so the next scene fades in over the same desktop.
  const back = spr(frame, durations.record - 30, {damping: 30, stiffness: 90, mass: 1});
  const zoom = spr(frame, 8, {damping: 30, stiffness: 60, mass: 1}) - back;
  const S = vertical ? 2.2 : 1.5;
  const s = interpolate(zoom, [0, 1], [1, S]);
  // Landscape keeps the desktop's right edge (and the clock) in frame; vertical centres the dropdown.
  const target = vertical ? Math.max(width - width * S, 540 - (dropLeft + DROP_W / 2) * S) : width - width * S;
  const tx = interpolate(zoom, [0, 1], [0, target]);
  const open = spr(frame, OPEN + 2, {damping: 22, stiffness: 180, mass: 0.7});
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{transform: `translate(${tx}px, 0) scale(${s})`, transformOrigin: '0 0'}}>
        <Desktop lit={frame >= OPEN && back < 0.5}>
          {frame >= OPEN ? (
            <div style={{position: 'absolute', left: dropLeft, top: DROP_TOP, opacity: open * (1 - back), transform: `translateY(${(1 - open) * -8}px) scale(${0.97 + 0.03 * open})`, transformOrigin: '70% 0'}}>
              <Dropdown frame={frame} />
            </div>
          ) : null}
        </Desktop>
        <Cursor
          stops={[
            [4, iconX - 420, 420],
            [28, iconX - 4, 14],
            [40, iconX - 4, 14],
            [56, dropLeft + 160, 76],
            [64, dropLeft + 160, 76],
            [118, dropLeft + 300, 232],
            [150, dropLeft + 300, 232],
          ]}
          clicks={[OPEN, RECORD, STOP]}
        />
      </AbsoluteFill>
      <Caption text={text.record} delay={14} />
    </AbsoluteFill>
  );
};
