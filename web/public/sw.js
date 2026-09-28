// Study Stash's service worker: caches the app's own files so it opens (and shows what was read before) with no
// network, and never touches /api/ — a lecture's notes, its list of classes, an upload: all of that is the app's
// own cache in resource.ts and localStorage, never this cache. Vite's build copies this file to dist/sw.js
// unchanged (it's in web/public/), and the library serves it at /app/sw.js with Service-Worker-Allowed: /app/.
//
// Bump CACHE when this file (or what it precaches) changes, so old entries are thrown away on activate.
const CACHE = 'study-stash-shell-v1';
const SCOPE = '/app/';
const SHELL = [SCOPE, `${SCOPE}manifest.webmanifest`, `${SCOPE}icon-192.png`, `${SCOPE}icon-512.png`];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.addAll(SHELL))
      .catch(() => {
        // offline on first install, or a file moved: the app still installs, just without a warm cache yet
      }),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n !== CACHE).map((n) => caches.delete(n))))
      .then(() => self.clients.claim()),
  );
});

// A new version waits for every open tab to close before it takes over, which is the safe default; a screen that
// offers "Update now" can skip that wait by sending this.
self.addEventListener('message', (event) => {
  if (event.data === 'SKIP_WAITING') self.skipWaiting();
});

function isAppFile(url) {
  return url.origin === self.location.origin && url.pathname.startsWith(SCOPE);
}

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  // Only /app/'s own files are ever handled here: never /api/, which is the library's own answers, live.
  if (!isAppFile(url)) return;

  // Vite names built assets by their contents, so once one's cached it never needs asking again.
  const immutable = url.pathname.startsWith(`${SCOPE}assets/`);
  if (immutable) {
    event.respondWith(
      caches.match(req).then(
        (hit) =>
          hit ||
          fetch(req).then((res) => {
            if (res.ok) caches.open(CACHE).then((cache) => cache.put(req, res.clone()));
            return res;
          }),
      ),
    );
    return;
  }

  // Every screen is the same page (the SPA), and it changes: ask the network first, so an update reaches the phone
  // as soon as it's reachable, and fall back to what was cached (or the shell itself) when it isn't.
  const navigation = req.mode === 'navigate';
  event.respondWith(
    fetch(req)
      .then((res) => {
        if (res.ok) caches.open(CACHE).then((cache) => cache.put(navigation ? SCOPE : req, res.clone()));
        return res;
      })
      .catch(() => caches.match(req).then((hit) => hit || caches.match(SCOPE))),
  );
});
