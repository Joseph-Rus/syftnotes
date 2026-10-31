import SwiftUI

// Ask, as the chat screens of the Claude and ChatGPT apps have it, in Syft Notes' look: no big title, the chats
// before on the left, the answering AI's own mark and name in the middle, New chat on the right; a nearly empty page
// until something's asked; the question in a quiet bubble on the right and the answer full width under it, the AI's
// own thinking mark while there are no words yet, then the lectures it used as small chips, who answered, Copy and Ask
// again as quiet icons; and one pill at the foot: what it asks about, the question, the send circle. The conversation
// is kept by the library as a chat, as the desktop keeps them (AiAskModel.Chats).

/// Where a lecture an answer came from opens: at the moment it drew on, on the Transcript.
func askLectureRoute(_ id: String, at: Double?) -> Route { .lecture(id, at: at) }

/// The Ask tab: answers from your own notes and lectures, about everything or one class.
struct AskTab: View {
    @Environment(AppModel.self) private var app
    @State private var conversation = AskConversation(saveKey: "ask.history", place: .home)
    @State private var scope: AskScope = .everything

    var body: some View {
        NavigationStack(path: app.stack(.ask)) {
            AskThread(conversation: conversation, scope: $scope, base: .everything, isTab: true) { source, at in
                // The lecture on Home's stack, at the minute, on its Transcript.
                if let id = source.id { app.go(askLectureRoute(id, at: at), tab: .home) }
            }
            .navigationTitle("Ask")
            .navigationBarTitleDisplayMode(.inline)
            .routes()
        }
    }
}

/// Ask about one lecture or class, from its page's Ask bar. `question` is sent as it opens ("Ask how to start this").
/// Nothing is opened inside it: a lecture an answer came from closes the sheet, then opens at its minute on the page
/// underneath. A lecture's own page can take that itself with `onMoment` (its Transcript at that time). What's asked
/// here is kept as the lecture's (or the class's) chat, as on the computer.
struct AskSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @State private var conversation: AskConversation
    @State private var scope: AskScope
    /// Where it was opened (a lecture's class found, when it wasn't given).
    @State private var base: AskScope
    private let question: String?
    private let onMoment: ((_ lecture: String, _ at: Double?) -> Void)?

    init(scope: AskScope, question: String? = nil, onMoment: ((_ lecture: String, _ at: Double?) -> Void)? = nil) {
        let known = scope.inClass(scope.lectureClassSaved)
        _scope = State(initialValue: known)
        _base = State(initialValue: known)
        _conversation = State(initialValue: AskConversation(saveKey: nil, place: known.place))
        self.question = question
        self.onMoment = onMoment
    }

    var body: some View {
        NavigationStack {
            AskThread(conversation: conversation, scope: $scope, base: base, isTab: false, firstQuestion: question) { source, at in
                guard let id = source.id else { return }
                dismiss()
                if let onMoment {
                    onMoment(id, at)
                } else {
                    app.go(askLectureRoute(id, at: at), tab: app.tab)
                }
            }
            .navigationTitle(scope.placeholder(noun: app.noun(of: scope.className)))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { dismiss() } label: { Text("Done").textStyle(.button) }
                }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
        .task {
            // A lecture's class, when it wasn't given and nothing saved says: the library's.
            guard case let .lecture(id, _, nil) = base, let l = try? await app.api?.lecture(id),
                  let cls = l.class, !cls.isEmpty, cls != "Unsorted" else { return }
            if scope == base { scope = base.inClass(cls) }
            base = base.inClass(cls)
        }
    }
}

/// The page both share: the chats and New chat over it, the questions asked and their answers (or, before any, the
/// answering AI's mark and one line), and the bar at its foot.
private struct AskThread: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.colorScheme) private var scheme
    var conversation: AskConversation
    @Binding var scope: AskScope
    /// Where it was opened: the scopes the bar offers follow from it (a lecture: it too).
    var base: AskScope
    /// The Ask tab (its conversation kept on the phone too), not a sheet.
    var isTab: Bool
    var firstQuestion: String?
    /// A lecture an answer came from, tapped: at the moment it drew on.
    var open: (AskTurn.Source, Double?) -> Void

    @State private var question = ""
    @State private var classes: [ClassInfo] = ResponseCache.shared.read("library", as: LibraryOverview.self)?.classes ?? []
    @State private var recent: [LectureSummary] = ResponseCache.shared.read("recent", as: [LectureSummary].self) ?? []
    /// Chats started here, until the library's list has them.
    @State private var made: [ChatSummary] = []
    @State private var noAI = false
    @State private var showChats = false
    @State private var warmed = false
    /// The page follows the answer as it's written, unless the student has scrolled up to read.
    @State private var following = true
    @State private var toast: String?
    private var engines = AskEngineWatch.shared

    init(conversation: AskConversation, scope: Binding<AskScope>, base: AskScope, isTab: Bool, firstQuestion: String? = nil,
         open: @escaping (AskTurn.Source, Double?) -> Void) {
        self.conversation = conversation
        _scope = scope
        self.base = base
        self.isTab = isTab
        self.firstQuestion = firstQuestion
        self.open = open
    }

    private var pad: CGFloat { sizeClass == .regular ? Space.pageRegular : Space.pageCompact }

    /// The noun of what's asked about ("lecture", "meeting").
    private var noun: String { app.noun(of: scope.className) }

    /// Over Google Drive the waiting is the laptop's; otherwise the library's.
    private var overDrive: Bool { app.isDrive || DebugLaunch.flag("-ask-drive") }

    /// A few questions to start with, as chips over the bar (an alternative look, `-ask-chips` in a debug build).
    private var showsChips: Bool { DebugLaunch.flag("-ask-chips") }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    // With a chat already on the page the first screen's words aren't there to say it.
                    if !conversation.turns.isEmpty || app.laptopBack { LaptopBanner().padding(.top, 8) }
                    if noAI {
                        EmptyState(title: "Asking isn't set up yet",
                                   detail: "On your computer, open Settings, then Your AI, and choose one.", mark: .oddOneOut)
                    } else if conversation.turns.isEmpty {
                        start
                    }
                    ForEach(Array(conversation.turns.enumerated()), id: \.element.id) { i, turn in
                        TurnView(turn: turn, last: i == conversation.turns.count - 1, inScope: base,
                                 stage: conversation.stage, engine: engines.engine, overDrive: overDrive, open: open,
                                 retry: { retry(turn) }, askAgain: { askAgain(turn) })
                            .id(turn.id)
                    }
                    Color.clear.frame(height: 1).id("end")
                }
                .padding(.horizontal, pad)
                .padding(.bottom, 12)
                .frame(maxWidth: 760 + 2 * pad)
                .frame(maxWidth: .infinity)
            }
            .defaultScrollAnchor(conversation.turns.isEmpty ? .top : .bottom, for: .initialOffset)
            .scrollDismissesKeyboard(.interactively)
            .page()
            .onScrollPhaseChange { _, phase, context in
                // Where the student left it: at the end, the page follows what's written; scrolled up, it stays.
                guard phase == .idle else { return }
                let g = context.geometry
                following = g.contentOffset.y + g.containerSize.height >= g.contentSize.height - 80
            }
            .onScrollGeometryChange(for: CGFloat.self) { $0.contentSize.height } action: { old, new in
                guard following, new > old, !conversation.turns.isEmpty else { return }
                proxy.scrollTo("end", anchor: .bottom)
            }
            .onChange(of: conversation.turns.count) { _, _ in
                following = true
                withAnimation(.smooth) { proxy.scrollTo("end", anchor: .bottom) }
            }
            .onChange(of: conversation.chatID) { _, id in
                // Kept by the library just now: listed at once, before its copy (over Google Drive) says so.
                if let id, let title = conversation.title, !made.contains(where: { $0.id == id }) {
                    let p = base.place
                    made.insert(ChatSummary(id: id, title: String(title.prefix(80)), class: p.class ?? "", lecture: p.lecture ?? "",
                                            updated: ISO8601DateFormatter().string(from: .now), kind: "ask", scope: p.scope, turns: 1), at: 0)
                }
            }
            .safeAreaInset(edge: .bottom, spacing: 0) {
                if !noAI {
                    composer {
                        guard $0, !conversation.turns.isEmpty else { return }
                        following = true
                        Task {
                            try? await Task.sleep(for: .milliseconds(350))
                            withAnimation(.smooth) { proxy.scrollTo("end", anchor: .bottom) }
                        }
                    }
                }
            }
        }
        .foregroundStyle(.ink)
        .toolbar { header }
        .sheet(isPresented: $showChats) {
            ChatsSheet(place: base.place, current: conversation.chatID, made: made, recent: recent, overDrive: overDrive,
                       onOpen: { chat in
                           conversation.open(chat)
                           scope = base
                       },
                       onNew: { newChat() },
                       onDeleted: { id in
                           made.removeAll { $0.id == id }
                           if id == conversation.chatID { conversation.startOver() }
                       })
        }
        .toast($toast)
        .task {
            guard let api = app.api else { return }
            // A question it was opened with goes at once ("How should I start this?").
            if let q = firstQuestion, conversation.turns.isEmpty { send(q) }
            #if DEBUG
            if let typing = DebugLaunch.value("-ask-typing"), isTab { question = typing }
            #endif
            // Which AI answers: asked alongside, never in the way (a laptop that can't say yet takes a while to).
            Task { await engines.refresh(api) }
            async let lib = try? api.library()
            async let latest = try? api.lectures(limit: 12)
            if let lib = await lib {
                classes = lib.classes
                noAI = !lib.ask
                app.learn(lib)
            }
            if let l = await latest { recent = l }
            #if DEBUG
            // `-ask "<question>"`: asked on the Ask tab, or in the sheet `-show ask…` opens.
            let sheet = DebugLaunch.value("-show")?.hasPrefix("ask") == true
            if let q = DebugLaunch.value("-ask"), isTab != sheet, conversation.turns.last?.question != q { send(q) }
            // `-ask-chats`: the Chats list open; `-ask-open <chat id>`: that kept chat open, as if picked from Chats.
            if DebugLaunch.flag("-ask-chats"), isTab != sheet { try? await Task.sleep(for: .seconds(1)); showChats = true }
            if let id = DebugLaunch.value("-ask-open"), isTab != sheet { openChat(id) }
            #endif
        }
    }

    // MARK: the header

    @ToolbarContentBuilder private var header: some ToolbarContent {
        ToolbarItemGroup(placement: .topBarLeading) {
            Button { showChats = true } label: { Image(icon: "history") }
                .accessibilityLabel("Chats")
                .accessibilityIdentifier("ask.chats")
            if !isTab { newChatButton }
        }
        ToolbarItem(placement: .principal) {
            AskHeader(engine: engines.engine, title: conversation.turns.isEmpty ? nil : conversation.title)
        }
        if isTab {
            ToolbarItem(placement: .topBarTrailing) { newChatButton }
        }
    }

    private var newChatButton: some View {
        Button { newChat() } label: { Image(icon: "edit_square") }
            .accessibilityLabel("New chat")
            .accessibilityIdentifier("ask.new")
            .disabled(conversation.turns.isEmpty)
    }

    private func newChat() {
        withAnimation(.smooth) { conversation.startOver() }
        scope = base
        question = ""
    }

    // MARK: before anything's asked

    /// The pixel s and one line, in the middle of the page: our mark alone (the header names the AI that answers, in
    /// words).
    private var start: some View {
        VStack(spacing: 16) {
            SyftMark(height: 96)
            Text(laptopAway ? "Your laptop isn't answering right now." : emptyLine).textStyle(.title).foregroundStyle(.ink)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if case .away(let since) = app.laptop {
                // Questions aren't kept for later: each is answered on the laptop while it's asked, or not at all.
                Text("\(LaptopPresence.lastHere(since)) Ask needs it to be on and awake, so ask again when it's back.")
                    .textStyle(.lede).foregroundStyle(.inkMuted)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("ask.laptopAway")
            }
        }
        .padding(.horizontal, 24)
        .frame(maxWidth: .infinity)
        .containerRelativeFrame(.vertical) { height, _ in height * 0.78 }
        .accessibilityElement(children: .combine)
    }

    /// The laptop is asleep or off (over Google Drive). The bar still sends: the page may be a few minutes behind a laptop
    /// that has just woken, and a question that gets no answer says so in the words it always has.
    private var laptopAway: Bool { app.laptop.isAway }

    private var emptyLine: String {
        switch base {
        case .everything: "What do you want to know about your notes?"
        case .class(let c): "What do you want to know about \(c)?"
        case .lecture: "What do you want to know about this \(noun)?"
        }
    }

    /// At most three short questions, each a few words.
    private var chips: [String] {
        switch base {
        case .lecture: ["Sum it up", "Quiz me", app.forSchool ? "Exam topics?" : "What was decided?"]
        case .class: ["This week", "Quiz me", app.forSchool ? "What's due?" : "What's left to do?"]
        case .everything: ["Sum up this week", "Quiz me", app.forSchool ? "What's due?" : "What's left to do?"]
        }
    }

    // MARK: Chats

    private func openChat(_ id: String) {
        Task {
            guard let api = app.api else { return }
            do {
                let chat = try await api.chat(id)
                withAnimation(.smooth) { conversation.open(chat) }
                scope = base
            } catch {
                toast = (error as? APIError)?.detail ?? "That chat couldn't be opened."
            }
        }
    }

    // MARK: the bar

    /// What the bar can ask about, as its menu lists them: all notes, each class, and this lecture when it was opened
    /// from one.
    private var choices: [AskScope] {
        var cls = classes.map { AskScope.class($0.name) }
        if let own = base.className, !cls.contains(.class(own)) { cls.append(.class(own)) }
        if case .lecture = base { return [.everything] + cls + [base] }
        return [.everything] + cls
    }

    private func composer(onFocus: @escaping (Bool) -> Void) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if showsChips && conversation.turns.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(chips, id: \.self) { c in
                            Button { send(c) } label: {
                                Text(c).textStyle(.buttonSmall).foregroundStyle(.ink).lineLimit(1)
                                    .padding(.horizontal, 14)
                                    .frame(height: 34)
                                    .overlay(Capsule().strokeBorder(.line))
                                    .contentShape(.capsule)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    .padding(.horizontal, 2)
                }
            }
            AskBar(placeholder: laptopAway ? "Ask when your laptop is back" : "Ask anything", text: $question, busy: conversation.busy, onStop: { conversation.stop() },
                   focusOnAppear: DebugLaunch.value("-ask-typing") != nil && isTab, onFocus: onFocus) {
                scopeMenu
            } onSend: {
                send(question)
            }
            .accessibilityElement(children: .contain)
        }
        .onChange(of: question) { old, new in
            // The AI that will answer gets ready once a question's begun, as on the computer.
            if old.isEmpty, !new.isEmpty, !warmed, let api = app.api { warmed = true; Task { await api.warm() } }
        }
        .padding(.horizontal, sizeClass == .regular ? pad : 12)
        .padding(.top, 8)
        .padding(.bottom, 8)
        .frame(maxWidth: 760 + 2 * pad)
        .frame(maxWidth: .infinity)
        .background(Color.surface)
    }

    /// The scope chip and its menu: a native one, the chosen with a check, each class with its highlighter.
    private var scopeMenu: some View {
        Menu {
            Picker("Ask about", selection: $scope) {
                ForEach(choices, id: \.self) { c in
                    if case .class(let cls) = c {
                        Label { Text(cls) } icon: { SwatchImage.of(app.hue(of: cls), dark: scheme == .dark) }.tag(c)
                    } else {
                        Text(c.label(noun: noun)).tag(c)
                    }
                }
            }
            .pickerStyle(.inline)
        } label: {
            AskScopeChip(title: scope.label(noun: noun), hue: app.hue(of: scope.className), showsSwatch: { if case .class = scope { true } else { false } }())
        }
        .menuOrder(.fixed)
        .accessibilityLabel("Asking about \(scope.label(noun: noun))")
        .accessibilityHint("Changes what it asks about")
        .accessibilityIdentifier("ask.scope")
    }

    private func send(_ text: String) {
        guard let api = app.api, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, !conversation.busy else { return }
        question = ""
        warmed = false
        conversation.send(text, scope: scope, label: scope.label(noun: noun), api: api)
    }

    private func retry(_ turn: AskTurn) {
        guard let api = app.api else { return }
        conversation.retry(turn, scope: scope, label: scope.label(noun: noun), api: api)
    }

    /// The same question again, as a follow-up at the end of the chat.
    private func askAgain(_ turn: AskTurn) {
        guard let api = app.api, !conversation.busy else { return }
        let s = turn.asked ?? scope
        conversation.send(turn.question, scope: s, label: turn.scope ?? s.label(noun: noun), api: api)
    }
}

/// The middle of Ask's header: the answering AI's name; in a chat, its title over the AI's small mark and name. Before
/// anything's asked the page shows the pixel s alone, so the AI is named in words only.
private struct AskHeader: View {
    var engine: AskEngine?
    var title: String?

    var body: some View {
        VStack(spacing: 1) {
            if let title {
                Text(title).textStyle(.buttonSmall).foregroundStyle(.ink).lineLimit(1)
            }
            HStack(spacing: 6) {
                if let engine, title != nil { BrandMark(engine, size: 12, relativeTo: .caption) }
                Text(engine?.name ?? "Ask")
                    .textStyle(title == nil ? .button : .micro)
                    .foregroundStyle(title == nil ? Color.ink : .inkMuted)
            }
        }
        .frame(maxWidth: 230)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel([title, engine.map { "Answered by \($0.name)" }].compactMap { $0 }.joined(separator: ", "))
        .accessibilityAddTraits(.isHeader)
    }
}

/// One question and its answer: the question in a quiet bubble on the right; while there are no words yet, the s's
/// hopping bits and what's happening, the AI named in words; the answer full width in the notes' type as it's written;
/// then the lectures it used as small chips, who answered (its small mark and name), Copy and Ask again. A question
/// with no answer says why in one line, with Try again.
private struct TurnView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.accessibilityReduceMotion) private var still
    var turn: AskTurn
    /// The newest turn: the one being answered, if any.
    var last: Bool
    /// Where the thread is: a class's own Ask doesn't name the class on every question.
    var inScope: AskScope
    var stage: AskStage
    /// The AI answering now, named in the words while it works.
    var engine: AskEngine?
    var overDrive: Bool
    var open: (AskTurn.Source, Double?) -> Void
    var retry: () -> Void
    var askAgain: () -> Void

    /// How much of the answer is on screen while it's written: it arrives in pieces (over Google Drive, a sentence
    /// or so at a time) and is put on the page at a steady pace, as the desktop's does.
    @State private var shown = 0
    @State private var pacing = false
    /// No words for a while over Google Drive: said why.
    @State private var slow = false
    @State private var didCopy = false

    private var asking: Bool { turn.state == .asking }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            question

            if asking && turn.answer.isEmpty {
                waiting
            } else if !turn.answer.isEmpty {
                // The pixel s beside the answer.
                HStack(alignment: .top, spacing: 10) {
                    SyftMark(height: 30, tile: .circle).padding(.top, 2)
                    MarkdownText(shownAnswer, style: .lede)
                        .accessibilityElement(children: .combine)
                }
                if asking || pacing {
                    // More on its way: the s's bits hopping under the words, the only thing moving.
                    SyftLoader(.hoppingBits, height: 14, label: LocalizedStringKey(engine.map { "\($0.name) is writing" } ?? "Writing"))
                        .padding(.leading, 40)
                        .padding(.top, -4)
                }
            }

            if turn.state == .failed, let p = turn.problem {
                VStack(alignment: .leading, spacing: 10) {
                    Text(p).textStyle(.body).foregroundStyle(.inkMuted).fixedSize(horizontal: false, vertical: true)
                    Button("Try again", action: retry)
                        .buttonStyle(.pill(small: true))
                        .accessibilityIdentifier("ask.retry")
                }
            }

            if turn.state == .done && !pacing {
                if let note = turn.note {
                    Text(note).textStyle(.micro).foregroundStyle(.inkSubtle).fixedSize(horizontal: false, vertical: true)
                }
                let sources = turn.sources.filter { $0.id != nil }
                if !sources.isEmpty {
                    FlowLayout(spacing: 8) {
                        ForEach(Array(sources.enumerated()), id: \.offset) { _, s in
                            SourceChip(source: s) { open(s, s.times.first) }
                        }
                    }
                }
                footer
            }
        }
        .padding(.vertical, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .onAppear {
            // Written while it's on screen: paced. Read back (an earlier turn, a chat opened): all at once.
            pacing = asking && !still
            shown = pacing ? 0 : turn.answer.count
        }
        .task(id: RevealStep(count: turn.answer.count, asking: asking)) { await reveal() }
        .task(id: asking && turn.answer.isEmpty ? stage : nil) {
            slow = false
            guard asking, turn.answer.isEmpty else { return }
            // (`-ask-waiting`, for pictures: at once.)
            try? await Task.sleep(for: .seconds(DebugLaunch.flag("-ask-waiting") ? 1 : 8))
            if !Task.isCancelled { slow = true }
        }
        .onChange(of: turn.state) { old, new in
            guard old == .asking, last else { return }
            AccessibilityNotification.Announcement(new == .failed ? (turn.problem ?? "No answer") : turn.stopped == true ? "Stopped" : "Answered").post()
        }
    }

    private struct RevealStep: Hashable { var count: Int; var asking: Bool }

    /// Put more of the answer on the page, a little at a time, catching up with what has arrived (in about a second;
    /// quicker once it's all in).
    private func reveal() async {
        guard pacing else { return }
        while !Task.isCancelled {
            let total = turn.answer.count
            if shown >= total {
                if !asking { pacing = false }
                return
            }
            let pending = total - shown
            shown = min(total, shown + (asking ? max(2, pending / 24) : max(8, pending / 6)))
            try? await Task.sleep(for: .milliseconds(33))
        }
    }

    private var shownAnswer: String {
        guard pacing else { return turn.answer }
        return MarkdownText.showable(String(turn.answer.prefix(shown)))
    }

    private var question: some View {
        VStack(alignment: .trailing, spacing: 6) {
            Text(turn.question).textStyle(.lede).foregroundStyle(.ink)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
                .padding(.horizontal, 14)
                .padding(.vertical, 9)
                .background(.surfaceSunken, in: .rect(cornerRadius: 18, style: .continuous))
            if case .class(let c)? = turn.asked, inScope != turn.asked {
                Highlight(c, hue: app.hue(of: c))
            }
        }
        .padding(.leading, 56)
        .frame(maxWidth: .infinity, alignment: .trailing)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("You asked: \(turn.question)")
    }

    /// No words yet: the s's bits hopping, the only thing moving (never beside the AI's own mark), and what's
    /// happening, as far as the phone can see, naming the AI ("Claude is reading your notes…").
    private var waiting: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 10) {
                SyftLoader(.hoppingBits, height: 18)
                Text(stage.words(overDrive: overDrive, engine: engine)).textStyle(.body).foregroundStyle(.inkMuted)
                    .contentTransition(.opacity)
                    .animation(.smooth, value: stage)
            }
            if slow && overDrive {
                // Under the words: past the loader (18 tall, twice as wide) and the gap.
                Text("Google Drive takes a few seconds.")
                    .textStyle(.meta).foregroundStyle(.inkSubtle)
                    .padding(.leading, 46)
                    .transition(.opacity)
            }
        }
        .animation(.smooth, value: slow)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.updatesFrequently)
        .accessibilityIdentifier("ask.waiting")
    }

    /// Who answered (its mark and name, or that it was stopped), Copy and Ask again.
    private var footer: some View {
        let by = turn.answeredBy
        return HStack(spacing: 6) {
            if let by { BrandMark(by, size: 14, relativeTo: .caption) }
            if let name = turn.engine.flatMap({ $0.isEmpty ? nil : $0 }) ?? by?.name {
                Text(turn.stopped == true ? "\(name) · Stopped" : name).textStyle(.micro).foregroundStyle(.inkSubtle)
            } else if turn.stopped == true {
                Text("Stopped").textStyle(.micro).foregroundStyle(.inkSubtle)
            }
            Spacer(minLength: 8)
            if !turn.answer.isEmpty {
                Button {
                    UIPasteboard.general.string = MarkdownText.plain(turn.answer)
                    didCopy = true
                    AccessibilityNotification.Announcement("Copied").post()
                    Task { try? await Task.sleep(for: .seconds(2)); didCopy = false }
                } label: { Icon(didCopy ? "check" : "content_copy", size: 17) }
                .buttonStyle(QuietIconStyle())
                .accessibilityLabel(didCopy ? "Copied" : "Copy the answer")
            }
            Button(action: askAgain) { Icon("refresh", size: 17) }
                .buttonStyle(QuietIconStyle())
                .accessibilityLabel("Ask again")
                .accessibilityIdentifier("ask.again")
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(turn.stopped == true ? "Stopped" : by.map { "Answered by \($0.name)" } ?? "Answer")
    }
}

/// Copy and Ask again under an answer: a small icon in the subtle ink, a finger's room round it, washed while pressed.
private struct QuietIconStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(configuration.isPressed ? Color.ink : .inkSubtle)
            .frame(width: 36, height: 32)
            .background(configuration.isPressed ? Color.press : .clear, in: .rect(cornerRadius: Radius.control))
            .contentShape(.rect(cornerRadius: Radius.control))
    }
}

/// A lecture an answer came from, as a small chip: its class's swatch, its title, the moment it drew on ("3:40").
/// It opens the lecture there.
private struct SourceChip: View {
    @Environment(AppModel.self) private var app
    var source: AskTurn.Source
    var open: () -> Void

    var body: some View {
        Button(action: open) {
            HStack(spacing: 6) {
                Swatch(app.hue(of: source.class), small: true)
                Text(title).textStyle(.meta).foregroundStyle(.ink).lineLimit(1)
                if let at { Text(at).textStyle(.meta).foregroundStyle(.inkSubtle).monospacedDigit().fixedSize() }
            }
            .padding(.horizontal, 10)
            .frame(minHeight: 32)
            .frame(maxWidth: 300, alignment: .leading)
            .background(.surfaceSunken, in: .rect(cornerRadius: Radius.control))
            .overlay(RoundedRectangle(cornerRadius: Radius.control).strokeBorder(.line))
            .contentShape(.rect(cornerRadius: Radius.control))
        }
        .buttonStyle(SourceChipStyle())
        .accessibilityElement(children: .combine)
        .accessibilityLabel(source.times.isEmpty ? title : "\(title), at \(source.times.map(Format.clock).joined(separator: ", "))")
        .accessibilityIdentifier("ask.source")
        .accessibilityHint(source.times.isEmpty ? "Opens it" : "Opens it at \(Format.clock(source.times[0]))")
    }

    private var title: String {
        if !source.title.isEmpty { return source.title }
        return Nouns.untitled(folder: source.class, date: source.date)
    }

    /// The first moment it drew on ("3:40"), or the part of the notes ("Key points").
    private var at: String? { source.times.first.map(Format.clock) ?? source.section }
}

private struct SourceChipStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label.overlay {
            if configuration.isPressed { RoundedRectangle(cornerRadius: Radius.control).fill(.press).allowsHitTesting(false) }
        }
    }
}
