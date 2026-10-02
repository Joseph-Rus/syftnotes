# Study Stash: design

One C# program (`engine/src/StudyStash.App`, .NET 10 and Avalonia) plays both parts of a student's
lecture library, and installs itself in one of two roles:

- **Laptop:** a menu bar (Mac) or tray (Windows) app that records, transcribes locally with
  Whisper, and sends each lecture on to a library.
- **Library:** `StudyStash serve` (or `run`, which also checks for updates) stores lectures per
  class, writes study notes with an AI engine, mirrors Canvas, and serves the web page and MCP.

```
laptop                                          library (a Mac mini, say — any computer that stays on)
  record → Whisper (local, no audio leaves it)    /api/ingest → a queue (SQLite)
  the class you picked, or none             ──▶   pipeline: study notes (an AI engine) → sort → Markdown
  Study Stash app / quick panel                   web page, Claude/MCP, Canvas mirror — over Tailscale
```

## The projects

- `StudyStash.Core` — the engine: config, the lecture database and note files, the AI providers,
  Canvas, recording and transcription, autostart, updates.
- `StudyStash.Library` — the library's web pages, the laptop-facing ingest API, and the setup page
  (ASP.NET Core).
- `StudyStash.Engine` — the `studystash` command line, for running the engine without the app.
- `StudyStash.Audio` — the microphone and the speech models: Whisper (Whisper.net; Metal on Apple
  silicon, Vulkan or the CPU on Windows) and NVIDIA's Parakeet (sherpa-onnx, on the processor; a
  model that comes as a folder of files, downloaded one after another).
- `StudyStash.App` — the Avalonia app: menu bar/tray, quick panel, full window, setup.

## Record → library → notes → Claude

1. **Record.** The app opens the microphone (`IAudioSource`), and Whisper transcribes it locally as
   it goes (`ITranscriber`), a piece at a time, carrying the previous piece's words as a prompt so
   names and terms stay spelled the same.
2. **Pick a class, or don't.** Record lets the library sort the lecture unless the student picks a
   class in the dropdown; a lecture recorded with no class is sent with an empty `folder`.
3. **Send it.** `LectureSender` POSTs the lecture (`Lecture.Payload()`) to `/api/ingest` once the
   library is reachable, then polls `/api/notes/{id}/status` until it's filed. Nothing is lost if
   the library is asleep or on another network: the laptop keeps trying.
4. **Queue and write.** `LibraryWeb.Ingest` turns the payload into a `Meeting` (`Wire.MeetingFromJson`)
   and enqueues it (`Store.Enqueue`). `Pipeline.ProcessAsync` then, in the background: writes study
   notes from the transcript with the picked AI engine, has the diagrams engine design their diagrams
   (`DiagramDesign`: it reads the timed transcript and the notes, decides whether anything is clearer
   as a picture and which kind fits — a flowchart, in groups for a big topic, a state or sequence
   diagram, a timeline or a mind map (`Mermaid` reads them all into one model; `DiagramLayout` lays
   each out) — answers in JSON that's checked strictly, and each diagram it designs goes at the end
   of its section, repaired once or left out if it doesn't draw or isn't true to the lecture;
   `DiagramLint` then lays each out at the notes' width, and one that would look wrong goes back once,
   its redesign kept only when better; a lecture that describes a physical thing at length may also
   get an illustration (`IllustrationDesign`: planned in the same reply, drawn by the same engine
   from the plan, its parts named, its callouts spaced by `Callouts`, looked over by `SvgLint`; see
   [Illustrations](#illustrations)); a pass that fails leaves the notes exactly as written), sorts
   it into a class (the recorded class or
   a title match wins outright; otherwise the AI reads the notes against each class's name, other
   names, what it covers and its Canvas course's name, and picks one with a strict schema, or it goes
   to Unsorted), and saves it as Markdown under `<library folder>/<Class>/`.
5. **Read it.** `studystash mcp` (stdio, for Claude Code or Claude Desktop on the library's own
   computer) and the HTTP + OAuth 2.1 door for claude.ai both read the same library through
   `ClaudeTools`, so Claude can search lectures, read one, and read Canvas.

## Files

Everything lives under one home folder (`~/.study-stash`, `--home`, or `STUDYSTASH_HOME`):

| File | What |
|---|---|
| `config.toml` | The library's settings: its name, password, port, classes, and which AI engine does what. |
| `client.toml` | The laptop's settings: which library it sends to. |
| `app.json` | The app's own state: what setup has finished, and how to record. |
| `recordings/` | The laptop's own lectures: audio state and what's been sent, one folder per lecture. |
| `state.db` | The library's SQLite index: one row per lecture, for search and the queue. |
| `<library folder>/<Class>/*.md` | The notes themselves, plain Markdown, one file per lecture. |

## The service

The library installs itself as this computer's own background service — `com.study-stash.server`
on a Mac (launchd), the Windows Startup folder, or systemd `--user` on Linux — so it comes back
after a restart. Setting it up again stops the copy already running first, so only one library ever
runs on a computer.

## Installers and updates

- **Four installers, one app per system.** The laptop and library installers hold the same
  program; only the role preset differs (`StudyStashRole` in the Mac bundle's Info.plist,
  `study-stash.ini` on Windows). `Apps.RolePreset` reads it once, on first run, so setup already
  knows which one it is; after that the role lives in the app's own settings.
- **One Windows app installs as either role.** Both Setup.exe files share one Inno `AppId` and
  install per-user into `%LOCALAPPDATA%\Programs\Study Stash`; whichever one you run last writes
  `study-stash.ini`'s `role=`, so installing the other role over an existing install is just an
  in-place upgrade, never a second copy.
- **Updates swap the installed copy.** An installed app checks GitHub for a new release, downloads
  the matching installer, and checks its SHA-256 against `SHA256SUMS.txt`. On a Mac it mounts the
  DMG, stages the new `.app` beside the old one, swaps them, and restarts any service that runs
  from the bundle. On Windows it runs the new Setup.exe silently, which closes the running app and
  relaunches it (and, should it stop short, opens the old copy again). Either way, only an installed copy
  updates itself; a build folder or `dotnet run` never calls GitHub. The app installs only while
  nothing is recording or being transcribed, and looks again right before the swap or the hand-off,
  since a lecture may start while it downloads. With `auto_update` off it says a new version is out
  and installs it when asked; a copy that can't replace itself (a Mac app opened from its disk image)
  says what to do; an update that fails says so, and the first start of a new version says it updated.
- **The Mac app is one universal bundle**, started by a tiny native launcher
  (`macos/launcher.c`) at `Contents/MacOS/StudyStash` — the spot macOS reads the real Info.plist
  from (`LSUIElement`, the microphone usage strings), so it can't be a per-architecture `exec`.
  The launcher `dlopen`s the matching architecture's `libhostfxr.dylib` under
  `Contents/MacOS/{arm64,x64}` and starts .NET in-process; a per-arch self-contained publish can't
  be `lipo`-merged, so this is the trick that makes both look like one program. It's signed ad hoc
  (no paid Developer ID) with the hardened runtime, `disable-library-validation` (so it can still
  load its own unsigned dylibs), and a microphone entitlement.
- The Info.plist carries `NSMicrophoneUsageDescription` and `NSAudioCaptureUsageDescription`
  (without them macOS kills the app on first mic use), `StudyStashRole` for the role preset, and
  `CFBundleShortVersionString`/`CFBundleVersion` set to `StudyStashVersion` so an installed copy
  can report its own version without running .NET.
- CI tests the engine on macOS, Linux, and Windows, builds and self-tests both apps (a fake
  microphone, the tiny Whisper model, real windows), installs each with its own installer, and
  publishes a release only when `StudyStashVersion` in `Directory.Build.props` is new.

## Security model

- **The library's password** is the only thing standing between its web page (and the laptop's API)
  and anyone on the same network or Tailscale. It's plain HTTP, so it's meant for a private network
  or Tailscale, never the open internet.
- **Claude's own door** (`ClaudeAccess`, `claude.json`) is a separate OAuth 2.1 authorization server:
  authorization code with PKCE, short-lived access tokens, rotating refresh tokens, and dynamic
  client registration for claude.ai and Claude Code. Only token hashes are kept, sign-in attempts
  are rate-limited, and a token made by hand in Settings can be revoked on its own.
- **claude.ai reaches the library only through Tailscale Funnel**, turned on by hand; nothing is
  exposed to the internet unless the student does that themselves.
- **Secrets** (`claude.json`, the library password) are written readable by the owner only.
- **Updates** come only from this repository's GitHub releases, which CI builds after the tests
  pass; `studystash update` installs the newest one, and it happens automatically unless
  `auto_update = false`.

## What's opt-in

Nothing that changes the computer runs unless the student turns it on. Setup asks before installing
Tailscale or Ollama, adding a Windows Firewall rule, or starting the library at login. Canvas stays
off until a course is linked. Letting the AI write files (in chat) is a switch of its own, and every
change it makes is listed with an Undo. AI engines other than the local Ollama model read only what
they're asked about, under the student's own account and plan; nothing is ever sent to this
project or its author.

## The AI, Canvas, chat and undo

- **Providers** (`Core/Ai/Providers.cs`): each AI engine is its own command-line program, run as a
  child process with JSON output, so it keeps the student's own sign-in and plan and the library
  never holds a key — Claude Code (`claude -p`, edits allowed only inside the working folder),
  Codex (`codex exec`, read-only or workspace-write), Antigravity for Gemini (`agy -p`). Ollama
  answers plain questions directly; as an agent it runs through Codex (`--oss`).
- **Canvas** (`Core/Canvas`): a small Chrome extension (`extension/`, embedded in the engine and
  written out for Chrome to load) is a read-only fetch proxy. It asks the library for work
  (`/api/v2/canvas/work`) with its own key, fetches Canvas URLs with the browser's own session, and
  posts the answers back — never a Canvas API token, which many schools disable. The sync is a
  persisted job queue; a read an AI asks for (the MCP Canvas tools) jumps the queue.
- **Chat and history** (`Core/Ai/Chats.cs`, `History.cs`): a chat turn runs the picked AI in the
  library folder with the engine's own MCP server. With edits allowed, the library's text is
  committed first, then exactly the files the turn changed become one commit, which History lists
  and Undo reverts. The library folder's git repository is local only.
- **Capture** asks the AI only for a plan (a class and title per item, as JSON); the engine moves
  the files, so it works with any AI and needs no write permission.

## Diagrams on screen

A ```` ```mermaid ```` fence is read into a `Flowchart` (`Core/Rich/Mermaid.cs`), laid out once away
from the window (`DiagramLayout`, through the app's `SceneCache`) into a colour-free `DiagramScene`,
and drawn by `DiagramView` with a `DiagramPainter` that keeps its outlines, lines and words between
frames. On paper (`NoteView.PageHeight`, the PDF) that's all it is: the still picture. On screen a
`DiagramExplorer` sits over it:

- **What's under the pointer** is found in the scene (`Core/Rich/DiagramHit.cs`: boxes by their
  outlines, arrows within a few pixels of their line, their words, groups' titles), and so is what
  lights up with it and which box an arrow key goes to (the one joined to it that lies most that way
  on the page).
- **Zoom and pan** (`Zoomer`) move and scale the picture as drawn, so the page never re-lays out; in
  a note the wheel still scrolls the page and only ⌘/Ctrl + wheel or a pinch zooms.
- **Folding** (`Core/Rich/DiagramFold.cs`) is a chart-to-chart change: a folded group becomes one
  box with its title and a count, standing where its first box was, and arrows re-attach to it (two
  that end up joining the same boxes become one, with both their words); the folded chart is laid
  out like any other, and the view glides each box from the old scene to the new. A chart of at
  least 12 boxes in two or more groups opens folded.
- **Steps** (`Core/Rich/DiagramSteps.cs`) read a chart from where it starts (a box no arrow points
  to, or a cycle's first box), each box once after every box with a forward arrow to it, a branch to
  its end before the next, a group finished before the walk leaves it; arrows back round a loop are
  the last step's way out.
- **Moments** (`Core/Rich/DiagramMoment.cs`): a designed diagram's `%% Study Stash diagram, from
  12:34` mark, and where a box's words were said, found by matching their stems against the timed
  transcript (rare words count for more).
- The page a diagram is on answers for it through `IDiagramHost` (`DiagramHost.Host`, found by
  walking up from the diagram): the lecture page's `LectureDiagrams` asks in its Ask bar, opens the
  transcript at a line, and plays the recording from a moment. A diagram on no lecture's page (a
  quick answer) offers none of that; the larger window asks the note it was opened from.

Movement follows the system's Reduce motion (macOS) or Animation effects (Windows) setting
(`Platform/Motion.cs`).

## Illustrations

An illustration is an SVG drawing of a physical thing with its parts named the way SVG names
things: each part a group with an id whose first children are a `<title>` (its name) and a `<desc>`
(its line from the lecture), its callouts in `<g id="labels">`, one `<g id="label-{id}">` each
(a leader line from the part, a dot, the name). Because the names live in the SVG, they travel
everywhere the drawing does: `SafeSvg` reads them back as `Parts`, search and Ask read them
(`Passages`), the phone and the Markdown export list them, and a browser shows them on hover.

- **Designing** (`Core/IllustrationDesign.cs`): the diagram designer's brief has a section on when
  a thing deserves a drawing (the lecturer described its parts and where they sit, at some length;
  one view shows it) and its JSON reply may carry `illustrations`: a title, the section, the moment,
  a caption, the subject and view, a layout, and 3 to 18 parts (`id`, `name`, `note`). The plan is
  checked like a diagram. The illustrator is asked once per plan (in parallel with the diagrams'
  checks) with the lecture, the plan, a house palette by material (light, base, shade, outline), the
  order to draw in (base shapes, pieces, fine detail, gradient shading, callouts) and the structure
  above; it answers with the SVG alone. Only engines that draw SVG (`Drawings.FlowchartsAndSvg`, the
  cloud engines) are asked.
- **Checking**: the parts are named in the drawing from the plan (`Illustration.Name`), the drawing
  must clean (`SafeSvg`) and its words (labels, names, lines) must be the lecture's (the diagrams'
  60% rule). `Core/Rich/SvgGeometry.cs` works out where everything is from the markup alone
  (transforms, `use`, paths with their curves and arcs, a text's width from its letters), so
  `Callouts.Tidy` can space a column of crowded labels and bring one back inside the canvas (with a
  straight leader from the same point on its part), and `SvgLint` can say what's still wrong in
  words: drawn too simply (under 120 shapes), a planned part missing, too small or off the canvas,
  no callout, a leader that ends away from its part, labels overlapping or off the edge, words under
  11 units. A drawing with problems goes back once while 200 seconds are left, for just the groups
  that change (`IllustrationDesign.Patch` puts them in place of their namesakes), and the result is
  kept only when it has fewer. Drawing is output-bound (a detailed figure is 25 to 35 KB written
  shape by shape: 8 to 14 minutes for Opus whatever the effort, so it's drawn at low effort,
  `DiagramEngines.DrawEffort`); a pass that plans one tells `AiJobs` it may run 18 minutes in all
  (`IllustrationDesign.Timeout`) instead of the diagrams' 8, and a drawing not done by then is left
  out.
- **Colours**: in light, as written. In dark, an illustration's shapes keep their own colours,
  moved into what a dark page carries (`Core/Rich/SvgColour.cs`, in OKLab: darks lifted clear of
  the dark paper, lights held back from glaring, hue and the order of light to dark kept so shading
  still reads; a near-white with no colour becomes the paper); its words and leader lines are in the
  roles' colours, so they follow the theme like any drawing's.
- **On screen** (`SvgView` with `PartsExplorer` and `PartsChrome`, the flowchart's own chrome):
  `PartMap` draws the drawing again a part at a time (and each label with its part) onto a map at
  about a pixel a unit, away from the window, so what's under the pointer is the part on top there,
  by its real outline, a few units' slop for thin ones. A lit part is drawn again over the dimmed
  drawing with a dilated accent ring; versions without the labels, with only their lines, or of one
  part or label alone are cut from the cleaned SVG (`Illustration`) and drawn as pictures of their
  own, kept until the colours change. Paper, the PDF and exports get the still picture with its
  labels.

## Privacy

- **Everything stays on the student's own computers** unless they pick an AI engine other than
  Ollama, which then reads what it's asked about under their own account. The diagrams engine, on
  automatic, only ever uses an AI the student already sends lectures to (the notes or questions
  engine) or a model Ollama runs on the library's computer, never an Ollama cloud model. Lectures, transcripts,
  and notes live on the library computer; by default, study notes are written by a local model.
  The only outside services are GitHub (update checks) and whichever AI engine the student picked.
  There's no analytics.
- **Recording is the student's responsibility:** consent laws, and their school's own rules on
  recording and sharing lectures.
