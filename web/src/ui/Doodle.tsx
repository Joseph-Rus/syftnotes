import { DOODLES, type DoodleName } from './doodles';

/**
 * One of the egghead's doodles (Eli's own drawing, and the ones drawn from it: ui/doodles.ts), drawn inline so its
 * ink and egg take the page's light or dark colours (--egg-ink, --egg-fill, --egg-white, --egg-yolk). `size` is its height.
 * `busy` brings him to life while something's happening: he bobs, and what he's working with moves (the pencil
 * writes, the sound comes in); still for anyone who asks for less motion.
 */
export function Doodle({ name, size = 112, busy = false }: { name: DoodleName; size?: number; busy?: boolean }) {
  const art = DOODLES[name];
  return (
    <svg class={busy ? 'doodle busy' : 'doodle'} viewBox={art.vb} style={{ height: `${size}px` }} aria-hidden="true">
      {art.fills.map(([kind, d]) => (
        <path d={d} fill={`var(--egg-${kind})`} />
      ))}
      <g fill="none" stroke="var(--egg-ink)" stroke-linecap="round" stroke-linejoin="round">
        {art.inks.map(([d, w]) => (
          <path d={d} stroke-width={w} />
        ))}
        {art.moving.length > 0 ? (
          <g class={`doodle-move ${name}`}>
            {art.moving.map(([d, w]) => (
              <path d={d} stroke-width={w} />
            ))}
          </g>
        ) : null}
      </g>
    </svg>
  );
}
