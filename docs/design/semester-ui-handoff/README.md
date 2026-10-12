# Semester UI redesign: handoff for Claude Code (v2)

This folder is the complete spec for restyling and restructuring the app's interface. It covers **how things look, where they live, and a few new interactions**: taking notes while recording, starting a recording from Coming up, and a calendar for deadlines. Keep the existing recording, transcription, Canvas sync and AI pipelines; extend them only where section 6 says so.

```
semester-ui-handoff/
├── README.md              ← this spec (read all of it first)
├── PROMPT.md              ← what to paste into Claude Code
├── tokens/
│   ├── tokens.css         ← every color, type style, space and radius, light + dark
│   └── tokens.json        ← same values for non-CSS code
├── reference/             ← working HTML for every screen; open in a browser, add ?theme=dark
│   ├── home.html          ← Home: Up next + one notes list
│   ├── home-filter.html   ← Home with the notes filter open
│   ├── home-move.html     ← Home with the folder picker open on a note
│   ├── due.html           ← Due: month calendar
│   ├── folder-class.html  ← a class folder (Machine Learning)
│   ├── folder-club.html   ← a non-class folder (ACM), showing "meetings"
│   ├── note.html          ← a lecture after recording: your notes merged with Claude's write-up
│   ├── recording.html     ← the same page while recording and taking notes
│   ├── components.html    ← every component in every state
│   └── semester.css       ← the reference implementation of each component
├── screenshots/           ← every reference page, light + dark (2x)
├── icons/                 ← Lucide icons used, 1.5 stroke, currentColor
└── fonts/                 ← Atkinson Hyperlegible Next (variable woff2) + OFL license
```

**The reference pages and screenshots are the source of truth for the look.** When this README and a reference page disagree, the reference wins. Use the light and dark themes, following the system setting, with an override in Settings.

---

## 1. How to do the work

1. **Read the codebase first.** Find the UI framework, where styles and theme values live, and the screens: sidebar, Home, Due, class page (including its hidden "class home" second layout), lecture detail (currently a three-column layout with a lecture list), the record button, the floating recorder, and the Ask bar.
2. **Install the foundations.** Copy `fonts/` into the app's assets and load `tokens/tokens.css` once at the root (or port `tokens.json` into the existing theme). Keep the token names.
3. **Restructure navigation** (section 3) before restyling, so you don't style screens that are about to be deleted.
4. **Rebuild screens one at a time** against `reference/`, in this order: sidebar → Home → folder page → note page → recording → Due calendar → mini recorder.
5. **Delete the old look as you go.** No leftover colors, shadows, font stacks or unused components.
6. **Check each step visually.** Run the app in light and dark, screenshot it, and compare against `screenshots/`. Fix the differences before moving on.
7. **Finish with the acceptance checklist** in section 10.

If the app isn't web-based, use `tokens.json` for values and treat the HTML/CSS as a precise visual spec.

---

## 2. What to remove

- **All metric tiles,** everywhere: Home, Due, and class pages (e.g. "14 Lectures / 3 To hand in / Mon 12 Oct Last lecture").
- **The class card grid** at the bottom of pages ("all the classes at the bottom").
- **The class page's second layout** ("Class home" with Record/Attach, metrics, Recent lectures, To hand in, Coming up, On Canvas) **and the Lectures / Assignments / Modules / Announcements tabs.** These merge into one folder page (section 5.3).
- **The lecture list column** that appears beside the lecture detail. There is **one sidebar only**. A note page fills the main pane, and Back returns to where you came from.
- **"Unsorted" in the sidebar.** Unsorted notes live in the Home notes list (section 5.1).
- **The green filled "selected lecture" card,** the colored dots, the pill badges ("In 6 days", "Missing"), the full-red Record button, the sparkle icon and the "Claude" model pill in the Ask bar.
- **Raw LaTeX** in summaries, and registrar suffixes in class names.

---

## 3. Information architecture

```
Sidebar (the only one)
├── Home              ← Up next + every note, filterable by folder or Unsorted
├── Due               ← month calendar of deadlines (week view as an option)
├── Classes           ← folders of kind "class": recordings are called lectures
├── Clubs             ← folders of kind "club":  recordings are called meetings
├── Work              ← folders of kind "work":  recordings are called meetings
└── Coming up         ← next two calendar events; click one to start recording it
    Library and Canvas sync status
```

- **Folder kinds.** Every folder has a `kind`: `class`, `club` or `work` (these are the sidebar groups; users can add folders to any group). A class folder can be linked to a Canvas course.
- **What a recording is called** depends on its folder:
  - in a `class` folder → **lecture** ("Lecture, Monday 12 October at 9:30 AM"; section title "Lectures")
  - in a `club` or `work` folder → **meeting** (section title "Meetings")
  - not in any folder (unsorted) → **recording** ("Recording, Sat 10 Oct at 4:12 PM")
  - generic surfaces that mix kinds (Home list, search, the Ask bar on Home) → **notes** ("Notes", "Ask anything about your notes")

  Put this in one helper (e.g. `nounFor(folder)`) and use it everywhere: labels, empty states, aria-labels and toasts.
- **Routes:** `/home`, `/due`, `/folder/:id`, `/note/:id`. A note page always shows its folder in the breadcrumb; moving a note to another folder updates the noun immediately.

---

## 4. Foundations

All values are in `tokens/tokens.css`.

**Surfaces.** Window background `--color-canvas`. The sidebar sits **directly on the canvas** with no panel. The main pane is one `--color-surface` sheet with `--radius-pane` and a 1px `--color-line` outline. The calendar grid is `--color-surface-sunken`; today's cell or column is `--color-surface-today` with `--shadow-today`. Only the Ask bar, menus and the mini recorder float (`--shadow-float`).

**Text.** `--color-ink` for titles and anything you act on; `--color-ink-muted` for summaries, dates and Claude-written note text; `--color-ink-subtle` for counts, times and placeholders.

**Red means recording or late, nothing else.** `--color-record` is the record dot, the live meter and the recording bar outline. `--color-late` is for "Missing" and "Was due…" text.

**No accent color.** Links are muted text with a 2px `--color-line` underline. Focus is a 2px solid `--color-focus` outline, offset 2px.

**Type.** One family, Atkinson Hyperlegible Next; weight carries the hierarchy. Use the `--text-*` shorthands. Use tabular figures everywhere. Sentence case everywhere, and never uppercase labels.

**Radius is nested:** controls 8 → today cell 10 → panels and Ask bar 12 → main pane 14 → Record and Attach pills.

**Motion.** Hover changes color only (120ms). Looping animation is limited to the recording dot pulse, the level meter and the note caret. Respect `prefers-reduced-motion`.

---

## 5. Screens

### 5.0 Sidebar (`.sidebar`), see any reference page
- **Toolbar row:** hide sidebar, back, forward.
- **Nav:** Home, Due (count of open assignments). The selected item is a white chip with `aria-current="page"`.
- **Groups:** Classes (with a + Add folder button), Clubs, Work. Each folder shows a swatch in its highlighter color, its name and a count, and opens its folder page.
- **Coming up** (`.coming`), pinned above the footer: the next two calendar events. Each item shows a swatch, the event title, and when it starts ("Tomorrow 8:45 AM"). **Clicking an item starts recording that event** (section 6.1). There is **no Record button** on these items: the whole item is the button, with a hover fill and a "Click to record" tooltip. An event starting within 15 minutes reads "In 11 minutes" in bold ink (`.is-soon`).
- **Footer:** "Library on this Mac" and "Canvas synced at 12:58". If sync fails, the second line reads "Canvas sync failed. Retry" in `--color-late`.

### 5.1 Home (`home.html`, `home-filter.html`)
- **Header:** today's date as the title; a one-sentence lede with the most urgent things ("Capstone team sync starts in 11 minutes. Reading quiz 6 is due tonight."); the Record button on the right (records into Unsorted unless an event is happening now, in which case it records into that event's folder).
- **Up next** (`.event` rows): the rest of today's and tomorrow's calendar events, up to 4. Each row shows the time and day, the event title, and its folder as a highlight. If the event title is just the class name, show the class name once, as the highlight. **The whole row is a button: clicking it starts recording** (no separate Record button). Hover fills the row; an event starting within 15 minutes shows "In 11 minutes" under the time, in ink. With no events, show "Nothing on your calendar for the rest of today or tomorrow."
- **Notes:** every note from every folder, newest first (this replaces "Recent lectures" and the Unsorted page). Rows use `.note`: date | title, folder label, two-line summary | length.
  - **Change a note's folder right from the list** (like Granola). The folder label under each title is a button (`.folder-pick`): it looks like the plain highlight until you hover the row, when a chevron and a hover fill appear. Clicking it opens the **folder picker** (`.menu--left`, see `home-move.html` and the Folder picker card in `components.html`):
    - a search field at the top ("Move to folder"), filtering as you type
    - **Unsorted**, then the folders grouped under Classes, Clubs and Work, each with its swatch and note count, and a check on the current folder
    - a divider, then **New folder** (creates the folder and moves the note into it)
    - Picking a folder moves the note immediately, updates the label (and the noun, section 3), and shows a toast: "Moved to Machine Learning. Undo". Keyboard: arrows move, Enter picks, Esc closes. The menu flips upward when there's no room below.
  - **Unsorted notes** show an inbox icon and "Unsorted" as their folder label (`.unsorted` inside `.folder-pick`); the same picker files them.
  - **Filter** (`.picker` + `.menu`, top right of the section): "All notes" (default, checked), "Unsorted" with its count, then the folders grouped under Classes, Clubs and Work, each with a chevron. **Picking a folder navigates to that folder's page.** Picking Unsorted filters the list in place, and the button label changes to "Unsorted".
  - Load more on scroll, 20 at a time.
- **Ask bar:** "Ask anything about your notes", scope "All notes".

### 5.2 Due (`due.html`): a calendar, not a list
- **Header:** title "Due"; the lede names what's most urgent ("Reading quiz 6 is due tonight, and Asynchronous Lecture 5 is still missing."). **No metrics.**
- **Calendar bar:** previous/next month, the month name, a "Today" button; on the right a class filter ("All classes ⌄") and a Month | Week toggle (`.seg`).
- **Month grid** (`.cal`): Mon–Sun columns, 5 or 6 rows. Days outside the month show their number in subtle grey; the 1st of a month reads "1 Oct". Today's cell is lifted (`.is-today`) and labeled "Today".
  - Each assignment is a highlight in its class color (`.cal-item`), one line, truncated with the full title in the tooltip. Show the time only when it isn't 11:59 PM; today's items say "Tonight 11:59 PM".
  - **Missing** (past due, not submitted): red "Missing" under the title. **Handed in:** struck through in the class swatch color, ink-subtle text.
  - More than 2 items in a day → show 2, then a "N more" link that opens a small popover listing the rest.
  - Clicking an item opens the assignment detail (the existing Canvas assignment sheet).
- **Week view:** the 7-day planner from v1 (deadlines highlighted under each day, class meetings above). Its styles are already in `reference/semester.css` (`.week`, `.day`, `.meeting`, `.deadline`). Build it if there is time; otherwise leave the toggle out.
- No Ask bar on this page.

### 5.3 Folder page (`folder-class.html`, `folder-club.html`): one page per folder
This replaces both the class tabs and the "Class home" layout.
- **Header:** the folder name as the page title, with a highlighter underline in the folder's color (`.folder-title`). The lede for a class reads "CSCI 4220 on Canvas. Meets Monday, Wednesday and Friday at 9:30 AM."; for other folders, "Club meetings, Thursdays at 6:00 PM." On the right: **Attach** (upload audio or a file into this folder) and **Record** (records into this folder).
- **Main column:** "Lectures" (class) or "Meetings" (club/work), grouped under This week, Last week, then month names. Same `.note` rows as Home, without the folder highlight.
- **Right rail** (272px, class folders only where data exists):
  - **Due:** the next 3 assignments for this class (title, then "Sun 18 Oct, in 6 days"; anything due within 7 days in bold ink), and a "See these on the calendar" link that opens Due filtered to this class.
  - **On Canvas:** links to Assignments, Modules and Announcements. These open Canvas (or the existing in-app Canvas viewer); they are no longer tabs.
  - **Next class / Next meeting:** "Wednesday at 9:30 AM".
- **Empty folder:** "No lectures yet. Record one here, or attach a recording." (Use "meetings" for club and work folders.)
- **Ask bar:** "Ask about Machine Learning", scope set to this folder.

### 5.4 Note page (`note.html`): a lecture, meeting or recording
- **No second list column.** The page fills the main pane. The toolbar adds a group of page actions: Move to folder, Share, Delete.
- **Breadcrumb:** the folder swatch and name (a link to the folder), a small chevron button that opens the same folder picker, then "Lecture, Monday 12 October at 9:30 AM, 52 min" (noun from section 3).
- **Title:** editable in place (`contenteditable` or an input styled as `.doc-title`).
- **Tabs:** Notes | Transcript. On the right: an "Only my notes" switch and Attach.
- **Notes** (`.notes`): one document that merges what the user typed with what Claude wrote from the recording (section 6.2).
  - User-typed text: `.mine`, ink, weight 600. Claude's text: `.ai`, `--color-ink-muted`, weight 400.
  - A legend line at the top: "What you wrote" / "Added by Claude from the recording".
  - "Only my notes" hides `.ai`.
  - The whole document stays editable. Text the user edits or types becomes `.mine`.
  - Render math with KaTeX. Readable width: 68ch, 16px, line height 1.65.
- **Transcript tab:** speaker-labeled paragraphs with timestamps; clicking a timestamp plays from there (keep the existing player).
- **Ask bar:** "Ask about this lecture" (noun per folder), scope "This lecture".

### 5.5 Recording (`recording.html`)
When recording starts (from Record, Coming up, Up next or the mini recorder), **open the note page for the new note right away** with:
- **Recording bar** (`.recbar`) at the top of the page: pulsing red dot, "Recording", level meter, elapsed time, then **Pause** and **Stop** (ink button).
- **Breadcrumb and title** prefilled from the calendar event ("Machine Learning, Mon 12 Oct"), or "Recording, Mon 12 Oct at 1:45 PM" when there's no event.
- **Tabs:** Notes | Live transcript.
- **Notes:** an empty editable document with the cursor in it, and a hint below: "Write as much or as little as you like. When you stop recording, Claude writes up the lecture around your notes." Everything typed here is stored as the user's notes.
- **Ask bar:** "Ask about what's been said so far".
- The **mini recorder** (`.mini`) appears when the app window isn't focused, and the main Record button shows its recording state on other pages.

---

## 6. New behavior

### 6.1 Start recording from Coming up and Up next
- Read events from the calendar source the app already uses for "Coming up".
- **Match each event to a folder:** use an explicit mapping if one exists; otherwise match the event title to a folder name or course code; otherwise Unsorted.
- **Clicking an event:** create a note in that folder, titled from the event, linked to the event ID; start recording; navigate to its note page (5.5). If a recording is already running, ask first: "Stop recording Data Mining and start Capstone team sync?" (Stop and start / Cancel).
- Starting from an event more than 15 minutes early is allowed; the button just isn't shown until hover.

### 6.2 Notes merged into the summary (Granola-style)
- **Store the user's notes separately** from the generated write-up: `note.userNotes` (rich text, as typed, with timestamps per block if available) and `note.enhanced` (the merged document).
- **When recording stops,** send the transcript plus `userNotes` to the existing summary step, with an instruction like this:

  > Rewrite these notes into a structured write-up of the lecture. Keep every line the user wrote, verbatim and in their order, as headings or bullet starts. Under or after each user line, add the relevant details from the transcript. Add sections for important material the user didn't note. Mark each span as `user` or `ai`.

- **Output format:** a block list, e.g. `[{type: "heading"|"bullet"|"paragraph", spans: [{text, source: "user"|"ai"}]}]`. Render `source: "user"` with `.mine` and `source: "ai"` with `.ai`.
- **If the user wrote nothing,** generate the write-up as today; it's all `.ai`, and the legend reads only "Written by Claude from the recording".
- **The note's one-line summary** (used in list rows) comes from the same step.
- Edits after generation update `enhanced`; edited spans become `user`. "Regenerate" (in a ⋯ menu) rebuilds from `userNotes` + transcript and asks before overwriting edits.

### 6.3 Unsorted and moving notes
- **Unsorted** = a note with no folder. It shows in the Home list with the inbox marker.
- **Moving notes:** the folder label on a Home row, the breadcrumb chevron on a note page, and the toolbar Move action all open the same folder picker; dragging a row onto a sidebar folder also works. After a move, show a toast: "Moved to Machine Learning. Undo".

---

## 7. Class colors

Each folder has one **highlighter color**: yellow, green, pink, blue, orange, violet, teal or lime. Store `folder.color`, assign the next unused color on creation, and let the user change it in folder settings. It appears as:
- the **swatch** in the sidebar, Coming up and the filter menu (`--swatch-*`)
- the **highlight** behind the folder name in note rows and Up next, and behind assignment names in the calendar (`--hl-*`)
- the **underline highlight** on the folder page title

Never use folder colors for status, and never fill large areas with them. Sample mapping: Machine Learning blue, Capstone green, Computer Architecture violet, Data Mining orange, Theory of Computation pink, ACM teal, Intellievent yellow, NSWC lime.

---

## 8. Content rules

- **Dates:** "Today", "Yesterday", then "Wed 7 Oct" within the year, then "7 Oct 2025". Times: "9:30 AM", "11:59 PM".
- **Durations:** "13 min", "1 h 12 min".
- **Class names:** strip registrar suffixes. Keep the course code for the folder lede and tooltips.
- **Nouns:** lecture / meeting / recording / notes, per section 3.
- **Voice:** plain, second person, sentence case, no exclamation marks, no emoji. Ledes are one or two sentences, with the most urgent thing first; never bold or color part of a sentence.

---

## 9. Accessibility

- Icon-only buttons have an `aria-label` and a matching `title` (include the shortcut).
- Coming up and Up next items are buttons whose label says what happens: "Record Capstone team sync, starts in 11 minutes".
- The calendar is a grid; each item's accessible name includes its class, due date and state ("Missing", "Handed in").
- The recording bar is `role="status"`. Pause and Stop are reachable by keyboard; ⌘R toggles recording.
- Note rows are a single link (the title link stretches over the row), with the folder picker as a separate button inside (`aria-haspopup="menu"`, labeled "In Machine Learning. Change folder").
- Status never relies on color alone.

---

## 10. Acceptance checklist

- [ ] There is one sidebar. No list column beside a note page.
- [ ] No metric tiles anywhere; no class card grid; no Unsorted item in the sidebar.
- [ ] A class has exactly one page (no tabs, no "Class home").
- [ ] Due is a month calendar with Missing and Handed in states, a class filter and month navigation.
- [ ] Clicking a Coming up or Up next item starts recording into the right folder and opens the note page. Neither shows a separate Record button.
- [ ] You can type notes while recording; after Stop, the write-up keeps your lines in ink and adds Claude's text in grey; "Only my notes" works.
- [ ] Nouns follow the folder kind: lecture in classes, meeting in clubs and work, recording when unsorted.
- [ ] The Home notes filter shows All or Unsorted in place, and navigates when a folder is picked; unsorted rows show the inbox marker.
- [ ] Clicking the folder label on any Home row opens the folder picker (search, Unsorted, grouped folders with counts, New folder) and moves the note in place, with Undo.
- [ ] Every screen matches its screenshot in light and dark.
- [ ] Only Atkinson Hyperlegible Next is loaded; every color comes from a token; red appears only for recording and late items.
- [ ] Summaries render math, with no `$` visible.
- [ ] Keyboard: ⌘R record, ⌘J Ask, ⌘K search; focus rings are visible; reduced motion stops looping animation.
