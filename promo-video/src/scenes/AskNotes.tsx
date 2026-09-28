import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {ease, spr, typed, words} from '../anim';
import {BELOW_TITLE, Title, Cursor, Desktop, useVertical} from '../components/Layout';

// The quick panel, as in docs/images/quick-panel.png, in the light look.
const W = 900;
const QUESTION = "what's on the cs 101 midterm?";
const ANSWER = "Recursion traces and call-stack diagrams, from Tuesday's lecture. Scope rules are fair game, but Big-O proofs aren't.";
const PICKER = 40; // the engine chip is clicked
const PICKED = 70; // Claude Code is chosen
const ASKED = 78; // the question is sent

// Who can answer: the engines Study Stash works with (Providers.cs), each signed in with the student's own plan.
const ENGINES: [string, string, string][] = [
  ['Claude Code', 'Anthropic · your Claude plan', 'Signed in'],
  ['Codex', 'OpenAI · your ChatGPT plan', 'Signed in'],
  ['Ollama', 'Free, on this computer', 'Ready'],
  ['Gemini', 'Google', 'Set up'],
];

const Sparkle: React.FC<{size?: number; color?: string}> = ({size = 30, color = colors.lagoon}) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={color}>
    <path d="M10 2.5l1.6 4.9a3 3 0 0 0 1.9 1.9l4.9 1.6-4.9 1.6a3 3 0 0 0-1.9 1.9L10 19.3l-1.6-4.9a3 3 0 0 0-1.9-1.9L1.6 10.9l4.9-1.6a3 3 0 0 0 1.9-1.9z" />
    <path d="M19 14.5l.7 2a1.5 1.5 0 0 0 .8.8l2 .7-2 .7a1.5 1.5 0 0 0-.8.8l-.7 2-.7-2a1.5 1.5 0 0 0-.8-.8l-2-.7 2-.7a1.5 1.5 0 0 0 .8-.8z" />
  </svg>
);

const Panel: React.FC<{style?: React.CSSProperties; children: React.ReactNode}> = ({style, children}) => (
  <div
    style={{
      background: 'rgba(250,252,253,0.97)',
      border: '1px solid rgba(16,24,40,0.09)',
      borderRadius: 26,
      boxShadow: '0 1px 2px rgba(16,24,40,0.06), 0 30px 70px -24px rgba(34,52,100,0.42)',
      fontFamily: fonts.ui,
      color: colors.text,
      ...style,
    }}
  >
    {children}
  </div>
);

export const AskNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const scale = vertical ? 1.08 : 1.12;
  const left = (width - W * scale) / 2;
  const top = vertical ? 450 : BELOW_TITLE + 8;
  const inP = spr(frame, 0, {damping: 24, stiffness: 150, mass: 0.9});
  const menu = frame >= PICKER + 2 && frame < PICKED + 4;
  const menuP = spr(frame, PICKER + 2, {damping: 22, stiffness: 190, mass: 0.7});
  const chosen = frame >= PICKED;
  const answer = words(ANSWER, frame, ASKED + 6, 12);
  const panel = spr(frame, ASKED, {damping: 24, stiffness: 150, mass: 0.9});
  const sources = ASKED + 58;
  const chipX = left + (W - 262) * scale;
  const caret = frame < ASKED && Math.floor(frame / 8) % 2 === 0;
  return (
    <AbsoluteFill>
      <Desktop>
        <div style={{position: 'absolute', left, top, width: W, transform: `scale(${scale})`, transformOrigin: 'top left'}}>
          <div style={{opacity: inP, transform: `translateY(${(1 - inP) * -12}px) scale(${0.98 + 0.02 * inP})`}}>
            <Panel style={{height: 88, display: 'flex', alignItems: 'center', gap: 18, padding: '0 16px 0 28px', position: 'relative'}}>
              <Sparkle />
              <div style={{flex: 1, fontSize: 30, fontWeight: 500, letterSpacing: '-0.01em'}}>
                {typed(QUESTION, frame, 6, 30)}
                <span style={{display: 'inline-block', width: 2.5, height: 32, marginLeft: 2, verticalAlign: -5, background: caret ? colors.lagoon : 'transparent'}} />
              </div>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '11px 16px',
                  borderRadius: 999,
                  background: menu ? 'rgba(16,24,40,0.10)' : 'rgba(16,24,40,0.06)',
                  fontSize: 17,
                  fontWeight: 600,
                  width: 236,
                  whiteSpace: 'nowrap',
                }}
              >
                <Sparkle size={18} />
                {chosen ? 'Claude Code' : 'Answer with'}
                <div style={{flex: 1}} />
                <span style={{color: colors.text2}}>⌃⌄</span>
              </div>
            </Panel>
          </div>
          {menu ? (
            <div style={{position: 'absolute', right: 0, top: 96, width: 430, opacity: menuP, transform: `translateY(${(1 - menuP) * -8}px)`, zIndex: 5}}>
              <Panel style={{padding: 8, borderRadius: 18}}>
                <div style={{fontSize: 13.5, fontWeight: 600, color: colors.text2, padding: '8px 12px 6px'}}>Answer with</div>
                {ENGINES.map(([name, maker, state], i) => {
                  const hover = i === 0 && frame >= PICKER + 16;
                  return (
                    <div key={name} style={{display: 'flex', alignItems: 'center', gap: 12, padding: '11px 12px', borderRadius: 11, background: hover ? colors.lagoon : 'transparent', color: hover ? '#fff' : colors.text}}>
                      <div style={{flex: 1}}>
                        <div style={{fontSize: 17, fontWeight: 600}}>{name}</div>
                        <div style={{fontSize: 13.5, color: hover ? 'rgba(255,255,255,0.85)' : colors.text2, marginTop: 1}}>{maker}</div>
                      </div>
                      <div style={{fontSize: 13.5, fontWeight: 500, color: hover ? '#fff' : state === 'Set up' ? colors.text3 : colors.lagoonDeep}}>{state}</div>
                    </div>
                  );
                })}
              </Panel>
            </div>
          ) : null}
          {frame >= ASKED ? (
            <div style={{marginTop: 16, opacity: panel, transform: `translateY(${(1 - panel) * -16}px)`}}>
              <Panel style={{padding: '24px 28px 20px'}}>
                <div style={{fontSize: 15, fontWeight: 600, color: colors.text2}}>From your notes · Claude Code</div>
                <div style={{fontFamily: fonts.serif, fontSize: 28, lineHeight: 1.45, marginTop: 10, minHeight: 122, color: '#232A38'}}>
                  {answer.shown}
                  {!answer.done ? <span style={{color: colors.lagoon}}> ▍</span> : null}
                </div>
                <div style={{fontSize: 15, fontWeight: 600, color: colors.text2, marginTop: 14, opacity: ease(frame, sources - 6, sources + 4)}}>Sources</div>
                {[
                  ['Recursion and the call stack', '18:05'],
                  ['Stack frames and scope', '41:20'],
                ].map(([name, time], i) => {
                  const p = spr(frame, sources + i * 6, {damping: 24, stiffness: 150, mass: 0.9});
                  const lit = i === 0;
                  return (
                    <div
                      key={name}
                      style={{
                        opacity: p,
                        transform: `translateY(${(1 - p) * 10}px)`,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 14,
                        marginTop: 8,
                        padding: '12px 16px',
                        borderRadius: 14,
                        fontSize: 19,
                        fontWeight: 600,
                        background: lit ? colors.lagoon : 'transparent',
                        color: lit ? '#fff' : colors.text,
                      }}
                    >
                      <svg width="22" height="22" viewBox="0 0 24 24">
                        <circle cx="12" cy="12" r="10" fill={lit ? '#fff' : 'none'} stroke={lit ? '#fff' : colors.text2} strokeWidth="2" />
                        <path d="M10 8.5l6 3.5-6 3.5z" fill={lit ? colors.lagoon : colors.text2} />
                      </svg>
                      <div style={{flex: 1}}>{name}</div>
                      <div style={{fontWeight: 500, fontVariantNumeric: 'tabular-nums', color: lit ? '#fff' : colors.text2}}>{time}</div>
                    </div>
                  );
                })}
              </Panel>
            </div>
          ) : null}
        </div>
        <Cursor
          stops={[
            [26, chipX + 60, top + 300 * scale],
            [PICKER - 2, chipX + 90, top + 42 * scale],
            [PICKER + 6, chipX + 90, top + 42 * scale],
            [PICKER + 16, chipX + 40, top + 152 * scale],
            [PICKED + 20, chipX + 40, top + 152 * scale],
            [PICKED + 50, chipX + 160, top + 420 * scale],
          ]}
          clicks={[PICKER, PICKED]}
        />
      </Desktop>
      <Title text={text.ask as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};
