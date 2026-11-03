# Libraries instead of our own code: the plan

Written 2026-11-03. It covers which well-maintained libraries replace code we wrote ourselves, which code stays ours,
and the order to change things in. Each phase is one branch, ships on its own, and leaves the app working. The toolkit
these phases build on (`Poll`, `AsyncAction`, `AppWindows`, the stores) is in `docs/ARCHITECTURE.md`.

The research was done on 2026-11-03 against NuGet and each project's repository. Check the version and Avalonia 12
support again when a phase starts.

## What we use and what stays ours

| Library | Licence | Avalonia 12 | Replaces | Phase |
|---|---|---|---|---|
| CommunityToolkit.Mvvm built-ins: `AsyncRelayCommand`, `WeakReferenceMessenger` (already referenced) | MIT | yes | the planned AsyncAction internals and Messenger | 1 |
| `TimeProvider` (.NET) + Microsoft.Extensions.TimeProvider.Testing | MIT | n/a | fixed waits in tests; makes `Poll` and the stores fakeable | 1 |
| DialogHost.Avalonia | MIT | yes (≥ 12.0.0) | the 7 hand-made `Confirming*` flags and our own delete dialogs | 2 |
| Verify.XunitV3 + Verify.Avalonia | MIT | yes (≥ 12.1.0) | ~10k lines of picture-only `*Shots`/`*Films` tests, with ~10 real snapshot checks | 3 |
| xunit.v3 for Core.Tests | Apache-2.0 | n/a | xunit 2.9 (App.Tests is already on v3) | 3 |
| Avalonia.Labs.Notifications | MIT | yes (≥ 12.0.1) | `MacNotifications` and `WinNotifications` (~560 lines); Linux gains real notifications | 4 |
| Velopack (+ the `vpk` tool) | MIT | any UI | our updater (`Updater`, `Updates`, most of `AppUpdates`), the Inno Setup installer, the notarize and sign scripts, the Linux install scripts: ~1,500 lines | 5 |
| Avalonia.AvaloniaEdit | MIT | yes (12.0.0) | the typed-notes editor (`TypedEditor`, `MarkdownBox`), ~900 lines net | 6 (trial first) |

**Stays ours** (it's what makes the product, or no library fits):
- `NoteView`, `TextSelection` and the quotes and spoken text;
- the Rich explorers (diagrams, parts, plots);
- `Hotkeys`, `MacStatusItem`, `MacTitleBar`, `GlassBackdrop`, `Desktop`;
- `Waveform` and the Syft brand controls;
- `NotesPdf`;
- `StepFlow`, `Words`, `Format`, `Poll` and the stores.

**Ruled out:**

| Library | Why not |
|---|---|
| SharpHook | needs the Mac Accessibility permission |
| Avalonia `TrayIcon` on Mac | its click isn't raised (12.1.5) |
| QuestPDF | revenue-limited licence |
| PDFsharp ≥ 6.5 | AGPL |
| DesktopNotifications | archived, no Mac support |
| Avalonia.Controls.Markdown, TreeDataGrid | paid |
| Humanizer | not our wording |
| ScottPlot, OxyPlot | would rebuild what PlotView already does |
| icon packs | a different look |
| FluentAssertions 8 | paid |

## Phase 0: before starting
- **Merge the toolkit PRs** (#117–#120). Phases 1 and 2 build on `Poll`, `AsyncAction`, `AppWindows` and `AiStatusStore`.
- **Lock in the gains:** lower `tools/connections-baseline.txt` to the counts after those merges.

## Phase 1: toolkit built-ins and a fake clock (`lib-builtins`)
Low risk. Nothing a student sees changes.
1. **`AsyncAction`** becomes a thin wrapper over `AsyncRelayCommand`:
   - `AllowConcurrentExecutions = false`;
   - `Busy` from `IsRunning`;
   - Cancel through its cancel command;
   - `Say` from our `Outcome` words.

   Move AiSetupModel's `ActAsync` and AiEnginesModel's actions onto it, one at a time.
2. **`WeakReferenceMessenger`** for app-wide events:
   - Start with services that call `Shell` directly (`NotesDownload`, five toasts). They send a message, and `Shell` listens.
   - The `shell-in-services` count goes down.
3. **`TimeProvider` everywhere something waits:**
   - `AppHost` holds `TimeProvider.System`, and `Poll`, `AiStatusStore` and `CanvasWatch` take it.
   - Tests pass `FakeTimeProvider` and move time forward instead of waiting.
   - Convert the worst flake hotspots first: PhoneDrive*, AskStreaming, CanvasWatch, SetupTests, ExtensionE2E.

**Done (`lib-builtins`), what was and wasn't done:**
- **`AsyncAction`** is a thin wrapper over the toolkit's `AsyncRelayCommand` (no concurrent runs; `Busy` is its `IsRunning`; `Cancel()` is the command's; `Say`/`Failure` come from the `Outcome`). Public API unchanged. AiSetupModel's `ActAsync` runs on it (its `Busy` follows the action's). AiEnginesModel's actions are not moved yet: they share one `Busy` with key saving and the settings posts, so each needs its own look.
- **`TimeProvider`:** `Poll` and `AiStatusStore` already took one; `CanvasWatch` and `Paced` (the answer's drawing pace) now do too (optional last parameter, default `TimeProvider.System`, so AppHost and Shell are untouched). Still to do: AppHost holding the one provider and handing it down, and `DriveLink`'s own `DriveTimes`, which should become a `TimeProvider` (the PhoneDrive tests' quiet-period waits wait on it).
- **Tests:** `Microsoft.Extensions.TimeProvider.Testing` in both test projects. CanvasWatch and Paced tests move a `FakeTimeProvider`; waits on real input or output use `Wait.UntilAsync` (App.Tests) / `Waiting.UntilAsync` (Core.Tests), both over `Poll`. Fixed `Task.Delay`/`Thread.Sleep` calls in the tests: 183 to 117 before the last pass over the rest. Left on purpose: waits that are the thing under test (a quiet period: nothing more is written; file times; Chrome-driven E2E, which are skipped without Chrome), the Real/Hold/film rigs.
- **Core.Tests is on xunit.v3** (3.2.2): `ValueTask` lifetimes, a `typeof` orderer, `[CallerFilePath]` constructors on the two fact attributes, `TheoryData.Data`, and `xUnit1051` switched off for that project (older tests don't pass `TestContext.Current.CancellationToken` yet). `DisableParallelization` collections are kept.

**Done when:**
- the `sleeps` and `shell-in-services` counts are lower and their baseline is lowered;
- the converted tests pass with no real waiting.

## Phase 2: one "Are you sure?" (`lib-dialoghost`)
1. Add DialogHost.Avalonia.
2. Build `Confirm.AskAsync(ConfirmAsk)` over it, with one host per window opened through `AppWindows`.
3. Style it with `Skin` tokens:
   - no gradients;
   - Escape answers no;
   - focus starts on the safe choice;
   - the buttons have accessible names.
4. Move the 7 `Confirming*` flags and the delete-lecture dialog onto it.

**Test:** one test that Escape answers false and that a yes runs the action once.

**Done (`lib-dialoghost`), what was and wasn't done:**
- **DialogHost.Avalonia 0.12.3** (latest stable, MIT, needs Avalonia ≥ 12.0.0; we're on 12.1.3), pinned in `StudyStash.App.csproj`. `<dialogHost:DialogHostStyles />` is in App.axaml; `Styles.axaml` styles `DialogHost.confirm` with the `Scrim`, `PopupBg`, `RadiusPanel` and `ShadowFloat` tokens (no gradients; the control's own 56 % veil is switched to the token's strength).
- **`Confirm.AskAsync(ConfirmAsk)`:** `IConfirm` in the constructor (`Confirm.Default` when a model is made without one). `AppWindows.Make` wraps each window's view in a dialog layer (`ConfirmLayer`); the question opens over the window the student is in (else the last one, else it brings the library up first, for the menu bar's "Stop and start"). One question at a time per window. Safe button focused, Escape and a click outside answer no, every button has its words as its accessible name. `ConfirmLayer.ViewOf/SetView` replace reading `window.Content` where a window's view is wanted.
- **Moved:** Settings' model Remove, Use just this computer, Use a library on another computer (these two are the frozen two-computer setup: moved, not changed), Use Canvas course names (the preview is the dialog's list), Rewrite every summary, Disconnect from Google, Remove Kokoro voices, the library window's "Stop recording … and start …?" (two places), and Delete this lecture. `ConfirmCard`, `MacDeleteLecture` and `WinDeleteLecture` are gone (`mac-views` 38 → 37; `literal-font-sizes` 246 → 240).
- **Words:** the Remove-voices question and the Rewrite-all question are new; the others keep their old words.
- **Left alone:** the new-folder naming card (`NamingFolder`), which asks for a name rather than a yes or no; windows made by hand (the browser question, floating panels) have no layer.
- **Test:** `ConfirmTests` (a real window: Escape answers no and runs nothing; Yes pressed twice runs once). `ConfirmShots` draws the question off-screen over the library, both looks, light and dark. View-model tests use `FakeConfirm`.

## Phase 3: real picture checks, fewer test lines (`lib-verify`, then `lib-xunit3`)
1. **Add Verify.XunitV3 and Verify.Avalonia to App.Tests.** Snapshot about 10 screens:
   - Home, a class page, a lecture's notes with a formula and a diagram, Due;
   - Settings → Your AI, the first-run funnel, the recorder;
   - the menu-bar dropdown, a notification, Ask beside notes;
   - each in one look and light mode, plus a dark mode for Home.
2. **Run snapshots on Linux CI only.** The fonts ship with the app, so a picture is the same on every run. Compare with SSIM tolerance. On a failure, CI keeps the received and verified pictures as an artifact.
3. **Move the picture code** the README and promo videos still use into a separate, non-CI project (`StudyStash.Pictures`). Delete the remaining picture-only test files.
4. **`lib-xunit3`:** move Core.Tests to xunit.v3.
   - Split the slow App tests into two processes (headless Avalonia can't run tests in parallel within one process).
   - Write the slowest-tests list to the run summary.

**Done when:**
- the snapshots pass on CI and a deliberate change shows up as a diff;
- the App tests have shrunk by about 10k lines;
- App test time is roughly halved.

## Phase 4: the system's own notifications (`lib-notifications`)
1. **Trial first.** Check `Avalonia.Labs.Notifications` on:
   - a signed Mac build in the macOS test VM;
   - a GitHub Windows runner;
   - Linux.

   Check:
   - the permission prompt;
   - buttons on a notification;
   - that a click opens the right page (Windows uses COM activation, not the `studystash:` link);
   - what happens when the system refuses.

   It's a "Labs" package, so pin the version.
2. Put it behind the existing `SystemNotices` way in, so the rest of the app doesn't change.
3. Keep `ToastShelf` (our own cards) for when the system refuses or isn't there.
4. Delete `MacNotifications`, `WinNotifications` and most of `NoticeLinks`.

**Test:** the existing notification self-test steps on all three systems.

## Phase 5: Velopack for installing and updating (a planned release, three steps)
It changes how every student installs and updates, so each step needs the owner's go-ahead.
1. **Trial (`lib-velopack-spike`).** In CI:
   - `vpk pack` for win-x64/arm64, osx-arm64/x64 and linux;
   - sign with Artifact Signing (`--azureTrustedSignFile`) and Apple notarization;
   - install, update and uninstall in VMs (the macOS Tart VM, a Windows runner).

   Decide:
   - **Mac downloads:** one universal Mac app (our `launcher.c` bundle packed as is) or a download per processor.
   - **The DMG:** whether to keep it, since Velopack makes a .pkg and our `dmg.sh` stays if we want drag-to-Applications.
2. **The bridge release (`lib-velopack-bridge`).** The app gets Velopack's `UpdateManager`. The installers are still made the old way, plus Velopack's.
   - **Windows:** the last Inno-era update downloads Velopack's Setup, runs it silently, then runs the Inno uninstaller.
   - **Mac:** the new package replaces the app in place.
   - **Linux:** the AppImage, with a menu entry written by us.
   - **Every system:** settings and lectures in `~/.study-stash` and Documents are never touched. On first launch from the new place, the AI app connections are rewritten to the new program path (`Environment.ProcessPath`, AiJobs.cs:610), so Claude, ChatGPT and the rest keep working.
   - `AppUpdates` keeps its rules (wait until idle and not recording, then notify); only the download and install moves to Velopack.
3. **Clean up (`lib-velopack-cleanup`).** After two releases with almost everyone moved over (the daily count shows the versions), delete:
   - `Updater`/`Updates`;
   - `setup.iss`;
   - the old sign, notarize and install scripts;
   - the old CI installer steps.

**Done when:**
- a student on the release before the bridge ends up on the Velopack version with nothing to do;
- their library and AI connections still work;
- CI proves install, update and uninstall on all three systems.

## Phase 6: the typed-notes editor (trial, later)
Try Avalonia.AvaloniaEdit for `TypedEditor` and `MarkdownBox`. It brings one document instead of a box per line, real
undo, selection across lines, and input for other keyboards and languages. The live preview (hidden marks, heading
sizes, ticking boxes) would be our own renderers on top. Go ahead only if the trial keeps today's typing feel.

## How each phase is run
- One branch per phase (two or three for Velopack), from main.
- One focused test per change, and no fixed waits.
- The connection counts only go down.
- CI is run with installers wherever install, update or notifications are touched.
- The public-repo scan runs before every push.
- The owner merges.
