import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {spr, typed, words} from '../anim';
import {BELOW_TITLE, ClassDot, Cursor, Desktop, Title, useVertical} from '../components/Layout';
import {Group, LibraryWindow, ListHead, ListRow, Sidebar} from '../components/Sidebar';
import {Sfx, Typing} from '../components/Sfx';

// Asking about a lecture, as in the app: the Ask bar under the notes, its "Answer with" menu (the app's own
// "mac-16-ai-ask-picker" shot) and the answer card above the bar ("mac-04-full-app-answer").
const PICKER = 32; // the engine chip is clicked
const PICKED = 62; // Codex is chosen
const SEND = 102; // the question is sent
const QUESTION = "What's on the midterm?";
const ANSWER = "Recursion traces and call-stack diagrams, from Tuesday's lecture. Scope rules are fair game, but Big-O proofs aren't.";

// The engines Study Stash works with (Core/Ai/Providers.cs), each signed in with the student's own plan.
const ENGINES: [name: string, line: string][] = [
  ['Claude Code', 'Anthropic · default for questions'],
  ['Codex', 'OpenAI · your ChatGPT plan'],
  ['Ollama', 'Private, on your library'],
];

const Sparkle: React.FC<{size?: number}> = ({size = 16}) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={colors.lagoonBright}>
    <path d="M10 2.5l1.6 4.9a3 3 0 0 0 1.9 1.9l4.9 1.6-4.9 1.6a3 3 0 0 0-1.9 1.9L10 19.3l-1.6-4.9a3 3 0 0 0-1.9-1.9L1.6 10.9l4.9-1.6a3 3 0 0 0 1.9-1.9z" />
    <path d="M19 14.5l.7 2a1.5 1.5 0 0 0 .8.8l2 .7-2 .7a1.5 1.5 0 0 0-.8.8l-.7 2-.7-2a1.5 1.5 0 0 0-.8-.8l-2-.7 2-.7a1.5 1.5 0 0 0 .8-.8z" />
  </svg>
);

const Chevrons = () => (
  <svg width="12" height="14" viewBox="0 0 12 16" fill="none" stroke={colors.text2} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <path d="M2.5 6L6 2.8 9.5 6M2.5 10L6 13.2 9.5 10" />
  </svg>
);

const chip: React.CSSProperties = {
  position: 'absolute',
  top: 9,
  height: 40,
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '0 14px',
  borderRadius: 999,
  background: 'rgba(255,255,255,0.09)',
  border: `1px solid ${colors.edge}`,
  fontSize: 15.5,
  fontWeight: 600,
  whiteSpace: 'nowrap',
  boxSizing: 'border-box',
};

// The Ask bar's pieces, measured from its right edge.
const SEND_W = 40;
const LECTURE = {right: 58, w: 132};
const ENGINE = {right: 202, w: 164};
const BAR_H = 58;

const Notes: React.FC = () => (
  <div style={{padding: '34px 40px 0'}}>
    <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
      <ClassDot color={colors.cs} size={8} /> CS 101 · Tuesday 23 September · 1 h 12 min
    </div>
    <div style={{fontFamily: fonts.display, fontSize: 36, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Recursion and the call stack</div>
    <div style={{display: 'inline-flex', marginTop: 14, padding: 3, borderRadius: 999, background: colors.card, border: `1px solid ${colors.edge}`, fontSize: 14.5, fontWeight: 600}}>
      <div style={{padding: '6px 18px', borderRadius: 999, background: 'rgba(255,255,255,0.12)'}}>Notes</div>
      <div style={{padding: '6px 18px', color: colors.text2}}>Transcript</div>
    </div>
    <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 22}}>Summary</div>
    <div style={{fontFamily: fonts.serif, fontSize: 19.5, lineHeight: 1.55, marginTop: 8, color: colors.serifInk}}>
      A recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack, which holds that call's arguments and local variables.
    </div>
    <div style={{fontFamily: fonts.display, fontSize: 20, fontWeight: 600, marginTop: 22}}>Key points</div>
    {['Every recursive function needs a base case.', 'The most recent call finishes first.'].map((k) => (
      <div key={k} style={{display: 'flex', gap: 14, fontFamily: fonts.serif, fontSize: 19.5, marginTop: 10, color: colors.serifInk}}>
        <span style={{color: colors.text3}}>•</span>
        {k}
      </div>
    ))}
  </div>
);

export const AskNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  // On a phone-shaped frame: just the page, laid out as on a laptop and shown 1.2 times as big.
  const z = vertical ? 1.2 : 1;
  const w = vertical ? 784 : 1320;
  const h = vertical ? 820 : 800;
  const left = (width - w * z) / 2;
  const top = vertical ? 430 : BELOW_TITLE;
  const detailLeft = vertical ? 0 : 252 + 290;
  const detailW = w - detailLeft;
  const bar = {left: 24, width: detailW - 48, top: h - 20 - BAR_H};
  const barRight = bar.left + bar.width;
  // Where things are, in the window, for the pointer.
  const at = (x: number, y: number) => ({x: left + (detailLeft + x) * z, y: top + y * z});
  const engine = at(barRight - ENGINE.right - ENGINE.w / 2, bar.top + 29);
  const menuLeft = barRight - ENGINE.right - ENGINE.w - 110;
  const menuBottom = h - bar.top + 10; // measured up from the window's bottom
  const menuTop = bar.top - 10 - 268;
  const codex = at(menuLeft + 140, menuTop + 8 + 34 + 58 + 29);
  const send = at(barRight - 9 - SEND_W / 2, bar.top + 29);

  const win = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const menuOpen = frame >= PICKER + 2 && frame < PICKED + 4;
  const menuP = spr(frame, PICKER + 2, {damping: 22, stiffness: 190, mass: 0.7});
  const codexHover = frame >= PICKER + 20;
  const chosen = frame >= PICKED ? 'Codex' : 'Claude Code';
  const question = frame >= SEND ? '' : typed(QUESTION, frame, 68, 20);
  const sent = frame >= SEND;
  const card = spr(frame, SEND + 2, {damping: 24, stiffness: 160, mass: 0.9});
  const answer = words(ANSWER, frame, SEND + 8, 14);

  return (
    <AbsoluteFill>
      <Desktop>
        <div style={{position: 'absolute', left: left / z, top: top / z, zoom: z, opacity: win, transform: `translateY(${(1 - win) * 30}px) scale(${0.98 + 0.02 * win})`}}>
          <LibraryWindow
            w={w}
            h={h}
            sidebar={
              vertical ? undefined : (
                <Sidebar
                  rows={[
                    {name: 'CS 101', color: colors.cs, count: 12, lit: true},
                    {name: 'BIO 110', color: colors.bio, count: 9},
                    {name: 'CALC II', color: colors.calc, count: 11},
                    {name: 'NURS 210', color: colors.health, count: 8},
                    {name: 'HIST 210', color: colors.hist, count: 7},
                  ]}
                />
              )
            }
            list={
              vertical ? undefined : (
                <>
                  <ListHead title="CS 101" sub="12 lectures" />
                  <Group name="This week" />
                  <ListRow title="Recursion and the call stack" sub="Tue 23 Sep · 1 h 12 min" color={colors.cs} lit />
                  <ListRow title="Stack frames and scope" sub="Thu 18 Sep · 1 h 14 min" color={colors.cs} />
                  <ListRow title="Functions as values" sub="Tue 16 Sep · 1 h 10 min" color={colors.cs} />
                  <Group name="Last week" />
                  <ListRow title="Loops and invariants" sub="Thu 11 Sep · 1 h 13 min" color={colors.cs} />
                </>
              )
            }
          >
            <Notes />
            {/* The answer card, above the Ask bar. */}
            {sent ? (
              <div
                style={{
                  position: 'absolute',
                  left: bar.left,
                  width: bar.width,
                  bottom: h - bar.top + 12,
                  padding: '16px 20px 14px',
                  borderRadius: 20,
                  background: colors.popup,
                  border: `1px solid ${colors.edge}`,
                  boxShadow: '0 24px 50px -20px rgba(0,0,0,0.7)',
                  opacity: card,
                  transform: `translateY(${(1 - card) * 14}px)`,
                  boxSizing: 'border-box',
                }}
              >
                <div style={{display: 'flex', fontSize: 14.5, fontWeight: 600, color: colors.text2}}>
                  <div style={{flex: 1}}>{QUESTION}</div>
                  <span style={{fontSize: 16}}>✕</span>
                </div>
                <div style={{fontSize: 18, lineHeight: 1.5, marginTop: 8, minHeight: 81, color: colors.text}}>
                  {answer.shown}
                  {!answer.done ? <span style={{color: colors.lagoonBright}}> ▍</span> : null}
                </div>
                <div style={{fontSize: 14, color: colors.text2, marginTop: 6, opacity: answer.done ? 1 : 0}}>Codex · from 18:05 and 41:20</div>
              </div>
            ) : null}
            {/* The Ask bar. */}
            <div
              style={{
                position: 'absolute',
                left: bar.left,
                top: bar.top,
                width: bar.width,
                height: BAR_H,
                borderRadius: 999,
                background: 'rgba(30, 42, 56, 0.96)',
                border: `1px solid ${colors.edge}`,
                boxShadow: '0 18px 40px -16px rgba(0,0,0,0.7)',
                fontSize: 16.5,
                boxSizing: 'border-box',
              }}
            >
              <div style={{position: 'absolute', left: 22, top: 0, height: BAR_H, display: 'flex', alignItems: 'center', color: question ? colors.text : colors.text3}}>
                {question || 'Ask about this lecture'}
                {question && !sent ? <span style={{display: 'inline-block', width: 2, height: 20, marginLeft: 2, background: Math.floor(frame / 8) % 2 ? colors.lagoonBright : 'transparent'}} /> : null}
              </div>
              <div style={{...chip, right: ENGINE.right, width: ENGINE.w, background: menuOpen ? 'rgba(255,255,255,0.16)' : chip.background}}>
                <Sparkle />
                {chosen}
                <div style={{flex: 1}} />
                <Chevrons />
              </div>
              <div style={{...chip, right: LECTURE.right, width: LECTURE.w}}>
                This lecture
                <div style={{flex: 1}} />
                <Chevrons />
              </div>
              <div style={{position: 'absolute', right: 9, top: 8, width: SEND_W, height: SEND_W, borderRadius: 20, background: question.length === QUESTION.length && !sent ? colors.lagoon : 'rgba(255,255,255,0.09)', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={colors.text} strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 19V5M6 11l6-6 6 6" />
                </svg>
              </div>
            </div>
            {/* The "Answer with" menu, above the engine chip. */}
            {menuOpen ? (
              <div
                style={{
                  position: 'absolute',
                  left: menuLeft,
                  bottom: menuBottom,
                  width: 330,
                  padding: 8,
                  borderRadius: 16,
                  background: colors.popup,
                  border: `1px solid ${colors.edge}`,
                  boxShadow: '0 24px 50px -16px rgba(0,0,0,0.75)',
                  opacity: menuP,
                  transform: `translateY(${(1 - menuP) * 8}px)`,
                  zIndex: 5,
                  boxSizing: 'border-box',
                }}
              >
                <div style={{fontSize: 14, fontWeight: 700, color: colors.text2, padding: '8px 12px 6px'}}>Answer with</div>
                {ENGINES.map(([name, line], i) => {
                  const hover = i === 1 && codexHover;
                  return (
                    <div key={name} style={{display: 'flex', gap: 10, padding: '9px 12px', borderRadius: 11, background: hover ? colors.lagoon : 'transparent'}}>
                      <div style={{width: 14, fontSize: 14, color: colors.text, paddingTop: 1}}>{i === 0 ? '✓' : ''}</div>
                      <div>
                        <div style={{fontSize: 16.5, fontWeight: 600}}>{name}</div>
                        <div style={{fontSize: 13.5, color: hover ? 'rgba(255,255,255,0.85)' : colors.text2, marginTop: 2}}>{line}</div>
                      </div>
                    </div>
                  );
                })}
                <div style={{height: 1, background: colors.line, margin: '6px 10px'}} />
                <div style={{fontSize: 15, color: colors.text2, padding: '8px 12px 6px 36px'}}>Change defaults in Settings</div>
              </div>
            ) : null}
          </LibraryWindow>
        </div>
        <Cursor
          stops={[
            [6, engine.x + 180, engine.y - 320],
            [PICKER - 4, engine.x, engine.y],
            [PICKER + 10, engine.x, engine.y],
            [PICKER + 22, codex.x, codex.y],
            [PICKED + 4, codex.x, codex.y],
            [SEND - 8, send.x, send.y],
            [SEND + 30, send.x, send.y],
            [SEND + 60, send.x - 160, send.y + 60],
          ]}
          clicks={[PICKER, PICKED, SEND]}
        />
      </Desktop>
      <Sfx at={PICKER + 2} name="pop" volume={0.2} />
      <Typing start={68} count={QUESTION.length} perSecond={20} />
      <Sfx at={SEND + 2} name="pop" volume={0.26} />
      <Title text={text.ask as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};
