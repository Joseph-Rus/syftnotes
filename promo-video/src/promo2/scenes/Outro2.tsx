import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../../config';
import {useVertical} from '../../components/Layout';
import {durations2} from '../config';
import {CommandMark, DrawnIcon, Glimpse, inOut, LockedLaptop, Mask, NightGround, night, PanesMark, Placement, prog, px} from './Bookends';

// The ending, on the same night desktop as the opening, dimmed so the words carry it.
//
// The promise: a laptop drawn a line at a time, a recording on its screen and a padlock closing over it, and the words
// said plainly in white, "your computer" in Lagoon. Then the end card, laid out like a product page: the icon, the
// name, where it runs and where to get it, with the film's glimpses (the pinned diagram, the drone, the sigmoid) resting
// beside it at different depths, drifting slowly. It holds to the last frame.

const DIM = 0.7;

export const Proof2: React.FC = () => {
  const f = useCurrentFrame();
  const vertical = useVertical();
  const {width} = useVideoConfig();
  const draw = prog(f, 0, 26, inOut);
  const wave = prog(f, 10, 34);
  const lock = prog(f, 22, 34);
  const shut = prog(f, 32, 40, inOut);
  const l1 = prog(f, 6, 28);
  const l2 = prog(f, 11, 33);
  const sub = prog(f, 30, 50);
  // The promise clears just before the end card comes in over the same desktop.
  const D = durations2.proof;
  const clear = prog(f, D - 18, D - 4, inOut);

  const lapW = vertical ? 520 : 380;
  const lapTop = vertical ? 540 : 186;
  const textTop = lapTop + Math.round((lapW * 250) / 360) + (vertical ? 92 : 66);
  const size = vertical ? 92 : 84;
  const p = text.proof;
  const big: React.CSSProperties = {fontFamily: fonts.display, fontWeight: 600, fontSize: size, lineHeight: 1.1, letterSpacing: '-0.028em', color: colors.onDesk, whiteSpace: 'nowrap'};
  const teal: React.CSSProperties = {color: night.teal};

  return (
    <AbsoluteFill>
      <NightGround dim={DIM} glow={{x: width / 2, y: lapTop + 120, r: vertical ? 700 : 760, a: 0.8 * draw * (1 - clear)}} />
      <AbsoluteFill style={{opacity: 1 - clear, transform: `translateY(${px(-12 * clear)}px)`}}>
        <div style={{position: 'absolute', left: Math.round((width - lapW) / 2), top: lapTop}}>
          <LockedLaptop width={lapW} draw={draw} wave={wave} lock={lock} shut={shut} />
        </div>
        <div style={{position: 'absolute', left: 0, right: 0, top: textTop, display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center'}}>
          {vertical ? (
            <>
              <Mask p={l1}>
                <div style={big}>{p.before.trim()}</div>
              </Mask>
              <Mask p={l2}>
                <div style={big}>
                  <span style={teal}>{p.highlight}</span>
                  {p.after}
                </div>
              </Mask>
            </>
          ) : (
            <Mask p={l1}>
              <div style={big}>
                {p.before}
                <span style={teal}>{p.highlight}</span>
                {p.after}
              </div>
            </Mask>
          )}
          <div
            style={{
              marginTop: vertical ? 44 : 34,
              fontFamily: fonts.ui,
              fontWeight: 500,
              fontSize: vertical ? 38 : 34,
              color: night.soft,
              opacity: sub,
              transform: `translateY(${px((1 - sub) * 14)}px)`,
            }}
          >
            {text.proofLine}
          </div>
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// The glimpses beside the end card: in landscape a fanned stack to the right of the words; on a phone, above and below.
const REST_WIDE: Placement[] = [
  // A cascade going back and up to the right: the pinned diagram in front, the drone behind it, the sigmoid furthest.
  {kind: 'sigmoid', x: 1690, y: 236, s: 0.5, z: 0.4, at: 14},
  {kind: 'drone', x: 1534, y: 404, s: 0.62, z: 0.66, at: 9},
  {kind: 'diagram', x: 1300, y: 664, s: 0.74, z: 1, at: 4},
];
const REST_TALL: Placement[] = [
  {kind: 'drone', x: 760, y: 300, s: 0.6, z: 0.6, at: 10},
  {kind: 'diagram', x: 330, y: 360, s: 0.7, z: 1, at: 4},
  {kind: 'descent', x: 790, y: 1660, s: 0.52, z: 0.4, at: 16},
  {kind: 'sigmoid', x: 320, y: 1600, s: 0.64, z: 0.7, at: 12},
];

const Chip: React.FC<{children: React.ReactNode; size: number}> = ({children, size}) => (
  <div
    style={{
      width: size,
      height: size,
      borderRadius: Math.round(size * 0.28),
      border: '1px solid rgba(214,221,255,0.22)',
      background: 'rgba(214,221,255,0.06)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
    }}
  >
    {children}
  </div>
);

export const Cta2: React.FC = () => {
  const f = useCurrentFrame();
  const vertical = useVertical();
  const {width} = useVideoConfig();
  const D = durations2.cta;
  // The words come in once the promise has cleared (the first 12 frames are the cross-fade).
  const icon = prog(f, 4, 26);
  const sheen = prog(f, 30, 56, inOut);
  const name = prog(f, 10, 32);
  const line = prog(f, 17, 39);
  const link = prog(f, 25, 47);
  const marks = prog(f, 32, 52);
  // The cards rest a little dimmer once the words are in, and the whole card stays put to the last frame.
  const rest = 1 - 0.18 * prog(f, 40, D - 20, inOut);

  const left = vertical ? 0 : 190;
  const iconSize = vertical ? 196 : 156;
  const iconTop = vertical ? 652 : 244;
  const nameSize = vertical ? 128 : 132;
  const nameTop = iconTop + iconSize + (vertical ? 40 : 34);
  const lineTop = nameTop + Math.round(nameSize * 1.12) + (vertical ? 22 : 16);
  const lineSize = vertical ? 54 : 48;
  const linkTop = lineTop + Math.round(lineSize * 1.2) + (vertical ? 56 : 46);
  const linkSize = vertical ? 42 : 38;
  const align = vertical ? 'center' : 'flex-start';
  const column: React.CSSProperties = {position: 'absolute', left, right: vertical ? 0 : undefined, display: 'flex', justifyContent: align};

  return (
    <AbsoluteFill>
      <NightGround dim={DIM} glow={{x: vertical ? width / 2 : 1400, y: vertical ? 960 : 560, r: vertical ? 900 : 900, a: 0.85}} />

      {(vertical ? REST_TALL : REST_WIDE).map((g) => (
        <Glimpse key={g.kind} g={g} f={f} cx={vertical ? width / 2 : 1000} cy={vertical ? 1000 : 700} drift={0.16} light={rest} play={[20, 80]} />
      ))}

      <div style={{...column, top: iconTop, opacity: icon, transform: `translateY(${px((1 - icon) * 16)}px)`}}>
        <DrawnIcon size={iconSize} trace={1} tile={1} s={1} ink={1} dot={1} sheen={sheen} />
      </div>
      <div style={{...column, top: nameTop}}>
        <Mask p={name}>
          <div style={{fontFamily: fonts.display, fontWeight: 700, fontSize: nameSize, lineHeight: 1.12, letterSpacing: '-0.03em', color: colors.onDesk, whiteSpace: 'nowrap'}}>
            {text.name}
          </div>
        </Mask>
      </div>
      <div style={{...column, top: lineTop}}>
        <Mask p={line}>
          <div style={{fontFamily: fonts.display, fontWeight: 600, fontSize: lineSize, lineHeight: 1.2, letterSpacing: '-0.015em', color: night.soft, whiteSpace: 'nowrap'}}>
            {text.cta}
          </div>
        </Mask>
      </div>
      <div style={{...column, top: linkTop, alignItems: 'center', gap: vertical ? 22 : 20, opacity: link, transform: `translateY(${px((1 - link) * 14)}px)`}}>
        <div
          style={{
            fontFamily: fonts.ui,
            fontWeight: 600,
            fontSize: linkSize,
            lineHeight: 1,
            letterSpacing: '-0.01em',
            color: '#FFFFFF',
            padding: vertical ? '22px 40px 24px' : '20px 36px 22px',
            borderRadius: 999,
            background: 'linear-gradient(180deg, #16A9A6 0%, #0B8786 100%)',
            boxShadow: '0 0 0 1px rgba(255,255,255,0.16) inset, 0 1px 0 rgba(255,255,255,0.25) inset, 0 22px 50px -18px rgba(20,190,185,0.75)',
            whiteSpace: 'nowrap',
          }}
        >
          {text.link}
        </div>
        <div style={{display: 'flex', gap: vertical ? 14 : 12, opacity: marks}}>
          <Chip size={vertical ? 82 : 76}>
            <CommandMark size={vertical ? 40 : 36} color="#DCE3FF" />
          </Chip>
          <Chip size={vertical ? 82 : 76}>
            <PanesMark size={vertical ? 40 : 36} color="#DCE3FF" />
          </Chip>
        </div>
      </div>
    </AbsoluteFill>
  );
};
