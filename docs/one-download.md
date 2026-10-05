# Study Stash: one download

Study Stash used to ship four installers (Laptop and Library, on Mac and Windows), each baking in what the computer
was for. Now there's one app and one download per system: `Study-Stash.dmg` on Mac, `Study-Stash-Setup.exe` on
Windows. What a computer is for — a laptop that records, a library that keeps and writes up lectures, or both, on
one computer — is a question setup asks the first time it opens, not a choice made at download time. Any install can
change its mind later, from Settings, with nothing to reinstall.

```
Study-Stash.dmg / Study-Stash-Setup.exe            first run                     later, any time
  the same app, no role baked in         ──────▶  setup asks: just this      ──▶  Settings → Connection →
                                                   computer, my laptop, or         This computer: switch
                                                   my library                      laptop ↔ library ↔ both
```

## First run: the role is asked, not downloaded

The build carries no role of its own: `Study-Stash.dmg`'s `Info.plist` has no `StudyStashRole` key, and Windows'
`study-stash.ini` writes no `role=` line either (CI checks the DMG for exactly this — see `.github/workflows/ci.yml`).
Setup still shows three starting choices — **Just this computer**, **This is my library**, **This is my laptop** —
worked out fresh each time it opens (`Setup.StartingRole`, `engine/src/StudyStash.App/Services/Setup.cs`):

- **A fresh install** has no saved role and no library connected yet, so setup suggests one:
  `SetupModel.Suggested` picks **This is my library** only when an *old-style* role installer said so (`Apps.RolePreset`,
  which still reads a Laptop/Library build's Info.plist key or study-stash.ini line, kept for exactly this); otherwise it
  suggests **Just this computer** — the friendliest default for a student who just wants to start.
- **An install that already finished setup** keeps its own role from `app.json` (`AppSettings.Role`), whatever the
  installer suggests: reopening setup (say, to change a class) never resets a working laptop or library.
- **A laptop that's mid-setup and already connected somewhere else** (its `client.toml` points at another
  computer) stays a laptop, so a half-finished setup doesn't lose its way if it's reopened.

Both looks (`Views/MacSetup.axaml`, `Views/WinSetup.axaml`) show all three choices on **Welcome**, sharing one
`SetupModel`; nothing about the installer changes which ones are offered.

## Switching later: Settings → Connection → This computer

`RoleSwitch` (`engine/src/StudyStash.App/Services/RoleSwitch.cs`) is what actually changes a computer's role after
setup, and `Settings.cs`' `SettingsModel` wires it into **Settings → Connection**, under "This computer"
(`Views/SettingsView.axaml`, one shared page for both looks). It says what this computer is for in plain words and,
depending on that, offers one of two moves, each a settings row that explains itself and asks before anything
changes:

- **Use just this computer** (a laptop). This is how a laptop stops depending on its library on another computer
  without losing anything; a laptop's Settings → Library page offers it too, and it's the only way in (the old "Make
  this computer the library too" button, which pointed the app at an empty library and left the old one's lectures
  out of sight, is gone). The row names what it would stop using ("Stop using Sam's library on mac-mini: your
  classes and notes come to this Mac…"), and the confirm says what happens: *"Sam's library on mac-mini keeps its own
  copy. Classes and notes come here: every lecture with its notes and transcript, the files you attached, your
  classes with their other names and Canvas courses, and your chats. Then this Mac is your library, and keeps
  recording."* Then:
  1. `RoleSwitch.ToLibraryAsync` → `LibraryHere.CreateAsync` makes (or finds) a library right here, reachable from
     this computer alone the way setup's Just this computer makes one, with the old library's name and password when
     it has none of its own. Just before client.toml is pointed at it, the old library (address, password, name) is
     remembered in `bring-from.json`, readable by the owner only like client.toml, until everything from it has come
     over. Every way of making this computer the library goes through `CreateAsync`, so setup's own Just this
     computer on a laptop that used another library remembers it too, and says its classes and notes can come over
     from Settings → Connection.
  2. `RoleSwitch.BringLecturesAsync` copies everything with `LibraryMove.CopyAsync` (below), under a progress line and
     bar ("Bringing lectures over: 40 of 140…", then attached files, then other notes and files), and ends by saying
     what came: *"26 lectures and 3 classes came over from Sam's library, with 5 files and 1 chat. 1 of them had no
     notes yet: this Mac writes them now. It keeps its own copy."* (and, when so, how many were already here and
     which files weren't on the old library any more).
  3. If it stops part-way (the old library's asleep, Tailscale's off), what came stays, and this computer is already a
     working library that records. The summary says *"Not everything from Sam's library has come over yet. Can't
     reach your old library… What came is safe here, and Sam's library still has everything: press Try again when it
     can be reached."*, and a row stays under This computer — *Not everything from Sam's library has come over yet* —
     with **Try again** (only what's missing comes) and **Leave them there** (for a library that's gone for good:
     the row goes, nothing is deleted anywhere). After setup it reads *Your classes and notes are still on Sam's
     library*, with **Bring them over**.
- **Use a library on another computer** (a library, or just this computer). First everything in this library goes to
  the library it's about to connect to (`RoleSwitch.HandOffLecturesAsync`, the same `LibraryMove` the other way
  round), so nothing is left behind; if that library can't be reached, the password's wrong, or it runs a Study
  Stash too old to take attached files and chats, nothing changes and the library here keeps running. Only then does
  this computer stop being the library and start sending to the other one (`RoleSwitch.ToLaptopAsync`): its own
  lectures, notes and settings stay on disk untouched, ready for it to be the library again any time. On a computer
  that's the library, the Connect button at the top of the page no longer re-points the app at another library
  (which hid this one's lectures); it says to use this move instead, with the address filled in.

Both directions restart whatever needs it — the library service starts, stops or takes new settings; the laptop's
connection points at its new address — through the same `AppHost`/`LibraryHere` machinery Setup and Settings already
use, so nothing about running the app changes because the switch went through `RoleSwitch` instead of first-run
setup.

### What comes over, and what doesn't (`LibraryMove`)

`LibraryMove` (`engine/src/StudyStash.Core/LibraryMove.cs`, routes in `LibraryWeb.Move.cs`, all behind the library
password) copies a library in parts, each a page at a time. The old library's routes only read; the new library's
never overwrite: a lecture, attachment or chat it already has (by id) is left alone, and a file whose path is taken by
a different file comes in beside it as "name (2).ext". So running it again brings only what's missing, never a second
copy, and a stop part-way leaves nothing half-written (a file arrives in a hidden folder and is checked — its size,
or its SHA-256 — before it's put in place).

| Comes over | How |
|---|---|
| Classes: names, other names (the title rules), what each covers, their order (so their colours) | `/api/v2/move/library`. A class the new library has already (whatever the case) keeps its own name and description and learns the old one's other names. |
| Each class's Canvas course, which courses are chosen, the school's Canvas address | Same. Only when the new library has no course for that class (and it's the same school). The Chrome extension follows the new library by itself (`ExtensionKeeper`); the next sync fills the Due list and course pages in again. |
| Every lecture, under its class, with who chose it, its transcript, typed and private notes, study notes and what wrote them, topics, and its note file exactly as it was (edits by hand or by the AI included) | `/api/v2/move/lectures`: pages of ids first, then only the lectures the new library says it hasn't got, whole. A lecture whose notes weren't written yet (or failed) comes too, and the new library writes them. |
| Files attached to lectures and classes, with the words read from them | `/api/v2/move/attachments`, each file streamed through (never all in memory). |
| The notes folder's other files: notes captured into a class (`<Class>/Notes/`), the Inbox, the Canvas mirror, anything put there by hand | `/api/v2/move/files`, each with its SHA-256. Hidden files and folders (the AI's undo history in `.git`) stay. |
| Chats | `/api/v2/move/chats`. The AI's own conversation lived on the old computer, so a follow-up here starts afresh from what was said. |
| How notes are written and sorted (`[summary]`, `[ollama]`, ai.json's picks) | Only into a library the switch just made (one whose AI was never chosen). |

Not brought, on purpose: phones paired with the old library and Claude's connections to it (they belong to its
address: pair and connect again here), the Chrome extension's key (the new library makes its own), Canvas's sync
state (rebuilt by the next sync), the folders the old library could read (paths on the other computer), the undo
history of AI edits, rewrite drafts not used yet, and the trash. Recordings never lived on the library: they're on
the laptop that made them.

A library from 0.10.0 hands over its filed lectures only (it has no other routes): they come, and the rest waits —
the summary says to update it and press Try again, and the row stays until it has. One older than that can't hand
anything over: the summary says to update it, and the row stays for Try again.

## What an older install does when it updates

Every release still carries the four old names — `Study-Stash-Laptop.dmg`, `Study-Stash-Library.dmg`,
`Study-Stash-Laptop-Setup.exe`, `Study-Stash-Library-Setup.exe` — as byte-for-byte copies of the one download
(`Updates.Installers`, `Updates.MacAsset`/`WindowsAsset` for the new names, `Updates.MacLaptopAsset` and friends for
the old ones; the release step in CI makes the copies and checksums all six together). That means:

- **A copy from before this round**, updating itself (`AppUpdates`) or updated from the CLI (`studystash update`),
  still asks for its old asset name and gets the current app — with no role baked in — same as anyone downloading
  fresh today. Its own `app.json` already has its role, so nothing about what it's for changes when it updates.
- **An old library's "Connect your laptop" page and setup wizard** (`LibraryWeb.cs`, `SetupWeb.cs`) now link to
  `Study-Stash.dmg` and `Study-Stash-Setup.exe` — the one download — rather than the retired Laptop-named ones.
- **`install.sh` and `install.ps1`** fetch the one download too. An old role argument (Mac) or `$env:STUDYSTASH_ROLE`
  (Windows) from an older line of the scripts is simply never read any more, so passing one changes nothing; the
  address an old laptop install used to get from `STUDYSTASH_SERVER` is now just printed, since setup asks for it
  itself.

None of the four old names will keep being published forever, but as long as they're on a release, every install
still out there — old script, old library page, or an app checking in on its own — updates cleanly into the one
download.

## Testing

`SetupTests.cs` covers `Setup.StartingRole`: a fresh install with no installer preference, one with an old-style
Laptop or Library installer's suggestion, an install that already finished setup keeping its own role regardless,
and a laptop mid-setup staying a laptop. `RoleSwitchTests.cs` covers `RoleSwitch` directly: finding (or not finding)
an old library to bring lectures from, a laptop becoming the library and inheriting an old library's name and
password, bringing lectures over and the words each outcome says, a library becoming a laptop (refusing its own
address, a wrong password, an address that can't be reached, and the happy path), and handing lectures off before
that switch. `SettingsRoleSwitchTests.cs` covers the same moves through `SettingsModel`, the way Settings' "This
computer" actually calls them, including asking before switching and what's shown while confirming.
`UseJustThisComputerTests.cs` is the end-to-end proof: an old library (a real one, on a loopback port, seen by the
app as "mac-mini") with classes and their rules and Canvas courses, lectures with notes and transcripts (one edited
by hand, one sorted by a person, one Unsorted, one still waiting for notes, more than a page), attached files,
captured notes, the Inbox, the Canvas mirror and a chat; a laptop presses Use just this computer, the app starts its
own library (the built engine) and everything arrives — every class in order with its rules, every lecture under its
class with its notes, transcript and note file byte for byte, every file — while the old library's files are
unchanged, byte for byte. A second test stops the copy twice part-way (the network gone on the second page of
lectures, then the old library going quiet in the middle of a file), checks what came stayed and the Try again row
shows, then presses Try again and checks everything is there exactly once. `LibraryMoveTests` covers the copy
between two libraries directly (pages, a second go changing nothing, wrong passwords, an old library too old).
`LibraryWebTests`
and `LibrarySetupTests` cover the library's own pages linking to the one download instead of the old Laptop names.
The Mac and Windows CI jobs build the one DMG and the one Setup.exe, check the DMG carries no `StudyStashRole`, and
self-test the app on every merge; a release run also installs, self-tests and updates each in place (the Windows
Setup.exe) and runs the DMG's app natively on an Intel Mac.
