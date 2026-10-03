import {SpringConfig} from 'remotion';
import {spr} from '../anim';

/**
 * A spring from 0 to 1 that comes to rest and stays: it rises as `spr` does, but never goes past 1 and back, or sinks
 * back below where it got to. A bouncy spring's last little swings moved a card or a label by a pixel and back again
 * (and Remotion's overshootClamping only hides the swing above 1, not the dip after it), which read as shaking.
 */
export const settle = (frame: number, delay: number, config: Partial<SpringConfig>) => {
  let best = 0;
  for (let f = delay; f <= frame; f++) {
    best = Math.max(best, spr(f, delay, config));
    if (best >= 1) return 1;
  }
  return best;
};
