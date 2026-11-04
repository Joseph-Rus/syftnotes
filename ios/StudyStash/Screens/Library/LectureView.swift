import AVFoundation
import SwiftUI

/// A lecture's page, as the desktop's note page (NotePage.axaml, handoff 5.4): where it's filed and when ("Calculus II
/// ⌄ Lecture, Thursday 15 October at 9:00 AM, 52 min"), its title (tap to rename), its topics, then Notes | Transcript
/// with Attach (and Only my notes, where the notes have the student's own lines). The notes read as the desktop's:
/// the student's lines in ink, what was written from the recording in grey. A reading room: on a phone the tab bar and
/// the Record bar step aside, and the foot of the page is one bar, Ask about this lecture and Study.
///
/// Opened at a moment (`lectureMoment`, from an Ask source or a search passage), it shows the transcript at that line,
/// washed for a moment so the eye finds it. A lecture whose notes are still being written shows what the student
/// typed and the transcript, and fills in when the notes come.
struct LectureView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.lectureMoment) private var lectureMoment
    let id: String

    enum Pane: Hashable { case notes, transcript }

    @State private var lecture: Resource<Lecture>?
    @State private var notes: Resource<RenderedLecture>?
    @State private var files: Resource<[Attachment]>?
    @State private var photoNotes: (source: String, html: String)?
    @State private var pane: Pane = DebugLaunch.pane == "Transcript" ? .transcript : .notes
    @State private var onlyMine = DebugLaunch.flag("-only-mine")
    @State private var asking = false
    @State private var studying = false
    /// On screen (not under another tab, or under a page pushed over it).
    @State private var showing = false
    @State private var picking = false
    @State private var confirmingDelete = false
    /// The title being typed over (nil: not renaming).
    @State private var renaming: String?
    @FocusState private var titleFocused: Bool
    /// What this page changed that the library hasn't said back yet: the folder it moved to, the title it was given.
    @State private var movedTo: String?
    @State private var renamedTo: String?
    @State private var toast: PageToast?
    /// The transcript line opened at, washed for a moment.
    @State private var washed: Int?
    /// A moment of this lecture an answer about it used, tapped in the Ask sheet: shown here.
    @State private var jump: Moment?
    struct Moment: Equatable {
        var at: Double?
        let id = UUID()
    }
    @State private var player = TranscriptPlayer()
    /// The notebook beside the notes (iPad, or a wide window).
    @State private var inkBeside = false
    /// The notes' column's width, which the notebook beside it halves.
    @State private var columnWidth: CGFloat = .infinity
    @State private var store = NotebookStore.shared
    @State private var recordings = RecordingStore.shared
    @State private var recorder = Recorder.shared

    private var compact: Bool { sizeClass != .regular }
    /// The notebook shows beside the notes only where there's room: a window narrowed to a phone's width (Split View,
    /// Stage Manager) puts it away until it's wide again, rather than squeezing both.
    private var beside: Bool { inkBeside && !compact }
    /// The notes' column as narrow as a phone's: on a phone, or halved by the notebook in a narrow window (an iPad
    /// mini's), where the iPad's margins and Only my notes in the tab strip didn't fit.
    private var narrow: Bool { compact || (beside && columnWidth < 512) }
    private var margin: CGFloat { narrow ? Space.pageCompact : Space.pageRegular }
    /// The notes' column: the desktop's 704 (68 characters at 16).
    private let column: CGFloat = 704

    /// The moment to open at: from the route, or `-at <seconds>` in a debug build.
    private var moment: Double? { lectureMoment ?? DebugLaunch.value("-at").flatMap(Double.init) }

    var body: some View {
        let l = lecture?.value
        HStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    page(l)
                        .padding(.horizontal, margin)
                        .frame(maxWidth: column + margin * 2)
                        .frame(maxWidth: .infinity)
                }
                .scrollDismissesKeyboard(.interactively)
                .accessibilityIdentifier("page.lecture")
                // Ask and Study under the notes, not across the notebook beside them.
                .safeAreaInset(edge: .bottom, spacing: 0) { bottomBar(l) }
                .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { columnWidth = $0 }
                .task(id: l == nil) { await openAtMoment(l, proxy, at: moment) }
                .task(id: jump) { if let jump { await openAtMoment(l, proxy, at: jump.at) } }
                #if DEBUG
                // Pictures: `-scroll-to-end` after the page has filled in.
                .task(id: files?.value?.count) {
                    guard DebugLaunch.flag("-scroll-to-end"), files?.value?.isEmpty == false else { return }
                    try? await Task.sleep(for: .seconds(2))
                    withAnimation(nil) { proxy.scrollTo("end-of-page", anchor: .bottom) }
                }
                #endif
            }
            if beside, let l {
                Rectangle().fill(.line).frame(width: 1).ignoresSafeArea()
                HandwritingView(className: filed(l), lecture: (l.id, title(l)), embedded: true)
                    .frame(maxWidth: .infinity)
                    .transition(.move(edge: .trailing))
            }
        }
        .page()
        .foregroundStyle(.ink)
        .titleOnScroll(l.map(title) ?? "", after: 150)
        // A reading room on a phone: the notes get the screen, and the foot of the page is Ask and Study. Only while it
        // shows: a link or a notification that opens another tab from here gets that tab's bar back.
        .toolbar(compact && showing ? .hidden : .automatic, for: .tabBar)
        .hidesRecordBar(compact)
        .onAppear { showing = true }
        .onDisappear { showing = false }
        .recordsInto(l.flatMap(filed))
        .toolbar { toolbar(l) }
        .sheet(isPresented: $picking) {
            if let l { FolderPickerSheet(current: folder(l)) { cls in Task { await move(l, to: cls) } } }
        }
        .confirmationDialog(deleteQuestion(l), isPresented: $confirmingDelete, titleVisibility: .visible) {
            Button("Delete", role: .destructive) { if let l { Task { await delete(l) } } }
            Button("Cancel", role: .cancel) {}
        } message: {
            if let l {
                Text("“\(title(l))” is removed from your library on every computer: its notes, transcript and recording. You can undo it for a few minutes.")
            }
        }
        .fullScreenCover(isPresented: $studying, onDismiss: { pane = .notes }) {
            if let l { StudyView(lectureID: l.id, title: title(l)) }
        }
        .sheet(isPresented: $asking) {
            if let l {
                // A moment in this lecture opens here, on its Transcript; another lecture's is pushed over it.
                AskSheet(scope: .lecture(id: l.id, title: title(l), class: filed(l))) { other, at in
                    if other == l.id { jump = Moment(at: at) } else { app.go(.lecture(other, at: at), tab: app.tab) }
                }
            }
        }
        .onChange(of: app.dismissal) {
            asking = false
            studying = false
            picking = false
        }
        .syftRefreshable { await load() }
        .task {
            guard let api = app.api else { return }
            if lecture == nil {
                lecture = Resource("lecture:\(id)") { try await api.lecture(id) }
                notes = Resource("lecture:\(id):rendered") { try await api.rendered(id) }
                files = Resource("lecture:\(id):files") { try await api.attachments(lecture: id) }
            }
            player.find(id)
            if moment != nil { pane = .transcript }
            await load()
            #if DEBUG
            if DebugLaunch.value("-study") != nil { studying = true }
            if DebugLaunch.value("-ink") != nil { inkBeside = true }
            if DebugLaunch.flag("-move") { picking = true }
            if DebugLaunch.flag("-rename"), let l = lecture?.value { startRenaming(l) }
            if DebugLaunch.flag("-delete") { confirmingDelete = true }
            #endif
        }
        // Notes still being written: asked for again every 15 seconds while the page is open.
        .task(id: l.map(Self.writing) ?? false) {
            guard let l = lecture?.value, Self.writing(l) else { return }
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(15))
                guard !Task.isCancelled else { return }
                await load()
                if let now = lecture?.value, !Self.writing(now) {
                    if now.status == "done" { show(PageToast("Your notes are ready.")) }
                    return
                }
            }
        }
        .onDisappear { player.stop() }
    }

    private func load() async {
        async let a: Void = lecture?.load() ?? ()
        async let b: Void = notes?.load() ?? ()
        async let c: Void = files?.load() ?? ()
        _ = await (a, b, c)
        if let l = lecture?.value {
            if movedTo == folder(l) { movedTo = nil }
            if renamedTo == l.title { renamedTo = nil }
        }
    }

    // MARK: what it is

    /// Its folder as the page shows it: where it was just moved, or where the library says; "Unsorted" for none.
    private func folder(_ l: Lecture) -> String {
        let cls = movedTo ?? l.class ?? ""
        return cls.isEmpty ? "Unsorted" : cls
    }

    /// Its folder, or nil when it's in none (Unsorted).
    private func filed(_ l: Lecture) -> String? {
        let cls = folder(l)
        return cls == "Unsorted" ? nil : cls
    }

    private func title(_ l: Lecture) -> String {
        if let renamedTo { return renamedTo }
        if let t = l.title, !t.isEmpty { return t }
        let cls = folder(l)
        return Nouns.untitled(folder: cls == "Unsorted" ? nil : cls, date: l.date)
    }

    /// "lecture", "meeting" or "recording", by where it's filed.
    private func noun(_ l: Lecture) -> String {
        app.noun(of: filed(l))
    }

    private static func writing(_ l: Lecture) -> Bool { l.status == "queued" || l.status == "working" }

    /// What the student typed while it recorded, when it was recorded on this phone.
    private var typedHere: String {
        recordings.recordings.first { $0.id == id }?.notes.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    // MARK: the page

    @ViewBuilder private func page(_ l: Lecture?) -> some View {
        if let l {
            LazyVStack(alignment: .leading, spacing: 0, pinnedViews: [.sectionHeaders]) {
                header(l)
                Section {
                    switch pane {
                    case .notes: notesPane(l)
                    case .transcript: transcriptPane(l)
                    }
                } header: {
                    tabs(l)
                }
            }
            .padding(.bottom, 32)
        } else if let error = lecture?.error {
            EmptyState(title: error.unreachable ? "Can't reach your library" : "Couldn't open this lecture",
                       detail: error.unreachable ? "This lecture opens when your phone can reach your computer again." : error.detail,
                       actionTitle: "Try again") { Task { await load() } }
                .padding(.top, 24)
        } else {
            StudyLoader(.page, color: .inkSubtle).frame(maxWidth: .infinity, minHeight: 240)
        }
    }

    /// Where it is and what it is, its title, its topics.
    private func header(_ l: Lecture) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            crumb(l)
            titleView(l).padding(.top, 10)
            if !l.topics.isEmpty {
                topics(l.topics).padding(.top, 12)
            }
        }
        .padding(.top, 8)
        .padding(.bottom, 14)
    }

    /// The folder (a link to its page) with the chevron that moves it, then "Lecture, Thursday 15 October at 9:00 AM,
    /// 52 min". On a phone the words go under the folder when they don't fit beside it.
    private func crumb(_ l: Lecture) -> some View {
        let words = [Nouns.capital(noun(l)), Format.noteWhen(l.date, l.seconds)].filter { !$0.isEmpty }.joined(separator: ", ")
        let place = HStack(spacing: 0) {
            let cls = folder(l)
            if cls == "Unsorted" {
                HStack(spacing: 6) {
                    Icon("inbox", size: 15, relativeTo: .subheadline).foregroundStyle(.inkSubtle)
                    Text("Unsorted").textStyle(.crumb).foregroundStyle(.inkMuted)
                }
            } else {
                NavigationLink(value: Route.classDetail(cls)) {
                    HStack(spacing: 8) {
                        Swatch(app.hue(of: cls))
                        Text(cls).font(.atkinson(14, .bold, relativeTo: .subheadline)).foregroundStyle(.ink)
                            .lineLimit(1)
                    }
                    .contentShape(.rect)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("\(cls). Open the class")
            }
            Button { picking = true } label: {
                Icon("expand_more", size: 14, relativeTo: .subheadline)
                    .foregroundStyle(.inkSubtle)
                    .frame(width: 30, height: 30)
                    .contentShape(.rect)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(cls == "Unsorted" ? "Unsorted. Change folder" : "In \(cls). Change folder")
        }
        let when = Text(words).textStyle(.crumb).foregroundStyle(.inkMuted)
        return ViewThatFits(in: .horizontal) {
            HStack(spacing: 4) { place; when.lineLimit(1).fixedSize() }
            VStack(alignment: .leading, spacing: 2) { place; when.fixedSize(horizontal: false, vertical: true) }
        }
        .frame(minHeight: 30)
    }

    /// The title, typed over to rename it (tap it, or ⋯ Rename).
    @ViewBuilder private func titleView(_ l: Lecture) -> some View {
        if let draft = renaming {
            TextField("Title", text: Binding(get: { draft }, set: { new in
                // Return keeps the title.
                if new.contains("\n") {
                    renaming = new.replacingOccurrences(of: "\n", with: "")
                    titleFocused = false
                } else {
                    renaming = new
                }
            }), axis: .vertical)
            .textStyle(.docTitle)
            .foregroundStyle(.ink)
            .focused($titleFocused)
            .submitLabel(.done)
            .padding(.bottom, 4)
            .overlay(alignment: .bottom) { Rectangle().fill(.line).frame(height: 2) }
            .onChange(of: titleFocused) { _, focused in if !focused { Task { await commitRename(l) } } }
        } else {
            Text(title(l))
                .textStyle(.docTitle)
                .foregroundStyle(.ink)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
                .onTapGesture { startRenaming(l) }
                .accessibilityAction(named: "Rename") { startRenaming(l) }
        }
    }

    /// Topics, each a link to what's said about it across the library.
    private func topics(_ topics: [String]) -> some View {
        FlowLayout(spacing: 6) {
            ForEach(Array(topics.enumerated()), id: \.offset) { i, t in
                HStack(spacing: 6) {
                    NavigationLink(value: Route.search(t)) {
                        Text(t).font(.atkinson(13, .medium, relativeTo: .footnote))
                    }
                    .buttonStyle(TopicLinkStyle())
                    .accessibilityHint("Search your notes for it")
                    if i < topics.count - 1 { Text("·").textStyle(.meta).foregroundStyle(.inkSubtle).accessibilityHidden(true) }
                }
            }
        }
    }

    /// Notes | Transcript under a hairline, with Attach on the right (and, on an iPad, Only my notes); it stays at the
    /// top once the page has scrolled past it, so switching never means scrolling back up.
    private func tabs(_ l: Lecture) -> some View {
        TabStrip(selection: Binding(get: { pane }, set: { new in
            pane = new
            if new == .notes { washed = nil }
        }), tabs: [(.notes, "Notes"), (.transcript, "Transcript")])
        .overlay(alignment: .trailing) {
            HStack(spacing: 14) {
                if !narrow, pane == .notes, someMine(l) { onlyMineToggle }
                Button { attach(l) } label: { Label("Attach", icon: "attach_file") }
                    .buttonStyle(.pill(small: true))
                    .accessibilityHint("Add your notes, slides, photos or a handout to this \(noun(l))")
            }
            .padding(.bottom, 4)
        }
        .padding(.top, 6)
        .background { Rectangle().fill(.surface).padding(.horizontal, -margin) }
    }

    private var onlyMineToggle: some View {
        Toggle("Only my notes", isOn: $onlyMine.animation(.snappy)).toggleStyle(.ink)
            .accessibilityHint("Show only what you wrote, without what was added from the recording")
    }

    // MARK: notes

    @ViewBuilder private func notesPane(_ l: Lecture) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if let html = noteHTML, !html.isEmpty, !Self.writing(l) {
                legend(l)
                NotesView(html: shown(html, l), provenance: true, onlyMine: onlyMine && someMine(l))
                    .task(id: "\(html.hashValue)-\(files?.value?.count ?? -1)") { await inlinePhotos(html) }
                    .padding(.top, 14)
            } else {
                unwritten(l)
            }
            attached(l)
            Color.clear.frame(height: 1).id("end-of-page")
        }
        .padding(.top, 18)
    }

    /// The notes with the lecture's own embedded photos in them (fetched through the app's session), or, until
    /// they're in, without.
    private func shown(_ html: String, _ l: Lecture) -> String {
        guard !NotePhotos.ids(in: html).isEmpty else { return html }
        if let p = photoNotes, p.source == html { return p.html }
        return NotePhotos.strip(html)
    }

    private func inlinePhotos(_ html: String) async {
        guard let api = app.api, !NotePhotos.ids(in: html).isEmpty else { return }
        let mine = Set((files?.value ?? []).filter { $0.type.hasPrefix("image/") }.map(\.id))
        let out = await NotePhotos.inline(html, allowed: mine) { id in
            guard let (data, _) = try? await AppModel.session.data(for: URLRequest(url: api.attachmentURL(id), timeoutInterval: 120)) else { return nil }
            return await Task.detached { NotePhotos.small(data) }.value
        }
        photoNotes = (html, out)
    }

    /// The notes' marks: whose words are whose.
    private func hasMine(_ l: Lecture) -> Bool { TypedMarks.has(noteMarkdown(l)) }
    private func allMine(_ l: Lecture) -> Bool { TypedMarks.allMine(noteMarkdown(l)) }

    /// The notes as the library renders them for the phone (or, in a debug build with `-sample-notes`, a lecture's
    /// worth that shows every kind of block), and as they were written (their marks say whose words are whose).
    private var noteHTML: String? {
        #if DEBUG
        if DebugLaunch.flag("-sample-notes") { return SampleNotes.html }
        #endif
        return notes?.value?.html
    }

    private func noteMarkdown(_ l: Lecture) -> String {
        #if DEBUG
        if DebugLaunch.flag("-sample-notes") { return SampleNotes.markdown }
        #endif
        return l.notes
    }
    private func someMine(_ l: Lecture) -> Bool { hasMine(l) && !allMine(l) }

    /// "What you wrote" in ink and "Added from the recording" in grey; with nothing of theirs, "Written from the
    /// recording". In a phone-wide column, Only my notes sits at its end.
    @ViewBuilder private func legend(_ l: Lecture) -> some View {
        let mine = hasMine(l), all = allMine(l)
        let yours = LegendItem(text: "What you wrote", mine: true)
        let added = LegendItem(text: mine ? "Added from the recording" : "Written from the recording", mine: false)
        if mine && !all && narrow {
            HStack(alignment: .top, spacing: 12) {
                VStack(alignment: .leading, spacing: 6) { yours; added }
                Spacer(minLength: 8)
                onlyMineToggle
            }
        } else {
            FlowLayout(spacing: 18) {
                if mine { yours }
                if !all { added }
            }
        }
    }

    /// No notes to show: being written (what the student typed here, then the transcript), failed (Try again), or
    /// none will be (the library's reason); the transcript under each.
    @ViewBuilder private func unwritten(_ l: Lecture) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            if Self.writing(l) {
                SyftLoader(.pathTrace, height: 88)
            } else if l.status == "failed" {
                SyftMark(height: 88, oddOneOut: true)
            }
            Text(unwrittenWords(l))
                .textStyle(.summary)
                .foregroundStyle(.inkMuted)
                .fixedSize(horizontal: false, vertical: true)
            if l.status == "failed" {
                Button("Try again") { Task { await rewrite(l) } }.buttonStyle(.pill(small: true))
            }
            let typed = typedHere
            if !typed.isEmpty {
                LegendItem(text: "What you wrote", mine: true).padding(.top, 4)
                NotesView(html: TypedMarks.html(typed), provenance: true)
            }
            if !l.transcript.isEmpty {
                Text("Transcript").textStyle(.h2).foregroundStyle(.ink).padding(.top, 10).accessibilityAddTraits(.isHeader)
                TranscriptView(text: l.transcript, playable: player.available, playing: playingLine(l)) { s in play(s) }
            }
        }
    }

    private func unwrittenWords(_ l: Lecture) -> String {
        let below = l.transcript.isEmpty ? "" : " The transcript is below."
        if Self.writing(l) {
            return "Your notes are being written on \(app.libraryName). This page fills in when they're ready."
        }
        if l.status == "failed" {
            let why = (l.error ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            return why.isEmpty ? "Notes couldn't be written." : "Notes couldn't be written: \(why)\(why.hasSuffix(".") ? "" : ".")"
        }
        if let why = l.noNotesWhy, !why.isEmpty { return why + below }
        if let e = l.error?.trimmingCharacters(in: .whitespaces), !e.isEmpty { return e + (e.hasSuffix(".") ? "" : ".") + below }
        return "There are no notes for this \(noun(l))." + below
    }

    /// What's attached to it, at the end of the notes: files, then notebooks written for it on this phone.
    @ViewBuilder private func attached(_ l: Lecture) -> some View {
        let everything = files?.value ?? []
        let list = everything.filter { !$0.isWhiteboard }
        let boards = WhiteboardStrip.ordered(everything)
        let books = store.notebooks.filter { $0.lectureID == l.id }
        if !boards.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                Text("Whiteboard").textStyle(.h2).foregroundStyle(.ink).accessibilityAddTraits(.isHeader)
                WhiteboardStrip(photos: boards)
            }
            .padding(.top, 36)
        }
        if !list.isEmpty || !books.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                Text("Attached").textStyle(.h2).foregroundStyle(.ink).accessibilityAddTraits(.isHeader)
                if !list.isEmpty { AttachmentList(files: list) }
                ForEach(books) { book in
                    Button { app.incoming = .handwriting(className: filed(l), lecture: (l.id, title(l))) } label: {
                        HStack(spacing: 12) {
                            Icon("edit_note", size: 18).foregroundStyle(.inkMuted)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(book.title).textStyle(.nav).foregroundStyle(.ink).lineLimit(1)
                                Text(book.needsSaving ? "Not saved to your library" : "Saved to your library")
                                    .textStyle(.micro).foregroundStyle(.inkMuted)
                            }
                            Spacer(minLength: 0)
                            Icon("chevron_right", size: 14).foregroundStyle(.inkSubtle)
                        }
                        .padding(.vertical, 8)
                    }
                    .buttonStyle(.row)
                }
            }
            .padding(.top, 36)
        }
    }

    // MARK: transcript

    @ViewBuilder private func transcriptPane(_ l: Lecture) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if l.transcript.isEmpty {
                Text(Self.writing(l) ? "The transcript shows here once your library has it." : "There's no transcript for this \(noun(l)).")
                    .textStyle(.summary).foregroundStyle(.inkMuted)
            } else {
                TranscriptView(text: l.transcript, washed: washed, playable: player.available, playing: playingLine(l)) { s in play(s) }
            }
        }
        .padding(.top, 18)
    }

    /// Opened at a moment: the transcript, scrolled so that line sits a third of the way down, washed for 2 seconds.
    private func openAtMoment(_ l: Lecture?, _ proxy: ScrollViewProxy, at: Double?) async {
        guard let l, let at, let line = TranscriptView.line(at: at, in: l.transcript) else { return }
        pane = .transcript
        washed = line
        try? await Task.sleep(for: .milliseconds(350))
        if reduceMotion { proxy.scrollTo(TranscriptView.rowID(line), anchor: UnitPoint(x: 0.5, y: 0.33)) } else {
            withAnimation(.smooth) { proxy.scrollTo(TranscriptView.rowID(line), anchor: UnitPoint(x: 0.5, y: 0.33)) }
        }
        try? await Task.sleep(for: .seconds(2))
        if reduceMotion { washed = nil } else { withAnimation(.easeOut(duration: 0.6)) { washed = nil } }
    }

    /// The line the recording is playing, while it plays.
    private func playingLine(_ l: Lecture) -> Int? {
        player.playing ? TranscriptView.line(at: player.time, in: l.transcript) : nil
    }

    private func play(_ seconds: Double) {
        guard !recorder.isActive else {
            show(PageToast("Stop recording to play this back."))
            return
        }
        player.play(from: seconds)
    }

    // MARK: the bar at the foot

    @ViewBuilder private func bottomBar(_ l: Lecture?) -> some View {
        VStack(spacing: 10) {
            if let toast {
                Toast(toast.text, actionTitle: toast.actionTitle, action: toast.action.map { act in { act(); self.toast = nil } })
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
            if recorder.isActive { recordingPill }
            if player.started { playerPill }
            if let l {
                HStack(spacing: 10) {
                    AskBarButton(placeholder: "Ask about this \(noun(l))", scope: compact ? nil : "This \(noun(l))") { asking = true }
                        .accessibilityLabel("Ask about this \(noun(l))")
                    Button { studying = true } label: { Label("Study", icon: "flashcards") }
                        .buttonStyle(StudyButtonStyle())
                        .accessibilityHint("Flashcards and a quiz from these notes")
                }
            }
        }
        .padding(.horizontal, margin)
        .padding(.top, 8)
        .padding(.bottom, compact ? 4 : 12)
        .frame(maxWidth: column + margin * 2)
        .frame(maxWidth: .infinity)
        .animation(.smooth, value: toast?.id)
    }

    /// Something's recording (on any page): its dot and time, a tap from the recording.
    private var recordingPill: some View {
        Button { app.record(for: app.recordClass) } label: {
            HStack(spacing: 8) {
                BreathingDot(paused: recorder.phase == .paused)
                Text(recorder.phase == .paused ? "Paused" : "Recording").textStyle(.buttonSmall).foregroundStyle(.ink)
                Text(Format.clock(recorder.elapsed)).textStyle(.buttonSmall).fontWeight(.medium).foregroundStyle(.inkMuted)
            }
            .padding(.horizontal, 14)
            .frame(height: 34)
            .background(.surface, in: .capsule)
            .lifted(.float, in: Capsule())
            .contentShape(.capsule)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Recording, \(Format.clock(recorder.elapsed)). Open the recording")
    }

    /// The recording made on this phone, playing from a line of the transcript.
    private var playerPill: some View {
        HStack(spacing: 4) {
            Button { player.toggle() } label: {
                Icon(player.playing ? "pause" : "play_arrow", size: 16, filled: true)
                    .foregroundStyle(.ink)
                    .frame(width: 34, height: 34)
                    .contentShape(.rect)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(player.playing ? "Pause" : "Play")
            Text(Format.clock(player.time)).textStyle(.buttonSmall).fontWeight(.medium).foregroundStyle(.inkMuted)
                .monospacedDigit()
            Button { player.stop() } label: {
                Icon("close", size: 15).foregroundStyle(.inkMuted).frame(width: 34, height: 34).contentShape(.rect)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Stop playing")
        }
        .padding(.horizontal, 6)
        .background(.surface, in: .capsule)
        .lifted(.float, in: Capsule())
    }

    // MARK: the toolbar

    @ToolbarContentBuilder private func toolbar(_ l: Lecture?) -> some ToolbarContent {
        if let l {
            if !compact {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { withAnimation(.smooth) { inkBeside.toggle() } } label: {
                        Label(inkBeside ? "Close notebook" : "Write alongside", icon: inkBeside ? "dock_to_right" : "edit_note")
                    }
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                ShareLink(item: shareText(l), subject: Text(title(l))) { Label("Share", icon: "ios_share") }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button("Move to…", icon: "drive_file_move") { picking = true }
                    Button("Rename", icon: "edit") { startRenaming(l) }
                    Button("Write notes", icon: "edit_note") {
                        app.incoming = .handwriting(className: filed(l), lecture: (l.id, title(l)))
                    }
                    Divider()
                    Button("Delete", icon: "delete", role: .destructive) { confirmingDelete = true }
                } label: {
                    Label("More", icon: "more_horiz")
                }
            }
        }
    }

    private func shareText(_ l: Lecture) -> String {
        let body = l.notes.isEmpty ? l.transcript : TypedMarks.plain(l.notes)
        return "# \(title(l))\n\(Nouns.capital(noun(l))), \(Format.noteWhen(l.date, l.seconds))\n\n\(body)"
    }

    private func deleteQuestion(_ l: Lecture?) -> String { "Delete this \(l.map(noun) ?? "lecture")?" }

    // MARK: doing things

    private func attach(_ l: Lecture) {
        app.incoming = .attach(className: filed(l), lecture: (l.id, title(l)))
    }

    private func show(_ t: PageToast) {
        toast = t
        let shown = t.id
        Task {
            try? await Task.sleep(for: .seconds(t.action == nil ? 3 : 5))
            if toast?.id == shown { toast = nil }
        }
    }

    /// Filed somewhere else, at once, with Undo.
    private func move(_ l: Lecture, to cls: String, undoing: Bool = false) async {
        let was = folder(l)
        guard cls != was, let api = app.api else { return }
        movedTo = cls
        do {
            try await LectureCalls.move(api, id: l.id, to: cls)
            show(PageToast(undoing ? "Moved back to \(cls)." : "Moved to \(cls).",
                           actionTitle: undoing ? nil : "Undo",
                           action: undoing ? nil : { Task { await move(l, to: was, undoing: true) } }))
            await lecture?.load()
        } catch {
            movedTo = nil
            show(PageToast(LectureCalls.words(error)))
        }
    }

    private func startRenaming(_ l: Lecture) {
        renaming = title(l)
        Task {
            // Once the field is on the page.
            try? await Task.sleep(for: .milliseconds(80))
            titleFocused = true
        }
    }

    /// The title typed is kept (an empty one, or the same, changes nothing).
    private func commitRename(_ l: Lecture) async {
        guard let draft = renaming else { return }
        renaming = nil
        let new = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !new.isEmpty, new != title(l), let api = app.api else { return }
        renamedTo = new
        do {
            try await LectureCalls.rename(api, id: l.id, title: new)
            await lecture?.load()
        } catch {
            renamedTo = nil
            show(PageToast(LectureCalls.words(error)))
        }
    }

    /// Deleted: back to where the student came from, with Undo for the few minutes the library keeps it.
    private func delete(_ l: Lecture) async {
        guard let api = app.api else { return }
        let name = title(l)
        do {
            try await LectureCalls.delete(api, id: l.id)
            player.stop()
            dismiss()
            // FLOWS: an app-level toast would say this on the page underneath; until the app has one, it floats.
            FloatingToast.show("Deleted “\(name)”", actionTitle: "Undo") {
                Task { try? await LectureCalls.restore(api, id: l.id) }
            }
        } catch {
            show(PageToast(LectureCalls.words(error)))
        }
    }

    private func rewrite(_ l: Lecture) async {
        guard let api = app.api else { return }
        do {
            try await LectureCalls.rewrite(api, id: l.id)
            await load()
        } catch {
            show(PageToast(LectureCalls.words(error)))
        }
    }
}

// MARK: - The library's lecture calls

/// Moving, renaming, deleting and rewriting a lecture, as the library takes them (`/api/v2/lectures/{id}/…`).
// FLOWS: the API client is getting move(lecture:to:), rename(lecture:title:), delete(lecture:), restore(lecture:) and
// rewrite(lecture:) on iphone-look; once they're merged, these call them.
private enum LectureCalls {
    private struct Class: Encodable { var `class`: String }
    private struct Title: Encodable { var title: String }
    private struct Nothing: Encodable {}
    private struct Reply: Decodable {}

    static func move(_ api: APIClient, id: String, to cls: String) async throws {
        let _: Reply = try await api.post("/lectures/\(APIClient.segment(id))/class", Class(class: cls))
    }

    static func rename(_ api: APIClient, id: String, title: String) async throws {
        let _: Reply = try await api.post("/lectures/\(APIClient.segment(id))/title", Title(title: title))
    }

    static func delete(_ api: APIClient, id: String) async throws {
        try await api.delete("/lectures/\(APIClient.segment(id))")
    }

    static func restore(_ api: APIClient, id: String) async throws {
        let _: Reply = try await api.post("/lectures/\(APIClient.segment(id))/restore", Nothing())
    }

    static func rewrite(_ api: APIClient, id: String) async throws {
        let _: Reply = try await api.post("/lectures/\(APIClient.segment(id))/rewrite", Nothing())
    }

    /// What went wrong, in the library's words when it gave some.
    static func words(_ error: Error) -> String {
        (error as? APIError)?.detail ?? "Something went wrong. Try again in a moment."
    }
}

// MARK: - Whose words are whose

/// The student's own words in a lecture's notes, as the desktop reads them (TypedNotes): what they typed is marked
/// `==like this==`; everything else was written from the recording.
enum TypedMarks {
    private static let marked = try! NSRegularExpression(pattern: #"==(?=\S)(.+?)(?<=\S)=="#)
    private static let lead = try! NSRegularExpression(pattern: #"^\s*(?:#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s+)*"#)

    /// Something in the notes is the student's own.
    static func has(_ markdown: String) -> Bool {
        marked.firstMatch(in: markdown, range: NSRange(markdown.startIndex..., in: markdown)) != nil
    }

    /// The notes without the marks.
    static func unmarked(_ markdown: String) -> String {
        marked.stringByReplacingMatches(in: markdown, range: NSRange(markdown.startIndex..., in: markdown), withTemplate: "$1")
    }

    /// Every word is the student's (what they typed, with nothing written from the recording).
    static func allMine(_ markdown: String) -> Bool {
        guard has(markdown) else { return false }
        let lines = markdown.replacingOccurrences(of: "\r\n", with: "\n")
        return unmarked(lines).components(separatedBy: "\n").map(mine).joined(separator: "\n") == lines
    }

    /// A line with its words marked as the student's, its heading or bullet left outside the marks.
    private static func mine(_ line: String) -> String {
        let ns = line as NSString
        let leadLength = lead.firstMatch(in: line, range: NSRange(location: 0, length: ns.length))?.range.length ?? 0
        let head = ns.substring(to: leadLength)
        let words = ns.substring(from: leadLength).trimmingCharacters(in: .whitespaces)
        let t = line.trimmingCharacters(in: .whitespaces)
        let notWords = t.hasPrefix("|") || t.hasPrefix("```") || t.hasPrefix("$$") || t.hasPrefix("![") || ["---", "***"].contains(t)
            || (t.hasPrefix("[") && t.hasSuffix("]"))
        if words.isEmpty || notWords { return line }
        return head + "==" + words + "=="
    }

    /// Notes as text to share: no marks, and the demo library's HTML as plain lines.
    static func plain(_ notes: String) -> String {
        var s = unmarked(notes)
        guard s.contains("</") else { return s }
        for (pattern, with) in [(#"</(p|h\d|li|tr)>"#, "\n"), (#"<li>"#, "- "), (#"<h\d>"#, "\n## "), (#"<[^>]+>"#, "")] {
            s = s.replacingOccurrences(of: pattern, with: with, options: .regularExpression)
        }
        return s.replacingOccurrences(of: "&amp;", with: "&").replacingOccurrences(of: "&lt;", with: "<").replacingOccurrences(of: "&gt;", with: ">")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// What the student typed while recording, as notes that are all theirs: "# " a heading, "- " a bullet, "1. " a
    /// numbered line, anything else a paragraph.
    static func html(_ typed: String) -> String {
        func esc(_ s: String) -> String {
            s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;")
        }
        var out = "", list: String?
        func close() { if let l = list { out += "</\(l)>"; list = nil } }
        for raw in typed.components(separatedBy: .newlines) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty { close(); continue }
            if let r = line.range(of: #"^#{1,6}\s+"#, options: .regularExpression) {
                close()
                out += "<h2><span class=\"mine\">\(esc(String(line[r.upperBound...])))</span></h2>"
            } else if let r = line.range(of: #"^([-*+]|\d+[.)])\s+"#, options: .regularExpression) {
                let kind = line.first?.isNumber == true ? "ol" : "ul"
                if list != kind { close(); out += "<\(kind)>"; list = kind }
                out += "<li><span class=\"mine\">\(esc(String(line[r.upperBound...])))</span></li>"
            } else {
                close()
                out += "<p><span class=\"mine\">\(esc(line))</span></p>"
            }
        }
        close()
        return out
    }
}

/// One side of the line over the notes: a short bar, then whose words these are.
private struct LegendItem: View {
    var text: String
    var mine: Bool

    var body: some View {
        HStack(spacing: 6) {
            RoundedRectangle(cornerRadius: 2).fill(mine ? Color.ink : .inkMuted).frame(width: 14, height: 3)
            Text(text).textStyle(.legend).fontWeight(mine ? .bold : .medium).foregroundStyle(mine ? Color.ink : .inkMuted)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - The transcript

/// The transcript, a line at a time as the desktop shows it: the time down the side (a button that plays the recording
/// from there, when it was recorded on this phone), then what was said. Where voices were told apart, a new voice's
/// line says who ("Speaker 2"). A line without a time joins the one before it.
struct TranscriptView: View {
    var text: String
    /// The line opened at, washed.
    var washed: Int? = nil
    var playable = false
    /// The line playing now.
    var playing: Int? = nil
    var onTime: ((Double) -> Void)? = nil

    struct Line: Hashable {
        var start: Double?
        var stamp: String
        var speaker: String?
        var words: String
    }

    var body: some View {
        let lines = Self.parse(text)
        if text.isEmpty {
            EmptyText("There's no transcript yet.")
        } else {
            LazyVStack(alignment: .leading, spacing: 10) {
                ForEach(Array(lines.enumerated()), id: \.offset) { i, line in
                    row(line, here: washed == i || playing == i)
                        .id(Self.rowID(i))
                }
            }
        }
    }

    private func row(_ line: Line, here: Bool) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            if !line.stamp.isEmpty {
                if playable, let start = line.start, let onTime {
                    Button { onTime(start) } label: {
                        Text(line.stamp).font(.atkinson(12, .medium, relativeTo: .caption)).foregroundStyle(.ink)
                            .underline(color: .line)
                    }
                    .buttonStyle(.plain)
                    .frame(width: 52, alignment: .leading)
                    .accessibilityLabel("Play from \(line.stamp)")
                } else {
                    Text(line.stamp).font(.atkinson(12, .medium, relativeTo: .caption)).foregroundStyle(.inkSubtle)
                        .frame(width: 52, alignment: .leading)
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                if let speaker = line.speaker {
                    Text(speaker).textStyle(.meta).fontWeight(.bold).foregroundStyle(.ink)
                }
                Text(line.words)
                    .font(.atkinson(16, relativeTo: .body))
                    .lineSpacing(5.5)
                    .foregroundStyle(.ink)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(here ? Color.hover : .clear, in: .rect(cornerRadius: 6))
        .padding(.horizontal, -8)
        .accessibilityElement(children: .combine)
    }

    static func rowID(_ i: Int) -> String { "transcript-line-\(i)" }

    /// The line said at `seconds`: the last to start at or before it.
    static func line(at seconds: Double, in text: String) -> Int? {
        let lines = parse(text)
        guard !lines.isEmpty else { return nil }
        return lines.lastIndex { ($0.start ?? 0) <= seconds + 0.5 } ?? 0
    }

    /// The transcript's lines: "[mm:ss] words" or "[h:mm:ss] words", a line with no time joined to the one before; a
    /// transcript with no times at all, a paragraph a line.
    static func parse(_ text: String) -> [Line] {
        var out: [Line] = []
        for raw in text.split(separator: "\n") {
            let s = raw.trimmingCharacters(in: .whitespaces)
            if s.isEmpty { continue }
            if let m = s.firstMatch(of: /^\[(?:(\d+):)?(\d{1,3}):(\d{2})\]\s?(.*)$/) {
                let seconds = Double(m.1.flatMap { Int($0) } ?? 0) * 3600 + Double(Int(m.2) ?? 0) * 60 + Double(Int(m.3) ?? 0)
                var words = String(m.4).trimmingCharacters(in: .whitespaces)
                var speaker: String?
                if let who = words.firstMatch(of: /^(Speaker \d+):\s*/) {
                    speaker = String(who.1)
                    words = String(words[who.range.upperBound...])
                }
                if words.isEmpty { continue }
                out.append(Line(start: seconds, stamp: Recording.stamp(seconds), speaker: speaker, words: words))
            } else if !out.isEmpty, out[out.count - 1].start != nil {
                out[out.count - 1].words += " " + s
            } else {
                out.append(Line(start: nil, stamp: "", speaker: nil, words: s))
            }
        }
        return out
    }

    /// Each timed line's time as written and its words (lines without one left out).
    nonisolated static func lines(_ text: String) -> [(stamp: String, words: String)] {
        text.split(separator: "\n").compactMap { raw in
            let s = raw.trimmingCharacters(in: .whitespaces)
            guard let m = s.firstMatch(of: /^\[(\d{1,2}:\d{2}(?::\d{2})?)\]\s*(.*)$/), !m.2.isEmpty else { return nil }
            return (String(m.1), String(m.2))
        }
    }
}

/// The recording of a lecture made on this phone (kept beside it), played from a line of its transcript.
@MainActor @Observable
private final class TranscriptPlayer {
    private(set) var url: URL?
    private(set) var started = false
    private(set) var playing = false
    private(set) var time: Double = 0
    @ObservationIgnored private var player: AVAudioPlayer?
    @ObservationIgnored private var ticker: Task<Void, Never>?

    var available: Bool { url != nil }

    func find(_ id: String) {
        let store = RecordingStore.shared
        guard let r = store.recordings.first(where: { $0.id == id }), let file = store.audioURL(r),
              FileManager.default.fileExists(atPath: file.path) else { return }
        url = file
    }

    func play(from seconds: Double) {
        guard let url else { return }
        if player == nil {
            try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio)
            try? AVAudioSession.sharedInstance().setActive(true)
            player = try? AVAudioPlayer(contentsOf: url)
        }
        guard let player else { return }
        player.currentTime = min(seconds, max(0, player.duration - 1))
        player.play()
        started = true
        playing = true
        tick()
    }

    func toggle() {
        guard let player else { return }
        if player.isPlaying { player.pause(); playing = false } else { player.play(); playing = true; tick() }
    }

    func stop() {
        ticker?.cancel()
        player?.stop()
        player = nil
        if started { try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation) }
        started = false
        playing = false
    }

    private func tick() {
        ticker?.cancel()
        ticker = Task { [weak self] in
            while !Task.isCancelled {
                guard let self, let p = self.player else { return }
                self.time = p.currentTime
                if !p.isPlaying { self.playing = false; return }
                try? await Task.sleep(for: .milliseconds(250))
            }
        }
    }
}

// MARK: - Pieces of the page

/// A short message at the foot of the page, maybe with Undo.
private struct PageToast {
    let id = UUID()
    var text: String
    var actionTitle: String?
    var action: (() -> Void)?

    init(_ text: String, actionTitle: String? = nil, action: (() -> Void)? = nil) {
        self.text = text
        self.actionTitle = actionTitle
        self.action = action
    }
}

/// A topic: muted words over the line colour's underline, ink while pressed (a link, no accent).
private struct TopicLinkStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(configuration.isPressed ? Color.ink : .inkMuted)
            .padding(.bottom, 1)
            .overlay(alignment: .bottom) { Rectangle().fill(configuration.isPressed ? Color.ink : .line).frame(height: 1.5) }
            .contentShape(.rect)
    }
}

/// Study, beside the Ask bar: a white pill on the same shadow, as tall as the bar.
private struct StudyButtonStyle: ButtonStyle {
    @ScaledMetric(relativeTo: .body) private var icon: CGFloat = 16
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .labelStyle(StudyLabel(size: icon))
            .textStyle(.button)
            .foregroundStyle(.ink)
            .padding(.horizontal, 18)
            .frame(height: 52)
            .background(.surface, in: .capsule)
            .overlay { if configuration.isPressed { Capsule().fill(.press) } }
            .lifted(.float, in: Capsule())
            .contentShape(.capsule)
    }

    private struct StudyLabel: LabelStyle {
        var size: CGFloat
        func makeBody(configuration: Configuration) -> some View {
            HStack(spacing: 8) {
                configuration.icon.scaleEffect(size / 24).frame(width: size, height: size)
                configuration.title
            }
        }
    }
}

/// The red dot of a recording, one cell of the pixel s (SyftCellShape), breathing while it records (a still ring when
/// paused; still for less motion).
private struct BreathingDot: View {
    var paused: Bool
    @Environment(\.accessibilityReduceMotion) private var still
    @State private var dim = false

    var body: some View {
        SyftCellShape()
            .fill(paused ? Color.clear : .record)
            .overlay { if paused { SyftCellShape().strokeBorder(.record, lineWidth: 2) } }
            .frame(width: 10, height: 10)
            .opacity(dim && !paused && !still ? 0.35 : 1)
            .animation(paused || still ? nil : .easeInOut(duration: 0.8).repeatForever(autoreverses: true), value: dim)
            .onAppear { dim = true }
            .accessibilityHidden(true)
    }
}

// MARK: - Moving a lecture

/// Where a lecture goes (the desktop's folder picker): a search that narrows the list as it's typed, Unsorted, then
/// every folder under its group's name with its swatch and how many it holds, a check on the one it's in. A pick moves
/// it at once.
// FLOWS: the library lane's Home row needs the same picker; this belongs in Components.swift as a shared piece.
private struct FolderPickerSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    var current: String
    var pick: (String) -> Void
    @State private var query = ""
    @FocusState private var searching: Bool

    /// Classes first, then each other group in the order the library has them.
    private var groupNames: [String] {
        var names = [""]
        for c in app.classes where !names.contains(c.group ?? "") { names.append(c.group ?? "") }
        return names
    }

    var body: some View {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        let classes = app.classes.filter { q.isEmpty || $0.name.lowercased().contains(q) || ($0.code ?? "").lowercased().contains(q) }
        let groups = groupNames.map { g in (label: g, items: classes.filter { ($0.group ?? "") == g }) }.filter { !$0.items.isEmpty }
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    HStack(spacing: 8) {
                        Icon("search", size: 16).foregroundStyle(.inkSubtle)
                        TextField("", text: $query, prompt: Text("Move to folder").foregroundStyle(.inkSubtle))
                            .textStyle(.body)
                            .focused($searching)
                            .submitLabel(.done)
                            .onSubmit { if let only = classes.first, classes.count == 1 { choose(only.name) } }
                    }
                    .padding(.horizontal, 12)
                    .frame(height: 40)
                    .background(.surfaceSunken, in: .rect(cornerRadius: Radius.control))
                    .overlay(RoundedRectangle(cornerRadius: Radius.control).strokeBorder(.line))
                    .padding(.bottom, 12)

                    if q.isEmpty || "unsorted".contains(q) {
                        row(name: "Unsorted", count: nil) {
                            Icon("inbox", size: 16).foregroundStyle(.inkSubtle).frame(width: 16)
                        }
                    }
                    ForEach(groups, id: \.label) { group in
                        GroupLabel(group.label.isEmpty ? app.words.classes : group.label)
                            .padding(.top, 16)
                            .padding(.bottom, 4)
                        ForEach(group.items) { c in
                            row(name: c.name, count: c.lectures) { Swatch(app.hue(of: c.name)).frame(width: 16) }
                        }
                    }
                    if classes.isEmpty && !q.isEmpty && !"unsorted".contains(q) {
                        EmptyText("No folder is called “\(query)”.").padding(.top, 8)
                    }
                }
                .padding(.horizontal, Space.pageCompact)
                .padding(.vertical, 8)
            }
            .page()
            .navigationTitle("Move to")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button { dismiss() } label: { Text("Cancel").textStyle(.nav) }
                }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationBackground(.surface)
    }

    private func row<Mark: View>(name: String, count: Int?, @ViewBuilder mark: () -> Mark) -> some View {
        let here = name == current
        return Button { choose(name) } label: {
            HStack(spacing: 10) {
                mark()
                Text(name).textStyle(.nav).fontWeight(here ? .bold : .medium).foregroundStyle(.ink).lineLimit(1)
                Spacer(minLength: 8)
                if here {
                    Icon("check", size: 16).foregroundStyle(.inkSubtle)
                } else if let count {
                    Text("\(count)").textStyle(.meta).foregroundStyle(.inkSubtle)
                }
            }
            .padding(.horizontal, 12)
            .frame(minHeight: 44)
            .background(here ? Color.hover : .clear, in: .rect(cornerRadius: Radius.control))
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(here ? [.isSelected] : [])
    }

    private func choose(_ name: String) {
        dismiss()
        if name != current { pick(name) }
    }
}

// MARK: - A message that outlives the page

/// A toast in a window of its own over the app, so it's still there after the page that showed it has gone (a deleted
/// lecture's "Undo"). Touches pass through everything but the toast.
// FLOWS: replace with the app's own toast when AppModel gets one.
@MainActor
private enum FloatingToast {
    private static var window: UIWindow?
    private static var shown: UUID?

    static func show(_ text: String, actionTitle: String, action: @escaping () -> Void) {
        guard let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first(where: { $0.activationState == .foregroundActive })
            ?? UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first else { return }
        let key = scene.windows.first { $0.isKeyWindow }
        let id = UUID()
        shown = id
        let view = Toast(text, actionTitle: actionTitle) {
            action()
            hide(id)
        }
        .padding(.horizontal, 20)
        .padding(.bottom, 24)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
        let host = UIHostingController(rootView: view)
        host.view.backgroundColor = .clear
        let w = window ?? PassThroughWindow(windowScene: scene)
        w.windowLevel = .alert + 1
        w.overrideUserInterfaceStyle = key?.traitCollection.userInterfaceStyle ?? .unspecified
        w.rootViewController = host
        w.isHidden = false
        window = w
        Task {
            try? await Task.sleep(for: .seconds(5))
            hide(id)
        }
    }

    private static func hide(_ id: UUID) {
        guard shown == id else { return }
        shown = nil
        window?.isHidden = true
        window?.rootViewController = nil
    }

    private final class PassThroughWindow: UIWindow {
        override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? {
            let hit = super.hitTest(point, with: event)
            return hit === rootViewController?.view ? nil : hit
        }
    }
}

#if DEBUG
// MARK: - A lecture's worth of notes, for pictures

/// Notes that show every kind of block the library writes, as `/lectures/{id}/rendered` sends them (Markdig's HTML,
/// the student's lines still ==marked==, formulas ready for KaTeX, a flowchart drawn as the library draws it):
/// `-sample-notes` puts them on any lecture's page, to see the type before the demo library has such a lecture.
enum SampleNotes {
    static let markdown = """
    ## Summary
    Integration by parts turns the integral of a product into an easier one.
    ## ==Product rule backwards==
    - ==start from d(uv)== Differentiate a product, then integrate both sides.
    - ==pick u so du is simpler== The method works when the second integral is easier than the first.
    ## Choosing u
    - ==LIATE: logs first== Logarithmic, inverse trig, algebraic, trig, exponential.
    ## ==To do==
    - ==problems 7.1: 3, 9, 15 before Friday==
    """

    static let html = #"""
    <h2>Summary</h2>
    <p>Integration by parts turns the integral of a product into an easier one by running the product rule backwards. Most of the skill is choosing which factor to call <em>u</em>, so that what's left is simpler than what you started with.</p>
    <h2>==Product rule backwards==</h2>
    <ul>
    <li>==start from d(uv)== Differentiate a product, then integrate both sides and rearrange: <div class="math display" data-tex="\int u\,dv = uv - \int v\,du">∫ u dv = uv − ∫ v du</div></li>
    <li>==pick u so du is simpler== The method only helps when <span class="math" data-tex="\int v\,du">∫ v du</span> is easier than the integral you began with.</li>
    <li>The constant of integration waits until the very end: adding it to <span class="math" data-tex="v">v</span> early cancels out.</li>
    </ul>
    <h2>Choosing <em>u</em>: LIATE</h2>
    <ul>
    <li>==LIATE: logs first== <strong>L</strong>ogarithmic, <strong>I</strong>nverse trig, <strong>A</strong>lgebraic, <strong>T</strong>rig, <strong>E</strong>xponential: whichever comes first is <em>u</em>.</li>
    <li>It's a habit, not a law. For <span class="math" data-tex="\int e^x \sin x\,dx">∫ eˣ sin x dx</span> either choice works, and the integral comes back to itself.</li>
    </ul>
    <figure class="diagram"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 340 300" width="340" height="300" font-family="&quot;Helvetica Neue&quot;, &quot;Segoe UI&quot;, Arial, sans-serif" role="img">
      <title>An integral of a product, Is one factor a polynomial?, Tabular method, LIATE</title>
      <rect width="340" height="300" fill="#FFFFFF"/>
      <path d="M170,54 L170,92" fill="none" stroke="#6E6E73" stroke-width="1.25" stroke-linecap="round" stroke-linejoin="round"/>
      <polygon points="165.5,86 170,94 174.5,86" fill="#6E6E73"/>
      <path d="M130,150 L84,210" fill="none" stroke="#6E6E73" stroke-width="1.25" stroke-linecap="round" stroke-linejoin="round"/>
      <polygon points="82.2,202.6 80.6,214.6 89.6,207.4" fill="#6E6E73"/>
      <path d="M210,150 L256,210" fill="none" stroke="#6E6E73" stroke-width="1.25" stroke-linecap="round" stroke-linejoin="round"/>
      <polygon points="250.4,207.4 259.4,214.6 257.8,202.6" fill="#6E6E73"/>
      <rect x="86" y="168" width="28" height="18" rx="3" fill="#FFFFFF"/>
      <text x="100" y="181" font-size="12" text-anchor="middle" fill="#6E6E73">yes</text>
      <rect x="228" y="168" width="22" height="18" rx="3" fill="#FFFFFF"/>
      <text x="239" y="181" font-size="12" text-anchor="middle" fill="#6E6E73">no</text>
      <rect x="70" y="16" width="200" height="38" rx="19" fill="#E8F0FE" stroke="#1A73E8" stroke-width="1"/>
      <text x="170" y="40" font-size="13" font-weight="500" text-anchor="middle" fill="#1D1D1F">An integral of a product</text>
      <polygon points="170,96 262,124 170,152 78,124" fill="#FFFFFF" stroke="#C7C7CC" stroke-width="1"/>
      <text x="170" y="128" font-size="12" font-weight="500" text-anchor="middle" fill="#1D1D1F">Is one factor a polynomial?</text>
      <rect x="14" y="218" width="132" height="54" rx="8" fill="#E6F4EA" stroke="#188038" stroke-width="1"/>
      <text x="80" y="241" font-size="13" font-weight="500" text-anchor="middle" fill="#1D1D1F">Tabular method</text>
      <text x="80" y="258" font-size="11" font-weight="400" text-anchor="middle" fill="#6E6E73">differentiate u to 0</text>
      <rect x="194" y="218" width="132" height="54" rx="8" fill="#FFFFFF" stroke="#C7C7CC" stroke-width="1"/>
      <text x="260" y="241" font-size="13" font-weight="500" text-anchor="middle" fill="#1D1D1F">LIATE</text>
      <text x="260" y="258" font-size="11" font-weight="400" text-anchor="middle" fill="#6E6E73">pick u, integrate once</text>
    </svg></figure>
    <h3>Worked example</h3>
    <p>Take <span class="math" data-tex="\int x e^{x}\,dx">∫ x eˣ dx</span>. With <span class="math" data-tex="u = x">u = x</span> and <span class="math" data-tex="dv = e^x\,dx">dv = eˣ dx</span>:</p>
    <div class="math display" data-tex="\int x e^x\,dx = x e^x - \int e^x\,dx = e^x(x-1) + C">∫ x eˣ dx = eˣ(x − 1) + C</div>
    <p>When <em>u</em> is a polynomial of degree two or more, the tabular method keeps the signs straight:</p>
    <table><thead><tr><th>Sign</th><th>u and its derivatives</th><th>dv and its integrals</th></tr></thead><tbody><tr><td>+</td><td><span class="math" data-tex="x^2">x²</span></td><td><span class="math" data-tex="e^x">eˣ</span></td></tr><tr><td>−</td><td><span class="math" data-tex="2x">2x</span></td><td><span class="math" data-tex="e^x">eˣ</span></td></tr><tr><td>+</td><td><span class="math" data-tex="2">2</span></td><td><span class="math" data-tex="e^x">eˣ</span></td></tr></tbody></table>
    <blockquote><p>The professor's tip for the exam: if the integral comes back to itself, move it to the left-hand side and solve for it.</p></blockquote>
    <h2>Definitions</h2>
    <ul>
    <li><strong>Integration by parts</strong>: the product rule run backwards, for the integral of a product.</li>
    <li><strong>LIATE</strong>: an order for choosing <em>u</em>; earlier kinds simplify when differentiated.</li>
    <li><strong>Tabular method</strong>: repeated parts as a table, for a polynomial times something easy to integrate.</li>
    </ul>
    <p>Checking an answer by differentiating it is quick with a computer algebra system:</p>
    <pre><code class="language-python">from sympy import symbols, exp, integrate
    x = symbols("x")
    integrate(x * exp(x), x)   # (x - 1)*exp(x)
    </code></pre>
    <h2>Questions to review</h2>
    <ol>
    <li>Why does LIATE put logarithms first?</li>
    <li>Evaluate <span class="math" data-tex="\int x \cos x\,dx">∫ x cos x dx</span>.</li>
    <li>When does integration by parts loop back to the original integral, and how do you finish?</li>
    </ol>
    <h2>==To do==</h2>
    <ul>
    <li>==problems 7.1: 3, 9, 15 before Friday==</li>
    </ul>
    """#
}
#endif
