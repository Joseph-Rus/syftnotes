Copy the `semester-ui-handoff` folder into the root of the app's repo, then paste this into Claude Code:

---

Redesign this app's UI using the handoff in `semester-ui-handoff/`.

1. Read `semester-ui-handoff/README.md` completely. Then open every page in `semester-ui-handoff/reference/` (add `?theme=dark` too) and look at the images in `semester-ui-handoff/screenshots/`. These are the target design and structure.
2. Explore this codebase. Tell me which files render the sidebar, Home, Due, the class page (including its "Class home" layout and tabs), the lecture detail and its list column, the record button, the floating recorder, Coming up and the Ask bar. Tell me where styles, theme values, calendar events, notes and summaries live. Then propose a plan, in the order given in README section 1, before editing.
3. Do the navigation changes in README section 3 first, then rebuild each screen against its reference page. Keep the existing recording, transcription, Canvas sync and AI pipelines; extend them only as described in section 6 (start recording from events, user notes merged into the write-up, unsorted handling).
4. After each screen, run the app in light and dark, take screenshots, and compare them with `semester-ui-handoff/screenshots/`. Fix the differences before moving on.
5. Finish by going through the acceptance checklist in section 10, and tell me anything you couldn't meet and why.
