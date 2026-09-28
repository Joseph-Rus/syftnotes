# Study Stash: Calendars

Study Stash reads the student's calendars to show what's coming up, and to name and file a recording by the class
it's for. Calendars run on the computer that records (the laptop; on a one-computer setup, that's the library too),
and everything is read-only: nothing is ever created, changed or deleted in a calendar.

```
Feed (webcal:// or https://…ics)                laptop (records)                library
  downloaded, read (RRULE expanded,   ──────▶  Upcoming: merges every source's        │
  time zones resolved)                          shown calendars, matches each         │
                                                 event's title to a class             │
                                                                          │            │
                                                 Recording started ◀──────┘            │
                                                 during an event takes                 │
                                                 its title and class                   │
                                                                          │            │
                                                 POST /api/v2/calendar/upcoming ──────▶ GET /api/v2/calendar/upcoming
                                                 (its week of events)                  (Coming up, the home page,
                                                                                         the phone app)
```

## Sources this round, and what's coming

This round only reads **iCalendar feeds** pasted into Settings: Google's "secret address" in iCal format, an iCloud
public calendar, a school's timetable, Canvas's own calendar feed — anything that answers with a `.ics` file at a
`webcal://` or `https://` address. Apple Calendar (macOS's own, once the student allows it), a signed-in Google
account and a signed-in Microsoft account come in a later wave; Settings' "Connect" buttons and the code beneath them
are already built to take them.

The contract every source implements is `ICalendarSource` (`engine/src/StudyStash.Core/Calendar/CalendarSource.cs`):

```csharp
public interface ICalendarSource {
    string Id { get; }      // "ics:<hash>", "apple", "google:<account>", "microsoft:<account>"
    string Kind { get; }    // "ics" | "apple" | "google" | "microsoft"
    string Name { get; }    // shown in Settings
    Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct);
    Task<IReadOnlyList<CalendarEvent>> EventsAsync(IReadOnlyCollection<string> calendarIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

`CalendarKinds` (`Calendar/CalendarKinds.cs`) is the registry every part of calendars asks: it knows how to make a
source from what `calendars.json` keeps, and, for a kind that connects with a button rather than a pasted address,
what that button says and does (`CalendarKind.ConnectLabel`, `.Connect`). `CalendarKinds.Default` knows iCalendar
feeds out of the box; Apple, Google and Microsoft will `Register` themselves as the app starts, wherever the
platform has them, and Settings shows their "Connect" button from then on — nothing in Settings, the sync or the
matching needs to change when they do.

## The iCalendar feed

`Calendar/Ics/IcsSource.cs` and its reader (`IcsReader.cs`, `IcsRule.cs`, `IcsZone.cs`, `IcsValues.cs`,
`IcsFeed.cs`) turn a pasted address into events:

- `webcal://` and `webcals://` are read as `https://`; a pasted address is tidied (a bare `calendar.google.com/…`
  gets its `https://`) and checked that it actually looks like a web address before it's saved.
- One address is one source: its id is `"ics:"` plus a short fingerprint of the address (lower-cased, scheme
  normalised), so pasting the same feed twice adds nothing twice.
- The feed is downloaded at most once a minute however often it's asked, since listing calendars and reading events
  come from the same download; a feed over 20 MB, one that isn't reachable, one that answers 401/403/404, or one
  whose address isn't a calendar feed at all each say why in plain words instead of a raw error (`CalendarFeedException`).
- The reader parses folded lines, quoted and unquoted parameters, and escaped text; a broken line is skipped rather
  than failing the whole feed.
- `RRULE` is expanded into real occurrences in the window asked for: weekly classes on their days, every *n*th week,
  the *n*th weekday of a month, until an end date or a count, with `EXDATE` (skipped days) and `RECURRENCE-ID`
  (a moved or cancelled occurrence) applied on top.
- Time zones are read from any name the feed uses — an IANA name, a Windows name, or a `/freeassociation.sourceforge.net/`-style
  prefixed path — or, failing that, from the feed's own `VTIMEZONE` rules; skipped and repeated hours (daylight
  saving) are handled the way the operating system's own time zone database does.
- All-day events, attendees (names, or addresses when there's no name) and a meeting link or the event's own page are
  kept; a cancelled occurrence is kept too, marked `Cancelled`, and dropped before the student ever sees it.

Tested against real feeds from Google, iCloud, Canvas and Outlook (`Fixtures/calendar/*.ics`).

## What's coming up

`Calendar/Upcoming.cs` is what actually runs on the laptop: every five minutes, or at once when Settings changes
something (`Wake()`), it reads every *shown* calendar of every source, from twelve hours ago to seven days ahead,
merges the results in time order without the cancelled ones or the same event twice (one class on both a Google
calendar and the school's feed, matched by source-and-id first, then by title, start, end and all-day-ness), and
saves it (`calendar_upcoming.json`) so the app's dropdown shows what it had the moment it opens, before anything is
read again. A source that can't be read for one round keeps what it had last time, and its problem is kept in
`calendars.json` for Settings to show in plain words; nothing else about it is touched.

**"Coming up"** (`ComingUpModel`, `Upcoming.Coming`) is the next few timed events (never an all-day one) today and
tomorrow, each with when it is ("Now, until 11:15 AM", "10:00 AM", "Tomorrow 9:00 AM"), what it's called, where, and
the class it's for with that class's dot. It's drawn in the app's dropdown (the quick panel), the library window's
sidebar, and, from the library's own data, the library's home page — hidden while there are no calendars; with
calendars and nothing left today or tomorrow, one quiet line says so.

## Class matching

`Calendar/EventClass.cs` decides which class (if any) an event's title is for, using the library's own classes,
their other names, and their Canvas course codes (`ClassHint`, `EventClass.From(Overview)`):

- A class matches by its own name, one of its other names, or its Canvas course code appearing in the title.
- Failing that, a shared course number *and* a shared word between the title and a class's name can match (so
  "COMP 101 Lecture" matches a class named "CS 101" only when something else ties them, e.g. a shared code).
- A loose match, or one that could equally be two different classes, is no match at all — Study Stash would rather
  show no class than the wrong one.

The same matcher runs in three places: the laptop, before it sends its week to the library (so a library that's
never read `EventClass` itself still gets a best guess); the library, when it serves `GET
/api/v2/calendar/upcoming` (matched fresh against its own classes and Canvas codes, which is more likely to be right
than whatever the laptop guessed); and the app's "Coming up" list and the line under Record.

## Recording titled and filed by the calendar

`Upcoming.During` decides the event a recording started *now* is for: the one that's on right now (the most
recently started), or, failing that, the next one starting within ten minutes — one with a matched class before one
without, and a later start before an earlier one when both are on now. `AppHost.EventNow()` exposes it as
`EventNow(CalendarEvent, string? Class)`, and:

- **The class.** If the student hasn't picked a class by hand, and the event's class is one the library actually
  has, Record shows that class and its dot, with the line "From your calendar: *event title*" underneath
  (`Shell.Calendar.cs`: `CalendarClass()`, `CalendarHint()`). Picking a class by hand always wins.
- **The title and the kept event.** `RecordingEvents.For` decides what a new recording starts as: when nothing was
  picked and an event matches, the lecture's title becomes the event's title, and the event itself (title, start,
  place, who's there, and which calendar it came from) rides along in what the laptop sends the library, kept on the
  lecture (`Lecture.Event`) so the library files it under the matched class the same way, without asking Canvas or
  the calendar again.
- **Auto-record** (a class starting its own recording when its event begins, with no one at the keyboard) isn't in
  this round. `calendars.json` already has a place for it (`AutoRecordClasses`), so it can be added without a new
  file format.

## The laptop → library sync

Calendars run on the laptop; the library only ever hears what's coming up, never anyone's calendar itself. After
every read (a "read", not necessarily a change), the laptop sends its week ahead to its library
(`UpcomingSender`, `Services/AppHost.Calendar.cs`): not again unchanged for half an hour, so a library that's
unreachable is only ever told once per silence, and told again the moment something did change.

- `POST /api/v2/calendar/upcoming` — body `{"events":[{"id","title","start","end","allDay","location","calendar",
  "class"}]}`, up to 500 events; the library keeps them in `calendar.json` beside `config.toml`
  (`LibraryCalendar.Save`, `Core/Calendar/LibraryCalendar.cs`). Anything that isn't a real id, title, and start/end
  that read as times is dropped rather than failing the whole request.
- `GET /api/v2/calendar/upcoming` → `{"events":[{"id","title","start","end","allDay","location","class"}],
  "updated"}`: the kept events not over yet, soonest first, each re-matched to the library's own classes and Canvas
  codes (falling back to the class the laptop matched, if the library still has a class of that name). `updated` is
  null before any laptop has ever sent anything.

In one-computer mode the laptop and the library are the same process, so the "sync" is really just one side calling
the other's local API — nothing about the contract changes.

## `calendars.json`

Kept beside `client.toml`, on the computer that records (`Calendar/CalendarSettings.cs`):

```json
{
  "sources": [{"id": "ics:1a2b3c4d5e6f", "kind": "ics", "name": "Fall 2026 classes",
               "url": "webcal://…", "data": {}, "added": "2026-09-28T12:00:00Z"}],
  "calendars": [{"id": "ics:1a2b3c4d5e6f", "sourceId": "ics:1a2b3c4d5e6f", "name": "Fall 2026 classes",
                 "color": null, "enabled": true}],
  "enabled": {"ics:1a2b3c4d5e6f": true},
  "autoRecordClasses": [],
  "problems": {}
}
```

- **sources** — every source the student added; `data` is for whatever a kind needs to make itself again (an
  account id, say) — never a password or a token, which a kind keeps in the system's keychain instead.
- **calendars** — every calendar every source had, last time it was listed, so Settings shows them at once without
  waiting on a read.
- **enabled** — the student's own switch per calendar, in Settings → Calendars; a calendar not here follows its
  source's own suggestion instead.
- **autoRecordClasses** — reserved for auto-record (see above); nothing in calendars reads it yet.
- **problems** — what went wrong reading each source last time, in words for Settings; a source that read fine isn't
  listed here.

Written owner-only (`Py.OwnerOnly`), since a feed's address is often a secret one (Google's "secret address" is a
password by another name).

## Settings → Calendars

Both looks (`Views/Mac|WinCalendarSettings.axaml`, one shared `CalendarSettingsModel`), reachable only on a computer
that records (next to Recording in the sidebar — a library-only computer has no calendars to show):

- **Add a calendar feed** — paste an address; a bad one says so before anything is read, a repeated one says it's
  already added, and one that can't be read says why (too big, not public, not there any more, not a calendar feed
  at all) — never a raw exception.
- **Connect a calendar** — one button per registered kind that offers one; none this round, so a quiet line says
  Apple, Google and Outlook are coming, and to paste a feed's address for now.
- **Each source**, once added: its name, a switch per calendar it has (shown/shared or not), and Remove, which takes
  the whole source and its calendars away at once.

Every change saves to `calendars.json` immediately and wakes the running read (`Upcoming.Wake()`), so Coming up and
a recording's class catch up without waiting for the next five-minute tick.

## Privacy

Calendars are **read-only** everywhere in Study Stash: nothing is ever created, changed or deleted in a calendar,
whatever the source. Calendars are read by the laptop that records; it keeps what it read on that computer and sends
only the next seven days (title, times, location, and the class it matched) to its own library — never to the
developers of Study Stash or anyone else. It isn't sold, used for advertising, or handed to the AI engine that writes
notes; only the name of the class it matched is used. Removing a source in Settings deletes its calendars and its
switches from that computer at once; once Apple, Google or Microsoft sources land, revoking access from the
account itself stops Study Stash reading anything more. This matches the website's privacy policy
(`site/privacy/index.html`, "Calendars").

## Testing

Nothing here ever reaches a real calendar service. `Fixtures/calendar/*.ics` are real feeds (redacted) from Google,
iCloud, Canvas and Outlook, read through `FeedServer` (`IcsSourceTests.cs`), a pretend web server that answers
whatever a test says and counts what it was asked. `IcsFeedTests`, `IcsReaderTests`, `IcsRuleTests`, `IcsZoneTests`
cover the reader and RRULE/time-zone expansion in isolation; `CalendarSettingsTests` covers `calendars.json`;
`LibraryCalendarTests` covers the library's own save/read and its class matching; `AppCalendarTests` covers a
recording started during an event taking its title and keeping the event, and one started with nothing on being
unchanged. `ComingUpViewTests` draws "Coming up" in both looks, in the dropdown and the library window; app-side
Settings tests (`CalendarSettingsModelTests`, `CalendarSettingsViewTests`) cover adding a feed (and what goes wrong),
a switch's save, Remove, and both looks in both themes. `LibraryWebTests` covers the home page's "Coming up" showing
(and, with nothing sent, not showing at all).
