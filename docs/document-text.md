# Study Stash: document text

Files handed to the library — a PDF, a photo or scan (png, jpg, heic), a Word document, a PowerPoint — have their
words read once, and those words feed the file search, attachments, notes, Ask and Claude. Typed text is read as
it is; a page or picture with none (a scan, iPad handwriting from GoodNotes or Notability, which is ink rather
than letters) is read by the computer's own text recognition. Nothing is installed and nothing leaves the
computer.

```
FileIndex.TextOf / Attachments  ──▶  DocumentText.ExtractAsync(path, ct)
                                          │
                        ┌─────────────────┼─────────────────────┐
                        ▼                 ▼                     ▼
                  docx / pptx      PDF (PdfPig or PDFKit,     picture (png, jpg,
                  (ZIP + XML)      page by page)              heic, …): text recognition
                                          │
                                 pages with next to no typed
                                 text are drawn and read by
                                 text recognition too
                        │
                        ▼
                  cached by path, size and time changed (home/cache/text)
```

The code: `engine/src/StudyStash.Core/Ai/DocumentText.cs` (the public method, docx/pptx, the cache, tidying),
`DocumentText.Pdf.cs` (PdfPig/PDFKit page reading, the one-at-a-time queue, the PDF-pages-as-pictures path),
`MacVision.cs` (a Mac's own PDFKit + Vision, through hand-written Objective-C calls), `WindowsOcr.cs` (Windows's
own `Windows.Media.Ocr` and `Windows.Data.Pdf`, run through a short PowerShell script because those are Windows
Runtime APIs a plain .NET program can't reach directly).

## What's read, and how

- **Word (`.docx`) and PowerPoint (`.pptx`)**: both are ZIP files of XML; the words are read straight out of that
  XML, no Word or PowerPoint installed. A Word document's body, footnotes and endnotes; a PowerPoint's slides in
  the order they'd play, each followed by its own speaker notes. A file with no words in it (a title slide, an
  empty template) reads as null, not an error.
- **PDF**: read page by page, blank pages left out. On a Mac, by PDFKit — the same reading Preview and Quick Look
  use, through direct calls, not a third-party library. Everywhere else, by PdfPig. A page with fewer than 40
  letters typed on it (an iPad page whose only typed text is a title or page number) is treated as untyped:
  drawn as a picture and read by text recognition instead, up to `MaxOcrPages` (60) pages and `TimeLimit` (2
  minutes) per file; pages that didn't get read that way keep whatever was typed on them, so a time-limited PDF
  still returns its typed pages.
- **Pictures (`.png`, `.jpg`/`.jpeg`, `.heic`/`.heif`, `.tif`/`.tiff`, `.bmp`, `.gif`, `.webp`)**: read whole by
  text recognition. A photo of a whiteboard, a scanned handout, a screenshot of slides.
- **Text recognition itself**:
  - **macOS**: Vision's accurate recognizer, with language correction on and the language detected on its own —
    the same engine behind Preview's "Copy text from image" and Photos' text-selection. Reads handwriting, not
    just print.
  - **Windows**: `Windows.Media.Ocr`, in every Windows 10 and 11, called from a short Windows PowerShell 5.1
    script (those are Windows Runtime APIs, unreachable from a plain .NET build without a Windows-only target).
    A PDF's pages are drawn by `Windows.Data.Pdf` first. Reads print well; handwriting poorly.
  - **Neither**: (a Linux library, or a Mac/Windows without the frameworks) reads nothing from a picture or a
    scanned PDF page; typed text still comes through.
- **Plain text** (`.txt`, `.md`, `.markdown`, `.csv`, `.tsv`) is read as it is. `.doc` and `.rtf` are converted by
  `textutil` on a Mac only.
- Anything else, or a file that isn't there, returns null — never an error.

## Limits

- **Pages**: a PDF's pages past `MaxPages` (300) aren't read at all — a 300-page textbook's first chapters are
  what's usually asked about, not its last. Of the pages that are read, at most `MaxOcrPages` (60) are drawn and
  read as pictures.
- **Time**: text recognition on one file stops after `TimeLimit` (2 minutes); the pages it read by then are kept,
  the rest fall back to whatever was typed on them (often nothing, for a scan). The clock starts once it's that
  file's turn to be recognized, not while it's waiting — a stack of scans queued behind one big PDF doesn't have
  its own budget eaten by someone else's wait.
- **One at a time**: only one file is recognized at once, on either OS — text recognition keeps the processor (or
  graphics hardware) busy, so recognizing several files at once would only make all of them slower. Others wait
  their turn; waiting costs them none of their own time limit.
- **Text length**: at most 400,000 characters are kept from one file (`DocumentText.MaxText`); a long textbook is
  cut, not refused.

## The cache

What's read from a file is kept under `home/cache/text`, keyed by the file's full path (hashed), and checked
against a stamp of how it was read plus the file's size and last-write time. Reading the same file again — the
file search's next pass, an attachment opened twice — costs nothing until the file actually changes. A file with
no words at all is remembered as having none, so it isn't retried every time. Two readers of the same file at once
can't leave a half-written cache entry: a reading is written to a temp file and moved into place whole, or not at
all; a cache that can't be written just means reading again next time, never an error. Raising `CacheVersion`
(when reading gets better) makes every file be read again, ignoring what an older version cached.

## Privacy

Every reading happens on the computer that has the file: PDFKit, PdfPig and Vision on a Mac; PdfPig and
`Windows.Media.Ocr` on Windows. Nothing is uploaded anywhere — no cloud OCR service, no third-party API — the
same as file search and Canvas. What's cached is the plain text itself, kept alongside the rest of the library's
data in `home/`.

## What's verified

`DocumentTextTests.cs` covers: docx/pptx extraction (body, footnotes, slide order with speaker notes, a file with
no words), PdfPig reading page by page with blank pages left out and the page limit respected, PDFKit and PdfPig
reading the same pages the same way, the cache (a file read once isn't read again until its size or time changed,
a file with no words remembered as such, a cache that can't be written just means reading again), the one-at-a-
time queue and that waiting a turn doesn't spend a file's own time limit, stopping mid-read keeping nothing
half-written, and files that can't be read at all returning null rather than throwing. `DocumentOcrTests.cs` and
`WindowsOcrTests.cs` cover real text recognition — handwriting in a photo (png, jpg, heic) read top to bottom, an
iPad PDF's handwriting read alongside its typed title, a scanned PDF stopping at its page and time limits while
keeping what it read, and a blank page reading as nothing — the Mac tests running Vision for real and skipping
elsewhere, the Windows ones running the PowerShell recognizer for real on Windows and skipping elsewhere.
`AttachmentsApiTests.cs` has one end-to-end test — a scanned photo attached to a lecture, read by the real
`DocumentText.ExtractAsync` (Vision included, on a Mac) rather than a stand-in reader — proving attachments get
real OCR, not just the API's plumbing around it.
