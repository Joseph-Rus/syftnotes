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
setup, and `Settings.cs`' `SettingsModel` wires it into **Settings → Connection**, under a new "This computer"
section (`Views/SettingsView.axaml`, one shared page for both looks). It shows the current role in plain words and,
depending on it, one of two switches — each with a short explanation and a confirm step before anything changes:

- **A laptop can also become the library.** Nothing about its recordings moves: `LibraryHere.CreateAsync` makes (or
  finds) a library right where its notes already are. If the laptop was connected to a library on another
  computer, that library's name and password come over automatically (so connecting from anywhere else is just a
  new address), and its lectures are brought over too — a page at a time, notes and classes as they were written,
  with no AI run again (`LibraryMove`, see `RoleSwitch.BringLecturesAsync`). The old library keeps every lecture of
  its own; bringing them over is safe to do more than once, and a problem doing it (the old library's offline, say)
  never stops the switch — it's said in plain words, with a way to try again.
- **A library can become a laptop.** First, this library's own lectures go to the library it's about to connect to
  (`RoleSwitch.HandOffLecturesAsync`, the same `LibraryMove` used the other way around), so nothing is left behind;
  if that library can't be reached or the password's wrong, nothing changes and the library here keeps running.
  Only once that's done does this computer stop being the library and start sending to the other one
  (`RoleSwitch.ToLaptopAsync`): its own lectures, notes and settings stay on disk untouched, ready for it to be the
  library again any time.

Both directions restart whatever needs it — the library service starts, stops or takes new settings; the laptop's
connection points at its new address — through the same `AppHost`/`LibraryHere` machinery Setup and Settings already
use, so nothing about running the app changes because the switch went through `RoleSwitch` instead of first-run
setup.

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
computer" actually calls them, including asking before switching and what's shown while confirming. `LibraryWebTests`
and `LibrarySetupTests` cover the library's own pages linking to the one download instead of the old Laptop names.
The Mac and Windows CI jobs build the one DMG and the one Setup.exe, check the DMG carries no `StudyStashRole`, and
install, self-test and update each in place.
