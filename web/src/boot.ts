import { Api, ApiError } from './api/client';
import type { Me } from './api/types';
import type { Platform } from './platform';

/** What the app shows first. */
export type Gate =
  | { kind: 'install'; platform: Platform }
  | { kind: 'unreachable' }
  | { kind: 'outdated' }
  | { kind: 'pair'; library: Me['library'] | null }
  | { kind: 'ready'; library: Me['library'] | null; offline: boolean };

export interface BootFacts {
  standalone: boolean;
  platform: Platform;
  /** The student chose to go on in the browser rather than install. */
  skippedInstall: boolean;
  /** This phone was paired before (so it has notes saved to read offline). */
  pairedBefore: boolean;
}

/**
 * Decide the first screen: in a phone's browser, how to install it; then, asking the library who this is, pairing
 * when it isn't paired, an explanation when the library can't be reached (Tailscale is off), or the app.
 */
export async function decide(api: Api, facts: BootFacts): Promise<Gate> {
  if (!facts.standalone && !facts.skippedInstall && facts.platform !== 'other')
    return { kind: 'install', platform: facts.platform };
  try {
    const me = await api.me();
    return me.paired ? { kind: 'ready', library: me.library, offline: false } : { kind: 'pair', library: me.library };
  } catch (e) {
    if (!(e instanceof ApiError)) throw e;
    if (e.unreachable) return facts.pairedBefore ? { kind: 'ready', library: null, offline: true } : { kind: 'unreachable' };
    if (e.status === 401) return { kind: 'pair', library: null };
    if (e.status === 404) {
      // A library from before the phone app: it can't pair phones. (Working against one with its password, it reads.)
      try {
        const lib = await api.library();
        return { kind: 'ready', library: { name: lib.name, version: lib.version }, offline: false };
      } catch (inner) {
        if (inner instanceof ApiError && inner.unreachable) return { kind: 'unreachable' };
        return { kind: 'outdated' };
      }
    }
    return { kind: 'unreachable' };
  }
}

/** Six digits from whatever was typed or pasted ("123 456", "Code: 123-456"), or fewer while it's being typed. */
export function cleanCode(text: string): string {
  return text.replace(/\D/g, '').slice(0, 6);
}
