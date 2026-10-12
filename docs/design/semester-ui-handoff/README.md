# Semester UI redesign: handoff for Claude Code

This folder is the complete spec for restyling the app's interface. It changes how things **look and are laid out**, not what the app does: keep all data loading, recording, Canvas sync and AI logic exactly as it is.

```
semester-ui-handoff/
├── README.md              ← this spec (read all of it first)
├── tokens/
│   ├── tokens.css         ← every color, type style, space and radius, light + dark
│   └── tokens.json        ← same values for non-CSS code (Swift, etc.)
├── reference/
│   ├── home.html          ← the Home screen, built and working; open it in a browser
│   ├── components.html    ← every component in every state
│   └── semester.css       ← the reference implementation of each component
├── screenshots/           ← home + components, light + dark (2x)
├── icons/                 ← the 22 icons used, Lucide 1.5 stroke, currentColor
└── fonts/                 ← Atkinson Hyperlegible Next (variable woff2) + OFL license
```

`reference/home.html` and `screenshots/home-light.png` are the source of truth for the look. When this README and the reference disagree, the reference wins. Open `reference/*.html?theme=dark` for dark mode.

---

## 1. How to do the work

1. **Read the codebase first.** Find the UI framework, where styles live (CSS files, Tailwind config, CSS-in-JS theme, SwiftUI modifiers), and the components that render: the sidebar, the Home screen (greeting, stat tiles, Recent lectures, Due soon, class cards), the record button, the floating recorder, and the Ask bar.
2. **Install the foundations.** Copy `fonts/` into the app's assets and load `tokens/tokens.css` once at the root (or port `tokens.json` into the existing theme object or Tailwind `theme.extend`). Keep the token names, so the reference CSS can be ported directly.
3. **Delete the old look.** Remove the old colors, shadows, font stacks and one-off styles as you replace each component. Don't leave two systems running side by side.
4. **Rebuild components one at a time** against `reference/semester.css` and `reference/components.html`, in this order: sidebar → page header and record button → week planner → lecture list → Ask bar → mini recorder → other screens.
5. **Check each step visually.** Run the app (or a Storybook/preview) in light and dark, screenshot it, and compare against `screenshots/`. Fix the differences before moving on.
6. **Finish with the acceptance checklist** in section 9.

If the app isn't web-based (e.g. SwiftUI), use `tokens.json` for values, and treat the HTML/CSS as a precise visual spec: same sizes, spacing, weights and colors.

---

## 2. What to remove

The current Home screen reads like a generic dashboard template. All of these go:

- **Stat tiles** (12 Lectures / 8 Classes / 10 To hand in / 1 Overdue / 1 Unsorted). Counts live in the sidebar and in one sentence under the date.
- **The "Good morning" greeting.** The page title is the date.
- **Colored dots** for classes. Classes are shown with highlighter swipes (section 4).
- **Icon boxes** on list rows, and icons in tiles, headings or counts.
- **Pill badges** like "Missing" or "In 2 days". Status is shown by where an item sits in the week, plus plain red text for late items.
- **Rounded panels inside rounded panels** (the grey "Recent lectures" and "Due soon" boxes).
- **The full-red "Recording…" button.** Red only fills the record dot.
- **The sparkle icon and the "Claude" model pill** in the Ask bar. (Model choice can move to settings, or sit in the scope menu.)
- **Raw LaTeX in summaries** (`$b = Ax + s$`). See section 6.
- **Registrar suffixes in class names** ("Theory of Computation and Compiler(CSCI4150.A)" → "Theory of Computation and Compilers").
- **Class cards** at the bottom of Home. The sidebar already lists classes.

---

## 3. Foundations

All values are in `tokens/tokens.css`. Rules for using them:

**Surfaces.** The window background is `--color-canvas`. The **sidebar sits directly on the canvas, with no panel behind it**. The main pane is one `--color-surface` sheet with `--radius-pane` and a 1px `--color-line` outline, and no drop shadow. The week grid is `--color-surface-sunken`. Today's column is `--color-surface-today`, lifted with `--shadow-today`. Only the Ask bar and the mini recorder float (`--shadow-float`).

**Text.** `--color-ink` for titles, names and anything you act on. `--color-ink-muted` for summaries, dates, inactive nav and class meetings. `--color-ink-subtle` for counts, times, placeholders and the sidebar footer. Every pairing used holds 4.5:1 contrast in both themes.

**Red means recording or late, nothing else.** `--color-record` is the record dot, the live level meter and the "Recorded" dot on a class meeting. `--color-late` is text for late items and the "Late" column heading. Never use red for decoration, links, or errors that aren't about deadlines.

**No accent color.** Links are `--color-ink-muted` with a 2px `--color-line` underline that turns `currentColor` on hover. Focus is a 2px solid `--color-focus` outline, offset 2px.

**Type.** One family, Atkinson Hyperlegible Next (made for legibility, OFL licensed). Weight carries the hierarchy, not size jumps or extra fonts. Use the `--text-*` shorthands: `--text-date` for the page title only, `--text-h2` for section titles, `--text-title` for lecture titles, `--text-due` for assignment names in the week, `--text-meta` / `--text-micro` for supporting text. Use tabular figures everywhere (`font-variant-numeric: tabular-nums`). Sentence case everywhere; **never uppercase labels**.

**Spacing.** 4px base (`--space-*`). Main pane side padding is `--space-14` (56px); sections are 36px apart; the lecture grid uses a 20px gap.

**Radius is nested, not uniform:** controls 8px → today column 10px → week grid and Ask bar 12px → main pane 14px → record button pill. Highlights use their own irregular radius (in `.hl`).

**Motion.** Hover changes color only (120ms). The only looping animation is the live level meter while recording. No entrance animations. Respect `prefers-reduced-motion`.

---

## 4. Class colors (the signature)

Each class gets one **highlighter color**: yellow, green, pink, blue, orange, violet, teal or lime. It's the one bold element in the UI. Everything else stays black, grey and white.

- **Data:** store a `color` key per class (one of the eight names). Assign in this order as classes are added: yellow, green, orange, pink, blue, teal, violet, lime. Let the user change it in class settings. Persist it.
- **Highlight** (`<mark class="hl">` inside an element with `data-hl="<color>"`): a marker swipe behind ink text, with irregular rounded corners that continue across line breaks. Use it for assignment names in the week grid and the class name under each lecture title. In dark mode the highlighter colors are deep and the text stays light. That's handled by the tokens, so don't override it.
- **Swatch** (`.swatch`): a small, slightly rotated mark. Use it next to class names in the sidebar and in class meetings. Swatches use the `--swatch-*` tokens (brighter in dark mode, because no text sits on them).
- Never use class colors for status, and never fill large areas with them.

Current mapping for the sample data: Chapel blue, Web App Dev green, Theory of Computation yellow, Machine and Deep Learning orange, Data Mining pink, Capstone teal, Internship violet, Personal Projects lime.

---

## 5. Screens

### App frame
Window padding 8px, 8px gap between sidebar and pane. Sidebar 244px wide.

### Sidebar (`.sidebar`)
- **Toolbar row:** hide-sidebar button on the left; back and forward on the right (forward disabled at 40% opacity when there's nowhere to go). Icon buttons are 32px, icons 18px at 1.5 stroke.
- **Nav:** Home, Due, Unsorted, each with an icon. Counts are right-aligned in `--color-ink-subtle`, with no badge background. The selected item is a white `--color-surface` chip with a 1px line outline, bold text and `aria-current="page"`.
- **"Classes" heading** in `--text-meta` bold, subtle, sentence case, with a 24px "+" Add class button.
- **Class items:** swatch, class name (single line with ellipsis; the full name goes in the tooltip) and an optional count of new lectures. **Hide zero counts.**
- **Footer** at the bottom, two plain lines in `--text-micro`, subtle: "Library on this Mac" and "Canvas synced at 10:18". If sync fails, the second line becomes "Canvas sync failed. Retry" in `--color-late`, with Retry as a link.

### Main pane toolbar
Search and Settings icon buttons, top right. Nothing else.

### Home: page header
- Title: today's date in `--text-date`, e.g. "Monday, October 12".
- **Lede sentence** under it in `--text-lede`, muted: one plain sentence built from the data, covering the most urgent thing first. Examples:
  - "Two assignments due Wednesday, and one asynchronous lecture still to watch."
  - "CCHW2 is due tonight at 11:59 PM."
  - "Nothing due this week."
  
  Don't color or bold part of the sentence.
- **Record button** (`.record`) aligned to the bottom-right of the header (see Recording, section 7).

### Home: "This week" planner (`.week`)
This replaces both the stat tiles and the "Due soon" list. It's the main thing on Home.
- **Columns:** an optional **Late** column first (only when something is overdue and not handed in), then Mon–Fri, then Sat and Sun at 0.8 width. Grid: `1.15fr repeat(5, 1fr) .8fr .8fr` (`.week--no-late` drops the first column).
- **Day header:** day name (`Today` for the current day) on the left, date number on the right in `--text-daynum`. The Late column has no number, and its heading is in `--color-late`.
- **Today** is lifted out of the grid: `--color-surface-today`, `--radius-day`, `--shadow-today`, extending 4px above and below.
- **Class meetings** (`.meetings`) at the top of each day: time in `--text-micro` subtle, then swatch and short class name. A meeting that was recorded shows a 6px red dot and "Recorded" after the time. **If the app has no class schedule data, leave out meetings entirely**; don't invent them.
- **Deadlines** (`.deadlines`) below a hairline: assignment name in a highlight with the class color, and the due time below in `--text-micro` ("11:59 PM"). In the Late column the line under the name reads "Was due Mon 5" in `--color-late`. A handed-in item stays on its day, struck through in its swatch color, with "Handed in" below.
- Clicking a deadline opens it (same behavior as the current Due soon rows). On hover, an ink outline appears around the highlight.
- When there are more than 4 deadlines on one day, show 3 and a "+2 more" text link.

### Home: "Recent lectures" (`.lecture`)
- **Section head:** "Recent lectures" on the left; on the right a text link that names the total, e.g. "All 12 lectures".
- **Row grid:** `108px | 1fr | 64px`. The date sits on the left in `--text-meta` bold muted ("Today", "Yesterday", "Wed 7 Oct", "Wed 30 Sep"). The middle holds the title (`--text-title`, wraps), then the class name as a highlight, then a two-line summary clamped at 64ch. Length is right-aligned ("13 min", "1 h 12 min").
- Rows are separated by 1px `--color-line` hairlines. **No boxes, no icons, no backgrounds.** On hover, the title is underlined.
- While a summary is being generated, show "Writing summary…" in italic subtle text instead of a badge.
- Show the 4 most recent.

### Ask bar (`.ask`)
Docked at the bottom of the main pane, inset 56px from each side and 20px from the bottom, 52px tall, `--radius-panel`, `--shadow-float`. A 120px fade to `--color-surface` sits behind it, so content scrolling underneath doesn't collide with it.
- **Contents:** text input ("Ask anything about your lectures"), a scope menu button ("All classes ⌄"; on a class page it defaults to that class), and a `⌘J` key hint. When there's text, the key hint swaps for a round ink send button (`.ask-send`) with an up-arrow icon.
- Focus draws a 1.5px ink ring. ⌘J focuses it from anywhere.

### Other screens
Apply the same rules to Due, Unsorted and the class pages:
- **Due:** group by day, with Late first. Each item shows its title in a highlight, the due time, and the class name in muted text. Use the same `.deadline` styling as the week grid, at list width.
- **Class page:** the title is the class name in `--text-date`, with the code ("CSCI 4150") and meeting times in the lede. Below it: Upcoming deadlines (same as Due, filtered), then Lectures (same rows as Recent lectures, without the class highlight, since you're already on that class).
- **Unsorted:** lecture rows, with a "Move to class" menu button on each row.

---

## 6. Content rules

- **Dates:** "Today", "Yesterday", then "Wed 7 Oct" within the year, then "7 Oct 2025". Due times are "11:59 PM". Never "10/5/2026".
- **Durations:** "13 min", "1 h 12 min".
- **Class names:** strip the registrar suffix in parentheses. Keep the code (e.g. "CSCI4150.A" → "CSCI 4150") for class pages and tooltips only. In tight spots (class meetings), use the class's short name: Theory, Web Dev, Deep Learning, Data Mining, Chapel, Capstone. Add an editable `shortName` field per class, defaulting to the first 1–2 words.
- **Math in summaries:** render `$…$` with KaTeX (or MathJax) inline. If that's not possible, strip the dollar signs and convert simple `^T`, `^2` and `_i` to Unicode (cᵀx, x², xᵢ). Never show raw LaTeX.
- **Voice:** plain, second person, sentence case, no exclamation marks, no emoji.

---

## 7. Recording

- **Idle:** `.record`, a white pill with a 1px line outline. Inside: a 40px red dot with a white inner dot, the word "Record", and a `⌘R` hint.
- **Recording:** add `.is-recording`. The pill outline turns 1.5px `--color-record`; the inner dot morphs into a rounded stop square (200ms); the label becomes the class being recorded (its short name); a five-bar red level meter follows, then the elapsed time ("12:04") in muted text. Clicking stops recording. Use `aria-pressed="true"`, and include the elapsed time in the aria-label.
- **Paused:** `.is-paused`. The dot becomes a red ring with a red inner dot, and the label reads "Paused" plus the time.
- **Mini recorder** (the floating "S" widget today): replace it with `.mini`, a white floating pill, 44px tall. Idle, it's just the 32px record dot. Recording, it shows dot (stop) + short class name + meter + time. It keeps its current docking behavior.
- After a recording stops, the class meeting it belongs to shows "● Recorded" in the week grid, and the lecture appears at the top of Recent lectures with "Writing summary…".

---

## 8. Accessibility

- Every icon-only button has an `aria-label` and a matching `title` tooltip (include its shortcut).
- The week grid is a list (`role="list"` / `listitem`), and each deadline is a link whose accessible name includes its class and due time.
- Focus is visible everywhere (2px outline). Tab order runs sidebar → toolbar → page → Ask bar.
- Status never relies on color alone: late items say "Was due…", recorded meetings say "Recorded", handed-in items say "Handed in".
- Light and dark both follow the system setting, with an override in settings.

---

## 9. Acceptance checklist

- [ ] Home matches `screenshots/home-light.png` and `home-dark.png` in layout, spacing, type and color.
- [ ] No stat tiles, no greeting, no colored dots, no pill badges, no icon boxes on rows, no sparkle icon, no nested grey panels.
- [ ] The only fonts loaded are Atkinson Hyperlegible Next and the system fallback.
- [ ] Every color comes from a token; no hard-coded hex values left in components.
- [ ] Red appears only for recording and late items.
- [ ] Each class has a persisted highlighter color, used consistently in the sidebar, week grid, lecture rows and class page.
- [ ] Class names show without registrar suffixes.
- [ ] Summaries render math, with no `$` visible.
- [ ] Record button and mini recorder work in idle, recording and paused states, as in `components.html`.
- [ ] The Ask bar never covers the last lecture row (bottom padding plus fade).
- [ ] Dark mode is checked on every screen; text contrast holds.
- [ ] Keyboard: ⌘R toggles recording, ⌘J focuses Ask, ⌘K opens search; focus rings are visible.
- [ ] Reduced motion stops the level meter animation.
