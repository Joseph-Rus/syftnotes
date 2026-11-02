import SwiftUI
import UIKit

// Due, as the desktop's (Views/DueCalendar.axaml, WeekPlanner.axaml, ViewModels/DueCalendarModel.cs and
// OverviewModel's DueSentence and WeekOf), built to the flows plan (iphone-look-refs/FLOWS.md, "due"): a calendar, not
// a list. The title, a sentence naming what's most pressing, when Canvas last synced, then the week or the month, each
// thing due marked with its class's highlighter, "Missing" in red under one that's late, a handed-in one struck
// through. A phone opens on the week: the desktop's planner on its side, a block a day, each day's classes (a tap
// records one) over what's due. Its month is a compact grid; a tapped day is listed under it in the same rows. An iPad
// opens on the desktop's month, and its week is the desktop's planner as it is. Weeks run Sunday to Saturday, as
// Canvas's calendar and the desktop's have them.

// MARK: - What's due, worked out (the desktop's rules)

/// Something to hand in, on its day: whether it was handed in (struck through).
struct DueEntry: Identifiable, Hashable {
    var item: DueItem
    /// When it's due; nil for undated work (never on the calendar).
    var at: Date?
    /// Handed in, excused, or otherwise not the student's to hand in any more.
    var done: Bool
    var id: String { "\(item.class)#\(item.id)" }
}

/// Every assignment on the Due list, and the desktop's sums over them: what's missing, what goes on which day, what
/// a calendar item says under its name, and the sentence at the top.
struct DueBook {
    var entries: [DueEntry]
    var now: Date
    var cal: Calendar

    init(_ list: DueList?, now: Date = .now) {
        self.now = now
        var c = Format.calendar
        c.firstWeekday = 1
        cal = c
        var seen = Set<String>()
        var out: [DueEntry] = []
        for g in list?.groups ?? [] {
            for i in g.items where seen.insert("\(i.class)#\(i.id)").inserted {
                out.append(DueEntry(item: i, at: Format.parse(i.due), done: g.key == "handed_in" || !Self.toHandIn(i)))
            }
        }
        entries = out
    }

    /// Still the student's to hand in (CanvasWords.ToHandIn).
    static func toHandIn(_ i: DueItem) -> Bool { ["open", "to_hand_in", "past due", "missing", "overdue"].contains(i.status) }

    var today: Date { cal.startOfDay(for: now) }
    func day(_ d: Date) -> Date { cal.startOfDay(for: d) }
    func add(_ days: Int, to d: Date) -> Date { cal.date(byAdding: .day, value: days, to: d) ?? d }

    private static let lateStatus: Set<String> = ["missing", "overdue", "past due"]

    /// Past its moment, or Canvas says so.
    func overdue(_ e: DueEntry) -> Bool { e.item.missing || Self.lateStatus.contains(e.item.status) || (e.at.map { $0 < now } ?? false) }

    /// Late enough to be called missing: overdue, from a day before today (something due earlier today stays on
    /// today, with its time).
    func lateBefore(_ e: DueEntry) -> Bool { !e.done && overdue(e) && (e.at.map { day($0) < today } ?? true) }

    /// The calendar's "Missing": past its day (or Canvas says it's missing) and not handed in.
    func missing(_ e: DueEntry) -> Bool {
        !e.done && (e.item.missing || Self.lateStatus.contains(e.item.status) || (e.at.map { day($0) < today } ?? false))
    }

    /// What the calendar shows, for one class or all, soonest first.
    func dated(only: String?) -> [DueEntry] {
        entries.filter { $0.at != nil && (only == nil || $0.item.class == only) }.sorted { $0.at! < $1.at! }
    }

    /// The month's days, a whole week to a row, Sunday first: from the Sunday on or before the 1st to the Saturday on
    /// or after the last.
    func monthDays(_ month: Date) -> [Date] {
        let first = cal.date(from: cal.dateComponents([.year, .month], from: month)) ?? month
        let start = Format.weekStart(first)
        let last = add(-1, to: cal.date(byAdding: .month, value: 1, to: first) ?? first)
        let count = (cal.dateComponents([.day], from: start, to: last).day ?? 0) + 1
        let rows = Int((Double(count) / 7).rounded(.up))
        return (0..<rows * 7).map { add($0, to: start) }
    }

    /// What's due on each day.
    func byDay(only: String?) -> [Date: [DueEntry]] {
        Dictionary(grouping: dated(only: only)) { day($0.at!) }
    }

    /// The line under a thing on its day: "Missing", "Tonight 11:59 PM" today, a time that isn't the end of the day;
    /// nothing for a handed-in one or one due at 11:59 PM.
    func calendarSub(_ e: DueEntry) -> String {
        guard let at = e.at else { return "" }
        if missing(e) { return "Missing" }
        if e.done { return "" }
        let c = cal.dateComponents([.hour, .minute], from: at)
        if day(at) == today { return "\((c.hour ?? 0) >= 17 ? "Tonight" : "Today") \(Format.time(at))" }
        return c.hour == 23 && c.minute == 59 ? "" : Format.time(at)
    }

    /// When, under a thing in a list: the calendar's line, "Handed in" for one struck through, and in the Late block
    /// the day it was due ("Was due Mon 5").
    func when(_ e: DueEntry, inLate: Bool = false) -> (text: String, late: Bool) {
        if e.done { return ("Handed in", false) }
        if inLate {
            guard let at = e.at else { return ("No due date", true) }
            return ("Was due \(Format.words(at, cal.isDate(at, equalTo: now, toGranularity: .year) ? "EEE d" : "d MMM yyyy"))", true)
        }
        let sub = calendarSub(e)
        return (sub, sub == "Missing")
    }

    /// Late, and due before `start` (or undated): what the week's Late block lists.
    func late(before start: Date, only: String?) -> [DueEntry] {
        entries.filter { (only == nil || $0.item.class == only) && lateBefore($0) && ($0.at.map { day($0) < start } ?? true) }
            .sorted { ($0.at ?? .distantPast) < ($1.at ?? .distantPast) }
    }

    // The sentence at the top.

    private static let counts = ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"]
    static func count(_ n: Int) -> String { n >= 0 && n < counts.count ? counts[n] : String(n) }
    static func kind(_ items: [DueEntry], plural: Bool) -> String {
        let quizzes = !items.isEmpty && items.allSatisfy { $0.item.kind == "quiz" }
        return quizzes ? (plural ? "quizzes" : "quiz") : (plural ? "assignments" : "assignment")
    }

    /// What's to hand in, the most pressing first: what's due next this week and when, then what's missing ("Reading
    /// quiz 6 is due tonight, and Asynchronous Lecture 5 is still missing."); "" with neither.
    var sentence: String {
        let weekEnd = add(7, to: day(Format.weekStart(now)))
        let open = entries.filter { !$0.done }
        let late = open.filter(lateBefore)
        let week = open.filter { e in !lateBefore(e) && e.at.map { day($0) >= today && day($0) < weekEnd } == true }
            .sorted { $0.at! < $1.at! }
        var first = ""
        if let w = week.first, let at0 = w.at {
            let d = day(at0)
            let that = week.filter { day($0.at!) == d }
            let on = d == today ? "today" : d == add(1, to: today) ? "tomorrow" : Format.words(d, "EEEE")
            if that.count == 1 {
                let c = cal.dateComponents([.hour, .minute], from: at0)
                let said = d == today ? ((c.hour ?? 0) >= 17 ? "tonight" : "today") : on
                // The end of the day is when most things are due, so it goes unsaid.
                first = c.hour == 23 && c.minute == 59 ? "\(w.item.name) is due \(said)" : "\(w.item.name) is due \(said) at \(Format.time(at0))"
            } else {
                first = "\(Self.count(that.count)) \(Self.kind(that, plural: true)) due \(on)"
            }
        }
        let then: String = switch late.count {
        case 0: ""
        case 1: first.isEmpty ? "\(late[0].item.name) is still missing" : "and \(late[0].item.name) is still missing"
        default: first.isEmpty ? "\(Self.count(late.count)) \(Self.kind(late, plural: true)) are still missing"
            : "and \(Self.count(late.count).lowercased()) are still missing"
        }
        if first.isEmpty && then.isEmpty { return "" }
        return !first.isEmpty && !then.isEmpty ? "\(first), \(then)." : "\(first)\(then)."
    }

    /// Due's lede: the sentence, or that nothing's due this week, or that everything's handed in.
    var lede: String {
        let s = sentence
        if !s.isEmpty { return s }
        return entries.contains { !$0.done } ? "Nothing due this week." : "Nothing to hand in. You're all caught up."
    }

    /// The classes with something on the calendar, in the library's order: the filter's choices.
    func classes(order: [String]) -> [String] {
        let have = Set(entries.filter { $0.at != nil }.map(\.item.class))
        return order.filter(have.contains) + have.subtracting(order).sorted()
    }

    /// "Problem Set 6, Calculus II, due Sun 18 Oct at 11:59 PM, Missing": what a screen reader says for one.
    func said(_ e: DueEntry) -> String {
        let at = e.at.map { d -> String in
            let day = Format.day(d, now: now)
            return "due \(["Today", "Tomorrow", "Yesterday"].contains(day) ? day.lowercased() : day) at \(Format.time(d))"
        }
        let state = e.done ? "Handed in" : missing(e) ? "Missing" : ""
        return [e.item.name, e.item.class, at ?? "no due date", state].filter { !$0.isEmpty }.joined(separator: ", ")
    }
}

/// A class meeting in the week, from the phone's calendar.
struct DueMeeting: Hashable {
    var event: UpcomingEvent
    var start: Date
    var end: Date
    /// The class it's for.
    var cls: String
    /// The class's name when the event is just that, else the event's own title ("Organic Chemistry Lab").
    var title: String
}

/// One block of the week: a day (or, first, what's late), its class meetings and what's due on it.
struct DueWeekDay: Identifiable {
    var date: Date?
    /// "Sun", "Today", or "Late".
    var name: String
    /// The day of the month; the Late block has none.
    var number: String
    var isToday = false
    var isLate = false
    var meetings: [DueMeeting] = []
    var deadlines: [DueEntry] = []
    var id: String { isLate ? "late" : date.map { "\($0.timeIntervalSince1970)" } ?? name }
}

extension DueBook {
    /// One day's block: its class meetings (from the calendar, for one class or all) and what's due.
    func block(_ date: Date, only: String?, events: [UpcomingEvent], classOf: (UpcomingEvent) -> String?) -> DueWeekDay {
        let date = day(date)
        var d = DueWeekDay(date: date, name: date == today ? "Today" : Format.words(date, "EEE"), number: Format.words(date, "d"), isToday: date == today)
        d.meetings = events.compactMap { e -> DueMeeting? in
            guard !e.allDay, let s = Format.parse(e.start), day(s) == date, let cls = classOf(e), only == nil || cls == only else { return nil }
            let title = ClassMatch.normalized(e.title) == ClassMatch.normalized(cls) ? cls : e.title
            return DueMeeting(event: e, start: s, end: Format.parse(e.end) ?? s, cls: cls, title: title)
        }.sorted { $0.start < $1.start }
        d.deadlines = byDay(only: only)[date] ?? []
        return d
    }

    /// A week as the planner shows it: what's late (a block of its own, only in the week that's on, and only when
    /// something is), then Sunday to Saturday.
    func week(_ start: Date, only: String?, events: [UpcomingEvent], classOf: (UpcomingEvent) -> String?) -> [DueWeekDay] {
        let start = day(start)
        var days: [DueWeekDay] = []
        if today >= start && today < add(7, to: start) {
            let late = late(before: start, only: only)
            if !late.isEmpty { days.append(DueWeekDay(date: nil, name: "Late", number: "", isLate: true, deadlines: late)) }
        }
        for n in 0..<7 { days.append(block(add(n, to: start), only: only, events: events, classOf: classOf)) }
        return days
    }

    /// "11 – 17 October", or across two months "27 Sep – 3 Oct".
    func weekHeading(_ start: Date) -> String {
        let end = add(6, to: start)
        return cal.isDate(start, equalTo: end, toGranularity: .month)
            ? "\(Format.words(start, "d")) – \(Format.words(end, "d MMMM"))"
            : "\(Format.words(start, "d MMM")) – \(Format.words(end, "d MMM"))"
    }
}

// MARK: - The tab

/// Due: what's most pressing in a sentence, and the calendar.
struct DueTab: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        NavigationStack(path: app.stack(.due)) {
            if let api = app.api {
                DuePage(api: api) { app.stack(.due).wrappedValue.append($0) }.laptopNotice().routes()
            }
        }
    }
}

/// Week or month.
enum DueView: Hashable { case week, month }

/// What the library says of Canvas (GET /api/v2/canvas/state): set up or not, whether its last sync failed.
private struct CanvasState: Codable, Sendable {
    var state: String
    var lastSync: String?
    enum CodingKeys: String, CodingKey {
        case state
        case lastSync = "last_sync"
    }
}

private struct DuePage: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var due: Resource<DueList>
    @State private var upcoming: Resource<Upcoming>
    @State private var canvas: CanvasState?
    @State private var syncing = false
    let api: APIClient
    let open: (Route) -> Void

    /// Chosen with Week | Month; nil until then: a phone opens on the week, an iPad on the month (the desktop's).
    @State private var chosenView: DueView?
    /// The month showing (its 1st) and the week showing (its Sunday).
    @State private var month: Date
    @State private var weekStart: Date
    /// The day the month lists under its grid.
    @State private var chosen: Date
    /// The class the calendar is for; nil for every class. It's the app's, so a class page's "See these on the
    /// calendar" (`app.showDue(class:)`) opens Due on that class, and the filter here is the same thing.
    private var only: String? { app.dueFilter }

    init(api: APIClient, open: @escaping (Route) -> Void) {
        self.api = api
        self.open = open
        _due = State(initialValue: Resource("due") { try await api.due() })
        _upcoming = State(initialValue: Resource("upcoming") { try await api.upcoming() })
        let cal = Calendar.current
        let today = cal.startOfDay(for: .now)
        var chosen = today
        var view: DueView?
        #if DEBUG
        if DebugLaunch.flag("-due-week") { view = .week }
        if DebugLaunch.flag("-due-month") { view = .month }
        if let n = DebugLaunch.value("-due-choose").flatMap(Int.init) { chosen = cal.date(byAdding: .day, value: n, to: today) ?? today }
        #endif
        _chosenView = State(initialValue: view)
        _chosen = State(initialValue: chosen)
        _month = State(initialValue: cal.date(from: cal.dateComponents([.year, .month], from: chosen)) ?? chosen)
        _weekStart = State(initialValue: Format.weekStart(chosen))
    }

    private var list: DueList? {
        #if DEBUG
        return DueSample.augment(due.value)
        #else
        return due.value
        #endif
    }

    private var regular: Bool { sizeClass == .regular }
    private var view: DueView { chosenView ?? (regular ? .month : .week) }

    /// Canvas isn't set up: the library says so, or it has never synced and has nothing.
    private var noCanvas: Bool {
        if canvas?.state == "not_set_up" { return true }
        guard let list else { return false }
        return list.synced == nil && list.groups.allSatisfy(\.items.isEmpty)
    }

    var body: some View {
        let book = DueBook(list)
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                if due.stale { OfflineNote().frame(maxWidth: .infinity).padding(.bottom, Space.s4) }
                Text("Due").textStyle(.date).foregroundStyle(.ink).accessibilityAddTraits(.isHeader)
                if list == nil, let error = due.error {
                    EmptyState(title: error.unreachable ? "Can't reach your library" : "Couldn't load what's due", detail: error.detail, actionTitle: "Try again") {
                        Task { await load() }
                    }
                } else if list == nil {
                    StudyLoader(.page, color: .inkSubtle).frame(maxWidth: .infinity, minHeight: 240)
                } else if noCanvas {
                    EmptyState(title: "Nothing to show yet",
                               detail: "Link Canvas in Syft Notes on your computer, in Settings, then Canvas. What's due shows up here.",
                               mark: .oddOneOut)
                } else {
                    lede(book).padding(.top, Space.s2)
                    calendarBar(book)
                        .padding(.top, regular ? Space.s8 : Space.s6)
                        .padding(.bottom, Space.s3)
                    calendar(book)
                }
            }
            .padding(.horizontal, regular ? Space.pageRegular : Space.pageCompact)
            .padding(.top, regular ? Space.s4 : Space.s1)
            .padding(.bottom, Space.s10)
            .frame(maxWidth: regular ? 1160 : .infinity, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
        .accessibilityIdentifier("page.due")
        .foregroundStyle(.ink)
        .page()
        .titleOnScroll("Due", after: 44)
        .syftRefreshable { await load() }
        .task { await load() }
    }

    /// The sentence, then when Canvas last synced (or that it failed, with Try again).
    private func lede(_ book: DueBook) -> some View {
        VStack(alignment: .leading, spacing: Space.s1) {
            Text(book.lede).textStyle(.lede).foregroundStyle(.inkMuted)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 640, alignment: .leading)
            if canvas?.state == "error" {
                HStack(spacing: Space.s3) {
                    Text("Canvas sync failed.").textStyle(.meta).foregroundStyle(.late)
                    Button(syncing ? "Syncing…" : "Try again") { Task { await syncAgain() } }
                        .buttonStyle(.textLink)
                        .disabled(syncing)
                }
            } else if let synced = Format.parse(canvas?.lastSync ?? list?.synced) {
                Text(Format.daysBetween(.now, synced) == 0 ? "Canvas synced at \(Format.time(synced))" : "Canvas synced \(Format.day(synced))")
                    .textStyle(.meta).foregroundStyle(.inkSubtle)
            }
        }
    }

    // MARK: the bar

    /// Back and forward a week (or a month), its name, Today; the class it's for and Week | Month. One row on an iPad,
    /// as the desktop's; two on a phone.
    @ViewBuilder private func calendarBar(_ book: DueBook) -> some View {
        let week = view == .week
        let heading = week ? book.weekHeading(weekStart) : Format.words(month, "MMMM yyyy")
        let steps = HStack(spacing: 2) {
            Button { step(-1) } label: { Label(week ? "Previous week" : "Previous month", icon: "chevron_left") }
                .buttonStyle(.icon)
                .accessibilityLabel(week ? "Previous week" : "Previous month")
            Button { step(1) } label: { Label(week ? "Next week" : "Next month", icon: "chevron_right") }
                .buttonStyle(.icon)
                .accessibilityLabel(week ? "Next week" : "Next month")
        }
        let title = Text(heading).font(.atkinson(20, .heavy, relativeTo: .title3)).tracking(-0.2).foregroundStyle(.ink)
            .lineLimit(1).minimumScaleFactor(0.8)
            .accessibilityAddTraits(.isHeader)
        let today = Button("Today") { goToday() }.buttonStyle(.pill(small: true))
        let views = Segmented(selection: Binding(get: { view }, set: { chosenView = $0 }), options: [(.week, "Week"), (.month, "Month")])
        if regular {
            HStack(spacing: 0) {
                steps
                title.padding(.leading, 10).padding(.trailing, 14).frame(minWidth: 150, alignment: .leading)
                today
                Spacer(minLength: Space.s4)
                filter(book)
                views.padding(.leading, Space.s2)
            }
        } else {
            VStack(alignment: .leading, spacing: Space.s3) {
                HStack(spacing: 0) {
                    steps.padding(.leading, -6)
                    title.padding(.leading, 6)
                    Spacer(minLength: Space.s3)
                    today
                }
                HStack {
                    filter(book)
                    Spacer(minLength: Space.s3)
                    views
                }
            }
        }
    }

    /// "All classes ⌄": every class, or one, each beside its swatch.
    private func filter(_ book: DueBook) -> some View {
        let names = book.classes(order: app.classes.map(\.name))
        return Menu {
            Button { app.dueFilter = nil } label: {
                if only == nil { Label("All classes", icon: "check") } else { Text("All classes") }
            }
            if !names.isEmpty {
                Section {
                    ForEach(names, id: \.self) { name in
                        Button { app.dueFilter = name } label: {
                            Label {
                                Text(name)
                            } icon: {
                                if only == name { Image(icon: "check") } else { DueSwatchImage.image(app.hue(of: name)) }
                            }
                        }
                    }
                }
            }
        } label: {
            PickerLabel(only ?? "All classes")
        }
        .buttonStyle(.picker)
        .accessibilityLabel("Showing \(only ?? "all classes")")
    }

    private func step(_ by: Int) {
        let cal = Calendar.current
        if view == .week {
            weekStart = cal.date(byAdding: .day, value: 7 * by, to: weekStart) ?? weekStart
        } else {
            month = cal.date(byAdding: .month, value: by, to: month) ?? month
            // The listed day follows: today in this month, else the 1st.
            let today = cal.startOfDay(for: .now)
            chosen = cal.isDate(today, equalTo: month, toGranularity: .month) ? today : month
        }
    }

    private func goToday() {
        let cal = Calendar.current
        let today = cal.startOfDay(for: .now)
        month = cal.date(from: cal.dateComponents([.year, .month], from: today)) ?? today
        weekStart = Format.weekStart(today)
        chosen = today
    }

    private func choose(_ d: Date) {
        let cal = Calendar.current
        chosen = d
        if !cal.isDate(d, equalTo: month, toGranularity: .month) {
            month = cal.date(from: cal.dateComponents([.year, .month], from: d)) ?? d
        }
        weekStart = Format.weekStart(d)
    }

    // MARK: the calendar

    @ViewBuilder private func calendar(_ book: DueBook) -> some View {
        let events = upcoming.value?.events ?? []
        let openItem: (DueEntry) -> Void = { e in open(.assignment(class: e.item.class, id: e.item.id)) }
        if view == .week {
            let days = book.week(weekStart, only: only, events: events) { app.className(ofEvent: $0) }
            let nothing = days.allSatisfy { $0.deadlines.isEmpty }
            if nothing {
                EmptyText("Nothing due this week.").padding(.bottom, Space.s4)
            }
            if regular {
                DeskWeek(book: book, days: days, open: openItem, meeting: meeting)
            } else if !(nothing && days.allSatisfy { $0.meetings.isEmpty }) {
                DayBlocks(book: book, days: days, open: openItem, meeting: meeting)
                    .simultaneousGesture(swipe)
            }
        } else if regular {
            DeskMonth(book: book, month: month, only: only, open: openItem)
        } else {
            PhoneMonth(book: book, month: month, chosen: chosen, only: only, choose: choose)
                .simultaneousGesture(swipe)
            DayBlocks(book: book, days: listed(book, events: events), open: openItem, meeting: meeting, emptyWords: true)
                .padding(.top, Space.s4)
        }
    }

    /// The day the month lists under its grid, in the week's blocks; with today chosen, what's late first.
    private func listed(_ book: DueBook, events: [UpcomingEvent]) -> [DueWeekDay] {
        let late = chosen == book.today ? book.late(before: book.today, only: only) : []
        let day = book.block(chosen, only: only, events: events) { app.className(ofEvent: $0) }
        return (late.isEmpty ? [] : [DueWeekDay(date: nil, name: "Late", number: "", isLate: true, deadlines: late)]) + [day]
    }

    /// A class in the week: today's and later ones record it, into its class (as Up next does); one that's over opens
    /// its class.
    private func meeting(_ m: DueMeeting) {
        if m.end > .now {
            app.record(for: m.cls, title: "\(m.title), \(Format.words(m.start, "EEE d MMM"))")
        } else {
            open(.classDetail(m.cls))
        }
    }

    /// A sideways swipe across the calendar goes back or forward a week (a month).
    private var swipe: some Gesture {
        DragGesture(minimumDistance: 24).onEnded { v in
            let dx = v.translation.width, dy = v.translation.height
            guard abs(dx) > 70, abs(dx) > abs(dy) * 1.6 else { return }
            step(dx < 0 ? 1 : -1)
        }
    }

    private func load() async {
        async let a: Void = due.load()
        async let b: Void = upcoming.load()
        async let c: Void = learnClasses()
        async let d: CanvasState? = try? api.get("/canvas/state")
        let (_, _, _, state) = await (a, b, c, d)
        if let state { canvas = state }
    }

    /// "Try again" after a failed sync: ask the library to sync Canvas now, then read what's due again.
    private func syncAgain() async {
        struct Sync: Encodable { var sync = true }
        struct Reply: Decodable {}
        syncing = true
        defer { syncing = false }
        _ = try? await api.post("/canvas", Sync()) as Reply
        try? await Task.sleep(for: .seconds(4))
        await load()
    }

    /// The classes' highlighters come from the library: ask for it when this tab is the first to open.
    private func learnClasses() async {
        guard app.classes.isEmpty, let overview = try? await api.library() else { return }
        app.learn(overview)
        ResponseCache.shared.write("library", overview)
    }
}

// MARK: - The phone's month

/// The month on a phone: a sunken grid, Sunday first, today's cell lifted out of it, the chosen day's number on ink.
/// Each day shows up to two things due as one-line highlights in their classes' colours ("Missing" in red under one
/// that's late, a handed-in one struck through), then "1 more". A tap on a day lists it under the grid.
private struct PhoneMonth: View {
    var book: DueBook
    var month: Date
    var chosen: Date
    var only: String?
    var choose: (Date) -> Void

    var body: some View {
        let days = book.monthDays(month)
        let due = book.byDay(only: only)
        let rows = days.count / 7
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ForEach(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], id: \.self) { d in
                    Text(d).font(.atkinson(12, .bold, relativeTo: .caption)).foregroundStyle(.inkSubtle)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.leading, 6)
                }
            }
            .padding(.top, 9)
            .padding(.bottom, 7)
            .accessibilityHidden(true)
            Rule()
            ForEach(0..<rows, id: \.self) { r in
                let week = Array(days[r * 7..<(r + 1) * 7])
                HStack(spacing: 0) {
                    ForEach(week, id: \.self) { d in
                        PhoneDay(book: book, date: d, entries: due[d] ?? [], inMonth: book.cal.isDate(d, equalTo: month, toGranularity: .month),
                                 chosen: d == chosen) { choose(d) }
                            .zIndex(d == book.today ? 1 : 0)
                    }
                }
                .fixedSize(horizontal: false, vertical: true)
                .zIndex(week.contains(book.today) ? 1 : 0)
                if r < rows - 1 { Rule() }
            }
        }
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line).allowsHitTesting(false))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(Format.words(month, "MMMM yyyy"))
    }
}

private struct PhoneDay: View {
    @Environment(AppModel.self) private var app
    var book: DueBook
    var date: Date
    var entries: [DueEntry]
    var inMonth: Bool
    var chosen: Bool
    var choose: () -> Void
    @ScaledMetric(relativeTo: .caption) private var height: CGFloat = 64

    var body: some View {
        let isToday = date == book.today
        let number = book.cal.component(.day, from: date) == 1 ? Format.words(date, "d MMM") : Format.words(date, "d")
        Button(action: choose) {
            VStack(alignment: .leading, spacing: 4) {
                Text(number)
                    .font(.atkinson(12.5, chosen || isToday ? .heavy : inMonth ? .bold : .medium, relativeTo: .caption))
                    .foregroundStyle(chosen ? Color.surface : isToday ? .ink : inMonth ? .inkMuted : .inkSubtle)
                    .lineLimit(1)
                    .fixedSize()
                    .padding(.horizontal, 5)
                    .padding(.vertical, 1)
                    .background(chosen ? Color.ink : .clear, in: .capsule)
                    .padding(.leading, -5)
                ForEach(entries.prefix(2)) { e in
                    CellHighlight(text: e.item.name, hue: app.hue(of: e.item.class), done: e.done, missing: book.missing(e))
                }
                if entries.count > 2 {
                    Text("\(entries.count - 2) more").font(.atkinson(10.5, .bold, relativeTo: .caption2)).foregroundStyle(.inkMuted)
                        .lineLimit(1).fixedSize()
                }
                Spacer(minLength: 0)
            }
            .padding(.leading, 6)
            .padding(.trailing, 3)
            .padding(.top, 7)
            .padding(.bottom, 6)
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: .infinity, alignment: .topLeading)
            .background {
                if isToday {
                    RoundedRectangle(cornerRadius: Radius.day).fill(.surfaceToday)
                        .lifted(.today, in: RoundedRectangle(cornerRadius: Radius.day))
                        .padding(-3)
                }
            }
            .contentShape(.rect)
        }
        .buttonStyle(DayPress())
        .accessibilityLabel(said)
        .accessibilityHint("Lists it under the calendar")
        .accessibilityAddTraits(chosen ? [.isSelected] : [])
    }

    private var said: String {
        let day = Format.words(date, "EEEE d MMMM")
        let prefix = date == book.today ? "Today, \(day)" : day
        if entries.isEmpty { return "\(prefix), nothing due" }
        return "\(prefix), \(entries.count) due: " + entries.map(book.said).joined(separator: "; ")
    }
}

/// A day pressed: washed, the colour only.
private struct DayPress: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label.overlay {
            if configuration.isPressed { RoundedRectangle(cornerRadius: Radius.control).fill(.press).padding(2).allowsHitTesting(false) }
        }
    }
}

/// Something due in a phone's day cell: its name, one line, small, on its class's highlighter (the shared Highlight's
/// pass, at the cell's size); a handed-in one struck through in its class's colour on no highlight; "Missing" in red
/// under one that's late.
private struct CellHighlight: View {
    var text: String
    var hue: Highlighter?
    var done: Bool
    var missing: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(text)
                .font(.atkinson(11, .bold, relativeTo: .caption2))
                .foregroundStyle(done ? Color.inkSubtle : .ink)
                .strikethrough(done, color: hue?.swatch ?? .inkSubtle)
                .lineLimit(1)
                .truncationMode(.tail)
                .padding(.horizontal, 3)
                .padding(.vertical, 0.5)
                .background {
                    if !done {
                        UnevenRoundedRectangle(topLeadingRadius: 3, bottomLeadingRadius: 4, bottomTrailingRadius: 3, topTrailingRadius: 5)
                            .fill(hue?.highlight ?? Highlighter.none)
                    }
                }
            if missing {
                Text("Missing").font(.atkinson(10, .bold, relativeTo: .caption2)).foregroundStyle(.late)
                    .lineLimit(1).fixedSize()
                    .padding(.leading, 3)
            }
        }
        .accessibilityHidden(true)
    }
}

// MARK: - A day's block (the phone's week, and the day the month lists)

/// The desktop's planner turned on its side: a sunken box with a block a day (Late first, only when something is), each
/// day's classes over a hairline, then what's due; today's block lifted out of it, named "Today".
private struct DayBlocks: View {
    @Environment(AppModel.self) private var app
    var book: DueBook
    var days: [DueWeekDay]
    var open: (DueEntry) -> Void
    var meeting: (DueMeeting) -> Void
    /// Say so on a day with nothing (the month's listed day); the week leaves an empty day empty.
    var emptyWords = false

    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(days.enumerated()), id: \.element.id) { i, d in
                if i > 0 && !d.isToday && !days[i - 1].isToday { Rule().padding(.horizontal, Space.s4) }
                block(d).zIndex(d.isToday ? 1 : 0)
            }
        }
        .padding(.vertical, Space.s1)
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line).allowsHitTesting(false))
    }

    private func block(_ d: DueWeekDay) -> some View {
        HStack(alignment: .top, spacing: Space.s4) {
            DayHead(name: d.name, number: d.isLate ? nil : d.number, late: d.isLate, today: d.isToday)
                .frame(width: 52, alignment: .leading)
            VStack(alignment: .leading, spacing: Space.s3) {
                if !d.meetings.isEmpty {
                    VStack(alignment: .leading, spacing: 2) {
                        ForEach(d.meetings, id: \.self) { m in meetingRow(m) }
                    }
                }
                if !d.meetings.isEmpty && !d.deadlines.isEmpty { Rule() }
                ForEach(d.deadlines) { e in
                    DueRow(book: book, entry: e, inLate: d.isLate) { open(e) }
                }
                if emptyWords && d.meetings.isEmpty && d.deadlines.isEmpty {
                    Text(d.isToday ? "Nothing due today." : "Nothing due.").textStyle(.summary).foregroundStyle(.inkSubtle)
                        .padding(.top, 3)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, Space.s4)
        .padding(.vertical, Space.s3)
        .frame(minHeight: 60, alignment: .top)
        .background {
            if d.isToday {
                RoundedRectangle(cornerRadius: Radius.day).fill(.surfaceToday)
                    .lifted(.today, in: RoundedRectangle(cornerRadius: Radius.day))
                    .padding(.horizontal, -4)
                    .padding(.vertical, -2)
            }
        }
    }

    /// A class that day: its time, then its title in its class's highlighter. A tap records it (or, once it's over,
    /// opens the class).
    private func meetingRow(_ m: DueMeeting) -> some View {
        let over = m.end <= .now
        return Button { meeting(m) } label: {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(Format.words(m.start, "h:mm")).textStyle(.micro).foregroundStyle(.inkSubtle)
                    .frame(minWidth: 34, alignment: .leading)
                Highlight(m.title, hue: app.hue(of: m.cls))
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.vertical, 4)
            .contentShape(.rect)
        }
        .buttonStyle(.row)
        .accessibilityLabel(over ? "\(m.title), \(Format.time(m.start)). Open \(m.cls)"
                                 : "Record \(m.title) as a \(app.noun(of: m.cls)), \(Format.day(m.start).lowercased()) at \(Format.time(m.start))")
    }
}

/// A day's name over its number ("Sun" / "18"), "Today" over today's, or "Late" in red.
private struct DayHead: View {
    var name: String
    var number: String?
    var late = false
    var today = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(name).font(.atkinson(13, .bold, relativeTo: .footnote))
                .foregroundStyle(late ? Color.late : today ? .ink : .inkSubtle)
            if let number {
                Text(number).textStyle(.dayNum).foregroundStyle(today ? Color.ink : .inkMuted).lineLimit(1)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isHeader)
    }
}

/// Something due in a list: its name in its class's highlighter (struck through once handed in), and under it the
/// class and, when there's something to say, when ("Calculus II · Tonight 11:59 PM"; "Missing" and "Was due Mon 5" in
/// the late red). The whole row opens it.
struct DueRow: View {
    @Environment(AppModel.self) private var app
    var book: DueBook
    var entry: DueEntry
    var inLate = false
    var action: () -> Void

    var body: some View {
        let when = book.when(entry, inLate: inLate)
        Button(action: action) {
            VStack(alignment: .leading, spacing: 3) {
                Highlight(entry.item.name, hue: app.hue(of: entry.item.class), style: .due, struck: entry.done)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                (Text(verbatim: entry.item.class).foregroundStyle(.inkSubtle)
                    + Text(verbatim: when.text.isEmpty ? "" : " · ").foregroundStyle(.inkSubtle)
                    + Text(verbatim: when.text).foregroundStyle(when.late ? Color.late : .inkSubtle).fontWeight(when.late ? .bold : .medium))
                    .textStyle(.micro)
                    .padding(.leading, 2)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 2)
            .contentShape(.rect)
        }
        .buttonStyle(.row)
        .accessibilityLabel(book.said(entry))
        .accessibilityHint("Opens the assignment")
    }
}

// MARK: - The iPad's month and week (the desktop's)

/// The desktop's month: a sunken grid, Sunday first, hairlines between the days, today's cell lifted out and labelled
/// "Today"; each thing due one line in its class's highlighter with "Missing" or a time under it, handed-in work
/// struck through; past two on a day, the rest behind "1 more".
private struct DeskMonth: View {
    var book: DueBook
    var month: Date
    var only: String?
    var open: (DueEntry) -> Void

    var body: some View {
        let days = book.monthDays(month)
        let due = book.byDay(only: only)
        let rows = days.count / 7
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ForEach(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], id: \.self) { d in
                    Text(d).font(.atkinson(13, .bold, relativeTo: .footnote)).foregroundStyle(.inkSubtle)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 10).padding(.top, 10).padding(.bottom, 8)
                }
            }
            .accessibilityHidden(true)
            Rule()
            ForEach(0..<rows, id: \.self) { r in
                let week = Array(days[r * 7..<(r + 1) * 7])
                HStack(spacing: 0) {
                    ForEach(Array(week.enumerated()), id: \.element) { c, d in
                        DeskDay(book: book, date: d, entries: due[d] ?? [], inMonth: book.cal.isDate(d, equalTo: month, toGranularity: .month), open: open)
                            .overlay(alignment: .trailing) { if c < 6 { Rectangle().fill(.line).frame(width: 1) } }
                            .zIndex(d == book.today ? 1 : 0)
                    }
                }
                .fixedSize(horizontal: false, vertical: true)
                .zIndex(week.contains(book.today) ? 1 : 0)
                if r < rows - 1 { Rule() }
            }
        }
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line).allowsHitTesting(false))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(Format.words(month, "MMMM yyyy"))
    }
}

private struct DeskDay: View {
    @Environment(AppModel.self) private var app
    var book: DueBook
    var date: Date
    var entries: [DueEntry]
    var inMonth: Bool
    var open: (DueEntry) -> Void
    @State private var showingMore = false

    var body: some View {
        let isToday = date == book.today
        let number = book.cal.component(.day, from: date) == 1 ? Format.words(date, "d MMM") : Format.words(date, "d")
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(number).font(.atkinson(13, inMonth ? .bold : .medium, relativeTo: .footnote))
                    .foregroundStyle(isToday ? Color.ink : inMonth ? .inkMuted : .inkSubtle)
                Spacer(minLength: 4)
                if isToday { Text("Today").font(.atkinson(12, .bold, relativeTo: .caption)).foregroundStyle(.ink) }
            }
            .accessibilityElement(children: .combine)
            ForEach(entries.prefix(2)) { e in item(e) }
            if entries.count > 2 {
                Button("\(entries.count - 2) more") { showingMore = true }
                    .buttonStyle(.textLink)
                    .popover(isPresented: $showingMore, arrowEdge: .bottom) {
                        VStack(alignment: .leading, spacing: 10) {
                            ForEach(entries.dropFirst(2)) { e in item(e) { showingMore = false } }
                        }
                        .padding(14)
                        .frame(width: 260, alignment: .leading)
                        .presentationCompactAdaptation(.popover)
                        .presentationBackground(.surface)
                    }
            }
        }
        .padding(.horizontal, isToday ? 12 : 9)
        .padding(.vertical, isToday ? 11 : 8)
        .frame(maxWidth: .infinity, minHeight: 112, maxHeight: .infinity, alignment: .topLeading)
        .background {
            if isToday {
                RoundedRectangle(cornerRadius: Radius.day).fill(.surfaceToday)
                    .lifted(.today, in: RoundedRectangle(cornerRadius: Radius.day))
            }
        }
        .padding(isToday ? -3 : 0)
        .onChange(of: app.dismissal) { showingMore = false }
    }

    private func item(_ e: DueEntry, then: (() -> Void)? = nil) -> some View {
        let sub = book.calendarSub(e)
        let missing = book.missing(e)
        return Button {
            then?()
            open(e)
        } label: {
            VStack(alignment: .leading, spacing: 1) {
                Highlight(e.item.name, hue: app.hue(of: e.item.class), style: .due, struck: e.done)
                    .lineLimit(1)
                    .truncationMode(.tail)
                if !sub.isEmpty {
                    Text(sub).font(.atkinson(11.5, missing ? .bold : .medium, relativeTo: .caption2))
                        .foregroundStyle(missing ? Color.late : .inkSubtle)
                        .lineLimit(1)
                        .padding(.leading, 2)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(book.said(e))
    }
}

/// The desktop's week: Late (only when something is), then Sunday to Saturday side by side, today's column lifted out;
/// each day its classes over a hairline, then what's due, the name in its class's highlighter with when under it. A
/// weekday is a share of 1, Saturday and Sunday 0.8, Late 1.15.
private struct DeskWeek: View {
    @Environment(AppModel.self) private var app
    var book: DueBook
    var days: [DueWeekDay]
    var open: (DueEntry) -> Void
    var meeting: (DueMeeting) -> Void

    var body: some View {
        WeightedRow(weights: days.map { d in
            d.isLate ? 1.15 : d.date.map { [1, 7].contains(book.cal.component(.weekday, from: $0)) } == true ? 0.8 : 1
        }) {
            ForEach(Array(days.enumerated()), id: \.element.id) { i, d in
                column(d)
                    .overlay(alignment: .leading) {
                        if i > 0 && !d.isToday && !days[i - 1].isToday { Rectangle().fill(.line).frame(width: 1) }
                    }
                    .zIndex(d.isToday ? 1 : 0)
            }
        }
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line).allowsHitTesting(false))
    }

    private func column(_ d: DueWeekDay) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .lastTextBaseline) {
                Text(d.name).font(.atkinson(13, .bold, relativeTo: .footnote))
                    .foregroundStyle(d.isLate ? Color.late : d.isToday ? .ink : .inkSubtle)
                Spacer(minLength: 4)
                if !d.isLate { Text(d.number).textStyle(.dayNum).foregroundStyle(d.isToday ? Color.ink : .inkMuted) }
            }
            .frame(minHeight: 26)
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(.isHeader)
            if !d.meetings.isEmpty {
                VStack(alignment: .leading, spacing: 8) {
                    ForEach(d.meetings, id: \.self) { m in
                        Button { meeting(m) } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(Format.words(m.start, "h:mm")).textStyle(.micro).foregroundStyle(.inkSubtle)
                                Highlight(m.title, hue: app.hue(of: m.cls)).fixedSize(horizontal: false, vertical: true)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(m.end > .now ? "Record \(m.title), \(Format.time(m.start))" : "\(m.title), \(Format.time(m.start)). Open \(m.cls)")
                    }
                }
            }
            if !d.meetings.isEmpty && !d.deadlines.isEmpty { Rule() }
            VStack(alignment: .leading, spacing: 12) {
                ForEach(d.deadlines) { e in
                    let when = book.when(e, inLate: d.isLate)
                    Button { open(e) } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Highlight(e.item.name, hue: app.hue(of: e.item.class), style: .due, struck: e.done)
                                .fixedSize(horizontal: false, vertical: true)
                            if !when.text.isEmpty {
                                Text(when.text).font(.atkinson(12.5, when.late ? .bold : .medium, relativeTo: .caption))
                                    .foregroundStyle(when.late ? Color.late : .inkSubtle)
                                    .padding(.leading, 2)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(book.said(e))
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, minHeight: 260, maxHeight: .infinity, alignment: .topLeading)
        .background {
            if d.isToday {
                RoundedRectangle(cornerRadius: Radius.day).fill(.surfaceToday)
                    .lifted(.today, in: RoundedRectangle(cornerRadius: Radius.day))
                    .padding(.vertical, -4)
            }
        }
    }
}

/// Columns side by side, each its share of the width, all as tall as the tallest.
private struct WeightedRow: Layout {
    var weights: [CGFloat]

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 900
        let widths = shares(width, subviews.count)
        let height = zip(subviews, widths).map { $0.sizeThatFits(ProposedViewSize(width: $1, height: nil)).height }.max() ?? 0
        return CGSize(width: width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        for (s, w) in zip(subviews, shares(bounds.width, subviews.count)) {
            s.place(at: CGPoint(x: x, y: bounds.minY), proposal: ProposedViewSize(width: w, height: bounds.height))
            x += w
        }
    }

    private func shares(_ width: CGFloat, _ n: Int) -> [CGFloat] {
        let w = (0..<n).map { $0 < weights.count ? weights[$0] : 1 }
        let total = w.reduce(0, +)
        return w.map { width * $0 / max(total, 1) }
    }
}

/// A class's swatch as a picture, for a menu (the system draws a menu's rows; it takes an image, kept in its colours).
@MainActor
private enum DueSwatchImage {
    static func image(_ hue: Highlighter?) -> Image {
        let size = CGSize(width: 18, height: 14)
        let colour = hue?.swatchUI ?? UIColor(hex: "#808080")
        let picture = UIGraphicsImageRenderer(size: size).image { _ in
            let ctx = UIGraphicsGetCurrentContext()
            ctx?.translateBy(x: size.width / 2, y: size.height / 2)
            ctx?.rotate(by: -8 * .pi / 180)
            colour.setFill()
            UIBezierPath(roundedRect: CGRect(x: -7, y: -5, width: 14, height: 10), cornerRadius: 3.5).fill()
        }
        return Image(uiImage: picture.withRenderingMode(.alwaysOriginal))
    }
}

// MARK: - An assignment

/// One assignment's page, everything the library knows (GET /api/v2/canvas/assignment): the shared model's fields and
/// the ones it leaves out (whether and when it was handed in, its grade, when it closes, how it's handed in).
struct AssignmentPage: Codable, Sendable {
    struct Criterion: Codable, Sendable, Hashable {
        struct Mark: Codable, Sendable, Hashable {
            var points: Double?
            var comment: String?
        }
        var criterion: String
        var points: Double?
        var description: String?
        var mark: Mark?
    }
    struct Comment: Codable, Sendable, Hashable {
        var author: String
        var text: String
        var at: String?
    }
    struct Submission: Codable, Sendable, Hashable {
        var state: String?
        var attempt: Int?
        var submittedAt: String?
        var gradedAt: String?
        var score: Double?
        var grade: String?
        var late: Bool?

        enum CodingKeys: String, CodingKey {
            case state, attempt, score, grade, late
            case submittedAt = "submitted_at"
            case gradedAt = "graded_at"
        }
    }
    var `class`: String
    var id: Int
    var name: String
    var kind: String
    var due: String?
    var points: Double?
    var status: String?
    var label: String
    var scoreText: String
    var score: Double?
    var grade: String?
    var gradingType: String?
    var late: Bool
    var missing: Bool
    var excused: Bool?
    var submitted: String?
    var gradedAt: String?
    var url: String
    var instructions: String?
    var instructionsHTML: String?
    var rubric: [Criterion]?
    var comments: [Comment]?
    var submission: Submission?
    var submissionTypes: [String]?
    var lockAt: String?

    enum CodingKeys: String, CodingKey {
        case `class`, id, name, kind, due, points, status, label, score, grade, late, missing, excused, submitted, url, instructions, rubric, comments, submission
        case scoreText = "score_text"
        case gradingType = "grading_type"
        case gradedAt = "graded_at"
        case instructionsHTML = "instructions_html"
        case submissionTypes = "submission_types"
        case lockAt = "lock_at"
    }
}

/// The assignment page's words, as the desktop's AssignmentModel and CanvasWords say them.
enum AssignmentWords {
    /// "Sun 18 Oct, 11:59 PM" (with its year once it isn't this one).
    static func full(_ d: Date, now: Date = .now) -> String {
        Format.words(d, Format.calendar.isDate(d, equalTo: now, toGranularity: .year) ? "EEE d MMM, h:mm a" : "EEE d MMM yyyy, h:mm a")
    }

    /// "Due today", "Due tomorrow", "Due in 5 days", "Was due yesterday", "Was due 3 days ago".
    static func relative(_ d: Date, now: Date = .now) -> String {
        let days = Format.daysBetween(now, d)
        return switch days {
        case 0: "Due today"
        case 1: "Due tomorrow"
        case 2...: "Due in \(days) days"
        case -1: "Was due yesterday"
        default: "Was due \(-days) days ago"
        }
    }

    /// "20", "7.5".
    static func points(_ p: Double?) -> String {
        guard let p else { return "" }
        return p == p.rounded() ? String(Int(p)) : String(format: "%g", p)
    }

    /// "Assignment", "Quiz", "Discussion", "To-do".
    static func kind(_ k: String) -> String {
        switch k {
        case "quiz": "Quiz"
        case "discussion": "Discussion"
        case "todo": "To-do"
        default: "Assignment"
        }
    }

    /// What was handed in, if anything: Canvas keeps an "unsubmitted" submission for every assignment, so only one with
    /// a time, a grade or a state past that is the student's.
    static func handedIn(_ a: AssignmentPage) -> (at: Date?, graded: Bool, late: Bool)? {
        if let s = a.submission, s.submittedAt != nil || s.gradedAt != nil || (s.state.map { !$0.isEmpty && $0 != "unsubmitted" } ?? false) {
            return (Format.parse(s.submittedAt), s.gradedAt != nil, s.late ?? a.late)
        }
        if let s = a.submitted, !s.isEmpty { return (Format.parse(s), a.gradedAt != nil, a.late) }
        if ["submitted", "graded", "pending_review"].contains(a.status ?? "") { return (nil, a.status == "graded", a.late) }
        return nil
    }

    /// Its score, as Canvas grades it: "9 / 10", or the letter or pass/fail.
    static func score(_ a: AssignmentPage) -> String {
        if ["letter", "pass_fail"].contains(a.gradingType ?? ""), let g = a.grade, !g.isEmpty { return g }
        if !a.scoreText.isEmpty { return a.scoreText }
        return a.score.map { "\(points($0)) / \(points(a.points))" } ?? ""
    }
}

/// One assignment: its class (the highlight, which opens the class), its name, when it's due, its points and state in
/// a line, whether it's handed in, Open in Canvas (and Hand it in on Canvas while it's open), the instructions with
/// their maths, the rubric, the grader's comments, the lectures it's about, and Ask how to start it.
struct AssignmentView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.openURL) private var openURL
    let className: String
    let id: Int
    @State private var detail: Resource<AssignmentPage>?
    @State private var lectures: [LectureSummary] = []
    @State private var asking = false

    var body: some View {
        ScrollView {
            if let detail {
                Loaded(resource: detail, failure: "Couldn't load this assignment") { a in content(a) }
                    .padding(.horizontal, sizeClass == .regular ? Space.pageRegular : Space.pageCompact)
            }
        }
        .accessibilityIdentifier("page.assignment")
        .foregroundStyle(.ink)
        .page()
        .titleOnScroll(detail?.value?.name ?? "", after: 110)
        // Ask how to start this sends its question as it opens; a source opens the lecture at its moment on this tab.
        .sheet(isPresented: $asking) {
            AskSheet(scope: .class(className),
                     question: detail?.value.map { "How should I start \($0.name)? Use what was said in my lectures." })
        }
        .onChange(of: app.dismissal) { asking = false }
        // The Record bar records into this assignment's class.
        .recordsInto(className)
        .syftRefreshable { await load() }
        .task { await load() }
    }

    private func load() async {
        guard let api = app.api else { return }
        let cls = className, id = id
        if detail == nil {
            detail = Resource("assignment-page:\(cls):\(id)") {
                #if DEBUG
                if let sample = DueSample.page(class: cls, id: id) { return sample }
                #endif
                return try await api.get("/canvas/assignment", query: ["class": cls, "id": String(id)])
            }
        }
        await detail?.load()
        // From your lectures: what the library finds for its name in its class, lectures only.
        if let name = detail?.value?.name, let found = try? await api.search(name, class: cls, limit: 6) {
            lectures = Array(found.lectures.filter { $0.class == nil || $0.class == cls }.prefix(3))
        }
    }

    private func content(_ a: AssignmentPage) -> some View {
        let handed = AssignmentWords.handedIn(a)
        let due = Format.parse(a.due)
        let excused = a.excused ?? false
        let closed = handed == nil && !excused && (Format.parse(a.lockAt).map { $0 < .now } ?? false)
        let late = handed == nil && !excused && (a.missing || ["missing", "overdue", "past due"].contains(a.status ?? "")
                                                    || (due.map { Format.daysBetween(.now, $0) < 0 } ?? false))
        let takesNothing = !(a.submissionTypes ?? []).isEmpty && (a.submissionTypes ?? []).allSatisfy { ["none", "on_paper", "not_graded"].contains($0) }
        let todo = a.kind == "todo"
        let canHandIn = handed == nil && !excused && !takesNothing && !closed && !todo
        return VStack(alignment: .leading, spacing: 0) {
            // Where it's from: the class (a tap opens it) and what it is.
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                NavigationLink(value: Route.classDetail(a.class)) {
                    Highlight(a.class, hue: app.hue(of: a.class))
                }
                .buttonStyle(.plain)
                .accessibilityLabel("\(a.class). Open the class")
                Text(AssignmentWords.kind(a.kind)).textStyle(.crumb).foregroundStyle(.inkMuted)
            }
            .padding(.top, Space.s2)

            Text(a.name).textStyle(.docTitle).foregroundStyle(.ink)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, Space.s3)
                .accessibilityAddTraits(.isHeader)

            facts(a, due: due, handed: handed, closed: closed, late: late, todo: todo)
                .padding(.top, Space.s3)
            if let words = submissionWords(a, due: due, handed: handed, closed: closed, todo: todo) {
                Text(words.text).textStyle(.meta).foregroundStyle(words.late ? Color.late : .inkSubtle)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, Space.s1)
            }

            HStack(spacing: Space.s2) {
                if let url = URL(string: a.url) {
                    Button { openURL(url) } label: { Label("Open in Canvas", icon: "open_in_new") }
                        .buttonStyle(.quiet)
                    if canHandIn {
                        Button { openURL(url) } label: { Label("Hand it in on Canvas", icon: "arrow_outward") }
                            .buttonStyle(.quiet)
                    }
                }
            }
            .padding(.top, Space.s5)

            if todo {
                Text("A to-do from your Canvas planner. There's nothing to hand in: open it in Canvas to read it, and mark it done there.")
                    .textStyle(.summary).foregroundStyle(.inkMuted)
                    .padding(.top, Space.s6)
            }

            if let html = a.instructionsHTML, !html.isEmpty {
                heading("Instructions")
                NotesView(html: html, accent: .ink)
            } else if let md = a.instructions, !md.isEmpty {
                heading("Instructions")
                MarkdownText(md).textStyle(.body)
            }

            if let rubric = a.rubric, !rubric.isEmpty {
                heading("Rubric")
                Panel(padding: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(rubric.enumerated()), id: \.offset) { i, r in
                            if i > 0 { Rule() }
                            HStack(alignment: .firstTextBaseline, spacing: Space.s4) {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(r.criterion).textStyle(.buttonSmall).foregroundStyle(.ink)
                                    if let d = r.description, !d.isEmpty { Text(d).textStyle(.summary).foregroundStyle(.inkMuted) }
                                    if let c = r.mark?.comment, !c.isEmpty { Text("“\(c)”").textStyle(.summary).foregroundStyle(.inkMuted) }
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                                Text(r.mark.map { "\(AssignmentWords.points($0.points)) / \(AssignmentWords.points(r.points))" } ?? "\(AssignmentWords.points(r.points)) pts")
                                    .textStyle(.meta).foregroundStyle(.inkMuted)
                            }
                            .padding(.horizontal, Space.s4)
                            .padding(.vertical, Space.s3)
                        }
                    }
                }
            }

            if let comments = a.comments, !comments.isEmpty {
                heading("Comments")
                VStack(alignment: .leading, spacing: Space.s4) {
                    ForEach(Array(comments.enumerated()), id: \.offset) { _, c in
                        VStack(alignment: .leading, spacing: 2) {
                            Text("“\(c.text)”").textStyle(.body).foregroundStyle(.ink)
                            Text([c.author, Format.parse(c.at).map { Format.day($0) }].compactMap { $0 }.joined(separator: ", "))
                                .textStyle(.micro).foregroundStyle(.inkSubtle)
                        }
                    }
                }
            }

            if !lectures.isEmpty {
                heading("From your lectures")
                VStack(spacing: 0) {
                    ForEach(Array(lectures.enumerated()), id: \.element.id) { i, l in
                        NavigationLink(value: Route.lecture(l.id)) { NoteRow(lecture: l, showFolder: false, first: i == 0) }
                            .buttonStyle(.plain)
                    }
                }
            }

            AskBarButton(placeholder: "Ask how to start this", scope: a.class) { asking = true }
                .padding(.top, Space.s10)
        }
        .padding(.bottom, Space.s10)
        .frame(maxWidth: 680, alignment: .leading)
        .frame(maxWidth: .infinity)
    }

    private func heading(_ title: String) -> some View {
        Text(title).textStyle(.h2).foregroundStyle(.ink)
            .accessibilityAddTraits(.isHeader)
            .padding(.top, Space.s8)
            .padding(.bottom, Space.s3)
    }

    /// "Due Sun 18 Oct, 11:59 PM · 20 points · To do": muted, "Missing" in the late red, the score once graded.
    private func facts(_ a: AssignmentPage, due: Date?, handed: (at: Date?, graded: Bool, late: Bool)?, closed: Bool, late: Bool, todo: Bool) -> some View {
        let graded = a.status == "graded" || a.gradedAt != nil || handed?.graded == true
        let score = AssignmentWords.score(a)
        let state = (a.excused ?? false) ? "Excused"
            : graded && !score.isEmpty ? "Graded \(score)"
            : closed ? "Closed"
            : handed != nil ? (handed?.late == true ? "Submitted late" : "Submitted")
            : late ? "Missing"
            : a.label
        var parts = [due.map { "Due \(AssignmentWords.full($0))" } ?? (todo ? "No date" : "No due date")]
        if !todo, let p = a.points { parts.append("\(AssignmentWords.points(p)) point\(p == 1 ? "" : "s")") }
        let lead = parts.joined(separator: " · ")
        let red = state == "Missing"
        return (Text(verbatim: lead).foregroundStyle(.inkMuted)
            + Text(verbatim: todo || state.isEmpty ? "" : " · ").foregroundStyle(.inkMuted)
            + Text(verbatim: todo ? "" : state).foregroundStyle(red ? Color.late : .inkMuted).fontWeight(red ? .bold : .medium))
            .textStyle(.crumb)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// Whether it's handed in, in a line: "Handed in Tue 13 Oct, 8:00 PM", "Nothing handed in yet. Due in 2 days.",
    /// "Not handed in. Closed Sun 18 Oct, 11:59 PM."
    private func submissionWords(_ a: AssignmentPage, due: Date?, handed: (at: Date?, graded: Bool, late: Bool)?, closed: Bool, todo: Bool) -> (text: String, late: Bool)? {
        if todo || (a.excused ?? false) { return nil }
        if let handed {
            return handed.at.map { ("Handed in \(AssignmentWords.full($0))\(handed.late ? ", late" : "").", false) } ?? ("Handed in.", false)
        }
        if closed { return ("Not handed in. Closed \(Format.parse(a.lockAt).map { AssignmentWords.full($0) } ?? "").", true) }
        guard let due else { return ("Nothing handed in yet.", false) }
        let relative = AssignmentWords.relative(due)
        return ("Nothing handed in yet. \(relative).", relative.hasPrefix("Was due"))
    }
}

// MARK: - Pictures (debug builds)

#if DEBUG
/// `-due-sample`: the demo library's three assignments, and a few more round them so every state shows in pictures:
/// one due tonight, one missing, two handed in, and a day with three. (FLOWS asks the demo library for one due tonight
/// and one missing; until it has them, these live here, in debug builds only.)
enum DueSample {
    static var on: Bool { DebugLaunch.flag("-due-sample") }

    private static func at(_ days: Int, _ hour: Int, _ minute: Int = 0) -> String {
        let d = Calendar.current.date(byAdding: .day, value: days, to: Calendar.current.startOfDay(for: .now))!
        let t = Calendar.current.date(bySettingHour: hour, minute: minute, second: 0, of: d)!
        return Format.words(t, "yyyy-MM-dd'T'HH:mm")
    }

    private static func item(_ id: Int, _ name: String, _ cls: String, _ due: String, status: String = "open", label: String = "To do",
                             missing: Bool = false, submitted: String? = nil, kind: String = "assignment") -> DueItem {
        DueItem(class: cls, id: id, name: name, kind: kind, due: due, points: 10, status: status, label: label, scoreText: "",
                late: false, missing: missing, excused: false, submitted: submitted, url: "https://canvas.example.edu")
    }

    static func augment(_ list: DueList?) -> DueList? {
        guard on, var list else { return list }
        let tonight = item(903, "Pre-lab quiz 7", "Organic Chemistry", at(0, 23, 59), kind: "quiz")
        let missing = item(902, "Reading response 4", "Intro to Psychology", at(-3, 23, 59), status: "missing", label: "Missing", missing: true)
        let morning = item(904, "Chapter 9 reading quiz", "Intro to Psychology", at(4, 10), kind: "quiz")
        let set7 = item(905, "Problem Set 7: Trig Substitution", "Calculus II", at(4, 23, 59))
        let quiz5 = item(901, "Quiz 5: Substitution", "Calculus II", at(-2, 9), status: "submitted", label: "Submitted", submitted: at(-3, 20))
        let forum = item(906, "Discussion: false memories", "Intro to Psychology", at(-6, 23, 59), status: "graded", label: "Graded", submitted: at(-7, 18))
        list.groups.insert(DueList.Group(key: "overdue", label: "Overdue", items: [missing]), at: 0)
        if let w = list.groups.firstIndex(where: { $0.key == "week" }) {
            list.groups[w].items.insert(tonight, at: 0)
            list.groups[w].items.append(contentsOf: [morning, set7])
        }
        list.groups.append(DueList.Group(key: "handed_in", label: "Handed in", items: [quiz5, forum]))
        list.toHandIn += 4
        return list
    }

    /// The sample assignments' pages: Quiz 5 handed in and graded, Reading response 4 missing.
    static func page(class cls: String, id: Int) -> AssignmentPage? {
        guard on, id > 900 else { return nil }
        let missing = id == 902
        return AssignmentPage(
            class: cls, id: id, name: missing ? "Reading response 4" : "Quiz 5: Substitution", kind: missing ? "assignment" : "quiz",
            due: missing ? at(-3, 23, 59) : at(-2, 9), points: 10, status: missing ? "missing" : "graded", label: missing ? "Missing" : "Graded",
            scoreText: missing ? "" : "9 / 10", score: missing ? nil : 9, grade: nil, gradingType: "points", late: false, missing: missing,
            excused: false, submitted: missing ? nil : at(-3, 20), gradedAt: missing ? nil : at(-1, 10), url: "https://canvas.example.edu",
            instructions: nil,
            instructionsHTML: missing
                ? "<p>Read chapter 7 and write 300 words: which of Loftus and Palmer's findings would change how a court treats an eyewitness?</p>"
                : "<p>Ten questions on <em>u</em>-substitution, 20 minutes. Show the substitution you chose for each, e.g. <span class=\"math\" data-tex=\"u = x^2 + 1\">u = x² + 1</span>.</p>",
            rubric: missing ? [.init(criterion: "Uses the reading", points: 6), .init(criterion: "Clear argument", points: 4)] : [],
            comments: missing ? [] : [.init(author: "Dr. Alvarez", text: "Good work. Watch the constant on question 8.", at: at(-1, 10))],
            submission: missing ? nil : .init(state: "graded", attempt: 1, submittedAt: at(-3, 20), gradedAt: at(-1, 10), score: 9, grade: "9", late: false),
            submissionTypes: ["online_upload"], lockAt: nil)
    }
}
#endif
