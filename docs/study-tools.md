# The study tools

Recipes, the exam flags, Review, exam prep and Listen. Everything they keep is on the library, so the
laptop, the phone and Claude see the same cards, prep and listens. The app reaches them through the
library's API with the laptop's key, like everything else.

## Recipes

`engine/src/StudyStash.Core/Ai/Recipes.cs`. A recipe is a question kept to ask again: `/study-guide`
is asked as the recipe's whole prompt. The library expands it in `AiJobs.AskAsync`, before the AI sees
it, so the phone, the web app and the quick panel get recipes without knowing about them, and the chat
keeps what was typed. Anything typed after the slash word goes with it as the student's own words.

Syft Notes has eight of its own (`Recipes.BuiltIns`). A student's own are kept in `recipes.json` in the
library's home, and one with the same name as a built-in is used in its place.

| | |
|---|---|
| `GET /api/v2/ai/recipes` | every recipe, the student's own first |
| `POST /api/v2/ai/recipes` `{name, prompt}` | keeps one (its id is its name, lower case, with dashes) |
| `DELETE /api/v2/ai/recipes/{id}` | deletes one of the student's own; a built-in can't be |

## The exam flags

`engine/src/StudyStash.Core/ExamHints.cs`. The sentences in a transcript where the lecturer says
what's on the exam, found by what was said, with no AI: "on the midterm", "make sure you know", "write
this down", "I'll ask you this", "fair game", and "will be on it" just after an exam was named. The
same said the other way ("won't be on the exam", "you don't need to memorise") is marked `not`. A
lecture's page lists them over its notes; Review's cards, exam prep and Listen are all told them. Only a lecture in
one of the student's classes has them (`Config.IsClass`): a meeting in a folder of their own (Clubs, Work) or an
unsorted recording has none.

`GET /api/v2/lectures/{id}/exam-flags` gives them as `[{at, said, not, moment}]`.

## Review

`engine/src/StudyStash.Core/Study/Review.cs`. Flashcards in `review.json`, each with its lecture,
the moment it was taught, and its place in its schedule. `Review.Next` is SM-2 as Anki uses it:

- A card not learned yet (new, or forgotten): Again comes back in ten minutes (and round again before
  the session ends), Hard or Good tomorrow, Easy in four days.
- A learned card: Again forgets it (back to learning, ease down 0.2); Hard is the gap times 1.2 (ease
  down 0.15); Good is the gap times its ease; Easy is that times 1.3 (ease up 0.15).
- Gaps are whole days, at most a year, and ease never drops under 1.3. Twenty new cards a day at most.

Cards come from a lecture's notes (`StudyMaterial.MakeCardsAsync`, the library's Ask AI, twelve at a
time, one for everything flagged for the exam and none for what won't be), from a practice test's
missed questions, or typed in. A lecture's questions are never added twice. A lecture deleted for good
takes its cards with it.

| | |
|---|---|
| `GET /api/v2/review?class=&limit=` | `{counts: {due, new, total, today}, due: [cards]}` |
| `GET /api/v2/review/lectures/{id}` | a lecture's cards |
| `POST /api/v2/review/lectures/{id}` `{more}` | the AI writes its cards (more only if asked, once it has some) |
| `POST /api/v2/review/cards` `{lecture, front, back, at, from}` | adds one |
| `POST /api/v2/review/cards/{id}/grade` `{grade}` | `again`, `hard`, `good` or `easy` |
| `POST /api/v2/review/cards/{id}` `{front, back}` | changes its words |
| `DELETE /api/v2/review/cards/{id}` | deletes it |

## Exam prep

`engine/src/StudyStash.Core/Study/ExamPrep.cs`. The exams coming up are Canvas's assignments and the
calendar's events in the next 60 days whose names say exam, midterm, final, test, quiz or prelim (not a
final project, quiz solutions or an exam review session). A prep covers the class's lectures since the
exam before it (by date, from the same two sources), up to the exam, at most sixteen. The AI writes a
study guide in Markdown (what the lecturer said to know first, then a section a topic, then "Check
yourself") and a practice test of twelve questions, multiple choice and short answer, each with why and
the lecture and moment it comes from. Questions that don't make sense (a right answer that isn't one of
the choices) are left out. Kept in `exam-prep/<id>.json`, with each go at the test.

| | |
|---|---|
| `GET /api/v2/exam-prep` | `{upcoming: [exams], preps: [summaries]}` |
| `POST /api/v2/exam-prep` `{class, title, when}` | writes a prep |
| `GET /api/v2/exam-prep/{id}` | one, whole |
| `POST /api/v2/exam-prep/{id}/attempt` `{marks, review_missed}` | keeps a go; the missed questions go into Review |
| `DELETE /api/v2/exam-prep/{id}` | deletes it |

## Listen

`engine/src/StudyStash.Core/Study/Listen.cs`. The AI writes a script for two voices from the lecture's
notes and exam flags, about five minutes read aloud: a tutor (A) who explains and a classmate (B) who
asks. The library's computer reads it with its own voices, so only the script's request leaves it:

- **Mac**: `say`, Samantha (or another US or British woman's voice) for A and Daniel (or another man's)
  for B, else its own voice at two speeds.
- **Windows**: System.Speech through Windows PowerShell, its first two English voices.
- **Linux**: `espeak-ng` (or `espeak`), at two pitches. Without it, Listen says to install it.

Each turn is spoken to a WAV, and `Wav.Join` mixes them to one 22 kHz mono WAV with 0.35 s between
turns (any rate or channels in, resampled in straight lines). It's made in the background; the app
looks again every few seconds, then fetches the WAV once and plays it with the same player as a
transcript's moments. Kept in `listen/<lecture>.wav` and `.json` (the script and its length).

| | |
|---|---|
| `GET /api/v2/listen/{id}` | `{state: none, making, ready or failed, script, seconds, error, can_speak}` |
| `POST /api/v2/listen/{id}` | starts making it |
| `GET /api/v2/listen/{id}/audio` | the WAV, once it's ready |
| `DELETE /api/v2/listen/{id}` | deletes it |

## Tests

`RecipesTests`, `ExamHintsTests`, `ReviewTests`, `ExamPrepTests` and `ListenTests` in the Core tests
run the library for real with a scripted AI (`StudyLibrary`); `ListenTests` also has this computer's
own voices speak a line. The app's are `AskRecipesTests`, `ExamFlagsTests`, `ReviewPageTests`,
`ExamPrepPageTests` and `ListenPageTests`, and `StudyShots` draws each of them, light and dark, in both
looks, into `STUDYSTASH_SHOTS`.
