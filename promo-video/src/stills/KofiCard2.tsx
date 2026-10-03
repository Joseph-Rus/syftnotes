import React from 'react';
import {AbsoluteFill} from 'remotion';
import {colors, fonts} from '../config';
import {Wallpaper, Win} from '../components/Layout';
import {Logo} from '../components/Logo';
import {around, boxAt, Flow, FLOW_W} from '../promo2/Flow';
import {FloatingBar, H2, PinActions} from '../promo2/ui';

// The second video's picture for a shared link (1200×630): the same desktop, what's new on the left, and on the right
// BIO 110's cardiac cycle with "Ventricles contract" pinned, as the video shows it.
const SCALE = 0.86;
const WIN = {w: 600, h: 520, left: 560, top: 55};

export const KofiCard2: React.FC = () => {
  const lit = around('contract');
  const pin = boxAt('contract');
  const flowLeft = (WIN.w - 56 - FLOW_W * SCALE) / 2;
  return (
    <AbsoluteFill style={{fontFamily: fonts.ui, color: colors.text}}>
      <div style={{position: 'absolute', left: 0, top: 0, width: 1200, height: 900}}>
        <Wallpaper />
      </div>
      <div style={{position: 'absolute', left: 64, top: 72, width: 460}}>
        <div style={{display: 'flex', alignItems: 'center', gap: 16}}>
          <Logo size={58} />
          <span style={{fontFamily: fonts.display, fontSize: 31, fontWeight: 600, letterSpacing: '-0.02em', color: colors.onDesk}}>Study Stash</span>
          <span style={{fontSize: 17, fontWeight: 700, color: '#FFFFFF', background: colors.lagoon, borderRadius: 999, padding: '4px 12px 5px'}}>New</span>
        </div>
        <div style={{marginTop: 56, fontFamily: fonts.display, fontSize: 64, fontWeight: 700, lineHeight: 1.03, letterSpacing: '-0.03em', color: colors.onDesk}}>
          Diagrams you can play with.
        </div>
        <div style={{marginTop: 24, fontSize: 23, lineHeight: 1.42, color: 'rgba(224, 229, 255, 0.82)'}}>
          Point at a box, pin it, step through it, test yourself. Labelled drawings, and formulas you can drag. Free for Mac
          and Windows.
        </div>
      </div>
      <div style={{position: 'absolute', left: WIN.left, top: WIN.top}}>
        <Win w={WIN.w} h={WIN.h}>
          <div style={{position: 'absolute', left: 28, top: 26, right: 28}}>
            <H2>The cycle, step by step</H2>
            <div style={{position: 'absolute', right: 0, top: -2}}>
              <FloatingBar icons={['play_circle', 'quiz', 'open_in_full']} />
            </div>
            <div style={{position: 'absolute', left: flowLeft, top: 58}}>
              <Flow look={{lit, dim: 1, accent: lit.edges, ring: 'contract'}} scale={SCALE} />
            </div>
            <div style={{position: 'absolute', left: flowLeft + (pin.x - pin.w / 2) * SCALE, top: 58 + (pin.y + pin.h / 2) * SCALE + 8, transform: 'scale(0.9)', transformOrigin: '0 0'}}>
              <PinActions p={1} />
            </div>
          </div>
        </Win>
      </div>
    </AbsoluteFill>
  );
};
