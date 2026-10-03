import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame} from 'remotion';
import {colors, fonts} from '../../config';
import {ease, spr} from '../../anim';
import {Desktop, Title} from '../../components/Layout';
import {Group, ListHead, ListRow} from '../../components/Sidebar';
import {Sfx} from '../../components/Sfx';
import {text2} from '../config';
import {Flow, FLOW_H, FLOW_W} from '../Flow';
import {Byline, H2, Icon, NoteHead, Serif, StageWindow, useStage} from '../ui';

// Notes first: the lecture's notes are filed and open at once; its diagrams are added into the same open note a
// couple of minutes later (the byline says "Adding diagrams…", then "Diagrams added", as AiNotesModel does).
const FILED = 16; // the notes are filed
const LATER = 60; // "a couple of minutes later"
const ADDED = 90; // the diagram is added into the open note

const Dots: React.FC<{frame: number}> = ({frame}) => (
  <span style={{display: 'inline-flex', gap: 3, marginLeft: 2}}>
    {[0, 1, 2].map((i) => (
      <span key={i} style={{width: 4, height: 4, borderRadius: 2, background: colors.lagoonBright, opacity: 0.35 + 0.65 * Math.max(0, Math.sin((frame - i * 5) / 6))}} />
    ))}
  </span>
);

export const NotesFirst: React.FC = () => {
  const frame = useCurrentFrame();
  const stage = useStage({list: true});
  const {vertical, page} = stage;
  const enter = spr(frame, 0, {damping: 26, stiffness: 120, mass: 1});
  const filed = frame >= FILED;
  const notes = (i: number): React.CSSProperties => {
    const p = spr(frame, FILED + 2 + i * 3, {damping: 24, stiffness: 160, mass: 0.8});
    return {opacity: filed ? p : 0, transform: `translateY(${(1 - p) * 14}px)`};
  };
  const added = frame >= ADDED;
  const scale = Math.min(1.08, page.w / FLOW_W);
  const open = ease(frame, ADDED, ADDED + 20);
  const sectionH = (52 + FLOW_H * scale + 18) * open;
  const build = interpolate(frame, [ADDED + 4, ADDED + 40], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  const later = spr(frame, LATER, {damping: 20, stiffness: 160, mass: 0.8});
  const laterOut = ease(frame, ADDED - 2, ADDED + 8);
  const clock = frame >= LATER + 8 ? 'Tue 23 Sep  11:16' : 'Tue 23 Sep  11:14';
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
      <ListHead title="BIO 110" sub={`${filed ? 9 : 8} lectures`} />
      <Group name="Today" />
      <ListRow title="The cardiac cycle" sub={filed ? 'Tue 23 Sep · 1 h 12 min' : 'Writing notes…'} color={colors.bio} lit />
      <Group name="Last week" />
      <ListRow title="Blood pressure and perfusion" sub="Thu 18 Sep · 1 h 05 min" color={colors.bio} />
      <ListRow title="Membranes and osmosis" sub="Tue 16 Sep · 58 min" color={colors.bio} />
    </>
  );

  return (
    <AbsoluteFill>
      <Desktop clock={clock}>
        <StageWindow stage={stage} lit="bio" pulse={filed ? Math.max(0, 1 - (frame - FILED) / 12) : 0} list={vertical ? undefined : list} enter={enter}>
          <div style={{position: 'absolute', left: stage.pad, top: vertical ? 70 : 30, width: page.w}}>
            {filed ? null : (
              <div style={{display: 'flex', alignItems: 'center', gap: 10, fontSize: 16, color: colors.text2, marginTop: 40}}>
                <Dots frame={frame} /> Writing the notes for “The cardiac cycle”
              </div>
            )}
            <div style={notes(0)}>
              <NoteHead cls="BIO 110" color={colors.bio} meta="Tue 23 Sep · 1 h 12 min" title="The cardiac cycle" />
            </div>
            <div style={{...notes(1), display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 18}}>
              <H2>Summary</H2>
              <Byline diagrams={diagramsLine} />
            </div>
            <Serif style={{...notes(2), marginTop: 8}}>
              One heartbeat is one cycle: the atria fill and squeeze, the ventricles contract and eject, then relax and refill.
              The valves closing make the two heart sounds.
            </Serif>
            {/* The diagram, added into the note where it belongs, pushing what's below it down. */}
            <div style={{height: sectionH, overflow: 'hidden', marginTop: 10 * open}}>
              <div style={{opacity: ease(frame, ADDED + 2, ADDED + 14)}}>
                <H2 style={{marginTop: 14}}>The cycle, step by step</H2>
                <div style={{marginTop: 14, display: 'flex', justifyContent: 'center'}}>
                  <Flow look={{build}} scale={scale} />
                </div>
              </div>
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
        </StageWindow>
        {/* Time passing: a couple of minutes, as the menu bar's clock shows too. */}
        <div
          style={{
            position: 'absolute',
            left: stage.at(page.x, 0).x,
            top: stage.at(0, vertical ? 560 : 430).y,
            width: page.w * stage.z,
            display: 'flex',
            justifyContent: 'center',
            opacity: Math.min(1, later * 1.4) * (1 - laterOut),
            transform: `scale(${0.85 + 0.15 * later})`,
            zIndex: 30,
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 14,
              padding: vertical ? '18px 32px' : '16px 30px',
              borderRadius: 999,
              background: 'rgba(30, 38, 70, 0.88)',
              border: '1px solid rgba(255,255,255,0.2)',
              boxShadow: '0 24px 50px -14px rgba(0,0,20,0.75)',
              fontFamily: fonts.ui,
              fontSize: vertical ? 36 : 30,
              fontWeight: 600,
              color: '#FFFFFF',
              whiteSpace: 'nowrap',
            }}
          >
            {/* A clock whose hands run on through the two minutes. */}
            <svg width={vertical ? 40 : 34} height={vertical ? 40 : 34} viewBox="0 0 24 24" fill="none" stroke="#FFFFFF" strokeWidth="2" strokeLinecap="round">
              <circle cx="12" cy="12" r="9" />
              <path d="M12 12 L12 6.5" transform={`rotate(${(frame - LATER) * 9} 12 12)`} />
              <path d="M12 12 L15.5 12" transform={`rotate(${(frame - LATER) * 0.75} 12 12)`} />
            </svg>
            2 minutes later
          </div>
        </div>
      </Desktop>
      <Sfx at={FILED} name="chime-filed" volume={0.4} />
      <Sfx at={LATER} name="whoosh" volume={0.1} />
      <Sfx at={ADDED + 2} name="pop" volume={0.22} />
      <Sfx at={ADDED + 6} name="ding" volume={0.3} />
      <Title text={text2.notes as [string, string]} delay={10} />
    </AbsoluteFill>
  );
};

