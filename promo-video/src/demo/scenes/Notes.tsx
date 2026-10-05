import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts} from '../../config';
import {Desktop, Title} from '../../components/Layout';
import {Group, ListHead} from '../../components/Sidebar';
import {inOut, prog, px} from '../../promo2/scenes/Bookends';
import {Flow, FLOW_H, FLOW_W} from '../../promo2/Flow';
import {H2, Icon, NoteHead, Serif, useStage} from '../../promo2/ui';
import {Cut, cueIn, titles} from '../config';
import {DemoWindow, LectureRow, Toast} from '../chrome';

// Notes first, diagrams after, as the app does it: Claude Code writes the notes (they take a couple of minutes: the
// clock moves on), the library files them in BIO 110 and the notification says so; the notes open with the byline
// "Adding diagrams…" (AiNotesModel), and a minute later the diagram is added into the same open note, pushing the key
// points down, and the byline says "Diagrams added".

/** "Written by Claude Code · Tue 10:55", then what the diagrams are doing, as AiNotesModel's ShownByline. */
const Byline: React.FC<{diagrams?: React.ReactNode}> = ({diagrams}) => (
  <span style={{fontSize: 14, color: colors.text3, display: 'inline-flex', alignItems: 'center', gap: 6}}>
    Written by Claude Code · Tue 10:55{diagrams ? <> · {diagrams}</> : null}
  </span>
);

const Dots: React.FC<{frame: number}> = ({frame}) => (
  <span style={{display: 'inline-flex', gap: 3, marginLeft: 2}}>
    {[0, 1, 2].map((i) => (
      <span key={i} style={{width: 4, height: 4, borderRadius: 2, background: colors.lagoonBright, opacity: px(0.35 + 0.65 * Math.max(0, Math.sin((frame - i * 5) / 6)))}} />
    ))}
  </span>
);

export const Notes: React.FC<{cut?: Cut}> = ({cut}) => {
  const frame = useCurrentFrame();
  const {width} = useVideoConfig();
  const FILED = cueIn(cut, 'notes-filed');
  const ADDED = cueIn(cut, 'notes-diagrams');
  const LATER = cut ? cueIn(cut, 'notes-later') : FILED + 72; // "A minute later"
  const stage = useStage({list: true});
  const {vertical, page} = stage;
  const enter = prog(frame, 0, cut ? 14 : 18);
  const filed = frame >= FILED;
  const notes = (i: number): React.CSSProperties => {
    const p = prog(frame, FILED + 2 + i * 4, FILED + 16 + i * 4);
    return {opacity: filed ? p : 0, transform: `translateY(${px((1 - p) * 14)}px)`};
  };
  const added = frame >= ADDED;
  const scale = Math.min(1.08, page.w / FLOW_W);
  const open = prog(frame, ADDED, ADDED + 20, inOut);
  const sectionH = (52 + FLOW_H * scale + 18) * open;
  const build = interpolate(frame, [ADDED + 4, ADDED + 40], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const sheen = prog(frame, ADDED + 6, ADDED + 40, inOut);
  const later = prog(frame, LATER, LATER + 10) * (1 - prog(frame, ADDED - 8, ADDED + 4));
  // The notes take a couple of minutes to write, and the diagrams a minute more: the menu bar's clock says so.
  const clock = frame < (cut ? 14 : 40) ? 'Tue 23 Sep  10:53' : frame < LATER + 10 ? 'Tue 23 Sep  10:55' : 'Tue 23 Sep  10:56';
  const toast = prog(frame, FILED, FILED + 12) * (1 - (cut ? prog(frame, FILED + 38, FILED + 48) : prog(frame, FILED + 120, FILED + 132)));
  // The reel's notification: larger, and low on the window's right, inside the safe area, clear of the notes as they open.
  const TOAST_Z = 1.3;
  const toastAt: React.CSSProperties = cut
    ? {right: width - (stage.left + stage.w * stage.z) + 22, top: stage.top + stage.h * stage.z - 22 - 150 * TOAST_Z, transformOrigin: '100% 0'}
    : {right: vertical ? 24 : 18, top: 50};
  const diagramsLine = !filed ? null : added ? (
    <span style={{display: 'inline-flex', alignItems: 'center', gap: 5, color: colors.lagoonBright, fontWeight: 600}}>
      <Icon name="check" size={15} color={colors.lagoonBright} /> Diagrams added
    </span>
  ) : (
    <span style={{display: 'inline-flex', alignItems: 'center', gap: 4}}>
      Adding diagrams… <Dots frame={frame} />
    </span>
  );

  const list = (
    <>
      <ListHead title="BIO 110" sub={`${filed ? 10 : 9} lectures`} />
      <Group name="This week" />
      <LectureRow title="The cardiac cycle" meta={filed ? 'Tue 23 Sep · 50 min' : 'Writing notes…'} snippet={filed ? 'One heartbeat is one cycle: the atria fill and squeeze, the ventricles contract…' : undefined} color={colors.bio} lit />
      <LectureRow title="Membranes and osmosis" meta="Thu 18 Sep · 50 min" snippet="Why water follows solutes across a membrane." color={colors.bio} />
      <Group name="Last week" />
      <LectureRow title="Blood pressure and perfusion" meta="Tue 16 Sep · 50 min" snippet="What sets the pressure, and how tissues get their share." color={colors.bio} />
    </>
  );

  return (
    <AbsoluteFill>
      <Desktop clock={clock}>
        <DemoWindow stage={stage} lit="bio" counts={{bio: filed ? 10 : 9}} pulse={filed ? Math.max(0, 1 - (frame - FILED) / 12) : 0} list={vertical ? undefined : list} enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: vertical ? 70 : 62, width: page.w}}>
            {filed ? null : (
              <div style={{display: 'flex', alignItems: 'center', gap: 10, fontSize: 16, color: colors.text2, marginTop: 40}}>
                <Dots frame={frame} /> Writing the notes for “The cardiac cycle”
              </div>
            )}
            <div style={notes(0)}>
              <NoteHead cls="BIO 110" color={colors.bio} meta="Tue 23 Sep · 50 min" title="The cardiac cycle" />
            </div>
            <div style={{...notes(1), display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 18}}>
              <H2>Summary</H2>
              <Byline diagrams={diagramsLine} />
            </div>
            <Serif style={{...notes(2), marginTop: 8}}>
              One heartbeat is one cycle: the atria fill and squeeze, the ventricles contract and eject, then relax and refill. The valves closing make the two heart sounds.
            </Serif>
            {/* The diagram, added into the note where it belongs, pushing what's below it down. */}
            <div style={{height: sectionH, overflow: 'hidden', marginTop: 10 * open, position: 'relative'}}>
              <div style={{opacity: prog(frame, ADDED + 2, ADDED + 14)}}>
                <H2 style={{marginTop: 14}}>The cycle, step by step</H2>
                <div style={{marginTop: 14, display: 'flex', justifyContent: 'center', position: 'relative'}}>
                  <Flow look={{build}} scale={scale} />
                </div>
              </div>
              {/* A soft light passes over it as it lands. */}
              {sheen > 0 && sheen < 1 ? (
                <div style={{position: 'absolute', inset: 0, pointerEvents: 'none', background: `linear-gradient(105deg, rgba(52,193,189,0) ${px(sheen * 140 - 40)}%, rgba(52,193,189,0.10) ${px(sheen * 140 - 20)}%, rgba(52,193,189,0) ${px(sheen * 140)}%)`}} />
              ) : null}
            </div>
            <div style={{...notes(3), marginTop: 18}}>
              <H2>Key points</H2>
              {['Atrial contraction adds the last fifth of the ventricles’ filling.', 'About 70 mL leaves the left ventricle with each beat.'].map((k) => (
                <Serif key={k} style={{display: 'flex', gap: 14, marginTop: 8}}>
                  <span style={{color: colors.text3}}>•</span>
                  {k}
                </Serif>
              ))}
            </div>
          </div>
        </DemoWindow>

        {/* The notification the app shows as the lecture is filed. */}
        <div style={{position: 'absolute', ...toastAt, opacity: toast, transform: `translateX(${px((1 - toast) * 30)}px)${cut ? ` scale(${TOAST_Z})` : ''}`}}>
          <Toast title="Filed in BIO 110" body="The cardiac cycle" button="Open note" width={vertical ? 520 : 444} />
        </div>

        {/* A minute goes by: the diagrams follow the notes. */}
        <div
          style={{
            position: 'absolute',
            left: stage.at(page.x, 0).x,
            top: stage.at(0, vertical ? 600 : 470).y,
            width: page.w * stage.z,
            display: 'flex',
            justifyContent: 'center',
            opacity: later,
            transform: `translateY(${px((1 - later) * 10)}px)`,
            zIndex: 30,
          }}
        >
          <div style={{display: 'flex', alignItems: 'center', gap: 14, padding: vertical ? '18px 32px' : '16px 30px', borderRadius: 999, background: 'rgba(30, 38, 70, 0.9)', border: '1px solid rgba(255,255,255,0.2)', boxShadow: '0 24px 50px -14px rgba(0,0,20,0.75)', fontFamily: fonts.ui, fontSize: vertical ? 36 : 30, fontWeight: 600, color: '#FFFFFF', whiteSpace: 'nowrap'}}>
            <svg width={vertical ? 40 : 34} height={vertical ? 40 : 34} viewBox="0 0 24 24" fill="none" stroke="#FFFFFF" strokeWidth="2" strokeLinecap="round">
              <circle cx="12" cy="12" r="9" />
              <path d="M12 12 L12 6.5" transform={`rotate(${px(interpolate(frame, [LATER, ADDED], [0, 360], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}))} 12 12)`} />
              <path d="M12 12 L15.5 12" />
            </svg>
            A minute later
          </div>
        </div>
      </Desktop>
      {cut ? null : <Title text={titles.notes!} delay={10} />}
    </AbsoluteFill>
  );
};
