# Voiceover and sound effects for the third video (the demo)

The third video is a full demo of Study Stash, 2 minutes 12 seconds, landscape and vertical. It is built to a fixed
timeline (`src/demo/plan.json`): every scene starts at a set time, Adam's line for it starts half a second later, and
every sound effect has its moment. Make the narration and the effects from this page, drop the files into
`promo-video/elevenlabs3/`, and run two commands. Nothing in the code needs to change.

## The script

| # | Scene | Starts | Line starts | Line | How to say it |
|---|---|---|---|---|---|
| 1 | Opener | 0.0 s | 1.0 s | This is Study Stash. Your lectures, written up and filed by class. | warm and confident; a small beat after "Study Stash" |
| 2 | Setup | 6.0 s | 6.5 s | Setting up is quick. Pick your AI, and it walks you through the rest. | easy and friendly, no hurry |
| 3 | Record | 15.0 s | 15.5 s | Press record when class starts. The words show up as they're said, and the recording never leaves your computer. | clear; a little lift on "as they're said", sincere on the last part |
| 4 | Notes first | 28.0 s | 28.5 s | Stop, and your notes are written for you, filed in the right class. The diagrams follow a minute later. | unhurried, a beat before "The diagrams" |
| 5 | Explore | 40.0 s | 40.5 s | Every diagram is alive. Point at a box to see what it connects to, pin it, ask about it, step through it, or hide the words and test yourself. | curious; the list with momentum, a little playful on "test yourself" |
| 6 | Plots | 54.0 s | 54.5 s | Formulas become graphs you can drag, so you can see what every number does. | enjoying it |
| 7 | Drawings | 64.0 s | 64.5 s | When a lecture describes something, like a drone or a hand, your notes can draw it, labelled. | a spark of delight on "draw it" |
| 8 | Ask | 72.0 s | 72.5 s | Ask anything about your lectures, and get answers from what was actually said in class. | plain and confident |
| 9 | Canvas | 83.0 s | 83.5 s | Connect Canvas to see what's due next, with every assignment's details in one place. | practical, helpful |
| 10 | Phone | 92.0 s | 92.5 s | Your library comes with you on your phone: your notes, what's due, and Ask. | light; a small pause at the colon |
| 11 | Settings | 100.0 s | 100.5 s | Make it yours. Turn rich notes on or off, use Claude Code's fast mode, or write lectures down after class, so your laptop stays quiet. | relaxed; a list, settling down on "stays quiet" |
| 12 | AI apps | 111.0 s | 111.5 s | Connect Claude, Codex or Gemini to your library in one click. | brisk |
| 13 | Promise | 118.0 s | 118.5 s | Your recordings never leave your computers. Free and open source, for Mac and Windows. | sincere, a touch slower; a beat between the sentences |
| 14 | End card | 125.0 s | 125.5 s | Study Stash. Download it free. | warm and confident, a beat after "Study Stash" |

A line has until about 0.4 s before the next scene starts. If one runs longer, that scene is lengthened by whole beats
(half a second each, the music is 120 BPM) and everything after it moves by the same amount, its effects included, so
each sound stays with its moment. Lines 1, 5 and 11 are the tightest. The script's text also lives in
`src/demo/plan.json`, which the video and the audio script both read; edit it there.

Every claim is true of the app as it is:
- **The words show up as they're said.** On a Mac with Apple silicon the recorder's live words arrive about a second
  after they're said; the video shows them coming in a few words at a time, a second apart. The recording stays on
  the computer; the library gets the transcript (Settings → Recording says so).
- **Notes are written, not instant.** The video shows "Writing the notes…" while the menu bar's clock moves on, then
  "Filed in BIO 110", then "Adding diagrams…", "A minute later", the diagram arriving and "Diagrams added".
- **Pick your AI, and it walks you through the rest.** The guided setup: Claude (or ChatGPT) is picked, then Claude
  walks through how you'll use it (Just this Mac), the microphone, the transcription model and the rest in a chat.
- **Fast mode** is Claude Code's own (it needs usage credits on the Claude account); **After class** only records
  during the lecture and writes it down once you stop, which saves battery.
- **One click:** each AI app's Connect adds Study Stash to it; the app is then reopened to load it, and Settings says
  "Connected".
- **"Your computers"** (plural) covers a laptop alone or a laptop with a library at home, as in the first two videos.

## Making the narration on the ElevenLabs website

Use the first two videos' voice so the three sound like one: **Adam, "Engaging, Friendly and Bright"**, model
**Multilingual v2**, speed **0.95**, stability **85%**, similarity **75%**, style **40%** (VOICEOVER.md, VOICEOVER2.md).

1. Open **Text to Speech**, pick Adam, set the settings above.
2. Paste the script below: each line its own paragraph, a blank line between. Generate the whole thing as **one
   take**, listen, regenerate until you like it, and download the MP3.
3. Save it as `promo-video/elevenlabs3/voice.mp3` (make the folder). Keep a copy in `voice-takes/` too, e.g.
   `voice-takes/adam-demo-<date>.mp3`.

```
This is Study Stash. Your lectures, written up and filed by class.

Setting up is quick. Pick your AI, and it walks you through the rest.

Press record when class starts. The words show up as they're said, and the recording never leaves your computer.

Stop, and your notes are written for you, filed in the right class. The diagrams follow a minute later.

Every diagram is alive. Point at a box to see what it connects to, pin it, ask about it, step through it, or hide the words and test yourself.

Formulas become graphs you can drag, so you can see what every number does.

When a lecture describes something, like a drone or a hand, your notes can draw it, labelled.

Ask anything about your lectures, and get answers from what was actually said in class.

Connect Canvas to see what's due next, with every assignment's details in one place.

Your library comes with you on your phone: your notes, what's due, and Ask.

Make it yours. Turn rich notes on or off, use Claude Code's fast mode, or write lectures down after class, so your laptop stays quiet.

Connect Claude, Codex or Gemini to your library in one click.

Your recordings never leave your computers. Free and open source, for Mac and Windows.

Study Stash. Download it free.
```

The take is cut into its 14 lines by **word timings**, not by guessing at pauses: Whisper (large-v3, on this Mac,
through the same Whisper.net the app uses) finds every word and when it was said, the words are matched to the script,
and each line is cut in the silence just before its first word. Each line keeps everything up to the next line's cut,
so quiet endings (the "sh" of "Stash") are never clipped. The script prints where it cut each line and warns if a cut
lands in speech. If a cut is ever wrong, give the 13 cuts yourself, in seconds into the take, where lines 2 to 14
start: `npm run demo-audio -- --cuts 5.2,11.9,…`.

## The sound effects

Nine sounds, all soft. **No bells and no chimes anywhere** (the score has none either). On the ElevenLabs website,
open **Sound Effects**, paste a prompt, set the duration, generate a few and keep the softest, cleanest one: nothing
ringing, nothing after the sound itself. Save each in `promo-video/elevenlabs3/` under its name (`.mp3`, `.wav`,
`.m4a`, `.aac`, `.flac` and `.ogg` all work). You can make one, some or all of them; any that's missing is made in code.

| File | ElevenLabs prompt | Duration | Plays at (seconds) |
|---|---|---|---|
| `sfx-whoosh-soft.mp3` | Very soft, gentle airy whoosh for a slide transition, low and smooth, soft air moving, no hiss, no reverb tail | 1 s | 0.3, 6.0, 28.0, 54.0, 72.0, 100.0, 118.0 |
| `sfx-click.mp3` | Very soft trackpad click, a single muted tap, close-miked, dry, no reverb, no high-pitched tick | 0.5 s (the shortest) | 9.5, 12.0, 16.5, 26.5, 44.0, 47.0, 50.0, 67.0, 76.0, 86.0, 103.0, 105.5, 107.5, 113.0; and the pointer's other clicks at 7.8, 15.3, 18.2, 45.8, 48.2, 49.1, 104.6, 106.5 |
| `sfx-record-start.mp3` | Soft rounded start-recording sound, a low muted thup with a gentle rise, like a small button pressed into felt, dry, no beep, no bell, no chime | 1 s | 16.7 |
| `sfx-pop.mp3` | Soft rounded interface pop, like a small card settling into place, a gentle low wooden tok, dry, no bell, no chime, no reverb | 0.5 s | 31.0, 44.3, 67.3 |
| `sfx-shimmer.mp3` | Soft airy shimmer, a breath of light rising, like a gentle brush sweep over fine sand, warm and quiet, no bells, no chimes, no ringing tones | 1.5 s | 36.5 |
| `sfx-slider.mp3` | Soft friction of a small knob slid smoothly along a track, quiet and steady, close-miked, no clicks, no ticks | 2 s | 56.0 (plays 1.6 s), 60.0 (plays 1.8 s) |
| `sfx-keys.mp3` | Quiet laptop typing, about two seconds of soft key presses, low muted thocks, a short sentence typed at an easy pace, dry, no clatter | 2 s | 73.5 (plays 1.9 s) |
| `sfx-tap.mp3` | One soft tap of a fingertip on a phone's glass screen, muted and rounded, very short, dry, no click, no tick | 0.5 s (the shortest) | 94.5, 96.5; and the tap on Due at 95.5 |
| `sfx-swell.mp3` | Warm soft swell of a felt piano and string pad chord in D major, rising gently over a second then fading over three seconds, calm and hopeful, no bells, no chimes, no percussion | 4 s | 125.0 |

The times are the plan's. Every visible click of the pointer makes the soft click, so the clicks list has eight more
than the timeline you were given (picking Claude, opening the menu bar's dropdown, opening the recorder from its pill,
Step through, Test yourself, checking a hidden box, opening the speed menu, and Recording in Settings' sidebar), and
the phone has one more tap (Due). They all use the same `sfx-click.mp3` and `sfx-tap.mp3`, so there's nothing extra
to make. If a line stretches a scene, the effects after it move with it; `npm run demo-audio` prints the final times.

Each of your files is trimmed to start at its first sound, cut after at most: whoosh 1.5 s, click 0.2, record-start
1.2, pop 0.5, shimmer 2.5, slider 2.5, keys 3.0, tap 0.25, swell 6.0 (with a short fade), and set to the same peak as
the others. How loud each plays is set in `src/demo/sound.ts`; if one of yours sounds too loud or too quiet, change its
number there (0 leaves it out).

**Music (optional).** `elevenlabs3/music.mp3` replaces the made score: it's trimmed to the video with a 2 s fade-out,
and dips under the voice the same way. Without it the video plays the first two videos' score, made here at 120 BPM so
every scene starts on a beat, with no bells.

## Dropping the files in and rendering

```sh
cd promo-video
mkdir -p elevenlabs3          # put voice.mp3, any sfx-*.mp3 and maybe music.mp3 here
npm run demo-audio            # split the take, fit the timeline, place the effects, remake the music
npm run render3:all           # → out/demo.mp4 and out/demo-vertical.mp4
```

`npm run demo-audio` uses whatever is in `elevenlabs3/` and makes the rest: without `voice.mp3` it reads the script in
the Mac's own voice (Reed), which is what the first renders use. It writes the lines to `public/audio/demo/vo/`, the
effects to `public/audio/demo/sfx/`, the music to `public/audio/demo/music.wav`, and the final times to
`src/demo/timeline.json`, and prints a table: each scene's planned and final start, when its line starts and ends, the
room left before the next scene, and any stretch; then every effect's final time. The renders are mastered to −14 LUFS
with the true peak under −1 dBTP (`scripts/master.py`).

It needs ffmpeg, Python with numpy and scipy (the Mac's own `/usr/bin/python3` has them; the script switches to it if
needed), and for splitting a take the .NET 10 SDK (`scripts/wordtimes`, built on first use; it looks for `$DOTNET`,
`dotnet` on the PATH, `~/study-stash-night/dotnet10`, `/usr/local/share/dotnet` and `~/.dotnet`) and the Whisper model
at `~/.study-stash/models/ggml-large-v3.bin`, which it only reads. The first split of a take takes a while (the model
loads and reads it); the words are cached, so running it again is instant.

Other options: `npm run demo-audio -- --scratch` (the Mac's voice even with a take there), `-- --voice <file>` (another
take), `-- --cuts …` (above). To check a take's split without touching the video:
`python3 scripts/demo_audio.py --test-split <take.mp3> src/demo/plan.json` (it writes to a temporary folder).

## Or: laying the sound under the picture in an editor

`npm run render3:silent` renders **`out/demo-silent-vertical.mp4`**, the vertical video with no audio stream at all
(no narration, effects or music), and **`CUES.md`** lists everything to lay under it: every scene's start and what's on
screen, every line of Adam's with its exact words and its window (how long it may run before the next scene), and
every effect with its file, the moment it marks, its ElevenLabs prompt and length, in one table in time order and
again grouped by file, with times as m:ss.s and frames at 30 fps. Both orientations share one timeline, so the sheet
fits the landscape cut too (`npm run render3:silent:landscape` → `out/demo-silent.mp4`). The sheet is made from
`src/demo/timeline.json` by `npm run demo-cues`, and again by every `npm run demo-audio`, so it always matches the
render.

