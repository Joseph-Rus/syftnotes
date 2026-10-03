import React from 'react';
import {AbsoluteFill, Easing, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../../config';
import {useVertical} from '../../components/Layout';
import {Sfx} from '../../components/Sfx';
import {durations2, text2} from '../config';
import {DrawnIcon, Glimpse, inCubic, inOut, Mask, NightGround, night, Placement, prog, px} from './Bookends';

const draw = Easing.bezier(0.4, 0, 0.2, 1); // a line of light: off quickly, then settling round the corners

// The opening. The screen is dark; a line of light draws the icon's rounded square, the paper fills in, the S is drawn
// and inked and its full stop drops in. "Study Stash" rises out of a mask with its "New" tag and the line under it,
// while glimpses of what's new float in from depth around it: the pinned diagram, the drone, the sigmoid with its
// slider, gradient descent. Then the cards drift on past the edges, the words go, and the night lifts off the desktop,
// so the first scene's window rises onto the very same desktop.

// Where the cards settle (their centres), how big, how near (1 nearest) and when they arrive.
const CARDS_WIDE: Placement[] = [
  {kind: 'diagram', x: 330, y: 236, s: 0.64, z: 1, at: 24},
  {kind: 'drone', x: 1596, y: 252, s: 0.62, z: 0.8, at: 30},
  {kind: 'sigmoid', x: 1646, y: 904, s: 0.56, z: 0.62, at: 36},
  {kind: 'descent', x: 286, y: 910, s: 0.48, z: 0.42, at: 42},
];
const CARDS_TALL: Placement[] = [
  {kind: 'diagram', x: 318, y: 352, s: 0.76, z: 1, at: 24},
  {kind: 'drone', x: 806, y: 520, s: 0.6, z: 0.75, at: 30},
  {kind: 'sigmoid', x: 300, y: 1530, s: 0.66, z: 0.62, at: 36},
  {kind: 'descent', x: 790, y: 1650, s: 0.56, z: 0.42, at: 42},
];

export const Intro2: React.FC = () => {
  const f = useCurrentFrame();
  const vertical = useVertical();
  const {width, height} = useVideoConfig();
  const D = durations2.opener;

  // The icon, put together.
  const trace = prog(f, 0, 20, draw);
  const tile = prog(f, 14, 28, inOut);
  const s = prog(f, 22, 40, inOut);
  const ink = prog(f, 31, 43);
  const dot = prog(f, 36, 48);
  const sheen = prog(f, 46, 70, inOut);
  // The words.
  const name = prog(f, 18, 40);
  const tag = prog(f, 30, 44);
  const line1 = prog(f, 30, 52);
  const line2 = prog(f, 35, 57);
  // The way out, into the first scene: the night lifts as the words and cards go (slowly at first, so the line under
  // the name stays to be read).
  const lift = prog(f, D - 30, D - 8, inOut);
  const leave = prog(f, D - 26, D - 4, inCubic);

  const iconSize = vertical ? 212 : 172;
  const iconTop = vertical ? 676 : 300;
  const nameTop = iconTop + iconSize + (vertical ? 48 : 40);
  const nameSize = vertical ? 124 : 120;
  const lineTop = nameTop + Math.round(nameSize * 1.12) + (vertical ? 34 : 26);
  const lineSize = vertical ? 54 : 46;
  const centreY = iconTop + iconSize / 2;

  const words: React.CSSProperties = {fontFamily: fonts.display, fontWeight: 600, fontSize: lineSize, lineHeight: 1.2, letterSpacing: '-0.015em', color: night.soft};
  const strong: React.CSSProperties = {color: night.teal};
  const o = text2.opener;

  return (
    <AbsoluteFill>
      <NightGround
        dim={0.62 * (1 - lift)}
        black={1 - prog(f, 0, 40, inOut)}
        zoom={1.06 - 0.06 * prog(f, 0, D - 10, inOut)}
        glow={{x: width / 2, y: centreY, r: vertical ? 760 : 820, a: tile * (1 - leave)}}
      />

      {(vertical ? CARDS_TALL : CARDS_WIDE).map((g) => (
        <Glimpse key={g.kind} g={g} f={f} cx={width / 2} cy={vertical ? 960 : 540} leave={leave} />
      ))}

      <AbsoluteFill style={{opacity: 1 - leave, transform: `translateY(${px(-18 * leave)}px) scale(${px((1 - 0.03 * leave) * 10000) / 10000})`}}>
        <div style={{position: 'absolute', left: Math.round((width - iconSize) / 2), top: iconTop, transform: `scale(${px((0.94 + 0.06 * tile) * 10000) / 10000})`}}>
          <DrawnIcon size={iconSize} trace={trace} tile={tile} s={s} ink={ink} dot={dot} sheen={sheen} />
        </div>

        <div style={{position: 'absolute', left: 0, right: 0, top: nameTop, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: vertical ? 22 : 26}}>
          <Mask p={name}>
            <div style={{fontFamily: fonts.display, fontWeight: 700, fontSize: nameSize, lineHeight: 1.12, letterSpacing: '-0.03em', color: colors.onDesk, whiteSpace: 'nowrap'}}>
              {text.name}
            </div>
          </Mask>
          <div
            style={{
              fontFamily: fonts.ui,
              fontWeight: 700,
              fontSize: vertical ? 30 : 28,
              lineHeight: 1,
              color: '#FFFFFF',
              background: colors.lagoon,
              borderRadius: 999,
              padding: '10px 20px 11px',
              boxShadow: '0 0 0 1px rgba(255,255,255,0.14) inset, 0 10px 30px -8px rgba(14,149,148,0.7)',
              opacity: tag,
              transform: `translateY(${px((1 - tag) * 10)}px) scale(${px((0.9 + 0.1 * tag) * 1000) / 1000})`,
              marginTop: Math.round(nameSize * 0.08),
            }}
          >
            New
          </div>
        </div>

        <div style={{position: 'absolute', left: 0, right: 0, top: lineTop, display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center'}}>
          {vertical ? (
            <>
              <Mask p={line1}>
                <div style={words}>{o.before.trim()}</div>
              </Mask>
              <Mask p={line2}>
                <div style={words}>
                  <span style={strong}>{o.highlight}</span>
                  {o.after}
                </div>
              </Mask>
            </>
          ) : (
            <Mask p={line1}>
              <div style={{...words, whiteSpace: 'nowrap'}}>
                {o.before}
                <span style={strong}>{o.highlight}</span>
                {o.after}
              </div>
            </Mask>
          )}
        </div>
      </AbsoluteFill>
      {/* One soft whoosh as the screen lights up; no bells. */}
      <Sfx at={2} name="whoosh" volume={0.1} />
    </AbsoluteFill>
  );
};
