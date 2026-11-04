# Before you change Syft Notes

Read `docs/ARCHITECTURE.md` (how the desktop app connects, and its toolkit) and `docs/DESIGN.md` (how it looks). The
desktop app is `engine/src/StudyStash.App` (C#/.NET 10, Avalonia, CommunityToolkit.Mvvm); build it with the .NET SDK
`engine/global.json` asks for, since the `dotnet` on PATH may be older.

The app kept falling into a fix loop because each feature built its own copy of something that already existed (its
own AI status, its own window setup, its own error handling) and the copies broke one by one. So: **use the toolkit;
don't build your own.** If it almost fits, extend it there. If a piece isn't built yet, build it in the toolkit the
first time you need it (docs/ARCHITECTURE.md section 3 says how), not inside your screen.

## Where things live
- `Shell*.cs`: opens windows and connects the top-level models. Don't add page logic, library calls or JSON here.
- `Services/`: long-lived work owned by `AppHost`; shared state in `Services/Stores/`; small helpers in `Services/Kit/`.
- `ViewModels/`: one view model per screen; shared pieces in `ViewModels/Kit/`.
- `Views/`: XAML only. `Controls/`: reusable controls. `Windows/`: windows and placement. `Platform/`: Mac/Windows interop.

## The toolkit: what to use
| You need | Use |
|---|---|
| Something long-lived (a store, a client, a factory) | Register it in `AppServices.Register` (AppServices.cs) and take it in your constructor; `AppServicesTests` checks it can be made. Never `new` it in Shell |
| A view model Shell makes at runtime | A factory registered in `AppServices` (`AskFactory`, `CanvasModels`) that passes every hook its buttons need in the constructor (`AskServices`, `CanvasConnectExits`), not `Action?` properties filled in afterwards |
| A service or view model asking the app to do something (a notification, Settings, quit…) | Send a message from Messages.cs on `IMessenger` (`WeakReferenceMessenger.Default` in a static service); Shell listens in Shell.Messages.cs. Never call `Shell.` |
| To start work without waiting for it (a page load, a click's errand) | `UiTasks.Run(work, what)`; `RunQuietly` for work on a timer. Never a bare `_ = SomethingAsync()` |
| A window | `AppWindows.Open(new WindowSpec { ... })`. Never `new Window` |
| Which page is open | `PageState` (one record, swapped in one assignment) |
| The AI's sign-in or ready state, anywhere | `AiStatusStore` on `AppHost`: never fetch it or keep a copy in a screen |
| A sign-in whose page ends on a code to paste (Gemini's) | `SignInCodeModel` + `Views/SignInCode.axaml`; the code goes to `AgentSignIn.SendCodeAsync` |
| Canvas state | `CanvasWatch` (`Hurry()` while a screen waits) |
| A call to the library | `LibraryApi` / `LibraryCall.Run`, which gives an `Outcome` with offline, too-old and refused already in plain words |
| A button that does async work | `AsyncAction` (busy, error and cancel in one place) |
| Waiting for something | `Poll.UntilAsync(check, every, timeout)`: no fixed sleeps anywhere |
| Text that can be cut off | `c:Tips.WhenCut="True"` plus `ToolTip.Tip` (CutTextTests checks the main pages) |
| A piece drawn differently on Mac and Windows | one view plus skin styles, or `c:BySkin`; a Mac/Win pair only when the layout really differs |
| To ask "Are you sure?" before something that can't be undone | `IConfirm.AskAsync(new ConfirmAsk(title, what happens, "Delete"))` (ViewModels/Kit/Confirm.cs), taken in the constructor; `Confirm.Default` is the app's. Never a `Confirming…` flag and inline buttons, never your own dialog |
| Icons, the logo, loaders | `c:Icon`, `AppMark`/`SyftMark`, `SyftLoader` |

## Connection rules (CI's "Connection rules" job counts them: `tools/check-connections.sh`)
1. One owner per shared thing. A view model never keeps its own copy of AI, Canvas or recording state, and never runs
   its own poll loop.
2. AI engine-state strings (`"not_signed_in"`…) are read only in the AI status store.
3. View models get what they need from their constructor or a `*Services` record, never from `Shell`, and never make
   their own `HttpClient`/`AiRemote`/`RemoteLibrary`/`CanvasClient`.
4. Views and controls never call services, `Dialogs`, `Process` or `Shell`.
5. Services never call `Shell`; they return results or raise events.
6. Windows open only through `AppWindows`.
7. No fixed sleeps, in app code, tests, the self-test or CI: wait on the real condition with a timeout.
8. No error swallowed whole: catch what you expect, log the rest, and tell the student in plain words, never with a
   raw exception message.
9. A fix to something shared goes in the shared piece, not in the screen that showed the bug.
10. When a change brings a count down, lower its line in `tools/connections-baseline.txt` in the same change.

## Standing rules
- **Refine, don't expand.** What works today stays working. A change makes an existing piece simpler, clearer or more
  reliable; it doesn't add a new layer, library, setting or option unless the owner asked for it. A good change usually
  removes more lines than it adds. Never mix a cleanup with a new feature in one PR.
- **Small PRs, merged fast.** About six open at once at most; each merges within a day or is closed.
- **Few tests.** Test what could really break for a student: one focused test per piece. No big fake suites.
- **A test that fails without a code cause** is fixed or deleted the same day. Never re-run CI to get past it, and no
  retries or quarantine.
- **Changing setup's pages?** Update `engine/src/StudyStash.App/SelfTest.cs` too; only CI's self-tests run it.
- **The repo is public.** No secrets, tokens, passwords, real emails, personal data or home-network names in code,
  tests, logs, commits or PRs. Examples use `@example.com`. Never open `~/.study-stash`, `config.toml` or
  `client.toml`, and keep tests off a real library on port 8787.
- **Look:** no gradients. Always the real Syft logo (`AppMark`/`SyftMark`), never an icon stand-in.
- **Words** are plain, for a student who isn't technical. Claude and ChatGPT by name; "Your AI" and "AI apps" in
  Settings.
- **When the owner is using the Mac,** work off-screen only: no windows, focus or clicks on his screen. Pictures come
  from the app's off-screen drawing (the picture checks in `App.Tests/Snapshots`, or the `StudyStash.Pictures` project
  for the README, site and films), never from capturing the screen.
- **The two-computer setup** (a library on another computer) is frozen: no features, fixes or tests for it.
- **Git:** one branch per change. Commits carry no AI attribution. The owner merges PRs; release PRs only after a
  green rehearsal.
