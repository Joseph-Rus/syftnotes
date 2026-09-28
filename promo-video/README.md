# Study Stash promo video

A 35-second promotional video for Study Stash, made with [Remotion](https://www.remotion.dev) (React for
video). It's built from code, SVG and the app's own fonts, with no images from other sites. It lives apart from the
app: nothing here is part of the app's build.

- `out/promo.mp4`: 1920×1080, 30 fps
- `out/promo-vertical.mp4`: 1080×1920, for Reels, Shorts and TikTok

## Preview and render

You need Node.js 18 or later.

```sh
cd promo-video
npm install                 # once
npm run studio              # opens Remotion Studio in the browser: scrub, play, tweak live
npm run render              # → out/promo.mp4
npm run render:vertical     # → out/promo-vertical.mp4
npm run render:all          # both
```

## What to edit

- **Words, colours, timing:** `src/config.ts`. Every line of text on screen, the palette (Study Stash's Lagoon teal,
  paper, navy, night), and each scene's length in frames (30 = one second) are all there.
- **Music:** put a track in `public/` (for example `public/music.mp3`), then set `music = 'music.mp3'` in
  `src/config.ts`. It fades in at the start and out over the last second. The slot is in `src/Promo.tsx`, marked ♪.
- **A scene's look or animation:** `src/scenes/`, one file per scene.
  - `Hook.tsx`: the problem.
  - `Reveal.tsx`: the name and tagline.
  - `RecordNotes.tsx`, `Diagrams.tsx`, `AskNotes.tsx`, `CanvasDue.tsx`: the four features.
  - `Proof.tsx`: the privacy promise.
  - `Cta.tsx`: the call to action.
- **The transitions between scenes:** `src/Promo.tsx`.
- **Shared pieces:**
  - `src/components/`: the logo, the window, the sidebar and the layout that adapts between landscape and vertical.
  - `src/anim.ts`: the springs and eases.

## Scenes

| Time | Scene | On screen |
|---|---|---|
| 0–3 s | Hook | "Hours of lectures every week." → "Notes you'll never reread." |
| 3–7 s | Reveal | The icon, **Study Stash**, "Your lectures, written up and filed by class." |
| 7–12 s | Record | "Record once. Notes write themselves.": recording, then the notes filed under CS 101 |
| 12–18 s | Diagrams | "Diagrams and formulas, drawn for you.": a health class's cardiac cycle, a math class's derivative |
| 18–23 s | Ask | "Ask your notes anything.": the answer arrives word by word, with its sources |
| 23–27 s | Canvas | "Canvas, right next to your notes.": what's due, and an assignment's rubric |
| 27–30 s | Proof | "Everything stays on your computer." Free · Open source · Mac & Windows |
| 30–35 s | Call to action | Get it free for Mac and Windows, github.com/Joseph-Rus/study-stash |

Inter and Inter Display come from the app (`engine/src/StudyStash.App/Assets/Fonts`) under the SIL Open Font
License (`public/fonts/Inter-OFL.txt`). The notes' serif is the computer's own (New York or Charter on a Mac,
Georgia elsewhere).
