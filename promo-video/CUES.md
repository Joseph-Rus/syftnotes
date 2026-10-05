# Cue sheet: the demo, picture only

For **`out/demo-silent-vertical.mp4`** (1080×1920, 30 fps, no audio stream): the demo's picture alone, for laying Adam's narration and the sound effects under it in an editor. Made by `npm run render3:silent`.

- **Total length: 2:13.0** (3990 frames).
- **One timeline for both orientations.** The vertical and the landscape renders are cut identically, frame for frame, from the same `src/demo/timeline.json`, so this sheet fits either.
- Generated from `src/demo/timeline.json` (the final times) and `src/demo/plan.json` (the words, moments and prompts) by `npm run demo-cues`. Don't edit it by hand: change the plan, run `npm run demo-audio` (which runs this too), and render again.
- The timeline is the plan's, except that when it was last fitted (to scratch: macOS say, Reed (English (US)), 170 wpm) these scenes were lengthened, on the beat, to give their line room: Opener +0.5 s, Settings +0.5 s. Everything after a lengthened scene starts that much later than in the original list, its effects with it; the times below already include it.
- Every time is when the cue **starts**, as m:ss.s and as the frame at 30 fps (frame ÷ 30 = seconds).
- A line's **window** is the time from its start to the next scene's start: the most it can run. Leave a breath (about 0.4 s) at its end.
- Effects marked *added* are clicks and a tap the pointer makes on the way that weren't on the first list; they use the same file.

## Everything, in order

| Time | Frame | Cue | What |
|---|---|---|---|
| 0:00.0 | 0 | **Scene 1: Opener** | The black intro: the icon drawn in light, “Study Stash” rising, “Your lectures, written up and filed by class.”, glimpses of what’s to come |
| 0:00.0 | 0 | Music (optional) `music.mp3` | Under everything, from the first frame to the last, dipping under each line; at 120 BPM every scene starts on a beat. |
| 0:00.3 | 9 | Effect `sfx-whoosh-soft.mp3` | the screen lights up |
| 0:01.0 | 30 | **Adam, line 1** | “This is Study Stash. Your lectures, written up and filed by class.” Window 5.5 s, until 0:06.5. |
| 0:06.5 | 195 | **Scene 2: Setup** | Guided setup: Welcome to Study Stash, Claude picked; Claude’s chat: Just this Mac, Set up; the microphone check; You’re set up, Open Study Stash; the notification pointing at the S. in the menu bar |
| 0:06.5 | 195 | Effect `sfx-whoosh-soft.mp3` | into the guided setup |
| 0:07.0 | 210 | **Adam, line 2** | “Setting up is quick. Pick your AI, and it walks you through the rest.” Window 8.5 s, until 0:15.5. |
| 0:08.3 | 249 | Effect `sfx-click.mp3` | Claude is picked *added* |
| 0:10.0 | 300 | Effect `sfx-click.mp3` | Just this Mac, Set up |
| 0:12.5 | 375 | Effect `sfx-click.mp3` | Open Study Stash |
| 0:15.5 | 465 | **Scene 3: Record** | The menu bar’s S. opens its dropdown; Record · BIO 110; the recorder’s pill (red dot, time, level meter); the recorder opens and the live words come in; “50 minutes later”; Stop; “Recording saved” |
| 0:15.8 | 474 | Effect `sfx-click.mp3` | the S. in the menu bar opens its dropdown *added* |
| 0:16.0 | 480 | **Adam, line 3** | “Press record when class starts. The words show up as they're said, and the recording never leaves your computer.” Window 12.5 s, until 0:28.5. |
| 0:17.0 | 510 | Effect `sfx-click.mp3` | Record, in the menu bar's dropdown |
| 0:17.2 | 516 | Effect `sfx-record-start.mp3` | recording starts: the recorder pill appears |
| 0:18.7 | 561 | Effect `sfx-click.mp3` | the pill opens the recorder *added* |
| 0:27.0 | 810 | Effect `sfx-click.mp3` | Stop |
| 0:28.5 | 855 | **Scene 4: Notes first** | The library, BIO 110: “Writing the notes…” as the clock moves on; filed (the list, the count, the notification); the notes open with “Adding diagrams…”; “A minute later”; the diagram drops in; “Diagrams added” |
| 0:28.5 | 855 | Effect `sfx-whoosh-soft.mp3` | into the library |
| 0:29.0 | 870 | **Adam, line 4** | “Stop, and your notes are written for you, filed in the right class. The diagrams follow a minute later.” Window 11.5 s, until 0:40.5. |
| 0:31.5 | 945 | Effect `sfx-pop.mp3` | filed in BIO 110, the notes open |
| 0:37.0 | 1110 | Effect `sfx-shimmer.mp3` | the diagram drops into the notes |
| 0:40.5 | 1215 | **Scene 5: Explore** | The cardiac cycle diagram: a box lit with its neighbours, pinned (Explain this, Quiz me, Where was this said?, Zoom in); Step through, Next; Test yourself, a hidden box checked, Knew it |
| 0:41.0 | 1230 | **Adam, line 5** | “Every diagram is alive. Point at a box to see what it connects to, pin it, ask about it, step through it, or hide the words and test yourself.” Window 13.5 s, until 0:54.5. |
| 0:44.5 | 1335 | Effect `sfx-click.mp3` | a box is pinned |
| 0:44.8 | 1344 | Effect `sfx-pop.mp3` | its actions appear |
| 0:46.3 | 1389 | Effect `sfx-click.mp3` | Step through *added* |
| 0:47.5 | 1425 | Effect `sfx-click.mp3` | Next step |
| 0:48.7 | 1461 | Effect `sfx-click.mp3` | Test yourself *added* |
| 0:49.6 | 1488 | Effect `sfx-click.mp3` | a hidden box is checked *added* |
| 0:50.5 | 1515 | Effect `sfx-click.mp3` | Knew it |
| 0:54.5 | 1635 | **Scene 6: Plots** | CS 340’s notes: the sigmoid’s steepness slider dragged; gradient descent’s learning rate dragged down, then up |
| 0:54.5 | 1635 | Effect `sfx-whoosh-soft.mp3` | into the plots |
| 0:55.0 | 1650 | **Adam, line 6** | “Formulas become graphs you can drag, so you can see what every number does.” Window 9.5 s, until 1:04.5. |
| 0:56.5 | 1695 | Effect `sfx-slider.mp3` | the sigmoid's steepness is dragged (plays 1.6 s) |
| 1:00.5 | 1815 | Effect `sfx-slider.mp3` | gradient descent's learning rate is dragged (plays 1.8 s) |
| 1:04.5 | 1935 | **Scene 7: Drawings** | ENGR 120’s drone: the battery passed, the flight controller lit and pinned (its card); then NURS 210’s hand, a tendon lit |
| 1:05.0 | 1950 | **Adam, line 7** | “When a lecture describes something, like a drone or a hand, your notes can draw it, labelled.” Window 7.5 s, until 1:12.5. |
| 1:07.5 | 2025 | Effect `sfx-click.mp3` | the drone's flight controller is pinned |
| 1:07.8 | 2034 | Effect `sfx-pop.mp3` | its card appears |
| 1:12.5 | 2175 | **Scene 8: Ask** | CS 101’s notes with the Ask bar: “What’s on the midterm?” typed and sent; the answer written in from what was said |
| 1:12.5 | 2175 | Effect `sfx-whoosh-soft.mp3` | into Ask |
| 1:13.0 | 2190 | **Adam, line 8** | “Ask anything about your lectures, and get answers from what was actually said in class.” Window 10.5 s, until 1:23.5. |
| 1:14.0 | 2220 | Effect `sfx-keys.mp3` | typing "What's on the midterm?" (plays 1.9 s) |
| 1:16.5 | 2295 | Effect `sfx-click.mp3` | send |
| 1:23.5 | 2505 | **Scene 9: Canvas** | Due: soonest first, the late one marked Missing; Lab 3 opened: due date, points, instructions, rubric |
| 1:24.0 | 2520 | **Adam, line 9** | “Connect Canvas to see what's due next, with every assignment's details in one place.” Window 8.5 s, until 1:32.5. |
| 1:26.5 | 2595 | Effect `sfx-click.mp3` | an assignment is opened |
| 1:32.5 | 2775 | **Scene 10: Phone** | The phone app: Library; a tap into The cardiac cycle’s notes; the Due tab; the Ask tab |
| 1:33.0 | 2790 | **Adam, line 10** | “Your library comes with you on your phone: your notes, what's due, and Ask.” Window 7.5 s, until 1:40.5. |
| 1:35.0 | 2850 | Effect `sfx-tap.mp3` | a lecture is tapped |
| 1:36.0 | 2880 | Effect `sfx-tap.mp3` | the Due tab *added* |
| 1:37.0 | 2910 | Effect `sfx-tap.mp3` | the Ask tab |
| 1:40.5 | 3015 | **Scene 11: Settings** | Settings → AI engines: Rich notes switched off; Claude Code speed → Fast mode; Recording: When it’s written down → After class |
| 1:40.5 | 3015 | Effect `sfx-whoosh-soft.mp3` | into Settings |
| 1:41.0 | 3030 | **Adam, line 11** | “Make it yours. Turn rich notes on or off, use Claude Code's fast mode, or write lectures down after class, so your laptop stays quiet.” Window 11.0 s, until 1:52.0. |
| 1:43.5 | 3105 | Effect `sfx-click.mp3` | the Rich notes switch |
| 1:45.1 | 3153 | Effect `sfx-click.mp3` | the Claude Code speed menu opens *added* |
| 1:46.0 | 3180 | Effect `sfx-click.mp3` | Fast mode |
| 1:47.0 | 3210 | Effect `sfx-click.mp3` | Recording, in the sidebar *added* |
| 1:48.0 | 3240 | Effect `sfx-click.mp3` | When it's written down: After class |
| 1:52.0 | 3360 | **Scene 12: AI apps** | Settings → AI tool access: Connect, for Claude Desktop; “Added. Quit and reopen…”; then “Connected · started 13:41” |
| 1:52.5 | 3375 | **Adam, line 12** | “Connect Claude, Codex or Gemini to your library in one click.” Window 6.5 s, until 1:59.0. |
| 1:54.0 | 3420 | Effect `sfx-click.mp3` | Connect, for Claude Desktop |
| 1:59.0 | 3570 | **Scene 13: Promise** | The drawn laptop that locks; “Your recordings stay on your computer.”; “Free and open source, for Mac and Windows.” |
| 1:59.0 | 3570 | Effect `sfx-whoosh-soft.mp3` | into the promise |
| 1:59.5 | 3585 | **Adam, line 13** | “Your recordings never leave your computers. Free and open source, for Mac and Windows.” Window 6.5 s, until 2:06.0. |
| 2:06.0 | 3780 | **Scene 14: End card** | The end card: the icon, Study Stash, Free for Mac and Windows, study-stash-app.web.app; held to the last frame |
| 2:06.0 | 3780 | Effect `sfx-swell.mp3` | the end card |
| 2:06.5 | 3795 | **Adam, line 14** | “Study Stash. Download it free.” Window 6.5 s, until 2:13.0. |

## Adam's lines

| # | Scene | Starts | Frame | Window | Line | How to say it |
|---|---|---|---|---|---|---|
| 1 | Opener | 0:01.0 | 30 | 5.5 s | This is Study Stash. Your lectures, written up and filed by class. | warm and confident; a small beat after "Study Stash" |
| 2 | Setup | 0:07.0 | 210 | 8.5 s | Setting up is quick. Pick your AI, and it walks you through the rest. | easy and friendly, no hurry |
| 3 | Record | 0:16.0 | 480 | 12.5 s | Press record when class starts. The words show up as they're said, and the recording never leaves your computer. | clear; a little lift on "as they're said", sincere on the last part |
| 4 | Notes first | 0:29.0 | 870 | 11.5 s | Stop, and your notes are written for you, filed in the right class. The diagrams follow a minute later. | unhurried, a beat before "The diagrams" |
| 5 | Explore | 0:41.0 | 1230 | 13.5 s | Every diagram is alive. Point at a box to see what it connects to, pin it, ask about it, step through it, or hide the words and test yourself. | curious; the list with momentum, a little playful on "test yourself" |
| 6 | Plots | 0:55.0 | 1650 | 9.5 s | Formulas become graphs you can drag, so you can see what every number does. | enjoying it |
| 7 | Drawings | 1:05.0 | 1950 | 7.5 s | When a lecture describes something, like a drone or a hand, your notes can draw it, labelled. | a spark of delight on "draw it" |
| 8 | Ask | 1:13.0 | 2190 | 10.5 s | Ask anything about your lectures, and get answers from what was actually said in class. | plain and confident |
| 9 | Canvas | 1:24.0 | 2520 | 8.5 s | Connect Canvas to see what's due next, with every assignment's details in one place. | practical, helpful |
| 10 | Phone | 1:33.0 | 2790 | 7.5 s | Your library comes with you on your phone: your notes, what's due, and Ask. | light; a small pause at the colon |
| 11 | Settings | 1:41.0 | 3030 | 11.0 s | Make it yours. Turn rich notes on or off, use Claude Code's fast mode, or write lectures down after class, so your laptop stays quiet. | relaxed; a list, settling down on "stays quiet" |
| 12 | AI apps | 1:52.5 | 3375 | 6.5 s | Connect Claude, Codex or Gemini to your library in one click. | brisk |
| 13 | Promise | 1:59.5 | 3585 | 6.5 s | Your recordings never leave your computers. Free and open source, for Mac and Windows. | sincere, a touch slower; a beat between the sentences |
| 14 | End card | 2:06.5 | 3795 | 6.5 s | Study Stash. Download it free. | warm and confident, a beat after "Study Stash" |

Adam's settings, as the first two videos: Adam ("Engaging, Friendly and Bright"), Multilingual v2, speed 0.95, stability 85%, similarity 75%, style 40% (VOICEOVER3.md).

## Sound effects, by file

All soft, well under the voice. No bells and no chimes anywhere. On the ElevenLabs website: Sound Effects, paste the prompt, set the length, generate a few and keep the softest, cleanest one.

### `sfx-whoosh-soft.mp3`, 7 times

**Prompt:** Very soft, gentle airy whoosh for a slide transition, low and smooth, soft air moving, no hiss, no reverb tail  
**Length:** 1 s

| Time | Frame | Moment |
|---|---|---|
| 0:00.3 | 9 | the screen lights up |
| 0:06.5 | 195 | into the guided setup |
| 0:28.5 | 855 | into the library |
| 0:54.5 | 1635 | into the plots |
| 1:12.5 | 2175 | into Ask |
| 1:40.5 | 3015 | into Settings |
| 1:59.0 | 3570 | into the promise |

### `sfx-click.mp3`, 22 times

**Prompt:** Very soft trackpad click, a single muted tap, close-miked, dry, no reverb, no high-pitched tick  
**Length:** 0.5 s (the shortest)

| Time | Frame | Moment |
|---|---|---|
| 0:08.3 | 249 | Claude is picked *added* |
| 0:10.0 | 300 | Just this Mac, Set up |
| 0:12.5 | 375 | Open Study Stash |
| 0:15.8 | 474 | the S. in the menu bar opens its dropdown *added* |
| 0:17.0 | 510 | Record, in the menu bar's dropdown |
| 0:18.7 | 561 | the pill opens the recorder *added* |
| 0:27.0 | 810 | Stop |
| 0:44.5 | 1335 | a box is pinned |
| 0:46.3 | 1389 | Step through *added* |
| 0:47.5 | 1425 | Next step |
| 0:48.7 | 1461 | Test yourself *added* |
| 0:49.6 | 1488 | a hidden box is checked *added* |
| 0:50.5 | 1515 | Knew it |
| 1:07.5 | 2025 | the drone's flight controller is pinned |
| 1:16.5 | 2295 | send |
| 1:26.5 | 2595 | an assignment is opened |
| 1:43.5 | 3105 | the Rich notes switch |
| 1:45.1 | 3153 | the Claude Code speed menu opens *added* |
| 1:46.0 | 3180 | Fast mode |
| 1:47.0 | 3210 | Recording, in the sidebar *added* |
| 1:48.0 | 3240 | When it's written down: After class |
| 1:54.0 | 3420 | Connect, for Claude Desktop |

### `sfx-record-start.mp3`, 1 time

**Prompt:** Soft rounded start-recording sound, a low muted thup with a gentle rise, like a small button pressed into felt, dry, no beep, no bell, no chime  
**Length:** 1 s

| Time | Frame | Moment |
|---|---|---|
| 0:17.2 | 516 | recording starts: the recorder pill appears |

### `sfx-pop.mp3`, 3 times

**Prompt:** Soft rounded interface pop, like a small card settling into place, a gentle low wooden tok, dry, no bell, no chime, no reverb  
**Length:** 0.5 s

| Time | Frame | Moment |
|---|---|---|
| 0:31.5 | 945 | filed in BIO 110, the notes open |
| 0:44.8 | 1344 | its actions appear |
| 1:07.8 | 2034 | its card appears |

### `sfx-shimmer.mp3`, 1 time

**Prompt:** Soft airy shimmer, a breath of light rising, like a gentle brush sweep over fine sand, warm and quiet, no bells, no chimes, no ringing tones  
**Length:** 1.5 s

| Time | Frame | Moment |
|---|---|---|
| 0:37.0 | 1110 | the diagram drops into the notes |

### `sfx-slider.mp3`, 2 times

**Prompt:** Soft friction of a small knob slid smoothly along a track, quiet and steady, close-miked, no clicks, no ticks  
**Length:** 2 s

| Time | Frame | Moment |
|---|---|---|
| 0:56.5 | 1695 | the sigmoid's steepness is dragged (plays 1.6 s) |
| 1:00.5 | 1815 | gradient descent's learning rate is dragged (plays 1.8 s) |

### `sfx-keys.mp3`, 1 time

**Prompt:** Quiet laptop typing, about two seconds of soft key presses, low muted thocks, a short sentence typed at an easy pace, dry, no clatter  
**Length:** 2 s

| Time | Frame | Moment |
|---|---|---|
| 1:14.0 | 2220 | typing "What's on the midterm?" (plays 1.9 s) |

### `sfx-tap.mp3`, 3 times

**Prompt:** One soft tap of a fingertip on a phone's glass screen, muted and rounded, very short, dry, no click, no tick  
**Length:** 0.5 s (the shortest)

| Time | Frame | Moment |
|---|---|---|
| 1:35.0 | 2850 | a lecture is tapped |
| 1:36.0 | 2880 | the Due tab *added* |
| 1:37.0 | 2910 | the Ask tab |

### `sfx-swell.mp3`, 1 time

**Prompt:** Warm soft swell of a felt piano and string pad chord in D major, rising gently over a second then fading over three seconds, calm and hopeful, no bells, no chimes, no percussion  
**Length:** 4 s

| Time | Frame | Moment |
|---|---|---|
| 2:06.0 | 3780 | the end card |

### `music.mp3` (optional)

**Prompt:** Warm, light instrumental bed for a calm product demo: a soft pad in D major, a gentle plucked arpeggio, soft drums coming in around 0:15, a quieter breakdown for the last fifteen seconds and a warm final chord, 120 BPM, no vocals, no bells, no chimes  
**Length:** the whole video, 2:13.0 (3990 frames)

| Time | Frame | Moment |
|---|---|---|
| 0:00.0 | 0 | Under everything, from the first frame to the last, dipping under each line; at 120 BPM every scene starts on a beat. |
| 2:13.0 | 3990 | The end of the video: fade it out over the last second or two. |

## Scenes

| # | Scene | Starts | Frame | Length | On screen |
|---|---|---|---|---|---|
| 1 | Opener | 0:00.0 | 0 | 6.5 s | The black intro: the icon drawn in light, “Study Stash” rising, “Your lectures, written up and filed by class.”, glimpses of what’s to come |
| 2 | Setup | 0:06.5 | 195 | 9.0 s | Guided setup: Welcome to Study Stash, Claude picked; Claude’s chat: Just this Mac, Set up; the microphone check; You’re set up, Open Study Stash; the notification pointing at the S. in the menu bar |
| 3 | Record | 0:15.5 | 465 | 13.0 s | The menu bar’s S. opens its dropdown; Record · BIO 110; the recorder’s pill (red dot, time, level meter); the recorder opens and the live words come in; “50 minutes later”; Stop; “Recording saved” |
| 4 | Notes first | 0:28.5 | 855 | 12.0 s | The library, BIO 110: “Writing the notes…” as the clock moves on; filed (the list, the count, the notification); the notes open with “Adding diagrams…”; “A minute later”; the diagram drops in; “Diagrams added” |
| 5 | Explore | 0:40.5 | 1215 | 14.0 s | The cardiac cycle diagram: a box lit with its neighbours, pinned (Explain this, Quiz me, Where was this said?, Zoom in); Step through, Next; Test yourself, a hidden box checked, Knew it |
| 6 | Plots | 0:54.5 | 1635 | 10.0 s | CS 340’s notes: the sigmoid’s steepness slider dragged; gradient descent’s learning rate dragged down, then up |
| 7 | Drawings | 1:04.5 | 1935 | 8.0 s | ENGR 120’s drone: the battery passed, the flight controller lit and pinned (its card); then NURS 210’s hand, a tendon lit |
| 8 | Ask | 1:12.5 | 2175 | 11.0 s | CS 101’s notes with the Ask bar: “What’s on the midterm?” typed and sent; the answer written in from what was said |
| 9 | Canvas | 1:23.5 | 2505 | 9.0 s | Due: soonest first, the late one marked Missing; Lab 3 opened: due date, points, instructions, rubric |
| 10 | Phone | 1:32.5 | 2775 | 8.0 s | The phone app: Library; a tap into The cardiac cycle’s notes; the Due tab; the Ask tab |
| 11 | Settings | 1:40.5 | 3015 | 11.5 s | Settings → AI engines: Rich notes switched off; Claude Code speed → Fast mode; Recording: When it’s written down → After class |
| 12 | AI apps | 1:52.0 | 3360 | 7.0 s | Settings → AI tool access: Connect, for Claude Desktop; “Added. Quit and reopen…”; then “Connected · started 13:41” |
| 13 | Promise | 1:59.0 | 3570 | 7.0 s | The drawn laptop that locks; “Your recordings stay on your computer.”; “Free and open source, for Mac and Windows.” |
| 14 | End card | 2:06.0 | 3780 | 7.0 s | The end card: the icon, Study Stash, Free for Mac and Windows, study-stash-app.web.app; held to the last frame |

Each scene cross-fades into the next over 12 frames (0.4 s), starting at the next scene's time above. The video ends at 2:13.0.
