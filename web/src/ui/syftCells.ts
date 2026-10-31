// The pixel s ("Pixel Sift", Joey's logo for Syft Notes): its 24 cells and the numbers its splash screens and loaders
// move by, copied from his motion reference (syft-motion.html) and logo board. Everything is in SVG units: a cell is
// 6.6 square with corners of 1.8, on a pitch of 8 (a gap of 1.4), the grid 6 columns × 7 rows, 48 × 56.

export const CELL = 6.6;
export const RADIUS = 1.8;
export const PITCH = 8;
export const GRID_W = 48;
export const GRID_H = 56;

/**
 * The 24 cells' corners (x, y) in path order, p0 → p23: from the top right tip, across the top, down the left, across
 * the middle, down the right, along the bottom to the bottom left tip. What travels along the s goes in this order.
 */
export const CELLS: readonly (readonly [number, number])[] = [
  [40, 8],
  [40, 0],
  [32, 0],
  [24, 0],
  [16, 0],
  [8, 0],
  [0, 8],
  [8, 8],
  [0, 16],
  [8, 16],
  [8, 24],
  [16, 24],
  [24, 24],
  [32, 24],
  [32, 32],
  [40, 32],
  [32, 40],
  [40, 40],
  [32, 48],
  [24, 48],
  [16, 48],
  [8, 48],
  [0, 48],
  [0, 40],
];

/** Every slot of the 6 × 7 grid, empty or not: the mesh behind the s. */
export const MESH: readonly (readonly [number, number])[] = Array.from(
  { length: 42 },
  (_, i) => [(i % 6) * PITCH, Math.floor(i / 6) * PITCH] as const,
);

const L = 'var(--syft-lilac)',
  S = 'var(--syft-sky)',
  C = 'var(--syft-coral)',
  M = 'var(--syft-mustard)',
  I = 'var(--syft-ink)';

/** Rows 0–1 lilac, 2–3 sky, 4–5 coral, the bottom row `base` (p23, its tip, coral). */
const sorted = (base: string) => CELLS.map((_, i) => (i <= 7 ? L : i <= 13 ? S : i <= 17 || i === 23 ? C : base));

/**
 * Each look's colour for each cell, in path order.
 * - `sorted`: the motion board's Sorted / Night (S1, L1) and the app icon's dark tile: the bottom row mustard.
 * - `light`: the logo board's white tile: the bottom row ink.
 * - `auto`: one or the other as the page is light or dark (--syft-base).
 * - `segments` (S2): each stroke its own colour, the middle one ink.
 * - `ink` (S3, Odd One Out): every cell the page's ink (paper on a dark page).
 */
export const SCHEMES = {
  sorted: sorted(M),
  light: sorted(I),
  auto: sorted('var(--syft-base)'),
  segments: CELLS.map((_, i) => (i <= 5 ? L : i <= 9 ? S : i <= 13 ? I : i <= 17 ? C : M)),
  ink: CELLS.map(() => 'var(--syft-cell)'),
} as const;

export type Scheme = keyof typeof SCHEMES;

/** A loose bit: its centre (x, y), its turn in degrees and its colour. Bits are the size of a cell. */
export interface Bit {
  x: number;
  y: number;
  r: number;
  color: string;
}

/** From the logo board's centres, in pitches from the middle of the top left cell. */
const bit = (col: number, row: number, r: number, color: string): Bit => ({
  x: CELL / 2 + col * PITCH,
  y: CELL / 2 + row * PITCH,
  r,
  color,
});

/** The white tile's six bits, tumbling above the s: coral, lilac, sky, mustard, lilac, ink, left to right. */
export const BITS_ABOVE: readonly Bit[] = [
  bit(0.3, -1.5, 20, C),
  bit(1.0, -1.7, -30, L),
  bit(1.5, -2.6, 15, S),
  bit(2.6, -2.1, -25, M),
  bit(3.8, -2.3, 35, L),
  bit(4.8, -1.9, -20, 'var(--syft-base)'),
];

/** The dark tile's three bits falling out under the s: coral, sky, lilac. */
export const BITS_BELOW: readonly Bit[] = [bit(1.0, 7.6, 20, C), bit(2.8, 8.5, 20, S), bit(4.3, 7.4, 20, L)];

/** The light mesh's (P6) bits under the s: coral, sky, mustard. */
export const BITS_BELOW_SEGMENTS: readonly Bit[] = [bit(1.0, 7.6, 20, C), bit(2.8, 8.5, 20, S), bit(4.3, 7.4, 20, M)];

/** Odd One Out (P5) and S3's start: the p1 cell popped up and right of its slot, and turned. */
export const POP = { dx: 10, dy: -14, r: 25 } as const;

/** S3's three little coral ticks over the p1 slot, in the grid's units (the reference's lines less its 26.7, 70). */
export const TICKS: readonly (readonly [number, number, number, number])[] = [
  [43.3, -4, 43.3, -8],
  [48.3, -2.5, 51.3, -5.5],
  [38.3, -2.5, 35.3, -5.5],
];

// --- The splash screens: a 100 × 200 phone, the s at (26.7, 70), the wordmark's baseline at 152 ---------------------

export const SPLASH_X = 26.7;
export const SPLASH_Y = 70;

/** S1 · Sort In: each cell's start (dx, dy), spin r and delay in seconds. The bottom rows land first. */
export const SORT_IN: readonly { dx: number; dy: number; r: number; delay: number }[] = [
  { dx: -10, dy: -92, r: 70, delay: 0.64 },
  { dx: 8, dy: -86, r: -60, delay: 0.76 },
  { dx: -12, dy: -94, r: 90, delay: 0.74 },
  { dx: 10, dy: -90, r: -110, delay: 0.78 },
  { dx: -8, dy: -84, r: 70, delay: 0.72 },
  { dx: 12, dy: -92, r: -80, delay: 0.8 },
  { dx: 14, dy: -100, r: -100, delay: 0.62 },
  { dx: -6, dy: -88, r: 50, delay: 0.66 },
  { dx: 10, dy: -96, r: 110, delay: 0.52 },
  { dx: -14, dy: -104, r: -80, delay: 0.55 },
  { dx: 12, dy: -95, r: 60, delay: 0.4 },
  { dx: -8, dy: -112, r: -120, delay: 0.44 },
  { dx: 15, dy: -100, r: 80, delay: 0.38 },
  { dx: -12, dy: -108, r: -50, delay: 0.42 },
  { dx: -14, dy: -100, r: -70, delay: 0.3 },
  { dx: 9, dy: -118, r: 100, delay: 0.27 },
  { dx: -10, dy: -110, r: 70, delay: 0.16 },
  { dx: 6, dy: -120, r: -110, delay: 0.2 },
  { dx: 8, dy: -110, r: 80, delay: 0 },
  { dx: -12, dy: -120, r: -60, delay: 0.05 },
  { dx: 14, dy: -105, r: 120, delay: 0.02 },
  { dx: -6, dy: -125, r: -90, delay: 0.08 },
  { dx: 10, dy: -115, r: 45, delay: 0.04 },
  { dx: 12, dy: -105, r: 90, delay: 0.14 },
];

/** S2 · Sieve Drop: the cells light up along the path, 0.5 s + index × 0.05 s. */
export const sieveDelay = (i: number) => 0.5 + i * 0.05;

/** S2's six loose bits falling straight through: x, colour, end turn, duration and delay (5.6 square, corners 1.6). */
export const SIEVE_BITS: readonly { x: number; color: string; r: number; duration: number; delay: number }[] = [
  { x: 30, color: C, r: 220, duration: 2.6, delay: 0 },
  { x: 46, color: S, r: -180, duration: 3.1, delay: 0.7 },
  { x: 58, color: L, r: 260, duration: 2.8, delay: 1.4 },
  { x: 70, color: M, r: -240, duration: 3.3, delay: 0.3 },
  { x: 38, color: I, r: 200, duration: 2.9, delay: 2 },
  { x: 64, color: C, r: -200, duration: 2.7, delay: 2.4 },
];

/** S3 · Pop Back: the ink cells pop in a row at a time, 0.2 s + row × 0.06 s. */
export const popDelay = (y: number) => 0.2 + (y / PITCH) * 0.06;

// --- The loaders -----------------------------------------------------------------------------------------------------

/** L1 · Path Trace: each cell lights 0.075 s after the one before it. */
export const traceDelay = (i: number) => i * 0.075;

/** L2 · Hopping Bits: four 10-square bits (corners 2.8) at y 44, each 0.12 s after the one before. */
export const HOPPERS: readonly { x: number; color: string; delay: number }[] = [
  { x: 24, color: L, delay: 0 },
  { x: 38, color: S, delay: 0.12 },
  { x: 52, color: C, delay: 0.24 },
  { x: 66, color: M, delay: 0.36 },
];

/** L3 · Pixel Progress: ten cells at y 44 from x 10.7, filling 0.15 s apart. */
export const PROGRESS: readonly { x: number; color: string; delay: number }[] = [L, L, L, S, S, C, C, C, M, M].map(
  (color, i) => ({
    x: 10.7 + i * PITCH,
    color,
    delay: i * 0.15,
  }),
);

/**
 * L4 · Row Shuffle: the chunky s (12-square cells, corners 3.5, at 23, 16), a row at a time, each from its own side
 * (`from`: -1 left, 1 right) 0.08 s after the one before. The bottom row is ink on a light page, mustard on a dark one.
 */
export const SHUFFLE_ROWS: readonly {
  from: -1 | 1;
  delay: number;
  color: string;
  cells: readonly (readonly [number, number])[];
}[] = [
  {
    from: -1,
    delay: 0,
    color: C,
    cells: [
      [14, 0],
      [28, 0],
      [42, 0],
    ],
  },
  { from: 1, delay: 0.08, color: M, cells: [[0, 14]] },
  {
    from: -1,
    delay: 0.16,
    color: S,
    cells: [
      [14, 28],
      [28, 28],
    ],
  },
  { from: 1, delay: 0.24, color: L, cells: [[42, 42]] },
  {
    from: -1,
    delay: 0.32,
    color: 'var(--syft-base)',
    cells: [
      [0, 56],
      [14, 56],
      [28, 56],
    ],
  },
];

/** L4's loop: 2.6 s; a row is in place from 16% to 70% of it. */
export const SHUFFLE_SECONDS = 2.6;
