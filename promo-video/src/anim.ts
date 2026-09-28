import {Easing, interpolate, spring, SpringConfig} from 'remotion';
import {FPS} from './config';

export const snappy: Partial<SpringConfig> = {damping: 18, stiffness: 170, mass: 0.8};
export const soft: Partial<SpringConfig> = {damping: 22, stiffness: 110, mass: 1};
export const bouncy: Partial<SpringConfig> = {damping: 9, stiffness: 160, mass: 0.7};

/** A spring from 0 to 1 that starts `delay` frames in. */
export const spr = (frame: number, delay = 0, config: Partial<SpringConfig> = snappy) =>
  spring({frame: frame - delay, fps: FPS, config});

/** 0 → 1 over [start, end] with an ease-out, clamped. */
export const ease = (frame: number, start: number, end: number, easing = Easing.bezier(0.22, 1, 0.36, 1)) =>
  interpolate(frame, [start, end], [0, 1], {easing, extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});

/** Rises into place and fades in: the move most things on screen make. */
export const rise = (p: number, distance = 40): React.CSSProperties => ({
  opacity: Math.min(1, p * 1.4),
  transform: `translateY(${(1 - p) * distance}px)`,
});

/** The first part of `text` a typist would have typed by `frame`, starting at `start`, `perSecond` characters a second. */
export const typed = (text: string, frame: number, start: number, perSecond = 22) =>
  text.slice(0, Math.max(0, Math.floor(((frame - start) / FPS) * perSecond)));

/** The first `n` words of `text`, as an answer being written arrives. */
export const words = (text: string, frame: number, start: number, perSecond = 9) => {
  const all = text.split(' ');
  const n = Math.max(0, Math.min(all.length, Math.floor(((frame - start) / FPS) * perSecond)));
  return {shown: all.slice(0, n).join(' '), done: n >= all.length};
};

/** The whole scene fades out over its last `last` frames, so a cut never feels abrupt. */
export const outro = (frame: number, duration: number, last = 10) =>
  interpolate(frame, [duration - last, duration], [1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
