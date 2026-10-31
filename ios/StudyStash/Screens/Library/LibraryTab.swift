import SwiftUI

/// Home: the first tab, and the first screen every time the app opens. The desktop's Home (Views/Overview.axaml) and
/// its sidebar on a phone, as FLOWS.md's "library" section lays it out: today's date as the title, a sentence or two
/// with the most pressing thing first, what's still on this phone, Up next (each row records its event), what's due
/// this week, the folders as the sidebar groups them (swatch, name, count), and every note, newest first, its folder
/// a highlight that moves it. + attaches; Settings sits beside it. Record is the bar above the tabs, not here.
struct LibraryTab: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        NavigationStack(path: app.stack(.home)) {
            if let api = app.api {
                LibraryHome(api: api, path: app.stack(.home)).laptopNotice().routes()
            }
        }
    }
}

private struct LibraryHome: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    let api: APIClient
    @Binding var path: NavigationPath

    @State private var library: Resource<LibraryOverview>
    /// The newest notes, every folder's; `older` holds the pages after it.
    @State private var notes: Resource<[LectureSummary]>
    @State private var unsorted: Resource<[LectureSummary]>
    @State private var due: Resource<DueList>
    @State private var upcoming: Resource<Upcoming>
    @State private var older: [LectureSummary] = []
    @State private var allLoaded = false
    @State private var loadingMore = false
    /// The filter: every note, or only the unsorted ones (a folder picked in it opens that folder's page instead).
    @State private var onlyUnsorted = DebugLaunch.value("-filter") == "unsorted"
    /// Notes moved from here, by id, until the library's lists say so themselves.
    @State private var moved: [String: String] = [:]
    /// The note whose folder is being picked.
    @State private var moving: LectureSummary?
    @State private var shooting: PhotoTarget?
    @State private var toast: HomeToast?
    @State private var askingUses = false
    /// Answered (or put off) here: the row goes at once.
    @State private var usesDone = false
    @State private var recordings = RecordingStore.shared

    /// Notes a page at a time, as the desktop loads them.
    static let pageSize = 20

    init(api: APIClient, path: Binding<NavigationPath>) {
        self.api = api
        _path = path
        _library = State(initialValue: Resource("library") { try await api.library() })
        _notes = State(initialValue: Resource("home-notes") { try await api.lectures(limit: LibraryHome.pageSize) })
        _unsorted = State(initialValue: Resource("home-unsorted") { try await api.lectures(class: "Unsorted", limit: 200) })
        _due = State(initialValue: Resource("due") { try await api.due() })
        _upcoming = State(initialValue: Resource("upcoming") { try await api.upcoming() })
        #if DEBUG
        if let id = DebugLaunch.value("-move") {
            _moving = State(initialValue: ResponseCache.shared.read("home-notes", as: [LectureSummary].self)?.first { $0.id == id })
        }
        #endif
    }

    private var regular: Bool { sizeClass == .regular }
    private var gap: CGFloat { regular ? 40 : 32 }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                TimelineView(.everyMinute) { tick in
                    LazyVStack(alignment: .leading, spacing: 0) {
                        page(now: tick.date)
                    }
                }
                .padding(.horizontal, regular ? Space.pageRegular : Space.pageCompact)
                .padding(.top, 4)
                .padding(.bottom, 40)
                .frame(maxWidth: 1040)
                .frame(maxWidth: .infinity)
            }
            .accessibilityIdentifier("page.home")
            .task {
                #if DEBUG
                if let to = DebugLaunch.value("-scrolled") ?? (DebugLaunch.flag("-scrolled") ? "notes" : nil) {
                    try? await Task.sleep(for: .milliseconds(700))
                    proxy.scrollTo(["folders", "due", "notes"].contains(to) ? to : "notes", anchor: .top)
                }
                #endif
            }
        }
        .foregroundStyle(.ink)
        .page()
        .titleOnScroll(Format.longDate(), after: 60)
        .toolbar {
            ToolbarItemGroup(placement: .topBarTrailing) {
                // One Attach: the sheet that asks which class, then scans, photos, files, writing, a recording.
                Button { app.incoming = .attach(className: nil, lecture: nil) } label: { Label("Attach", icon: "add") }
                    .accessibilityLabel("Attach")
                // Settings, in the page's corner as the desktop has it (it's no longer a tab).
                NavigationLink(value: Route.settings) { Label("Settings", icon: "settings") }
                    .accessibilityLabel("Settings")
            }
        }
        .overlay(alignment: .bottom) { toastView }
        .animation(.smooth, value: toast)
        .syftRefreshable { await loadAll() }
        .task { await loadAll() }
        // While the laptop records, its banner appears (and goes) within one read of the library's copy.
        .task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(10))
                await library.load()
            }
        }
        .fullScreenCover(item: $shooting) { BoardCamera(target: $0) }
        .onChange(of: library.value?.classes) { _, _ in if let v = library.value { app.learn(v) } }
        .onChange(of: onlyUnsorted) { _, only in if only { Task { await unsorted.load() } } }
        .sheet(item: $moving) { l in
            MoveToPicker(current: folder(of: l), classes: library.value?.classes ?? app.classes, unsorted: library.value?.unsorted ?? 0) { to in
                Task { await move(l, to: to) }
            }
        }
        .sheet(isPresented: $askingUses, onDismiss: { app.askedUses = true; usesDone = true }) { UsesView(onboarding: true) }
        // Going somewhere from outside (a notification, a widget) closes what this page has open.
        .onChange(of: app.dismissal) { _, _ in moving = nil; askingUses = false }
    }

    // MARK: the page

    @ViewBuilder private func page(now: Date) -> some View {
        if app.isDemo { demoBanner.padding(.bottom, 20) }
        header(now: now)
        if let n = library.value?.recordingNow {
            RecordingNowBanner(info: n, now: now) { shooting = RecordingNowBanner.target(n) }.padding(.top, 20)
        }
        if app.needsUses && !usesDone { usesRow.padding(.top, 20) }
        if stale { HomeOffline().padding(.top, 20) }
        OnThisPhone(gap: gap)
        if regular {
            // An iPad: Up next and what's due side by side, as the desktop's two columns.
            HStack(alignment: .top, spacing: 48) {
                upNext(now: now).frame(maxWidth: .infinity, alignment: .leading)
                dueThisWeek(now: now).frame(maxWidth: .infinity, alignment: .leading)
            }
        } else {
            upNext(now: now)
            dueThisWeek(now: now).id("due")
            folders.id("folders")
        }
        NotebookShelf(gap: gap)
        notesSection.padding(.top, gap).id("notes")
    }

    private var stale: Bool { library.stale || notes.stale || upcoming.stale }

    // MARK: the date and the sentence

    private func header(now: Date) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(Format.longDate(now)).textStyle(.date).foregroundStyle(.ink)
                .accessibilityAddTraits(.isHeader)
                .fixedSize(horizontal: false, vertical: true)
            let lede = HomeWords.lede(
                events: upcoming.value?.events, due: canvas ? due.value : nil, overview: library.value,
                unsent: recordings.unsent.filter { $0.state != .recording && $0.state != .sending }.count,
                className: { app.className(ofEvent: $0) }, noun: { app.noun(of: $0) }, words: app.words, now: now)
            if !lede.isEmpty {
                Text(lede).textStyle(.lede).foregroundStyle(.inkMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 600, alignment: .leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// Canvas is linked: the Due list has synced (or has something in it) and the library is for school.
    private var canvas: Bool {
        guard app.forSchool, let d = due.value else { return false }
        return d.synced != nil || d.groups.contains { !$0.items.isEmpty }
    }

    /// The demo library: a slim line saying so, and the way out.
    private var demoBanner: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text("These are sample classes.").textStyle(.meta).foregroundStyle(.inkMuted)
            Spacer(minLength: 8)
            // The demo ends and setup opens on its "On your computer" step.
            Button("Connect your computer") { Task { await app.endDemo() } }.buttonStyle(.textLink)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line))
    }

    /// A library from before it asked what it's for: asked once, as a row here (never a sheet over Home).
    private var usesRow: some View {
        Button { askingUses = true } label: {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Tell Syft Notes what it's for").textStyle(.title).foregroundStyle(.ink)
                    Text("So it calls things by the right names: lectures and classes, or meetings and folders.")
                        .textStyle(.summary).foregroundStyle(.inkMuted)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Icon("chevron_right", size: 16, relativeTo: .subheadline).foregroundStyle(.inkSubtle)
            }
            .padding(.vertical, 6)
        }
        .buttonStyle(.row)
    }

    // MARK: Up next

    /// The rest of today's and tomorrow's events, up to four; each row records it, into its class. Shown once the
    /// calendar has been read.
    @ViewBuilder private func upNext(now: Date) -> some View {
        if let events = upcoming.value?.events {
            let coming = Array(HomeWords.coming(events, now: now).prefix(4))
            VStack(alignment: .leading, spacing: 0) {
                SectionHeading(title: "Up next").padding(.bottom, coming.isEmpty ? 8 : 4)
                if coming.isEmpty {
                    EmptyText("Nothing on your calendar for the rest of today or tomorrow.")
                }
                ForEach(Array(coming.enumerated()), id: \.element.id) { i, e in
                    EventRow(event: e, suggest: i == 0 || coming[i - 1].title != e.title) {
                        Task { await library.load(); if let v = library.value { app.learn(v) } }
                    }
                }
            }
            .padding(.top, gap)
        }
    }

    // MARK: Due this week

    /// What's missing, then what's due in the next seven days, up to three; each opens the assignment here. Only for
    /// a school library with Canvas, and only when something is.
    @ViewBuilder private func dueThisWeek(now: Date) -> some View {
        if canvas, let list = due.value {
            let soon = HomeWords.dueSoon(list, now: now)
            if !soon.isEmpty {
                VStack(alignment: .leading, spacing: 0) {
                    SectionHeading(title: "Due this week", trailing: "See all") { app.showDue(class: nil) }
                    .padding(.bottom, 4)
                    ForEach(Array(soon.prefix(3).enumerated()), id: \.offset) { _, x in
                        Button { path.append(Route.assignment(class: x.item.class, id: x.item.id)) } label: {
                            HomeDueRow(item: x.item, late: x.late, hue: app.hue(of: x.item.class), now: now)
                        }
                        .buttonStyle(.row)
                    }
                }
                .padding(.top, gap)
            }
        }
    }

    // MARK: the folders, as the desktop's sidebar groups them

    @ViewBuilder private var folders: some View {
        if let overview = library.value, !overview.classes.isEmpty {
            let groups = HomeWords.groups(overview.classes, classes: app.words.classes)
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(groups.enumerated()), id: \.element.name) { i, group in
                    GroupLabel(group.name)
                        .padding(.top, i == 0 ? 0 : 20).padding(.bottom, 2)
                    ForEach(group.classes) { c in
                        NavigationLink(value: Route.classDetail(c.name)) {
                            FolderRow(info: c, hue: Highlighter(classIndex: c.color))
                        }
                        .buttonStyle(.row)
                    }
                }
            }
            .padding(.top, gap)
        } else if library.value == nil, let error = library.error {
            EmptyState(title: error.unreachable ? "Can't reach your library" : "Couldn't load your \(app.words.classes.lowercased())",
                       detail: error.unreachable ? "It opens when your phone can reach your computer again. Recording still works." : error.detail,
                       actionTitle: "Try again") { Task { await loadAll() } }
                .padding(.top, gap - 24)
        }
    }

    // MARK: every note

    private var shownNotes: [LectureSummary] {
        var seen = Set<String>()
        let list = onlyUnsorted ? (unsorted.value ?? []) : (notes.value ?? []) + older
        return list.filter { seen.insert($0.id).inserted }.map { l in
            guard let to = moved[l.id] else { return l }
            var m = l
            m.class = to
            return m
        }
        .filter { !onlyUnsorted || folder(of: $0) == "Unsorted" }
    }

    /// A note's folder, "Unsorted" for none.
    private func folder(of l: LectureSummary) -> String {
        let c = moved[l.id] ?? l.class ?? ""
        return c.isEmpty ? "Unsorted" : c
    }

    private var notesSection: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .lastTextBaseline) {
                SectionHeading(title: "Notes")
                filter
            }
            .padding(.bottom, 12)
            let list = shownNotes
            let source = onlyUnsorted ? unsorted : notes
            if list.isEmpty {
                if source.value != nil {
                    EmptyText(onlyUnsorted ? "Nothing to sort. Every note is in a folder."
                              : "Your notes will be here. Record your first class with the Record button below, or on your computer.")
                } else if let error = source.error {
                    EmptyState(title: error.unreachable ? "Can't reach your library" : "Couldn't load your notes",
                               detail: error.unreachable ? "They show when your phone can reach your computer again. Recording still works." : error.detail,
                               actionTitle: "Try again") { Task { await source.load() } }
                } else {
                    StudyLoader(.page, color: .inkSubtle).frame(maxWidth: .infinity, minHeight: 120)
                }
            } else {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(list.enumerated()), id: \.element.id) { i, l in
                        NavigationLink(value: Route.lecture(l.id)) {
                            NoteRow(lecture: l, first: i == 0) { moving = l }
                        }
                        .buttonStyle(.plain)
                        .onAppear { if !onlyUnsorted, l.id == list.last?.id { Task { await loadMore() } } }
                    }
                }
                if !onlyUnsorted, !allLoaded, list.count >= Self.pageSize {
                    Group {
                        if loadingMore { StudyLoader(.inline, color: .inkSubtle) } else {
                            Button("Show more") { Task { await loadMore() } }.buttonStyle(.textLink)
                        }
                    }
                    .padding(.top, 8)
                    .padding(.leading, regular ? 128 : 0)
                }
            }
        }
    }

    /// The desktop's filter: "All notes", "Unsorted" (in place), then each folder under its group (those open its page).
    private var filter: some View {
        Menu {
            Button { onlyUnsorted = false } label: {
                if onlyUnsorted { Text("All notes") } else { Label("All notes", icon: "check") }
            }
            Button { onlyUnsorted = true } label: {
                if onlyUnsorted { Label("Unsorted", icon: "check") } else { Label("Unsorted", icon: "inbox") }
                if let n = library.value?.unsorted { Text(n == 0 ? "None" : n == 1 ? "One note" : "\(n) notes") }
            }
            if let classes = library.value?.classes, !classes.isEmpty {
                ForEach(HomeWords.groups(classes, classes: app.words.classes), id: \.name) { group in
                    Section(group.name) {
                        ForEach(group.classes) { c in
                            Button { path.append(Route.classDetail(c.name)) } label: {
                                Text(c.name)
                                Text(HomeWords.counted(c.lectures, app.noun(of: c.name)))
                            }
                        }
                    }
                }
            }
        } label: {
            PickerLabel(onlyUnsorted ? "Unsorted" : "All notes")
        }
        .buttonStyle(.picker)
        .accessibilityLabel(onlyUnsorted ? "Showing unsorted notes. Which notes to show" : "Showing all notes. Which notes to show")
    }

    // MARK: moving a note

    /// Files `l` in `to` at once ("Unsorted" for none), with a toast that can put it back.
    private func move(_ l: LectureSummary, to: String, undoing: Bool = false) async {
        let from = folder(of: l)
        guard from != to else { return }
        moved[l.id] = to
        do {
            // FLOWS: after the merge, `try await api.move(lecture: l.id, to: to)`.
            struct Body: Encodable { var `class`: String }
            let _: Lecture = try await api.post("/lectures/\(APIClient.segment(l.id))/class", Body(class: to))
            UINotificationFeedbackGenerator().notificationOccurred(.success)
            toast = undoing ? nil : HomeToast(text: to == "Unsorted" ? "Moved to Unsorted." : "Moved to \(to).", undo: { [l] in
                Task { await move(l, to: from, undoing: true) }
            })
            async let a: Void = library.load()
            async let b: Void = onlyUnsorted ? unsorted.load() : notes.load()
            _ = await (a, b)
            if let v = library.value { app.learn(v) }
        } catch {
            moved[l.id] = from
            UINotificationFeedbackGenerator().notificationOccurred(.error)
            toast = HomeToast(text: (error as? APIError)?.unreachable == true
                              ? "Couldn't move it. Your library can't be reached." : "Couldn't move it.", undo: nil)
        }
    }

    @ViewBuilder private var toastView: some View {
        if let t = toast {
            Toast(t.text, actionTitle: t.undo == nil ? nil : "Undo") {
                let undo = t.undo
                toast = nil
                undo?()
            }
            .padding(.horizontal, Space.pageCompact)
            .padding(.bottom, 16)
            .transition(.move(edge: .bottom).combined(with: .opacity))
            .task(id: t.id) {
                try? await Task.sleep(for: .seconds(5))
                if toast?.id == t.id { toast = nil }
            }
        }
    }

    // MARK: loading

    private func loadAll() async {
        async let a: Void = library.load()
        async let b: Void = notes.load()
        async let c: Void = due.load()
        async let d: Void = upcoming.load()
        _ = await (a, b, c, d)
        if onlyUnsorted { await unsorted.load() }
        older = []
        moved = [:]
        allLoaded = (notes.value?.count ?? 0) < Self.pageSize
        if let v = library.value { app.learn(v) }
    }

    /// The next page of notes, older than the oldest shown.
    private func loadMore() async {
        guard !loadingMore, !allLoaded, let last = shownNotes.last, let before = last.date else { return }
        loadingMore = true
        defer { loadingMore = false }
        guard let page = try? await api.lectures(limit: Self.pageSize, before: before) else { return }
        let known = Set(((notes.value ?? []) + older).map(\.id))
        let fresh = page.filter { !known.contains($0.id) }
        older += fresh
        if page.count < Self.pageSize || fresh.isEmpty { allLoaded = true }
    }
}

/// A toast with a way to undo what it says.
private struct HomeToast: Identifiable, Equatable {
    let id = UUID()
    var text: String
    var undo: (() -> Void)?
    static func == (a: HomeToast, b: HomeToast) -> Bool { a.id == b.id }
}

/// "Offline. Showing what was saved before. Recording still works." The foundation's OfflineNote with the plan's last
/// sentence (it can move into OfflineNote).
private struct HomeOffline: View {
    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Icon("cloud_off", size: 16, relativeTo: .subheadline).foregroundStyle(.inkSubtle)
                .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 3 }
            Text("Offline. Showing what was saved before. Recording still works.").textStyle(.meta).foregroundStyle(.inkMuted)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line))
    }
}

/// Something due, as Up next's rows are laid out: when (the time over the day; "Missing" in the late red over the day
/// it was due) | its name, marked with its class's highlighter.
private struct HomeDueRow: View {
    @Environment(\.horizontalSizeClass) private var sizeClass
    var item: DueItem
    var late: Bool
    var hue: Highlighter?
    var now: Date

    var body: some View {
        let at = Format.parse(item.due)
        let dayOnly = Format.isDayOnly(item.due) || at.map { d in
            let hm = Format.calendar.dateComponents([.hour, .minute], from: d)
            return hm.hour == 0 && hm.minute == 0
        } ?? true
        let top = late ? "Missing" : at.map { dayOnly ? Format.day($0, now: now) : Format.time($0) } ?? "No due date"
        let bottom = at.map { d in late ? Format.date(d, now: now) : dayOnly ? "" : Format.day(d, now: now) } ?? ""
        HStack(alignment: .center, spacing: sizeClass == .regular ? 20 : 14) {
            VStack(alignment: .leading, spacing: 0) {
                Text(top).textStyle(.when).foregroundStyle(late ? Color.late : .inkMuted)
                if !bottom.isEmpty { Text(bottom).textStyle(.micro).foregroundStyle(.inkSubtle) }
            }
            .frame(width: sizeClass == .regular ? 108 : 84, alignment: .leading)
            Highlight(item.name, hue: hue, style: .due)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.vertical, 10)
        .contentShape(.rect)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(item.name), \(item.class), \(late ? "missing, was due \(at.map { Format.date($0, now: now) } ?? "")" : "due \(Format.dueWhen(item.due, now: now))")")
        .accessibilityAddTraits(.isButton)
    }
}

/// A folder as the desktop's sidebar shows it: its swatch, its name, how many notes. The whole row opens its page.
private struct FolderRow: View {
    var info: ClassInfo
    var hue: Highlighter

    var body: some View {
        HStack(spacing: 12) {
            Swatch(hue)
            Text(info.name).textStyle(.nav).foregroundStyle(.ink)
                .lineLimit(2)
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
            Text("\(info.lectures)").textStyle(.meta).monospacedDigit().foregroundStyle(.inkSubtle)
        }
        .padding(.vertical, 11)
        .frame(minHeight: 44)
        .contentShape(.rect)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(info.name), \(info.lectures)")
        .accessibilityAddTraits(.isButton)
    }
}

/// Move to: where a note goes. Unsorted, then the folders under their groups, each with its swatch and count, the
/// one it's in checked; a search field when there are many. A pick moves it at once.
private struct MoveToPicker: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(AppModel.self) private var app
    var current: String
    var classes: [ClassInfo]
    var unsorted: Int
    var pick: (String) -> Void
    @State private var query = ""

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    let q = query.trimmingCharacters(in: .whitespaces)
                    if q.isEmpty || "unsorted".contains(q.lowercased()) {
                        row(name: "Unsorted", count: unsorted) {
                            Icon("inbox", size: 16, relativeTo: .subheadline).foregroundStyle(.inkSubtle).frame(width: 16)
                        }
                    }
                    let shown = classes.filter { q.isEmpty || $0.name.localizedCaseInsensitiveContains(q) || ($0.code ?? "").localizedCaseInsensitiveContains(q) }
                    ForEach(HomeWords.groups(shown, classes: app.words.classes), id: \.name) { group in
                        GroupLabel(group.name).padding(.top, 18).padding(.bottom, 2)
                        ForEach(group.classes) { c in
                            row(name: c.name, count: c.lectures) { Swatch(Highlighter(classIndex: c.color)).frame(width: 16) }
                        }
                    }
                    if !q.isEmpty && shown.isEmpty && !"unsorted".contains(q.lowercased()) {
                        EmptyText("No folder called \u{201C}\(q)\u{201D}. Folders are made on your computer, in the sidebar.")
                            .padding(.top, 12)
                    }
                }
                .padding(.horizontal, Space.pageCompact)
                .padding(.vertical, 8)
            }
            .page()
            .navigationTitle("Move to")
            .navigationBarTitleDisplayMode(.inline)
            .searchable(text: $query, placement: .navigationBarDrawer(displayMode: classes.count > 6 ? .always : .automatic), prompt: "Find a folder")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button { dismiss() } label: { Text("Cancel").textStyle(.nav) }
                }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationBackground(.surface)
    }

    private func row<Mark: View>(name: String, count: Int, @ViewBuilder mark: () -> Mark) -> some View {
        Button {
            dismiss()
            pick(name)
        } label: {
            HStack(spacing: 12) {
                mark()
                Text(name).textStyle(.nav).fontWeight(name == current ? .bold : .medium).foregroundStyle(.ink)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if name == current {
                    Icon("check", size: 16, relativeTo: .subheadline).foregroundStyle(.ink)
                } else {
                    Text("\(count)").textStyle(.meta).monospacedDigit().foregroundStyle(.inkSubtle)
                }
            }
            .frame(minHeight: 44)
        }
        .buttonStyle(.row)
        .accessibilityLabel(name == current ? "\(name), where it is now" : "Move to \(name)")
    }
}

/// What Home says, the desktop's way (OverviewModel.HomeLede, EventSentence, DueSentence, Coming) in the order
/// FLOWS.md gives: one or two plain sentences, the most pressing first.
enum HomeWords {
    static let counts = ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"]

    /// "Two", "Eleven" as "11": a count as a sentence starts with it.
    static func count(_ n: Int) -> String { n >= 0 && n < counts.count ? counts[n] : "\(n)" }

    /// "3 lectures", "1 meeting".
    static func counted(_ n: Int, _ noun: String) -> String { "\(n) \(n == 1 ? noun : noun + "s")" }

    /// The events still to come today and tomorrow (one that's on now counts), soonest first.
    static func coming(_ events: [UpcomingEvent], now: Date) -> [UpcomingEvent] {
        events.compactMap { e -> (UpcomingEvent, Date)? in
            guard let s = Format.parse(e.start), let end = Format.parse(e.end) ?? Format.parse(e.start),
                  end > now, Format.daysBetween(now, s) <= 1 else { return nil }
            return (e, s)
        }
        .sorted { $0.1 < $1.1 }
        .map(\.0)
    }

    /// An event by the name the page uses: its own, or (named for its class and nothing more) the class's.
    static func named(_ e: UpcomingEvent, cls: String?) -> String {
        let title = e.title.trimmingCharacters(in: .whitespaces)
        if let cls, title.caseInsensitiveCompare(cls) == .orderedSame { return cls }
        return title
    }

    /// What's on, or next today: "Capstone team sync starts in 11 minutes." (within the hour), "Data Mining is on
    /// now.", "Officer meeting is at 6:00 PM."; "" with nothing left today.
    static func eventSentence(_ events: [UpcomingEvent], className: (UpcomingEvent) -> String?, now: Date) -> String {
        guard let next = coming(events, now: now).first(where: { Format.parse($0.start).map { Format.daysBetween(now, $0) <= 0 } ?? false }),
              let start = Format.parse(next.start) else { return "" }
        let name = named(next, cls: className(next))
        if start <= now { return "\(name) is on now." }
        let until = start.timeIntervalSince(now)
        return until <= 3600 ? "\(name) starts \(Format.inMinutes(until))." : "\(name) is at \(Format.time(start))."
    }

    /// After today: "Your next class is Calculus II, tomorrow at 9:00 AM." ("meeting" for a club's or work's; "Next
    /// on your calendar is …" for no folder's).
    static func nextSentence(_ events: [UpcomingEvent], className: (UpcomingEvent) -> String?, noun: (String?) -> String, now: Date) -> String {
        guard let next = events.compactMap({ e in Format.parse(e.start).map { (e, $0) } })
            .filter({ $0.1 > now && Format.daysBetween(now, $0.1) >= 1 }).min(by: { $0.1 < $1.1 }) else { return "" }
        let (e, start) = next
        let cls = className(e)
        let away = Format.daysBetween(now, start)
        let day = away == 1 ? "tomorrow" : away < 7 ? Format.words(start, "EEEE") : "on \(Format.date(start, now: now))"
        let when = e.allDay ? day : "\(day) at \(Format.time(start))"
        let name = named(e, cls: cls)
        let kind = cls.map { noun($0) }
        if kind == Nouns.lecture { return "Your next class is \(name), \(when)." }
        if kind == Nouns.meeting { return "Your next meeting is \(name), \(when)." }
        return "Next on your calendar is \(name), \(when)."
    }

    /// What's still to hand in (not handed in, not excused), and whether it's late.
    static func open(_ due: DueList) -> [(item: DueItem, late: Bool)] {
        due.groups.filter { $0.key != "handed_in" }
            .flatMap { g in g.items.filter { !$0.excused }.map { (item: $0, late: g.key == "overdue" || $0.missing) } }
    }

    /// What's missing (oldest first), then what's due today and the six days after it (soonest first).
    static func dueSoon(_ due: DueList, now: Date) -> [(item: DueItem, late: Bool)] {
        let cal = Format.calendar
        let today = cal.startOfDay(for: now)
        let end = cal.date(byAdding: .day, value: 7, to: today)!
        let all = open(due)
        let late = all.filter(\.late).sorted { (Format.parse($0.item.due) ?? .distantPast) < (Format.parse($1.item.due) ?? .distantPast) }
        let week = all.filter { x in
            guard !x.late, let d = Format.parse(x.item.due) else { return false }
            return d >= today && d < end
        }
        .sorted { (Format.parse($0.item.due) ?? .distantFuture) < (Format.parse($1.item.due) ?? .distantFuture) }
        return late + week
    }

    /// "Problem Set 6 is due tonight", "Two assignments due Wednesday", then what's missing ("…, and Lab Report 3 is
    /// still missing."); "" with nothing due in the next seven days and nothing missing. (The desktop's words; its
    /// week is Sunday to Saturday, the phone's the next seven days, so Home and its "Due this week" agree on a
    /// Friday about Sunday's work.)
    static func dueSentence(_ due: DueList, now: Date) -> String {
        let cal = Format.calendar
        let soon = dueSoon(due, now: now)
        let late = soon.filter(\.late).map(\.item)
        let week = soon.filter { !$0.late }.compactMap { x in Format.parse(x.item.due).map { (x.item, $0) } }

        var first = ""
        if let soonest = week.first {
            let at = soonest.1
            let that = week.filter { cal.isDate($0.1, inSameDayAs: at) }
            let away = Format.daysBetween(now, at)
            let on = away == 0 ? "today" : away == 1 ? "tomorrow" : Format.words(at, "EEEE")
            if that.count == 1 {
                let hm = cal.dateComponents([.hour, .minute], from: at)
                let day1 = away == 0 ? ((hm.hour ?? 0) >= 17 ? "tonight" : "today") : on
                // The end of the day is when most things are due, so it goes unsaid.
                first = hm.hour == 23 && hm.minute == 59 ? "\(that[0].0.name) is due \(day1)" : "\(that[0].0.name) is due \(day1) at \(Format.time(at))"
            } else {
                first = "\(count(that.count)) \(kind(that.map(\.0))) due \(on)"
            }
        }
        var then = ""
        switch late.count {
        case 0: then = ""
        case 1: then = first.isEmpty ? "\(late[0].name) is still missing" : "and \(late[0].name) is still missing"
        default: then = first.isEmpty ? "\(count(late.count)) \(kind(late)) are still missing" : "and \(count(late.count).lowercased()) are still missing"
        }
        if first.isEmpty && then.isEmpty { return "" }
        return !first.isEmpty && !then.isEmpty ? "\(first), \(then)." : "\(first)\(then)."
    }

    /// "assignments", or "quizzes" when everything counted is one.
    static func kind(_ items: [DueItem]) -> String {
        !items.isEmpty && items.allSatisfy { $0.kind == "quiz" } ? "quizzes" : "assignments"
    }

    /// Home's sentence or two, the most pressing first: what's on today, a recording that hasn't reached the library,
    /// what's due and missing, notes being written. With none of those, that nothing's due and when the next class
    /// is; with no calendar or Canvas to tell of, how many notes there are.
    static func lede(events: [UpcomingEvent]?, due: DueList?, overview: LibraryOverview?, unsent: Int,
                     className: (UpcomingEvent) -> String?, noun: (String?) -> String, words: Words, now: Date) -> String {
        let today = events.map { eventSentence($0, className: className, now: now) } ?? ""
        var parts = [today]
        if unsent > 0 {
            parts.append(unsent == 1 ? "A recording on this phone hasn't reached your library."
                         : "\(count(unsent)) recordings on this phone haven't reached your library.")
        }
        if let due { parts.append(dueSentence(due, now: now)) }
        if let w = overview?.writing, w > 0 {
            parts.append("Notes for \(count(w).lowercased()) \(w == 1 ? words.lecture : words.lectures) are being written.")
        }
        parts = parts.filter { !$0.isEmpty }
        let next = today.isEmpty ? events.map { nextSentence($0, className: className, noun: noun, now: now) } ?? "" : ""
        if !parts.isEmpty {
            if parts.count == 1, !next.isEmpty { parts.append(next) }
            return parts.prefix(2).joined(separator: " ")
        }
        if let o = overview, o.classes.reduce(0, { $0 + $1.lectures }) + o.unsorted == 0 {
            return "Your notes will be here. Record your first class with the Record button below, or on your computer."
        }
        if due != nil { return ["Nothing due this week.", next].filter { !$0.isEmpty }.joined(separator: " ") }
        if !next.isEmpty { return next }
        if events != nil { return "Nothing on your calendar for the rest of today or tomorrow." }
        guard let o = overview else { return "" }
        let notes = o.classes.reduce(0) { $0 + $1.lectures } + o.unsorted
        return "\(count(notes)) note\(notes == 1 ? "" : "s") in \(count(o.classes.count).lowercased()) folder\(o.classes.count == 1 ? "" : "s")."
    }

    /// The folders under their sidebar groups: Classes first (`classes` names it: "Folders" outside school), then the
    /// rest in the order they come.
    static func groups(_ list: [ClassInfo], classes: String) -> [(name: String, classes: [ClassInfo])] {
        var out: [(name: String, classes: [ClassInfo])] = []
        for c in list {
            let g = (c.group ?? "").isEmpty ? classes : c.group!
            if let i = out.firstIndex(where: { $0.name == g }) { out[i].classes.append(c) } else { out.append((g, [c])) }
        }
        return out.sorted { a, b in (a.name == classes ? 0 : 1) < (b.name == classes ? 0 : 1) }
    }
}

/// What on this phone hasn't reached the library: recordings (sending, waiting, or refused) and notebooks with
/// changes not saved there. A recording opens its page; a notebook opens; long press sends, shares or deletes.
struct OnThisPhone: View {
    @Environment(AppModel.self) private var app
    @State private var store = RecordingStore.shared
    @State private var notebooks = NotebookStore.shared
    @State private var open: UUID?
    var gap: CGFloat = 32

    var body: some View {
        let list = store.unsent.filter { $0.state != .recording }
        let unsaved = notebooks.notebooks.filter(\.needsSaving)
        if !list.isEmpty || !unsaved.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                SectionHeading(title: "On this phone").padding(.bottom, 2)
                ForEach(Array(list.enumerated()), id: \.element.id) { i, r in
                    VStack(spacing: 0) {
                        if i > 0 { Rule() }
                        row(r)
                    }
                }
                ForEach(Array(unsaved.enumerated()), id: \.element.id) { i, n in
                    VStack(spacing: 0) {
                        if i > 0 || !list.isEmpty { Rule() }
                        Button { open = n.id } label: {
                            line(title: n.title, icon: "edit_note", status: "Not saved to your library", late: false)
                        }
                        .buttonStyle(.row)
                    }
                }
            }
            .padding(.top, gap)
            .fullScreenCover(item: Binding(get: { open.map(IdentifiedUUID.init) }, set: { open = $0?.id })) { NotebookEditor(id: $0.id) }
            .onChange(of: app.dismissal) { _, _ in open = nil }
        }
    }

    private func row(_ r: Recording) -> some View {
        HStack(alignment: .center, spacing: 8) {
            // The recording's own page: where it's got to, what was typed and said, Send now, Share, Delete.
            NavigationLink(value: Route.recording(r.id)) {
                line(title: title(r), icon: r.state == .failed ? "warning" : r.state == .sending ? "upload" : "cloud_upload",
                     status: status(r), late: r.state == .failed && r.error != nil)
            }
            .buttonStyle(.row)
            if r.state == .sending { StudyLoader(.inline, color: .inkSubtle) }
            Menu { actions(r) } label: { Label("More", icon: "more_horiz") }
                .buttonStyle(.icon)
                .accessibilityLabel("More for \(title(r))")
        }
        .contextMenu { actions(r) }
    }

    @ViewBuilder private func actions(_ r: Recording) -> some View {
        Button("Send now", icon: "upload") { send(r) }
        if let audio = store.audioURL(r), FileManager.default.fileExists(atPath: audio.path) {
            ShareLink(item: audio) { Label("Share the audio", icon: "ios_share") }
        }
        Button("Delete", icon: "delete", role: .destructive) { store.delete(r) }
    }

    private func line(title: String, icon: String, status: String, late: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).textStyle(.title).foregroundStyle(.ink).lineLimit(2).multilineTextAlignment(.leading)
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Icon(icon, size: 15, relativeTo: .footnote).foregroundStyle(.inkSubtle)
                    .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 3 }
                Text(status).textStyle(.meta).foregroundStyle(late ? Color.late : .inkMuted).lineLimit(3)
                    .multilineTextAlignment(.leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 12)
        .contentShape(.rect)
    }

    private func send(_ r: Recording) {
        if let api = app.api { Task { await store.send(r.id, api: api) } }
    }

    /// Its title, or the desktop's working title for it: "Calculus II, Fri 16 Oct", "Recording, Fri 16 Oct at 4:12 PM".
    private func title(_ r: Recording) -> String {
        if !r.title.isEmpty { return r.title }
        let day = Format.words(r.started, "EEE d MMM")
        if let c = r.className, !c.isEmpty, c != "Unsorted" { return "\(c), \(day)" }
        return "Recording, \(day) at \(Format.time(r.started))"
    }

    private func status(_ r: Recording) -> String {
        switch r.state {
        case .sending: return "Sending to your library…"
        case .failed: return r.error ?? "Couldn't send it. It's sent again when your library can be reached."
        default:
            let length = Format.duration(r.seconds)
            let waiting = app.isDrive ? "on its way to your laptop" : "waiting to reach your library"
            return length.isEmpty ? waiting.prefix(1).uppercased() + waiting.dropFirst() : "\(length), \(waiting)"
        }
    }
}

/// The notebooks written (or brought in from GoodNotes) on this phone, newest first, as pages on a shelf; a tap opens
/// one.
struct NotebookShelf: View {
    @Environment(AppModel.self) private var app
    @State private var store = NotebookStore.shared
    @State private var open: UUID?
    var gap: CGFloat = 32

    var body: some View {
        if !store.notebooks.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                SectionHeading(title: "Notebooks").padding(.bottom, 12)
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(alignment: .top, spacing: 16) {
                        ForEach(store.notebooks) { n in
                            Button { open = n.id } label: { cover(n) }
                                .buttonStyle(.plain)
                                .accessibilityLabel("\(n.title), \(n.needsSaving ? "not saved to your library" : "in \(n.className ?? "your library")")")
                        }
                    }
                    .padding(.vertical, 2)
                }
                .scrollClipDisabled()
            }
            .padding(.top, gap)
            .fullScreenCover(item: Binding(get: { open.map(IdentifiedUUID.init) }, set: { open = $0?.id })) { NotebookEditor(id: $0.id) }
            .onChange(of: app.dismissal) { _, _ in open = nil }
        }
    }

    private func cover(_ n: Notebook) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Group {
                if let image = store.thumbnail(n, width: 240) { Image(uiImage: image).resizable().scaledToFill() } else { Color.white }
            }
            .frame(width: 112, height: 144)
            .clipShape(.rect(cornerRadius: Radius.control))
            .overlay(RoundedRectangle(cornerRadius: Radius.control).strokeBorder(.line))
            .padding(.bottom, 4)
            Text(n.title).textStyle(.meta).fontWeight(.bold).foregroundStyle(.ink).lineLimit(2)
            Text(n.needsSaving ? "Not saved to your library" : n.className ?? "In your library")
                .textStyle(.micro).foregroundStyle(.inkMuted).lineLimit(2)
        }
        .frame(width: 112, alignment: .leading)
        .contentShape(.rect)
    }
}

struct IdentifiedUUID: Identifiable { var id: UUID }
