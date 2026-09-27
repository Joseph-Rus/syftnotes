# Study Stash

Your own lecture library: record on your laptop, and it's transcribed, filed under the right
class, and turned into study notes on your own always-on computer. Free and open source, and
everything stays on your own computers.

Study Stash is one app for both jobs, on Mac and Windows:

- **Your laptop** (the computer you carry to class): a menu bar (Mac) or tray (Windows) app.
  Click **Record**, and it listens to the microphone and transcribes what it hears locally, with
  Whisper large-v3 (Metal on Apple silicon, Vulkan or the CPU on Windows) — the audio never leaves
  the laptop. It looks at your timetable to know which class is on, then sends the lecture to your
  library once it's reachable. A quick panel (⌥Space on a Mac, Alt+Shift+Space on Windows) searches
  your lectures from anywhere, and the full window browses classes, lectures, notes, Canvas work,
  and lets you ask an AI about any of it.
- **Your library** (a home server: a Mac mini, an old laptop, any computer that stays on): run
  `StudyStash serve`, and it stores each lecture under its class, writes study notes with an AI
  engine, mirrors your Canvas courses through a small Chrome extension, and serves a web page and
  MCP so Claude can read your lectures too.

```
your laptop                                     your library
┌───────────────────────────┐  POST /api/ingest  ┌──────────────────────────────────────┐
│ record → Whisper (local)   │ ──────────────────▶│ queue → study notes (your AI engine)  │
│  ↳ filed by the timetable  │   over Tailscale    │ → sort into a class → Markdown file   │
└───────────────────────────┘                     │ web page, Claude/MCP, Canvas mirror    │
                                                   └──────────────────────────────────────┘
```

## 1. Set up the library's computer

On the computer that keeps your library (a Mac mini, or any Mac or Windows PC that stays on),
download its installer from the [releases page](https://github.com/Joseph-Rus/study-stash/releases/latest):

| | |
|---|---|
| **Mac** | [Study-Stash-Library.dmg](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash-Library.dmg) |
| **Windows** | [Study-Stash-Library-Setup.exe](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash-Library-Setup.exe) |

Or, from a terminal, one line downloads and installs it for you:

```bash
# Mac (Terminal)
curl -fsSL https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.sh | sh -s -- library
```
```powershell
# Windows (PowerShell)
$env:STUDYSTASH_ROLE='library'; irm https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.ps1 | iex
```

- **Mac:** open the DMG and drag **Study Stash** into Applications. macOS asks once before opening
  an app from the internet that isn't from the App Store (Study Stash isn't signed with a paid
  Apple Developer ID): click **Done**, then **System Settings → Privacy & Security → Open Anyway**.
- **Windows:** run the Setup.exe. It installs for your account only, no admin rights, and adds
  **Study Stash** to the Start Menu. Since it isn't signed, Windows may say "Windows protected your
  PC": click **More info**, then **Run anyway**.

Open **Study Stash** and click **Set Up**: it downloads the Whisper model that transcribes
lectures, has you name your library and pick a password, and lets you add your classes (you can
always add more later). When you finish, the app shows your library and the address and password
to connect your laptop.

**Coming from 0.4.x?** Install the new app the same way — your lectures and settings stay right
where they are.

## 2. Connect your laptop

This is the computer you record lectures on. Download its installer the same way:

| | |
|---|---|
| **Mac** | [Study-Stash-Laptop.dmg](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash-Laptop.dmg) |
| **Windows** | [Study-Stash-Laptop-Setup.exe](https://github.com/Joseph-Rus/study-stash/releases/latest/download/Study-Stash-Laptop-Setup.exe) |

```bash
# Mac (Terminal)
curl -fsSL https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.sh | sh
```
```powershell
# Windows (PowerShell)
irm https://raw.githubusercontent.com/Joseph-Rus/study-stash/main/install.ps1 | iex
```

Install it the same way as the library (Mac: drag into Applications, then **Open Anyway**;
Windows: run the Setup.exe, then **Run anyway**). The first time it records, it asks for the
microphone — say yes (it may ask again after an update, since an ad-hoc signed app can't remember
across one). Open **Study Stash**, click **Set Up**, and type the library's address and password
from step 1 (or find them again under **Settings → Connect a laptop** on the library's page).

## Updates

Both computers update themselves: 10 minutes after it starts, then every 6 hours, the app checks
for a new release and installs it as soon as nothing is recording, paused, or being transcribed,
then restarts on the new version. Turn this off with `auto_update = false` in `client.toml`; to
update right away, run `studystash update`, or click **Update now** in Settings.

On a Mac, an update downloads the release's DMG and swaps in the new **Study Stash.app**. On
Windows, it downloads the new Setup.exe and runs it quietly, which closes and reopens the app.
Either way the download is checked against the release's `SHA256SUMS.txt` first, and only an
installed copy updates itself — a build folder, or `dotnet run`, never calls GitHub.

Releases come from CI (`.github/workflows/ci.yml`): every push tests the engine on macOS, Linux,
and Windows, builds and self-tests both apps (a fake microphone and the tiny Whisper model prove
each one actually transcribes), and installs each with its own installer. To ship a release, bump
`StudyStashVersion` in `engine/Directory.Build.props` and merge to `main`. When everything passes,
CI tags it and publishes the four installers plus `SHA256SUMS.txt`, and both computers pick it up
within the next 6 hours.

## Uninstall

**Mac:** quit Study Stash and drag it from Applications to the Trash. **Windows:** Settings →
Apps → **Study Stash** → Uninstall. Either way, your lectures stay in your home folder (the
library's) and in `Documents\Study Stash` (the laptop's) — uninstalling only removes the app.

## AI engines

Study notes and sorting are written by whichever engine you pick in Settings: Ollama, running
locally and for free, or Claude Code, Codex, or Gemini's CLI on the library's computer, signed in
with your own account and plan. Pick one engine for everything, or a different one for notes,
sorting, or answering questions. Without an engine, a lecture is still filed by its class and
title, and keeps its transcript without study notes.

## Canvas

Settings → Canvas connects your school's Canvas with a small Chrome extension (in `extension/`)
that reads Canvas with your own sign-in — no Canvas API token, which many schools turn off — and
hands what it reads to the library. The library mirrors each class: assignment instructions and
rubrics, your submissions with their scores and comments, module files and pages, announcements.

## Claude and MCP

Your library speaks [MCP](https://modelcontextprotocol.io), so Claude can read your lectures and
notes directly:

- **Claude Code or Claude Desktop**, on the library's own computer: `studystash mcp` over stdio.
- **claude.ai**, from anywhere: HTTP with OAuth 2.1, behind a [Tailscale Funnel](https://tailscale.com/kb/1223/funnel).

## Where your data lives

Everything is under `~/.study-stash` (or `--home`, or the `STUDYSTASH_HOME` environment
variable): `config.toml` or `client.toml`, the lecture database, and the notes themselves as plain
Markdown files under a class folder you can open, back up, or sync however you like.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com).

```sh
dotnet build engine/StudyStash.slnx
dotnet test engine/StudyStash.slnx
dotnet run --project engine/src/StudyStash.App -- --home /some/temp/dir
```

`macos/build-app.sh` builds "Study Stash.app" and a DMG (Xcode's command line tools too).
`engine/README.md` has more on the engine's projects, tests, and fixtures.

## License

[MIT](LICENSE). No warranty: it's provided as is, so check your notes against the lecture before
relying on them, since models make mistakes. See [SECURITY.md](SECURITY.md) to report a security
problem.
