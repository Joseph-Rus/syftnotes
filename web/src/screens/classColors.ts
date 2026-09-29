import type { Api } from '../api/client';
import { useResource } from '../data/resource';
import { classColor } from '../theme/themes';

/** Each class's dot colour by name, from the library's own list (its place there is its colour, as on the Mac).
 * A name the library doesn't have gets the accent. Shares the Library screen's read, so it costs nothing extra. */
export function useClassColors(api: Api): (name: string | null | undefined) => string {
  const library = useResource('library', (signal) => api.library(signal));
  const byName = new Map((library.data?.classes ?? []).map((c) => [c.name, classColor(c.color)]));
  return (name) => (name && byName.get(name)) || 'var(--accent)';
}
