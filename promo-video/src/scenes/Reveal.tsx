import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {colors, fonts, text} from '../config';
import {bouncy, ease, rise, soft, spr} from '../anim';
import {Night, Paper, useVertical} from '../components/Layout';
import {Logo} from '../components/Logo';

/** The name arrives: paper opens out of the night, the icon springs in and its full stop drops into place. */
export const Reveal: React.FC = () => {
  const frame = useCurrentFrame();
  const {width, height} = useVideoConfig();
  const vertical = useVertical();
  const open = spr(frame, 0, soft);
  const radius = open * Math.hypot(width, height) * 0.6;
  const icon = spr(frame, 6, {damping: 12, stiffness: 140, mass: 0.8});
  const dot = spr(frame, 20, bouncy);
  const name = spr(frame, 16);
  const line = spr(frame, 30);
  const mark = ease(frame, 46, 66);
  const iconSize = vertical ? 300 : 260;
  return (
    <AbsoluteFill>
      <Night />
      <AbsoluteFill style={{clipPath: `circle(${radius}px at 50% 50%)`}}>
        <Paper>
          <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', gap: 34, flexDirection: 'column'}}>
            <div style={{transform: `rotate(${(1 - icon) * -10}deg) scale(${0.7 + 0.3 * icon})`}}>
              <Logo size={iconSize} s={icon} dot={dot} />
            </div>
            <div
              style={{
                ...rise(name, 50),
                fontFamily: fonts.display,
                fontWeight: 700,
                fontSize: vertical ? 132 : 128,
                letterSpacing: '-0.03em',
                color: colors.ink,
                lineHeight: 1,
              }}
            >
              {text.name}
            </div>
            <div
              style={{
                ...rise(line, 30),
                fontFamily: fonts.serif,
                fontSize: vertical ? 58 : 54,
                color: colors.ink2,
                textAlign: 'center',
                maxWidth: vertical ? 900 : 1400,
                lineHeight: 1.25,
              }}
            >
              {text.tagline.before}
              <span style={{position: 'relative', whiteSpace: 'nowrap', color: colors.ink}}>
                <span
                  style={{
                    position: 'absolute',
                    left: -6,
                    right: -6,
                    top: '18%',
                    bottom: '6%',
                    background: colors.highlighter,
                    borderRadius: 6,
                    transform: `scaleX(${mark})`,
                    transformOrigin: 'left center',
                    zIndex: 0,
                  }}
                />
                <span style={{position: 'relative'}}>{text.tagline.highlight}</span>
              </span>
              {text.tagline.after}
            </div>
          </AbsoluteFill>
        </Paper>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
