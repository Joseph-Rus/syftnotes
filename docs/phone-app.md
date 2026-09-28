# Study Stash: the phone app's web side

This is the web app the library serves at `/app/` — see [`docs/phone.md`](phone.md) for the library side (pairing,
the `device` cookie, Tailscale Serve on 8443, `/api/v2/lectures/{id}/rendered`) and how the two fit together. This
document is the PWA itself: `web/`, a Vite + TypeScript + Preact app that builds to `web/dist`.

```
web/src/
  api/        client.ts (typed /api/v2), ndjson.ts (line-by-line streaming), types.ts (every shape, from the C#)
  boot.ts     decides the first screen: install, pair, unreachable, outdated, or the app
  platform.ts which phone this is (iOS/Android/other), standalone or not, a name to pair with
  router.ts   every screen's address under /app/, Back, tab and depth (push/pop/swap transitions)
  format.ts   dates, durations, sizes, "This week" / "Tomorrow", the way the Mac says them
  data/       resource.ts (what's cached and re-asked), store.ts (a tiny shared-value store)
  theme/      themes.ts (the Mac's ten colour themes as CSS custom properties), useTheme.ts (puts them on the page)
  ui/         kit.tsx (the pieces every screen is built from), icons.tsx, pull.ts (pull-to-refresh), math.ts (KaTeX)
  screens/    Install, Pair, Unreachable, Home, ClassScreen, LectureScreen, Upload, ComingUp, Search, Settings
  App.tsx     the boot gate, then routes between screens; main.tsx wires it to the page and registers the service worker
```

## The boot gate

`boot.decide` (tested in `tests/boot.test.ts`) is the one function that decides what a phone sees first, given
`GET /api/v2/me`'s answer and a few local facts (is this running from the Home Screen, has this phone paired
before, did the student choose to skip installing):

1. **Not installed, not skipped:** the install screen — iOS's Share → Add to Home Screen, or Android's install
   prompt — with "Use it in the browser instead" to go on anyway.
2. **`GET /me` says `paired: false`:** the pairing screen — six digits with `autocomplete="one-time-code"` (so iOS
   and Android autofill it from a text message or, more usually here, from the code the student is looking at on
   the library's own screen), a name defaulting to what the phone calls itself ("iPhone", "iPad", …), and whatever
   the library says back in its own words when the code is wrong, expired, or tried too many times
   (`POST /api/v2/devices/pair`'s `{error}`, which the client passes straight through as `ApiError.detail`).
3. **The library can't be reached at all** (no network, Tailscale off, the computer asleep): the "Can't reach your
   library" screen with **Try again** — or, if this phone paired before, the library it read last time, offline.
4. **A library too old to know about phones** (no `/api/v2/me`, so `boot.ts` falls back to `/api/v2/library` with
   no password): a plain "update the library" dead end.
5. **Paired, reachable:** the app.

## The typed client

`api/client.ts`'s `Api` class is every `/api/v2` call the app uses, typed from the real C# (`api/types.ts` names
which file and function each shape comes from). Every request carries the `device` cookie automatically
(`credentials: 'same-origin'`); a `401` calls `onUnauthorized` (App.tsx sends the phone back to pairing) the same way
whether it came from a plain JSON response or from an upload's `XMLHttpRequest`. `ApiError.unreachable` (`status 0`)
is a network failure — no library to blame, just no way to ask it — and is what turns the offline banner on
(`data/resource.ts`'s `reachable` store).

Streaming (`ask`, `sendChat`) reads newline-delimited JSON as it arrives (`ndjson.ts`); this round doesn't use
either (Ask and chats are next round's work), but the client and its tests keep them working since the desktop app
depends on the same shapes.

### What merging `next` changed

Merging the library side in (attachments, calendars, devices, `/rendered`) meant reading the real C# rather than
guessing at the round-0.9 contract in the shared brief, and one shape needed fixing:

- **`Attachment.class` is never `null`.** `Attachment.ToJson` in `engine/src/StudyStash.Core/Attachments.cs` always
  writes a real class name (or `"Unsorted"`) — an upload with neither `class` nor `lecture` still lands somewhere.
  `types.ts` had it as `string | null`; fixed to `string`.
- **`Attachment.reading` wasn't in the contract at all.** The real JSON carries `reading` (still being read) beside
  `hasText` (found some words) — the difference the brief's "Reading your handwriting…" behaviour needs: `reading`
  true means keep asking, `hasText` false and `reading` false means there was nothing to read. Added to `types.ts`
  and used by `screens/attachments.tsx`'s `AttachmentRow` and the upload screen's polling.
- Everything else lined up exactly: `/api/v2/me`, `/devices/pair`'s `{device}` and cookie, `/lectures/{id}/rendered`'s
  `{id, title, class, date, html}`, the `PhoneNotes.Render` markup (`<span class="math" data-tex="…">`,
  `<figure class="diagram">`), and `/attachments`' upload response and 413/400 wording.

## Screens

- **Install / Pair / Unreachable** (`screens/Install.tsx`, `Pair.tsx`, `Unreachable.tsx`): one centred column, big
  touch targets, safe-area padding top and bottom. Pairing autofills the six digits the moment there are six of
  them; no separate submit tap needed unless autofill doesn't fire.
- **Home** (`screens/Home.tsx`): Coming up (`GET /api/v2/calendar/upcoming`, hidden entirely when the library has
  none), recent lectures, then every class with its dot (`theme/themes.ts`'s `classColor`, the same palette
  `ClassColors.Palette` draws on the Mac). Pull to refresh re-asks all three.
- **A class** (`ClassScreen.tsx`): its lectures grouped by when (`format.lectureGroup`: "This week", "Last week",
  then by month), and files attached to the class itself (not one lecture) with **Add files**.
- **A lecture** (`LectureScreen.tsx`): its notes from `/rendered`, KaTeX run over every `.math[data-tex]` element
  after the HTML lands (`ui/math.ts`'s `renderMath`; a formula KaTeX can't parse just keeps the plain-text fallback
  `PhoneNotes.Render` already wrote, `throwOnError: false`), and its attachments.
- **Add files** (`screens/Upload.tsx`): a camera button (`accept="image/*" capture="environment"`) and a Files
  button (PDFs, images, Office documents, multiple at once); each file uploads on its own so it gets its own
  progress bar and its own **Retry** on failure; anything over 200 MB is refused locally with the same words the
  library would use, before it's ever sent. Once uploaded, the list of what's already there polls every four
  seconds while anything is still `reading`, so "Reading your handwriting…" clears on its own once the library's
  OCR finishes — no manual refresh needed.
- **Coming up** (`ComingUp.tsx`) and **Search** (`Search.tsx`, a nice-to-have this round) round out the tab bar;
  **Settings** (`Settings.tsx`) says which library this phone is paired with and points back to the library's own
  computer for adding or removing phones. Ask, chats and assignments are next round's work — their routes exist
  (`router.ts` already has `ask` and `due`) but the tab bar and screens don't use them yet.

## Look and feel

`theme/themes.ts` works out the Mac's Liquid Glass tokens (`Skin.MacTokens`'s formulas, in `oklch()`) for light and
dark from one of its ten colour themes; `useTheme.ts` writes them as a `<style>` on the page and matches the
browser's own chrome (the status bar, Safari's tab bar) to it. The phone always runs Lagoon (the default) for now —
the library doesn't expose which of the ten a student picked over `/api/v2` yet, so there's nothing to read; adding
that is wave 2. `ui/kit.tsx` is every shared piece (navigation bar with a title that shrinks into it on scroll,
inset grouped lists, buttons, empty states, skeletons) and `ui/pull.ts` is pull-to-refresh, damped like iOS's rubber
band. An iPad (or a wide window) gets the sidebar layout in `styles/screens.css`'s media query instead of a bottom
tab bar.

## The PWA itself

- **`web/public/manifest.webmanifest`**: name Study Stash, `start_url`/`scope` `/app/`, `display: standalone`, and
  icons at 192 and 512 (plus maskable versions with a teal safe-zone background), made from `assets/icon.png` with
  `sips` (`sips -z 192 192 assets/icon.png --out web/public/icon-192.png`, and similarly for 512 and the padded
  maskable pair). `apple-touch-icon.png` is the repo's own 180×180 (`assets/apple-touch-icon.png`), copied as-is.
- **`index.html`** links the manifest and icons and carries iOS's own meta tags (`apple-mobile-web-app-capable`,
  `-title`, `-status-bar-style`), since iOS reads those instead of the manifest for how the app runs from the Home
  Screen.
- **`web/public/sw.js`** (Vite copies `public/` verbatim, so this lands at `dist/sw.js` unchanged — no build step
  needed to keep it at `/app/sw.js`, which is where the library's `Service-Worker-Allowed: /app/` header expects
  it): precaches the shell (`/app/`, the manifest, the two plain icons) on install; caches `assets/…` on first
  fetch and serves from cache after that (Vite names them by their contents, so they never go stale); for every
  other `/app/` file — the page itself, above all — asks the network first and falls back to what's cached (or the
  shell) when there's none, so navigating to `/app/lecture/42` still opens offline. It never touches anything under
  `/api/`: a lecture's notes, the list of classes, an upload — none of that is this cache's business, only
  `data/resource.ts`'s in-memory one and whatever the app chooses to keep. A new version waits for every open tab to
  close before it takes over (the safe default); it listens for a `SKIP_WAITING` message so a future "Update now"
  affordance can skip that wait.

## Testing

Vitest (`web/tests/`, `npm test` in `web/`): boot decisions (every gate, `tests/boot.test.ts`), the client and its
errors, NDJSON streaming, the router (every address, Back, tab/depth), formatting, themes, the resource cache, pull-
to-refresh's damping and threshold, KaTeX rendering (a good formula draws, a bad one leaves the plain text, no
throwing either way), and an attachment's kind-in-a-word. `npm run build` (`tsc --noEmit` then `vite build`),
`npm run typecheck` and `npm run lint` all run clean.

**Verified against the real library** (a scratch home under a temp directory, `STUDYSTASH_WEB_DIR` pointing at
`web/dist`, `studystash serve --home <scratch>`, curled from loopback so `RoleOf` treats the request as admin with
no login): `/app/` and every `/app/…` deep link serve the SPA shell with the phone CSP and `X-Frame-Options: DENY`;
`/app/sw.js` carries `Service-Worker-Allowed: /app/` and `no-cache`; `/app/manifest.webmanifest` is
`application/manifest+json`; a hashed `/app/assets/…` file is `public, max-age=31536000, immutable`; `/app` redirects
to `/app/`; a path with `..` or an unknown extension is a plain `404`; `/api/v2/me` answers `{paired: false, …}`
before any pairing. `POST /api/v2/devices/code` with the laptop's key answered `409` with the library's own words
("…needs Tailscale here first…HTTPS certificates…") exactly as `docs/phone.md` describes — this sandbox's tailnet
doesn't have HTTPS certificates turned on, so getting a real pairing code (and walking `/devices/pair`) end to end
wasn't possible here; the 409 path is the one this environment could actually prove.

## Left for wave 2

- **Ask, chats and assignments** — out of scope this round; the routes exist, the screens don't.
- **Which theme the student picked** — `/api/v2` has no way to say yet; the phone runs Lagoon regardless of what's
  set on the Mac. Once there's an endpoint, `useTheme` just needs a theme name instead of the default.
- **An "Update now" affordance** for the service worker's waiting version (the message plumbing is there; no UI
  calls it yet).
- **Search filters** (by class) and passages' own class colour (search currently shows a plain accent dot for a
  passage's class, since `Passage` carries a name but not the class's palette index).
