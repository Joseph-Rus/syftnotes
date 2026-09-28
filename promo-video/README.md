# Study Stash promo video

A 35-second promotional video for Study Stash, made with [Remotion](https://www.remotion.dev) (React for
video). It shows the app as it is: its light look on a Mac desktop, and a pointer that uses it. It's built from code,
SVG and the app's own fonts, with no images from other sites. It lives apart from the app: nothing here is part of
the app's build.

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

| Time | Scene | What happens |
|---|---|---|
| 0–3 s | Hook | "Hours of lectures every week. Notes you'll never reread.", beside a pile of untitled recordings |
| 3–6 s | Reveal | The icon, **Study Stash**, "Your lectures, written up and filed by class." |
| 6–12 s | Record | The menu bar icon opens the dropdown, Record, the waveform, Stop, and the lecture is filed in CS 101 |
| 12–18 s | Diagrams | A nursing class's nursing process (written by Claude Code), then a math class's derivative (by Codex) |
| 18–23 s | Ask | The quick panel; the "Answer with" menu picks Claude Code over Codex (OpenAI) and Ollama; the answer with its sources |
| 23–27 s | Canvas | What's due, and Lab 3's rubric |
| 27–30 s | Promise | "Your recordings stay on your computer." Free and open source, for Mac and Windows |
| 30–35 s | Call to action | Free for Mac and Windows, study-stash-app.web.app |

The desktop picture, rolling hills in Study Stash's teal under a pale sky, is drawn in `src/components/Layout.tsx`
(`Wallpaper`; its colours are `sky` and `hills` in `src/config.ts`). Headlines sit above the app in its own Inter
Display, lined up with the window: the first part in ink, the rest in grey (`Title`). The serif appears only inside the
app, where the notes really use it. The pointer's path and clicks are the `stops` and `clicks` of each scene's
`<Cursor>`.

Inter and Inter Display come from the app (`engine/src/StudyStash.App/Assets/Fonts`) under the SIL Open Font
License (`public/fonts/Inter-OFL.txt`). The notes' serif is the computer's own (New York or Charter on a Mac,
Georgia elsewhere).
