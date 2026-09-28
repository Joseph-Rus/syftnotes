# Voiceover

The narration is one flowing read of about 95 words, a line per scene, each timed to land on its action. The video
recuts itself to fit the voice: each scene is at least as long as its line plus a breath, and still starts on a beat of
the music. With the scratch voice the cut is 44.4 s.

## The script

The same text lives in `src/voiceover.json`, which the generator and the video both read. Edit it there.

| Scene | Starts | Line | How to say it |
|---|---|---|---|
| Hook | 0.25 s in | Every week, the lectures pile up… and the notes? You never look at them again. | wry, a little tired, like a friend who's been there |
| Reveal | 0.45 s in | Meet Study Stash — your lectures, turned into notes you'll actually use. | warm, a small smile on the name |
| Record | 0.9 s in | Hit record in the menu bar. It transcribes on your computer, then writes your notes, and files them by class. | clear and easy, unhurried |
| Diagrams | 0.4 s in | It even draws: labeled anatomy for nursing… formulas and graphs for calculus. | a spark of delight on "even" |
| Ask | 0.3 s in | Ask your notes anything, with Claude or OpenAI, and see exactly where each answer came from. | curious, confident |
| Canvas | 0.3 s in | Canvas is right there too: what's due, and how it's graded. | light, matter-of-fact |
| Promise | 0.3 s in | And your recordings never leave your computers. | sincere, a touch slower |
| Ending | 0.6 s in | Study Stash. Free, for Mac and Windows. | warm and confident, a beat after "Study Stash" |

Every claim is true of the app:
- Transcription happens on the computer.
- The notes and answers come from the AI you choose: Claude Code, Codex (OpenAI), Ollama or Gemini.
- Answers name the moments in the lecture they came from.
- "Your computers" (plural) covers both setups, a laptop alone or with a library at home.

## Making it with ElevenLabs

```sh
cd promo-video
export ELEVENLABS_API_KEY=...                  # elevenlabs.io → Profile → API keys; stays in your shell
npm run voice -- --list-voices                 # your voices and their ids
npm run voice -- --voice <voice id>            # all eight lines → recut → music
npm run render:all                             # both videos
```

`npm run voice` makes the eight lines in order and trims each one. It sets every line to the same loudness, lengthens
any scene its line needs (on the beat) in `src/timeline.json`, and remakes the music to the new cut. The music then
dips under each line in the video and comes back up between them.

- **Takes:** `--takes 3` makes three of each line: the first is used and the others are kept as
  `public/audio/vo/<line>.take2.wav` and so on. Listen, then run again with `--pick record=2,ask=3` to choose.
- **Scratch voice:** `npm run voice -- --scratch` uses the Mac's own voice, to check the cut before spending
  credits. The renders in `out/` were made this way until you run it with ElevenLabs.
- **The key:** it's read from `ELEVENLABS_API_KEY` and sent only to ElevenLabs. It is never printed or saved. Don't
  put it in a file in the repository.

## What the research says, and what the generator does

From ElevenLabs' own documentation:

- **Model:** ElevenLabs now recommends **Eleven v4** (`eleven_v4`) as "a net upgrade over Eleven v3, delivering
  better results in almost every case", and its request-stitching guide uses v4. That's the default here.
  **Multilingual v2** is the fallback, used automatically if your plan doesn't offer v4. It's the long-standing
  steady choice for narration.
- **One read, not eight clips:** request stitching conditions each line on the ones before it
  (`previous_request_ids`, up to three, oldest first, from the `request-id` header) and on the text after it
  (`next_text`), "to improve the prosody and coherence". It isn't available on `eleven_v3`, so the generator skips
  it there. The same voice, model, settings and a fixed `seed` are used on every line.
- **Voice settings:**
  - Stability 0.5, similarity 0.75, style 0.15, speaker boost on, speed 1.0. That's ElevenLabs' common starting point
    (stability about 50, similarity about 75), with a little style for warmth.
  - Raise stability if takes of the same line sound too different; lower it for more expression.
  - Keep similarity at or below about 0.8: higher can over-enunciate or copy artifacts.
  - Speed can go from 0.7 to 1.2.
- **Pacing:** use punctuation. Ellipses slow down and add weight, dashes give a short pause, capitals add emphasis.
  SSML `<break>` tags only work on the v2 and Flash models (up to 3 s each, and too many cause instability); v3 and v4
  ignore them. That's why the script uses "…" and "—".
- **Pronunciation:**
  - On v4, write IPA between slashes with stress marks, e.g. `/klɔːd/`.
  - On v2, use SSML phoneme tags (CMU Arpabet is the most predictable) or a pronunciation dictionary.
  - If "Claude" ever comes out wrong, write "Clawd".
- **Audio:** WAV at 48 kHz straight from the API, to match the video. If your plan doesn't include it, the generator
  falls back to MP3 at 192, then 128 kbps.
- **Choosing a voice:** the voice matters most, above the model. Pick one that already sounds like this read: warm,
  conversational, young-adult, not an announcer. Filter the Voice Library by "Narration" or "Conversational", or
  describe it in Voice Design ("a warm, friendly young adult narrator, relaxed and clear, like a helpful classmate").
  Try the hook line with two or three voices before making all eight.

Sources:
- [ElevenLabs best practices](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices)
- [Models](https://elevenlabs.io/docs/overview/models)
- [Request stitching](https://elevenlabs.io/docs/eleven-api/guides/how-to/text-to-speech/request-stitching)
- [Create speech API](https://elevenlabs.io/docs/api-reference/text-to-speech/convert)
- [How to add pauses](https://elevenlabs.io/docs/help-center/product/core-capabilities/text-to-speech/how-can-i-add-pauses)
