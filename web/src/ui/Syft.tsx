import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { useIsDark } from '../theme/useTheme';
import {
  BITS_ABOVE,
  BITS_BELOW,
  BITS_BELOW_SEGMENTS,
  CELL,
  CELLS,
  GRID_H,
  GRID_W,
  HOPPERS,
  MESH,
  POP,
  PROGRESS,
  RADIUS,
  SCHEMES,
  SHUFFLE_ROWS,
  SHUFFLE_SECONDS,
  SIEVE_BITS,
  SORT_IN,
  SPLASH_X,
  SPLASH_Y,
  TICKS,
  popDelay,
  sieveDelay,
  traceDelay,
  type Bit,
  type Scheme,
} from './syftCells';

// Syft Notes' pixel s, Joey's "Pixel Sift": the mark, its three splash screens and its four loaders, drawn as SVG from
// the cells in ui/syftCells.ts and moved by the keyframes in styles/syft.css. The app's name is never drawn any other way.

type Style = JSX.CSSProperties & Record<`--${string}`, string>;

function Cell({
  x,
  y,
  fill,
  class: cls,
  style,
}: {
  x: number;
  y: number;
  fill?: string;
  class?: string;
  style?: Style;
}) {
  return <rect class={cls} x={x} y={y} width={CELL} height={CELL} rx={RADIUS} fill={fill} style={style} />;
}

/** A loose bit: a cell turned about its own centre. */
function BitCell({ b }: { b: Bit }) {
  return (
    <Cell
      x={b.x - CELL / 2}
      y={b.y - CELL / 2}
      fill={b.color}
      style={{ transform: `rotate(${b.r}deg)`, transformBox: 'fill-box', transformOrigin: 'center' }}
    />
  );
}

/** The pop-out of Odd One Out: the p1 cell in coral where S3 starts it, and the ticks over it. */
function Popped() {
  const [x, y] = CELLS[1]!;
  return (
    <>
      <g
        stroke="var(--syft-coral)"
        stroke-width={1.2}
        stroke-linecap="round"
        transform={`translate(${POP.dx} ${POP.dy})`}
      >
        {TICKS.map(([x1, y1, x2, y2]) => (
          <line x1={x1} y1={y1} x2={x2} y2={y2} />
        ))}
      </g>
      <Cell
        x={x}
        y={y}
        fill="var(--syft-coral)"
        style={{
          transform: `translate(${POP.dx}px, ${POP.dy}px) rotate(${POP.r}deg)`,
          transformBox: 'fill-box',
          transformOrigin: 'center',
        }}
      />
    </>
  );
}

export interface SyftMarkProps {
  /** Its height in CSS pixels (a tile's width and height). */
  size?: number;
  /**
   * The cells' colours: `auto` (the default) is the white tile's look on a light page and the dark tile's on a dark one;
   * `sorted`, `light`, `segments` and `ink` are ui/syftCells.ts's.
   */
  scheme?: 'auto' | Scheme;
  /**
   * The empty 6 × 7 grid behind it. Default: as the look has it (the dark tile does). With `auto`, the light mesh's
   * look in the light (P6: each stroke its own colour) and the night mesh's in the dark (P3).
   */
  showMesh?: boolean;
  /** The loose bits (above the s on the white tile, falling out below it on the dark one). Default: from 48 px tall. */
  bits?: boolean;
  /** Odd One Out: the top right cell popped out of its slot in coral (empty pages, problems). */
  oddOneOut?: boolean;
  /** On a tile: `circle` (an avatar) or `square` (the app icon's rounded square). */
  tile?: 'circle' | 'square';
  /** What a screen reader says it is; without one, it's decoration. */
  label?: string;
  class?: string;
}

/** The static pixel s. */
export function SyftMark({
  size = 96,
  scheme = 'auto',
  showMesh,
  bits,
  oddOneOut = false,
  tile,
  label,
  class: cls,
}: SyftMarkProps) {
  const dark = useIsDark();
  const look: Scheme =
    scheme === 'auto' ? (oddOneOut ? 'ink' : dark ? 'sorted' : showMesh ? 'segments' : 'light') : scheme;
  const mesh = showMesh ?? (scheme === 'auto' && dark && !oddOneOut);
  const loose = !oddOneOut && (bits ?? size >= 48);
  const looseBits = !loose
    ? []
    : look === 'light'
      ? BITS_ABOVE
      : look === 'segments'
        ? BITS_BELOW_SEGMENTS
        : BITS_BELOW;
  const colours = SCHEMES[look];

  // What it covers: the grid, and the bits or the popped cell beyond it.
  let [x0, y0, x1, y1] = [0, 0, GRID_W - 1.4, GRID_H - 1.4];
  for (const b of looseBits) {
    const reach = CELL * 0.71;
    x0 = Math.min(x0, b.x - reach);
    x1 = Math.max(x1, b.x + reach);
    y0 = Math.min(y0, b.y - reach);
    y1 = Math.max(y1, b.y + reach);
  }
  if (oddOneOut) {
    y0 = Math.min(y0, -23);
    x1 = Math.max(x1, 59);
  }
  const w = x1 - x0,
    h = y1 - y0;
  let box: [number, number, number, number];
  if (tile) {
    // The board's tiles leave about a fifth all round (an avatar's circle a little more).
    const side = Math.max(w, h) / (tile === 'circle' ? 0.6 : 0.66);
    box = [x0 + w / 2 - side / 2, y0 + h / 2 - side / 2, side, side];
  } else {
    const pad = 1;
    box = [x0 - pad, y0 - pad, w + 2 * pad, h + 2 * pad];
  }
  const width = tile ? size : (size * box[2]) / box[3];
  return (
    <svg
      class={`syft-mark ${cls ?? ''}`}
      viewBox={box.map((n) => Math.round(n * 100) / 100).join(' ')}
      width={width}
      height={size}
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : 'true'}
    >
      {tile === 'circle' ? (
        <circle cx={box[0] + box[2] / 2} cy={box[1] + box[3] / 2} r={box[2] / 2} fill="var(--syft-tile)" />
      ) : tile === 'square' ? (
        <rect x={box[0]} y={box[1]} width={box[2]} height={box[3]} rx={box[2] * 0.2237} fill="var(--syft-tile)" />
      ) : null}
      {mesh ? (
        <g fill="var(--syft-mesh)">
          {MESH.map(([x, y]) => (
            <Cell x={x} y={y} />
          ))}
        </g>
      ) : null}
      {looseBits.map((b) => (
        <BitCell b={b} />
      ))}
      {CELLS.map(([x, y], i) => (oddOneOut && i === 1 ? null : <Cell x={x} y={y} fill={colours[i]} />))}
      {oddOneOut ? <Popped /> : null}
    </svg>
  );
}

/** "syft notes" as the logo writes it: "syft" in Archivo Black Italic, " notes" in Archivo Regular. */
export function SyftWordmark({ size = 24 }: { size?: number }) {
  return (
    <span class="syft-wordmark" style={{ fontSize: `${size}px` }} aria-label="Syft Notes" role="img">
      <span class="syft-wordmark-syft" aria-hidden="true">
        syft
      </span>
      <span class="syft-wordmark-notes" aria-hidden="true">
        {' notes'}
      </span>
    </span>
  );
}

// --- Splash screens -------------------------------------------------------------------------------------------------

export type SplashVariant = 'sortIn' | 'sieveDrop' | 'popBack';

/** The wordmark under the s in the 100 × 200 phone (the reference's: 13 tall, centred at 50, baseline 152). */
function SplashWord({ class: cls }: { class: string }) {
  return (
    <text
      class={`syft-wordmark ${cls}`}
      x={50}
      y={152}
      text-anchor="middle"
      fill="var(--syft-word)"
      font-size={13}
      style={{ letterSpacing: '-0.3px' }}
    >
      <tspan class="syft-wordmark-syft">syft</tspan>
      <tspan class="syft-wordmark-notes"> notes</tspan>
    </text>
  );
}

function SortIn() {
  return (
    <>
      <g fill="var(--syft-mesh-dark)" transform={`translate(${SPLASH_X} ${SPLASH_Y})`}>
        {MESH.map(([x, y]) => (
          <Cell x={x} y={y} />
        ))}
      </g>
      <g transform={`translate(${SPLASH_X} ${SPLASH_Y})`}>
        {CELLS.map(([x, y], i) => {
          const c = SORT_IN[i]!;
          return (
            <Cell
              class="syft-sf-in"
              x={x}
              y={y}
              fill={SCHEMES.sorted[i]}
              style={{ '--dx': `${c.dx}px`, '--dy': `${c.dy}px`, '--r': `${c.r}deg`, animationDelay: `${c.delay}s` }}
            />
          );
        })}
      </g>
    </>
  );
}

function SieveDrop() {
  return (
    <>
      <g class="syft-sv-mesh" fill="var(--syft-mesh-light)" transform={`translate(${SPLASH_X} ${SPLASH_Y})`}>
        {MESH.map(([x, y]) => (
          <Cell x={x} y={y} />
        ))}
      </g>
      <g>
        {SIEVE_BITS.map((b) => (
          <rect
            class="syft-sv-fall"
            x={b.x}
            y={-10}
            width={5.6}
            height={5.6}
            rx={1.6}
            fill={b.color}
            style={{ '--r': `${b.r}deg`, animationDuration: `${b.duration}s`, animationDelay: `${b.delay}s` } as Style}
          />
        ))}
      </g>
      <g transform={`translate(${SPLASH_X} ${SPLASH_Y})`}>
        {CELLS.map(([x, y], i) => (
          <Cell
            class="syft-sv-pop"
            x={x}
            y={y}
            fill={SCHEMES.segments[i]}
            style={{ animationDelay: `${sieveDelay(i)}s` }}
          />
        ))}
      </g>
    </>
  );
}

function PopBack() {
  const [hx, hy] = CELLS[1]!;
  return (
    <>
      <g transform={`translate(${SPLASH_X} ${SPLASH_Y})`} fill="var(--syft-cell)">
        {CELLS.map(([x, y], i) =>
          i === 1 ? null : <Cell class="syft-sp-in" x={x} y={y} style={{ animationDelay: `${popDelay(y)}s` }} />,
        )}
        <Cell class="syft-sp-hop" x={hx} y={hy} fill="var(--syft-coral)" />
      </g>
      <g
        class="syft-sp-tick"
        stroke="var(--syft-coral)"
        stroke-width={1.2}
        stroke-linecap="round"
        transform={`translate(${SPLASH_X} ${SPLASH_Y})`}
      >
        {TICKS.map(([x1, y1, x2, y2]) => (
          <line x1={x1} y1={y1} x2={x2} y2={y2} />
        ))}
      </g>
    </>
  );
}

const SPLASH_CLASS: Record<SplashVariant, string> = {
  sortIn: 'sort-in syft-dark',
  sieveDrop: 'sieve-drop syft-light',
  popBack: 'pop-back syft-light',
};
const WORD_CLASS: Record<SplashVariant, string> = {
  sortIn: 'syft-sf-word',
  sieveDrop: 'syft-sv-word',
  popBack: 'syft-sp-word',
};

function SplashArt({ variant }: { variant: SplashVariant }) {
  return variant === 'sortIn' ? <SortIn /> : variant === 'sieveDrop' ? <SieveDrop /> : <PopBack />;
}

export interface SyftSplashProps {
  /** S1 Sort In (dark), S2 Sieve Drop (light) or S3 Pop Back (paper). */
  variant: SplashVariant;
  /** Whether the app is ready under it: it fades once this is true (after `minMs`), or at `maxMs` whatever. */
  ready?: boolean;
  /** Called once it has faded out: unmount it then. */
  onFinish?: () => void;
  minMs?: number;
  maxMs?: number;
  /** In a frame of its own instead of over the whole screen, and staying (the demo page). */
  framed?: boolean;
}

/**
 * The whole screen while the app starts: the s assembles once and the wordmark rises, it holds until the app is ready
 * (at least `minMs`, never more than `maxMs`), then fades out over 250 ms and says so with `onFinish`.
 */
export function SyftSplash({
  variant,
  ready = true,
  onFinish,
  minMs = 1200,
  maxMs = 4000,
  framed = false,
}: SyftSplashProps) {
  const [leaving, setLeaving] = useState(false);
  const started = useRef(typeof performance !== 'undefined' ? performance.now() : Date.now());
  const finish = useRef(onFinish);
  finish.current = onFinish;

  useEffect(() => {
    if (framed) return;
    if (leaving) {
      const t = setTimeout(() => finish.current?.(), 250);
      return () => clearTimeout(t);
    }
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    const elapsed = now - started.current;
    const wait = ready ? Math.max(0, minMs - elapsed) : Math.max(0, maxMs - elapsed);
    const t = setTimeout(() => setLeaving(true), wait);
    return () => clearTimeout(t);
  }, [ready, leaving, minMs, maxMs, framed]);

  return (
    <div class={`syft-splash ${SPLASH_CLASS[variant]}${leaving ? ' leaving' : ''}${framed ? ' framed' : ''}`}>
      <svg viewBox="0 0 100 200" role="img" aria-label="Syft Notes">
        <SplashArt variant={variant} />
        <SplashWord class={WORD_CLASS[variant]} />
      </svg>
    </div>
  );
}

/**
 * S3 Pop Back on a page, without its screen or words: the setup welcome's mark (the ink s pops in, its coral bit hops
 * home), played once. Ink on a light page, paper on a dark one.
 */
export function SyftHello({ size = 120 }: { size?: number }) {
  // The s and the hop above it, in the phone's units.
  const box = [22, 44, 64, 84] as const;
  return (
    <svg
      class="syft-mark"
      viewBox={box.join(' ')}
      width={(size * box[2]) / box[3]}
      height={size}
      role="img"
      aria-label="Syft Notes"
    >
      <PopBack />
    </svg>
  );
}

// --- Loaders --------------------------------------------------------------------------------------------------------

export type LoaderVariant = 'pathTrace' | 'hoppingBits' | 'pixelProgress' | 'rowShuffle';

export interface SyftLoaderProps {
  variant: LoaderVariant;
  /** Its height in CSS pixels. */
  size?: number;
  /** Words shown under it ("Sorting your notes…"). Screen readers hear "Loading" either way. */
  label?: ComponentChildren;
  /** Pixel Progress and Row Shuffle: how far along, 0 to 1, drawn as it is instead of looping. */
  progress?: number;
  /** Path Trace on its own dark tile, as the board has it. */
  tile?: boolean;
  class?: string;
}

/** Where the reference puts Path Trace's grid in its 100-square tile (the tile's corners: 32 of its 260 px). */
const TRACE_X = 26.7,
  TRACE_Y = 22.7;

/** Path Trace: the empty grid, the unlit s, and the lit trail. */
function PathTrace({ tile }: { tile: boolean }) {
  return (
    <>
      {tile ? <rect x={-TRACE_X} y={-TRACE_Y} width={100} height={100} rx={12.3} fill="var(--syft-tile)" /> : null}
      <g fill="var(--syft-trace-mesh)">
        {MESH.map(([x, y]) => (
          <Cell x={x} y={y} />
        ))}
      </g>
      <g fill="var(--syft-ghost)">
        {CELLS.map(([x, y]) => (
          <Cell x={x} y={y} />
        ))}
      </g>
      {CELLS.map(([x, y], i) => (
        <Cell class="syft-tr" x={x} y={y} fill={SCHEMES.sorted[i]} style={{ animationDelay: `${traceDelay(i)}s` }} />
      ))}
    </>
  );
}

function HoppingBits() {
  return (
    <>
      {HOPPERS.map((h) => (
        <rect
          class="syft-bb"
          x={h.x}
          y={44}
          width={10}
          height={10}
          rx={2.8}
          fill={h.color}
          style={{ animationDelay: `${h.delay}s` }}
        />
      ))}
    </>
  );
}

function PixelProgress({ progress }: { progress?: number }) {
  const lit = progress === undefined ? -1 : Math.min(10, Math.ceil(Math.max(0, progress) * 10 - 1e-9));
  return (
    <>
      <g fill="var(--syft-mesh)">
        {PROGRESS.map((q) => (
          <Cell x={q.x} y={44} />
        ))}
      </g>
      {PROGRESS.map((q, i) =>
        lit < 0 ? (
          <Cell class="syft-pg" x={q.x} y={44} fill={q.color} style={{ animationDelay: `${q.delay}s` }} />
        ) : (
          <Cell class={`syft-pg-step${i < lit ? ' on' : ''}`} x={q.x} y={44} fill={q.color} />
        ),
      )}
    </>
  );
}

/** How far a row has come in while pulling: rows deal in one after another as `progress` goes from 0 to 1. */
const dealt = (progress: number, row: number) => Math.min(1, Math.max(0, progress * SHUFFLE_ROWS.length - row));

function RowShuffle({ progress }: { progress?: number }) {
  return (
    <g transform="translate(23 16)">
      {SHUFFLE_ROWS.map((row, i) => {
        let cls: string | undefined;
        let style: Style | undefined;
        if (progress === undefined) {
          // Looping, entered with every row in place (a row is in from 16% to 70% of its 2.6 s), so a pull that ended
          // with the s dealt carries straight on.
          cls = row.from < 0 ? 'syft-rs-l' : 'syft-rs-r';
          const inPlace = 0.16 * SHUFFLE_SECONDS + SHUFFLE_ROWS[SHUFFLE_ROWS.length - 1]!.delay;
          style = { animationDelay: `${(row.delay - inPlace).toFixed(3)}s` };
        } else {
          const f = dealt(progress, i);
          const eased = 1 - (1 - f) * (1 - f);
          style = { transform: `translateX(${(row.from * 80 * (1 - eased)).toFixed(2)}px)`, opacity: `${f}` };
        }
        return (
          <g class={cls} style={style}>
            {row.cells.map(([x, y]) => (
              <rect x={x} y={y} width={12} height={12} rx={3.5} fill={row.color} />
            ))}
          </g>
        );
      })}
    </g>
  );
}

/** Each loader's drawing, cropped to what moves in it (the reference draws each in a 100-square tile). */
const LOADER_BOX: Record<LoaderVariant, readonly [number, number, number, number]> = {
  pathTrace: [-4, -4, 54.6, 62.6],
  hoppingBits: [21, 28, 58, 29],
  pixelProgress: [9.7, 43, 80.6, 8.6],
  rowShuffle: [0, 12, 100, 76],
};

/**
 * A loader: L1 Path Trace (a page or the whole screen waiting), L2 Hopping Bits (buttons and inline), L3 Pixel Progress
 * (imports and syncing, with `progress` when there's a real value), L4 Row Shuffle (pull to refresh).
 */
export function SyftLoader({ variant, size, label, progress, tile = false, class: cls }: SyftLoaderProps) {
  const box: readonly [number, number, number, number] =
    variant === 'pathTrace' && tile ? [-TRACE_X, -TRACE_Y, 100, 100] : LOADER_BOX[variant];
  const height =
    size ?? (variant === 'pathTrace' ? 72 : variant === 'pixelProgress' ? 10 : variant === 'rowShuffle' ? 36 : 20);
  const moving = progress === undefined || variant === 'pathTrace' || variant === 'hoppingBits';
  return (
    <span
      class={`syft-loader${moving ? ' moving' : ''}${tile ? ' syft-dark' : ''} ${cls ?? ''}`}
      role="status"
      aria-live="polite"
    >
      <svg viewBox={box.join(' ')} width={(height * box[2]) / box[3]} height={height} aria-hidden="true">
        {variant === 'pathTrace' ? (
          <PathTrace tile={tile} />
        ) : variant === 'hoppingBits' ? (
          <HoppingBits />
        ) : variant === 'pixelProgress' ? (
          <PixelProgress progress={progress} />
        ) : (
          <RowShuffle progress={progress} />
        )}
      </svg>
      {label ? <span class="syft-loader-label">{label}</span> : null}
      <span class="syft-sr">Loading</span>
    </span>
  );
}

/** Which splash fits the page: S1 Sort In in the dark, S2 Sieve Drop in the light. */
export function useSplashVariant(): SplashVariant {
  return useIsDark() ? 'sortIn' : 'sieveDrop';
}
