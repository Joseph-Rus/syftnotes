import React from 'react';
import {AbsoluteFill, Easing, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../../config';
import {useVertical} from '../../components/Layout';
import {DrawnIcon, Glimpse, inCubic, inOut, Mask, NightGround, night, Placement, prog, px} from '../../promo2/scenes/Bookends';
import {Cut, cueIn, lengthIn, voAt} from '../config';

const draw = Easing.bezier(0.4, 0, 0.2, 1);

// The opening, the second video's black intro retimed for the demo's first line: a line of light draws the icon, the
// paper fills in, the S is drawn and inked, its full stop drops in; "Study Stash" rises as Adam says it, then
// "Your lectures, written up and filed by class." Glimpses of what's to come float in from depth (the pinned diagram,
// the drone, the sigmoid, gradient descent), then everything drifts on and the night lifts off the desktop.

const CARDS_WIDE: Placement[] = [
  {kind: 'diagram', x: 330, y: 236, s: 0.64, z: 1, at: 40},
  {kind: 'drone', x: 1596, y: 252, s: 0.62, z: 0.8, at: 48},
  {kind: 'sigmoid', x: 1646, y: 904, s: 0.56, z: 0.62, at: 56},
  {kind: 'descent', x: 286, y: 910, s: 0.48, z: 0.42, at: 64},
];
const CARDS_TALL: Placement[] = [
  {kind: 'diagram', x: 318, y: 352, s: 0.76, z: 1, at: 40},
  {kind: 'drone', x: 806, y: 520, s: 0.6, z: 0.75, at: 48},
  {kind: 'sigmoid', x: 300, y: 1530, s: 0.66, z: 0.62, at: 56},
  {kind: 'descent', x: 790, y: 1650, s: 0.56, z: 0.42, at: 64},
];

// The reel's: the same glimpses, arriving sooner.
const CARDS_TALL_FAST: Placement[] = CARDS_TALL.map((g, i) => ({...g, at: 4 + i * 4}));

export const Opener: React.FC<{cut?: Cut}> = ({cut}) => {
  const f = useCurrentFrame();
  const vertical = useVertical();
  const {width} = useVideoConfig();
  const D = lengthIn(cut, 'opener');
  const vo = cut ? cueIn(cut, 'vo') : voAt('opener'); // "This is Study Stash." (the reel's: "Study Stash.")

  // The icon, put together before the first word. The reel's starts drawing on the first frame and is whole in under a
  // second, the name rising as it's said, and no tagline: it's straight into the app.
  const trace = cut ? prog(f, 0, 16, draw) : prog(f, 4, 30, draw);
  const tile = cut ? prog(f, 9, 21, inOut) : prog(f, 22, 40, inOut);
  const s = cut ? prog(f, 13, 25, inOut) : prog(f, 30, 50, inOut);
  const ink = cut ? prog(f, 19, 29) : prog(f, 40, 54);
  const dot = cut ? prog(f, 22, 31) : prog(f, 46, 60);
  const sheen = cut ? prog(f, 30, 50, inOut) : prog(f, 58, 88, inOut);
  // The name as it's said ("Study Stash" about a third of a second into the line), then the tagline.
  const name = cut ? prog(f, vo, vo + 18) : prog(f, vo + 6, vo + 30);
  const line1 = cut ? 0 : prog(f, vo + 44, vo + 68);
  const line2 = cut ? 0 : prog(f, vo + 50, vo + 74);
  // Out into the setup, over the same desktop.
  const lift = cut ? prog(f, D - 26, D - 6, inOut) : prog(f, D - 34, D - 8, inOut);
  const leave = cut ? prog(f, D - 24, D - 4, inCubic) : prog(f, D - 30, D - 4, inCubic);
  const cards = vertical ? (cut ? CARDS_TALL_FAST : CARDS_TALL) : CARDS_WIDE;

  const iconSize = vertical ? 212 : 172;
  const iconTop = vertical ? 676 : 292;
  const nameTop = iconTop + iconSize + (vertical ? 48 : 40);
  const nameSize = vertical ? 124 : 120;
  const lineTop = nameTop + Math.round(nameSize * 1.12) + (vertical ? 34 : 26);
  const lineSize = vertical ? 54 : 46;
  const centreY = iconTop + iconSize / 2;

  const words: React.CSSProperties = {fontFamily: fonts.display, fontWeight: 600, fontSize: lineSize, lineHeight: 1.2, letterSpacing: '-0.015em', color: night.soft};
  const strong: React.CSSProperties = {color: night.teal};
  const t = text.tagline;

  return (
    <AbsoluteFill>
      <NightGround dim={0.62 * (1 - lift)} black={1 - prog(f, 0, cut ? 14 : 44, inOut)} zoom={1.06 - 0.06 * prog(f, 0, D - 10, inOut)} glow={{x: width / 2, y: centreY, r: vertical ? 760 : 820, a: tile * (1 - leave)}} />

      {cards.map((g) => (
        <Glimpse key={g.kind} g={g} f={f} cx={width / 2} cy={vertical ? 960 : 540} leave={leave} drift={0.22} />
      ))}

      <AbsoluteFill style={{opacity: 1 - leave, transform: `translateY(${px(-18 * leave)}px) scale(${px((1 - 0.03 * leave) * 10000) / 10000})`}}>
        <div style={{position: 'absolute', left: Math.round((width - iconSize) / 2), top: iconTop, transform: `scale(${px((0.94 + 0.06 * tile) * 10000) / 10000})`}}>
          <DrawnIcon size={iconSize} trace={trace} tile={tile} s={s} ink={ink} dot={dot} sheen={sheen} />
        </div>

        <div style={{position: 'absolute', left: 0, right: 0, top: nameTop, display: 'flex', justifyContent: 'center'}}>
          <Mask p={name}>
            <div style={{fontFamily: fonts.display, fontWeight: 700, fontSize: nameSize, lineHeight: 1.12, letterSpacing: '-0.03em', color: colors.onDesk, whiteSpace: 'nowrap'}}>{text.name}</div>
          </Mask>
        </div>

        <div style={{position: 'absolute', left: 0, right: 0, top: lineTop, display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center'}}>
          {vertical ? (
            <>
              <Mask p={line1}>
                <div style={words}>
                  {t.before}
                  <span style={strong}>{t.highlight}</span>
                </div>
              </Mask>
              <Mask p={line2}>
                <div style={words}>{t.after.trim()}</div>
              </Mask>
            </>
          ) : (
            <Mask p={line1}>
              <div style={{...words, whiteSpace: 'nowrap'}}>
                {t.before}
                <span style={strong}>{t.highlight}</span>
                {t.after}
              </div>
            </Mask>
          )}
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
