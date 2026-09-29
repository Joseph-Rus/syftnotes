# Study Stash: the phone app

The phone app is a web app (a PWA) that each student's own library serves. There's no App Store app and no server of
ours: the phone reaches the student's library over Tailscale, the student adds the page to their Home Screen, and it
opens full screen like any other app. It reads the library through the same `/api/v2` the desktop app uses.

```
phone (Safari / Chrome, on the tailnet)                    library (StudyStash serve, port 8000)
  scans the QR code in Settings                            Settings → Phone: POST /api/v2/devices/code
  https://<library>.ts.net:8443/app/  ── Tailscale Serve ─▶  tailscale serve --https=8443 → http://127.0.0.1:8000
  GET /app/, /app/assets/…                        ──────▶    LibraryWeb.Phone.cs: web/ (web/dist, built)
  POST /api/v2/devices/pair {code, name}          ──────▶    LibraryWeb.Devices.cs: Set-Cookie: device=…
  GET /api/v2/me, /library, /lectures, …          ──────▶    RequireKey: a paired phone's cookie counts as the key
  GET /api/v2/lectures/{id}/rendered              ──────▶    PhoneNotes.cs: notes as safe HTML, SVG, KaTeX markup
```

The code, library side: `engine/src/StudyStash.Library/` — `Devices.cs` (the paired phones, devices.json, and the
pairing code), `LibraryWeb.Devices.cs` (`/api/v2/devices/*` and the cookie check), `LibraryWeb.Phone.cs` (`/app/`,
`/api/v2/me`, `/api/v2/lectures/{id}/rendered`), `PhoneNotes.cs` (the notes as HTML), `LibraryWeb.PhoneSettings.cs`
(the library's own Settings page); `engine/src/StudyStash.Core/Qr.cs` (the QR code) and `ClaudeReach.cs` (Tailscale
Serve). App side: `ViewModels/PhonesModel.cs` and `Views/MacPhones.axaml` / `Views/WinPhones.axaml` (Settings →
Phone). Web side: `web/` (see [The web app](#the-web-app)).

## Reaching the library: Tailscale Serve on 8443

A PWA needs https: service workers, "Add to Home Screen" as an app, and Secure cookies all require it. The library
itself speaks plain http on its port (8000), so the phone reaches it through **Tailscale Serve**, which answers with a
real certificate for `<library>.<tailnet>.ts.net` and passes each request on to `http://127.0.0.1:8000`.

- **Port 8443, not 443.** The Claude connector already uses Serve (or Funnel) on 443 for the Claude port (8001,
  `ClaudeWeb.cs`), and Serve has one handler per https port: putting the library on 443 would take Claude's place
  (and turning Claude's on later would take the phone's). 8443 is one of the three https ports Tailscale allows
  (443, 8443, 10000). The phone app's address is therefore `https://<library>.ts.net:8443/app/`.
- **Serve, never Funnel.** The phone app is on the tailnet only: nothing of the library's API is put on the internet.
  The phone needs Tailscale, signed in to the same tailnet.
- **Turned on when a phone is added.** `POST /api/v2/devices/code` runs
  `tailscale serve --bg --https=8443 http://127.0.0.1:<port>` through `ClaudeReach.Set(port, internet: false, on:
  true, httpsPort: 8443)` before making the code, every time (it's idempotent, and the student may have turned it off).
  It's never turned off automatically: it only serves the library's own pages and API, which still ask for the
  password or a paired phone.
- **No https address.** When Tailscale isn't installed, isn't running, has no MagicDNS name, or the tailnet hasn't
  turned HTTPS certificates on, there's no code: the answer is `409 {detail, fix}`, where `detail` begins "Your phone
  reaches the library over Tailscale, so it needs Tailscale here first." and then says what to do (ClaudeReach's own
  words), and `fix` is the page that fixes it (tailscale.com/download, the DNS admin page, or the HTTPS page Tailscale
  printed). Settings shows the words with **Open the page** for the fix; the library's page links it too.

### What a request looks like when it arrives through Serve

Serve connects from `127.0.0.1` over plain http and adds `X-Forwarded-For`, `X-Forwarded-Proto: https`,
`X-Forwarded-Host` and `Tailscale-User-*` headers. The library's "on this computer, so you're in" rule (`IsLocal`)
already refuses any request with a forwarding header, so a phone through Serve is **not** taken for the library's own
computer (tested: `A_phone_through_tailscale_serve_is_not_taken_for_this_computer`). Nothing else needs the forwarded
scheme or host: the phone app's address comes from Tailscale's own name for the computer (`ClaudeReach.Set`'s answer),
never from a request, and the `device` cookie is set Secure regardless (the browser sees https).

## Adding a phone

1. On a computer that's in (the app's Settings → Phone, or the library's web Settings page), **Add a phone**:
   `POST /api/v2/devices/code` (the laptop's key, or a paired phone's cookie) →
   `{code: "042917", url: "https://mini.tail1234.ts.net:8443/app/", expires: "2026-09-28T12:10:00.0000000+00:00"}`.
   Settings shows a QR code of `url`, the code as "042 917", and how long it has left, counting down.
2. The phone's camera opens `url`; the web app asks `GET /api/v2/me`, sees `paired: false`, and asks for the code.
3. `POST /api/v2/devices/pair {code, name}`. Right: `200 {device: {id, name, added}}` and
   `Set-Cookie: device=<secret>; max-age=34560000; path=/; secure; samesite=strict; httponly`. Wrong: `401 {error}`.
   Too many wrong codes: `429 {error}`.
4. The app's Settings notices the new phone within a few seconds (it asks `GET /api/v2/devices` every third second
   while the code is on show), puts the code away and says "Sam's iPhone is added."

### The code

- 6 digits from a cryptographic random number, **10 minutes**, **one use**, and **one at a time**: a new code
  replaces the one on show. It's kept in memory only; a restart forgets it.
- Compared in constant time (`CryptographicOperations.FixedTimeEquals`), and the same work is done whether or not a
  code is on show. Spaces and dashes typed with it are ignored; a JSON number works too.
- **Guesses run out.** Each code takes 5 wrong tries and then stops working (a guess has at most 5 in a million
  chances). Across everyone, 10 wrong codes within 10 minutes stop all pairing (`429`) until the oldest is 10 minutes
  old. A lockout doesn't touch phones already paired.
- `name` is trimmed, put on one line, cut to 60 characters; with none, the phone is named from its browser ("iPhone",
  "iPad", "Android phone", "Android tablet", else "Phone").

### The phones, and removing one

- `home/devices.json` (owner-only, like `claude.json`): `{devices: [{id, name, token_hash, added, last_seen}]}`. Only
  the SHA-256 of each cookie's secret is kept; the secret itself is only ever in the `Set-Cookie` answer.
- `GET /api/v2/devices` → `{devices: [{id, name, added, lastSeen}]}` (ISO 8601), the most recently used first.
- `DELETE /api/v2/devices/{id}` → the list without it (`404` if there's no such phone). The next request with that
  phone's cookie is refused: the check reads devices.json again whenever another process has changed it, so a
  removal from the app (through the API) or from a second library process takes effect at once.
- `last_seen` is written at most once a minute per phone, so browsing doesn't rewrite the file on every request.
- At most 50 phones: pairing a 51st forgets the one heard from longest ago.

## The `device` cookie is a key to /api/v2

The single check is `RequireKey` in `LibraryWeb.cs`, which every `/api/v2` route goes through (`Api`/`ApiAsync`). Its
first line lets in a request **for a path under `/api/v2`** that carries a paired phone's `device` cookie — exactly
as if it had sent the library's password. So a phone can do everything the desktop app can through the API (including
making a code for another phone and removing phones), and nothing else:

- not the laptop routes outside `/api/v2` (`/api/ingest`, `/api/health`, `/api/notes`…),
- not the library's web pages (`/`, `/settings`…), which still want the password cookie (`pool`) or this computer.

`SameSite=Strict` keeps the cookie off requests started by other sites (`ts.net` is on the Public Suffix List, so
another tailnet machine's page is another site), `HttpOnly` keeps it from scripts, and 400 days is the longest a
browser keeps a cookie. With no library password set, `/api/v2` is open to anyone on the network, as it always was;
pairing still works, and `/api/v2/me` still tells a paired phone from one that isn't.

`GET /api/v2/me` needs no key: `{paired: bool, library: {name, version}}`, no-store. The web app asks it first.

## Serving the web app at /app/

- **Where the files are:** `STUDYSTASH_WEB_DIR`, else `web/` beside the app (`AppContext.BaseDirectory`). The Library
  project copies a built `web/dist` there (`StudyStash.Library.csproj`, only when `web/dist/index.html` exists), so it
  lands beside both `StudyStash serve` and the desktop app, and in anything published from them. `LibraryWebOptions.PhoneApp`
  overrides both (tests).
- **Not built:** every `/app/…` answers `404` with a page in the library's own look: "The phone app isn't here yet".
- **Every screen is the page (SPA fallback):** a path that isn't a file gets `index.html` (`/app/lectures/42`,
  `/app/search?q=cells`). A missing path *with* an extension (`/app/assets/index-old.js`) is a plain `404`, so a
  stale script is never answered with HTML. `/app` moves to `/app/` (301). No sign-in: the files are the same for
  everyone; what they show comes from `/api/v2`.
- **Nothing outside the folder:** hidden files (`.anything`), `..`, backslashes and colons in a path are refused, and
  the resolved path must be inside the folder.
- **Types:** ASP.NET's list, plus `.webmanifest` → `application/manifest+json` and `.js`/`.mjs` → `text/javascript`;
  text types get `; charset=utf-8`.
- **Caching:** `assets/…` (Vite names them by their content) → `public, max-age=31536000, immutable`; everything else
  (index.html, sw.js, the manifest, icons) → `no-cache` with Last-Modified, so an update reaches the phone on its
  next open.
- **Headers:** every file `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`; HTML pages also
  `X-Frame-Options: DENY` and the app's own CSP (`LibraryWeb.PhoneCsp`), not the library pages' nonce-based one:
  `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src
  'self'; worker-src 'self'; manifest-src 'self'; frame-ancestors 'none'`. So the web app has no inline scripts and
  loads nothing from a CDN: KaTeX and its fonts must be bundled into `web/dist`.
- **`/app/sw.js`** also gets `Service-Worker-Allowed: /app/`.

## A lecture's notes for the phone

`GET /api/v2/lectures/{id}/rendered` → `{id, title, class, date, html}`. The notes are the lecture's (written from
the transcript, or the ones it came with: the same as `/api/v2/lectures/{id}`'s `notes`), made into HTML by
`PhoneNotes.Render`:

- **Prose** through the same Markdown as the library's `/note/{id}` page (`Ui.RenderMd`: tables, footnotes,
  definition lists; **raw HTML off**; `javascript:`/`data:` links made `#`).
- **Mermaid flowcharts** (```` ```mermaid ````, or a bare fence that starts `flowchart`/`graph`) laid out by the app's
  own `DiagramLayout` (turned top-down when it's much wider than a phone) and drawn by `DiagramSvg` on its own white
  paper: `<figure class="diagram"><svg …>`. One that can't be read or laid out is shown as its source, a code block.
- **SVG drawings** (fenced or written straight into the notes) cleaned by `SafeSvg` (no scripts, event handlers,
  foreign objects, links or outside references): `<figure class="diagram"><svg …>`; one that can't be made safe is
  its reason, in italics (`<p class="diagram-problem">`).
- **Formulas** as KaTeX-ready markup: `<span class="math" data-tex="…">` inline, `<div class="math display"
  data-tex="…">` for a `$$` block (a `$$…$$` inside a line is a `span.math.display`). The element's text is the
  formula in plain characters (`MathText.Plain`: "α × β"), so it reads before KaTeX draws it; the web app calls
  `katex.render(el.dataset.tex, el, {displayMode})` on each.
- **Code** as `<pre><code class="language-…">`.

Nothing in `html` can run: tests feed it `<script>`, `onerror=`, `javascript:` links, scripts and `onload` inside SVG
and inside Mermaid labels, and check none comes out.

## In Settings

- **The app** (Mac and Windows, one `PhonesModel`): Settings → Your library → **Phone**. With no phone, one row: "Read
  your notes and lectures on your phone" and **Add a phone**. The code: the QR code on a white card (always black on
  white, in light and dark: that's what a camera reads; drawn as one path of squares, aliased, so it's crisp at any
  scale), the code large in a monospace font, "Works for 9:41 more, once." counting down, the three steps, **Done**,
  and **New code** once it runs out. Then **Phones**: each with when it was added and last used, and **Remove**. A
  refused code (no Tailscale) shows the library's words under it.
- **The library's page** (`/settings#phone`, "Your phone"): the same, server-drawn: **Add a phone** posts
  `/settings/phone`, the page after shows the QR code (an inline SVG from `Qr.Svg`), the code and a countdown; each
  phone has **Remove** (`/settings/phone/{id}/remove`).

## The web app

*Owned by the pwa-web work; this section describes its shape from the shared plan, and is filled in as it lands.*

- **Source:** `web/`, Vite + TypeScript + Preact. `npm run build` → `web/dist` (index.html, `assets/` hashed,
  `sw.js`, `manifest.webmanifest`, icons). Built with `base: '/app/'`.
- **Start:** `GET /api/v2/me`. Not paired → the pairing screen (6 digits, a name) → `POST /api/v2/devices/pair`.
  Paired → the library.
- **Reads** what the desktop app reads: `/api/v2/library`, `/lectures`, `/lectures/{id}`, `/lectures/{id}/rendered`,
  `/search`, `/chats`, `/assignments`, `/canvas/*`, `/inbox`, `/settings`, `/calendar/upcoming`, and uploads through
  `/api/v2/attachments`. Every call sends the cookie (same origin); a `401` means the phone was removed: back to the
  pairing screen.
- **Offline:** the service worker (scope `/app/`) caches the app's files; what it keeps of the library's answers is
  the web app's to say.
- **Notes:** the `html` from `/rendered`, styled by the web app (diagrams `max-width: 100%`, `height: auto`), with
  KaTeX run on `.math` elements; KaTeX's script, CSS and fonts come from `web/dist` (the CSP allows nothing else).

## Testing

- `DevicesTests`: codes (six digits, ten minutes, once, one at a time, spaces), guesses running out per code and
  across everyone, removal seen by another copy, last seen at most once a minute, names.
- `PhoneApiTests`: a code turns Serve on (8443) and gives the address; only a computer that's in can make one; no
  Tailscale → 409 in words; pairing sets the strict cookie; the cookie reads `/api/v2` but not the laptop routes or
  pages; wrong, reused and expired codes; the rate limit; removal locks out at once; `/me`; a request through Serve
  isn't this computer's.
- `PhoneAppTests`: SPA fallback, the CSP and headers, caching, the service worker's header, the manifest's type, 404s,
  paths outside the folder, and the page when it isn't built.
- `PhoneNotesTests`: prose, flowcharts, SVG, formulas and code; nothing that can run; the route.
- `PhoneSettingsPageTests`: the library's page: QR code and a code that pairs, Remove, no Tailscale.
- `QrTests`: the finder squares, a margin, black on white. (The QR code was also checked by decoding it with macOS's
  CoreImage detector, from the SVG and from screenshots of Settings in both looks.)
- App: `PhonesModelTests` (the code, countdown, a new phone noticed, Remove, a refused code, no/unreachable/older
  library), `PhonesViewTests` (both looks, both themes: black on white, filling its card, the code, Remove),
  `PhonesShots` (shots/mac-settings-phone-*.png, win-settings-phone-*.png).
