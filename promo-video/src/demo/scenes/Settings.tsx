import React from 'react';
import {AbsoluteFill, Easing, interpolate, useCurrentFrame} from 'remotion';
import {colors} from '../../config';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {prog, px} from '../../promo2/scenes/Bookends';
import {Cut, cueIn, titles} from '../config';
import {G} from '../chrome';
import {ChoiceCard, CW, CX, GroupBox, Head, PageTitle, RowText, Select, Sep, SetRow, SettingsWindow, sideRow, Switch, useSettingsStage} from '../settings';

// Make it yours, in Settings: AI engines' Rich notes switch is turned off (its four rows fold away: "Plain notes only.
// Nothing extra is asked of your AI"), Claude Code speed goes from Standard to Fast mode (AiWords.SpeedAbout says what
// it trades), and Recording's "When it's written down" goes to After class, which only records during the lecture.

const RICH_ROWS = 232; // the four rows Rich notes folds away
const SPEED_FAST = 'The same Opus, up to 2.5 times faster for notes and diagrams, but billed at a higher rate. It needs usage credits on your Claude account, and without them Claude Code runs at its normal speed';

const Engines: React.FC<{f: number; RICH: number; SPEED: number; FAST: number}> = ({f, RICH, SPEED, FAST}) => {
  const rich = 1 - prog(f, RICH, RICH + 6);
  const fold = prog(f, RICH + 2, RICH + 16, Easing.bezier(0.65, 0, 0.35, 1));
  const fast = f >= FAST;
  const menu = f >= SPEED && f < FAST + 3;
  const menuP = prog(f, SPEED + 1, SPEED + 6);
  const hover = f >= FAST - 9;
  return (
    <>
      <PageTitle title="AI engines" lede="Notes are written after each lecture. Answers come while you ask. Engines run on this Mac, as part of your library." />
      <Head>Who does what</Head>
      <GroupBox>
        <SetRow>
          <RowText title="Writes your notes" sub="After each lecture, on your library" />
          <Select value="Claude Code" />
        </SetRow>
        <Sep />
        <SetRow>
          <RowText title="Answers your questions" sub="Ask bar, recorder chat and quick panel" />
          <Select value="Claude Code" />
        </SetRow>
      </GroupBox>
      <Head>Rich notes</Head>
      <GroupBox>
        <SetRow>
          <RowText title="Rich notes" sub={rich > 0.5 ? 'Diagrams, formula plots and labelled drawings, added after each lecture’s notes' : 'Plain notes only. Nothing extra is asked of your AI'} />
          <Switch on={rich} press={Math.max(0, 1 - Math.abs(f - RICH - 1) / 3)} />
        </SetRow>
        <div style={{height: px(RICH_ROWS * (1 - fold)), overflow: 'hidden', opacity: 1 - fold}}>
          {[
            ['Diagrams', 'Flowcharts, state and sequence diagrams, timelines and mind maps'],
            ['Formula plots', 'A curve or distribution drawn from the lecturer’s formula, with sliders'],
            ['Drawings', 'Labelled figures of what a lecture describes (not on Ollama)'],
          ].map(([t, s]) => (
            <React.Fragment key={t}>
              <Sep />
              <SetRow>
                <RowText title={t} sub={s} />
                <Switch on={1} />
              </SetRow>
            </React.Fragment>
          ))}
          <Sep />
          <SetRow>
            <RowText title="Drawn by" sub="Claude Code reads each transcript" />
            <Select value="Automatic" />
          </SetRow>
        </div>
      </GroupBox>
      <Head>Claude Code speed</Head>
      <GroupBox style={{overflow: 'visible', position: 'relative'}}>
        <SetRow>
          <RowText title="Notes and diagrams" sub={fast ? SPEED_FAST : 'Claude Code as it’s set up, at its usual pace'} />
          <Select value={fast ? 'Fast mode' : 'Standard'} open={menu} />
        </SetRow>
        {menu ? (
          // The menu, under its button's right edge (MenuFlyout, BottomEdgeAlignedRight).
          <div style={{position: 'absolute', right: 16, top: 44, width: 190, padding: 6, borderRadius: 14, background: '#2B3337', border: '1px solid rgba(255,255,255,0.10)', boxShadow: '0 18px 40px -12px rgba(0,0,0,0.7)', opacity: menuP, transform: `translateY(${px((1 - menuP) * -4)}px)`, zIndex: 5}}>
            {['Standard', 'Fast mode', 'Quicker model'].map((n, i) => (
              <div key={n} style={{display: 'flex', alignItems: 'center', gap: 8, height: 30, padding: '0 10px', borderRadius: 8, fontSize: 13, background: i === 1 && hover ? 'rgba(255,255,255,0.12)' : 'transparent'}}>
                <div style={{width: 14}}>{i === 0 ? <G name="check" size={13} /> : null}</div>
                {n}
              </div>
            ))}
          </div>
        ) : null}
      </GroupBox>
      <div style={{height: 40}} />
    </>
  );
};

const MODELS: [string, string, string?][] = [
  ['Whisper large-v3', '3.1 GB. The most accurate, and the heaviest: it wants a Mac with Apple silicon or a PC with a big graphics card.'],
  ['Whisper large-v3 turbo', '1.6 GB. More accurate than the compact one, and heavier: it wants a Mac with Apple silicon or a PC with a graphics card.'],
  ['Whisper large-v3 turbo (compact)', '574 MB. large-v3 turbo in a third of the space. Keeps up with a lecture on most computers and leaves room for everything else.', 'Recommended'],
  ['NVIDIA Parakeet v3', '2.5 GB. Nearly as accurate as the compact turbo, and quick on the processor alone: it doesn’t need a graphics card. Writes whole sentences and never makes up words over silence.'],
  ['Whisper small', '488 MB. Quick on any processor. Misses more names and terms than the large ones.'],
  ['Whisper base', '148 MB. The lightest that still follows a lecture, for an older computer or one with little memory.'],
];

const Recording: React.FC<{after: number}> = ({after}) => (
  <>
    <PageTitle title="Recording" lede="Each lecture is written down on this computer by the model you pick. The recording never leaves it; the library gets the transcript." />
    <Head>Transcription model</Head>
    {MODELS.map(([t, b, tag]) => (
      <ChoiceCard key={t} title={t} body={b} tag={tag} chosen={tag ? 1 : 0} check={!!tag} />
    ))}
    <div style={{fontSize: 12, lineHeight: 1.45, color: colors.text2, width: 478, margin: '4px 0 0'}}>The compact model keeps up with a lecture and leaves this Mac room for everything else. Its Apple silicon can run a bigger one too.</div>
    <Head style={{marginTop: 20}}>When it’s written down</Head>
    <ChoiceCard title="As you record" body="The recorder shows the transcript as the lecture goes, and you can ask about what’s been said so far." chosen={1 - after} />
    <ChoiceCard title="After class" body="Only records during the lecture, which saves battery. It’s written down once you stop, so the transcript, and asking about it, come then." chosen={after} />
    <Head>Language</Head>
    <div style={{width: 300, height: 28, borderRadius: 6, background: 'rgba(255,255,255,0.08)', fontSize: 12.5, color: colors.text3, display: 'flex', alignItems: 'center', padding: '0 10px'}}>Found for each lecture (or en, es, fr…)</div>
    <div style={{height: 60}} />
  </>
);

// Where the rows are on the AI engines page (its own points, the page unscrolled), for the pointer.
const RICH_Y = 380;
const SPEED_Y = 484;
// And the After class card on Recording, and how far the page scrolls to show it (landscape).
const AFTER_Y = 956;
const AFTER_SCROLL = 420;

// The reel's window: a little smaller, so everything that's clicked sits inside Instagram's safe area (its right
// edge, past the switches, goes under the buttons), and short enough to stay above the caption.
export const REEL_SETTINGS = {z: 1.08, left: 34, top: 404, bottom: 1490};

export const Settings: React.FC<{cut?: Cut}> = ({cut}) => {
  const f = useCurrentFrame();
  const st = useSettingsStage(cut ? REEL_SETTINGS : undefined);
  const {vertical, z, h, left, top} = st;
  const RICH = cueIn(cut, 'settings-rich');
  const SPEED = cueIn(cut, 'settings-speed');
  const FAST = cueIn(cut, 'settings-fast');
  const REC = cueIn(cut, 'settings-recording');
  const AFTER = cueIn(cut, 'settings-after');
  const enter = prog(f, 0, cut ? 14 : 18);
  const onRecording = f >= REC + 1;
  const scroll = vertical ? 0 : interpolate(f, [REC + 5, REC + 22], [0, AFTER_SCROLL], {easing: Easing.inOut(Easing.cubic), extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const after = prog(f, AFTER + 1, AFTER + 7);

  const switchAt = st.at(CX + CW - 16 - 19, RICH_Y);
  const selectAt = st.at(CX + CW - 16 - 60, SPEED_Y);
  const fastAt = st.at(CX + CW - 16 - 120, SPEED_Y + 22 + 6 + 30 + 15);
  const recAt = st.at(sideRow('Recording').x, sideRow('Recording').y);
  const afterAt = st.at(CX + 200, AFTER_Y - (vertical ? 0 : AFTER_SCROLL));

  return (
    <AbsoluteFill>
      <Desktop clock="Thu 25 Sep  13:38">
        <div style={{position: 'absolute', left, top, opacity: enter, transform: `translateY(${px((1 - enter) * 24)}px) scale(${z})`, transformOrigin: '0 0'}}>
          <SettingsWindow on={onRecording ? 'Recording' : 'AI engines'} h={h} scroll={onRecording ? scroll : 0} hot={f >= REC - 8 && !onRecording ? 'Recording' : null}>
            {onRecording ? <Recording after={after} /> : <Engines f={f} RICH={RICH} SPEED={SPEED} FAST={FAST} />}
          </SettingsWindow>
        </div>
        <Cursor
          softPress
          stops={[
            [cut ? 2 : 6, switchAt.x - 240, switchAt.y + 200],
            [RICH - 6, switchAt.x - 4, switchAt.y - 4],
            [RICH + 8, switchAt.x - 4, switchAt.y - 4],
            [SPEED - 6, selectAt.x, selectAt.y - 4],
            [SPEED + 4, selectAt.x, selectAt.y - 4],
            [FAST - 6, fastAt.x, fastAt.y - 4],
            [FAST + 4, fastAt.x, fastAt.y - 4],
            [REC - 6, recAt.x, recAt.y - 4],
            [REC + 6, recAt.x, recAt.y - 4],
            [AFTER - 6, afterAt.x, afterAt.y],
            [AFTER + 10, afterAt.x, afterAt.y],
            [AFTER + (cut ? 40 : 60), afterAt.x + 160, afterAt.y + (cut ? 60 : 120)],
          ]}
          clicks={[RICH, SPEED, FAST, REC, AFTER]}
        />
      </Desktop>
      {cut ? null : <Title text={titles.settings!} delay={10} />}
    </AbsoluteFill>
  );
};
