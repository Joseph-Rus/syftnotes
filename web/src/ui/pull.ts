// Pull to refresh: how far the page follows the finger, and when letting go refreshes.

/** Past this (in points, after damping), letting go refreshes. */
export const PULL_THRESHOLD = 70;
/** Where the spinner rests while the refresh runs. */
export const PULL_REST = 56;

/** The finger's travel, damped like iOS's rubber band: easy at first, stiffer the further it goes. */
export function damp(dy: number): number {
  if (dy <= 0) return 0;
  const max = 140;
  return max * (1 - Math.exp(-dy / (max * 1.1)));
}

/** How far along to the threshold, 0 to 1, for the spinner's turn. */
export const progress = (distance: number) => Math.min(1, Math.max(0, distance / PULL_THRESHOLD));

/** Follows one pull: start when the page is at its top, move, and end (true: refresh). */
export class Pull {
  private startY: number | null = null;
  distance = 0;

  start(y: number, scrollTop: number) {
    this.startY = scrollTop <= 0 ? y : null;
    this.distance = 0;
  }

  /** The new distance; 0 when this touch isn't a pull (the page was scrolled, or it went up). */
  move(y: number, scrollTop: number): number {
    if (this.startY === null) return 0;
    if (scrollTop > 0) {
      this.startY = null;
      this.distance = 0;
      return 0;
    }
    this.distance = damp(y - this.startY);
    return this.distance;
  }

  end(): boolean {
    const go = this.startY !== null && this.distance >= PULL_THRESHOLD;
    this.startY = null;
    this.distance = 0;
    return go;
  }

  get active() {
    return this.startY !== null && this.distance > 0;
  }
}
