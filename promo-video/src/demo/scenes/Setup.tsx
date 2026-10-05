import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../../config';
import {COLUMN, Cursor, Desktop, menuIconX, Title, useVertical} from '../../components/Layout';
import {inOut, prog, px} from '../../promo2/scenes/Bookends';
import {ui} from '../../promo2/ui';
import {cue, length, titles} from '../config';
import {G, Toast} from '../chrome';

// The guided first launch, as the app's "mac-05-setup-guided-*" shots draw it (MacGuidedSetup, dark), at a quarter
// larger than on a Mac: Welcome to Study Stash, Claude picked; then Claude walks you through it in a chat: Just this
// Mac (Set up), the microphone check, and "You're set up" (Open Study Stash). The window closes and the first run's
// notification points at the S. in the menu bar, where the next scene picks up.

const W = 900;
const H = 560;
const RAIL = 216;
const PANE = 252; // where the right side's content starts
const teal = colors.lagoon;
const line = 'rgba(255,255,255,0.09)';
const fill = 'rgba(255,255,255,0.075)';

type Step = {label: string; sub?: string; right?: string; state: 'done' | 'now' | 'todo'};

const Mark: React.FC<{state: Step['state']; n?: number}> = ({state, n}) => {
  if (state === 'done')
    return (
      <div style={{width: 16, height: 16, borderRadius: 8, background: ui.accent, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <G name="check" size={12} color="#0B1A1C" width={3} />
      </div>
    );
  if (n !== undefined)
    return (
      <div style={{width: 16, height: 16, borderRadius: 8, border: `1.5px solid ${state === 'now' ? colors.text : colors.text3}`, boxSizing: 'border-box', fontSize: 9.5, fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center', color: state === 'now' ? colors.text : colors.text3}}>
        {n}
      </div>
    );
  return (
    <div style={{width: 16, height: 16, borderRadius: 8, border: `1.5px solid ${state === 'now' ? ui.accent : colors.text3}`, boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
      {state === 'now' ? <div style={{width: 6, height: 6, borderRadius: 3, background: ui.accent}} /> : null}
    </div>
  );
};

const Rail: React.FC<{head: string; steps: Step[]; numbered?: boolean; open?: number}> = ({head, steps, numbered, open = 0}) => (
  <div style={{position: 'absolute', left: 8, top: 53, width: RAIL, height: H - 53 - 9, borderRadius: 16, background: 'rgba(255,255,255,0.035)', border: `1px solid ${line}`, boxSizing: 'border-box', padding: '14px 10px'}}>
    <div style={{display: 'flex', alignItems: 'center', gap: 7, fontSize: 12.5, fontWeight: 600, color: colors.text2, padding: '0 8px 8px'}}>
      <G name="sparkle" size={13} color={colors.text2} /> {head}
    </div>
    {steps.map((s, i) => (
      <div key={s.label} style={{display: 'flex', gap: 10, alignItems: s.sub ? 'flex-start' : 'center', padding: '8px 9px', minHeight: 20, borderRadius: 9, background: s.state === 'now' ? 'rgba(255,255,255,0.08)' : 'transparent', marginBottom: 1}}>
        <div style={{paddingTop: s.sub ? 1 : 0}}>
          <Mark state={s.state} n={numbered ? i + 1 : undefined} />
        </div>
        <div style={{flex: 1, minWidth: 0}}>
          <div style={{fontSize: 13.5, fontWeight: s.state === 'now' ? 700 : 500, color: s.state === 'todo' ? '#C9CED3' : colors.text}}>{s.label}</div>
          {s.sub ? <div style={{fontSize: 11, color: colors.text2, marginTop: 2, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: 140}}>{s.sub}</div> : null}
        </div>
        {s.right ? <div style={{fontSize: 11, color: colors.text2, paddingTop: 2}}>{s.right}</div> : null}
      </div>
    ))}
    {open > 0 ? (
      <div style={{position: 'absolute', left: 14, right: 14, bottom: 34, height: 32, borderRadius: 16, background: teal, color: '#FFFFFF', fontSize: 13.5, fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center', opacity: open}}>
        Open Study Stash
      </div>
    ) : null}
    <div style={{position: 'absolute', left: 18, bottom: 9, fontSize: 12.5, color: colors.text2}}>Set up by hand</div>
  </div>
);

const Button: React.FC<{label: string; on?: boolean; press?: number; style?: React.CSSProperties}> = ({label, on = true, press = 0, style}) => (
  <div
    style={{
      height: 32,
      padding: '0 18px',
      borderRadius: 16,
      background: on ? teal : 'rgba(14,149,148,0.35)',
      color: on ? '#FFFFFF' : 'rgba(255,255,255,0.55)',
      fontSize: 13.5,
      fontWeight: 700,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      transform: `scale(${px(1 - 0.04 * press)})`,
      ...style,
    }}
  >
    {label}
  </div>
);

/** The press of a button around a click: in and out over a few frames. */
const pressAt = (f: number, c: number) => Math.max(0, 1 - Math.abs(f - c - 1) / 3);

// ---- Welcome: pick your AI -------------------------------------------------------------------------------------

const AiCard: React.FC<{name: string; uses: string; needs: string; link: string; chosen: number; hover: number}> = ({name, uses, needs, link, chosen, hover}) => (
  <div
    style={{
      width: 268,
      height: 176,
      boxSizing: 'border-box',
      padding: '16px 16px 14px',
      borderRadius: 14,
      background: hover > 0 ? `rgba(255,255,255,${px(0.06 + 0.03 * hover)})` : 'rgba(255,255,255,0.06)',
      border: `1px solid ${line}`,
      boxShadow: chosen > 0 ? `0 0 0 ${px(2 * chosen)}px ${ui.accent}` : 'none',
      position: 'relative',
      display: 'flex',
      flexDirection: 'column',
    }}
  >
    <div style={{display: 'flex', alignItems: 'center'}}>
      <div style={{flex: 1, fontSize: 15, fontWeight: 700}}>{name}</div>
      <div style={{width: 18, height: 18, borderRadius: 9, border: `1.5px solid ${chosen > 0.5 ? ui.accent : colors.text2}`, boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <div style={{width: 8, height: 8, borderRadius: 4, background: ui.accent, opacity: chosen}} />
      </div>
    </div>
    <div style={{fontSize: 12.5, color: colors.text2, marginTop: 8, lineHeight: 1.4}}>{uses}</div>
    <div style={{fontSize: 12.5, fontWeight: 600, marginTop: 6, lineHeight: 1.4}}>{needs}</div>
    <div style={{flex: 1}} />
    <div style={{fontSize: 12.5, color: colors.lagoonBright}}>{link} ↗</div>
  </div>
);

const Welcome: React.FC<{f: number}> = ({f}) => {
  const PICK = cue('setup-pick');
  const chosen = prog(f, PICK, PICK + 6);
  const hover = prog(f, PICK - 14, PICK - 6);
  return (
    <>
      <Rail
        head="Guided setup"
        numbered
        steps={[
          {label: 'Pick your AI', state: 'now'},
          {label: 'Install Claude Code', state: 'todo'},
          {label: 'Sign in to Claude', state: 'todo'},
          {label: 'Set up Study Stash', state: 'todo'},
        ]}
      />
      <div style={{position: 'absolute', left: PANE + 20, top: 82, width: 568}}>
        <div style={{fontSize: 24, fontWeight: 700, letterSpacing: '-0.01em'}}>Welcome to Study Stash</div>
        <div style={{fontSize: 13.5, lineHeight: 1.5, color: colors.text2, marginTop: 10, width: 470}}>
          Pick the AI that will set up Study Stash with you, write your notes and answer your questions. You’ll use your own account.
        </div>
        <div style={{display: 'flex', gap: 16, marginTop: 20}}>
          <AiCard name="Claude" uses="Uses Claude Code, from Anthropic." needs="Needs a paid Claude plan: Pro or Max." link="What’s Claude Pro?" chosen={chosen} hover={hover} />
          <AiCard name="ChatGPT" uses="Uses Codex, from OpenAI." needs="Needs a paid ChatGPT plan: Plus or higher." link="What’s ChatGPT Plus?" chosen={0} hover={0} />
        </div>
        <div style={{fontSize: 12.5, color: colors.text2, marginTop: 18}}>No subscription? Use a free model on this Mac (Ollama) ›</div>
      </div>
      <div style={{position: 'absolute', left: PANE + 20, top: H - 52, fontSize: 13.5, color: colors.text2}}>Set up by hand</div>
      <Button label="Continue" on={chosen > 0.5} style={{position: 'absolute', right: 40, top: H - 60}} />
    </>
  );
};

// ---- The chat: Claude walks you through the rest ------------------------------------------------------------------

const Said: React.FC<{who?: string; children: React.ReactNode; p?: number; style?: React.CSSProperties}> = ({who = 'Claude', children, p = 1, style}) => (
  <div style={{opacity: p, transform: `translateY(${px((1 - p) * 8)}px)`, ...style}}>
    <div style={{fontSize: 11.5, fontWeight: 700, color: colors.text2}}>{who}</div>
    <div style={{fontSize: 13.5, lineHeight: 1.45, marginTop: 3, width: 520}}>{children}</div>
  </div>
);

const Card: React.FC<{children: React.ReactNode; p?: number; style?: React.CSSProperties}> = ({children, p = 1, style}) => (
  <div style={{width: 520, boxSizing: 'border-box', padding: '14px 16px', borderRadius: 14, background: 'rgba(255,255,255,0.08)', border: `1px solid rgba(255,255,255,0.13)`, opacity: p, transform: `translateY(${px((1 - p) * 10)}px)`, ...style}}>
    {children}
  </div>
);

const Option: React.FC<{label: string; aside?: string; tag?: string; on?: boolean}> = ({label, aside, tag, on}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 10, height: 32, padding: '0 10px', borderRadius: 8, marginTop: 8, background: on ? 'rgba(255,255,255,0.06)' : fill, boxShadow: on ? `0 0 0 1.5px ${ui.accent}` : 'none'}}>
    <div style={{width: 16, height: 16, borderRadius: 8, border: on ? 'none' : `1.5px solid ${colors.text2}`, background: on ? ui.accent : 'transparent', boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
      {on ? <div style={{width: 6, height: 6, borderRadius: 3, background: '#FFFFFF'}} /> : null}
    </div>
    <div style={{fontSize: 13.5}}>{label}</div>
    {tag ? <div style={{fontSize: 11, fontWeight: 700, color: colors.lagoonBright, background: 'rgba(43,183,178,0.18)', padding: '2px 7px', borderRadius: 6}}>{tag}</div> : null}
    {aside ? <div style={{fontSize: 13.5, color: colors.text2}}>{aside}</div> : null}
  </div>
);

const Bars: React.FC<{f: number; p: number}> = ({f, p}) => (
  <div style={{display: 'flex', alignItems: 'center', gap: 2.6, height: 26, marginTop: 12}}>
    {Array.from({length: 26}, (_, i) => {
      // A voice moving the bars: a pure function of the frame.
      const v = Math.abs(Math.sin(f / 3.1 + i * 0.83) * Math.cos(f / 7.3 + i * 0.41));
      const h = px(4 + (6 + 16 * v) * p);
      return <div key={i} style={{width: 3, height: h, borderRadius: 1.5, background: colors.lagoonBright, opacity: 0.85}} />;
    })}
  </div>
);

const Reply: React.FC = () => (
  <div style={{position: 'absolute', left: PANE, top: H - 60, width: W - PANE - 28, height: 44, borderRadius: 22, background: 'rgba(255,255,255,0.075)', border: `1px solid ${line}`, boxSizing: 'border-box', display: 'flex', alignItems: 'center', padding: '0 6px 0 16px'}}>
    <div style={{flex: 1, fontSize: 13.5, color: colors.text3}}>Reply to Claude…</div>
    <div style={{width: 32, height: 32, borderRadius: 16, background: 'rgba(255,255,255,0.12)', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
      <G name="up" size={16} />
    </div>
  </div>
);

const ChatHead: React.FC = () => (
  <>
    <div style={{position: 'absolute', left: PANE, top: 51, fontSize: 12.5, color: colors.text2}}>Step 3 of 3</div>
    <div style={{position: 'absolute', left: PANE, top: 68, fontSize: 19, fontWeight: 700, letterSpacing: '-0.01em'}}>Let’s set up Study Stash</div>
    <div style={{position: 'absolute', left: PANE, right: 28, top: 104, height: 1, background: line}} />
  </>
);

const Chat: React.FC<{f: number}> = ({f}) => {
  const MAC = cue('setup-mac');
  const answered = f >= MAC + 3;
  const bubble = prog(f, MAC + 3, MAC + 11);
  const ready = prog(f, MAC + 9, MAC + 17);
  const next = prog(f, MAC + 15, MAC + 23);
  const mic = prog(f, MAC + 20, MAC + 30);
  const scroll = px(152 * prog(f, MAC + 4, MAC + 22, inOut)); // the chat moves up as it grows
  const steps: Step[] = [
    {label: 'Claude is ready', state: 'done'},
    answered ? {label: 'Just this Mac', state: 'done'} : {label: 'How you’ll use it', state: 'now'},
    answered ? {label: 'Microphone', sub: 'Allowed · say something', state: 'now'} : {label: 'Microphone', state: 'todo'},
    {label: 'Transcription model', sub: 'Not downloaded yet', state: 'todo'},
    answered ? {label: 'Notes', sub: 'Written by Claude', state: 'done'} : {label: 'Notes', state: 'todo'},
    {label: 'Classes', right: 'Optional', state: 'todo'},
    {label: 'Canvas', right: 'Optional', state: 'todo'},
    {label: 'Start at login', sub: 'Recommended', state: 'todo'},
  ];
  return (
    <>
      <Rail head="Setup with Claude" steps={steps} />
      <ChatHead />
      <div style={{position: 'absolute', left: PANE, top: 106, width: W - PANE - 28, height: H - 60 - 106 - 8, overflow: 'hidden'}}>
        <div style={{position: 'absolute', left: 0, top: 16 - scroll, width: '100%'}}>
          <Said>Hi! This takes about 5 minutes. Will you use Study Stash on just this Mac, or record on a laptop and keep your library on another computer?</Said>
          <div style={{display: 'inline-flex', alignItems: 'center', gap: 5, marginTop: 6, padding: '3px 8px', borderRadius: 7, background: 'rgba(255,255,255,0.08)', fontSize: 11.5, color: colors.text2}}>
            <G name="check" size={11} color={colors.text2} /> Checked your setup
          </div>
          <Card style={{marginTop: 14}}>
            <div style={{fontSize: 13.5, fontWeight: 700}}>How will you use Study Stash?</div>
            <Option label="Just this Mac" tag="Recommended" on />
            <Option label="This is my laptop" aside="(library elsewhere)" />
            <Option label="This is my library" aside="(a computer that stays on)" />
            <div style={{display: 'flex', justifyContent: 'flex-end', marginTop: 12}}>
              <Button label="Set up" press={pressAt(f, MAC)} on={!answered} />
            </div>
          </Card>
          {answered ? (
            <>
              <div style={{display: 'flex', justifyContent: 'flex-end', marginTop: 14, opacity: bubble, transform: `translateY(${px((1 - bubble) * 8)}px)`}}>
                <div style={{height: 32, padding: '0 14px', borderRadius: 16, background: teal, color: '#FFFFFF', fontSize: 13.5, fontWeight: 600, display: 'flex', alignItems: 'center'}}>Just this Mac</div>
              </div>
              <div style={{display: 'flex', alignItems: 'center', gap: 7, marginTop: 12, fontSize: 12.5, color: colors.text2, opacity: ready}}>
                <div style={{width: 14, height: 14, borderRadius: 7, background: ui.accent, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
                  <G name="check" size={10} color="#0B1A1C" width={3} />
                </div>
                Your library is ready on this Mac
              </div>
              <Said p={next} style={{marginTop: 12}}>Next, let’s make sure Study Stash can hear your lectures.</Said>
              <Card p={mic} style={{marginTop: 10}}>
                <div style={{fontSize: 13.5, fontWeight: 700}}>Let Study Stash hear your lectures</div>
                <div style={{fontSize: 12.5, lineHeight: 1.45, color: colors.text2, marginTop: 6}}>
                  It records with this computer’s microphone. The recording stays here; only the transcript goes to your library.
                </div>
                <Bars f={f} p={mic} />
                <div style={{fontSize: 12.5, color: colors.text2, marginTop: 10}}>Say something. The bars move when Study Stash hears you.</div>
              </Card>
            </>
          ) : null}
        </div>
      </div>
      <Reply />
    </>
  );
};

// ---- You're set up -------------------------------------------------------------------------------------------------

const DONE = ['Just this Mac', 'Microphone', 'Transcription model · Whisper large-v3 turbo (compact)', 'Notes · Written by Claude', 'Classes · 6 classes', 'Start at login'];

const Finish: React.FC<{f: number; from: number}> = ({f, from}) => {
  const OPEN = cue('setup-open');
  const card = prog(f, from + 4, from + 14);
  return (
    <>
      <Rail
        head="Setup with Claude"
        open={1}
        steps={[
          {label: 'Claude is ready', state: 'done'},
          {label: 'Just this Mac', state: 'done'},
          {label: 'Microphone', state: 'done'},
          {label: 'Transcription model', sub: 'Whisper large-v3 turbo (compact)', state: 'done'},
          {label: 'Notes', sub: 'Written by Claude', state: 'done'},
          {label: 'Classes', sub: '6 classes', state: 'done'},
          {label: 'Canvas', right: 'Optional', state: 'todo'},
          {label: 'Start at login', state: 'done'},
        ]}
      />
      <ChatHead />
      <div style={{position: 'absolute', left: PANE, top: 122}}>
        <div style={{display: 'flex', alignItems: 'center', gap: 7, fontSize: 12.5, color: colors.text2}}>
          <div style={{width: 14, height: 14, borderRadius: 7, background: ui.accent, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
            <G name="check" size={10} color="#0B1A1C" width={3} />
          </div>
          Your library is ready on this Mac
        </div>
        <Said style={{marginTop: 14}}>You’re all set. Your library is ready, and Claude writes your notes.</Said>
        <Card p={card} style={{marginTop: 12}}>
          <div style={{fontSize: 13.5, fontWeight: 700}}>You’re set up</div>
          {DONE.map((d, i) => (
            <div key={d} style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: i ? 3 : 9, fontSize: 12.5, color: '#C9CED3', opacity: prog(f, from + 6 + i * 2, from + 12 + i * 2)}}>
              <G name="check" size={12} color={colors.lagoonBright} width={2.4} /> {d}
            </div>
          ))}
          <div style={{display: 'flex', justifyContent: 'flex-end', marginTop: 10}}>
            <Button label="Open Study Stash" press={pressAt(f, OPEN)} />
          </div>
        </Card>
      </div>
      <Reply />
    </>
  );
};

/** Where things are in the window (its own points), for the pointer. */
const SPOTS = {
  claude: {x: PANE + 20 + 150, y: 82 + 24 + 10 + 40 + 20 + 90},
  setUp: {x: PANE + 520 - 16 - 30, y: 106 + 16 + 54 + 6 + 14 + 160 + 16},
  open: {x: PANE + 520 - 16 - 62, y: 122 + 20 + 14 + 40 + 12 + 14 + 22 + 6 * 18.5 + 26},
};

export const Setup: React.FC = () => {
  const f = useCurrentFrame();
  const {width} = useVideoConfig();
  const vertical = useVertical();
  const D = length('setup');
  const PICK = cue('setup-pick');
  const MAC = cue('setup-mac');
  const OPEN = cue('setup-open');
  const CHAT = PICK + 18; // Claude is installed and signed in: the chat begins
  const FINISH = MAC + 45; // the rest of the steps go by

  const z = vertical ? 1.12 : 1.25;
  const left = vertical ? Math.round((width - W * z) / 2) : COLUMN;
  const top = vertical ? 600 : 200;
  const enter = prog(f, 0, 14);
  const close = prog(f, OPEN + 4, OPEN + 14, inOut);
  const at = (p: {x: number; y: number}): [number, number] => [left + p.x * z, top + p.y * z];

  const page = f < CHAT ? 0 : f < FINISH ? 1 : 2;
  const swap = (from: number) => prog(f, from - 4, from + 4);
  const iconX = menuIconX(width);
  const toast = prog(f, OPEN + 18, OPEN + 30);

  return (
    <AbsoluteFill>
      <Desktop clock="Sun 31 Aug  20:41">
        <div style={{position: 'absolute', left, top, opacity: enter * (1 - close), transform: `translateY(${px((1 - enter) * 24 + close * 16)}px) scale(${z})`, transformOrigin: '0 0'}}>
          <div style={{width: W, height: H, position: 'relative', borderRadius: 18, background: colors.window, border: `1px solid ${colors.edge}`, boxShadow: '0 0 0 0.5px rgba(0,0,0,0.6), 0 40px 90px -24px rgba(0,0,20,0.7)', overflow: 'hidden', fontFamily: fonts.ui, color: colors.text}}>
            <div style={{position: 'absolute', left: 20, top: 20, display: 'flex', gap: 8}}>
              {['#FF5F57', '#4A4F55', '#4A4F55'].map((c, i) => (
                <div key={i} style={{width: 12, height: 12, borderRadius: 6, background: c}} />
              ))}
            </div>
            <div style={{position: 'absolute', left: 0, right: 0, top: 18, textAlign: 'center', fontSize: 13, fontWeight: 700, color: '#B9C0C6'}}>Set up Study Stash</div>
            {page === 0 || f < CHAT + 4 ? (
              <div style={{position: 'absolute', inset: 0, opacity: 1 - swap(CHAT)}}>
                <Welcome f={f} />
              </div>
            ) : null}
            {(page === 1 && f >= CHAT - 4) || (page === 2 && f < FINISH + 4) ? (
              <div style={{position: 'absolute', inset: 0, opacity: swap(CHAT) * (1 - swap(FINISH))}}>
                <Chat f={f} />
              </div>
            ) : null}
            {f >= FINISH - 4 ? (
              <div style={{position: 'absolute', inset: 0, opacity: swap(FINISH)}}>
                <Finish f={f} from={FINISH} />
              </div>
            ) : null}
          </div>
        </div>

        {/* The first run's pointer to the S. in the menu bar (IconWords.WhereItIs). */}
        <div style={{position: 'absolute', right: vertical ? 24 : 18, top: 50, opacity: toast, transform: `translateX(${px((1 - toast) * 40)}px)`}}>
          <Toast title="Study Stash is in your menu bar" body="Click the S. at the top right of your screen to record, search or open Settings." width={vertical ? 520 : 444} />
        </div>

        <Cursor
          softPress
          stops={[
            [8, ...at({x: SPOTS.claude.x + 260, y: SPOTS.claude.y + 220})],
            [PICK - 6, ...at(SPOTS.claude)],
            [PICK + 6, ...at(SPOTS.claude)],
            [MAC - 8, ...at(SPOTS.setUp)],
            [MAC + 10, ...at(SPOTS.setUp)],
            [FINISH + 2, ...at({x: SPOTS.open.x + 30, y: SPOTS.open.y - 60})],
            [OPEN - 6, ...at(SPOTS.open)],
            [OPEN + 8, ...at(SPOTS.open)],
            [D - 30, iconX - 6, 14],
          ]}
          clicks={[PICK, MAC, OPEN]}
        />
      </Desktop>
      <div style={{opacity: 1 - prog(f, OPEN + 6, OPEN + 18)}}>
        <Title text={titles.setup!} delay={8} />
      </div>
    </AbsoluteFill>
  );
};
