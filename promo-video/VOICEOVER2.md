# Voiceover for the second video

The second video (what's new: diagrams you can play with, drawings, formulas that move, a lighter app) has its own
narration: one flowing read of about 100 words, a line per scene, each timed to land on its action. As with the first,
the video recuts itself to fit the voice: each scene is at least as long as its line plus a breath, and still starts on
a beat of the music. With the scratch voice (the Mac's own) the cut is 50.4 s.

## The script

The same text lives in `src/promo2/voiceover.json`, which the generator and the video both read. Edit it there.

| Scene | Starts at | Line starts | Line | How to say it |
|---|---|---|---|---|
| Opener | 0.0 s | 0.45 s in | New in Study Stash. | bright, a little proud, like showing a friend something new |
| Notes | 3.0 s | 0.4 s in | Your notes come first. A couple of minutes later, the diagrams drop right in. | easy, unhurried; a small lift on "drop right in" |
| Explore | 9.0 s | 0.3 s in | Point at a box to see what it connects to. Pin it, ask about it, or step through it. | curious, clear, a beat between the two sentences |
| Recall | 15.6 s | 0.3 s in | Then hide the words, and test yourself. | playful, a little challenge in it |
| Kinds | 19.8 s | 0.3 s in | Plus state machines, sequences, timelines, mind maps. | brisk, a list on the beat |
| Drawings | 25.2 s | 0.3 s in | When a lecture describes a drone or a hand, your notes can draw it, labelled. | a spark of delight on "draw it" |
| Plots | 31.2 s | 0.3 s in | Formulas turn into graphs you can drag: the sigmoid sharpens… gradient descent overshoots. | enjoying it; a smile on "overshoots" |
| Quiet | 38.4 s | 0.3 s in | Light, and quiet, on your computer. | calm, softer, settling down |
| Promise | 42.0 s | 0.3 s in | Your recordings never leave your computers. | sincere, a touch slower |
| Ending | 45.0 s | 0.6 s in | Study Stash. Free, for Mac and Windows. | warm and confident, a beat after "Study Stash" |

The "Starts at" times are for the scratch cut; a real take moves them a little, since every scene stretches to fit its
line.

Every claim is true of the app as it is:
- **Notes first, diagrams after.** The notes are filed about a second after they're written; the diagrams follow in
  the background, usually one to three minutes later, and land in the open note (its byline says "Adding diagrams…",
  then "Diagrams added"). So: "a couple of minutes later", never "in seconds".
- **Point, pin, ask, step, test.** Hovering a box lights it with its arrows and neighbours; a click pins it, with
  Explain this, Quiz me, Where was this said? and Zoom in (the first two ask the AI you've set up); Step through and Play
  walk the diagram; Test yourself hides the words and keeps count.
- **The kinds.** State diagrams (an accepting state is a double circle), sequence diagrams, timelines and mind maps are
  all drawn by the app now.
- **Drawings "can" be made.** A detailed, labelled drawing is added when a lecture describes a physical thing; not
  every lecture gets one, and they are drawings, not photographs.
- **Formulas.** The plots are computed by the app (the video recomputes the same functions: the sigmoid with steepness
  k from 0.2 to 5, gradient descent on w₁² + 5w₂² from (−2.6, 1.5), diverging past η = 0.2).
- **Light and quiet.** Measured on an M-series Mac: idle under half a percent of one core, and recording about a quarter
  of what it used to take. Nothing is said about battery life.
- **"Your computers"** (plural) covers both setups, a laptop alone or with a library at home, as in the first video.

## Making it on the ElevenLabs website (no API key)

For the two videos to sound like one voice, use the first video's take settings: **Adam, "Engaging, Friendly and
Bright"**, model **Multilingual v2**, speed **0.95**, stability **85%**, similarity **75%**, style **40%** (see
VOICEOVER.md). The whole script as one take usually sounds most natural.

1. Open **Text to Speech**, pick Adam, and set the settings above.
2. Paste the script below, each line as its own paragraph, with a blank line between. Generate, listen, and
   regenerate until you like it. Download the MP3.
3. Keep it with the first take: `voice-takes/adam-promo2-<date>.mp3`.
4. Run, from `promo-video/`:

   ```sh
   npm run voice2 -- --from-take voice-takes/adam-promo2-<date>.mp3
   npm run render2:all
   ```

The take is cut at the nine pauses that best fit where each line should end, by word count. If a cut lands a word early
or late (pauses all about the same length), give the nine cuts yourself, in seconds, between one line's last word and
the next line's first: `npm run voice2 -- --from-take <file> --cuts 1.6,6.9,…`.

Or download a file per line, named after its scene, into one folder: `opener.mp3`, `notes.mp3`, `explore.mp3`,
`recall.mp3`, `kinds.mp3`, `drawings.mp3`, `plots.mp3`, `quiet.mp3`, `proof.mp3`, `cta.mp3`; then
`npm run voice2 -- --from-files <folder>`. One line redone on its own goes in with `--line kinds=<file>`.

Either way the generator trims each line, sets them all to the same loudness, writes them to `public/audio/vo2/`,
lengthens any scene its line needs (on the beat) in `src/promo2/timeline.json`, notes what it used in
`src/promo2/voice.json`, and remakes the music (`public/audio/music2.wav`). Then render.

```
New in Study Stash.

Your notes come first. A couple of minutes later, the diagrams drop right in.

Point at a box to see what it connects to. Pin it, ask about it, or step through it.

Then hide the words, and test yourself.

Plus state machines, sequences, timelines, mind maps.

When a lecture describes a drone or a hand, your notes can draw it, labelled.

Formulas turn into graphs you can drag: the sigmoid sharpens… gradient descent overshoots.

Light, and quiet, on your computer.

Your recordings never leave your computers.

Study Stash. Free, for Mac and Windows.
```

## With the API, or a scratch voice

Everything in VOICEOVER.md applies, with `voice2` in place of `voice`:

```sh
npm run voice2 -- --scratch                 # the Mac's own voice, to check the cut (what out/ was made with)
export ELEVENLABS_API_KEY=...               # stays in your shell; never printed or saved
npm run voice2 -- --voice <voice id>        # all ten lines, request-stitched, then the recut and the music
```

The whole narration is about 560 characters.
