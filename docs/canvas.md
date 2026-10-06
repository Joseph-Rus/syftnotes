# Study Stash: Canvas

Study Stash mirrors each class's Canvas course into that class's folder in the library, so the student, the app's
Canvas screens and Claude can all read their coursework without opening Canvas. Canvas is read through a small
Chrome extension with the student's own sign-in (many schools turn off Canvas access tokens). Nothing is ever written
to Canvas.

```
Chrome extension (student's Canvas session)            library (StudyStash serve)
  waits for work: GET /api/v2/canvas/work   ──────▶     CanvasSync.WorkAsync: start a sync when due, hand out jobs
  (the library holds it up to 20 s)                     (answers the moment there is some)
  fetch each job's Canvas URL, with cookies             (AI reads first, then the sync's queue)
  POST /api/v2/canvas/results               ──────▶     Crawl.Handle: file each answer, queue what follows
                                                        CanvasSync.Finish: assignments list, changes, errors
```

The code: `engine/src/StudyStash.Core/Canvas/` (`Crawl.cs` the queue and the mirror, `CanvasSync.cs` the
orchestration and AI reads, `Assignments.cs` the flat list, `CanvasSettings.cs` `home/canvas.json`,
`Extension.cs` the extension folder, `HtmlText.cs` HTML to Markdown, `Scout.cs` the course scout),
`engine/src/StudyStash.Library/LibraryWeb.Canvas.cs` (the extension's door, the API, the pages) and `extension/`.

This document starts as an audit of Eli's port of Marginalia's Canvas mirror against Marginalia itself, the design's
Canvas screens (sections 06 to 12) and the Canvas REST API. Workstream WS5 closes the gaps in eight tasks (T1 to T8);
each section says which task does what.

## Audit: bugs found in the port

Verified by reading the code. Several came from Marginalia. The last column is the task that fixes each one.

| # | Bug | What goes wrong | Fixed in |
|---|---|---|---|
| 1 | **Every 401 means "signed out".** | Canvas answers `401 {"status":"unauthorized","errors":[{"message":"user not authorized to perform that action"}]}` for a tab the student can't see (Files, Pages and Quizzes are often hidden). The crawl then cleared its whole queue and Settings said "Chrome isn't signed in". Signed out is `401 {"status":"unauthenticated"}` ("user authorization required"), an HTML page for a JSON job, or a bounce to `/login`. | T1 |
| 2 | **Announcements stop after 28 days.** | `/api/v1/announcements?start_date=…` without `end_date`: Canvas defaults `end_date` to start + 28 days, so in the fall only August's announcements were ever read. | T1 |
| 3 | **Paged listings overwrite themselves.** | Modules and announcements wrote modules.md / announcements.md from each page, so a second page left only page 2. | T1 |
| 4 | **Big modules mirror empty.** | With `include[]=items` Canvas may leave `items` out (giving `items_url`); the port treats that as no items. | T5 |
| 5 | **Module file links break.** | modules.md links to `SafeName(item title)` but the file is saved under its `display_name`; two files with one name in a module overwrite each other and swap on every sync. | T5 |
| 6 | **Module folders duplicate.** | Folders are "NN Name" by position; a rename or reorder makes a second folder with fresh copies. | T5 |
| 7 | **False "Removed".** | A failed assignments listing (a 403 or 5xx was dropped) made `Finish` diff a short list: "Removed: …" changes, and `canvas_assignments.json` lost those assignments. | T1 |
| 8 | **No back-off.** | `403 Forbidden (Rate Limit Exceeded)` was treated as "forbidden" and dropped; no retry for 5xx or network errors. | T1 |
| 9 | **Extension updates stall the sync.** | `CanvasSync.Work` gives no jobs to an extension of another version; the library's own extension folder is only rewritten from Settings, so after an update Chrome polls "hot" (every 1.5 s for 90 s each minute) and never syncs. | T2 |
| 10 | **Big files are rejected.** | A 40 MB file is ~53 MB of base64 in a JSON POST, six results posted together; Kestrel's default 30 MB request limit answers 413, the extension throws, and the job comes back every 10 minutes forever. | T2 |
| 11 | **The extension reads error bodies as files.** | Bytes jobs don't check `r.ok`, and the size check happens after reading the whole body. | T2 |
| 12 | **One file host, hard-coded twice.** | `*.inscloudgate.net` is in background.js and `CanvasSync.FileStore`, plus the manifest via `Prepare`: it should be one list (`Extension.FileHosts`). | T2 |
| 13 | **HTML → Markdown is messy.** | "Links to an external site." screen-reader spans end up in the text; equation images lose their LaTeX; iframes (videos) vanish; relative links (`/courses/…`) break outside Canvas; files linked inside instructions, pages and announcements are neither saved nor linked locally. | T4 |
| 14 | **Status is too coarse.** | Pass/fail and letter-graded work (score null, grade set) shows as "submitted"; graded late work loses "late" (the design shows "Submitted late · 17/20"). | T3 |
| 15 | **Submissions lose detail.** | Grader comment attachments (marked-up PDFs) aren't saved; attempt history isn't read; rubric marks lack rating names and points possible. | T3 |
| 16 | **`canvas_page` converts any body to Markdown.** | Marginalia converted only HTML (by content type). | T2 |
| 17 | **Other people's data.** | AI reads (`canvas_api`) can fetch rosters (`/users`, `/enrollments`) and other students' posts (`/discussion_topics/:id/entries`, `/view`). | T8 |
| 18 | **crawl.json holds too much.** | It keeps every raw assignment JSON of the sync and is rewritten after every result. | T3 |

## Audit: parity with Marginalia

Marginalia is Eli's original Python app (`marginalia/{crawl,canvas,canvas_browser,engine,web}.py` and its
`extension/`). Nothing it did for a student should be missing or wrong here.

| Marginalia | Study Stash | Why |
|---|---|---|
| Persisted job queue (crawl.json), six jobs per ask, in-flight jobs back in the queue after 10 minutes | **Kept** | A restart or a closed Chrome loses nothing. |
| Four listings per course: assignments (with your submission), your submissions (comments, rubric assessment, assignment), modules with items, announcements | **Kept**; announcements now come from `/api/v1/courses/:id/discussion_topics?only_announcements=true` (T1) | The old endpoint stops after 28 days (bug 2); the course's list has the whole term and `read_state`. |
| spec.md, feedback.md, submission files, module files and pages, modules.md, announcements.md | **Kept**, same content | Under `<class>/Canvas/` instead of `materials/canvas/` in a notes repo. |
| A spec.md written by hand (no `generated_by:`) is left alone | **Missing in the port, restored (T1)** | The student's own notes win over the mirror. |
| An assignment's folder found by a spec.md naming its `canvas_id` or Canvas URL | **Restored (T1), narrowed** for any folder directly under `Canvas/assignments/` | Marginalia matched the URL anywhere in any spec, so a copied link in one assignment's instructions handed its folder to the linked one. A spec the sync wrote now names its assignment only by the `canvas_id:` line in its front matter; only a hand-written one may name it by URL. Marginalia also guessed from folder names (`match_existing`); see below. |
| Manifest: a file version already here (`updated_at`) isn't downloaded again; files are compared before writing | **Kept** | A second sync of the same Canvas writes nothing (tested). |
| Videos, audio, locked files and files over 40 MB skipped | **Kept** | Too big for the extension's transport; T5 records them as skipped with a Canvas link. |
| Status rules (excused, graded, missing, late, submitted, no submission, past due, open) | **Kept**, refined in T3 | Letter and pass/fail grades, graded-and-late (bug 14). |
| Changes between syncs in words ("NEW", "DUE MOVED", "STATUS", "REMOVED") | **Kept** as "New:", "Due date moved:", "Now graded:", "Removed:"; structured in T3 | The design's notifications (section 12) need kinds, not only text. |
| AI reads through the extension (json, text, bytes), only Canvas and its file store, bytes saved only inside the library | **Kept** | `canvas_api`, `canvas_page`, `canvas_download`. |
| `canvas_page` converts HTML to Markdown only when the answer is HTML | **Missing in the port, restored in T2** | Bug 16. |
| Course import read `include[]=term` and `course_code` | **Missing in the port, restored in T7** | Course info for matching "COMP 101 on Canvas" to class "CS 101". |
| Read-only extension: refuses non-Canvas URLs, strips `while(1);`, reports a sign-in bounce or HTML for JSON as 401, reloads itself when its folder has a newer version | **Kept**, extended in T2 | Eli added a per-library key (`X-Study-Stash-Key`), host permissions narrowed to this Canvas and this library, and the `public_url` fallback for downloads a service worker can't follow. 1.3 also says `signed_out`, passes on Canvas's rate limit, checks a file's size before reading it, checks its folder before every ask, and posts files one at a time (see "The extension"). |
| `Requeue` when a fresh extension asks with `force` | **Added by Eli, kept** | An updated extension loses nothing. |
| Course scout (AI explores a course, writes canvas-recipe.md) | **Kept** | T8 tells it what the sync now saves, so it only explores what's outside. |
| Personal access token mode (`canvas.py`) | **Not ported** | Schools turn tokens off; the extension works at every school. |
| Headless Playwright browser with its own Chrome profile (`canvas_browser.py`) | **Not ported** | No bundled browser; the extension already has the student's session. |
| `match_existing`: guessing an assignment's folder from hand-made folder names ("HW 1" ↔ "Homework 1") | **Not ported** | That was for Eli's own hand-kept folders; Study Stash makes the folders. A spec.md naming the assignment still claims its folder. |
| `integrate` (an AI folds new material into course notes) and the semester.md deadlines reconcile | **Not ported** | Study Stash has no hand-kept course notes; chat, the scout and the Due list cover it. |
| Git commits of Canvas content, the `<SEM>/canvas.md` snapshot table | **Not ported** | The library isn't a git repository; the Due page and the API replace the snapshot. |

## Audit: what neither had (the gaps)

Compared with the design's Canvas screens (06 settings, 07 connect, 08 states, 09 Due list and an assignment,
10 class tabs, 11 class sections, 12 next due, quick panel and notifications) and the Canvas REST API.

| Gap | Design | Closed in |
|---|---|---|
| Assignment status: to do / submitted / late / missing / excused / graded, with letter and pass/fail grades | 09, 10 | T3 |
| Rubric with ratings and points, per-criterion marks and comments ("Stack traces 8 / 10 · 'The frame for n = 1 is missing in 3b.'") | 10 | T3 |
| Submission: attempts, submitted at, grader comments with author and date, files with sizes, grader attachments | 10 | T3 |
| A per-class index (JSON) that the API and tools read | all | T3 |
| Canvas HTML read well as Markdown; files linked in instructions, pages and announcements saved and linked locally | 10 | T4 |
| Pages outside modules, the front page, the syllabus (`Canvas/syllabus.md`) | 11 | T4 |
| Module items of every type (File, Page, Assignment, Quiz, Discussion, ExternalUrl, ExternalTool, SubHeader); Box, Drive and OneDrive links with their source ("Saved from Box") | 11 | T5 |
| The Files area ("Files · 23"), with a graceful fallback when Canvas hides it (401/403) | 11 | T5 |
| Announcements with read state ("4 · 1 new"), bodies as Markdown, attachments | 11, 12 | T6 |
| Quizzes (title, due, points, description; never questions or answers) and discussion prompts (never other students' posts) | 10 | T6 |
| Upcoming work across courses (planner items), grouped overdue / this week / later / handed in | 09, 12 | T6 |
| Course code and term; course ↔ class matching ("COMP 101 on Canvas" ↔ "CS 101") | 06, 10 | T7 |
| Connection state (not set up / no extension / signed out / Chrome away / syncing / synced at / error), notifications | 07, 08, 12 | T7 |
| The JSON API for the Canvas screens | 06 to 12 | T7 |
| Read-only Canvas tools for Claude beyond `due_assignments`; other people's data refused in AI reads | 14 | T8 |
| The extension's version handshake, big files, one list of file hosts | 07, 08 | T2 |

## What's on disk

### Today (after T2, before T3–T8)

```
<class folder>/Canvas/
  assignments/<name>/spec.md          instructions, due date, points, rubric (a hand-written one is left alone)
  assignments/<name>/feedback.md      your submission: status, score, rubric marks, comments
  assignments/<name>/submission/      the files you turned in
  modules.md                          the module outline, linked to the local copies
  modules/<NN Module>/                module files, and module pages as Markdown
  announcements.md                    announcements, newest first
  canvas-recipe.md                    the scout's map of how this course uses Canvas
home/
  canvas.json                         settings: Canvas address, class → course id, last sync, error, changes
  crawl.json                          the sync's queue, its sections, the manifest of saved files
  canvas_assignments.json             every assignment and where you stand, for the Due list
  canvas_key                          the extension's own key
  chrome-extension/                   the extension's folder, for Chrome's "Load unpacked" (brought up to date when the library starts);
                                      on a Mac, only one an older Study Stash wrote (see below)
~/Study Stash/Chrome extension/       on a Mac, the extension's folder (Chrome's picker hides the dot-folder home)
```

### Target (after T8) — reached

```
<class folder>/Canvas/
  syllabus.md                               T4  the course syllabus
  assignments/<name>/spec.md                    instructions, due, points, rubric (+ quiz facts / discussion prompt, T6)
  assignments/<name>/files/                 T4  files linked in the instructions
  assignments/<name>/feedback.md                your submission: status, score, rubric marks, comments, attempts
  assignments/<name>/submission/                latest attempt's files; submission/attempt N/ for older ones (T3)
  assignments/<name>/feedback/              T3  files the grader attached to comments
  modules.md                                    outline, rendered at the end of a sync, links to real local copies
  modules/<NN Module>/                          module files and pages; items the scout saved from Box/Drive (T5)
  pages/<title>.md, pages/files/            T4  pages outside modules, the front page, files they link
  files/<Canvas folder path>/<file>         T5  the Files area, when Canvas lets the student see it
  announcements.md, announcements/files/    T6  newest first; attachments
  quizzes/<title>.md                        T6  practice and ungraded quizzes (graded ones fold into their assignment)
  discussions/<title>.md                    T6  ungraded discussion prompts (graded ones fold into their assignment)
  canvas-recipe.md                              the scout's
home/
  canvas.json  crawl.json  canvas_assignments.json
  canvas/<class>.json (+ .sync.json while syncing)      T3 the per-class index
  canvas_notifications.json                              T7
  canvas_seen.json                                       T6 announcements opened in Study Stash
```

The sync never deletes anything from disk: an item gone from Canvas disappears from the index and the API after a
complete read, and the student keeps the copy they had.

## Robustness: how a sync survives Canvas

Each job's answer is classified (`Crawl.Classify`) before anything is filed:

| Answer | Means | What the sync does |
|---|---|---|
| `signed_out: true` from the extension, or 401 whose body isn't Canvas's `"unauthorized"` JSON (an empty body from a sign-in bounce, `{"status":"unauthenticated"}`, an HTML page) | Chrome isn't signed in to Canvas | Stops: the queue is cleared and nothing more goes to Canvas, the sync isn't filed (no `last_sync`, every class keeps what it had), state is `signed_out` and Settings says "Chrome isn't signed in to Canvas". The next good answer clears it: the next sync (within the hour, or at once with Sync now) starts afresh (measured end to end: signed out mid-sync, then signed in and synced again, it finishes with no error). |
| 401 with `{"status":"unauthorized"}` ("user not authorized to perform that action"), any other 403, 404 | The student can't see this (a hidden tab, a locked page, something removed) | Skipped quietly; a listing's section becomes `hidden`. |
| 403 or 429 with "Rate Limit Exceeded", or with `X-Rate-Limit-Remaining` ≤ 0 | Canvas asks the sync to slow down | The job goes back to the front of the queue and nothing is handed out for 30 s, doubling (60, 120 … up to 10 min) each time a job sent after the last pause is refused again; `Retry-After` wins when longer. The rest of the burst that caused a pause doesn't lengthen it. While paused, `Work` answers `hot: false`, so the extension sleeps until its next alarm. The back-off resets when a job sent after the pause comes back fine. |
| An OK answer with `X-Rate-Limit-Remaining` ≤ 0 | The answer is good; the next ones wouldn't be | Filed, then a 30 s pause. |
| 5xx, or no answer (status 0 with a network error) | A hiccup | Asked again, up to three times in all; then an error. |
| "refused: not a Canvas URL", "too big", other 4xx | It won't work by asking again | An error. |

**Sections.** Each class's listings (assignments, submissions, modules, announcements, files; pages outside modules,
the syllabus and the front page aren't tracked sections — see below) are sections of the sync: `reading` when it
starts, then `ok` (the last page was read), `hidden` or `failed`. `Crawl.Sections` shows the current (or last)
sync's; `TakeFinished` hands them to `CanvasSync.Finish`.

- A class whose `assignments` section isn't `ok` keeps its previous rows in `canvas_assignments.json`: no false
  "Removed", and the file is unchanged. Settings' error names what failed: "Couldn't read CS 101 assignments from
  Canvas (Canvas answered 503), so what you had is kept."
- Listings whose pages only make sense together (modules, announcements) collect every page and are filed on the
  last one. A failed or hidden one writes nothing, so modules.md and announcements.md stay as they were (modules.md
  is written at all only once the course is known to have any module, so a first sync that can't read modules leaves
  no file rather than an empty one).
- The Files area is two listings under one section (`files`): folders first (so each file's folder path is known),
  then the files themselves, both paged; either one hidden or failing marks the whole section that way, and Canvas
  is never asked for the files without their folders.
- A class no longer linked to Canvas isn't reported as "Removed".

**Other rules.** Every listing follows Canvas's `Link: <…>; rel="next"` header. Jobs the extension took and never
answered go back in the queue after 10 minutes, or at once when the extension starts afresh (`force`) or the library
starts (a sync cut off by a restart carries on; measured end to end). A spec.md
without `generated_by: study-stash` was written by hand and is never overwritten. AI reads use the same
classification: a hidden tab comes back as Canvas said it (status 401 and its JSON), only a real sign-out is the
error "Chrome isn't signed in to Canvas.", and a file read that Canvas refused saves nothing.

## The extension

`extension/` is a Manifest V3 extension (version **1.6**; it has its own version, separate from the app's). The same
files run in Chrome 120 or later and the browsers built on it (Edge, Brave, Arc, Opera, Vivaldi), which load the folder
described here, and in Firefox 140 or later and the browsers built on it (Zen, LibreWolf, Waterfox), which take a
packed, signed copy connected by a pasted code instead: [firefox-add-on.md](firefox-add-on.md). Wherever this page
says Chrome, any browser of its family does the same. The engine carries it as embedded resources (`extension/<file>` in `StudyStash.Core`), and
`Extension.Ensure(dir, library, key, canvasUrl)` writes it out as a folder for Chrome's "Load unpacked": the scripts
(`background.js`, `connection.js`, the popup, the "S." icons that `macos/make_icon.swift out.chrome` draws), a manifest,
and `config.json` + `config.js`. The library keeps its own folder (`Extension.Folder(home)`) ready by itself: on start, and whenever the Canvas address changes.

**Where the folder is.** `Extension.Folder(home)` is `<home>/chrome-extension` on Windows and Linux. On a Mac the home
is `~/.study-stash`, and Chrome's "Load unpacked" window hides dot-folders (and `~/Library`), so the folder is
`~/Study Stash/Chrome extension` instead: shown by that window without any shortcut, never synced to iCloud (not in
Desktop or Documents), and only this person's (the folder and the `Study Stash` folder Study Stash makes for it are
`0700`; `config.json` and `config.js` are `0600`). Another hidden home in the same account gets its own
`Chrome extension (<8 hex>)`; a home outside the person's home folder, or one Chrome already shows, keeps
`<home>/chrome-extension`. **Migration:** a Chrome that loaded `<home>/chrome-extension` before keeps loading it from
there, so `Extension.EnsureFor` writes both whenever that old folder exists (same library, key and Canvas), and the
connected copy never breaks. The student can also drag the folder onto Chrome's Extensions page (Developer mode on)
instead of using Load unpacked; on a Mac and on Windows, Add to Chrome shows the folder selected in Finder/Explorer
for that.

The same files also pack as a **Chrome Web Store** zip (`StudyStash extension-zip OUT.zip`, `Extension.PackForStore`):
no config files, no `host_permissions`, `optional_host_permissions` for any site instead. A store copy connects by a
pasted code (`connection_code`, base64url of `config.json`) and asks Chrome for its three sites in that click. Its
extra status is `no_access` (connected, but Chrome hasn't allowed the sites). See
[chrome-web-store.md](chrome-web-store.md); nothing is published.

**Permissions stay minimal.** `permissions` is `["alarms", "storage"]` (the 30-second alarm; the extension's own status
and reload guard) and nothing else: no tabs, cookies or content scripts. `host_permissions` is empty in the repo;
`Ensure` fills in exactly three kinds (no Canvas one while there's no Canvas address): the
school's Canvas (`https://school.instructure.com/*`), Canvas's file store (every host in `Extension.FileHosts`, today
`https://*.inscloudgate.net/*`) and the library's own origin. A fetch with `credentials: 'include'` to a host it may
reach carries the student's Canvas session; the extension refuses every other URL (`refused: not a Canvas URL`).

**config.json** (owner-only): `{"app": <library>, "key": <extension key>, "canvas": <Canvas base>, "files":
["*.inscloudgate.net"], "protocol": 3}`; **config.js** holds the same as `const STUDY_STASH = {…};` for a running 1.3
until it reloads. `connection.js`'s `loadConnection()` reads, in order: `config.json` (fetched with `cache:
'no-store'`, so Chrome reads the file as it is on disk now), `chrome.storage.local.connection` (a copy installed from
the Chrome Web Store has no folder to read), then the `STUDY_STASH` global from a legacy config.js (the worker's
`importScripts('config.js')` is inside a try, so a folder without it still registers). The worker reads it at the
start of every round, so a new key or library address needs no reload. With an empty `canvas` every job is refused
with `refused: Study Stash has no Canvas address yet`. `files` is the one list of file hosts (`Extension.FileHosts`),
shared with the manifest and the library's own check (`Extension.OnFileHost`, which `CanvasSync.CanvasUrl` uses for
AIs' reads): https only, no port, no user name, the host itself or a subdomain of a `*.` entry. A config.js from before
1.3 has no `files`, and background.js falls back to the old `*.inscloudgate.net` pattern. `protocol` is the protocol
the Study Stash that wrote the folder speaks; the extension sends its own.

### Protocol and handshake

| Who | What |
|---|---|
| extension → library | `GET /api/v2/canvas/work?v=<its version>&p=<its protocol>&wait=20&a=<the library address it uses>[&b=<its browser>][&force=1]`, header `X-Study-Stash-Key` (or the library password). 1.2 and earlier send no `p`, read as 1; 1.3 sends no `wait` or `a`; 1.5 and earlier send no `b` ("Chrome", "Edge", "Brave", "Firefox": the library keeps it with each copy, cleaned to letters, digits and spaces, and names that browser in what it says). The first ask each time the pump starts (installed, reloaded, the alarm after a failure) sends `wait=0` and is answered at once, so the status and badge are right straight away. `force=1` comes from a fresh start (installed, reloaded, the popup's "Sync Canvas now"). |
| library → extension | `{"jobs":[{"id","url","kind":"json\|text\|bytes"}], "hot": bool, "ext": "<the library's extension version>", "p": 3}`. With `p ≥ 3` and `wait`, the library **holds the request** until there is work (an AI's read or Find my courses is queued, someone asks for a sync, a sync falls due, Canvas's pause runs out: it looks again every 5 s) or `min(wait, 25)` seconds pass (Chrome drops a fetch with no answer after 30), or the request goes away, or the library is stopping. Older protocols are answered at once, and `hot` (ask again in 1.5 s: an AI is reading) is for them. |
| extension → library | `POST /api/v2/canvas/results {"results":[result]}`; result = `{"id","status","link","type","final","text" or "b64","error","signed_out","rate","retry_after"}`. The last three are new in protocol 2; the library reads their absence as an old extension. |

**Every extension gets work.** The library hands jobs to any protocol ≥ 1, whatever its version (bug 9: 1.2 and
earlier were given nothing while their version differed from the library's, so a folder that wasn't updated stalled
the sync for good).

What protocol 3 (1.4) adds:

- **Waiting for work.** The pump asks again as soon as the library answers, so an AI's read, Find my courses or "Sync
  now" reaches Chrome in about a second instead of on the next minute's alarm (measured end to end: Find answers in
  under 0.1 s). A library that answers an empty request in under a second is asked again only after 1.5 s (never a
  tight loop), and a library that answers `p < 3` is asked the old way (again only while `hot`, else on the alarm).
  The popup's Sync button aborts a waiting request and asks again at once with `force=1`.
- **Staying awake.** After every answer the worker writes `chrome.storage.session.lastPoll`: any extension call resets
  Chrome's 30-second idle timer, and each request is held for 20 s at most, so the worker never sleeps while the
  library answers (measured: three idle minutes, a check-in at least every 15 s). When the library can't be reached
  the pump stops, and the **30-second alarm** (`periodInMinutes: 0.5`) starts it again (measured: a restarted library is
  found again within 30 s).
- **Saying what's wrong.** `chrome.storage.local.status = {state, library, at}`, where `state` is `ok`, `no_config`
  (no connection anywhere), `library_refused` (the library answered 401/403: the key changed, say after a reinstall),
  `library_unreachable` (no answer, or an error) or `signed_out` (a Canvas answer said signed out, until one comes back
  fine). Anything but `ok` puts a `!` badge on the toolbar button and the reason in its title; the popup says the same
  sentence ("This extension's key was refused. Open Study Stash and add the extension again.", "Can't reach your
  library at mini.local:8787.", "Sign in to Canvas in Chrome.") and keeps Sync Canvas now / Open.
- **Its address.** `a` is the library address from its config; the library records whether that's this computer's
  loopback (`seen_where: this_computer`) or another address (`another_computer`).
- **Two Chromes.** The library's own Chrome and the laptop's may both run the extension (each from its own folder).
  Both wait for work, jobs go to whichever asks first (a job is handed out once), and each is kept on its own in
  `canvas.json` `extension_copies` (`where` → `{seen, version, protocol}`), written at most every 15 s per Chrome.
  `seen`, `seen_version`, `seen_protocol` and `seen_where` are the one that asked last; `copies` in
  `/api/v2/canvas/extension` lists them all, each with `connected`. "Updated itself" is noted only when one Chrome's
  own version goes up (two Chromes on different versions taking turns aren't an update); a 1.3 copy (no `a`) that
  reloads into 1.4 is the same Chrome. Measured end to end: with both waiting, `canvas.json` is written about four
  times in 30 s and Find is answered at once.
- **A wrong key.** The library answers 401 before anything else, so a Chrome with a key it didn't make is never
  written down, takes no work and reads nothing on Canvas; the extension says `library_refused`. Once the right key is
  in its folder, the next alarm (within 30 s) connects it, with no reload: the key is read every round.

What protocol 2 (1.3) does, answer by answer (all kept in 1.4):

- **Signed out, said plainly.** `signed_out: true` for a bounce to Canvas's `/login`, an HTML page where JSON was
  asked for (a school's sign-on page on its own host), or a 401 whose body says `unauthenticated` / `user authorization
  required`. Never for Canvas's `{"status":"unauthorized"}`, which only means the student can't see that tab. The
  status is 401 in all three, so a library from before `signed_out` still stops the sync.
- **Canvas's allowance.** `rate` (from `X-Rate-Limit-Remaining`) and `retry_after` (from `Retry-After`) are passed on
  as Canvas wrote them, with `type` (the content type); the library parses them and pauses (see Robustness).
- **Files.** The extension checks `Content-Length` before reading a body: over 40 MiB it answers `{status, error: "too
  big"}` and never reads it (and checks the real length again after reading). When Canvas answers a file with an
  error (`!r.ok`), no `b64` is sent: only the first 4,000 characters of the error as `text`, so the library tells a
  hidden file from a sign-out the same way it does for JSON, and never saves an error page as the file.
- **Kept from before:** `while(1);` stripped from JSON, text answers cut at 2,000,000 characters, and the `public_url`
  fallback: when a service worker can't follow `/files/<id>/download`, it asks `/api/v1/files/<id>/public_url` and
  fetches the signed link, only if that link is on a file host.
- **Posting.** A round's JSON and text answers go in one POST; each file answer goes in a POST of its own (one big
  body per request). A POST the library refuses stops the pump: those jobs go out again after the ten-minute in-flight
  timeout (at once when the library was restarting: a library hands out again, when it starts, every job that was
  out with Chrome), and the next alarm pumps again.

### Versions, updates and reload

- **Reload before work.** Before every ask for work, and again before running work the library held for it, the
  extension reads its folder's `manifest.json` (`chrome.runtime.getURL`, `cache: 'no-store'`). When its version, or
  its sorted `host_permissions` (1.4: the Canvas or library address changed), isn't what's running it calls
  `chrome.runtime.reload()` without running anything, so a reload never drops work. The reloaded copy starts with
  `force=1`, and the library puts back whatever an older copy still held (`Crawl.Requeue`, and `AgentQueue.Requeue` for
  an AI's reads still being waited for), so an update or a new Canvas address loses nothing. When the library rewrites
  the folder it also nudges the waiting request, so the reload happens at once (measured: Find works on a new Canvas
  address 0.1 s after it was saved, with no human step). A reload for the same folder as the last one (Chrome came back
  still running something else) waits a minute (`chrome.storage.local.lastReload = {at, want}`), so the extension can
  never reload itself in a loop. (1.2 only looked at its folder after the library's `ext` differed, so it may take one batch before
  reloading; the new copy's forced ask brings that batch back at once.)
- **The library keeps its folder current.** `Extension.Refresh(dir)` brings a folder up to this engine's version,
  keeping what it connects to (`Extension.Connection`: config.json, or a legacy config.js): scripts, pages, icons and
  manifest are rewritten, and config.json + config.js with the same library address, key and Canvas. It leaves alone a
  folder without `manifest.json` or a readable connection, rewrites only files that differ, and returns whether the
  version changed. The library ensures its folder (`Extension.EnsureFor`) when it maps its routes (`LibraryWeb.MapCanvas`), so
  a library update reaches Chrome on its next ask (a running 1.3 reloads into 1.4 by itself, measured end to end).
- **The update is noted once.** Each visit records the running version (`canvas.json` `extension_version`). When it
  goes up from a known older version, `extension_update = {from, to, at, dismissed}` is set, and `GET /api/v2/canvas`
  shows `"extension_update": {"from","to","at"}` until `POST /api/v2/canvas {"dismiss_update": true}` (design 08's
  "Extension updated" state: "The Chrome extension updated itself", "Now version 1.4. Nothing to do.", Dismiss; the
  version is `to`). `extension_latest` is the library's version and
  `extension_outdated` is true while the running one is older, compared as versions (1.10 is newer than 1.9).

### Setting it up, and the folder's life

There is no "Make it" step anywhere: each computer's folder is written and kept by Study Stash itself.

1. **The library** makes its folder (`Extension.Folder(home)`, and keeps an old `<home>/chrome-extension` current) when it starts (`EnsureHere`, even before there's a Canvas address:
   then its manifest has no Canvas host and every job is refused), and writes it again when the Canvas address is
   saved (Settings, `POST /api/v2/canvas {"url"}`, Find my courses with an address typed); every start also brings
   it up to a new engine version. Its config points at `http://127.0.0.1:<port>`.
2. **The laptop app** keeps its folder (`ExtensionKeeper.LocalFolder`, the same rule) with `ExtensionKeeper.KeepAsync`: it asks the library
   (`GET /api/v2/canvas/extension`) and, once the library has a Canvas address, writes the folder pointing at the
   library as the laptop reaches it (`CanvasClient.ServerUrl`), the key and the Canvas address. It writes again when
   any of those or the extension version changes, and never when nothing did. When the app and the library share a
   computer and a folder, it only reports the library's own.
3. **The student** loads the folder once in Chrome (chrome://extensions, Developer mode, Load unpacked, or drags the
   folder onto that page), or installs
   the Chrome Web Store copy and pastes the code (`connection_code`, `ExtensionKeeper.ConnectionCode`). The screens
   can show "Add to Chrome" until `connected` (a check-in within 90 s for protocol 3), then which Chrome it is
   (`seen_where`).
4. **After that, nothing by hand.** A folder rewritten with new host permissions or a new version reloads the running
   copy by itself (the library also nudges the waiting request, so it happens at once); a new key or library address
   in config.json is read on the next round without a reload. Chrome only has to keep running with Developer mode on
   (Chrome turns off an unpacked extension that reloads itself once Developer mode is off: the store copy avoids that).

**Find my courses** saves a typed address first, then asks Chrome; `FindOutcome` names what went wrong (no address,
Chrome signed out, no extension has checked in, Chrome away, or the error itself) instead of "Chrome didn't answer".
It keeps each course's term dates (`term.start_at`/`end_at`, and the course's own `start_at`/`end_at`) in `CourseInfo`.

**Choosing which courses to bring in.** A school's list holds last term's courses, sandboxes, chapel and orientation
next to this term's classes, so nothing is brought in until the student ticks it: setup's Canvas step and the connect
window list every course found, and Settings → Canvas has "Courses to bring in". `CourseChoices.Suggest` ticks a
course when its term's (or its own) dates say it's on now (up to 30 days early, 7 days late), or, without dates, when
its term's name ("Fall 2026", "2026 Fall") does; it leaves unticked, saying why, a past or later term, a course in no
real term ("Default Term" or none), and one named like a sandbox, chapel, convocation, orientation, advising or
training. The choice lives in `canvas.json` as `chosen` (course ids; absent in a library from before it, where every
linked course counts as chosen). The sync (`CanvasSettings.Synced`) and the Scout read only chosen courses.
`POST /api/v2/canvas/choose {"courses": [ids], "keep": true, "match": false}` (`CourseChoices.Apply`) makes a class for
each newly chosen course (its cleaned, unique name, or an unlinked class of that name; with `match`, the unlinked class
`CourseMatch` suggests), and each course no longer chosen stops syncing: with `keep` its class and files stay; without,
its Canvas folder, index, assignments and crawl memory go, and its class too unless a lecture is filed in it (refused
with 409 while a sync runs). Its answer is `GET /api/v2/canvas` plus `outcome: {added, stopped, removed,
kept_for_lectures}`. `GET /api/v2/canvas` says, per course in `course_info`, `chosen`, `suggested`, `why` and `class`,
and the whole `chosen` list (null before any choice). Linking a class by hand (`POST /api/v2/canvas {"courses"}`)
chooses its course too.

**The app refreshes by itself.** `GET /api/v2/canvas/state` carries `revision`, which changes whenever a sync finishes
or a course is linked, unlinked or chosen. The app's `CanvasFeed` follows the shared watch and reads the Due list and
the classes again whenever `revision` (or, during a sync, the classes left) changes, so an open Due page, a class page,
the dropdown's next due and the sidebar's count follow a sync without reopening anything. Canvas's files are written
with `SharedFile` (a file of its own, swapped in, retried while busy) and read without holding them: on Windows a file
that's open can't be replaced, and a finished sync used to fail to save what's due while the app read it.

**For WS3/WS6 (the app).** The Canvas screens read `extension_version`, `extension_latest`,
`extension_outdated` and `extension_update` from `GET /api/v2/canvas` (T7 also puts them in `/api/v2/canvas/state`)
and dismiss the update with `POST /api/v2/canvas {"dismiss_update": true}`.

### Size limits

| Limit | Value | Where |
|---|---|---|
| Biggest file mirrored | 40 MiB (41,943,040 bytes) | `Crawl.MaxBytes` (not queued when Canvas says it's bigger) and background.js `MAX_BYTES` (Content-Length, then the real length) |
| One POST to `/api/v2/canvas/results` | 256 MiB | Raised from Kestrel's 30,000,000-byte default for that route only, after the key check. A 40 MiB file is about 56 MB of base64 in JSON; an old extension posting up to four such files together fits too. |
| A text answer | 2,000,000 characters | background.js; an AI's `canvas_page` keeps the first 60,000 |
| An error page for a file | 4,000 characters | background.js |

**AIs' page reads** (`kind: "text"`, `canvas_page`) come back as Markdown only when Canvas's content type says HTML;
plain text, CSV or JSON come back as Canvas sent them (Marginalia's rule, bug 16).

## The JSON API for the Canvas screens

Every route below is under the library password, `Authorization: Bearer <password>`, like the rest of `/api/v2`
(`LibraryWeb.Api`/`ApiAsync`). A class name goes in the query (`?class=CS%20101`), never the path, since class names
may hold `/`. `*_at` fields are Canvas's own UTC ISO timestamps; `due` (and `synced`, `last_sync`, `posted_at` when
they come from a saved index) are as `AssignmentInfo`/`CourseIndex` keep them. A date the library doesn't have (a
library that has never synced, Chrome never seen, nothing submitted) is `null`, never `""` (`CanvasView.When`), and
the app reads any date it can't parse as none (`CanvasApi.ReadWhen`): one odd field never breaks a screen. An item's
`marked_done` is when it was ticked off in Canvas's planner, or `null`. `Core/Canvas/CanvasView.cs` builds
every answer below from `CanvasSettings`, a class's `CourseIndex` (`home/canvas/<class>.json`) and the crawl's live
state; `Library/LibraryWeb.Canvas.cs` only maps routes onto it, so `LocalLibrary` (Claude's tools, T8) can call the
same builders without going through HTTP.

**Where the data comes from today.** Assignments, their rubric, submission, attempts, files and comments are real
(T3): every assignment- and Due-list endpoint below reflects a real sync. `Modules`, `Files`, `Announcements`,
`Quizzes`, `Discussions` and `Todos` are already fields on `CourseIndex` and every endpoint that reads them is
built and tested against that shape, but the crawl doesn't fill them in yet (T5 leaves modules.md and
announcements.md as plain Markdown instead of a structured `ModuleInfo`/`AnnouncementInfo` list; quizzes,
discussions and the planner are T6's). Until then `GET .../modules`, `.../files`, `.../announcements` and
`.../pages` answer with the right shape and an empty (or files-only) list; the Due list and notifications only ever
carry assignments, never a `TodoInfo`. Nothing needs to change in this file or in `LibraryWeb.Canvas.cs` when T5/T6
land: they fill `CourseIndex`, and every builder here reads straight from it.

### State

- `GET /api/v2/canvas/state` (also embedded as `state` in `GET /api/v2/canvas`): `{"state", "school", "url",
  "extension":{"seen","version","latest","outdated","updated":{"from","to","at"}|null,"connected","key_matches",
  "last_seen","browser" (the browser that checked in last, "" from an extension before 1.6),"refused_at"}, "last_sync" (when the last
  sync finished), "next_sync", "poll_minutes", "syncing":{"left","total","classes":[…]}|null, "paused_until"|null,
  "error":{"text","at"}|null, "warnings":[…], "revision"}`. `state` is decided by `CanvasView.StateOf` in this order: `not_set_up`
  (no Canvas address) > `no_extension` (no Chrome has ever checked in with this library's **current** extension key)
  > `signed_out` > `chrome_away` (no Chrome with the current key is checking in now: 90 s of quiet for a long-polling
  extension, 5 minutes for an older one) > `syncing` > `error` (the last sync ended with one) > `connected`.
  "Connected" is only ever a check-in with the current key: each check-in is written down with a fingerprint of the
  key it came with (`CanvasSettings.KeyId`, never the key), so a registration from an old install, from before keys
  were written down, or from before the key changed never counts, and one that came with the library password gets
  its work but isn't the extension. `extension.seen` is when a Chrome last checked in with the current key,
  `extension.connected` whether one is checking in now, `key_matches` whether the Chrome that asked last had the
  current key, `last_seen` when any Chrome last asked, and `refused_at` when a Chrome with another key was last turned
  away (`/api/v2/canvas/work` answers such a key 401 "Wrong key." even on a library without a password). The app
  moves past its extension step only on `extension.connected`, and asks to "Connect Chrome again" for an old key.
  `GET /api/v2/canvas/extension` carries the same `connected`, `key_matches`, `seen_with_key` and `refused_at`, and
  each of `copies` its own `key_matches`. `syncing.left`/`total` count
  classes, not jobs (`total` = classes in this sync, `left` = classes whose four listings haven't all finished);
  `classes` names them, straight from the crawl's live section states — no new state kept for this.
- `POST /api/v2/canvas` (unchanged routes, wider body): also takes `poll_minutes` (15–1440, clamped) and
  `dismiss_update: true`; its answer (same as `GET /api/v2/canvas`) now also carries `state` and `course_info`
  (Canvas course id → `{code, name, term}`, from `FindCoursesAsync`, which now follows every page and asks for
  `include[]=term`).

### Classes and the Due list

- `GET /api/v2/canvas/classes` → one row per class in this library, linked or not: `[{"class","linked",
  "canvas":{"id","code","name","term","url"}|null, "suggested":{"id","name"}|null,"last_sync","counts":
  {"to_hand_in","done","modules","files","announcements","announcements_new"},"files_hidden","scout":
  {"state":"done|exploring|waiting|failed|never","files","when","report"}}]`. `suggested` (only when a class isn't
  linked) is `CourseMatch.Suggest`'s best guess from the school's course list: a shared number ("CS 101" ~
  "COMP 101") or a shared word, one a prefix of the other ("CALC" ~ "Calculus"); it's shown, never linked, by
  itself. `announcements_new` is unread-on-Canvas **and** not opened in Study Stash: `CanvasSeen`
  (`home/canvas_seen.json`) narrows Canvas's own `read_state` further, per student, per class.
- `GET /api/v2/canvas/due` → `{"synced","to_hand_in","next":item|null,"groups":[{"key","label","items":[item]}]}`
  across every class, groups in order **Overdue** (not done, due before now) / **This week** (due before the eighth
  day from now) / **Later** / **No due date** / **Handed in** (submitted, graded, excused, or marked done in Canvas's
  own planner though nothing was ever handed in (T6, `item.marked_done`) — most recently
  submitted/graded/marked-done first, only the last 7 days). `to_hand_in` is the size of the first four groups
  combined; `next` is the soonest of them that isn't overdue. Canvas's planner also has to-dos with no assignment of
  their own (an ungraded page or note with a date): kept in `CourseIndex.Todos`, and the library folds them into
  these same groups (`item.kind: "todo"`, LibraryWeb's `/due` handler, since `CanvasView.Due` itself only takes rows
  already shaped like `Assignment`).
- `GET /api/v2/canvas/assignments?class=` → `{"class","to_hand_in":[item],"done":[item]}` (soonest due first / most
  recently due first). `item` = `{"class","id","name","kind","due","due_at","points","status","label","score",
  "grade","score_text","late","missing","excused","submitted","graded_at","marked_done","url","folder"}`.

### One assignment

- `GET /api/v2/canvas/assignment?class=&id=` → `item` plus `{"instructions","unlock_at","lock_at",
  "submission_types","allowed_attempts","grading_type","files":[file],"rubric":[{"id","criterion","description",
  "points","ratings":[{"label","points"}],"mark":{"points","rating","comment"}|null}],"submission":{"state",
  "attempt","submitted_at","graded_at","score","grade","late","points_deducted","body","files":[file],"attempts":
  [{"attempt","submitted_at","late","files":[file]}]}|null,"comments":[{"author","at","text","files":[file],
  "media_url"}],"spec","feedback"}` — a graded quiz or discussion doesn't get a separate `"quiz"`/`"discussion"` key:
  its facts fold straight into `spec.md`'s own fact line ("Quiz · 5 questions · 15 minutes · 2 attempts"), and a
  discussion's prompt fills `"instructions"` when the assignment has none of its own (T6). `spec`/`feedback`
  are the assignment's own `spec.md`/`feedback.md`, relative to the class's folder, for `/api/v2/files/raw`. `file` =
  `{"id","name","size","content_type","format","local","skipped","url"}`; `format` is derived from the content type
  or the name's extension (module File items save a real one straight on `ModuleItemInfo.Format`; Files-area items
  derive it the same way when the API answers, since Canvas rarely changes a file's type between syncs).
  A 404 for an unknown class or assignment id.

### Modules, files, announcements, pages

- `GET /api/v2/canvas/modules?class=` → `{"count","modules":[{"id","name","position","state","unlock_at",
  "items_count","items":[{"id","type","kind","title","indent","format","size","local","saved","source","url",
  "external_url","assignment_id","locked","skipped"}]}]}`, straight off `CourseIndex.Modules`.
- `GET /api/v2/canvas/files?class=` → `{"allowed","count","files":[{"id","folder","name","size","content_type",
  "format","updated_at","local","skipped"}]}`; `allowed` is false only when Canvas hides the Files area
  (`CourseIndex.FilesHidden`).
- `GET /api/v2/canvas/announcements?class=` → `{"count","new","items":[{"id","title","posted_at","author","new",
  "read_on_canvas","body","files":[file],"url"}]}`, newest first; `new` is unread on Canvas and not marked seen in
  Study Stash. `POST /api/v2/canvas/announcements/seen {"class","ids":[…]}` marks announcements opened here
  (`Core/Canvas/CanvasSeen.cs`, `home/canvas_seen.json`) so they stop counting as new even before Canvas itself
  shows them read.
- `GET /api/v2/canvas/pages?class=` → `{"syllabus":path|null,"front_page":page|null,"pages":[page],"quizzes":[…],
  "discussions":[…]}`; `page` = `{"title","url","updated_at","local","in_module"}`. `quizzes`/`discussions` here list
  every one Canvas has, but `local` is only set for a practice/ungraded one (its own `Canvas/quizzes/<title>.md` or
  `Canvas/discussions/<title>.md`, T6); a graded one folded into an assignment has `local: null` here — its facts are
  on the assignment instead (see "One assignment" above).

### Notifications

- `GET /api/v2/canvas/notifications?after=<id>` → `{"last","items":[{"id","kind","title","text","class",
  "assignment_id","announcement_id","at","seen"}]}`. `Core/Canvas/CanvasNotifications.cs`
  (`home/canvas_notifications.json`, ids always increasing, capped at 200) turns a finished sync's `CanvasChange`s
  into notifications (`new` → `new_assignment`/"New assignment", `moved` → `due_moved`, `graded` → `new_score`,
  `feedback` → `new_feedback`, `missing` → `missing`; `removed` isn't news). A class's first sync makes no changes at
  all, so it makes no notifications either. Reading this endpoint also adds one `due_soon`/"Due soon" per assignment
  the first time it's open and due within 24 hours, never repeated for the same assignment.
- `POST /api/v2/canvas/notifications/seen {"up_to":id}` marks every notification up to that id seen.

### Raw files

- `GET /api/v2/files/raw?class=&path=` → the bytes of any file already saved inside that class's folder (any size,
  any type — for opening `ps4-answers.pdf` on a laptop, say); 404 for a path that leaves the folder or doesn't
  exist. `GET /api/v2/files?class=&path=` (unchanged) still only serves small `.md`/`.txt` files, as text, for the
  library's own pages.

### Unchanged

`GET /api/v2/canvas` gains `state` and `course_info` but keeps every key the app already reads (`url`, `available`,
`courses`, `error`, `extension_seen`, `syncing`, `left`, `last_sync`, …); `GET /api/v2/assignments` keeps its flat
list and all its keys. The extension's door (`/api/v2/canvas/work`, `/results`, `/status`) and the AI reads
(`/api/v2/canvas/fetch`, `/agent-courses`, `/courses`, `/api/v2/canvas/scout`) are untouched.

## Claude's Canvas tools

`Core/ClaudeTools.cs` gives Claude read-only tools over Canvas, on top of `ILibrarySource.CanvasAsync(what, body)`:
`LocalLibrary` answers straight from `CanvasView` and each class's `CourseIndex` (in the library itself, or a laptop
running `Study Stash mcp` against its own home folder); `RemoteLibrary` calls the §5 endpoints over `/api/v2` (a
laptop reading a remote library), and a 404 (an older library without an endpoint) becomes "The library runs an
older Study Stash, without this." The `what`s: `courses`, `fetch` (unchanged, for `canvas_api`/`canvas_page`/
`canvas_download`), `assignments` (class, days — the flat due list), `assignment` (class, id or name), `modules`
(class), `files` (class), `announcements` (class, limit).

Tools (all `ReadOnly`, `Idempotent`; only `canvas_download` isn't read-only), registered when `lib.HasCanvas`:

- **due_assignments** (class_name?, days=14) — the due list, grouped the way the app shows it (Overdue, Due soon, No
  due date) and labelled ("To do", "Missing", "Submitted late", …), soonest first, with the folder holding each
  assignment's spec.md and feedback.md.
- **get_assignment** (class_name, assignment: an id, or words of its name — "problem set 4") — the whole story:
  instructions, due date, points, status, the rubric with your marks and the grader's comments ("Stack traces: 8 / 10
  · The frame for n = 1 is missing in 3b."), what you submitted and its files, the grader's comments with author and
  date, and where spec.md/feedback.md live. Several names matching lists them instead of guessing.
- **class_modules** (class_name) — a class's modules in order, each item's kind and whether/where it's saved
  ("(saved from Box)", "(link)", "(locked)").
- **class_files** (class_name) — the Files area, or says plainly when Canvas hides it from the student.
- **class_announcements** (class_name, limit=10) — newest first, author, date, whether it's new, body cut short.
- **canvas_courses**, **canvas_api**, **canvas_page**, **canvas_download** — unchanged, except `canvas_api`/
  `canvas_page`/`canvas_download` now refuse other people's data (below) before ever asking Canvas.

**Other people's data is refused**, not just left unmirrored: `CanvasSync.DeniesOtherPeople` (checked in `FetchAsync`,
by path, ignoring the query like everywhere else here) refuses a roster (`/users`, except the student's own
`/users/self`), `/enrollments`, `/peer_reviews`, `/search/recipients`, `/conversations`, a discussion's `/entries`,
`/view` or `/entry_list`, and a course's bare `/students` (its own `/students/submissions` — the student's own grades
— is fine) with "Study Stash doesn't read other people's Canvas data.", before Canvas is ever asked. The sync itself
never requests any of these either (§ "What's mirrored, and what's not" below).

The course scout's prompt (`Scout.Prompt`) now lists everything the sync already saves (spec/feedback/submission,
modules and their files and pages, syllabus, pages outside modules, the Files area, announcements, quizzes,
discussions) so it only explores what's missing, and tells it exactly how to make a Box/Drive/OneDrive download show
up as "saved" in modules.md and `class_modules`: save it into that module's own folder, named after the item's title
(an item titled "Tracing worksheet" becomes "Tracing worksheet.pdf") — the naming contract `CanvasView.Modules` (T5)
already checks for.

### What's mirrored, and what's not, and why

Mirrored: assignments with rubric and submission (T3), HTML read well with linked files saved (T4), every module
item type with Box/Drive/OneDrive sources (T5), the Files area when Canvas allows it (T5), announcements, quizzes and
discussion prompts (T6), the cross-class Due list (T6), course info and connection state (T7), notifications (T7).

Never mirrored, on purpose:

| What | Why |
|---|---|
| Calendar events | Not coursework; only planner assignments and to-dos feed the Due list. |
| Other students' posts, discussion entries/views, rosters, enrollments, peer reviews, conversations | Privacy — never requested by the sync, and refused in AI reads (above). |
| Quiz questions, answers and submissions | Academic integrity; only a quiz's facts (title, due, points, description) are kept. |
| Grade statistics / distributions | Not the student's own data. |
| New Quizzes and LTI tool content (Turnitin, Gradescope, publisher tools) | No stable, student-readable API without the tool's own sign-in; a module item just links out. |
| Videos, and any file over 40 MB | Too big for the extension's transport (bug 10); recorded as `skipped` with a Canvas link. |
| Material only reachable outside Canvas (Box, Drive, OneDrive, a course website) | The sync has no session there; the course scout explores and saves what it can reach. |

### The modules gap (T5), closed

An earlier task left `Crawl.Modules(cls, list)` only rendering `modules.md` straight off Canvas's raw JSON, never
filling `CourseIndex.Modules`: `GET /api/v2/canvas/modules` and `class_modules` answered an empty list against a real
sync even though modules.md itself was right. T5 fixed it: `Crawl.Modules` now builds `ModuleInfo`/`ModuleItemInfo`
for every item (a module with no inline `items` fetches them from `items_url`, paged), `modules.md` is rendered from
that index when the sync finishes (like spec.md/feedback.md — a class whose first sync couldn't read its modules gets
no outline instead of an empty one), and a renamed or reordered module moves its existing folder (`moddir:{class}:
{id}` in the manifest) instead of leaving a duplicate. The Files area (folders, then files, both paged) now fills
`CourseIndex.Files`/`FilesHidden` the same way. See `CanvasModulesTests.cs`.

### Announcements, quizzes, discussions and the planner (T6), closed

`Crawl.Announcements` used to render `announcements.md` straight off Canvas's raw JSON without ever filling
`CourseIndex.Announcements` — exactly the modules gap T5 closed, but for announcements: the API and `class_announcements`
always answered empty against a real sync. T6 fixed it the same way: `Announcements` now builds `AnnouncementInfo` for
every item (author, posted date, Canvas's own read state, the body converted like a page's, with its file links
resolved), queues its attachments into `Canvas/announcements/files/`, and `announcements.md` itself is rendered at
`Render` (once, from the promoted index, like modules.md and spec.md) so its links point at where a file really
landed. A `CanvasChange("announcement", …)` is said for an id the previous index didn't have — never on a class's
first sync, matching how a new assignment is only news from the second sync on.

Two new listings, added the same way modules and announcements already were: `quizzes`
(`/api/v1/courses/{id}/quizzes`, never `/questions`, `/submissions` or `/quiz_submissions`) and `discussions`
(`/api/v1/courses/{id}/discussion_topics` with no `only_announcements` — never `/entries`, `/view` or `/entry_list`).
A quiz or discussion Canvas already folds into an assignment (its `quiz_id`/`discussion_topic_id` was already read
off the assignment itself, T3) gets no `Canvas/quizzes/`or `Canvas/discussions/` file of its own: its facts (question
count, time limit, attempts) or prompt are added straight into that assignment's `spec.md` instead
(`CanvasMarkdown.Spec`'s new `quiz`/`discussion` parameters); an ungraded one gets its own file, and `Local` is set
on the index *before* it's saved (`SetLocalPaths`, since `Render` — which does the actual writing — only runs after
the index is promoted and saved) so the API can link to it from the moment the sync finishes.

Canvas's planner is one request for every linked class together (`/api/v1/planner/items?context_codes[]=course_…`
repeated, `start_date`/`end_date` a wide window around today): `Planner` sorts each item back to its class by
`course_id` (a small `course id → class` map built at `Start`), an `assignment` item with `planner_override.
marked_complete` becomes `Assignment.MarkedDone`/`MarkedDoneAt` (applied to the promoted index by `ApplyPlanner`,
which — like `Submissions`'s section-independence — only overwrites what the planner listing itself read this sync,
keeping the previous value when it didn't), anything else with a date becomes a `TodoInfo` in `CourseIndex.Todos`
(`Assignments.From(class, TodoInfo)` turns one into a Due-list row, kind `todo`, merged in across every linked class
by `/api/v2/canvas/due`'s handler), and a `calendar_event` is dropped (not coursework). `Assignment.Done` now also
asks `MarkedDone`, so a student's own tick in Canvas moves
work to the Due list's Handed-in group even though nothing was ever submitted — the moment it happened
(`MarkedDoneAt`) sorts it there the same way a submission or a grade would. See `CanvasAnnouncementTests.cs`,
`CanvasDueTests.cs`.

One test-harness wrinkle worth knowing: Canvas answers announcements and real discussions at the *same* path
(`only_announcements=true` is what tells them apart), but `FakeCanvas` routes by path alone. `FakeCanvas.Key` now
special-cases `discussion_topics` without that flag onto its own internal key (`DiscussionsRoute`, never a real
Canvas URL) so a fixture can answer the two differently; `Pages()` (paginating a listing across several fake
responses) keeps `only_announcements=true` on every page's constructed `Link` header for the same reason, the way
Canvas's own header would repeat the whole original query.

## Testing

Nothing here ever contacts a real Canvas. `engine/tests/StudyStash.Core.Tests/FakeCanvas.cs` stands in for Canvas
and the extension together: routes by URL path (the query is ignored except `page`), `Json`, `Pages` (with Link
headers), `Status`, `Bytes`, `FailTimes`, `On`, Canvas's own 404 for anything else, a `Requested` list for "never
asked for" checks, and `Run(sync)`, which plays the extension until the sync is done. Fixtures are in
`Fixtures/canvas/`: COMP 101 (course 4201) with the design's data on the 2025 calendar (Lab 3 due Tue 30 Sep,
Problem set 4 graded 18/20 by Dr. Okafor with the rubric comment "The frame for n = 1 is missing in 3b.", Week 3 and
Week 4 modules, four announcements with one unread). `ExtensionScriptTests` runs the real `background.js` and `connection.js` (the copies
the engine carries) in Jint, with `importScripts`, `chrome.*` (runtime with `host_permissions`, alarms,
`storage.local`/`session`, `action.setBadgeText`/`setTitle`), `fetch` (the folder's `manifest.json` and `config.json`
too), `btoa`, `URL` and `setTimeout` stood in for: the three connection sources, reloads on a new version or new host
permissions and the reload guard, the status and badge for each failure, and protocol 3's parameters;
`CanvasSyncTests` covers `WorkAsync` (work there at once, an AI's read or a nudge waking a held request, an empty
answer when the wait is over, older protocols answered at once, an old copy's read handed out again);
`CanvasExtensionTests` covers the folder, the handshake and a 40 MB file through real Kestrel. `CanvasApiTests` syncs
a library over all four of the design's classes (adding minimal fixtures for BIO 110, CALC II and HIST 210 alongside
COMP 101's) and reads the JSON API in this document back through a real `TestSite`: state's priority order, the
classes list (including a suggested, unlinked course), the cross-class Due list, one class's to-hand-in/done split,
one assignment's rubric marks and the grader's comment, modules/files/announcements' shape, notifications (due soon,
marking seen), a raw file download, and the old keys `GET /api/v2/canvas` and `GET /api/v2/assignments` still
answer. `CanvasAnnouncementTests` covers announcements read newest-first with an attachment saved, an announcement
that's only news from its second sync on, a practice quiz's facts with no forbidden request ever made for its
questions, and a discussion topic whose file never carries another student's reply (`cs101-discussion-entries.json`
is registered but never asked for). `CanvasDueTests` covers an assignment marked done in the planner (moved to
Handed in, kept even when a later sync's planner listing fails) and a planner to-do with no assignment of its own
(`CourseIndex.Todos`), with calendar events dropped. Tests fix the clock at the design's "now", Thu 25 Sep 2025,
10:24 in California (`2025-09-25T17:24:00Z`), and never assert times in the machine's own zone. Made-up people only.

**In a real Chrome.** `bash engine/tests/extension-e2e.sh [--chrome-dir DIR]` downloads Chrome for Testing once and
runs `ExtensionE2ETests` (skipped in the normal suite): a pretend Canvas over HTTP (`canvas.test` and `canvas2.test`,
both mapped to 127.0.0.1), a library on real Kestrel with a throwaway home, and the folder the library made by itself.
The stories, in order: the extension checks in; Find my courses (under 5 s); a linked class syncs; signed out says so;
a newer manifest on disk reloads it; a new Canvas address reloads it and Find works there with no human step; a
restarted library is found again within 40 s; three idle minutes with the worker awake throughout; `S8a` a laptop's
Chrome (a second Chrome, reaching the library as `library.test`) whose folder has a wrong key says `library_refused`
(badge, popup) while the library records nothing, and connects within the alarm once the key is right; `S8b` both
Chromes connected and listed in `copies`, `canvas.json` not rewritten on every visit, Find answered at once; `S8c`
signed out in the middle of a sync (the pretend Canvas signs out when the course's pages are asked for): the sync
stops, nothing more reaches Canvas, state `signed_out`, and after signing in a sync finishes cleanly; and a folder as
0.5.0 left it (1.3, `Fixtures/extension-1.3`) updates itself to 1.4. The sync story also has a 60 MB module file that
is never downloaded: modules.md links to it on Canvas ("not saved: too big"). Then `S9b`: the Chrome Web Store build, unzipped from `PackForStore`,
registers with no connection (`no_config`), no sites and a popup asking for the code, and after the library's code is
pasted (without Chrome's permission, which headless Chrome can't give) it says `no_access` and asks nothing of Canvas
or the library. Then, from the app's side (`LaptopChromeE2ETests`, in the app's tests): a library that has never
synced, the laptop app's own connect steps (`CanvasConnectModel` over its `CanvasClient`, with the library password)
from the school's address, the extension folder the laptop app makes pointing at the library as `library.test`,
Chrome checking in as `another_computer` with the library's key, Find my courses, matching, a sync, and every Canvas
screen's read afterwards, down to a submitted file's bytes. Without Chrome, `LibraryCanvasTests` runs the same steps
against a real library with a pretend extension over HTTP, and checks that no Canvas answer has a date as `""`.
`ChromeRunner.EvaluateAsync` runs JavaScript in the extension's worker or one of its pages over
DevTools (`--remote-debugging-port=0`).

Run it with `TMPDIR` pointing somewhere disposable (Chrome's profiles and the library's home go there and are
removed). `STUDYSTASH_E2E_LIBRARY_HOST=<an address of this computer, like its LAN one>` makes the library listen there
too and the laptop's Chrome use it instead of `library.test` (Chrome's Local Network Access checks don't block the
worker's fetch to a LAN address). `STUDYSTASH_E2E_CHROME=<binary>` skips the download. The whole run takes about six
minutes, three of them the idle story. Last proven with keys written down and the laptop story: 13 + 1 green
(Chrome for Testing 154). Before that: 13 of 13 green three runs in a row (Chrome for Testing 154, with
the full suite between runs), and the wrong-key and two-Chromes stories again with the laptop's Chrome on the Mac's LAN
address.
