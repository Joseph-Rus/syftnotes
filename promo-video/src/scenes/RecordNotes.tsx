import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {ease, spr, typed} from '../anim';
import {Sfx} from '../components/Sfx';
import {Title, ClassDot, Cursor, Desktop, menuIconX, useVertical} from '../components/Layout';

// The menu bar dropdown, as in docs/images/menu-bar.png: Record, then what's recent.
const DROP_W = 420;
const DROP_TOP = 46;
const OPEN = 30; // the icon is clicked
const RECORD = 60; // Record is clicked
const STOP = 128; // Stop is clicked
const FILED = 178; // the new lecture is filed, as the narration says "files them by class"

const Waveform: React.FC<{frame: number}> = ({frame}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 3.5, height: 34}}>
    {Array.from({length: 18}, (_, i) => {
      const h = 5 + Math.abs(Math.sin(frame / 4 + i * 0.9) * Math.cos(frame / 9 + i * 0.37)) * 28;
      return <div key={i} style={{width: 3.5, height: h, borderRadius: 2, background: '#D5DCE8', opacity: 0.55 + (h / 34) * 0.45}} />;
    })}
  </div>
);

const Chevrons: React.FC<{size?: number}> = ({size = 16}) => (
  <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke={colors.text2} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <path d="M4.5 6L8 2.8 11.5 6M4.5 10L8 13.2 11.5 10" />
  </svg>
);

const Recent: React.FC<{name: string; sub: React.ReactNode; right: string; color: string; progress?: number; lit?: boolean; style?: React.CSSProperties}> = ({
  name,
  sub,
  right,
  color,
  progress,
  lit,
  style,
}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '8px 12px', borderRadius: 11, background: lit ? colors.hover : 'transparent', ...style}}>
    <ClassDot color={color} size={8} />
    <div style={{flex: 1}}>
      <div style={{fontSize: 17, fontWeight: 500, color: colors.text}}>{name}</div>
      <div style={{fontSize: 14.5, color: colors.text2, marginTop: 1}}>{sub}</div>
      {progress !== undefined ? (
        <div style={{height: 3.5, width: 156, borderRadius: 2, background: 'rgba(255,255,255,0.12)', marginTop: 5}}>
          <div style={{height: 3.5, width: 156 * progress, borderRadius: 2, background: colors.lagoon}} />
        </div>
      ) : null}
    </div>
    <div style={{fontSize: 15, color: colors.text2}}>{right}</div>
  </div>
);

/** The menu bar dropdown in dark mode, as MacPanel draws it (the app's own "mac-01-dropdown" shots). */
const Dropdown: React.FC<{frame: number}> = ({frame}) => {
  const recording = frame >= RECORD && frame < STOP;
  const seconds = 10 + Math.max(0, Math.floor((frame - RECORD) / 6));
  const quote = '“…and when we hit the base case, the frames come off one by one.”';
  const added = spr(frame, STOP + 4, {damping: 22, stiffness: 150, mass: 0.8});
  const filed = frame >= FILED;
  const pill: React.CSSProperties = {flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8, height: 38, borderRadius: 999, fontSize: 16, fontWeight: 700};
  return (
    <div
      style={{
        width: DROP_W,
        borderRadius: 20,
        background: colors.popup,
        border: `1px solid ${colors.edge}`,
        boxShadow: '0 0 0 0.5px rgba(0,0,0,0.5), 0 30px 70px -18px rgba(0,0,20,0.75)',
        padding: 10,
        fontFamily: fonts.ui,
        color: colors.text,
      }}
    >
      {recording ? (
        <div style={{background: colors.card, borderRadius: 14, padding: '12px 12px 12px'}}>
          <div style={{display: 'flex', alignItems: 'center', gap: 8, fontSize: 15.5, fontWeight: 700}}>
            <div style={{width: 8, height: 8, borderRadius: 4, background: colors.lagoonBright, opacity: 0.55 + 0.45 * Math.abs(Math.sin(frame / 7))}} />
            Recording · CS 101
            <div style={{flex: 1}} />
            <span style={{fontSize: 14.5, fontWeight: 500, color: colors.text2}}>Show recorder</span>
          </div>
          <div style={{display: 'flex', alignItems: 'center', gap: 14, marginTop: 8}}>
            <div style={{fontFamily: fonts.ui, fontSize: 34, fontWeight: 400, fontVariantNumeric: 'tabular-nums', letterSpacing: '-0.01em'}}>24:{String(seconds).padStart(2, '0')}</div>
            <Waveform frame={frame} />
          </div>
          <div style={{fontSize: 14.5, color: colors.text2, lineHeight: 1.4, minHeight: 40, marginTop: 6}}>{typed(quote, frame, RECORD + 8, 34)}</div>
          <div style={{display: 'flex', gap: 8, marginTop: 10}}>
            <div style={{...pill, background: 'rgba(255,255,255,0.10)'}}>❙❙ Pause</div>
            <div style={{...pill, background: colors.lagoon, color: '#FFFFFF', transform: `scale(${frame >= STOP - 2 ? 0.96 : 1})`}}>■ Stop</div>
          </div>
        </div>
      ) : (
        <>
          <div style={{display: 'flex', gap: 10, alignItems: 'center'}}>
            <div
              style={{
                flex: 1,
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                height: 42,
                background: colors.lagoon,
                color: '#FFFFFF',
                borderRadius: 999,
                padding: '0 18px',
                fontSize: 17,
                fontWeight: 700,
                transform: `scale(${frame >= RECORD - 2 && frame < RECORD + 2 ? 0.96 : 1})`,
              }}
            >
              <div style={{width: 13, height: 13, borderRadius: 7, background: '#FFFFFF'}} />
              Record · CS 101
              <div style={{flex: 1}} />
              <span style={{fontSize: 14, fontWeight: 500, opacity: 0.85}}>⌥⇧R</span>
            </div>
            <div style={{width: 42, height: 42, borderRadius: 21, background: 'rgba(255,255,255,0.08)', border: `1px solid ${colors.edge}`, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
              <Chevrons />
            </div>
          </div>
          <div style={{fontSize: 15, color: colors.text2, padding: '9px 10px 0'}}>From your calendar: CS 101 Lecture</div>
        </>
      )}
      <div style={{fontSize: 15, fontWeight: 700, color: colors.text2, padding: '16px 12px 4px'}}>Recent</div>
      {frame >= STOP ? (
        <div style={{opacity: added, transform: `translateY(${(1 - added) * -10}px)`}}>
          <Recent
            name="Recursion and the call stack"
            color={colors.cs}
            right="10:02"
            lit={filed}
            sub={filed ? 'Filed in CS 101' : 'Writing notes…'}
          />
        </div>
      ) : null}
      <Recent name="Membranes and osmosis" color={colors.bio} right="9:40" sub={`Transcribing ${Math.min(96, 42 + Math.floor(frame / 3))}%`} progress={Math.min(0.96, 0.42 + frame / 300)} />
      <Recent name="Series convergence tests" color={colors.calc} right="Mon" sub="Filed in CALC II" />
      {frame < STOP ? <Recent name="The Treaty of Versailles" color={colors.hist} right="Mon" sub="Filed in HIST 210" /> : null}
      <div style={{display: 'flex', alignItems: 'center', gap: 10, margin: '8px 0 0', padding: '0 14px', height: 38, borderRadius: 13, background: 'rgba(255,255,255,0.07)', border: `1px solid ${colors.edge}`, fontSize: 16, color: colors.text}}>
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke={colors.text2} strokeWidth="2.6">
          <circle cx="11" cy="11" r="7" />
          <path d="M20 20l-4-4" strokeLinecap="round" />
        </svg>
        Search notes and lectures
        <div style={{flex: 1}} />
        <span style={{fontSize: 13, color: colors.text2}}>⌥Space</span>
      </div>
      <div style={{display: 'flex', alignItems: 'center', gap: 8, fontSize: 14.5, color: colors.text2, padding: '12px 12px 10px'}}>
        <ClassDot color="#22C55E" size={7} /> Library connected · Model ready
      </div>
      <div style={{height: 1, background: colors.line, margin: '0 8px'}} />
      <div style={{display: 'flex', alignItems: 'center', padding: '12px 12px 6px', fontSize: 17.5, fontWeight: 500}}>
        Open Study Stash
        <div style={{flex: 1}} />
        <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke={colors.text2} strokeWidth="1.9">
          <circle cx="12" cy="12" r="3.2" />
          <path d="M12 2.8v2.4M12 18.8v2.4M4.2 7.5l2.1 1.2M17.7 15.3l2.1 1.2M4.2 16.5l2.1-1.2M17.7 8.7l2.1-1.2" strokeLinecap="round" />
          <circle cx="12" cy="12" r="7.2" />
        </svg>
      </div>
    </div>
  );
};

export const RecordNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const iconX = menuIconX(width);
  const dropLeft = Math.min(iconX - 120, width - DROP_W - 16); // hangs from the icon toward the right, clear of the headline
  // The camera moves in on the menu bar and its dropdown.
  // It pulls back out at the end, the dropdown closing, so the next scene fades in over the same desktop.
  const back = spr(frame, durations.record - 30, {damping: 30, stiffness: 90, mass: 1});
  const zoom = spr(frame, 8, {damping: 30, stiffness: 60, mass: 1}) - back;
  const S = vertical ? 2.2 : 1.5;
  const s = interpolate(zoom, [0, 1], [1, S]);
  // Landscape keeps the desktop's right edge (and the clock) in frame; vertical centres the dropdown.
  const target = width - width * S; // the camera keeps the desktop's right edge in frame
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
            [56, dropLeft + 150, DROP_TOP + 30],
            [64, dropLeft + 150, DROP_TOP + 30],
            [118, dropLeft + 310, DROP_TOP + 196],
            [150, dropLeft + 310, DROP_TOP + 196],
          ]}
          clicks={[OPEN, RECORD, STOP]}
        />
      </AbsoluteFill>
      <Sfx at={8} name="whoosh" volume={0.12} />
      <Sfx at={OPEN + 2} name="pop" volume={0.22} />
      <Sfx at={RECORD + 1} name="chime-record" volume={0.4} />
      <Sfx at={FILED} name="chime-filed" volume={0.42} />
      <Sfx at={durations.record - 30} name="whoosh" volume={0.1} />
      <Title text={text.record as [string, string]} delay={14} width={vertical ? undefined : 700} under />
    </AbsoluteFill>
  );
};
