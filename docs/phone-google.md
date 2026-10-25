# The phone over Google Drive: the contract

Written 2026-10-24 for two lanes built side by side: `phone-google` (the laptop, C#) and `phone-google-ios` (the
iPhone app, Swift). Both build to this file. Each lane copies it into its branch as `docs/phone-google.md` and keeps
it true to what it built; if a lane has to change the contract, it writes the change at the bottom under
"Changes asked for" and says so in its report, it doesn't silently do something else.

## What it's for

A student with **just this computer** (`AppRole.Both`: the app runs its own library on 127.0.0.1) connects their
phone through their own Google Drive instead of Tailscale. No server of ours, no VPN.

- Laptop: Settings → Phone → **Connect with Google** → the browser opens Google → they pick an account → Allow. The
  page then shows the account, a QR code for the phone, and **Disconnect**.
- Phone: **Continue with Google** (or scan the laptop's QR code, which opens the app and pre-picks the same account)
  → Google's sheet → Allow → the library shows.
- The phone shows what the laptop put in the folder, and everything it makes or changes (a recording written down on
  the phone, typed notes, notebook pages, files, voice memos, renames, Ask) goes back through the folder to the
  laptop's library, which does it exactly as if the phone had asked over Tailscale.

**Not changed:** a laptop whose library is on another computer, and a library-only computer, keep Tailscale exactly as
today (Settings → Phone → Add a phone, `/api/v2/devices/*`, Tailscale Serve). In just-this-computer mode, phones paired
over Tailscale before keep working and stay listed.

## Measured (2026-10-24, real Drive)

One write takes ~0.9 s; another reader sees it ~1.7 s after the write starts, even reading non-stop. Watching a file
**by its id** is much faster than Drive's change list (`changes.list` lagged 2-4 s more). So: every file both sides
watch is found once by name, then read by id. A question costs two trips, about 4 s before Claude's first words;
answer pieces arrive ~1.6 s behind. Do nothing that adds a trip on Ask's path.

## Google

- Project `study-stash-app` (number 1073354844610). Drive API is on. Consent screen "Study Stash", External, in
  Testing (test user russelljoey147@gmail.com).
- Scopes, both sides: `openid email https://www.googleapis.com/auth/drive.appdata`. Nothing else.
- Everything lives in Drive's hidden **appDataFolder** (`parents: ["appDataFolder"]`, `spaces=appDataFolder`). It's
  per Google project: the laptop's and the phone's sign-in IDs are in the same project, so they see the same folder.
- **Laptop** sign-in ID: the Desktop client `1073354844610-rlceur96jtct2rnhb5616c14n3fnmf6e.apps.googleusercontent.com`.
  Loopback redirect (`http://127.0.0.1:<any port>`), PKCE S256, `access_type=offline`, `prompt=consent` the first
  time. Its client secret is **never committed** (the repo is public; GitHub's secret scanning flags `GOCSPX-`): the
  build reads it from a git-ignored file or an environment variable, and CI from a repository secret
  `GOOGLE_DESKTOP_CLIENT_SECRET`. A build without it shows Connect with Google as unavailable, in words, and every test
  that doesn't need real Google still passes.
- **Phone** sign-in ID: an iOS client for bundle `app.studystash.ios` (Joey is making it; until then a build setting
  `GOOGLE_IOS_CLIENT_ID` with a placeholder). Redirect `com.googleusercontent.apps.<id>:/oauth2redirect` through
  `ASWebAuthenticationSession`, PKCE, no secret, no Google SDK. Tokens in the Keychain.
- Tokens: the laptop keeps the refresh token in `<home>/google.json`, owner-only (`Py.OwnerOnly`, like devices.json).
  The account's email comes from the id_token. Disconnect revokes (`https://oauth2.googleapis.com/revoke`), deletes
  the folder's files this laptop wrote, and forgets google.json.

## The folder

Every name below can carry a prefix for tests (`STUDYSTASH_DRIVE_SPACE` on the laptop, launch argument
`-drive-space <prefix>` in a debug iPhone build): `<prefix>laptop.json` and so on. Real use has no prefix. Test runs
always use a prefix of their own and delete their files when done.

Each file has **one writer**. Nobody ever writes a file the other side writes.

| File | Writer | What |
|---|---|---|
| `laptop.json` | laptop | who the laptop is, the copy's index, which requests it has answered |
| `copy-<hash>.json` | laptop | one answer of the library's, exactly as `/api/v2` gave it |
| `blob-<hash>` | laptop | the bytes of a file the phone asked for (an attachment's raw) |
| `phone-<phoneId>.json` | phone | that phone's box: who it is, whether it's open, its requests |
| `upload-<requestId>` | phone | the bytes of a file the phone sends (attachment, voice memo) |
| `answer-<requestId>.json` | **laptop writes, phone creates** | the phone creates it empty (`{}`) before asking, so it knows the id to watch; then only the laptop writes it |

### laptop.json

```json
{
  "v": 1,
  "laptop": {"id": "<stable id of this install>", "name": "Joey's MacBook Air", "version": "0.17.3"},
  "seen": "2026-10-24T17:40:02Z",
  "copy": {
    "GET /library": {"file": "<drive id>", "sha": "<sha256 hex of the body>", "size": 1234},
    "GET /lectures/rec-abc": {"file": "…", "sha": "…", "size": 0}
  },
  "answered": ["<request id>", "…"]
}
```

- `seen` is rewritten at least every 60 s while the laptop runs; the phone says "Your laptop was last here 3 hours
  ago" from it.
- `answered`: the last 500 request ids it has finished, so a request is never done twice.

### The copy (laptop → phone)

The laptop asks its own library (`GET http://127.0.0.1:<port>/api/v2/…` with the library's password, as
`RemoteLibrary` does) and keeps these in the folder, each as one `copy-` file, the body exactly as the library gave it:

- `GET /library`
- `GET /lectures?limit=<all>` (every lecture, newest first; the phone filters by `class`, `limit` and `before`
  itself, with the library's own rules; the laptop lane reads `Reader.Lectures` and writes those rules down here)
- for every lecture: `GET /lectures/<id>` and `GET /lectures/<id>/rendered`
- `GET /chats`, and `GET /chats/<id>` for each
- `GET /canvas/due`, `GET /calendar/upcoming`
- `GET /attachments` (all; the phone filters by `class` and `lecture`)
- `GET /voice-memos`

Key: the method, a space, the path after `/api/v2` and its query, keys sorted (`GET /lectures?limit=1000`). The
`copy-` file name is the first 16 hex characters of the key's sha256.

The laptop refreshes the copy about every 15 s and straight after it answers any request that changes something.
Only answers whose sha changed are uploaded; a lecture that's gone has its files deleted and its keys removed.
The first copy of a big library goes in the background, the list and the newest lectures first.

The phone reads `laptop.json` on opening and about every 10 s while open, downloads only the files whose sha it
doesn't already have, and keeps them on the phone (the app group container, so the widgets read them too). It opens
on what it has, at once, offline included.

### Requests (phone → laptop)

The phone's box, `phone-<phoneId>.json`:

```json
{
  "v": 1,
  "phone": {"id": "<stable uuid of this install>", "name": "Joey's iPhone", "model": "iPhone"},
  "open_until": "2026-10-24T17:41:30Z",
  "requests": [
    {"id": "<uuid>", "at": "…", "method": "POST", "path": "/lectures/rec-abc/title",
     "json": {"title": "Limits"}, "answer": "<drive id of answer-<id>.json>"},
    {"id": "<uuid>", "at": "…", "method": "POST", "path": "/attachments", "json": null,
     "upload": {"file": "<drive id of upload-<id>>", "name": "page.pdf", "type": "application/pdf",
                "fields": {"class": "Calculus II", "lecture": "phone-…"}},
     "answer": "…"}
  ]
}
```

- `open_until`: the phone sets it 90 s ahead when the app comes to the front and every 60 s while it stays there,
  and to now when it goes to the back. While any box is open, the laptop reads open boxes **every second, by id**;
  otherwise every 15 s. It looks for new boxes (`name contains 'phone-'`) every 60 s and when it starts.
- Asking: create `answer-<id>.json` (`{}`), then add the request to the box, in that order. For Ask and chat, the
  phone creates a spare answer file as soon as the student starts typing (and sends `POST /ai/warm` then, as today),
  so sending the question is one write.
- The phone takes a request out of its box when its answer is `done`, or when it gives up on it (below). The laptop
  ignores ids in its `answered`.

### Answers

`answer-<id>.json`, written by the laptop only:

```json
{"id": "<request id>", "status": 200, "type": "application/json", "body": {…}, "done": true}
```

- A streamed reply (`POST /ai/ask`, `POST /chat`: one JSON object a line) is written as it comes:
  `{"id", "status": 200, "lines": ["{…}", "{…}"], "done": false}`, rewritten with the lines so far at most once a
  second and the moment the stream ends (`done: true`). The phone reads it by id every 0.5 s while waiting and hands
  each new line on, so the Ask screen works unchanged.
- An error is the library's own: `{"status": 404, "body": {"detail": "…"}, "done": true}`.
- A binary body (`GET /attachments/<id>/raw`) goes into a `blob-` file; the answer says
  `{"status": 200, "type": "application/pdf", "blob": "<drive id>", "done": true}`. The phone keeps it.
- A request the laptop won't do (not in the list below): `{"status": 403, "body": {"detail": "Your laptop doesn't
  take that from the phone."}, "done": true}`.

The laptop deletes `upload-` files once used, and anything of its own (answers, blobs) older than a day. The phone
deletes answer files once read. Either side may delete stray files of its own kind older than a day.

### What the laptop does for the phone (and nothing else)

Exactly the iPhone app's own calls (`ios/StudyStash/API/APIClient.swift`), replayed against the local library:

- GET: `/me`, `/library`, `/lectures`, `/lectures/{id}`, `/lectures/{id}/rendered`, `/search`, `/chats`,
  `/chats/{id}`, `/canvas/due`, `/canvas/assignment`, `/calendar/upcoming`, `/attachments`, `/attachments/{id}/raw`,
  `/voice-memos`
- POST: `/lectures` (a recording written down on the phone; idempotent by its `phone-…` id), `/lectures/{id}/class`,
  `/lectures/{id}/title`, `/lectures/{id}/restore`, `/lectures/{id}/rewrite`, `/ai/ask` (streamed), `/ai/warm`,
  `/chat` (streamed), `/classes/alias`, `/uses`, `/attachments` (multipart from `upload`), `/voice-memos` (multipart
  from `upload`), `/voice-memos/{id}/retry`
- DELETE: `/lectures/{id}`, `/chats/{id}`, `/attachments/{id}`, `/voice-memos/{id}`

Never settings, passwords, AI engines or keys, Canvas tokens, devices, Claude connections, history, files, or
anything outside `/api/v2`. Each request goes to the library as that phone (the phone's name where the library
records who did something: `by` on a voice memo, the phone's lecture).

`/devices/*` and pairing don't exist over Drive. `GET /me` over Drive is answered on the phone itself:
`{"paired": true, "library": {"name": <laptop.json's laptop name>, "version": …}}`.

### The phone side

- A `URLProtocol` for a host of its own (`drive.studystash.app`), the same way `DemoLibrary` answers
  `demo.studystash.app`: `APIClient` and every screen stay as they are. GETs in the copy are answered from the phone
  at once; anything else becomes a request.
- Giving up: a GET or an Ask/chat with no answer after 25 s is taken out of the box and fails as unreachable, with
  "Your laptop isn't answering. Is it on and awake?" (Ask while the laptop sleeps isn't kept for later: Joey hasn't
  decided, so keep it simple). A write (a recording, a rename, a file, a voice memo) **stays in the box** until the
  laptop does it; the screen gets "unreachable" after 25 s as today, so a recording stays "waiting"; sending the
  same request again (same method, path and body) reuses the one already waiting instead of adding a second.
- The phone shows the laptop's name and when it was last there (`seen`), and that a recording is on its way.

## Sign-in screens, in words

Plain, second person, no jargon, the desktop's words. "Google Drive" is fine to say; "OAuth", "token", "appDataFolder"
are not. Laptop: "Connect your phone through your Google account. Your notes go into a private Study Stash folder in
your Google Drive that only Study Stash can see." Phone: "Continue with Google. Use the same Google account as on your
laptop." If Google refuses (a school account that blocks outside apps), say so: "Your school's Google account doesn't
allow Study Stash. Try a personal Gmail on both."

## The iPhone side, as built (`ios/StudyStash/Drive/`)

- `GoogleSignIn.swift`: Continue with Google through `ASWebAuthenticationSession` (SwiftUI's `webAuthenticationSession`),
  PKCE S256, `login_hint` when the QR code named an account (else `prompt=select_account`), no secret, no Google SDK.
  The client is the build setting `GOOGLE_IOS_CLIENT_ID` (Info.plist `GoogleClientID`) and its reversed form
  `GOOGLE_IOS_URL_SCHEME` (a URL type); both are placeholders (`…-replace-me`) until the iOS client exists, and while
  they are, Continue with Google says "Continue with Google isn't set up in this copy of Study Stash yet." The account
  (email from the id_token, the refresh token) is one Keychain item, after-first-unlock, this device only; the widgets
  don't need it. A reply without the `drive.appdata` scope (the Drive box unticked on Google's page) is refused in
  words; `admin_policy_enforced` / `org_internal` get the school words. Sign out (Settings) deletes the phone's box,
  uploads and answer files from the folder, revokes the token and forgets the copy; recordings not sent stay.
- `DriveFiles.swift`: the folder through Drive's REST v3. Files over 4 MB go up resumable (one PUT).
- `DriveLink.swift` (an actor): the copy, the box, requests and answers. `DriveLibrary.swift`: the `URLProtocol` for
  `drive.studystash.app`, on the app's session next to `DemoLibrary`, so `APIClient` and every screen are unchanged.
  An attachment opens through that session (`ClassView` downloads `/attachments/<id>/raw`, then Quick Look shows the
  file on the phone), so it goes the same way: a request, the laptop's `blob-` file, kept on the phone after.
- The copy is kept in the app group container (`drive/`, `drive-<prefix>/` for a test space): `laptop.json` as last
  read, each copied body under the same 16-hex name, an index of their shas. A read whose key the laptop lists but the
  phone hasn't fetched yet (the first copy still coming, or changed since) is fetched by id on the spot, one trip, not
  asked of the laptop. Keys are matched decoded (the laptop escapes ids in paths; the phone sends them escaped too).
- The library's own rules for what the phone filters itself (`LibraryReader.Lectures`, `Store.ListAttachments`):
  - `GET /lectures`: from the one key `GET /lectures?limit=…`; `class` is an exact match on each lecture's `class`
    ("Unsorted" too); `before` keeps lectures whose `date` sorts before it as text (ordinal); `limit` is 50 when not
    given, held to 1…500; the list's order (newest first, then title) is kept.
  - `GET /attachments`: from `GET /attachments`; with `lecture`, the ones whose `lecture` is it (`class` then ignored);
    else with `class`, the ones whose `class` is it; else all; the list's order kept. (The library also says
    `rewrite` for one lecture's list; the phone doesn't read it, so the filtered answer leaves it out.)
  - `GET /me` is the phone's own: `{"paired": <laptop.json is in the folder>, "library": {"name", "version"}}`.
- Requests: the answer file first, then the `upload-` file, then the box (one writer at a time). The request is kept
  on the phone (with an upload's bytes) from the moment it's made, so a write survives the app closing and is sent
  when it can be. A multipart upload with several files becomes one request a file (the box carries one file a
  request); their answers' `attachments` are put together. `POST /ai/warm` also makes the spare answer file; the next
  Ask or chat uses it (within an hour), so asking is one write.
- Giving up, as above, plus: with no internet the screen hears "This phone isn't online…" at once (a write is kept);
  with no laptop in the folder a read hears "Your laptop isn't connected to Google yet…" at once; a streamed answer
  that stops changing for 2 minutes is given up. A write the laptop does after its screen stopped waiting is kept as
  done (the last 50, by method, path and body): sending the same again answers from it instead of asking twice (a
  recording's Send again, RecordingStore's resend). A write already waiting that the laptop hasn't done is joined and
  given up on after one look, not another 25 s.
- While the app is in front: the copy every 10 s, the box every 60 s (`open_until` 90 s ahead), writes no screen waits
  for looked at every 3 s. To the back: `open_until` now. The system's app refresh (`app.studystash.ios.refresh`, the
  same task as the reminders) reads the copy, sends what's waiting and looks at it. Once a day the phone deletes
  `upload-` files older than a day that no request of its own points at.
- Setup: Welcome is Continue with Google (ink), "Use the same Google account as on your laptop.", Scan the code on
  your laptop, and Other ways (Connect over Tailscale; Try it with sample classes). The scanner (and the Camera app,
  through the `studystash://` scheme) takes `studystash://google?account=<email>` as well as a library's address.
  Signed in with no laptop in the folder: "Now connect your laptop", which looks every 3 s and goes on by itself to
  What it's for / You're in. Settings shows the phone, Google Drive and the laptop, the account, "Your laptop was last
  here 3 hours ago." when `seen` is over 3 minutes old, what's on its way, and Sign out of Google.
- The laptop lane's changes (below), on the phone: (1) the filters are exactly those rules (`class`/`lecture` trimmed);
  (2) a phone that finds its id in `removed` stops, signs out of Google (which forgets its id, so the next sign-in is a
  new box) and Welcome says "Your laptop removed this phone. Sign in again to use it."; (3) a copy that hashes ahead
  of laptop.json is kept, and fetched again only until laptop.json agrees; (4) the QR link as given, `space` taken by
  debug builds only; (5) a body that's a JSON string with a non-JSON `type` is handed on as its text, an Ask/chat
  answered with `body` (no `lines`) is a plain answer; (6) `phone.name` is `UIDevice.current.name`, which iOS gives as
  "iPhone"/"iPad" (no entitlement for the owner's name); (7) a request in a new box's first minute can give up before
  the laptop finds the box (a write stays waiting); (8) a waiting write whose answer file is gone (the laptop
  disconnected) gets a new answer file and is sent again; (9) the box keeps requests in the order asked, and a
  multi-file upload puts all its files in the box at once, in order; one not answered leaves the rest waiting.
- Debug builds only: `-drive-token-file <path>` (JSON with `client_id`, `client_secret`, `token_uri`,
  `refresh_token`; refreshed with those, never put in the Keychain or revoked) and `-drive-space <prefix>`; a test
  laptop's QR code's `&space=<prefix>` does the same as `-drive-space`.

## Changes asked for

### From the laptop lane (phone-google), 2026-10-24

What the laptop does is in `docs/phone-google.md` on branch `phone-google` (its copy of this contract, kept true to
what it built). What the phone needs to know:

1. **The lectures' copy key is exactly `GET /lectures?limit=1000`**, and its body holds *every* lecture, however many
   (the library gives at most 500 a page; the laptop joins the pages). The phone filters it the way the library does
   (`LibraryReader.Lectures`, `Store.ListNotes`): the list is already in the library's order (`date` newest first,
   then `title`); `class` keeps lectures whose `class` is exactly it (case and all; lectures in no class have
   `"Unsorted"`), none keeps all; `before` keeps those whose `date` *string* sorts before it (plain ordinal string
   comparison, not as dates); then the first `limit` (50 when not given, at least 1, at most 500).
   `GET /attachments?class=&lecture=` from the `GET /attachments` copy (`LibraryWeb.AttachmentList`): values trimmed,
   empty ones ignored; with `lecture`, those whose `lecture` is it (`class` is then ignored); else with `class`, those
   whose `class` is it; else all; order as listed. (The library adds `"rewrite": bool` beside one lecture's
   attachments; the copy can't say it, so leave it out: the iPhone app doesn't read it.) Lecture and chat ids in keys
   are percent-encoded as `APIClient.segment` does: `GET /lectures/<id>`, `GET /lectures/<id>/rendered`,
   `GET /chats/<id>`. `GET /chats` is copied without a query.
2. **laptop.json gains `"removed": ["<phoneId>", …]`** (Settings → Remove on the laptop). The laptop stops answering
   that phone and deletes its box (and the uploads its waiting requests named). A phone that finds its own id there
   should stop sending, say "Your laptop removed this phone. Sign in again to use it.", and use a new phone id (a new
   box) at its next sign-in: with the same id it's never answered again.
3. **A copy file can be a moment ahead of laptop.json.** The laptop rewrites a `copy-` file in place (same id), then
   laptop.json about a second later. If the body the phone downloaded doesn't hash to laptop.json's sha, keep it
   anyway (it's newer) and expect the next laptop.json to agree; don't discard it or loop.
4. **The QR link**: `studystash://google?account=<the email, percent-encoded as a URL query value>`, e.g.
   `studystash://google?account=joey.russell%40gmail.com`. A test run (STUDYSTASH_DRIVE_SPACE set) adds
   `&space=<prefix>`: a debug build may take it as `-drive-space`; a release build ignores it.
5. **Answers, in detail**: every answer has `type` (the library's content type). A body that isn't JSON comes as a
   JSON string (success) or `{"detail": "<text>"}` (error); an empty one as `null`. An Ask/chat the library answers
   without streaming (an error, a busy chat's 409) is a plain answer with `body`, not `lines`. A streamed one's first
   write goes the moment the first line comes. An upload that isn't there: `400 {"detail": "The file didn't arrive.
   Send it again."}`; `upload.fields` values must be strings. Request ids must match `[A-Za-z0-9_-]{1,100}` (a UUID)
   or the request is skipped; always give `answer` (without it the laptop makes `answer-<id>.json` itself, which the
   phone can't watch by id). A request the laptop can't do yet (its library is starting) isn't answered; it's tried
   again 5 s later, so the give-up rules apply. Blobs over 5 MB go through Drive's resumable upload, both ways.
6. **The phone's name, as the library records it**: `POST /lectures` gets `owner` = the box's `phone.name` unless the
   phone sent one; a voice memo's `by` is that name in plain ASCII ("Joey’s iPhone" becomes "Joey's iPhone": it
   travels in a header).
7. **New boxes**: the laptop looks every 10 s for its first 15 minutes after it starts (connecting, with the QR code
   on screen), then every 60 s. A phone signed in later may wait up to a minute for its first answer; its reads come
   from the copy at once.
8. **Disconnect** also deletes the answer files (they hold the library's words), as well as laptop.json, copies and
   blobs. Boxes and uploads stay: the phone's.
9. **A phone's changes are done one at a time, in the order they are in its box** (every request but a GET,
   `/ai/warm`, and a streamed Ask/chat, which don't wait), as they are over Tailscale, where the app awaits each:
   a recording, then a notebook page attached to its lecture, works only in that order (the library says 404 for an
   attachment to a lecture it hasn't got yet). So the phone keeps its box's requests in the order it asked them,
   and a write that stays waiting holds the writes behind it until it's done.
