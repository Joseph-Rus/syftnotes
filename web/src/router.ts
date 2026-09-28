import { useEffect, useState } from 'preact/hooks';

/** Where the phone app lives on the library: every screen's address starts here. */
export const BASE = '/app/';

export type Route =
  | { name: 'library' }
  | { name: 'class'; class: string }
  | { name: 'lecture'; id: string }
  | { name: 'search'; q: string }
  | { name: 'ask'; chat: string | null }
  | { name: 'due' }
  | { name: 'coming-up' }
  | { name: 'upload'; class: string | null; lecture: string | null }
  | { name: 'settings' }
  | { name: 'not-found' };

/** The tabs along the bottom (and the iPad's sidebar). */
export type Tab = 'library' | 'search' | 'ask' | 'week' | 'upload' | 'settings';

export function tabOf(route: Route): Tab {
  switch (route.name) {
    case 'library':
    case 'class':
    case 'lecture':
    case 'not-found':
      return 'library';
    case 'due':
    case 'coming-up':
      return 'week';
    default:
      return route.name;
  }
}

/** How deep a screen sits in its tab: a deeper one slides in over the one before, as on iOS. */
export function depthOf(route: Route): number {
  switch (route.name) {
    case 'class':
      return 1;
    case 'lecture':
      return 2;
    case 'ask':
      return route.chat ? 1 : 0;
    default:
      return 0;
  }
}

const seg = (s: string) => encodeURIComponent(s);

/** A route's address, under /app/. */
export function href(route: Route): string {
  switch (route.name) {
    case 'library':
    case 'not-found':
      return BASE;
    case 'class':
      return `${BASE}class/${seg(route.class)}`;
    case 'lecture':
      return `${BASE}lecture/${seg(route.id)}`;
    case 'search':
      return route.q ? `${BASE}search?q=${seg(route.q)}` : `${BASE}search`;
    case 'ask':
      return route.chat ? `${BASE}ask/${seg(route.chat)}` : `${BASE}ask`;
    case 'due':
      return `${BASE}due`;
    case 'coming-up':
      return `${BASE}coming-up`;
    case 'upload': {
      const q = new URLSearchParams();
      if (route.class) q.set('class', route.class);
      if (route.lecture) q.set('lecture', route.lecture);
      const s = q.toString();
      return `${BASE}upload${s ? `?${s}` : ''}`;
    }
    case 'settings':
      return `${BASE}settings`;
  }
}

/** The route an address shows. Anything outside /app/ or unknown is the library's front screen or not found. */
export function parse(pathname: string, search = ''): Route {
  let path = pathname.startsWith(BASE) ? pathname.slice(BASE.length) : pathname === '/app' ? '' : null;
  if (path === null) return { name: 'library' };
  path = path.replace(/\/+$/, '');
  if (path === '' || path === 'index.html') return { name: 'library' };
  const parts = path.split('/').map((p) => {
    try {
      return decodeURIComponent(p);
    } catch {
      return p;
    }
  });
  const query = new URLSearchParams(search);
  const [head, arg] = parts;
  if (parts.length > 2) return { name: 'not-found' };
  switch (head) {
    case 'class':
      return arg ? { name: 'class', class: arg } : { name: 'library' };
    case 'lecture':
      return arg ? { name: 'lecture', id: arg } : { name: 'library' };
    case 'search':
      return { name: 'search', q: query.get('q') ?? '' };
    case 'ask':
      return { name: 'ask', chat: arg ?? null };
    case 'due':
      return { name: 'due' };
    case 'coming-up':
      return { name: 'coming-up' };
    case 'upload':
      return { name: 'upload', class: query.get('class'), lecture: query.get('lecture') };
    case 'settings':
      return { name: 'settings' };
    default:
      return { name: 'not-found' };
  }
}

export type Direction = 'push' | 'pop' | 'swap';

type Listener = (route: Route, direction: Direction) => void;
const listeners = new Set<Listener>();
let current: Route = typeof location === 'undefined' ? { name: 'library' } : parse(location.pathname, location.search);
let lastDirection: Direction = 'swap';

function directionBetween(from: Route, to: Route): Direction {
  if (tabOf(from) !== tabOf(to)) return 'swap';
  const a = depthOf(from),
    b = depthOf(to);
  return b > a ? 'push' : b < a ? 'pop' : 'swap';
}

function emit(route: Route, direction: Direction) {
  current = route;
  lastDirection = direction;
  for (const l of listeners) l(route, direction);
}

/** How many screens this app has added to the history since it opened: Back goes through them first. */
let steps = 0;

/** Go to a screen. `replace` swaps this one out of the history instead of adding to it. */
export function navigate(to: Route | string, options: { replace?: boolean } = {}) {
  const url = typeof to === 'string' ? to : href(to);
  const u = new URL(url, location.href);
  const route = parse(u.pathname, u.search);
  if (options.replace) history.replaceState({ steps }, '', url);
  else history.pushState({ steps: ++steps }, '', url);
  emit(route, directionBetween(current, route));
}

/** Back one screen: through the history when this app put a screen there, else up to `fallback`. */
export function back(fallback: Route) {
  if (steps > 0) history.back();
  else navigate(fallback, { replace: true });
}

if (typeof window !== 'undefined') {
  history.replaceState({ steps: 0 }, '');
  window.addEventListener('popstate', (e) => {
    steps = (e.state as { steps?: number } | null)?.steps ?? 0;
    const route = parse(location.pathname, location.search);
    emit(route, directionBetween(current, route));
  });
}

/** The screen being shown, and which way the last move went (for its transition). */
export function useRoute(): { route: Route; direction: Direction } {
  const [state, setState] = useState({ route: current, direction: lastDirection });
  useEffect(() => {
    const l: Listener = (route, direction) => setState({ route, direction });
    listeners.add(l);
    if (state.route !== current) setState({ route: current, direction: lastDirection });
    return () => {
      listeners.delete(l);
    };
  }, []);
  return state;
}

/** Follow a plain link inside the app without reloading the page. */
export function onLinkClick(e: MouseEvent) {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = (e.target as Element | null)?.closest?.('a');
  if (!a || a.target || a.hasAttribute('download')) return;
  const url = new URL(a.href, location.href);
  if (url.origin !== location.origin || !url.pathname.startsWith(BASE)) return;
  e.preventDefault();
  navigate(url.pathname + url.search);
}
