import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../../config';
import {useVertical} from '../../components/Layout';
import {inOut, LockedLaptop, Mask, NightGround, night, prog, px} from '../../promo2/scenes/Bookends';
import {length, voAt} from '../config';

// The promise, as the second video ends (Proof2), timed to the demo's line: a laptop drawn a line at a time, a
// recording on its screen and a padlock closing over it, "Your recordings stay on your computer." as Adam says they
// never leave it, then "Free and open source, for Mac and Windows." as he says that.

const DIM = 0.7;

export const Promise3: React.FC = () => {
  const f = useCurrentFrame();
  const vertical = useVertical();
  const {width} = useVideoConfig();
  const D = length('promise');
  const vo = voAt('promise');
  const draw = prog(f, 0, 30, inOut);
  const wave = prog(f, 12, 40);
  const lock = prog(f, 28, 42);
  const shut = prog(f, 40, 50, inOut);
  const l1 = prog(f, vo - 4, vo + 20);
  const l2 = prog(f, vo + 2, vo + 26);
  // "Free and open source, …" is the line's second sentence, about three seconds in.
  const sub = prog(f, vo + 84, vo + 104);
  const clear = prog(f, D - 18, D - 4, inOut);

  const lapW = vertical ? 520 : 380;
  const lapTop = vertical ? 540 : 250;
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
          <div style={{marginTop: vertical ? 44 : 34, fontFamily: fonts.ui, fontWeight: 500, fontSize: vertical ? 38 : 34, color: night.soft, opacity: sub, transform: `translateY(${px((1 - sub) * 14)}px)`}}>{text.proofLine}</div>
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
