import React from 'react';
import {useCurrentFrame} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {ease, outro, rise, spr, typed, words} from '../anim';
import {Feature, Glass} from '../components/Layout';

const W = 900;
const H = 610;
const QUESTION = "what's on the cs 101 midterm?";
const ANSWER =
  "Recursion traces and call-stack diagrams, from Tuesday's lecture. Scope rules are fair game, but Big-O proofs aren't.";
const ASKED = 50;

const Sparkle: React.FC<{size?: number}> = ({size = 34}) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={colors.lagoonBright}>
    <path d="M10 2.5l1.6 4.9a3 3 0 0 0 1.9 1.9l4.9 1.6-4.9 1.6a3 3 0 0 0-1.9 1.9L10 19.3l-1.6-4.9a3 3 0 0 0-1.9-1.9L1.6 10.9l4.9-1.6a3 3 0 0 0 1.9-1.9z" />
    <path d="M19 14.5l.7 2a1.5 1.5 0 0 0 .8.8l2 .7-2 .7a1.5 1.5 0 0 0-.8.8l-.7 2-.7-2a1.5 1.5 0 0 0-.8-.8l-2-.7 2-.7a1.5 1.5 0 0 0 .8-.8z" />
  </svg>
);

const Play: React.FC<{lit: boolean}> = ({lit}) => (
  <svg width="26" height="26" viewBox="0 0 24 24">
    <circle cx="12" cy="12" r="10" fill={lit ? '#fff' : 'none'} stroke={lit ? '#fff' : colors.nightInk2} strokeWidth="2" />
    <path d="M10 8.5l6 3.5-6 3.5z" fill={lit ? colors.lagoon : colors.nightInk2} />
  </svg>
);

export const AskNotes: React.FC = () => {
  const frame = useCurrentFrame();
  const q = typed(QUESTION, frame, 8, 26);
  const caret = frame < ASKED && Math.floor(frame / 8) % 2 === 0;
  const panel = spr(frame, ASKED);
  const answer = words(ANSWER, frame, ASKED + 8, 10);
  const sources = ASKED + 70;
  return (
    <Feature headline={text.ask} w={W} h={H} opacity={outro(frame, durations.ask)}>
      <div style={{...rise(spr(frame, 2), 60)}}>
        <Glass radius={34} style={{height: 104, display: 'flex', alignItems: 'center', gap: 22, padding: '0 34px'}}>
          <Sparkle />
          <div style={{flex: 1, fontSize: 38, fontWeight: 500, letterSpacing: '-0.01em'}}>
            {q}
            <span style={{display: 'inline-block', width: 3, height: 40, marginLeft: 3, verticalAlign: -6, background: caret ? colors.lagoonBright : 'transparent'}} />
          </div>
          <div style={{fontSize: 19, color: colors.nightInk3}}>⌘↩ Ask your notes</div>
        </Glass>
      </div>
      <div style={{marginTop: 20, opacity: Math.min(1, panel * 1.5), transform: `translateY(${(1 - panel) * -30}px) scaleY(${0.9 + panel * 0.1})`, transformOrigin: 'top center'}}>
        <Glass radius={30} style={{padding: '30px 34px 26px'}}>
          <div style={{fontSize: 19, fontWeight: 600, color: colors.nightInk3}}>From your notes</div>
          <div style={{fontFamily: fonts.serif, fontSize: 32, lineHeight: 1.45, marginTop: 14, minHeight: 140, color: colors.nightInk}}>
            {answer.shown}
            {!answer.done && frame >= ASKED + 8 ? <span style={{color: colors.lagoonBright}}> ▍</span> : null}
          </div>
          <div style={{...rise(spr(frame, sources - 4), 16), fontSize: 19, fontWeight: 600, color: colors.nightInk3, marginTop: 20}}>Sources</div>
          {[
            ['Recursion and the call stack', '18:05'],
            ['Stack frames and scope', '41:20'],
          ].map(([name, time], i) => {
            const lit = i === 0 && frame >= sources + 18;
            return (
              <div
                key={name}
                style={{
                  ...rise(spr(frame, sources + i * 7), 20),
                  display: 'flex',
                  alignItems: 'center',
                  gap: 16,
                  marginTop: 10,
                  padding: '14px 18px',
                  borderRadius: 16,
                  fontSize: 23,
                  fontWeight: 600,
                  background: lit ? colors.lagoon : 'transparent',
                  transition: 'none',
                }}
              >
                <Play lit={lit} />
                <div style={{flex: 1}}>{name}</div>
                <div style={{fontWeight: 500, color: lit ? '#fff' : colors.nightInk2, fontVariantNumeric: 'tabular-nums'}}>{time}</div>
              </div>
            );
          })}
          <div style={{display: 'flex', gap: 30, marginTop: 18, fontSize: 16, color: colors.nightInk3, opacity: ease(frame, sources + 20, sources + 34)}}>
            <span>↩ Open source</span>
            <span>⌘C Copy answer</span>
            <span style={{flex: 1, textAlign: 'right'}}>esc Close</span>
          </div>
        </Glass>
      </div>
    </Feature>
  );
};
