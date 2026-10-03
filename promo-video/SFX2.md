# Sound effects for the second video

The second video uses five sound effects, all soft and well under the voice. There are no bells (no "ding" on Knew it,
no chime when the notes are filed, none in the score) and no ticks (hovers, sliders and steps play silently). The
video ships with versions made in code (`scripts/make_audio.py`). To use your own from ElevenLabs instead, make any of
them with the prompts below and drop the files in. You can replace one, some or all of them.

| Effect | Save it as | Where you hear it | ElevenLabs prompt |
|---|---|---|---|
| Click | `sfx2/click.mp3` | Every click of the pointer (16 of them) | Very soft trackpad click, a single muted tap, 0.05 seconds, close-miked, dry, no reverb, no high-pitched tick |
| Key | `sfx2/key.mp3` | The Y and N keys in Test yourself (4) | One soft laptop key press, a low muted thock, very short, dry, no reverb, no clatter |
| Pop | `sfx2/pop.mp3` | A pinned box's actions and a pinned part's card, the diagram arriving in the notes, each new kind of diagram (about 9) | Soft rounded interface pop, like a small card settling into place, a gentle low wooden tok, 0.1 seconds, dry, no bell, no chime, no reverb |
| Whoosh | `sfx2/whoosh.mp3` | Each cross-fade between scenes, "2 minutes later", the drone giving way to the hand (11) | Very gentle airy swish for a slide transition, low and smooth, soft air moving, half a second, no hiss, no reverb tail |
| Marker | `sfx2/marker.mp3` | A highlighter under a word (up to 3) | A felt-tip highlighter drawn once across paper, soft and short, 0.3 seconds, dry, close-miked |

On the ElevenLabs website, open Sound Effects, paste a prompt, set the duration to the shortest it offers, and
generate a few. Pick the softest, cleanest one: nothing ringing, nothing after the sound itself.

## Dropping them in

1. Save each file in `promo-video/sfx2/` (make the folder next to `voice-takes/`), named as in the table. `.mp3`,
   `.wav`, `.m4a`, `.aac`, `.flac` and `.ogg` all work.
2. Run `npm run sfx2`. It lists each effect as "from click.mp3" (yours) or "made here" (the made one, for any file
   that isn't there). It trims the silence before the sound, cuts it after at most 0.15 s (click, key), 0.4 s (pop),
   0.8 s (marker) or 1.2 s (whoosh) with a short fade, and levels it to the same peak as the made one. The results go
   to `public/audio/sfx2/`, which is what the video plays.
3. Render again: `npm run render2:all`.

To go back to a made one, delete your file and run `npm run sfx2` again.

How loud each effect plays is in `src/promo2/sound.ts`. A click peaks around −34 dBFS, a pop around −28, a whoosh
around −26, under a voice that peaks around −6. If one of yours sounds too loud or too quiet, change its number
there. Setting one to `null` leaves that effect out everywhere.

The first video keeps its own effects (`public/audio/sfx`), unchanged.
