import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../../config';
import {ClassDot, Cursor, Desktop, Title} from '../../components/Layout';
import {Group, ListHead} from '../../components/Sidebar';
import {prog, px} from '../../promo2/scenes/Bookends';
import {useStage} from '../../promo2/ui';
import {cue, cueLength, titles} from '../config';
import {DemoWindow, G, LectureRow} from '../chrome';

// Asking about a lecture, as the app does it (MacAiAskBar and MacAiAnswer, the "mac-04-full-app-answer" and
// "mac-16-ai-answer-being-written" shots): the Ask bar under the notes, with the engine (Claude Code) and what to ask
// about (This lecture). The question is typed and sent; the answer is written in above the bar as it comes, from what
// was said in class, with the moments it drew on.

const QUESTION = 'What’s on the midterm?';
const ANSWER =
  'Recursion traces and call-stack diagrams. She said you’ll draw the stack at its deepest point, for something like factorial(4). Scope rules are fair game; Big-O proofs won’t be on it.';

const BAR_H = 58;

const Chip: React.FC<{children: React.ReactNode}> = ({children}) => (
  <div style={{height: 38, display: 'flex', alignItems: 'center', gap: 8, padding: '0 13px', borderRadius: 19, background: 'rgba(255,255,255,0.08)', border: `1px solid ${colors.edge}`, fontSize: 15.5, fontWeight: 600, whiteSpace: 'nowrap', boxSizing: 'border-box'}}>
    {children}
  </div>
);

export const Ask: React.FC = () => {
  const f = useCurrentFrame();
  const stage = useStage({list: true});
  const {vertical, page, h} = stage;
  const enter = prog(f, 0, 18);
  const KEYS = cue('ask-keys');
  const TYPED = KEYS + cueLength('ask-keys');
  const SEND = cue('ask-send');

  // Typed at an even pace over the keys' sound.
  const n = Math.max(0, Math.min(QUESTION.length, Math.floor(((f - KEYS) / (TYPED - KEYS)) * QUESTION.length + 0.5)));
  const question = f >= SEND ? '' : QUESTION.slice(0, n);
  const sent = f >= SEND;
  const card = prog(f, SEND + 2, SEND + 12);
  // The answer is written in as it comes: a few words a moment.
  const words = ANSWER.split(' ');
  const shown = Math.max(0, Math.min(words.length, Math.floor((f - SEND - 12) / 4.2)));
  const done = shown >= words.length;

  const barW = page.w;
  const barTop = h - 22 - BAR_H;
  const sendAt = stage.at(page.x + barW - 9 - 20, barTop + BAR_H / 2);
  const caret = !sent && Math.floor(f / 15) % 2 === 0;

  const list = (
    <>
      <ListHead title="CS 101" sub="12 lectures" />
      <Group name="This week" />
      <LectureRow title="Recursion and the call stack" meta="Mon 22 Sep · 50 min" snippet="A recursive function solves a problem by calling itself on a smaller version of it." color={colors.cs} lit />
      <LectureRow title="Stack frames and scope" meta="Fri 19 Sep · 50 min" snippet="Where a variable lives decides who can see it, and for how long." color={colors.cs} />
      <LectureRow title="Functions as values" meta="Wed 17 Sep · 50 min" snippet="Passing a function to another function, and why map and filter work." color={colors.cs} />
      <Group name="Last week" />
      <LectureRow title="Loops and invariants" meta="Fri 12 Sep · 50 min" snippet="An invariant is a statement that stays true on every pass through a loop." color={colors.cs} />
    </>
  );

  return (
    <AbsoluteFill>
      <Desktop clock="Wed 24 Sep  21:10">
        <DemoWindow stage={stage} lit="cs" list={vertical ? undefined : list} enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: vertical ? 70 : 62, width: page.w}}>
            <div style={{display: 'flex', alignItems: 'center', gap: 9, fontSize: 15, color: colors.text2}}>
              <ClassDot color={colors.cs} size={8} /> CS 101 · Monday 22 September · 50 min
            </div>
            <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Recursion and the call stack</div>
            <div style={{display: 'inline-flex', marginTop: 12, padding: 3, borderRadius: 999, background: colors.card, border: `1px solid ${colors.edge}`, fontSize: 14.5, fontWeight: 600}}>
              <div style={{padding: '6px 18px', borderRadius: 999, background: 'rgba(255,255,255,0.12)'}}>Notes</div>
              <div style={{padding: '6px 18px', color: colors.text2}}>Transcript</div>
            </div>
            <div style={{fontFamily: fonts.display, fontSize: 21, fontWeight: 600, marginTop: 22}}>Summary</div>
            <div style={{fontFamily: fonts.serif, fontSize: 19, lineHeight: 1.55, marginTop: 8, color: colors.serifInk}}>
              A recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack, which holds that call’s arguments and local variables. The calls pause in order until a base case returns.
            </div>
            <div style={{fontFamily: fonts.display, fontSize: 21, fontWeight: 600, marginTop: 22}}>Key points</div>
            {['Every recursive function needs a base case that returns without calling itself.', 'The most recent call finishes first.'].map((k) => (
              <div key={k} style={{display: 'flex', gap: 14, fontFamily: fonts.serif, fontSize: 19, marginTop: 10, color: colors.serifInk}}>
                <span style={{color: colors.text3}}>•</span>
                {k}
              </div>
            ))}
          </div>

          {/* The answer, above the Ask bar, written in as it comes. */}
          {sent ? (
            <div
              style={{
                position: 'absolute',
                left: stage.pad,
                width: barW,
                bottom: h - barTop + 12,
                padding: '16px 20px 14px',
                borderRadius: 22,
                background: '#232A2E',
                border: `1px solid ${colors.edge}`,
                boxShadow: '0 24px 50px -20px rgba(0,0,0,0.75)',
                opacity: card,
                transform: `translateY(${px((1 - card) * 14)}px)`,
                boxSizing: 'border-box',
              }}
            >
              <div style={{display: 'flex', alignItems: 'center', fontSize: 14, fontWeight: 600, color: colors.text2}}>
                <div style={{flex: 1}}>{QUESTION}</div>
                <G name="close" size={16} color={colors.text2} />
              </div>
              <div style={{fontSize: 17.5, lineHeight: 1.5, marginTop: 8, minHeight: 79, color: colors.text}}>
                {words.slice(0, shown).join(' ')}
                {!done ? <span style={{color: colors.lagoonBright}}> ▍</span> : null}
              </div>
              <div style={{fontSize: 14, color: colors.text2, marginTop: 6, opacity: prog(f, SEND + 12 + words.length * 4.2, SEND + 20 + words.length * 4.2)}}>Claude Code · from 12:40, 31:05 and 44:20</div>
            </div>
          ) : null}

          {/* The Ask bar. */}
          <div
            style={{
              position: 'absolute',
              left: stage.pad,
              top: barTop,
              width: barW,
              height: BAR_H,
              borderRadius: 29,
              background: 'rgba(36, 44, 50, 0.98)',
              border: `1px solid ${colors.edge}`,
              boxShadow: '0 18px 40px -16px rgba(0,0,0,0.7)',
              boxSizing: 'border-box',
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              padding: '0 9px 0 22px',
              fontSize: 16.5,
            }}
          >
            <div style={{flex: 1, display: 'flex', alignItems: 'center', color: question ? colors.text : colors.text3, whiteSpace: 'nowrap', overflow: 'hidden'}}>
              {question || (sent ? 'Ask about this lecture' : '')}
              {!sent ? <span style={{display: 'inline-block', width: 2, height: 20, marginLeft: question ? 2 : 0, background: caret ? colors.lagoonBright : 'transparent'}} /> : null}
              {!sent && !question ? <span style={{marginLeft: 2}}>Ask about this lecture</span> : null}
            </div>
            <Chip>
              <G name="sparkle" size={15} color={colors.lagoonBright} /> Claude Code <G name="updown" size={14} color={colors.text2} />
            </Chip>
            {vertical ? null : (
              <Chip>
                This lecture <G name="updown" size={14} color={colors.text2} />
              </Chip>
            )}
            <div style={{width: 40, height: 40, borderRadius: 20, background: question.length === QUESTION.length && !sent ? colors.lagoon : 'rgba(255,255,255,0.08)', display: 'flex', alignItems: 'center', justifyContent: 'center', transform: `scale(${px(1 - 0.08 * Math.max(0, 1 - Math.abs(f - SEND - 1) / 3))})`}}>
              {sent && !done ? <G name="stop" size={16} /> : <G name="up" size={18} />}
            </div>
          </div>
        </DemoWindow>
        <Cursor
          softPress
          stops={[
            [6, sendAt.x - 260, sendAt.y - 330],
            [KEYS, sendAt.x - 200, sendAt.y - 260],
            [SEND - 8, sendAt.x - 4, sendAt.y - 6],
            [SEND + 20, sendAt.x - 4, sendAt.y - 6],
            [SEND + 60, sendAt.x + 60, sendAt.y + 40],
          ]}
          clicks={[SEND]}
        />
      </Desktop>
      <Title text={titles.ask!} delay={10} />
    </AbsoluteFill>
  );
};
