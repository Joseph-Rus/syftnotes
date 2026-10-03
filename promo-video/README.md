# Study Stash promo video

A 35-second promotional video for Study Stash, made with [Remotion](https://www.remotion.dev) (React for
video). It shows the app as it is, in dark mode on a Mac desktop, with a pointer that uses it. The dropdown, the library
window, the Ask bar's "Answer with" menu and Canvas are drawn to match the app's own screenshot tests
(`engine/tests/StudyStash.App.Tests`: `mac-01-dropdown`, `mac-04-full-app`, `mac-16-ai-ask-picker`,
`mac-09-canvas-due`, in dark). It's built from code,
SVG and the app's own fonts, with no images from other sites. It lives apart from the app: nothing here is part of
the app's build.

- `out/promo.mp4`: 1920×1080, 30 fps
- `out/promo-vertical.mp4`: 1080×1920, for Reels, Shorts and TikTok

A second video, about 50 seconds, shows what's new: diagrams you can play with, labelled drawings, formulas that move,
and a lighter app (see [The second video](#the-second-video-whats-new) below).

- `out/promo2.mp4`: 1920×1080, 30 fps
- `out/promo2-vertical.mp4`: 1080×1920

## Preview and render

You need Node.js 18 or later.

```sh
cd promo-video
npm install                 # once
npm run studio              # opens Remotion Studio in the browser: scrub, play, tweak live
npm run render              # → out/promo.mp4
npm run render:vertical     # → out/promo-vertical.mp4
npm run render:all          # both
npm run audio               # makes the music and sound effects again (needs Python with numpy and scipy)
npm run voice -- --scratch  # the narration in the Mac's voice; see VOICEOVER.md for ElevenLabs
```

## What to edit

- **Words, colours, timing:** `src/config.ts`. Every line of text on screen, the palette (Study Stash's Lagoon teal,
  paper, navy, night), and each scene's length in frames (30 = one second) are all there.
- **Sound:** the music and every sound effect are made from scratch by `scripts/make_audio.py`: synthesised, with
  nothing sampled or downloaded, so there is nothing to license. The score is in D major at 100 BPM: a pad, then a
  plucked arpeggio from the reveal, drums and bass from the recording, a breakdown for the promise, and a final chord.
  Every scene starts on a beat (18 frames), so if you change a scene's length in `src/config.ts`, run `npm run audio`
  and the music follows. The sound effects are listed in `src/components/Sfx.tsx`; each scene places its own, and
  every click of the pointer makes one. The finished mix measures about −14 LUFS, the usual level for YouTube and
  social video. To use a track of your own instead, put it in `public/` and set `music` in `src/config.ts`.
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
| | Handwriting | A photo of handwritten notes goes up from the phone app; the lecture reads it and rewrites its notes with it |
| 12–18 s | Diagrams | A nursing class's labelled organs of the torso (written by Claude Code), then a math class's derivative (by Codex) |
| 18–23 s | Ask | The Ask bar under a lecture's notes: its "Answer with" menu (Claude Code, Codex from OpenAI, Ollama) picks Codex, and the answer is written above the bar |
| 23–27 s | Canvas | What's due, and Lab 3's rubric |
| 27–30 s | Promise | "Your recordings stay on your computer." Free and open source, for Mac and Windows |
| 30–35 s | Call to action | Free for Mac and Windows, study-stash-app.web.app |

The desktop picture, a night-blue sky and a glossy silk wave in the style of a macOS wallpaper, is painted in code in `src/components/Layout.tsx`
(`Wallpaper`; its colours are `desk` in `src/config.ts`). Headlines sit above the app in its own Inter
Display, lined up with the window: the first part in ink, the rest in grey (`Title`). The serif appears only inside the
app, where the notes really use it. The pointer's path and clicks are the `stops` and `clicks` of each scene's
`<Cursor>`.

Inter and Inter Display come from the app (`engine/src/StudyStash.App/Assets/Fonts`) under the SIL Open Font
License (`public/fonts/Inter-OFL.txt`). The notes' serif is the computer's own (New York or Charter on a Mac,
Georgia elsewhere).

## The second video: what's new

`Promo2` and `Promo2Vertical`, in `src/promo2/`, beside the first video and sharing its desktop, window, sidebar,
pointer, titles, cross-fades, score and sound effects, so the two feel like one family. It tells it in the order a
student lives it: the notes arrive, then you play with what's in them, then what's under the hood, and the first
video's promise and ending.

```sh
npm run render2              # → out/promo2.mp4
npm run render2:vertical     # → out/promo2-vertical.mp4
npm run render2:all          # both
npm run card2                # → out/ko-fi-preview-2.png, a 1200×630 picture for a shared link
npm run audio2               # its music again, after changing a length in src/promo2/config.ts
npm run sfx2                 # its sound effects again, or yours from sfx2/ (SFX2.md)
npm run voice2 -- --scratch  # its narration in the Mac's voice; VOICEOVER2.md for ElevenLabs
```

| Time | Scene | What happens |
|---|---|---|
| 0–3 s | Opener | The icon, **Study Stash** with a "New" tag, "Your notes, now with diagrams you can play with." |
| 3–9 s | Notes first | BIO 110's "The cardiac cycle" is filed and its notes open at once; the byline says "Adding diagrams…"; "2 minutes later" (the menu bar clock moves on too) the diagram is added into the open note, pushing the key points down, and the byline says "Diagrams added" |
| 9–16 s | Explore | The pointer rests on "Ventricles contract": it lights up with its arrows and neighbours, the rest dims. A click pins it (arrows in Lagoon; Explain this, Quiz me, Where was this said?, Zoom in). Step through, Next twice ("Step 3 of 8"), then Play, a step on every beat |
| 16–20 s | Recall | Test yourself hides every box's words; a box is checked and marked Knew it, the rest with Y and N, up to "5 of 8 recalled" |
| 20–25 s | Kinds | Four notes' diagrams land on the beat: a state diagram reading "01" into its double-circled accepting state, a sequence diagram of logging in, message by message, a timeline of germ theory, a mind map of tissue types |
| 25–31 s | Drawings | ENGR 120's quadcopter: the pointer passes the battery and rests on the flight controller (ring, label lit, the rest dimmed), and a click pins its card (name, what the drawing says of it, Explain this, Quiz me, Where was this said?, Zoom in). Then NURS 210's hand, its deep flexor tendon pinned the same way |
| 31–38 s | Plots | CS 340's notes: the steepness slider is dragged from 1 to 5 and the sigmoid sharpens into a step; the page scrolls to gradient descent, whose learning rate is dragged down to 0.05 and up to 0.21, where the path zigzags and then diverges |
| 38–42 s | Quiet | "Light on your computer." Waiting: under half a percent of one core. Recording: about a quarter of what it used to take. Measured on an M-series Mac |
| 42–45 s | Promise | The first video's: "Your recordings stay on your computer." Free and open source, for Mac and Windows |
| 45–50 s | Call to action | The first video's: Free for Mac and Windows, study-stash-app.web.app |

The times are for the scratch narration; the real take recuts them (VOICEOVER2.md).

**What's real, what's drawn.** Everything is drawn in code, in the app's dark look, matched to the app's own
screenshot tests (`art2/ref`: the pinned box, the step strip, the recall strip, the pinned part's card, the plots, the
new kinds). The drawings are the app's own: `art2/svg/after-drone.svg` and `after-hand.svg`, drawn by Claude for Study
Stash, made into modules by `python3 scripts/art2.py` (`src/promo2/art/`) and drawn inline, so a part can light up,
dim and wear its ring. They don't use the parts library's composed drawings (those carry CC BY credits), so no
credits are needed. The plots are recomputed every frame from the app's own definitions (`Core/Rich/PlotDesign.cs`:
the sigmoid, k from 0.2 to 5; gradient descent on w₁² + 5w₂² from (−2.6, 1.5), 25 steps, η from 0.01 to 0.21), so
the curve and the path are exactly what the app would draw at each slider value. The notes and diagrams are invented
demo content (the cardiac cycle, a quadcopter, the hand, logistic regression), in the classes of the first video's
library plus ENGR 120 and CS 340.

**What to edit.**
- **Words and lengths:** `src/promo2/config.ts` (`text2`, `baseDurations`). Each scene's key moments are constants at
  the top of its file in `src/promo2/scenes/` (when the pointer hovers, pins, steps, drags).
- **The pieces of the app:** `src/promo2/ui.tsx` (toolbar, strips, pinned actions, the note window), `Flow.tsx` (the
  cardiac cycle), `Parts.tsx` (the drawings and their cards), `Plots.tsx` (the two plots and their sliders).
- **Sound:** the same generator, `npm run audio2`; the score's moments fall on the opener (a boom), the notes (drums
  and bass), the diagrams (claps), the quiet scene (the breakdown) and the ending, without the first video's bells.
  Its effects are its own, fewer and softer: five sounds in `public/audio/sfx2` (`npm run sfx2`), how loud each plays
  in `src/promo2/sound.ts`, and no bells or ticks. SFX2.md lists them, with ElevenLabs prompts for making your own and
  where to drop the files. The first video's effects and music are byte for byte what they were. After rendering,
  `scripts/master.py` sets the sound to −14 LUFS with its true peak under −1 dBTP (the picture is copied as it is).

