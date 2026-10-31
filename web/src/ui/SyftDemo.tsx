import { useState } from 'preact/hooks';
import { look, useTheme } from '../theme/useTheme';
import { useStore } from '../data/store';
import { SyftHello, SyftLoader, SyftMark, SyftSplash, SyftWordmark, type SplashVariant } from './Syft';

// Development only (`npm run dev`, then /app/syft-demo; main.tsx never loads it in a build): the pixel s's seven moving
// pieces side by side, as Joey's motion board has them, and the still marks the app uses, in the light or the dark.

const SPLASHES: { variant: SplashVariant; name: string; note: string }[] = [
  { variant: 'sortIn', name: 'S1 · Sort In', note: 'App start in the dark.' },
  { variant: 'sieveDrop', name: 'S2 · Sieve Drop', note: 'App start in the light.' },
  { variant: 'popBack', name: 'S3 · Pop Back', note: 'Setup’s welcome (without its screen or words).' },
];

export function SyftDemo() {
  useTheme();
  const chosen = useStore(look);
  const [round, setRound] = useState(0);
  const [progress, setProgress] = useState(0.4);
  const setAppearance = (appearance: 'light' | 'dark' | 'system') => look.set({ ...chosen, appearance });
  return (
    <div class="syft-demo">
      <header>
        <SyftWordmark size={40} />
        <p>Pixel Sift: splash screens, loaders and marks.</p>
        <div class="syft-demo-buttons">
          <button type="button" class="button tinted small" onClick={() => setRound((r) => r + 1)}>
            Play again
          </button>
          {(['light', 'dark', 'system'] as const).map((a) => (
            <button
              type="button"
              class={`button small ${chosen.appearance === a ? 'filled' : 'plain'}`}
              onClick={() => setAppearance(a)}
            >
              {a}
            </button>
          ))}
        </div>
      </header>

      <h2>Splash screens</h2>
      <div class="syft-demo-row" key={round}>
        {SPLASHES.map((s) => (
          <figure>
            <div class="syft-demo-phone">
              <SyftSplash variant={s.variant} framed />
            </div>
            <figcaption>
              <b>{s.name}</b> {s.note}
            </figcaption>
          </figure>
        ))}
      </div>

      <h2>Loaders</h2>
      <div class="syft-demo-row">
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader variant="pathTrace" tile size={200} />
          </div>
          <figcaption>
            <b>L1 · Path Trace</b> On its tile; a page or the screen waiting.
          </figcaption>
        </figure>
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader variant="pathTrace" size={96} />
          </div>
          <figcaption>
            <b>L1 · Path Trace</b> On the page (the lecture’s notes, Study).
          </figcaption>
        </figure>
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader variant="hoppingBits" size={40} label="Sifting…" />
          </div>
          <figcaption>
            <b>L2 · Hopping Bits</b> Buttons, inline and small spaces.
          </figcaption>
        </figure>
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader variant="pixelProgress" size={16} label="Sorting your notes…" />
          </div>
          <figcaption>
            <b>L3 · Pixel Progress</b> Imports and syncing, looping.
          </figcaption>
        </figure>
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader
              variant="pixelProgress"
              size={16}
              progress={progress}
              label={`Sending… ${Math.round(progress * 100)}%`}
            />
            <input
              type="range"
              min={0}
              max={1}
              step={0.01}
              value={progress}
              onInput={(e) => setProgress(Number((e.target as HTMLInputElement).value))}
            />
          </div>
          <figcaption>
            <b>L3 · Pixel Progress</b> With a real value.
          </figcaption>
        </figure>
        <figure>
          <div class="syft-demo-tile">
            <SyftLoader variant="rowShuffle" size={96} />
          </div>
          <figcaption>
            <b>L4 · Row Shuffle</b> Pull to refresh.
          </figcaption>
        </figure>
      </div>

      <h2>Marks</h2>
      <div class="syft-demo-row marks">
        <figure>
          <SyftMark size={140} />
          <figcaption>The mark (as the page is)</figcaption>
        </figure>
        <figure>
          <SyftMark size={120} tile="square" />
          <figcaption>On its tile</figcaption>
        </figure>
        <figure>
          <SyftMark size={64} tile="circle" bits />
          <SyftMark size={30} tile="circle" />
          <figcaption>Avatars (beside an answer: 30)</figcaption>
        </figure>
        <figure>
          <SyftMark size={110} oddOneOut />
          <figcaption>Odd One Out (empty, problems)</figcaption>
        </figure>
        <figure>
          <SyftMark size={120} showMesh scheme={chosen.appearance === 'dark' ? 'sorted' : 'segments'} bits />
          <figcaption>With the mesh (Search)</figcaption>
        </figure>
        <figure key={round}>
          <SyftHello size={120} />
          <figcaption>Pop Back on a page (setup)</figcaption>
        </figure>
      </div>
      <style>{DEMO_CSS}</style>
    </div>
  );
}

const DEMO_CSS = `
.syft-demo { min-height: 100%; padding: 32px 24px 64px; background: var(--ground); color: var(--fg); font-family: var(--font); overflow: auto; height: 100%; }
.syft-demo header p { color: var(--fg2); margin: 8px 0 16px; }
.syft-demo h2 { font-size: 20px; margin: 32px 0 16px; }
.syft-demo-buttons { display: flex; gap: 8px; flex-wrap: wrap; }
.syft-demo-row { display: flex; flex-wrap: wrap; gap: 24px; align-items: flex-start; }
.syft-demo figure { margin: 0; display: flex; flex-direction: column; gap: 10px; align-items: center; max-width: 220px; }
.syft-demo figcaption { font-size: 13px; color: var(--fg2); text-align: center; }
.syft-demo figcaption b { color: var(--fg); display: block; }
.syft-demo-phone { position: relative; width: 200px; height: 400px; border-radius: 30px; overflow: hidden; box-shadow: var(--shadow); }
.syft-demo-tile { width: 220px; height: 220px; border-radius: 24px; background: var(--raised); display: flex; flex-direction: column; gap: 12px; align-items: center; justify-content: center; box-shadow: var(--shadow); overflow: hidden; }
.syft-demo-tile input { width: 160px; }
.syft-demo-row.marks figure { min-height: 160px; justify-content: flex-end; }
`;
