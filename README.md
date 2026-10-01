<p align="center"><img src="docs/images/icon.png" width="96" alt="Study Stash"></p>

# Study Stash

Your own lecture library. Record a lecture, and it's transcribed on your computer, filed under the
right class, and turned into study notes — next to your Canvas assignments, and ready to ask about.
Free and open source, for Mac and Windows, and your recordings never leave your own computers.

[![The 50-second demo: a lecture's notes, with a labelled diagram of the organs of the torso](docs/images/demo-poster.png)](https://study-stash-app.web.app/assets/study-stash-demo.mp4)

[Watch the 50-second demo](https://study-stash-app.web.app/assets/study-stash-demo.mp4) (it's narrated, so turn the sound on).

## What it does

- **Records and transcribes on your computer.** Click **Record** in the menu bar (Mac) or tray
  (Windows), or press ⌥⇧R / Ctrl+Alt+R. It transcribes as you go, and the audio never leaves your
  computer. A Mac with Apple silicon or a PC with a graphics card uses Whisper (Metal or Vulkan),
  starting on the compact large-v3 turbo, which keeps up and leaves room for everything else. A
  computer with no graphics card Whisper can use, and 4 fast cores and 8 GB of memory, starts on
  NVIDIA's Parakeet instead (a 2.5 GB download): it's made for the processor, nearly as accurate as
  the compact turbo, and never makes up words over silence; it reads 25 European languages, and a
  lecture in another language uses Whisper. A computer too weak for either gets a lighter Whisper.
  The bigger, more accurate models are a choice in setup and Settings → Recording, where an
  experimental **Tell speakers apart** also marks in a transcript ("Speaker 2:") where a voice other
  than the lecturer's seems to speak; it is careful rather than complete, and can be wrong. While it
  records, a tiny pill shows the time and the level; pause or stop from the menu bar.
- **Writes study notes with the AI you choose.** A summary, key points, definitions and questions
  to review, written by Ollama (free and private) or by Claude Code, Codex or Gemini with the plan
  you already have. Rewrite a lecture's notes with another engine and keep whichever you like.
- **Files every lecture under its class**, working out which from what was said, or using the class
  you picked. Anything it can't place waits in **Unsorted**, and a lecture you don't need can be
  deleted (with Undo).
- **Shows you everything at a glance.** The library opens on **Home**: what's due soon, what's
  coming up, your newest lectures and a card for every class. Each class (or club) has a home of
  its own too: its lectures, what's to hand in, when it next meets, its files, and on Canvas its
  assignments, modules and announcements.
- **Ask about any lecture, class, or all of them.** The Ask bar under a lecture's notes answers
  from your notes and transcripts, with the engine you pick for that question.
- **Finds anything in a second.** The quick panel (⌥Space on a Mac, Alt+Shift+Space on Windows)
  searches lectures, passages in your notes, and classes, and asks your notes a question.
- **Knows what's coming up.** Paste your calendar's link (Google's secret iCal address, iCloud,
  Outlook or your school's) in Settings → Calendars and pick which calendars to share. Upcoming
  classes show in the menu bar, the quick panel and the library, and a lecture recorded during one
  is named after it and filed under its class.
- **Takes your own notes and slides too.** Attach iPad handwriting, slides, PDFs or photos to a
  lecture or class (drag them in). Their words, handwriting included, are read on your computer
  and go into the lecture's notes, Ask and Claude.
- **Brings in Canvas.** What's due across your classes, each assignment's instructions, rubric,
  your submission and feedback, module files and pages, announcements, quizzes and discussions.
- **Comes with you on your phone.** Your library serves a phone app over Tailscale: read your
  notes, see what's coming up, search, and add photos and PDFs to a lecture from your iPhone or
  iPad. Settings → Your library → Phone shows a code to pair it ([docs/phone.md](docs/phone.md)).
- **Lets Claude read your library** over MCP: Claude Code, Claude Desktop and claude.ai.
- **Looks at home on your computer.** A native Mac look (Liquid Glass) and Windows 11's, light and
  dark, with ten colour themes in Settings → Appearance.
- **Keeps itself up to date**, quietly, when nothing is recording.

![The library window: a class's lectures, a lecture's notes, and the Ask bar](docs/images/library-window.png)

<p align="center">
  <img src="docs/images/menu-bar.png" width="49%" alt="The menu bar dropdown: Record, recent lectures and search; and while recording">
  <img src="docs/images/quick-panel.png" width="40%" alt="The quick panel: search results, and an answer from your notes">
</p>

![Canvas in Study Stash: what's due, and an assignment with its rubric](docs/images/canvas-due.png)

## One computer or two

Study Stash is one app with two jobs, so you can use it the way that suits you:

- **One computer.** Your laptop records, keeps the library and writes the notes itself. Nothing
  else to set up. Notes are written while it's on.
- **A laptop and a library.** Your laptop records; a computer that stays on at home (a Mac mini,
  an old laptop, any Mac or PC) keeps the library, writes the notes and syncs Canvas, even while
  your laptop is asleep. The laptop reaches it at home or, anywhere, over
  [Tailscale](https://tailscale.com).

```
your laptop                                  your library (this laptop, or a computer at home)
┌────────────────────────────┐   lectures   ┌──────────────────────────────────────────────┐
│ record → Whisper (local)   │ ───────────▶ │ study notes (your AI) → filed under its class │
│ ask, search, Canvas, notes │ ◀─────────── │ Canvas mirror · Claude/MCP · search           │
└────────────────────────────┘              └──────────────────────────────────────────────┘
```

## Install

One download for every computer, on the [latest release](https://github.com/Joseph-Rus/study-stash/releases/latest):

| Mac | Windows |
|---|---|
| [Study-Stash.dmg](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash.dmg) | [Study-Stash-Setup.exe](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash-Setup.exe) |

It's the same app either way; what a computer is for is chosen when you first open it, not when
you download it.

- **Mac:** open the DMG and drag **Study Stash** into Applications. Study Stash isn't signed with a
  paid Apple Developer ID yet, so the first time macOS asks: click **Done**, then **System Settings
  → Privacy & Security → Open Anyway**. Allow the microphone when it asks.
- **Windows:** run the Setup.exe. It installs for your account only, no admin rights needed. If
  Windows says "Windows protected your PC", click **More info**, then **Run anyway**.

Then open **Study Stash**. See [Setting up](#setting-up).

## Setting up

The first time Study Stash opens, it asks which AI will set it up with you:

- **Claude** (through Claude Code, from Anthropic): needs a paid Claude plan, Pro or Max.
- **ChatGPT** (through Codex, from OpenAI): needs a paid ChatGPT plan, Plus or higher.

Pick one, press **Install** (Study Stash runs its maker's own installer, for your account only, no
admin password; nothing is downloaded until you press it, and it's skipped when it's already
there), then **Open sign-in page** to sign in on Claude's or ChatGPT's own page. Study Stash never
sees your password or your sign-in. A tiny test message then checks your plan includes it.

After that your AI walks you through the rest in a chat, with a checklist beside it: one computer or
two, the microphone, the transcription model, your classes, Canvas, and starting at login. It can
only use Study Stash's own setup tools (no commands, files or web), and anything that changes your
computer happens only when you press the button in its card. Close the window part-way and the chat
picks up where it left off. The chat uses a little of your plan, and your AI then writes your
notes and answers your questions (you can switch to a free local model in Settings any time).

**No subscription?** "Use a free model on this Mac (Ollama)" on the first screen goes to setup by
hand, with Ollama picked for your notes. **Set up by hand** is on every screen too, and
**Settings → General → Run setup** runs either again.

Setup by hand asks how you'll use it:

- **Just this computer:** check the microphone, download the transcription model, choose who
  writes the notes, Canvas, your classes, and starting at login (recommended, so your library runs
  whenever you're logged in).
- **This is my library:** a password for your library, who writes the notes, Canvas, your classes, and
  starting at login. It ends by showing the address and password to connect a laptop.
- **This is my laptop:** find your library (or type its address and password), check the microphone,
  download the transcription model, Canvas, and your classes.

Changed your mind, or your plans changed? Switch any time in **Settings → Connection → This
computer**: a laptop can also become your library (bringing over whatever it was connected to
before), and a library can become a laptop, sending its own lectures to the new one first so
nothing is left behind. Nothing to reinstall or redownload.

Or install from a terminal:

```bash
# Mac (Terminal)
curl -fsSL https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.sh | sh
```
```powershell
# Windows (PowerShell)
irm https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.ps1 | iex
```

## AI engines

Pick who writes your notes, who draws their diagrams and who answers your questions in setup or in
**Settings → Your library → AI engines** — one engine for everything, or a different one for each
job. The engines run on your library's computer (with one computer, that's your laptop). Without an
engine, lectures are still filed and keep their transcripts. With Claude Code, Codex or Gemini, a
lecture's transcript (and, for its diagrams, its notes) goes to that AI under your own account;
with Ollama nothing leaves your computer.

**Ollama** — free, and nothing leaves your computer. Install it from [ollama.com](https://ollama.com),
and Study Stash offers to download a model.

**Claude Code** — uses your Claude plan. Install it, then run `claude` once in Terminal and sign in:

```bash
curl -fsSL https://claude.ai/install.sh | bash      # Mac (or: brew install --cask claude-code)
```
```powershell
irm https://claude.ai/install.ps1 | iex             # Windows (PowerShell)
```

**Codex** — uses your ChatGPT plan. Install it, then run `codex` once and choose **Sign in with
ChatGPT**:

```bash
npm install -g @openai/codex                        # Mac or Windows (or on a Mac: brew install --cask codex)
```

**Gemini** — the Gemini CLI, signed in with your Google account.

Setup's notes step shows these commands with a Copy button, opens Terminal for you, and has
**Check again** to pick the engine up once it's installed and signed in.

**Draws diagrams** is a pick of its own. Once a lecture's notes are written, a stronger model reads
its timed transcript and the notes, decides whether the lecture teaches anything a picture makes
clearer (a process, a cycle, a pathway, a decision rule, a hierarchy, a structure, states and what
moves between them, an exchange between parties, a timeline, a topic's themes, a comparison, or
something spatial like the forces on an object), and if it does, designs it from what the lecturer
said and puts it at the end of the section it illustrates, with a title, a caption that says what
to notice, and the moment of the lecture it comes from. It picks the kind that fits: a flowchart
(a big topic as up to 36 boxes in groups, each box carrying the lecture's numbers and conditions in
smaller words), a state diagram (an automaton's accepting states double-circled), a sequence
diagram, a timeline or a mind map. A lecture gets one per idea worth drawing — up to four for a long
one — one under about three minutes gets none, and so does a lecture of discussion, admin or
definitions.

- **Automatic** (the default) uses the strongest engine that already reads your lectures — the one
  that writes your notes or answers your questions — trying Claude Code (Opus, high effort), then
  Codex (high effort), then Gemini (3.1 Pro); otherwise the biggest model Ollama runs on the
  library's computer (never an Ollama cloud model). So a lecture never goes to an AI it wasn't
  already going to.
- **An engine of your own choice** uses its strongest model; one that isn't signed in or is over
  its limit falls back to Ollama when **Use Ollama instead** is on, as the notes do.
- **Same as notes** has the notes engine draw them as it writes, the way it did before 0.10.1;
  **Off** leaves them out.

A diagram that doesn't draw goes back once to be fixed and is otherwise left out, as is one with
too many boxes or words the lecture never said. Each one kept is laid out at the notes' width and
looked over; one that would look wrong (too wide to read, arrows crossing, a sentence in a box, a
big chart with no groups) goes back once with what's wrong, while there's time, and its redesign is
used only when it's better. If the diagrams can't be designed (the engine is busy, over its limit,
or takes longer than 8 minutes), the notes are kept exactly as written. The library's log says
what was drawn and redesigned, or why nothing was.

## Notes

A lecture's notes write formulas and diagrams as their own engine can, and the app draws them, in
the notes, in a quick answer and in the Ask chat:

- **Formulas** are LaTeX (`$...$` inline, `$$...$$` on their own line), typeset in the app's own
  type, light or dark. One CSharpMath can't typeset shows its plain source instead, calmly.
- **Diagrams**: a process, cycle, pathway or hierarchy comes back as a Mermaid flowchart
  (` ```mermaid `) and is drawn natively, in the theme's colours — a big one in groups, laid out as
  columns of its groups with square arrows between them; Mermaid state diagrams (automata too),
  sequence diagrams, timelines and mind maps are drawn natively as well; something spatial (a
  structure, a physics setup, a circuit) comes back as a sanitised SVG. They're designed after the notes by the
  engine that **Draws diagrams** (see [AI engines](#ai-engines)), each under a bold title with a
  caption and the moment of the lecture it comes from; rewriting a lecture's notes designs them
  again rather than adding more. Either kind opens larger on a click, and a diagram Study Stash
  can't draw shows its source with a plain reason instead of failing.
- **Download**: a lecture's "Download as Markdown…" (or a whole class's) saves a `.md` file that
  Obsidian, Typora and VS Code all open well — formulas stay LaTeX, a Mermaid diagram is saved
  again as an SVG beside it, and an SVG diagram becomes an image link to its own sanitised file.
  "Download as PDF…" saves the notes as the app draws them, ready to print: headings, tables, typeset
  formulas and diagrams as sharp vector drawings with the text selectable, light on white whatever
  the app's look, on Letter paper where that's the local size and A4 elsewhere, with page numbers.

## Canvas

Study Stash reads Canvas through Chrome with your own sign-in — no Canvas password or token — and
only reads; nothing on Canvas changes. On your library's computer:

1. **Settings → Your library → Canvas** (or the Canvas step in setup): type your school's Canvas
   address.
2. Click **Add to Chrome**. It opens Chrome's extensions page and shows the extension's folder.
   Turn on **Developer mode**, click **Load unpacked**, and pick that folder — or drag the folder
   onto the Extensions page. On a Mac the folder is **Study Stash → Chrome extension** in your home
   folder (`~/Study Stash/Chrome extension`), where Chrome's window can see it; on Windows it's
   `chrome-extension` in `%USERPROFILE%\.study-stash`.
3. Sign in to Canvas in Chrome, then click **Find my courses** and match each class to its course.

Canvas then syncs about once an hour while Chrome is open (it can stay minimized): what's due,
assignments with rubrics, your submissions and feedback, modules, files, pages and announcements,
filed into each class's folder.

## Claude and MCP

Your library speaks [MCP](https://modelcontextprotocol.io), so Claude can read your lectures, notes
and Canvas work (read-only). Set it up in **Settings → Your library → AI tool access**:

- **Claude Code or Codex**, on your computers: copy the setup from AI tool access and paste it into
  a terminal (Claude Code) or `~/.codex/config.toml` (Codex).
- **Claude Desktop and claude.ai**: turn on internet access (it uses a
  [Tailscale Funnel](https://tailscale.com/kb/1223/funnel), protected by signing in with your
  library password), then in Claude add a custom connector named **Study Stash** with the address
  `https://<your library's name>.<your tailnet>.ts.net/mcp`.

## Updates

Installed copies update themselves: they check for a new release a few minutes after starting and
every 6 hours, and install it when nothing is recording or being transcribed. To update right
away, click **Update now** in Settings. On a Mac an update swaps in the new **Study Stash.app**; on
Windows it runs the new Setup.exe quietly. Every download is checked against the release's
`SHA256SUMS.txt` first.

## Uninstall

**Mac:** quit Study Stash and drag it from Applications to the Trash. **Windows:** Settings → Apps →
**Study Stash** → Uninstall. Your lectures and settings stay where they are; only the app goes.

[docs/claude-connector.md](docs/claude-connector.md) walks through adding Study Stash as a custom connector in Claude,
what Claude can read, troubleshooting, and how each requirement of Claude's connectors is met and tested.

## Where your data lives

Everything is under `~/.study-stash` (or `--home`, or the `STUDYSTASH_HOME` environment variable):
your settings, the lecture database, and the notes themselves as plain Markdown files in a folder
per class, which you can open, back up or sync however you like. On a Mac the Chrome extension's
folder is the one exception: `~/Study Stash/Chrome extension`, because Chrome's Load unpacked window
doesn't show folders whose names start with a dot.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com), and [Node](https://nodejs.org) (22 or later) for the
phone app in `web/`.

```sh
dotnet build engine/StudyStash.slnx
dotnet test engine/StudyStash.slnx
dotnet run --project engine/src/StudyStash.App -- --home /some/temp/dir
(cd web && npm ci && npm test && npm run build)   # the phone app, into web/dist, which the app then carries
```

`macos/build-app.sh` builds "Study Stash.app" and `Study-Stash.dmg` (with Xcode's command line tools), and
`windows/build.ps1` `Study-Stash-Setup.exe`; both build the phone app into it first. `engine/README.md` has more on the projects and tests.

Releases come from CI (`.github/workflows/ci.yml`). Every change is tested once, when it reaches
`main`: on macOS, Linux and Windows, with both apps built, self-tested and installed. Branches and
pull requests aren't tested on their own; to prove one first (say, Windows-only code), start a run
by hand with **Actions → ci → Run workflow** or `gh workflow run ci --ref <branch>`. To ship a
release, bump `StudyStashVersion` in `engine/Directory.Build.props` and merge to `main`; CI
publishes `Study-Stash.dmg`, `Study-Stash-Setup.exe` (and copies under the old installers' names, so
older copies still update) and `SHA256SUMS.txt`. Code that already passed isn't tested twice: a
push that only changes the version on top of a commit that passed, or a merge of a branch whose run
passed on the very same code, just builds and publishes.

## Support Study Stash

Study Stash is free and open source, and two students build it with Claude. If it saves you time,
you can [buy us more Claude usage](https://ko-fi.com/studystashteam) on Ko-fi, and it goes into
building the next version.

## License

[MIT](LICENSE). No warranty: check your notes against the lecture before relying on them, since AI
makes mistakes. Record only where you're allowed to — many schools and places require everyone's
consent. See [SECURITY.md](SECURITY.md) to report a security problem.

Lectures are written down by [Whisper](https://github.com/openai/whisper) models (MIT, from OpenAI,
run with [Whisper.net](https://github.com/sandrohanea/whisper.net)) or, if you choose it, by
[NVIDIA Parakeet TDT 0.6B v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)
([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)), converted to ONNX by the
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) project (Apache-2.0), which runs it. To tell
voices apart, when you turn that on, it uses
[pyannote's segmentation 3.0](https://huggingface.co/pyannote/segmentation-3.0) (MIT) and NVIDIA's
[TitaNet small](https://catalog.ngc.nvidia.com/orgs/nvidia/teams/nemo/models/titanet_small)
(CC BY 4.0), also converted by sherpa-onnx. The models are downloaded when you pick them; none is
bundled.

Notes and diagrams are drawn with [CSharpMath](https://github.com/verybadcat/CSharpMath) (MIT,
bundling the [Latin Modern Math](https://www.gust.org.pl/projects/e-foundry/lm-math) font under
the GUST font licence), [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) (MIT), and
[Microsoft Automatic Graph Layout](https://github.com/microsoft/automatic-graph-layout) (MIT).
