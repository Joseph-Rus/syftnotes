import SwiftUI

// Search, as the desktop's quick panel (MacQuick) has it, on a page: the classes whose name matches, each with its
// swatch; the notes that match, as the note rows Home has; and what was said (or written) that matched, with the words
// found in ink, each opening its lecture at that minute. With nothing typed: the searches made before, and every class
// to go to.

/// Search everything: lectures, the passages that matched (in notes or what was said), and classes.
struct SearchTab: View {
    @Environment(AppModel.self) private var app
    @State private var query = DebugLaunch.value("-search") ?? ""

    var body: some View {
        NavigationStack(path: app.stack(.search)) {
            SearchResultsView(query: query, live: true) { query = $0 }
                .searchable(text: $query, prompt: "Lectures, topics, what was said")
                .onSubmit(of: .search) { RecentSearches.add(query) }
                .laptopNotice()
                .routes()
        }
    }
}

/// What a search found. `live` is the Search tab, searched as you type (`pick` runs a search made before); otherwise a
/// search opened from a page (a lecture's topic), titled with its words.
struct SearchResultsView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    var query: String
    var live = false
    var pick: ((String) -> Void)?

    @State private var results: SearchResults?
    @State private var problem: String?
    @State private var attempt = 0
    @State private var recent = RecentSearches.load()

    private var words: String { query.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var pad: CGFloat { sizeClass == .regular ? Space.pageRegular : Space.pageCompact }

    var body: some View {
        Group {
            if words.isEmpty && live {
                resting
            } else {
                ScrollView {
                    content
                        .padding(.horizontal, pad)
                        .padding(.bottom, 24)
                        .frame(maxWidth: 760 + 2 * pad, alignment: .leading)
                        .frame(maxWidth: .infinity)
                }
                .scrollDismissesKeyboard(.immediately)
                .page()
            }
        }
        .foregroundStyle(.ink)
        .navigationTitle(live ? "Search" : words)
        .navigationBarTitleDisplayMode(.large)
        .onChange(of: words.isEmpty) { _, empty in if empty { recent = RecentSearches.load() } }
        .task(id: "\(words)#\(attempt)") {
            let q = words
            guard !q.isEmpty, let api = app.api else { results = nil; problem = nil; return }
            if live { try? await Task.sleep(for: .milliseconds(250)) }
            guard !Task.isCancelled else { return }
            do {
                let r = try await api.search(q, limit: 30)
                guard !Task.isCancelled else { return }
                results = r
                problem = nil
            } catch is CancellationError {
            } catch {
                problem = (error as? APIError)?.detail ?? "Something went wrong."
            }
        }
    }

    @ViewBuilder private var content: some View {
        if let problem {
            EmptyState(title: "Couldn't search", detail: problem, actionTitle: "Try again") { attempt += 1 }
        } else if let r = results {
            if r.lectures.isEmpty && r.passages.isEmpty && r.classes.isEmpty {
                EmptyText("Nothing matches “\(words)”. Try a topic, or a word the lecturer said.")
                    .padding(.top, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                found(r)
            }
        } else {
            StudyLoader(.page, color: .inkSubtle)
                .frame(maxWidth: .infinity, minHeight: 160)
        }
    }

    // MARK: before anything's typed

    /// The searches made before (tap to run one again, swipe to forget it), then every class to go to.
    private var resting: some View {
        List {
            if !recent.isEmpty {
                Section {
                    ForEach(recent, id: \.self) { r in
                        Button { pick?(r) } label: {
                            HStack(spacing: 12) {
                                Icon("search", size: 16).foregroundStyle(.inkSubtle)
                                Text(r).textStyle(.nav).foregroundStyle(.ink).lineLimit(1)
                                Spacer(minLength: 8)
                            }
                            .frame(minHeight: 44)
                            .contentShape(.rect)
                        }
                        .buttonStyle(.row)
                        .accessibilityHint("Searches for it again")
                        .swipeActions {
                            Button(role: .destructive) { forget(r) } label: { Label("Remove", icon: "delete") }
                        }
                        .restingRow(pad)
                    }
                } header: {
                    GroupLabel("Recent searches").restingHeader(pad)
                }
            }
            if !app.classes.isEmpty {
                Section {
                    ForEach(app.classes) { c in
                        // The link is under the row, so the list draws no chevron of its own: the row has the app's.
                        classRow(c)
                            .background { NavigationLink(value: Route.classDetail(c.name)) { EmptyView() }.opacity(0) }
                            .restingRow(pad)
                    }
                } header: {
                    GroupLabel("Your \(app.words.classes.lowercased())").restingHeader(pad)
                }
            } else if recent.isEmpty {
                EmptyText("Find a \(app.words.lecture) by its title or topic, or anything that was said in class.")
                    .restingRow(pad)
                    .listRowSeparator(.hidden)
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .scrollDismissesKeyboard(.immediately)
        .page()
    }

    private func forget(_ r: String) {
        RecentSearches.remove(r)
        withAnimation { recent = RecentSearches.load() }
    }

    // MARK: what it found

    private func found(_ r: SearchResults) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if !r.classes.isEmpty {
                GroupLabel(app.words.classes).padding(.top, 12).padding(.bottom, 2)
                ForEach(Array(r.classes.enumerated()), id: \.element.name) { i, c in
                    if i > 0 { Rule() }
                    NavigationLink(value: Route.classDetail(c.name)) { classRow(c) }
                        .buttonStyle(.row)
                        .simultaneousGesture(TapGesture().onEnded { remember() })
                }
            }
            if !r.lectures.isEmpty {
                GroupLabel("Notes").padding(.top, r.classes.isEmpty ? 12 : 28).padding(.bottom, 6)
                ForEach(Array(r.lectures.enumerated()), id: \.element.id) { i, l in
                    NavigationLink(value: Route.lecture(l.id)) { NoteRow(lecture: l, first: i == 0) }
                        .buttonStyle(.plain)
                        .simultaneousGesture(TapGesture().onEnded { remember() })
                }
            }
            if !r.passages.isEmpty {
                GroupLabel("Said in class").padding(.top, 28).padding(.bottom, 2)
                ForEach(Array(r.passages.enumerated()), id: \.offset) { i, p in
                    if i > 0 { Rule() }
                    // What was said opens on the Transcript at its minute; a passage of the notes opens the Notes.
                    NavigationLink(value: p.kind == "transcript" ? askLectureRoute(p.id, at: p.at) : Route.lecture(p.id)) {
                        passageRow(p)
                    }
                    .buttonStyle(.row)
                    .simultaneousGesture(TapGesture().onEnded { remember() })
                }
            }
        }
    }

    /// A search that found something and was followed is worth keeping.
    private func remember() { if live { RecentSearches.add(words) } }

    /// A class: its swatch, its name, how many notes it has, the chevron. Opens its page.
    private func classRow(_ c: ClassInfo) -> some View {
        let noun = app.noun(of: c.name)
        return HStack(spacing: 12) {
            Swatch(Highlighter(classIndex: c.color))
            Text(c.name).textStyle(.nav).foregroundStyle(.ink).lineLimit(1)
            Spacer(minLength: 12)
            Text("\(c.lectures) \(c.lectures == 1 ? noun : noun + "s")").textStyle(.meta).foregroundStyle(.inkSubtle)
                .monospacedDigit()
            Icon("chevron_right", size: 16).foregroundStyle(.inkSubtle)
        }
        .frame(minHeight: 48)
        .contentShape(.rect)
        .accessibilityElement(children: .combine)
    }

    /// A passage, as the desktop's quick panel has one: the lecture it's in (its class's swatch, its title) and where
    /// in it (the minute, or the part of the notes), then the words around what was found, those in bold ink.
    private func passageRow(_ p: Passage) -> some View {
        let title = (p.title?.isEmpty == false ? p.title : nil) ?? Nouns.untitled(folder: p.class, date: p.date)
        let at = p.kind == "transcript" ? p.at.map(Format.clock) : (p.section.isEmpty ? nil : p.section)
        return VStack(alignment: .leading, spacing: 5) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Swatch(app.hue(of: p.class), small: true)
                Text(title).textStyle(.nav).fontWeight(.semibold).foregroundStyle(.ink).lineLimit(1)
                Spacer(minLength: 12)
                if let at { Text(at).textStyle(.length).foregroundStyle(.inkSubtle).monospacedDigit().lineLimit(1) }
            }
            Text(marked(Self.snippet(p.text)))
                .textStyle(.summary)
                .lineLimit(3)
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.vertical, 14)
        .contentShape(.rect)
        .accessibilityElement(children: .combine)
    }

    /// A passage's words as one run of text: the transcript's [00:12] stamps gone, its lines joined.
    nonisolated static func snippet(_ text: String) -> String {
        text.replacing(/\[\d{1,2}:\d{2}(?::\d{2})?\]\s*/, with: "")
            .replacing(/\s+/, with: " ")
            .trimmingCharacters(in: .whitespaces)
    }

    /// The passage muted, the words searched for in ink at weight 700.
    private func marked(_ text: String) -> AttributedString {
        var a = AttributedString(PlainMath.inline(text))
        a.foregroundColor = .inkMuted
        for word in words.split(separator: " ").map(String.init) where word.count > 1 {
            var search = a.startIndex..<a.endIndex
            while let r = a[search].range(of: word, options: [.caseInsensitive, .diacriticInsensitive]) {
                a[r].foregroundColor = .ink
                a[r].font = .atkinson(14.5, .bold, relativeTo: .subheadline)
                search = r.upperBound..<a.endIndex
            }
        }
        return a
    }
}

private extension View {
    /// A row of Search's resting list: the page behind it, hairlines between, the page's margins.
    func restingRow(_ pad: CGFloat) -> some View {
        listRowBackground(Color.surface)
            .listRowSeparatorTint(.line)
            .listRowInsets(EdgeInsets(top: 0, leading: pad, bottom: 0, trailing: pad))
            .alignmentGuide(.listRowSeparatorLeading) { _ in 0 }
    }

    /// A group label over Search's resting list, at the page's margin.
    func restingHeader(_ pad: CGFloat) -> some View {
        padding(.top, 16).padding(.bottom, 2).listRowInsets(EdgeInsets(top: 0, leading: pad, bottom: 0, trailing: pad))
    }
}

/// The searches made on this phone, newest first, eight at most.
enum RecentSearches {
    static let key = "search.recent"

    static func load() -> [String] {
        #if DEBUG
        // `-recent "a|b"`: searches made before, for pictures.
        if let seed = DebugLaunch.value("-recent") { return seed.split(separator: "|").map(String.init) }
        #endif
        return UserDefaults.standard.stringArray(forKey: key) ?? []
    }

    static func add(_ query: String) {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard q.count > 1 else { return }
        var list = (UserDefaults.standard.stringArray(forKey: key) ?? []).filter { $0.caseInsensitiveCompare(q) != .orderedSame }
        list.insert(q, at: 0)
        UserDefaults.standard.set(Array(list.prefix(8)), forKey: key)
    }

    static func remove(_ query: String) {
        let list = (UserDefaults.standard.stringArray(forKey: key) ?? []).filter { $0 != query }
        UserDefaults.standard.set(list, forKey: key)
    }
}
