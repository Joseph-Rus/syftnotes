# The phone over Google Drive

Written 2026-10-24 as the contract for two lanes built side by side: `phone-google` (the laptop, C#) and
`phone-google-ios` (the iPhone app, Swift). This copy is the laptop lane's, kept true to what it built; the changes it
asked of the shared contract are at the bottom.

## What it's for

A student with **just this computer** (`AppRole.Both`: the app runs its own library on 127.0.0.1) connects their
phone through their own Google Drive instead of Tailscale. No server of ours, no VPN.

- Laptop: Settings → Phone → **Connect with Google** → the browser opens Google → they pick an account → Allow. The
  page then shows the account, a QR code for the phone, and **Disconnect…**, and below it the phones that use the
  folder, each with **Remove**.
- Phone: **Continue with Google** (or scan the laptop's QR code, which opens the app and pre-picks the same account)
  → Google's sheet → Allow → the library shows.
- The phone shows what the laptop put in the folder, and everything it makes or changes (a recording written down on
  the phone, typed notes, notebook pages, files, voice memos, renames, Ask) goes back through the folder to the
  laptop's library, which does it exactly as if the phone had asked over Tailscale.

**Not changed:** a laptop whose library is on another computer, and a library-only computer, keep Tailscale exactly as
today (Settings → Phone → Add a phone, `/api/v2/devices/*`, Tailscale Serve). In just-this-computer mode, phones paired
over Tailscale before keep working and stay listed (under "Phones added over Tailscale"). Connect with Google comes
first; **Add a phone** over Tailscale is still there, folded under a quiet **Other ways** beneath it ("For an Android
phone or the phone's web page: reach this computer over Tailscale."), closed until it's pressed. Open, it's the
Tailscale way exactly as before: Add a phone, the 6-digit code and its QR code, and the library's words with **Open
the page** when Tailscale is missing. Folding it away again puts a code on show away.

```
phone (iPhone app)                     Google Drive, appDataFolder                laptop (Study Stash, just this computer)
  phone-<id>.json  (its box) ───────▶  read by id, every 1 s while open  ───────▶  DriveLink: PhoneCalls checks it,
  answer-<req>.json ({} first)                                                    LibraryCalls replays it on
  upload-<req>     (a file it sends)                                               http://127.0.0.1:<port>/api/v2
                                     ◀─────── answer-<req>.json written  ◀───────  with the library's password
  reads laptop.json every ~10 s      ◀─────── laptop.json, copy-*, blob-*  ◀──────  the copy, every ~15 s and after a change
```

## The code

- `engine/src/StudyStash.Core/PhoneDrive/` — `Google.cs` (the Desktop client, the secret, sign-in, refresh, revoke,
  google.json, the student's words for each trouble), `Drive.cs` (`IDrive`, the Drive REST API over plain
  `HttpClient`: retries and the token), `PhoneCalls.cs` (the only calls a phone may make), `LibraryCalls.cs` (this
  computer's library over `/api/v2`), `DriveLink*.cs` (the copy, the boxes, the answers, laptop.json).
- `engine/src/StudyStash.App/Services/PhoneDrive.cs` — Connect, Disconnect, Remove, the link's start and stop
  (`AppHost.Drive`, started by `AppHost.Start` when the role is Both and google.json is there; stopped on quit).
- `engine/src/StudyStash.App/ViewModels/PhoneGoogleModel.cs`, `Views/MacPhones.axaml`, `Views/WinPhones.axaml` —
  Settings → Phone (`PhonesModel.Google` is set only when the role is Both).

## Measured (2026-10-24, real Drive)

Before building: one write takes ~0.9 s; another reader sees it ~1.7 s after the write starts, even reading non-stop.
Watching a file **by its id** is much faster than Drive's change list (`changes.list` lagged 2-4 s more). So: every
file both sides watch is found once by name, then read by id. A question costs two trips, about 4 s before Claude's
first words; answer pieces arrive ~1.6 s behind. Do nothing that adds a trip on Ask's path.

The laptop as built (`PhoneDriveRealCheck`, real Drive, the contract's own timings, a throwaway library with 12
lectures and a scripted Claude writing its answer in six pieces 0.4 s apart; the phone's part played from the test,
reading by id every 0.5 s):

| What | Time |
|---|---|
| laptop.json found by the phone after the link starts | 1.8-2.0 s |
| the copy's `GET /library` read by the phone | 4.1-4.4 s |
| the first copy complete (12 lectures, 31 files, four at a time) | 7.6-8.3 s (29.4 s one at a time) |
| Ask: the box written (one write) | 0.8-1.0 s |
| Ask: the first line on the phone, from the moment it starts writing its box | 3.1-4.2 s in 11 of 12 asks (four runs); one 5.9 s |
| Ask: done on the phone | 5.0-7.6 s |
| a rename: answered / in the copy with laptop.json agreeing | 2.7-3.0 s / 7.0-8.2 s |
| a 6 MB file phone → laptop (uploaded, in the library) / laptop → phone (a blob, read back the same) | 5.6-7.5 s / 6.2-7.7 s |

## Google

- Project `study-stash-app` (number 1073354844610). Drive API is on. Consent screen "Study Stash", External, in
  Testing (one test user, an account of our own). In Testing, Google lets a refresh token live 7 days: the laptop then
  says "Google signed Study Stash out of your account…" with **Connect again**.
- Scopes, both sides: `openid email https://www.googleapis.com/auth/drive.appdata`. Nothing else. A student who
  unticks Google Drive on Google's page is told to connect again and leave it ticked.
- Everything lives in Drive's hidden **appDataFolder** (`parents: ["appDataFolder"]`, `spaces=appDataFolder`). It's
  per Google project: the laptop's and the phone's sign-in IDs are in the same project, so they see the same folder.
- **Laptop** sign-in ID: the Desktop client `1073354844610-rlceur96jtct2rnhb5616c14n3fnmf6e.apps.googleusercontent.com`.
  Loopback redirect (`http://127.0.0.1:<any port>`, a bare socket), PKCE S256, `access_type=offline`,
  `prompt=select_account consent` (every Connect is a first time: Disconnect forgets). Its client secret is **never
  committed** (the repo is public; GitHub's secret scanning flags `GOCSPX-`): `StudyStash.Core.csproj` builds it in
  from the environment variable `GOOGLE_DESKTOP_CLIENT_SECRET` or the git-ignored file
  `engine/google-desktop-client-secret.txt` (as assembly metadata), and the same variable at run time overrides it. CI
  passes the repository secret `GOOGLE_DESKTOP_CLIENT_SECRET` to the Mac and Windows installer builds
  (`.github/workflows/ci.yml`). A build without it shows Connect with Google greyed out with "This copy of Study Stash
  can't connect to Google: it was built without Google sign-in. The download from studystash.app can.", and every
  test that doesn't need real Google passes. (A Desktop client's secret is not a real secret to Google — it ships
  inside every copy of the app — but it stays out of the repo.)
- **Phone** sign-in ID: an iOS client for bundle `app.studystash.ios` (Joey is making it; until then a build setting
  `GOOGLE_IOS_CLIENT_ID` with a placeholder). Redirect `com.googleusercontent.apps.<id>:/oauth2redirect` through
  `ASWebAuthenticationSession`, PKCE, no secret, no Google SDK. Tokens in the Keychain.
- Tokens: the laptop keeps the refresh token in `<home>/google.json` (`{email, refresh_token, connected}`),
  owner-only (made empty and owner-only before anything is written in it, then moved into place). The account's email
  comes from the id_token. Disconnect deletes the folder's files this laptop wrote (laptop.json, copies, blobs,
  answers), revokes (`https://oauth2.googleapis.com/revoke`), and forgets google.json; if Google can't be reached it
  still forgets it. Nothing is logged that holds a token, the secret, a request's body or a note's text: the log says
  `[drive] POST /lectures/{}/title from Joey's iPhone: 200`.
- What Google says no to, in the student's words (`GoogleException.Words`): Cancel or an account turned away ("Google
  didn't connect. If you pressed Cancel…"), a school account (`admin_policy_enforced`/`org_internal`: "Your school's
  Google account doesn't allow Study Stash. Try a personal Gmail on both."), no answer in five minutes (Google shows
  its own error page for a blocked account and never sends the browser back), offline.

## The folder

Every name below can carry a prefix for tests (`STUDYSTASH_DRIVE_SPACE` on the laptop, launch argument
`-drive-space <prefix>` in a debug iPhone build): `<prefix>laptop.json` and so on. Real use has no prefix. Test runs
always use a prefix of their own and delete their files when done. Listing by a kind's prefix (`name contains
'<prefix>phone-'`) finds exactly the files whose names start with it (checked on real Drive).

Each file has **one writer**. Nobody ever writes a file the other side writes. (Two deletions cross over, both on
the student's say-so: Remove deletes that phone's box and its waiting uploads, and Disconnect deletes the answer
files, which hold the library's words.)

| File | Writer | What |
|---|---|---|
| `laptop.json` | laptop | who the laptop is, the copy's index, which requests it has answered, the phones it no longer answers |
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
  "answered": ["<request id>", "…"],
  "removed": ["<phone id>", "…"]
}
```

- `laptop.id` is made once per install (`<home>/laptop-id`) and kept through Disconnect; `name` is the computer's
  own (a Mac's Sharing name, "Joey's MacBook Air"; a PC's name).
- `seen` is rewritten at least every 60 s while the laptop runs (every 45 s); the phone says "Your laptop was last
  here 3 hours ago" from it.
- `answered`: the last 500 request ids it has finished, so a request is never done twice, across restarts too.
- `removed`: phones Settings → Remove took off. The laptop doesn't answer their boxes (see "Changes asked for").
- laptop.json is written at most once a second, a moment after anything in it changes.

### The copy (laptop → phone)

The laptop asks its own library (`GET http://127.0.0.1:<port>/api/v2/…` with the library's password, as
`RemoteLibrary` does) and keeps these in the folder, each as one `copy-` file, the body exactly as the library gave it:

| Key | What |
|---|---|
| `GET /library` | |
| `GET /lectures?limit=1000` | **every** lecture, newest first, however many (the library gives at most 500 a page: the laptop asks page by page and joins them) |
| `GET /lectures/<id>`, `GET /lectures/<id>/rendered` | for every lecture in the list; `<id>` percent-encoded as the iPhone's `APIClient.segment` does (everything but letters, digits and `-._~`) |
| `GET /chats`, `GET /chats/<id>` | every chat (the list without a query, as the phone asks it) |
| `GET /canvas/due`, `GET /canvas/state`, `GET /calendar/upcoming`, `GET /ai` | |
| `GET /attachments` | every attached file (no query) |
| `GET /voice-memos` | |

Key: the method, a space, the path after `/api/v2` and its query, keys sorted. The `copy-` file name is the first 16
hex characters of the key's sha256, then `.json` (`copy-1f0e….json`).

**The phone answers these GETs itself from the copy**, by the library's own rules:

- `GET /lectures?class=&limit=&before=` from `GET /lectures?limit=1000` (`LibraryReader.Lectures`,
  `Store.ListNotes`): the list is already in the library's order (`date` newest first, then `title`). `class` keeps
  the lectures whose `class` is exactly it (case and all; lectures in no class have `"Unsorted"`); none keeps all.
  `before` keeps those whose `date` string sorts before it (plain ordinal comparison of the strings, not as dates).
  Then the first `limit` (50 when not given; at least 1, at most 500).
- `GET /attachments?class=&lecture=` from `GET /attachments` (`LibraryWeb.AttachmentList`, `Store.ListAttachments`):
  each value trimmed, empty ones ignored. With `lecture`, the attachments whose `lecture` is it (`class` is then
  ignored); else with `class`, those whose `class` is it; else all. The order stays as listed (when added). The
  library also answers `"rewrite": true|false` beside a lecture's attachments (its notes were written without some of
  them); the copy can't say that, so the phone leaves it out (the iPhone app doesn't read it).
- `GET /lectures/<id>` and `/rendered`, `GET /chats/<id>`: the file under that key; no key means it isn't in the copy
  (yet) and the phone asks for it as a request.

How the laptop keeps it: every ~15 s, and 0.3 s after it answers any request that changes something (that lecture
looked at first), it asks the library for the lists; for each lecture that's new, whose list entry changed, that a
request was about, or whose turn it is (eight a round, in turn, so a note edited by hand on the laptop arrives within
a few minutes even when its list entry didn't change), it asks for the lecture and its rendered notes; chats the same
way. Only answers whose sha changed are uploaded (an unchanged lecture is never uploaded again, a restart included);
a lecture or chat that's gone has its files deleted and its keys removed. The first copy of a big library goes in the
background, the lists first, then the lectures newest first, four at a time ("Putting your notes in your Google Drive
for your phone: 40 of 140 lectures…" under the account in Settings). Copies left in the folder that nothing is any
more (another install's) are deleted after the first copy.

A copy file is rewritten in place and laptop.json about a second after: for that second, a file can be newer than the
sha laptop.json gives for it (see "Changes asked for").

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

- A request `id` is letters, digits, `-` and `_`, at most 100 (a UUID); one that isn't is skipped.
- `open_until`: the phone sets it 90 s ahead when the app comes to the front and every 60 s while it stays there,
  and to now when it goes to the back. While any box is open, the laptop reads open boxes **every second, by id**;
  otherwise every 15 s. It looks for new boxes (`name contains 'phone-'`) when it starts, every 10 s for its first 15
  minutes (the student is signing the phone in with the QR code on screen), then every 60 s.
- Asking: create `answer-<id>.json` (`{}`), then add the request to the box, in that order. For Ask and chat, the
  phone creates a spare answer file as soon as the student starts typing (and sends `POST /ai/warm` then, as today),
  so sending the question is one write.
- The phone takes a request out of its box when its answer is `done`, or when it gives up on it (below). The laptop
  ignores ids in its `answered`. Seeing a box for the first time after it starts, the laptop reads the answer of each
  request in it before doing it (one read, only then): one answered just before a restart isn't done again.
- A phone's changes (every request but a GET, `/ai/warm`, and a streamed Ask/chat) are done one at a time, in the
  order they are in its box, as they are over Tailscale where the app awaits each: a recording, then a notebook page
  attached to its lecture, only works in that order. Reads and Ask don't wait for them. Once a change is done, its
  box is read again straight away for the next.
- `upload.fields`: string values only (others are left out); the file goes as the part `file` named `upload.name`
  with type `upload.type`, after the fields, the multipart form the iPhone app makes. The laptop deletes the upload
  once the library has answered (whatever it said); a request with no upload, or whose upload isn't there, is
  answered `400 {"detail": "The file didn't arrive. Send it again."}`.

### Answers

`answer-<id>.json`, written by the laptop only:

```json
{"id": "<request id>", "status": 200, "type": "application/json", "body": {…}, "done": true}
```

- `type` is the library's content type. `body` is its JSON; a body that isn't JSON is a JSON string on a success and
  `{"detail": "<text>"}` on an error; an empty one is `null`.
- A streamed reply (`POST /ai/ask`, `POST /chat` that the library streams: `application/x-ndjson`, one JSON object a
  line) is written as it comes: `{"id", "status": 200, "type": "application/x-ndjson", "lines": ["{…}", "{…}"],
  "done": false}`, rewritten with the lines so far at most once a second (the first the moment the first line comes)
  and the moment the stream ends (`done: true`). The phone reads it by id every 0.5 s while waiting and hands each new
  line on, so the Ask screen works unchanged. An Ask or chat the library answers without streaming (an error, a busy
  chat's 409) is a plain answer with `body`.
- An error is the library's own: `{"status": 404, "type": "application/json", "body": {"detail": "…"}, "done": true}`.
- A binary body (`GET /attachments/<id>/raw`) goes into a `blob-` file (named by its bytes' sha256, reused for the
  same bytes within 12 hours); the answer says `{"status": 200, "type": "application/pdf", "blob": "<drive id>",
  "done": true}`. The phone keeps it. Files over 5 MB go up in Drive's resumable upload, both ways.
- A request the laptop won't do (not in the list below): `{"status": 403, "body": {"detail": "Your laptop doesn't
  take that from the phone."}, "done": true}`.
- A request the laptop can't do yet (its library is starting or stopped) isn't answered: it's tried again five seconds
  later, and so the phone's give-up rules apply.

The laptop deletes `upload-` files once used, and anything of its own (answers, blobs) older than a day, except the
answer file of a request still waiting in a box. The phone deletes answer files once read. Either side may delete
stray files of its own kind older than a day.

### What the laptop does for the phone (and nothing else)

Exactly the iPhone app's own calls (`ios/StudyStash/API/APIClient.swift`, and the two the Due tab makes itself), replayed against the local library
(`PhoneCalls.cs`):

- GET: `/me`, `/library`, `/ai` (which AI answers each job, no keys: the phone shows Ask's AI while it thinks), `/lectures`, `/lectures/{id}`, `/lectures/{id}/rendered`, `/search`, `/chats`,
  `/chats/{id}`, `/canvas/due`, `/canvas/state`, `/canvas/assignment`, `/calendar/upcoming`, `/attachments`, `/attachments/{id}/raw`,
  `/voice-memos`
- POST: `/lectures` (a recording written down on the phone; idempotent by its `phone-…` id), `/lectures/{id}/class`,
  `/lectures/{id}/title`, `/lectures/{id}/restore`, `/lectures/{id}/rewrite`, `/ai/ask` (streamed), `/ai/warm`,
  `/chat` (streamed), `/chats/{id}` (rename), `/chats/{id}/restore` (Delete's undo), `/classes/alias`, `/uses`, `/attachments` (multipart from `upload`), `/voice-memos` (multipart
  from `upload`), `/voice-memos/{id}/retry`, `/canvas` (the Due tab's sync now: whatever the phone sends, the
  library is sent only `{"sync": true}`, so a phone can't change Canvas's address or which course is which class)
- DELETE: `/lectures/{id}`, `/chats/{id}`, `/attachments/{id}`, `/voice-memos/{id}`

**Stop.** The phone's Stop takes a streamed request (Ask, chat) out of its box. While the laptop is writing that
answer, it checks the box on each read: once the request has been missing from two reads running (one stale read of
Drive isn't enough), it drops its request to the library, which stops the AI as the app's own Stop does, and it
writes nothing more to the answer. A request the phone gave up on (25 s, no answer) stops the same way.

Never settings, passwords, AI engines or keys, Canvas tokens, devices, Claude connections, history, files, or
anything outside `/api/v2`. A path is checked segment by segment after decoding: an empty segment, `.` or `..`, a
slash, backslash or control character inside one, a fragment, or anything but a URL query's own characters in the
query is refused; each segment is encoded again before the library hears it, so nothing can climb out of `/api/v2`.
The query is passed on as the phone wrote it.

Each request goes to the library as that phone: `POST /lectures` gets `owner` = the phone's name (from its box) unless
the phone sent one, so the lecture is the phone's; every request carries the phone's name in
`X-Study-Stash-Computer`, which a voice memo takes as its `by`. A header carries only ASCII, so there "Joey’s iPhone"
is "Joey's iPhone" and accents are dropped.

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
  same request again (same method, path and body) reuses the one already waiting instead of adding a second. (The
  laptop's side holds either way: a recording sent twice is one lecture, as the library keeps a lecture by its id.)
- The phone shows the laptop's name and when it was last there (`seen`), and that a recording is on its way.

## The QR code

Settings → Phone shows, once connected, a QR code of

```
studystash://google?account=<the account's email, percent-encoded as a URL query value>
```

for example `studystash://google?account=sam%40example.com`. A test run (STUDYSTASH_DRIVE_SPACE set) adds
`&space=<prefix>`, which a debug phone build may take as its `-drive-space`; a release build ignores it. Always black
on white, in light and dark (checked by decoding it with macOS's CoreImage detector from the Settings pictures).

## Sign-in screens, in words

Plain, second person, no jargon, the desktop's words. "Google Drive" is fine to say; "OAuth", "token", "appDataFolder"
are not. Laptop: "Connect your phone through your Google account" / "Your notes go into a private Study Stash folder
in your Google Drive that only Study Stash can see." Once connected: "Connected as sam@example.com", "1. On your
iPhone, open Study Stash and tap Continue with Google, or scan this code with the camera.", "2. Use the same Google
account: sam@example.com.", "Your phone keeps a copy of your notes, so it can read them while this Mac is off.
Recordings, files and questions from your phone reach this Mac when it's on." Disconnect asks first: "Your phone
stops getting your notes, and Study Stash empties its folder in your Google Drive." Each phone: "Open now" or "Last
open 3 hours ago". Phone: "Continue with Google. Use the same Google account as on your laptop." If Google refuses (a
school account that blocks outside apps), say so: "Your school's Google account doesn't allow Study Stash. Try a
personal Gmail on both."

## Testing

- `PhoneDriveTests` (a real library, `LibraryRig`; a Drive folder in memory, `FakeDrive`; the phone played as the
  iPhone app plays it, `FakePhone`): a phone can do only the iPhone app's own calls (settings, passwords, devices, AI
  keys, `..` in all its spellings, a fragment, another host: each 403, and the library's settings untouched); a
  request is done once however often its box is read, twice in the same box, and after a restart; a recording sent
  twice is one lecture, the phone's; a phone's changes are done in the order it asked (a recording, then a page
  attached to it, in one write of its box); Connect (the browser coming back to the loopback address with a code, the
  account kept owner-only with its email, a school account and an unticked Google Drive each said in words); a changed
  lecture reaches the copy (laptop.json agreeing with the file) and the
  others are never uploaded again, a restart uploads nothing unchanged, a deleted lecture leaves the folder; a streamed
  Ask is written in growing pieces at most once a second and ends `done`; Disconnect revokes, deletes the laptop's
  files and google.json, and nothing is answered or written after.
- `PhonesViewTests`: Settings → Phone through Google in both looks and both themes (the QR card black on white, the
  account, Disconnect asking first, the phones with Remove, the Tailscale phones below, Add a phone folded away),
  before connecting (Connect with Google, or greyed out with the words when the build has no secret), and Other ways
  opening Add a phone over Tailscale in both looks (the code and its QR code, black on white; folded away again, the
  code goes).
- `PhonesShots`: `shots/mac-settings-phone-google-{not-connected,connected,phones,other-ways}-{light,dark}.png` and
  the `win-` ones (other-ways: Other ways open with a code on show for an Android phone), kept (cropped to the window)
  in `docs/qa/2026-10-24/`. Their QR codes were decoded with macOS's CoreImage
  detector.
- `PhoneDriveRealCheck`: the table above, against real Drive; skipped unless `STUDYSTASH_DRIVE_CHECK_TOKEN` names a
  test account's token file (`client_id`, `client_secret`, `refresh_token`; never printed). It deletes every file with
  its prefix after.

## Changes asked for

### From the laptop lane (phone-google), 2026-10-24

Written into the shared contract's "Changes asked for" for the iPhone lane. Most are said above where they belong; in
short:

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
   `studystash://google?account=sam%40example.com`. A test run (STUDYSTASH_DRIVE_SPACE set) adds
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

## Whiteboard photos (laptop and library side)

- The laptop's app tells its own library what it records: `POST /api/v2/recording-now` `{id, class, title, started, stopped}` at start, every 30 s and at stop.
  The library keeps it in memory for 90 s after the last report (`RecordingNow`) and answers `GET /api/v2/library` with
  `"recording_now": {"id","class","title","started"}` or `null`. The app also makes the phone link's copy of `/library` look again at once (`DriveLink.CopyNow`).
- `POST /api/v2/attachments` takes `lecture`, `taken` (ISO 8601 with offset) and `kind` (`whiteboard`). `lecture` may be a lecture the library has, or
  the id of the current or a recent (24 h) recording-now: the photo waits under that id, in the recording's class (else Unsorted), and is that lecture's once it arrives.
- An attachment's JSON gains `taken`, `at` (seconds from the lecture's start, worked out by the library once the lecture is there, else `null`) and `kind`.
- Notes writing gives the photos to the AI as images (Claude Code: files copied into its working folder and named in the prompt; Codex: `--image`);
  Gemini and Ollama get each photo's OCR text. A note may embed `![Whiteboard at 12:34](attachment:<id>)`; every renderer shows only that lecture's own pictures.
