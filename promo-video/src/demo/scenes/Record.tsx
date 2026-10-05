import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../../config';
import {ClassDot, Cursor, Desktop, MENU_H, menuIconX, Title, useVertical} from '../../components/Layout';
import {inOut, prog, px} from '../../promo2/scenes/Bookends';
import {Cut, cueIn, lengthIn, titles} from '../config';
import {G, Toast} from '../chrome';

// Recording, as the app does it on a Mac. The S. in the menu bar opens its dropdown (MacPanel, "mac-01-dropdown");
// Record starts it, the dropdown goes and the recorder's pill appears in the corner: a red dot, the time and a tiny level
// meter (MacRecorder; with the pointer over it, Stop takes the meter's place). A click on the pill opens the recorder,
// where what's said shows up about a second after it's said (the live words, on Apple silicon). Fifty minutes later,
// Stop;
// the notification says it's being written down.
//
// The dropdown, pill and recorder are drawn at their sizes on a Mac, times K, on the desktop; the camera moves in.

const K = 1.3;
const REC = '#FF453A';
const glass = 'linear-gradient(180deg, #1D2837 0%, #17212E 100%)';
const edge = '1px solid rgba(255,255,255,0.12)';

type Chunk = [frame: number, words: string];
type Line = {time: string; chunks: Chunk[]};

/** The newest lines, as they come in: each in pieces about a second apart, as the live words arrive. */
const heard = (lines: Line[], f: number) =>
  lines
    .map((l) => ({time: l.time, text: l.chunks.filter(([at]) => f >= at).map(([, w]) => w).join(' ')}))
    .filter((l) => l.text.length > 0);

const Meter: React.FC<{f: number; bars: number; h: number; gap: number; w: number; color: string}> = ({f, bars, h, gap, w, color}) => (
  <div style={{display: 'flex', alignItems: 'center', gap, height: h}}>
    {Array.from({length: bars}, (_, i) => {
      const v = Math.abs(Math.sin(f / 2.7 + i * 1.9) * Math.cos(f / 6.1 + i * 0.7));
      return <div key={i} style={{width: w, height: px(Math.max(2, h * (0.25 + 0.75 * v))), borderRadius: w / 2, background: color}} />;
    })}
  </div>
);

const clock = (s: number) => {
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const ss = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
};

/** The recorder's pill: small, it only says it's recording. */
const Pill: React.FC<{f: number; elapsed: string; hovered: boolean}> = ({f, elapsed, hovered}) => (
  <div style={{height: 28, borderRadius: 14, padding: '0 5px 0 10px', background: glass, border: edge, boxShadow: '0 10px 26px -8px rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', gap: 6, boxSizing: 'border-box', fontFamily: fonts.ui, color: colors.text}}>
    <div style={{width: 8, height: 8, borderRadius: 4, background: REC, opacity: px(0.6 + 0.4 * Math.abs(Math.cos(f / 22)))}} />
    <div style={{fontSize: 12, fontWeight: 600, fontVariantNumeric: 'tabular-nums'}}>{elapsed}</div>
    <div style={{width: 18, height: 18, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
      {hovered ? (
        <div style={{width: 18, height: 18, borderRadius: 9, background: 'rgba(255,255,255,0.16)', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
          <G name="stop" size={12} />
        </div>
      ) : (
        <Meter f={f} bars={3} h={10} gap={2} w={2.4} color={colors.text2} />
      )}
    </div>
  </div>
);

const RoundButton: React.FC<{glyph: 'shrink' | 'pause' | 'stop'; tint?: boolean; press?: number}> = ({glyph, tint, press = 0}) => (
  <div style={{width: 32, height: 32, borderRadius: 16, background: tint ? colors.lagoon : 'transparent', display: 'flex', alignItems: 'center', justifyContent: 'center', transform: `scale(${px(1 - 0.08 * press)})`}}>
    <G name={glyph} size={glyph === 'shrink' ? 17 : 15} color={glyph === 'shrink' ? colors.text2 : '#FFFFFF'} />
  </div>
);

/** The recorder, open: the class and time, make it small, pause and stop; what's been said, newest at the bottom,
 * fading out toward the top; and asking about the lecture so far. */
export const Recorder: React.FC<{f: number; elapsed: string; lines: {time: string; text: string}[]; waiting: boolean; stopPress: number}> = ({f, elapsed, lines, waiting, stopPress}) => (
  <div style={{width: 360, height: 520, borderRadius: 28, background: glass, border: edge, boxShadow: '0 30px 70px -18px rgba(0,0,20,0.75)', boxSizing: 'border-box', position: 'relative', overflow: 'hidden', fontFamily: fonts.ui, color: colors.text}}>
    <div style={{position: 'absolute', left: 20, right: 12, top: 0, height: 60, display: 'flex', alignItems: 'center', gap: 10}}>
      <div style={{width: 8, height: 8, borderRadius: 4, background: REC}} />
      <div style={{fontSize: 13, fontWeight: 600}}>BIO 110</div>
      <div style={{fontSize: 13, color: colors.text2, fontVariantNumeric: 'tabular-nums'}}>{elapsed}</div>
      <div style={{flex: 1}} />
      <div style={{display: 'flex', gap: 2, padding: 3, borderRadius: 20, background: 'rgba(255,255,255,0.08)'}}>
        <RoundButton glyph="shrink" />
        <RoundButton glyph="pause" />
        <RoundButton glyph="stop" tint press={stopPress} />
      </div>
    </div>
    {/* What's been said, newest at the bottom, fading out toward the top. */}
    <div
      style={{
        position: 'absolute',
        left: 20,
        right: 20,
        top: 60,
        bottom: 66,
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'flex-end',
        gap: 12,
        paddingBottom: 12,
        WebkitMaskImage: 'linear-gradient(180deg, rgba(0,0,0,0) 0%, #000 55%)',
        maskImage: 'linear-gradient(180deg, rgba(0,0,0,0) 0%, #000 55%)',
      }}
    >
      {waiting ? (
        <div style={{fontSize: 13, lineHeight: 1.45, color: colors.text2}}>What’s said shows here a few seconds after it’s said.</div>
      ) : (
        lines.map((l, i) => (
          <div key={l.time} style={{display: 'flex', gap: 10}}>
            <div style={{width: 34, flexShrink: 0, fontSize: 11, color: colors.text2, paddingTop: 2, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap'}}>{l.time}</div>
            <div style={{fontSize: 13, lineHeight: '18.85px', color: i === lines.length - 1 ? colors.text : colors.text2}}>{l.text}</div>
          </div>
        ))
      )}
    </div>
    {/* Asking about the lecture so far (the recorder's chat). */}
    <div style={{position: 'absolute', left: 12, right: 12, bottom: 12, height: 44, borderRadius: 22, background: 'rgba(255,255,255,0.07)', border: '1px solid rgba(255,255,255,0.09)', boxSizing: 'border-box', display: 'flex', alignItems: 'center', gap: 6, padding: '0 6px 0 16px'}}>
      <div style={{flex: 1, fontSize: 13, color: colors.text3}}>Ask about this lecture</div>
      <div style={{height: 26, padding: '0 9px', borderRadius: 13, background: 'rgba(255,255,255,0.09)', display: 'flex', alignItems: 'center', gap: 5, fontSize: 12.5, fontWeight: 600}}>
        <G name="sparkle" size={12} color={colors.lagoonBright} /> Claude Code <G name="updown" size={11} color={colors.text2} />
      </div>
      <div style={{width: 30, height: 30, borderRadius: 15, background: 'rgba(255,255,255,0.09)', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <G name="up" size={15} />
      </div>
    </div>
    {/* a pure function of the frame, so the glass's sheen never flickers */}
    <div style={{position: 'absolute', inset: 0, borderRadius: 28, pointerEvents: 'none', boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.06)'}} data-f={f} />
  </div>
);

const Recent: React.FC<{name: string; sub: string; right: string; color: string; lit?: boolean}> = ({name, sub, right, color, lit}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 10, padding: '6px 9px', borderRadius: 9, background: lit ? 'rgba(255,255,255,0.07)' : 'transparent'}}>
    <ClassDot color={color} size={7} />
    <div style={{flex: 1}}>
      <div style={{fontSize: 13, fontWeight: 500}}>{name}</div>
      <div style={{fontSize: 11.5, color: colors.text2, marginTop: 1}}>{sub}</div>
    </div>
    <div style={{fontSize: 11.5, color: colors.text2}}>{right}</div>
  </div>
);

/** The menu bar's dropdown, before recording (MacPanel, dark). */
const Dropdown: React.FC<{press: number}> = ({press}) => (
  <div style={{width: 320, borderRadius: 16, background: glass, border: edge, boxShadow: '0 30px 70px -18px rgba(0,0,20,0.75)', padding: 8, boxSizing: 'border-box', fontFamily: fonts.ui, color: colors.text}}>
    <div style={{display: 'flex', gap: 8, alignItems: 'center'}}>
      <div style={{flex: 1, height: 32, borderRadius: 16, background: colors.lagoon, display: 'flex', alignItems: 'center', gap: 9, padding: '0 12px', fontSize: 13, fontWeight: 700, transform: `scale(${px(1 - 0.04 * press)})`}}>
        <div style={{width: 10, height: 10, borderRadius: 5, background: '#FFFFFF'}} />
        Record · BIO 110
        <div style={{flex: 1}} />
        <span style={{fontSize: 11, fontWeight: 500, opacity: 0.85}}>⌥⇧R</span>
      </div>
      <div style={{width: 30, height: 30, borderRadius: 15, background: 'rgba(255,255,255,0.08)', border: edge, boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <G name="updown" size={14} color={colors.text2} />
      </div>
    </div>
    <div style={{fontSize: 11.5, color: colors.text2, padding: '8px 8px 0'}}>From your calendar: BIO 110 Lecture</div>
    <div style={{fontSize: 11.5, fontWeight: 700, color: colors.text2, padding: '14px 9px 4px'}}>Recent</div>
    <Recent name="Membranes and osmosis" sub="Filed in BIO 110" right="Thu" color={colors.bio} />
    <Recent name="Series convergence tests" sub="Filed in CALC II" right="Mon" color={colors.calc} />
    <Recent name="Recursion and the call stack" sub="Filed in CS 101" right="Mon" color={colors.cs} />
    <div style={{display: 'flex', alignItems: 'center', gap: 8, margin: '6px 0 0', padding: '0 10px', height: 28, borderRadius: 14, background: 'rgba(255,255,255,0.07)', border: edge, boxSizing: 'border-box', fontSize: 13}}>
      <G name="search" size={13} color={colors.text2} /> Search notes and lectures
      <div style={{flex: 1}} />
      <span style={{fontSize: 10.5, color: colors.text2}}>⌥Space</span>
    </div>
    <div style={{display: 'flex', alignItems: 'center', gap: 7, fontSize: 11.5, color: colors.text2, padding: '10px 9px 8px'}}>
      <ClassDot color="#22C55E" size={6} /> Library connected · Model ready
    </div>
    <div style={{height: 1, background: colors.line, margin: '0 6px'}} />
    <div style={{display: 'flex', alignItems: 'center', padding: '10px 9px 4px', fontSize: 13.5, fontWeight: 500}}>
      Open Study Stash
      <div style={{flex: 1}} />
      <G name="gear" size={15} color={colors.text2} />
    </div>
  </div>
);

// What the lecturer says, as the live words bring it in. The class starts at 10:02; fifty minutes later, it ends.
// `pace` is when each piece arrives after the recorder opens (the reel's come quicker).
const START_PACE = [54, 70, 96, 118, 142, 160, 196, 222];
const START_PACE_FAST = [10, 19, 30, 40, 51, 60, 72, 82];
const START_LINES = (s: number, p = START_PACE): Line[] => [
  {time: '0:01', chunks: [[s + p[0], 'Okay, let’s get'], [s + p[1], 'going.']]},
  {time: '0:03', chunks: [[s + p[2], 'Today is the'], [s + p[3], 'cardiac cycle:'], [s + p[4], 'everything one'], [s + p[5], 'heartbeat does.']]},
  {time: '0:08', chunks: [[s + p[6], 'First the atria'], [s + p[7], 'fill, and then']]},
];
const END_LINES = (j: number, p = [26, 52]): Line[] => [
  {time: '48:31', chunks: [[j, 'So the valves closing are what you hear: lub, then dub.']]},
  {time: '49:12', chunks: [[j, 'For Thursday, read chapter 12, the conduction system.']]},
  {time: '49:56', chunks: [[j + p[0], 'That’s it'], [j + p[1], 'for today.']]},
];

// The reel's camera: the Mac's top right corner, close, behind the screen's black bezel, so the menu bar and the
// recorder sit inside Instagram's safe area (the bezel takes the top and the right, where Instagram draws its own).
export const REEL_SCREEN = {zoom: 1.5, right: 975, top: 200, corner: 46};

export const Record: React.FC<{cut?: Cut}> = ({cut}) => {
  const f = useCurrentFrame();
  const {width, height} = useVideoConfig();
  const vertical = useVertical();
  const D = lengthIn(cut, 'record');
  const MENU = cueIn(cut, 'record-menu');
  const RECORD = cueIn(cut, 'record-click');
  const START = cueIn(cut, 'record-start');
  const PILL = cueIn(cut, 'record-pill');
  const STOP = cueIn(cut, 'record-stop');
  const JUMP = cut ? cueIn(cut, 'record-jump') : PILL + 120; // fifty minutes later
  const LIVE = PILL; // the recorder opens: the live words show from here

  // The camera: in on the menu bar's corner, out again at the end into the library. The reel's holds still on it.
  const S = cut ? REEL_SCREEN.zoom : vertical ? 2.0 : 1.4;
  const zoom = cut ? 1 : prog(f, 0, 26, inOut) * (1 - prog(f, D - 40, D - 8, inOut));
  const s = 1 + (S - 1) * zoom;
  const tx = cut ? REEL_SCREEN.right - width * s : (width - width * s);

  const iconX = menuIconX(width);
  const right = width - 16; // the recorder's corner, under the menu bar
  const top = MENU_H + 8;
  const dropW = 320 * K;
  const dropLeft = Math.min(iconX - 120, width - dropW - 16);

  const open = prog(f, MENU + 2, MENU + 9);
  const shut = prog(f, RECORD + 2, RECORD + 8);
  const pillIn = prog(f, START, START + 7);
  const expand = prog(f, PILL + 1, PILL + 11, inOut);
  const closed = prog(f, STOP + 2, STOP + 10);
  const recording = f >= START && f < STOP + 2;
  const jumped = f >= JUMP;
  const elapsedS = jumped ? 2995 + Math.floor((f - JUMP) / 30) : Math.max(0, Math.floor((f - START) / 30));
  const elapsed = clock(elapsedS);
  const lines = jumped ? heard(cut ? END_LINES(JUMP, [8, 16]) : END_LINES(JUMP), f) : heard(START_LINES(LIVE, cut ? START_PACE_FAST : START_PACE), f);
  const waiting = !jumped && lines.length === 0;
  const later = cut ? prog(f, JUMP - 4, JUMP + 6) * (1 - prog(f, JUMP + 22, JUMP + 30)) : prog(f, JUMP - 4, JUMP + 8) * (1 - prog(f, JUMP + 44, JUMP + 56));
  // The reel goes on into the library straight after Stop, so it leaves the notification out.
  const saved = cut ? 0 : prog(f, STOP + 12, STOP + 24);

  const pillW = 98;
  const pillAt = {x: right - pillW * K + 46 * K, y: top + 14 * K}; // the pill's time
  const stopAt = {x: right - (12 + 3 + 16) * K, y: top + 30 * K};
  const recordAt = {x: dropLeft + 110 * K, y: MENU_H + 8 + 8 * K + 16 * K};

  // The pointer's path: the reel's is quicker, and rests beside the recorder while the words come in.
  const rest = {x: right - 360 * K - 70, y: top + 300};
  const stops: [number, number, number][] = cut
    ? [
        [0, iconX + 40, 64],
        [MENU - 3, iconX - 6, 14],
        [MENU + 3, iconX - 6, 14],
        [RECORD - 5, recordAt.x, recordAt.y],
        [RECORD + 3, recordAt.x, recordAt.y],
        [PILL - 7, pillAt.x, pillAt.y],
        [PILL + 3, pillAt.x, pillAt.y],
        [PILL + 22, rest.x, rest.y],
        [STOP - 22, rest.x + 10, rest.y - 10],
        [STOP - 6, stopAt.x, stopAt.y],
        [STOP + 6, stopAt.x, stopAt.y],
        [D, stopAt.x - 100, stopAt.y + 200],
      ]
    : [
        [0, iconX - 6, 14],
        [MENU + 6, iconX - 6, 14],
        [RECORD - 6, recordAt.x, recordAt.y],
        [RECORD + 4, recordAt.x, recordAt.y],
        [PILL - 16, pillAt.x, pillAt.y],
        [PILL + 2, pillAt.x, pillAt.y],
        [PILL + 26, rest.x, rest.y],
        [JUMP + 40, right - 360 * K - 60, top + 290],
        [STOP - 10, stopAt.x, stopAt.y],
        [STOP + 8, stopAt.x, stopAt.y],
        [D - 10, stopAt.x - 160, stopAt.y + 300],
      ];
  const camera = `translate(${px(tx)}px, 0px) scale(${px(s * 10000) / 10000})`;

  const desk = (
    <>
        <Desktop lit={f >= MENU && f < RECORD + 4} clock={jumped ? (f >= STOP ? 'Tue 23 Sep  10:53' : 'Tue 23 Sep  10:52') : 'Tue 23 Sep  10:02'}>
          {/* While recording, the S. wears a small red dot at its top right. */}
          {recording ? <div style={{position: 'absolute', left: iconX + 7, top: 8, width: 7, height: 7, borderRadius: 4, background: REC, boxShadow: '0 0 0 1.5px rgba(8,10,40,0.6)'}} /> : null}

          {f >= MENU && f < RECORD + 9 ? (
            <div style={{position: 'absolute', left: dropLeft, top: MENU_H + 8, transformOrigin: '0 0', transform: `translateY(${px((1 - open) * -6 - shut * 4)}px) scale(${K})`, opacity: open * (1 - shut)}}>
              <Dropdown press={Math.max(0, 1 - Math.abs(f - RECORD - 1) / 3)} />
            </div>
          ) : null}

          {f >= START && f < PILL + 6 ? (
            <div style={{position: 'absolute', left: right - pillW, top, transformOrigin: '100% 0', transform: `scale(${K})`, opacity: pillIn * (1 - prog(f, PILL + 1, PILL + 5))}}>
              <div style={{width: pillW, display: 'flex', justifyContent: 'flex-end'}}>
                <Pill f={f} elapsed={elapsed} hovered={f >= PILL - 14} />
              </div>
            </div>
          ) : null}

          {f >= PILL && f < STOP + 12 ? (
            <div
              style={{
                position: 'absolute',
                left: right - 360,
                top,
                transformOrigin: '100% 0',
                transform: `scale(${K})`,
                opacity: Math.min(1, expand * 2) * (1 - closed),
                // It grows from the pill's corner: its top right stays where the pill was.
                clipPath: `inset(0px 0px ${px((1 - expand) * 492)}px ${px((1 - expand) * 262)}px round 28px)`,
              }}
            >
              <Recorder f={f} elapsed={elapsed} lines={lines} waiting={waiting} stopPress={Math.max(0, 1 - Math.abs(f - STOP - 1) / 3)} />
            </div>
          ) : null}

          {/* After Stop: the notification (Toasts) says it's being written down. */}
          <div style={{position: 'absolute', left: right - 444, top, opacity: saved, transform: `translateX(${px((1 - saved) * 30)}px)`}}>
            <Toast title="Recording saved" body="Study Stash is writing it down; the library files it and writes your notes." />
          </div>
        </Desktop>
        <Cursor softPress stops={stops} clicks={[MENU, RECORD, PILL, STOP]} />
    </>
  );

  // The rest of the lecture goes by. The reel's sits over the recorder's quiet top, inside the safe area.
  const reelRec = cut ? {left: REEL_SCREEN.right - (16 + 360 * K) * S, right: width - (REEL_SCREEN.right - 16 * S)} : null;
  const chip = (
    <div
      style={{
        position: 'absolute',
        left: reelRec ? reelRec.left : 0,
        right: reelRec ? reelRec.right : vertical ? 0 : width - 1180,
        top: cut ? 600 : vertical ? 1470 : 520,
        display: 'flex',
        justifyContent: 'center',
        opacity: later,
        transform: `translateY(${px((1 - later) * 10)}px)`,
      }}
    >
      <div style={{display: 'flex', alignItems: 'center', gap: 14, padding: vertical ? '18px 32px' : '16px 30px', borderRadius: 999, background: 'rgba(30, 38, 70, 0.9)', border: '1px solid rgba(255,255,255,0.2)', boxShadow: '0 24px 50px -14px rgba(0,0,20,0.75)', fontFamily: fonts.ui, fontSize: vertical ? 36 : 30, fontWeight: 600, color: '#FFFFFF', whiteSpace: 'nowrap'}}>
        <svg width={vertical ? 40 : 34} height={vertical ? 40 : 34} viewBox="0 0 24 24" fill="none" stroke="#FFFFFF" strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 12 L12 6.5" transform={`rotate(${px(interpolate(f, [JUMP - 4, JUMP + (cut ? 30 : 56)], [0, 360], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}))} 12 12)`} />
          <path d="M12 12 L15.5 12" />
        </svg>
        50 minutes later
      </div>
    </div>
  );

  if (cut) {
    const {right: sr, top: st, corner} = REEL_SCREEN;
    return (
      <AbsoluteFill style={{background: '#050507'}}>
        <div style={{position: 'absolute', left: 0, top: st, width: sr, height: height - st, overflow: 'hidden', borderTopRightRadius: corner}}>
          <div style={{position: 'absolute', left: 0, top: 0, width, height, transform: camera, transformOrigin: '0 0'}}>{desk}</div>
        </div>
        {/* The screen's edge against the bezel, a hairline of light. */}
        <div style={{position: 'absolute', left: -2, top: st - 1, width: sr + 1, height: height - st + 4, borderTopRightRadius: corner + 1, boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.09)', pointerEvents: 'none'}} />
        {chip}
      </AbsoluteFill>
    );
  }

  return (
    <AbsoluteFill>
      <AbsoluteFill style={{transform: camera, transformOrigin: '0 0'}}>{desk}</AbsoluteFill>
      {chip}
      <div style={{opacity: 1 - prog(f, D - 44, D - 30)}}>
        <Title text={titles.record!} delay={10} width={vertical ? undefined : 840} under />
      </div>
    </AbsoluteFill>
  );
};
