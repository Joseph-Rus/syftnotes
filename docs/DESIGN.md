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
                                                  then, after filing: diagrams (DiagramJobs) into the notes
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
   names and terms stay spelled the same. With Settings → Recording's "After class" the lecture is
   only recorded (`Lecture.AfterClass`: nothing is transcribed and no model is loaded while it records
   or is paused) and the same pieces are transcribed from its WAV once it stops.
   `engine/tools/TranscribeBench` measures what each way costs on a real lecture.
2. **Pick a class, or don't.** Record lets the library sort the lecture unless the student picks a
   class in the dropdown; a lecture recorded with no class is sent with an empty `folder`.
3. **Send it.** `LectureSender` POSTs the lecture (`Lecture.Payload()`) to `/api/ingest` once the
   library is reachable, then polls `/api/notes/{id}/status` until it's filed. Nothing is lost if
   the library is asleep or on another network: the laptop keeps trying.
4. **Queue and write.** `LibraryWeb.Ingest` turns the payload into a `Meeting` (`Wire.MeetingFromJson`)
   and enqueues it (`Store.Enqueue`). `Pipeline.ProcessAsync` then, in the background: writes study
   notes from the transcript with the picked AI engine (told to draw none of their own when a designer
   will add them), sorts it into a class (the recorded class or
   a title match wins outright; otherwise the AI reads the notes against each class's name, other
   names, what it covers and its Canvas course's name, and picks one with a strict schema, or it goes
   to Unsorted), and saves it as Markdown under `<library folder>/<Class>/`: the lecture is **filed**
   (status `done`), readable in the app, on the phone, in Ask, search and a Markdown download.
5. **Diagrams, after.** Filing hands the lecture to `DiagramJobs` (`Pipeline`'s `filed`, with
   `AiJobs.TakeDiagramsFollow` saying whether a designer adds them). A job is a file in
   `home/diagram-jobs`; the queue runs one at a time, oldest first, never while notes are being written
   (`Pipeline.Writing`), and a pass on Ollama stops for notes that come in and starts again after. It
   picks the designer as the student's diagrams pick says now (`AiJobs.DesignerAsync`: nobody when
   rich notes are off, `AiSettings.Kinds()` — the **Rich notes** switch and one for diagrams, formula plots
   and drawings, in `ai.json` — and the pick carries which kinds are on, so `DiagramDesign.Prompt` leaves a
   switched-off kind out of the brief, `Read` drops one it's given anyway, and no illustrator or composer is
   made when drawings are off; and `AiSpeed` says how Claude Code is run, below), and runs the
   diagram pass (`AiJobs.DesignDiagramsAsync`, a hard stop past its own 8 or 18 minutes:
   `DiagramDesign`: it reads the timed transcript and the notes, decides whether anything is clearer
   as a picture and which kind fits — a flowchart, in groups for a big topic, a state or sequence
   diagram, a timeline or a mind map (`Mermaid` reads them all into one model; `DiagramLayout` lays
   each out), or a plot of a formula whose shape is the point (`Plot`, checked by `PlotLint`) — answers in JSON that's checked strictly, and each diagram it designs goes at the end
   of its section, repaired once or left out if it doesn't draw or isn't true to the lecture;
   `DiagramLint` then lays each out at the notes' width, and one that would look wrong goes back once,
   its redesign kept only when better; a lecture that describes a physical thing at length may also
   get an illustration (`IllustrationDesign`: planned in the same reply, drawn by the same engine
   from the plan, its parts named, its callouts spaced by `Callouts`, looked over by `SvgLint`; see
   [Illustrations](#illustrations)); a pass that fails leaves the notes exactly as filed). The job
   remembers the notes it designed for (`Notes.Fingerprint`) and the diagrams once they're designed,
   so a restart just puts them in; a pass cut off by a restart runs again, `MaxTries` (3) at most.
   `Store.AddDiagrams` puts them in under the store's lock (`DiagramDesign.Place`): earlier passes'
   marked diagrams come out, each new one goes at the end of its heading's section while that
   heading is still there (else it's skipped), and nothing else moves; the note file is replaced in
   one step (written beside it, then moved over it) — written afresh when nobody touched it, or, when
   it was edited since filing, with the diagrams put into its `## Summary` in place and every other
   line as it was. Notes filed again (a rewrite used, notes written afresh) replace the job, and the
   older pass's result is dropped. `GET /api/v2/ai/rewrite/{id}` says `"diagrams": "adding"` meanwhile:
   the app's `AiNotesModel` polls it, shows it on the byline, and sets the new notes on the same
   `NoteView`, which keeps every unchanged block (a diagram's pins and folds) and the reader's place
   (`NoteView.HoldPlace`). The laptop sees them the same way, over the library's API; the phone and
   Ask read the notes afresh. **Rewrite notes with** (`Rewrites`) writes the draft without diagrams
   (`AiJobs.WriteNotesPlannedAsync`) and queues them when it's used.
6. **Read it.** `studystash mcp` (stdio, for Claude Code or Claude Desktop on the library's own
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
| `diagram-jobs/` | The library's diagrams still to come, one file per lecture, so a restart picks them back up. |

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
  be `lipo`-merged, so this is the trick that makes both look like one program. By default it's signed ad hoc
  (no paid Developer ID) with the hardened runtime, `disable-library-validation` (so it can still
  load its own unsigned dylibs), and a microphone entitlement. A release whose repository has the
  signing secrets re-signs it after the build with a Developer ID, without `disable-library-validation`,
  notarizes and staples the app and the DMG, and signs Windows' exe, dlls, Setup.exe and uninstaller
  (`macos/sign-release.sh`, `windows/sign-release.ps1`, [signing.md](signing.md)).
- The Info.plist carries `NSMicrophoneUsageDescription` and `NSAudioCaptureUsageDescription`
  (without them macOS kills the app on first mic use), `StudyStashRole` for the role preset, and
  `CFBundleShortVersionString`/`CFBundleVersion` set to `StudyStashVersion` so an installed copy
  can report its own version without running .NET.
- CI tests the engine on macOS, Linux, and Windows and self-tests both apps (a fake microphone, the
  tiny Whisper model, real windows) for every change that reaches `main`. A release (a
  `StudyStashVersion` in `Directory.Build.props` that has no tag yet) also builds both installers,
  installs and updates the Windows one, runs the Mac app natively on an Intel Mac, and only then
  publishes. Code that already passed on the same tree isn't tested twice. The shape is described at
  the top of `.github/workflows/ci.yml`.

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
- **Speed** (`Core/Ai/AiSpeed.cs`, `ai.json`'s `speed`, Settings → AI engines → Claude Code speed): how
  Claude Code writes the notes (`AiJobs` for the `notes` job and a rewrite) and designs the rich notes
  (`DiagramEngines.PickAsync`, carried by `DiagramPick`, so the illustrator and the revise round get it too);
  sorting and answers never change, and neither does any other engine. *Standard* is as it was (the notes with
  the model picked or Claude Code's default, the designer `opus` at `high`). *Fast* is Claude Code's fast mode:
  `AiRequest.Fast` adds `--settings '{"fastMode":true}'` to that one `claude -p` run (nothing is saved in the
  student's own settings; Claude Code 2.1.205 or later, else it's ignored), with the model named `opus`, because
  fast mode is Opus's (Opus 5.5, 5 and 4.8 only) and turning it on for another model switches it; a notes model
  picked as Sonnet or Haiku stays as picked, and the composer (Sonnet, low) never gets it. The run's `system`
  event says `fast_mode_state` (`on`) and its `usage` says `speed` (`fast`). It bills at a higher rate and, on a
  subscription plan, only from usage credits (which the account must have turned on); an account that can't use
  it runs at normal speed rather than failing (Claude Code itself retries a fast request it can't have at
  standard speed), and a designer run the CLI turns down is asked again as the student set it up, like any
  strongest-model run. *Quick* is `sonnet` at `low` effort for the notes and `medium` for the
  designer. The notes' byline names the model that wrote them (`Claude opus`, `Claude sonnet`).
- **Rich notes** (`AiSettings.RichNotes`, `RichDiagrams`, `RichPlots`, `RichDrawings`; `RichKinds`): off,
  `AiJobs` tells the notes to draw nothing and queues no diagram pass, so nothing extra is asked of the AI. The
  designer's brief is built from the kinds that are on (`DiagramDesign.Prompt`'s `kinds`: with all on it is
  byte for byte what it was), a reply with a kind that's off is cut in `Read`, and with drawings off no
  illustration is planned or drawn. `AiSettings.SetRich` keeps them consistent (turning off the last kind turns
  rich notes off; turning them on again from a stored `diagrams: off` goes back to automatic). The laptop reads
  and changes all of it over `/api/v2/ai/engines` and `/api/v2/ai/defaults` (`rich`, `rich_diagrams`,
  `rich_plots`, `rich_drawings`, `speed`; `AiOverview.Rich` and `.Speed`, absent from an older library, whose
  rows the pane then hides), and the library's own web settings page has the same switches.
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

## Plots

A ```` ```plot ```` fence is a few plain lines (`Core/Rich/Plot.cs`): `title`, the axes (`x -6 to 6
"z"`), sliders (`param k = 1 from 0.2 to 5 "steepness"`), then what's drawn — curves (`σ(z) = 1/(1 +
e^(-k z))`, `y = …`, `x = …`), `curve (x(t), y(t)) for t from …`, `bars|stems|dots|steps P(k) = …
for k from 0 to n` (a term may use earlier ones: recurrences), `points`, `point`, `label`, `shade f
[and g] from a to b`, `tangent f at a`, `secant`, `vector`, `field (P, Q)`, `heat|contour L(u, v) =
…`, `descent L from (u0, v0) rate η steps n` and `matrix [[a, b], [c, d]] morph`; a colour, `dashed`
or `dotted`, then a `"label"` (whose `{…}` is worked out live: `{area}`, `{slope}`, `{det}`, `{k}`).
It's read strictly (a mistake names its line, which is what a repair is sent back with) and written
back canonically, each formula as written.

- **Formulas** (`Core/Rich/PlotExpr.cs`) are read by a hand-written parser into a small tree that
  only does arithmetic: no code runs. It reads what lecturers write (`2x`, `x²`, `e^{-x^2/2}`, `σ`
  or `sigma`, `\frac`, `\sqrt`, `|x|`, `n!`, `sin 2x`, `kx` as k·x when both are names it knows,
  `f'(x)`), with `if`, chained comparisons, `sum`/`prod` over a whole-number range, `choose`,
  `gamma`, `erf`, `Phi`. Limits keep it safe: 400 pieces and 40 levels an expression, 50 000 steps
  an evaluation (a sum's terms, a call to another curve), a frame's own budget across all of them
  (`PlotEnv.FrameLeft`), sequences worked out once per slider setting, curves calling each other in
  a circle refused. Past a limit a value is undefined, never a hang.
- **Sampling** (`PlotSampler`) halves a stretch until the line between samples is within a fifth of
  a pixel of the curve, breaks where it's undefined or jumps (no line across an asymptote or a
  step), and keeps what's far off the screen to its two ends. Areas are Simpson's rule, slopes a
  centred difference, gradient descent exact steps, contours marching squares, eigenvectors closed
  form.
- **Layout** (`PlotLayout`) makes one colour-free `PlotScene` for the app, paper, the SVG
  (`PlotSvg`, the download's picture and the phone's) alike: ticks at 1, 2 or 5 × 10ⁿ (or π), the
  series in a fixed, colour-blind-checked order (`PlotPalette`), a legend for two or more, labels
  placed clear of each other and of the lines. A slider redraws it by laying it out again (well
  under a millisecond for a curve, a couple for a heat map at a drag's coarser detail).
- **On screen** `PlotView` holds a `PlotCanvas` (drawing, crosshair readings, pins, dragging a point
  that's a slider, zoom by working the plane out again), `PlotSlider`s, and the diagrams' own kind
  of toolbar, actions and strip; it asks through the same `IDiagramHost`. `PlotWindow` opens it
  larger. `PlotFormula` puts **Show what this looks like** by a displayed formula, and Ask's prompt
  carries the plot format when a question asks to see a shape (`PlotDesign.AsksForPlot`).
- **The designer** is taught the format and a worked example of each kind (`PlotDesign.Brief`); a
  plot it designs is grounded by its title and labels like any diagram, then `PlotLint` works it out
  at its sliders' starting values and ends and sends it back once with what's wrong (undefined
  almost everywhere, flat, off its axes, a slider that changes nothing or takes a curve off the
  plot, a point outside it, too many curves).

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
  above; it answers with the SVG alone. This is for a plan the parts library can't compose (below).
  Only engines that draw SVG (`Drawings.FlowchartsAndSvg`, the cloud engines) are asked, to compose
  or to draw.
- **Composing from parts, before drawing** (`Core/Rich/PartsLibrary.cs`, `Core/Rich/PartsScene.cs`,
  `Core/IllustrationDesign.Compose.cs`): most subjects aren't drawn at all. `PartsLibrary.xml` (an
  embedded resource, about 300 parts, 4 MB, built by `engine/tools/PartsImport`; sources and licences
  in `docs/parts-sources.md`) holds ready-made parts, each an SVG symbol already through `SafeSvg`
  (and through it again when it's first used: a part it refuses is never drawn), in the house
  palette (every colour moved onto the nearest material ramp, in OKLab), with a name, tags, a
  category, its view and real size, a point well inside it, **regions** (its shapes for the left
  ventricle or the USB port, grouped as `r-{id}` without changing what's drawn over what) and
  **ports** (a burette's tip, a board's pin 13, a rack's U slots). The app shortlists parts for a
  plan by its words (BM25 over names, tags and regions; no model) and composes only when they seem
  to cover 40% of the planned parts. The composer (the designer's engine with a fast model, Claude
  Code's Sonnet, at low effort: `DiagramEngines.ComposeModel`) gets the plan and a catalogue of at
  most 40 parts and answers with a scene of 1 to 2 KB: parts placed by middle and width (or a port
  put on another's port, or at the scale of a part drawn to scale), turned or mirrored; at most 12
  simple shapes in house materials for what no part shows (an ellipse, a rectangle, a blob, a band
  for a tendon or a wire, an arrow); each label's target (a part, a region, a port, a shape), with an
  optional short fact. `PartsScene.Read` takes it strictly (only shortlisted parts, finite numbers in
  range, references to things that exist; two labels on one thing settled by its name), and
  `PartsScene.Draw` draws it the same way every time: each part a safe copy of its library drawing
  under one matrix (a part used twice written once and copied), labelled regions renamed to the
  plan's ids with their title and desc, a small ring for a labelled point with no shapes, the art
  scaled to fit, labels in columns either side (above and below a wide thing, its ends' labels in
  columns), each column in the order its points sit, leaders bent at the art's edge; a big shape's
  label points just inside its edge, clear of the others; a labelled thing too small to point at is
  drawn a little bigger. The same checks as a drawing follow (safe, the lecture's words, `SvgLint`
  without "drawn too simply"), and it's kept only when it labels 70% of the plan. CC BY parts are
  credited in its caption. A plan composed before is drawn again from its scene (`ComposedScenes`:
  in memory and in `illustration-scenes/` under the library's home, one small JSON a plan, read back
  through `PartsScene.Read` like any answer). When it can't be composed (no parts, the composer says
  no, too little labelled), it's drawn as above. Composing took 5 to 22 seconds where drawing took
  3 to 20 minutes.
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
  `DiagramEngines.DrawEffort`); a pass that has to draw one tells `AiJobs` it may run 18 minutes in
  all (`IllustrationDesign.Timeout`) instead of the diagrams' 8 (a pass whose illustrations are all
  composed never does), and a drawing not done by then is left out.
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
