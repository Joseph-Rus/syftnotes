# Study Stash: Attachments

A student can hand the library their own files for a lecture or a whole class: a photo of handwritten notes, a
PDF of slides, a scanned handout. The library keeps the file, reads the words in it (handwriting and scans too),
and uses those words when it writes that lecture's study notes and when Ask or Claude answer a question about it.

```
App (drag-drop, Attach)          Library web page (upload form)          Phone, later
  POST /api/v2/attachments  ─────────────────────────────────────────▶  (not yet: read-only for now)
                                          │
                                          ▼
                         saved under the class's Attachments/ folder
                                          │
                                          ▼
                    read in the background (DocumentText.ExtractAsync, one at a time)
                                          │
                          ┌───────────────┴────────────────┐
                          ▼                                ▼
              a lecture's notes are written        Ask and Claude's list_attachments /
              with its attachments' words           read_attachment read them too
```

The code: `engine/src/StudyStash.Core/Attachments.cs` (naming, MIME sniffing, cutting words to fit a prompt),
`Store.Attachments.cs` (where a file lives and its row in the database), `engine/src/StudyStash.Library/LibraryWeb.Attachments.cs`
(the API and the web pages' list), `engine/src/StudyStash.App/ViewModels/AttachmentsModel.cs` and
`Views/Mac|WinAttachments.axaml(.cs)` (the app), `ClaudeTools.cs` (`list_attachments`, `read_attachment`), and
the notes pipeline (`Pipeline.cs`, the resummarize path) that hands a lecture's attachments to whatever writes
its notes.

## What the student does

- **In the app**: a lecture's page and a class's page each have an Attachments section with an **Attach** button
  and a place to drop files. Dropping a file, or choosing one from the picker, uploads it to the library (this
  computer, or another one on the network) and it shows up in the list right away, saying "Reading your
  handwriting…" until its words are read. Choosing a row opens the file with whatever app the computer already
  opens it with.
- **On the library's web pages**: the same list appears on a lecture's page and a class's page, with an upload
  form. Anyone signed in with the library password can add, open or remove a file there, the same as in the app.
- **On the phone**: not yet. The phone app (the PWA) doesn't have an upload screen in this round; that's tracked
  separately. Everything here is ready for it to use once it does — the API is the same one the app calls.
- **Rewriting notes**: when a lecture's notes were written before an attachment came in, or before its words
  were read, the library offers "Rewrite notes with your attachments" on that lecture's page (in the app and on
  the web). Choosing it writes the notes again with the engine, now including those words, and marks them used.

## Where files live

Every attachment is saved under its class's own folder, in a subfolder named `Attachments/`, next to that
class's lectures. A file for a lecture is still filed under the lecture's class, not the lecture itself, so
moving a lecture to another class (or renaming the class) takes its attachments with it, and deleting a class
moves them to the trash the same way its lectures do. Attachments with no class or lecture wait in "Unsorted".

A file keeps the name it came with, made safe for the filesystem (no path, no characters a disk refuses, not too
long, not a reserved Windows name), and gets a number added if that name is already taken in the folder. What a
file *really* is — a PDF, a picture, a Word or PowerPoint file, plain text — is read from its first bytes, not
whatever the browser claimed; that's what decides whether it opens inline in a browser (PDFs and pictures only)
or only ever downloads.

## How attachments feed notes, Ask and Claude

Once a file is saved, the library reads its words in the background with `DocumentText.ExtractAsync`, one
attachment at a time (reading handwriting is slow, so a stack of scans doesn't tie up the whole computer). That's
the same reading file search uses: typed text as it is, and the computer's own text recognition (handwriting and
scans too) where there isn't any — see `docs/document-text.md`.

- **Notes**: when the engine writes a lecture's study notes, it includes the words from that lecture's
  attachments (and the class's, for context), each labelled as "The student's own notes", "Slides" or a
  "Handout" so the model knows what it's reading, and capped so no one file crowds out the transcript.
- **Ask**: asking a question about a lecture or a class also searches attachments' words, and names the file it
  answered from.
- **Claude**: the `list_attachments` and `read_attachment` MCP tools let Claude see what's attached (with its
  size, kind, and whether its words could be read yet) and read an attachment's text a page at a time.

## Limits

- One upload request may carry up to **200 MB** total, across every file in it. Kestrel's usual 30 MB body limit
  is raised only for this one route; every other route keeps the usual limit.
- Going over 200 MB gets a `413` with a plain-English message ("That's more than 200 MB at once. Attach fewer
  files, or smaller ones.") and nothing from that request is kept — files already written to a staging folder
  are cleaned up, not left behind or half-saved.
  An empty file (0 bytes) is refused too, by name, before anything is kept.
- Every file is written to disk as it arrives, never held in memory, so a big file doesn't spike memory.

## The API (`/api/v2/attachments`)

- `POST /api/v2/attachments`, multipart/form-data: one or more parts named `file`, plus optional `class` and
  `lecture` (a lecture id) fields. Neither given: the file waits in Unsorted. → `{attachments: [Attachment]}`.
- `GET /api/v2/attachments?class=&lecture=` → `{attachments: [Attachment]}`, filtered by whichever is given
  (a lecture also gets `rewrite: bool`, whether that lecture's notes were written without some of its
  attachments).
- `GET /api/v2/attachments/{id}/raw` → the file itself: inline for PDFs and pictures, downloaded otherwise, never
  run as a page on the library's own address.
- `GET /api/v2/attachments/{id}/text` → `{id, name, text, reading}`.
- `DELETE /api/v2/attachments/{id}` → `{deleted: id}`, removing its file too.

An `Attachment` is `{id, name, class, lecture, size, type, added, hasText}`: `type` is the sniffed MIME type,
`added` is an ISO 8601 timestamp, and `hasText` says whether any words were read from it (there's also `reading`,
whether it's still being read). Every route needs the library's password (a `Bearer` key), the device cookie a
paired phone gets, or a signed-in member — the same as every other `/api/v2/*` route.

## Privacy

Attachments never leave the library computer except to the student themself (the app, the web pages, or Claude
through the tools above, if the student has turned that on) — nothing is uploaded anywhere else, and OCR runs on
the library's own computer, not a cloud service. Removing an attachment deletes its file from disk immediately;
it isn't kept in a trash the way a deleted class or lecture is.

## What's verified

Tests in `engine/tests/StudyStash.Core.Tests/AttachmentsApiTests.cs` cover: saving into the right class folder
and listing it back, several files in one request with name collisions, path safety (an id that isn't a real
row, a row pointing outside the library, a name with a path in it), fetching a file as what its bytes say (not
what was claimed) with pictures and PDFs inline and everything else downloaded, removing a file, the 200 MB
limit (over it is refused and nothing kept, just under it is kept, and — with a real Kestrel server, not the
in-memory test host — only this route takes more than the usual 30 MB), the library password being required
everywhere, "Reading your handwriting…" while a document is read and "No words to read in it" when there are
none, a reader that throws leaving the attachment readable rather than stuck, the lecture/class pages showing
attachments and the rewrite offer only when there's one to make, and attachments a restart interrupted being
read again when the library starts. The notes pipeline, Ask, and the Claude tools each have their own tests
covering how they use attachments' words.
