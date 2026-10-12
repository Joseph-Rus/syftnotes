# Design

`semester-ui-handoff/` is the spec the app's look and layout were built from (October 2026, its
second version): the tokens, every screen and component as working HTML and CSS (open a page of
`reference/` in a browser; add `?theme=dark` for dark), screenshots of each in light and dark, and
the rules in its `README.md`. It's kept here as it was handed over.

Where the spec lives in the app (`engine/src/StudyStash.App`):

| In the spec | In the app |
| --- | --- |
| `tokens/tokens.css` | `Skin.cs` (`Light`, `Dark`, and the tokens by name) |
| `reference/semester.css` | `Styles.axaml` (the look's own pieces, at the end) |
| The highlight, the swatch, a folder title's underline | `Controls/Highlight.cs` |
| The sidebar (`.sidebar`, `.coming`) | `Views/SidebarBody.axaml`, in `Views/MacLibrary.axaml` and `WinLibrary.axaml` (the window's frame in each look) |
| `home.html`, `folder-*.html`, `due.html` | `Views/Overview.axaml` over `ViewModels/OverviewModel.cs`; a note's row is `Views/NoteRowView.axaml` |
| The month and the week (`.cal`, `.week`) | `Views/DueCalendar.axaml`, `Views/WeekPlanner.axaml`, `ViewModels/DueCalendarModel.cs` |
| `note.html`, `recording.html` | `Views/NotePage.axaml` over `NoteModel` (in `ViewModels/LibraryModel.cs`) |
| The folder picker (`home-move.html`) | `Views/FolderPicker.axaml`, `ViewModels/FolderPickerModel.cs` |
| What a recording is called (section 3) | `Nouns` in `ViewModels/TypedNotes.cs` |
| Whose words are whose (section 6.2) | `TypedNotes` in the app; `Summarize.AroundTyped` and `Summarize.KeepTyped` in `StudyStash.Core` |
| Where each page comes from | `Shell.Overview.cs` (Home, Due, a folder, Canvas's lists), `Shell.Library.cs` (a note), `Shell.Recording.cs` (recording) |
| `icons/` | `IconPaths.cs`, made by `engine/tools/icons/lucide.py` |

How a few things in the spec are done here:

- **Folder kinds** are the sidebar's groups: a folder under Classes holds lectures, one under Clubs,
  Work or a group of the student's own holds meetings, and a note in no folder is a recording.
- **The student's own words** are marked in a note's Markdown (`==like this==`, a highlighter's mark
  in any Markdown app) rather than kept as a list of spans. The page draws them in ink and heavier,
  the rest in grey; "Only my notes" shows the marked lines alone; a line changed in Edit becomes
  theirs. What they typed while recording is also kept apart, as it was typed, with the lecture.
- **A class's lists on Canvas** (Assignments, Modules, Announcements) open in the app from the
  folder page's rail: the one place with a list beside what's opened from it.

To lay pictures of the app over the spec's: `STUDYSTASH_SHOTS=<folder> dotnet test
engine/tests/StudyStash.App.Tests --filter Design_shots` draws every screen with the spec's own
sample, the size of its `screenshots/` at 1x, light and dark.

`semester-ui-pictures/` has the app's own pictures of the new screens (drawn off screen with the spec's
sample) and three short films of them in use, for the pull request that brought the design in.
