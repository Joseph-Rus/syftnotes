import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {ease, spr} from '../anim';
import {BELOW_TITLE, ClassDot, Cursor, Desktop, Title, useVertical} from '../components/Layout';
import {Group, LibraryWindow, ListHead, ListRow} from '../components/Sidebar';
import {Sfx} from '../components/Sfx';

// Handwritten notes, from a photo to the lecture's notes, as the app does it: the phone app's "Add files" (Camera,
// "Sending"), the lecture's Attachments ("Reading your handwriting…"), then "Rewrite notes with your attachments".
const CAMERA = 14; // Camera is tapped
const SHUTTER = 40; // the photo is taken
const SENT = 92; // the upload finishes
const LANDS = 112; // the photo reaches the lecture's Attachments
const READ = 150; // the handwriting has been read
const REWRITE = 170; // "Rewrite notes with your attachments" is clicked
const REWRITTEN = 192; // the notes have the handwriting in them

const INK = '#1F3A78';
const HAND = '"Bradley Hand", Noteworthy, "Segoe Print", cursive';

/** A page of handwritten notes on lined paper. */
const Page: React.FC<{w: number; h: number; tilt?: number}> = ({w, h, tilt = -3}) => (
  <div
    style={{
      width: w,
      height: h,
      transform: `rotate(${tilt}deg)`,
      background: '#FBF8F0',
      borderRadius: 6,
      boxShadow: '0 10px 30px -8px rgba(0,0,0,0.45)',
      position: 'relative',
      overflow: 'hidden',
    }}
  >
    <svg width={w} height={h} viewBox="0 0 300 390" preserveAspectRatio="none" style={{position: 'absolute', inset: 0}}>
      {Array.from({length: 14}, (_, i) => (
        <line key={i} x1="0" x2="300" y1={52 + i * 24} y2={52 + i * 24} stroke="#AFC6E6" strokeWidth="0.8" />
      ))}
      <line x1="34" x2="34" y1="0" y2="390" stroke="#E7A0A0" strokeWidth="1" />
    </svg>
    <svg width={w} height={h} viewBox="0 0 300 390" style={{position: 'absolute', inset: 0}}>
      <g fontFamily={HAND} fontWeight={700} fill={INK}>
        <text x="46" y="44" fontSize="24">Recursion</text>
        <text x="46" y="94" fontSize="16">base case → stops it!</text>
        <text x="46" y="142" fontSize="16">fact(3) = 3 · fact(2)</text>
        <text x="96" y="166" fontSize="16">= 3 · 2 · fact(1)</text>
        <text x="46" y="214" fontSize="16">stack: last in, first out</text>
      </g>
      <path d="M46 50 C 90 54, 130 47, 168 52" stroke={INK} strokeWidth="2" fill="none" strokeLinecap="round" />
      <g stroke={INK} strokeWidth="2" fill="none" strokeLinecap="round" strokeLinejoin="round">
        <path d="M60 246 l58 -1 l1 26 l-58 1 z" />
        <path d="M60 272 l58 0 l0 26 l-58 1 z" />
        <path d="M61 299 l57 -1 l1 26 l-58 1 z" />
        <path d="M140 262 C 170 258, 180 280, 150 300" />
        <path d="M150 300 l2 -10 M150 300 l10 -3" />
      </g>
      <g fontFamily={HAND} fontWeight={700} fill={INK} fontSize="13">
        <text x="72" y="264">fact(1)</text>
        <text x="72" y="290">fact(2)</text>
        <text x="72" y="317">fact(3)</text>
        <text x="172" y="296">pops first</text>
      </g>
    </svg>
  </div>
);

/** A tap on the phone: a soft ring that grows and fades. */
const Tap: React.FC<{at: number; x: number; y: number; frame: number}> = ({at, x, y, frame}) => {
  const p = ease(frame, at, at + 12);
  if (frame < at || p >= 1) return null;
  return <div style={{position: 'absolute', left: x - 30, top: y - 30, width: 60, height: 60, borderRadius: 30, border: '3px solid rgba(255,255,255,0.9)', background: 'rgba(255,255,255,0.18)', opacity: 1 - p, transform: `scale(${0.5 + p})`}} />;
};

const PHONE_W = 390;
const PHONE_H = 800;

/** The phone app's "Add files" screen, dark: Camera, Files, and what's being sent. */
const Phone: React.FC<{frame: number}> = ({frame}) => {
  const camera = frame >= CAMERA + 4 && frame < SHUTTER + 6;
  const flash = interpolate(frame, [SHUTTER, SHUTTER + 2, SHUTTER + 8], [0, 1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const sending = frame >= SHUTTER + 6;
  const progress = ease(frame, SHUTTER + 10, SENT, Easing.inOut(Easing.cubic));
  const sent = frame >= SENT;
  const ui = 'rgba(255,255,255,';
  return (
    <div style={{width: PHONE_W, height: PHONE_H, borderRadius: 64, background: '#0A0D12', padding: 13, boxShadow: '0 0 0 2px #2A3038, 0 40px 90px -20px rgba(0,0,20,0.8)', position: 'relative'}}>
      <div style={{position: 'relative', width: '100%', height: '100%', borderRadius: 52, overflow: 'hidden', background: '#0E1A21', fontFamily: fonts.ui, color: colors.text}}>
        <div style={{position: 'absolute', top: 12, left: '50%', marginLeft: -60, width: 120, height: 34, borderRadius: 17, background: '#000', zIndex: 5}} />
        {camera ? (
          <div style={{position: 'absolute', inset: 0, background: '#1B1F24', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
            <Page w={300} h={390} tilt={-4} />
            <div style={{position: 'absolute', bottom: 46, left: '50%', marginLeft: -36, width: 72, height: 72, borderRadius: 36, border: '4px solid #fff', boxSizing: 'border-box'}}>
              <div style={{margin: 5, width: 54, height: 54, borderRadius: 27, background: '#fff'}} />
            </div>
          </div>
        ) : (
          <div style={{padding: '64px 18px 0'}}>
            <div style={{display: 'flex', alignItems: 'center', fontSize: 17, color: colors.lagoonBright}}>‹ Back</div>
            <div style={{fontFamily: fonts.display, fontSize: 32, fontWeight: 700, marginTop: 10, letterSpacing: '-0.02em'}}>Add files</div>
            <div style={{marginTop: 16, padding: 14, borderRadius: 14, background: '#1A2530', display: 'flex', gap: 10}}>
              {['Camera', 'Files'].map((b) => (
                <div key={b} style={{flex: 1, height: 46, borderRadius: 12, background: 'rgba(52,193,189,0.16)', color: colors.lagoonBright, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8, fontSize: 17, fontWeight: 600}}>
                  {b === 'Camera' ? '◉' : '▭'} {b}
                </div>
              ))}
            </div>
            <div style={{fontSize: 13, color: `${ui}0.55)`, padding: '8px 4px 0', lineHeight: 1.35}}>Sent to this lecture. Photos, PDFs, slides — anything with your notes on it.</div>
            {sending ? (
              <>
                <div style={{fontSize: 13, fontWeight: 600, color: `${ui}0.55)`, padding: '22px 4px 8px', textTransform: 'none'}}>Sending</div>
                <div style={{padding: 12, borderRadius: 14, background: '#1A2530', display: 'flex', alignItems: 'center', gap: 12}}>
                  <div style={{width: 44, height: 56, overflow: 'hidden', borderRadius: 4, flexShrink: 0}}>
                    <div style={{transform: 'scale(0.15)', transformOrigin: 'top left'}}>
                      <Page w={300} h={390} tilt={0} />
                    </div>
                  </div>
                  <div style={{flex: 1}}>
                    <div style={{fontSize: 16, fontWeight: 600}}>IMG_2041.HEIC</div>
                    <div style={{fontSize: 13, color: `${ui}0.55)`, marginTop: 3}}>{sent ? '2.1 MB · Sent' : `${(2.1 * progress).toFixed(1)} of 2.1 MB`}</div>
                    <div style={{height: 4, borderRadius: 2, background: `${ui}0.12)`, marginTop: 7}}>
                      <div style={{height: 4, borderRadius: 2, width: `${progress * 100}%`, background: colors.lagoonBright}} />
                    </div>
                  </div>
                  <div style={{fontSize: 18, color: colors.lagoonBright, opacity: sent ? 1 : 0}}>✓</div>
                </div>
              </>
            ) : null}
          </div>
        )}
        <div style={{position: 'absolute', inset: 0, background: '#fff', opacity: flash}} />
        <Tap at={CAMERA} x={112} y={222} frame={frame} />
        <Tap at={SHUTTER} x={PHONE_W / 2 - 13} y={PHONE_H - 108} frame={frame} />
      </div>
    </div>
  );
};

/** The lecture's page: its key points, and Attachments, which the photo lands in. */
const LecturePage: React.FC<{frame: number}> = ({frame}) => {
  const landed = frame >= LANDS;
  const reading = landed && frame < READ;
  const bar = frame >= READ && frame < REWRITTEN;
  const added = spr(frame, REWRITTEN, {damping: 24, stiffness: 150, mass: 0.9});
  const glow = interpolate(frame, [REWRITTEN, REWRITTEN + 8, REWRITTEN + 60], [0, 1, 0.35], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const row = spr(frame, LANDS, {damping: 22, stiffness: 170, mass: 0.8});
  const spin = (frame * 12) % 360;
  const Spinner = () => (
    <svg width="14" height="14" viewBox="0 0 24 24" style={{transform: `rotate(${spin}deg)`}}>
      <circle cx="12" cy="12" r="9" fill="none" stroke={colors.text2} strokeWidth="3" strokeDasharray="42 60" strokeLinecap="round" />
    </svg>
  );
  const points = ['Every recursive function needs a base case.', 'Each call waits for the one it made.'];
  return (
    <div style={{padding: '34px 40px 0'}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
        <ClassDot color={colors.cs} size={8} /> CS 101 · Tuesday 23 September · 1 h 12 min
      </div>
      <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Recursion and the call stack</div>
      <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 20}}>Key points</div>
      {points.map((k) => (
        <div key={k} style={{display: 'flex', gap: 14, fontFamily: fonts.serif, fontSize: 19, marginTop: 10, color: colors.serifInk}}>
          <span style={{color: colors.text3}}>•</span>
          {k}
        </div>
      ))}
      {frame >= REWRITTEN ? (
        <div style={{display: 'flex', gap: 14, fontFamily: fonts.serif, fontSize: 19, marginTop: 10, color: colors.serifInk, opacity: added, transform: `translateY(${(1 - added) * 8}px)`}}>
          <span style={{color: colors.text3}}>•</span>
          <span style={{background: `rgba(52,193,189,${0.22 * glow})`, borderRadius: 6, padding: '0 4px', margin: '0 -4px'}}>
            The stack is last in, first out: fact(1) returns first.
          </span>
        </div>
      ) : null}
      {/* Attachments, as MacAttachments draws them. */}
      <div style={{display: 'flex', alignItems: 'center', marginTop: 26}}>
        <div style={{flex: 1, fontSize: 14, fontWeight: 600, color: colors.text2}}>Attachments</div>
        <div style={{padding: '6px 14px', borderRadius: 999, background: colors.card, border: `1px solid ${colors.edge}`, fontSize: 14, fontWeight: 600}}>⌁ Attach</div>
      </div>
      {reading ? (
        <div style={{display: 'flex', alignItems: 'center', gap: 8, fontSize: 14.5, color: colors.text2, marginTop: 8}}>
          <Spinner /> Reading your handwriting…
        </div>
      ) : null}
      {landed ? (
        <div style={{display: 'flex', alignItems: 'center', gap: 12, marginTop: 8, padding: '9px 12px', borderRadius: 11, background: colors.card, opacity: row, transform: `translateY(${(1 - row) * -6}px)`}}>
          <div style={{width: 30, height: 38, overflow: 'hidden', borderRadius: 3, flexShrink: 0}}>
            <div style={{transform: 'scale(0.1)', transformOrigin: 'top left'}}>
              <Page w={300} h={390} tilt={0} />
            </div>
          </div>
          <div style={{flex: 1}}>
            <div style={{fontSize: 15.5}}>Handwritten notes</div>
            <div style={{fontSize: 13, color: colors.text2, marginTop: 2}}>{reading ? 'Your notes · 2.1 MB · Reading your handwriting…' : 'Your notes · 2.1 MB · 1 page'}</div>
          </div>
          <div style={{color: colors.text3, fontSize: 14}}>✕</div>
        </div>
      ) : null}
      <div style={{display: 'flex', alignItems: 'center', gap: 12, marginTop: 8, padding: '9px 12px', borderRadius: 11, background: colors.card}}>
        <div style={{width: 30, height: 38, borderRadius: 4, background: 'linear-gradient(135deg, #3B82F6, #1D4ED8)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 700}}>PDF</div>
        <div style={{flex: 1}}>
          <div style={{fontSize: 15.5}}>Lecture 7 slides</div>
          <div style={{fontSize: 13, color: colors.text2, marginTop: 2}}>Slides · 4.8 MB</div>
        </div>
        <div style={{color: colors.text3, fontSize: 14}}>✕</div>
      </div>
      {bar ? (
        <div style={{display: 'flex', alignItems: 'center', gap: 12, marginTop: 12, padding: '12px 12px 12px 16px', borderRadius: 16, background: 'rgba(30,42,56,0.96)', border: `1px solid ${colors.edge}`, opacity: spr(frame, READ, {damping: 24, stiffness: 160, mass: 0.9})}}>
          <div style={{flex: 1, fontSize: 15}}>Your notes were written before some of these came.</div>
          <div style={{padding: '9px 16px', borderRadius: 999, background: colors.lagoon, color: '#fff', fontSize: 14.5, fontWeight: 700, whiteSpace: 'nowrap', transform: `scale(${frame >= REWRITE && frame < REWRITE + 4 ? 0.96 : 1})`}}>
            {frame >= REWRITE ? 'Rewriting…' : 'Rewrite notes with your attachments'}
          </div>
        </div>
      ) : null}
    </div>
  );
};

export const Handwriting: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  // Where things sit. Landscape: the phone on the left, the lecture window on the right. Vertical: the phone above.
  const phoneScale = vertical ? 0.72 : 0.88;
  const phone = vertical ? {x: (width - PHONE_W * 0.72) / 2, y: 330} : {x: 170, y: 232};
  const z = vertical ? 1.05 : 1;
  const w = vertical ? 900 : 1120;
  const h = vertical ? 700 : 720;
  const win = vertical ? {x: (width - w * z) / 2, y: 930} : {x: width - 1120 - 110, y: BELOW_TITLE};
  const listW = vertical ? 0 : 290;
  const inP = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const phoneIn = spr(frame, 2, {damping: 22, stiffness: 120, mass: 1});
  // The photo's flight: from its row on the phone to its row in Attachments.
  const from = {x: phone.x + 44 * phoneScale + 13 * phoneScale, y: phone.y + 330 * phoneScale};
  const to = {x: win.x + (listW + 52) * z, y: win.y + (vertical ? 312 : 322) * z};
  const fly = ease(frame, SENT, LANDS, Easing.inOut(Easing.cubic));
  const flying = frame >= SENT && frame < LANDS;
  const arc = Math.sin(fly * Math.PI) * -120;
  // The middle of "Rewrite notes with your attachments", at the right of the page.
  const button = {x: win.x + (vertical ? 703 : listW + 632) * z, y: win.y + (vertical ? 478 : 458) * z};
  return (
    <AbsoluteFill>
      <Desktop>
        <div style={{position: 'absolute', left: win.x / z, top: win.y / z, zoom: z, opacity: inP, transform: `translateY(${(1 - inP) * 30}px)`}}>
          <LibraryWindow
            w={w}
            h={h}
            list={
              vertical ? undefined : (
                <>
                  <ListHead title="CS 101" sub="12 lectures" />
                  <Group name="This week" />
                  <ListRow title="Recursion and the call stack" sub="Tue 23 Sep · 1 h 12 min" color={colors.cs} lit />
                  <ListRow title="Stack frames and scope" sub="Thu 18 Sep · 1 h 14 min" color={colors.cs} />
                  <Group name="Last week" />
                  <ListRow title="Loops and invariants" sub="Thu 11 Sep · 1 h 13 min" color={colors.cs} />
                </>
              )
            }
          >
            <LecturePage frame={frame} />
          </LibraryWindow>
        </div>
        <div style={{position: 'absolute', left: phone.x, top: phone.y, transform: `translateY(${(1 - phoneIn) * 80}px) scale(${phoneScale})`, transformOrigin: 'top left', opacity: phoneIn}}>
          <Phone frame={frame} />
        </div>
        {flying ? (
          <div
            style={{
              position: 'absolute',
              left: interpolate(fly, [0, 1], [from.x, to.x]),
              top: interpolate(fly, [0, 1], [from.y, to.y]) + arc,
              transform: `scale(${interpolate(fly, [0, 0.5, 1], [0.16, 0.3, 0.1])}) rotate(${interpolate(fly, [0, 1], [-6, 0])}deg)`,
              transformOrigin: 'top left',
              zIndex: 30,
            }}
          >
            <Page w={300} h={390} tilt={0} />
          </div>
        ) : null}
        <Cursor
          stops={[
            [READ - 20, button.x + 200, button.y + 180],
            [REWRITE - 4, button.x, button.y],
            [REWRITE + 30, button.x, button.y],
            [REWRITE + 60, button.x + 140, button.y + 120],
          ]}
          clicks={[REWRITE]}
        />
      </Desktop>
      <Sfx at={CAMERA} name="pop" volume={0.18} />
      <Sfx at={SHUTTER} name="click" volume={0.5} />
      <Sfx at={SENT - 2} name="whoosh" volume={0.14} />
      <Sfx at={LANDS} name="pop" volume={0.24} />
      <Sfx at={REWRITTEN} name="chime-filed" volume={0.3} />
      <Title text={text.handwriting as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};
