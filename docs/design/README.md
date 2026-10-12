# Design

`semester-ui-handoff/` is the spec the app's look was built from (October 2026): the tokens, the Home
screen and every component as working HTML and CSS (open `reference/home.html` in a browser; add
`?theme=dark` for dark), screenshots in light and dark, and the rules in its `README.md`. It's kept
here as it was handed over.

Where the spec lives in the app:

| In the spec | In the app |
| --- | --- |
| `tokens/tokens.css` | `engine/src/StudyStash.App/Skin.cs` (`Light`, `Dark`, and the tokens by name) |
| `reference/semester.css` | `engine/src/StudyStash.App/Styles.axaml` (the look's own pieces, at the end) |
| The highlight and the swatch | `Controls/Highlight.cs` |
| The week | `Controls/WeekRow.cs`, `ViewModels/OverviewModel.cs`, `Views/MacOverview.axaml` |
| The sidebar and the page | `Views/MacLibrary.axaml` (`WinLibrary.axaml` is the same window under Windows' title bar) |
| `icons/` | `IconPaths.cs`, made by `engine/tools/icons/lucide.py` |

To lay a picture of the app over the spec's: `STUDYSTASH_SHOTS=<folder> dotnet test
engine/tests/StudyStash.App.Tests --filter Design_shots` draws Home with the spec's own sample, the
size of `screenshots/home-light.png` at 1x.
