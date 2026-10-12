# Icons and type

How the app's icon table and font files were made, to make them again.

- `lucide.py <lucide icons folder> <IconPaths.cs>` writes `src/StudyStash.App/IconPaths.cs` from
  [Lucide](https://lucide.dev)'s own SVG files (the `icons/` folder of the `lucide-static` npm package,
  version 0.469.0). The names the app's views ask for are in the script, each beside Lucide's name for
  it; add a line there for a new icon. Run it with `python3 -I`.
- `font-weights.py <variable font> <out folder>` makes the static weights in
  `src/StudyStash.App/Assets/Fonts` from Atkinson Hyperlegible Next's variable font (Avalonia loads a
  TTF per weight). It needs `fonttools` and `brotli`.
