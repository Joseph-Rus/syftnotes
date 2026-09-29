/**
 * The QR code opens the library at its Tailscale IP over plain http, which any phone on the tailnet reaches whatever
 * its DNS settings (Tailscale's traffic is encrypted end to end, so plain http is safe there; the browser just can't
 * tell). When the library could also give its https name, the address carries it as `?https=`. A phone that can look
 * that name up (MagicDNS on) moves there, where the app can read offline and install as a real app; one that can't
 * stays here, where everything but offline reading works.
 *
 * Only an address straight from the QR code carries `?https=`, so only a phone about to pair moves: a paired phone's
 * cookie belongs to the address it paired on.
 */

/** How long the https name gets to answer before the phone stays where it is. */
export const PROBE_MS = 4000;

/** Whether `url` answers at all (any response: it's only asked whether the name resolves and connects). */
export type Probe = (url: string, ms: number) => Promise<boolean>;

export const fetchProbe: Probe = async (url, ms) => {
  const stop = new AbortController();
  const timer = setTimeout(() => stop.abort(), ms);
  try {
    await fetch(url, { mode: 'no-cors', cache: 'no-store', credentials: 'omit', signal: stop.signal });
    return true;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
};

/** The https app address the QR code offered, when it's one of Tailscale's own (…ts.net) and this page is plain http. */
export function offeredSecure(href: string): URL | null {
  const here = new URL(href);
  if (here.protocol !== 'http:') return null;
  const raw = here.searchParams.get('https');
  if (!raw) return null;
  let to: URL;
  try {
    to = new URL(raw);
  } catch {
    return null;
  }
  if (to.protocol !== 'https:' || !to.hostname.endsWith('.ts.net') || !to.pathname.startsWith('/app/')) return null;
  return to;
}

/** Where to go instead of here (the https app, when it answers), or null to stay. */
export async function secureAddress(href: string, probe: Probe = fetchProbe): Promise<string | null> {
  const to = offeredSecure(href);
  if (!to) return null;
  const ok = await probe(new URL('/api/v2/me', to).href, PROBE_MS);
  return ok ? to.href : null;
}

/** The address without `?https=`, so a bookmark or Home Screen icon made here is the plain one. */
export function withoutOffer(href: string): string {
  const url = new URL(href);
  url.searchParams.delete('https');
  return url.href;
}
