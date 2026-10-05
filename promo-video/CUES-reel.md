# Cue sheet: the Instagram reel, picture only

For **`out/reel-silent.mp4`** (1080×1920, 30 fps, no audio stream): the reel's picture alone, for laying Adam's narration and the sound effects under it in an editor. Made by `npm run render:reel:silent`.

> **Safe area, for whoever edits.** Instagram draws over the top 220 px (its bar), the bottom 420 px (the caption, the audio line and the buttons) and the right 130 px (like, comment, share) of the 1080×1920 frame. Every headline and everything that matters in the reel sits inside, in x 0–950, y 220–1500. Keep anything you add (captions, stickers, text) in there too. `ReelGuides` in the Studio (`npm run studio`) plays the reel with those areas shaded.

- **Total length: 0:57.5** (1725 frames). Instagram's limit for this is under a minute.
- Generated from `src/reel/plan.json` by `npm run reel-cues`. Don't edit it by hand: change the plan, run it again and render again. The plan's times are final: nothing in the reel is stretched to fit a line, so the picture never moves under what you lay by this sheet.
- Every time is when the cue **starts**, as m:ss.s and as the frame at 30 fps (frame ÷ 30 = seconds).
- A line's **window** is the time from its start to the next scene's start: the most it can run. Leave a breath (about 0.3 s) at its end. Each line is meant to start about 0.3 s into its scene.
- The effects are the demo's nine files (same names, same prompts; no bells anywhere). Every pointer click has a click, every tap on the phone a tap. A *plays* time is how long that effect should last (a drag, the typing): trim or fade it there.
- No music is cued. If you add some, keep it well under the voice and fade it out over the last two seconds.

## Everything, in order

| Time | Frame | Cue | What |
|---|---|---|---|
| 0:00.0 | 0 | **Scene 1: Opener** | The black intro, fast: a line of light draws the icon from the first frame, “Study Stash” rises, glimpses of what’s to come float in, and the night lifts into the app. |
| 0:00.2 | 6 | Effect `sfx-whoosh-soft.mp3` | the screen lights up |
| 0:00.3 | 9 | **Adam, line 1** | “Study Stash.” Window 2.2 s, until 0:02.5. |
| 0:02.5 | 75 | **Scene 2: Record** | The Mac’s top right corner, close: the menu bar’s S. opens its dropdown; Record · BIO 110; the recorder’s pill; the recorder opens and the live words come in; “50 minutes later”; Stop. Headline: “Hit record. Words show up live.” |
| 0:02.8 | 84 | Effect `sfx-click.mp3` | the S. in the menu bar opens its dropdown |
| 0:02.8 | 84 | **Adam, line 2** | “Hit record in class, and the words show up as they're said.” Window 5.7 s, until 0:08.5. |
| 0:03.4 | 102 | Effect `sfx-click.mp3` | Record, in the dropdown |
| 0:03.5 | 105 | Effect `sfx-record-start.mp3` | recording starts: the recorder pill appears |
| 0:04.1 | 123 | Effect `sfx-click.mp3` | the pill opens the recorder |
| 0:08.0 | 240 | Effect `sfx-click.mp3` | Stop |
| 0:08.5 | 255 | **Scene 3: Notes first** | BIO 110’s window: “Writing the notes…”; filed (the notes open, the notification “Filed in BIO 110”) with “Adding diagrams…”; “A minute later”; the diagram drops in; “Diagrams added”. Headline: “Notes, written for you. Diagrams and all.” |
| 0:08.5 | 255 | Effect `sfx-whoosh-soft.mp3` | into the library |
| 0:08.8 | 264 | **Adam, line 3** | “Stop, and your notes are written and filed for you, diagrams and all.” Window 7.7 s, until 0:16.5. |
| 0:10.0 | 300 | Effect `sfx-pop.mp3` | filed in BIO 110: the notes open |
| 0:12.7 | 381 | Effect `sfx-shimmer.mp3` | the diagram drops into the notes |
| 0:16.5 | 495 | **Scene 4: Explore** | The cardiac cycle diagram: a box lit with its neighbours, pinned (Explain this, Quiz me, Where was this said?, Zoom in); Step through, Next; Test yourself, a hidden box checked, Knew it. Headline: “Diagrams you explore. Point, pin, step, test.” |
| 0:16.8 | 504 | **Adam, line 4** | “Point at a box, pin it, step through it, or test yourself.” Window 6.7 s, until 0:23.5. |
| 0:17.9 | 537 | Effect `sfx-click.mp3` | a box is pinned: its four actions appear |
| 0:18.9 | 567 | Effect `sfx-click.mp3` | Step through |
| 0:19.6 | 588 | Effect `sfx-click.mp3` | Next step |
| 0:20.3 | 609 | Effect `sfx-click.mp3` | Test yourself |
| 0:21.0 | 630 | Effect `sfx-click.mp3` | a hidden box is checked |
| 0:21.7 | 651 | Effect `sfx-click.mp3` | Knew it |
| 0:23.5 | 705 | **Scene 5: Plots** | CS 340’s notes: the sigmoid’s steepness dragged up to 5 and partway back. Headline: “Graphs you can drag. Watch the formula move.” |
| 0:23.5 | 705 | Effect `sfx-whoosh-soft.mp3` | into the plots |
| 0:23.8 | 714 | **Adam, line 5** | “Drag a formula and watch it move.” Window 4.2 s, until 0:28.0. |
| 0:24.1 | 723 | Effect `sfx-slider.mp3` | the sigmoid's steepness is dragged up, and partway back (plays 2.0 s) |
| 0:28.0 | 840 | **Scene 6: Drawings** | ENGR 120’s drone: the battery passed, the flight controller lit and pinned (its card). Headline: “Labelled drawings. Of what class describes.” |
| 0:28.3 | 849 | **Adam, line 6** | “It can even draw what the lecture describes.” Window 4.2 s, until 0:32.5. |
| 0:29.5 | 885 | Effect `sfx-click.mp3` | the drone's flight controller is pinned |
| 0:29.6 | 888 | Effect `sfx-pop.mp3` | its card appears |
| 0:32.5 | 975 | **Scene 7: Ask** | CS 101’s notes with the Ask bar: “What’s on the midterm?” typed and sent; the answer streams in from what was said. Headline: “Ask your lectures. Answers from class.” |
| 0:32.8 | 984 | **Adam, line 7** | “Ask your lectures anything, and get answers from class.” Window 5.2 s, until 0:38.0. |
| 0:33.1 | 993 | Effect `sfx-keys.mp3` | typing "What's on the midterm?" (plays 1.2 s) |
| 0:34.5 | 1035 | Effect `sfx-click.mp3` | send |
| 0:38.0 | 1140 | **Scene 8: Canvas** | Due: Lab 2 write-up marked Missing, then this week’s, soonest first; Lab 3 opened: due date, points, instructions, rubric. Headline: “Canvas, connected. What’s due, soonest first.” |
| 0:38.0 | 1140 | Effect `sfx-whoosh-soft.mp3` | into Due, from Canvas |
| 0:38.3 | 1149 | **Adam, line 8** | “See what's due from Canvas, soonest first.” Window 4.2 s, until 0:42.5. |
| 0:40.5 | 1215 | Effect `sfx-click.mp3` | Lab 3 is opened |
| 0:42.5 | 1275 | **Scene 9: Phone** | The phone app: Library; a tap into The cardiac cycle’s notes; a tap on Due. Headline: “On your phone, too. Notes and what’s due.” |
| 0:42.5 | 1275 | Effect `sfx-whoosh-soft.mp3` | onto the phone |
| 0:42.8 | 1284 | **Adam, line 9** | “Your notes and what's due, on your phone too.” Window 4.2 s, until 0:47.0. |
| 0:43.3 | 1299 | Effect `sfx-tap.mp3` | a lecture is tapped |
| 0:44.7 | 1341 | Effect `sfx-tap.mp3` | the Due tab |
| 0:47.0 | 1410 | **Scene 10: Settings** | Settings → AI engines: Rich notes switched off; Claude Code speed → Fast mode; Recording: When it’s written down → After class. Headline: “Make it yours. Notes your way.” |
| 0:47.3 | 1419 | **Adam, line 10** | “Make it yours: rich notes, fast mode, after class.” Window 5.2 s, until 0:52.5. |
| 0:48.2 | 1446 | Effect `sfx-click.mp3` | the Rich notes switch |
| 0:48.9 | 1467 | Effect `sfx-click.mp3` | the Claude Code speed menu opens |
| 0:49.4 | 1482 | Effect `sfx-click.mp3` | Fast mode |
| 0:50.0 | 1500 | Effect `sfx-click.mp3` | Recording, in the sidebar |
| 0:50.6 | 1518 | Effect `sfx-click.mp3` | When it's written down: After class |
| 0:52.5 | 1575 | **Scene 11: End card** | The end card: the icon, Study Stash, Free for Mac and Windows, study-stash-app.web.app, the marks for Mac and Windows; held to the last frame. |
| 0:52.5 | 1575 | Effect `sfx-swell.mp3` | the end card |
| 0:52.8 | 1584 | **Adam, line 11** | “Study Stash. Free for Mac and Windows.” Window 4.7 s, until 0:57.5. |
| 0:57.5 | 1725 | **The end** | The last frame of the end card. |

## Adam's lines

| # | Scene | Starts | Frame | Window | Line | How to say it |
|---|---|---|---|---|---|---|
| 1 | Opener | 0:00.3 | 9 | 2.2 s | Study Stash. | warm and confident, quick |
| 2 | Record | 0:02.8 | 84 | 5.7 s | Hit record in class, and the words show up as they're said. | bright, a little lift on "as they're said" |
| 3 | Notes first | 0:08.8 | 264 | 7.7 s | Stop, and your notes are written and filed for you, diagrams and all. | a small beat after "Stop", then easy |
| 4 | Explore | 0:16.8 | 504 | 6.7 s | Point at a box, pin it, step through it, or test yourself. | a list with momentum, playful on "test yourself" |
| 5 | Plots | 0:23.8 | 714 | 4.2 s | Drag a formula and watch it move. | enjoying it |
| 6 | Drawings | 0:28.3 | 849 | 4.2 s | It can even draw what the lecture describes. | a spark of delight on "draw" |
| 7 | Ask | 0:32.8 | 984 | 5.2 s | Ask your lectures anything, and get answers from class. | plain and confident |
| 8 | Canvas | 0:38.3 | 1149 | 4.2 s | See what's due from Canvas, soonest first. | practical, brisk |
| 9 | Phone | 0:42.8 | 1284 | 4.2 s | Your notes and what's due, on your phone too. | light |
| 10 | Settings | 0:47.3 | 1419 | 5.2 s | Make it yours: rich notes, fast mode, after class. | relaxed, a little pause at the colon, then a quick list |
| 11 | End card | 0:52.8 | 1584 | 4.7 s | Study Stash. Free for Mac and Windows. | warm, a beat after "Study Stash" |

Adam's settings, as the demo: Adam ("Engaging, Friendly and Bright"), Multilingual v2, speed 0.95, stability 85%, similarity 75%, style 40% (VOICEOVER3.md). The reel moves faster than the demo, so if a line runs past its window, try speed 1.0 for that line rather than cutting words.

All eleven lines, as one script (a pause between each):

> Study Stash.
>
> Hit record in class, and the words show up as they're said.
>
> Stop, and your notes are written and filed for you, diagrams and all.
>
> Point at a box, pin it, step through it, or test yourself.
>
> Drag a formula and watch it move.
>
> It can even draw what the lecture describes.
>
> Ask your lectures anything, and get answers from class.
>
> See what's due from Canvas, soonest first.
>
> Your notes and what's due, on your phone too.
>
> Make it yours: rich notes, fast mode, after class.
>
> Study Stash. Free for Mac and Windows.

## Sound effects, by file

All soft, well under the voice. No bells and no chimes anywhere. On the ElevenLabs website: Sound Effects, paste the prompt, set the length, generate a few and keep the softest, cleanest one. If you already made them for the demo, they are the same files.

### `sfx-whoosh-soft.mp3`, 5 times

**Prompt:** Very soft, gentle airy whoosh for a slide transition, low and smooth, soft air moving, no hiss, no reverb tail  
**Length:** 1 s

| Time | Frame | Moment |
|---|---|---|
| 0:00.2 | 6 | the screen lights up |
| 0:08.5 | 255 | into the library |
| 0:23.5 | 705 | into the plots |
| 0:38.0 | 1140 | into Due, from Canvas |
| 0:42.5 | 1275 | onto the phone |

### `sfx-click.mp3`, 18 times

**Prompt:** Very soft trackpad click, a single muted tap, close-miked, dry, no reverb, no high-pitched tick  
**Length:** 0.5 s (the shortest)

| Time | Frame | Moment |
|---|---|---|
| 0:02.8 | 84 | the S. in the menu bar opens its dropdown |
| 0:03.4 | 102 | Record, in the dropdown |
| 0:04.1 | 123 | the pill opens the recorder |
| 0:08.0 | 240 | Stop |
| 0:17.9 | 537 | a box is pinned: its four actions appear |
| 0:18.9 | 567 | Step through |
| 0:19.6 | 588 | Next step |
| 0:20.3 | 609 | Test yourself |
| 0:21.0 | 630 | a hidden box is checked |
| 0:21.7 | 651 | Knew it |
| 0:29.5 | 885 | the drone's flight controller is pinned |
| 0:34.5 | 1035 | send |
| 0:40.5 | 1215 | Lab 3 is opened |
| 0:48.2 | 1446 | the Rich notes switch |
| 0:48.9 | 1467 | the Claude Code speed menu opens |
| 0:49.4 | 1482 | Fast mode |
| 0:50.0 | 1500 | Recording, in the sidebar |
| 0:50.6 | 1518 | When it's written down: After class |

### `sfx-record-start.mp3`, 1 time

**Prompt:** Soft rounded start-recording sound, a low muted thup with a gentle rise, like a small button pressed into felt, dry, no beep, no bell, no chime  
**Length:** 1 s

| Time | Frame | Moment |
|---|---|---|
| 0:03.5 | 105 | recording starts: the recorder pill appears |

### `sfx-pop.mp3`, 2 times

**Prompt:** Soft rounded interface pop, like a small card settling into place, a gentle low wooden tok, dry, no bell, no chime, no reverb  
**Length:** 0.5 s

| Time | Frame | Moment |
|---|---|---|
| 0:10.0 | 300 | filed in BIO 110: the notes open |
| 0:29.6 | 888 | its card appears |

### `sfx-shimmer.mp3`, 1 time

**Prompt:** Soft airy shimmer, a breath of light rising, like a gentle brush sweep over fine sand, warm and quiet, no bells, no chimes, no ringing tones  
**Length:** 1.5 s

| Time | Frame | Moment |
|---|---|---|
| 0:12.7 | 381 | the diagram drops into the notes |

### `sfx-slider.mp3`, 1 time

**Prompt:** Soft friction of a small knob slid smoothly along a track, quiet and steady, close-miked, no clicks, no ticks  
**Length:** 2 s

| Time | Frame | Moment |
|---|---|---|
| 0:24.1 | 723 | the sigmoid's steepness is dragged up, and partway back (plays 2.0 s) |

### `sfx-keys.mp3`, 1 time

**Prompt:** Quiet laptop typing, about two seconds of soft key presses, low muted thocks, a short sentence typed at an easy pace, dry, no clatter  
**Length:** 2 s

| Time | Frame | Moment |
|---|---|---|
| 0:33.1 | 993 | typing "What's on the midterm?" (plays 1.2 s) |

### `sfx-tap.mp3`, 2 times

**Prompt:** One soft tap of a fingertip on a phone's glass screen, muted and rounded, very short, dry, no click, no tick  
**Length:** 0.5 s (the shortest)

| Time | Frame | Moment |
|---|---|---|
| 0:43.3 | 1299 | a lecture is tapped |
| 0:44.7 | 1341 | the Due tab |

### `sfx-swell.mp3`, 1 time

**Prompt:** Warm soft swell of a felt piano and string pad chord in D major, rising gently over a second then fading over three seconds, calm and hopeful, no bells, no chimes, no percussion  
**Length:** 4 s

| Time | Frame | Moment |
|---|---|---|
| 0:52.5 | 1575 | the end card |

## Scenes

| # | Scene | Starts | Frame | Length | Headline | On screen |
|---|---|---|---|---|---|---|
| 1 | Opener | 0:00.0 | 0 | 2.5 s | (none) | The black intro, fast: a line of light draws the icon from the first frame, “Study Stash” rises, glimpses of what’s to come float in, and the night lifts into the app |
| 2 | Record | 0:02.5 | 75 | 6.0 s | Hit record. Words show up live. | The Mac’s top right corner, close: the menu bar’s S. opens its dropdown; Record · BIO 110; the recorder’s pill; the recorder opens and the live words come in; “50 minutes later”; Stop |
| 3 | Notes first | 0:08.5 | 255 | 8.0 s | Notes, written for you. Diagrams and all. | BIO 110’s window: “Writing the notes…”; filed (the notes open, the notification “Filed in BIO 110”) with “Adding diagrams…”; “A minute later”; the diagram drops in; “Diagrams added” |
| 4 | Explore | 0:16.5 | 495 | 7.0 s | Diagrams you explore. Point, pin, step, test. | The cardiac cycle diagram: a box lit with its neighbours, pinned (Explain this, Quiz me, Where was this said?, Zoom in); Step through, Next; Test yourself, a hidden box checked, Knew it |
| 5 | Plots | 0:23.5 | 705 | 4.5 s | Graphs you can drag. Watch the formula move. | CS 340’s notes: the sigmoid’s steepness dragged up to 5 and partway back |
| 6 | Drawings | 0:28.0 | 840 | 4.5 s | Labelled drawings. Of what class describes. | ENGR 120’s drone: the battery passed, the flight controller lit and pinned (its card) |
| 7 | Ask | 0:32.5 | 975 | 5.5 s | Ask your lectures. Answers from class. | CS 101’s notes with the Ask bar: “What’s on the midterm?” typed and sent; the answer streams in from what was said |
| 8 | Canvas | 0:38.0 | 1140 | 4.5 s | Canvas, connected. What’s due, soonest first. | Due: Lab 2 write-up marked Missing, then this week’s, soonest first; Lab 3 opened: due date, points, instructions, rubric |
| 9 | Phone | 0:42.5 | 1275 | 4.5 s | On your phone, too. Notes and what’s due. | The phone app: Library; a tap into The cardiac cycle’s notes; a tap on Due |
| 10 | Settings | 0:47.0 | 1410 | 5.5 s | Make it yours. Notes your way. | Settings → AI engines: Rich notes switched off; Claude Code speed → Fast mode; Recording: When it’s written down → After class |
| 11 | End card | 0:52.5 | 1575 | 5.0 s | (none) | The end card: the icon, Study Stash, Free for Mac and Windows, study-stash-app.web.app, the marks for Mac and Windows; held to the last frame |

Each scene cross-fades into the next over 10 frames (0.3 s), starting at the next scene's time above. The reel ends at 0:57.5.
