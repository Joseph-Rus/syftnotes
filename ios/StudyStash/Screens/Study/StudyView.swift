import SwiftUI

/// Flashcards and a quiz made from one lecture (or a whole class) by the library's AI (from its notes, as Ask reads
/// them), kept on the phone so they open at once next time.
struct StudyDeck: Codable, Sendable {
    struct Card: Codable, Hashable, Sendable { var front: String; var back: String }
    struct Question: Codable, Hashable, Sendable { var question: String; var choices: [String]; var answer: Int; var why: String? }
    var cards: [Card]
    var quiz: [Question]

    /// The deck in an answer: the first JSON object in it, read leniently (the AI may wrap it in prose or a code fence).
    static func parse(_ text: String) -> StudyDeck? {
        guard let start = text.firstIndex(of: "{"), let end = text.lastIndex(of: "}"), start < end else { return nil }
        guard let deck = try? JSONDecoder().decode(StudyDeck.self, from: Data(text[start...end].utf8)) else { return nil }
        let quiz = deck.quiz.filter { $0.choices.count >= 2 && $0.choices.indices.contains($0.answer) }
        let cards = deck.cards.filter { !$0.front.isEmpty && !$0.back.isEmpty }
        return cards.isEmpty && quiz.isEmpty ? nil : StudyDeck(cards: cards, quiz: quiz)
    }

    static func prompt(_ title: String) -> String {
        """
        Make study material from the lecture "\(title)": 12 flashcards on its key ideas, terms and formulas, and 8 \
        multiple-choice questions that test understanding, not trivia. Reply with ONLY this JSON, nothing before or after:
        \(shape)
        """
    }

    /// A whole class's: from all its lectures, the ideas that matter most across them.
    static func prompt(class name: String) -> String {
        """
        Make study material from my lectures in the class "\(name)", across all of them, weighted to the ideas that \
        come up most: 16 flashcards on key ideas, terms and formulas, and 10 multiple-choice questions that test \
        understanding, not trivia. Reply with ONLY this JSON, nothing before or after:
        \(shape)
        """
    }

    private static let shape = """
        {"cards":[{"front":"question or term","back":"short answer"}],"quiz":[{"question":"...","choices":["A","B","C","D"],"answer":0,"why":"one sentence"}]}
        "answer" is the index of the right choice.
        """
}

/// Study a lecture (or a class): its flashcards and its quiz under one switch, with the lecture's title over them.
/// Black, grey and white like the rest of the app: a card is a white sheet lifted off the page, ink is what you press,
/// and right and wrong are told in words and marks, never in colour.
struct StudyView: View {
    /// What the deck is made from.
    enum Scope: Hashable {
        case lecture(id: String, title: String)
        case `class`(String)
    }

    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    let scope: Scope
    /// "Read the notes again": the page under this cover shows the lecture's notes (it closes this either way).
    var onReadNotes: (() -> Void)?

    enum Mode: Hashable { case cards, quiz }
    @State private var mode: Mode = DebugLaunch.studyQuiz ? .quiz : .cards
    @State private var deck: StudyDeck?
    @State private var making = false
    @State private var problem: String?
    @State private var lectureClass: String?
    @State private var toast: String?
    /// Bumped when a new deck arrives, so both halves start afresh.
    @State private var round = 0
    /// Where the request goes, shown before the first deck is asked for (AskConsent); `waiting` once it's been put off.
    @State private var consenting = false
    @State private var waiting = false

    /// A lecture's flashcards and quiz (Study, on a lecture's page).
    init(lectureID: String, title: String, onReadNotes: (() -> Void)? = nil) {
        scope = .lecture(id: lectureID, title: title)
        self.onReadNotes = onReadNotes
    }

    /// A class's, from all its lectures (a class page's ⋯, "Make flashcards for the class").
    init(className: String) {
        scope = .class(className)
    }

    private var cacheKey: String {
        switch scope {
        case .lecture(let id, _): "study:\(id)"
        case .class(let name): "study:class:\(name)"
        }
    }

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 0) {
                header
                content
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("page.study")
            .page()
            .foregroundStyle(.ink)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button { dismiss() } label: { Text("Done").textStyle(.button) }
                }
                if deck != nil {
                    ToolbarItem(placement: .primaryAction) {
                        Menu {
                            Button { makeWhenAgreed() } label: { Label("Make new flashcards and a quiz", icon: "refresh") }
                                .disabled(making)
                        } label: {
                            Label("More", icon: "more_horiz")
                        }
                    }
                }
            }
            .toast($toast, seconds: 4)
            .sheet(isPresented: $consenting) {
                AskConsentSheet(purpose: .study) { Task { await make() } }
            }
        }
        .task {
            if let saved = ResponseCache.shared.read(cacheKey, as: StudyDeck.self) { deck = saved } else { makeWhenAgreed() }
        }
        .task { await findClass() }
    }

    /// The lecture's title, its class's highlight under it (as a note's row has them); a class's deck, its name.
    private var header: some View {
        VStack(alignment: .leading, spacing: 0) {
            switch scope {
            case .lecture(_, let title):
                Text(title).textStyle(.title).lineLimit(2).fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)
                folder.padding(.top, 4)
            case .class(let name):
                Highlight(name, hue: app.hue(of: name), style: .title)
                    .accessibilityAddTraits(.isHeader)
                Text("Flashcards and a quiz from all its \(Nouns.section(app.noun(of: name)).lowercased())")
                    .textStyle(.meta).foregroundStyle(.inkMuted).padding(.top, 4)
            }
            if let deck, !(deck.cards.isEmpty || deck.quiz.isEmpty) {
                Segmented(selection: $mode, options: [(.cards, "Flashcards"), (.quiz, "Quiz")])
                    .padding(.top, 14)
            }
        }
        .padding(.horizontal, Space.pageCompact)
        .padding(.top, 6)
        .padding(.bottom, 4)
        .frame(maxWidth: 640, alignment: .leading)
        .frame(maxWidth: .infinity)
    }

    @ViewBuilder private var folder: some View {
        if let cls = lectureClass, !cls.isEmpty, cls != "Unsorted" {
            Highlight(cls, hue: app.hue(of: cls))
        } else if lectureClass != nil {
            HStack(spacing: 6) {
                Icon("inbox", size: 15, relativeTo: .footnote).foregroundStyle(.inkSubtle)
                Text("Unsorted").textStyle(.folder).foregroundStyle(.inkMuted)
            }
        }
    }

    @ViewBuilder private var content: some View {
        if let deck {
            ZStack {
                // Both stay alive, so a look at the quiz and back keeps your place in the cards.
                FlashcardsView(cards: deck.cards, quizAfter: !deck.quiz.isEmpty, readNotes: readNotesTitle, onReadNotes: readNotes) {
                    withAnimation(.smooth) { mode = .quiz }
                }
                .opacity(mode == .cards ? 1 : 0)
                .allowsHitTesting(mode == .cards)
                .accessibilityHidden(mode != .cards)
                QuizView(questions: deck.quiz, cardsAfter: !deck.cards.isEmpty, readNotes: readNotesTitle, onReadNotes: readNotes) {
                    withAnimation(.smooth) { mode = .cards }
                }
                .opacity(mode == .quiz ? 1 : 0)
                .allowsHitTesting(mode == .quiz)
                .accessibilityHidden(mode != .quiz)
            }
            .id(round)
            .overlay { if making { makingNote(over: true) } }
            .onAppear {
                if deck.cards.isEmpty { mode = .quiz } else if deck.quiz.isEmpty { mode = .cards }
            }
        } else if making {
            makingNote(over: false)
        } else if waiting && !AskConsent.agreed {
            ScrollView {
                EmptyState(title: "Flashcards come from your AI",
                           detail: "Syft Notes asks the AI on your \(app.isDrive || app.isDemo ? "laptop" : "computer") to write them from your notes.",
                           actionTitle: "See where they go", action: { consenting = true }, mark: .oddOneOut)
                    .padding(.horizontal, Space.pageCompact)
                    .frame(maxWidth: 640)
                    .frame(maxWidth: .infinity)
            }
        } else {
            ScrollView {
                EmptyState(title: "Couldn't make flashcards", detail: problem, actionTitle: "Try again",
                           action: { makeWhenAgreed() }, mark: .oddOneOut)
                    .padding(.horizontal, Space.pageCompact)
                    .frame(maxWidth: 640)
                    .frame(maxWidth: .infinity)
            }
        }
    }

    /// While the AI writes them: what's happening, in a sentence.
    private func makingNote(over: Bool) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            SyftLoader(.pathTrace, height: 88)
            HStack(spacing: 10) {
                StudyLoader(.page, color: .inkSubtle)
                Text(over ? "Making new ones…" : "Making flashcards and a quiz from your notes…")
                    .textStyle(.h2).foregroundStyle(.ink)
                    .fixedSize(horizontal: false, vertical: true)
            }
            EmptyText(over ? "The ones you have stay until the new ones are ready."
                      : "Syft Notes reads the notes the way Ask does and writes questions from them. It takes a moment.")
        }
        .padding(.horizontal, Space.pageCompact)
        .padding(.vertical, 24)
        .frame(maxWidth: 640, alignment: .leading)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(Color.surface.opacity(over ? 1 : 0))
    }

    private var readNotesTitle: String {
        switch scope {
        case .lecture: "Read the notes again"
        case .class(let name): "Back to \(name)"
        }
    }

    /// Back to the lecture's notes: this cover closes onto the lecture's page.
    private func readNotes() {
        onReadNotes?()
        dismiss()
    }

    private func findClass() async {
        guard case .lecture(let id, _) = scope else { return }
        if let l = ResponseCache.shared.read("lecture:\(id)", as: Lecture.self) {
            lectureClass = l.class ?? ""
        } else if let api = app.api, let l = try? await api.lecture(id) {
            lectureClass = l.class ?? ""
        }
    }

    /// A deck asked for now if the student has agreed to where it's sent; else the sheet that says so first.
    private func makeWhenAgreed() {
        if AskConsent.agreed {
            Task { await make() }
        } else {
            waiting = true
            consenting = true
        }
    }

    /// Ask the library's AI for a deck. A deck already here stays until a new one has come.
    private func make() async {
        guard let api = app.api else { return }
        making = true
        problem = nil
        defer { making = false }
        var text = ""
        do {
            let stream = switch scope {
            case let .lecture(id, title): try await api.ask(StudyDeck.prompt(title), lecture: id)
            case .class(let name): try await api.ask(StudyDeck.prompt(class: name), class: name)
            }
            for try await e in stream {
                switch e {
                case .text(let t): text += t
                case .answer(let t): text = t
                case .done(let r): if !r.answer.isEmpty { text = r.answer }
                case .error(_, let d): problem = d
                case .chat: break
                }
            }
        } catch {
            problem = (error as? APIError)?.detail ?? "Couldn't reach your library."
        }
        if let d = StudyDeck.parse(text) {
            deck = d
            round += 1
            ResponseCache.shared.write(cacheKey, d)
        } else {
            if problem == nil { problem = "The AI's answer wasn't flashcards. Try again." }
            if deck != nil { toast = "Couldn't make new ones. These are the ones from before." }
        }
    }
}

// MARK: - Flashcards

/// Cards to turn over and sort: the question big on a white card; tap it and it turns to the answer; then Knew it or
/// Again (or swipe right or left). Cards you didn't know come round again. At the end, the ones that took more than
/// one go, to read over or go through again on their own.
private struct FlashcardsView: View {
    let cards: [StudyDeck.Card]
    var quizAfter: Bool
    var readNotes: String
    var onReadNotes: () -> Void
    var onQuiz: () -> Void

    /// This round's cards (all of them, or just the ones you missed last time).
    @State private var roundCards: [StudyDeck.Card] = []
    @State private var queue: [StudyDeck.Card] = []
    @State private var flipped = false
    @State private var drag: CGSize = .zero
    @State private var known = 0
    /// Cards sent round again at least once this round.
    @State private var missed: [StudyDeck.Card] = []
    @Environment(\.accessibilityReduceMotion) private var still

    private let swipe: CGFloat = 110

    var body: some View {
        Group {
            if cards.isEmpty {
                EmptyText("There are no flashcards this time. Make new ones from ⋯ at the top.")
                    .padding(.horizontal, Space.pageCompact)
                    .padding(.top, 24)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            } else if let card = queue.first {
                VStack(spacing: 0) {
                    progress.padding(.top, 16)
                    cardStack(card)
                        .frame(maxHeight: .infinity)
                        .padding(.top, 20)
                        .padding(.bottom, 24)
                    buttons
                        .padding(.bottom, 12)
                }
                .padding(.horizontal, Space.pageCompact)
                .frame(maxWidth: 640)
                .frame(maxWidth: .infinity)
            } else {
                finished
            }
        }
        .onAppear {
            guard roundCards.isEmpty else { return }
            start(cards)
            #if DEBUG
            // Pictures of each state: `-study-flip` (the first card's answer), `-study-done` (the end, two missed).
            if DebugLaunch.flag("-study-flip") { flipped = true }
            if DebugLaunch.flag("-study-done") {
                missed = Array(cards.suffix(2))
                known = cards.count
                queue = []
            }
            #endif
        }
    }

    // How far through: in words, and as a line that fills with what you know.
    private var progress: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text("\(known) of \(roundCards.count) known").textStyle(.meta).foregroundStyle(.inkMuted)
                Spacer()
                Text(queue.count == 1 ? "Last card" : "\(queue.count) to go").textStyle(.meta).foregroundStyle(.inkSubtle)
            }
            ProgressLine(done: known, of: roundCards.count)
        }
        .accessibilityElement(children: .combine)
    }

    private func cardStack(_ card: StudyDeck.Card) -> some View {
        ZStack {
            // The next card waits under this one, so a swipe shows there's more.
            if queue.count > 1 {
                RoundedRectangle(cornerRadius: Radius.pane)
                    .fill(.surfaceSunken)
                    .overlay(RoundedRectangle(cornerRadius: Radius.pane).strokeBorder(.line))
                    .padding(.horizontal, 14)
                    .offset(y: 10)
                    .frame(maxWidth: 520, maxHeight: 480)
                    .accessibilityHidden(true)
            }
            Flip(angle: flipped ? 180 : 0, still: still) {
                face(card, front: true)
            } back: {
                face(card, front: false)
            }
            .frame(maxWidth: 520, maxHeight: 480)
            .overlay(alignment: drag.width > 0 ? .topLeading : .topTrailing) { verdict }
            .offset(x: drag.width, y: drag.height / 4)
            .rotationEffect(.degrees(still ? 0 : Double(drag.width) / 24))
            .gesture(DragGesture(minimumDistance: 12)
                .onChanged { drag = $0.translation }
                .onEnded { v in
                    if abs(v.translation.width) > swipe { next(knew: v.translation.width > 0) } else { withAnimation(.snappy) { drag = .zero } }
                })
            .onTapGesture { turn() }
            .id(card)
            .transition(.opacity)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(flipped ? "Answer: \(card.back)" : "Question: \(card.front)")
            .accessibilityHint(flipped ? "Double-tap to see the question again." : "Double-tap to see the answer.")
            .accessibilityAddTraits(.isButton)
            .accessibilityAction { turn() }
            .accessibilityActions {
                Button("Knew it") { next(knew: true) }
                Button("Again") { next(knew: false) }
            }
        }
    }

    /// While a card's dragged: what letting go will do, stamped on its corner.
    @ViewBuilder private var verdict: some View {
        let amount = min(1, abs(drag.width) / swipe)
        if abs(drag.width) > 16 {
            let knew = drag.width > 0
            HStack(spacing: 6) {
                Icon(knew ? "check" : "undo", size: 16)
                Text(knew ? "Knew it" : "Again").textStyle(.button)
            }
            .foregroundStyle(knew ? Color.surface : .ink)
            .padding(.horizontal, 12)
            .frame(height: 34)
            .background(knew ? Color.ink : .surface, in: .capsule)
            .overlay(Capsule().strokeBorder(.ink, lineWidth: 1.5))
            .rotationEffect(.degrees(knew ? -8 : 8))
            .padding(18)
            .opacity(amount)
            .accessibilityHidden(true)
        }
    }

    private func face(_ card: StudyDeck.Card, front: Bool) -> some View {
        VStack(spacing: 0) {
            HStack {
                Text(front ? "Question" : "Answer").textStyle(.meta).foregroundStyle(.inkMuted)
                Spacer()
                Icon("flashcards", size: 16, relativeTo: .footnote).foregroundStyle(.inkSubtle)
            }
            Spacer(minLength: 16)
            if front {
                Text(card.front)
                    .textStyle(.docTitle)
                    .multilineTextAlignment(.center)
                    .minimumScaleFactor(0.55)
            } else {
                Text(card.front)
                    .textStyle(.lede)
                    .foregroundStyle(.inkMuted)
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .minimumScaleFactor(0.8)
                Rectangle().fill(.line).frame(width: 48, height: 2).padding(.vertical, 18)
                Text(card.back)
                    .font(.atkinson(26, .bold, relativeTo: .title))
                    .lineSpacing(4)
                    .multilineTextAlignment(.center)
                    .minimumScaleFactor(0.5)
            }
            Spacer(minLength: 16)
            Text(front ? "Tap the card to see the answer" : "Tap the card to see the question").textStyle(.micro).foregroundStyle(.inkSubtle)
        }
        .foregroundStyle(.ink)
        .padding(.horizontal, 24)
        .padding(.vertical, 20)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.surface, in: .rect(cornerRadius: Radius.pane))
        .lifted(.float, in: RoundedRectangle(cornerRadius: Radius.pane))
        .contentShape(.rect(cornerRadius: Radius.pane))
    }

    /// Again (an outline pill) and Knew it (the ink one), half the width each.
    private var buttons: some View {
        HStack(spacing: 12) {
            Button { next(knew: false) } label: {
                Label("Again", icon: "undo").frame(maxWidth: .infinity)
            }
            .buttonStyle(.pill)
            Button { next(knew: true) } label: {
                Label("Knew it", icon: "check").frame(maxWidth: .infinity)
            }
            .buttonStyle(.pill(ink: true))
        }
        .frame(maxWidth: 420)
    }

    // At the end: how it went, where to go next, and the cards that took more than one go.
    private var finished: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                Text(roundCards.count == 1 ? "You went through the card" : "You went through all \(roundCards.count) cards")
                    .textStyle(.docTitle)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 24)
                Text(summary)
                    .textStyle(.lede).foregroundStyle(.inkMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 8)
                FlowLayout(spacing: 10) {
                    if missed.isEmpty {
                        Button { start(cards) } label: { Label("Go again", icon: "refresh") }.buttonStyle(.pill(ink: true))
                    } else {
                        Button { start(missed) } label: { Text(missed.count == 1 ? "Go over that one" : "Go over those \(missed.count)") }
                            .buttonStyle(.pill(ink: true))
                        Button { start(cards) } label: { Text("All of them") }.buttonStyle(.pill)
                    }
                    if quizAfter {
                        Button(action: onQuiz) { Label("Take the quiz", icon: "quiz") }.buttonStyle(.pill)
                    }
                }
                .padding(.top, 20)
                Button(readNotes, action: onReadNotes).buttonStyle(.textLink).padding(.top, 20)
                if !missed.isEmpty {
                    GroupLabel("Took another go")
                        .padding(.top, 36)
                        .padding(.bottom, 4)
                    ForEach(Array(missed.enumerated()), id: \.offset) { i, card in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(card.front).textStyle(.title).fixedSize(horizontal: false, vertical: true)
                            Text(card.back).textStyle(.summary).foregroundStyle(.inkMuted).fixedSize(horizontal: false, vertical: true)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.vertical, 14)
                        .overlay(alignment: .top) { if i > 0 { Rule() } }
                        .accessibilityElement(children: .combine)
                    }
                }
            }
            .padding(.horizontal, Space.pageCompact)
            .padding(.bottom, 32)
            .frame(maxWidth: 640, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
    }

    private var summary: String {
        let firstGo = roundCards.count - missed.count
        if missed.isEmpty { return roundCards.count == 1 ? "You knew it straight away." : "You knew every one straight away." }
        let knewWords = firstGo == 0 ? "None came straight away" : firstGo == 1 ? "You knew 1 straight away" : "You knew \(firstGo) straight away"
        let againWords = missed.count == 1 ? "1 took another go" : "\(missed.count) took another go"
        return "\(knewWords), and \(againWords)."
    }

    private func turn() {
        withAnimation(still ? .easeInOut(duration: 0.2) : .spring(duration: 0.45, bounce: 0.15)) { flipped.toggle() }
    }

    private func next(knew: Bool) {
        guard let card = queue.first else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        withAnimation(.smooth(duration: 0.3)) {
            queue.removeFirst()
            if knew {
                known += 1
            } else {
                queue.append(card)
                if !missed.contains(card) { missed.append(card) }
            }
            flipped = false
            drag = .zero
        }
    }

    private func start(_ these: [StudyDeck.Card]) {
        withAnimation(.smooth) {
            roundCards = these
            queue = these.shuffled()
            known = 0
            missed = []
            flipped = false
            drag = .zero
        }
    }
}

/// A card's two sides: turned about its upright like a real card (with Reduce Motion, one fades into the other).
private struct Flip<Front: View, Back: View>: View, @preconcurrency Animatable {
    var angle: Double
    var still: Bool
    @ViewBuilder var front: Front
    @ViewBuilder var back: Back

    var animatableData: Double {
        get { angle }
        set { angle = newValue }
    }

    var body: some View {
        if still {
            ZStack {
                front.opacity(1 - angle / 180)
                back.opacity(angle / 180)
            }
        } else {
            ZStack {
                front.opacity(angle < 90 ? 1 : 0)
                back.opacity(angle < 90 ? 0 : 1)
                    .rotation3DEffect(.degrees(180), axis: (x: 0, y: 1, z: 0))
            }
            .rotation3DEffect(.degrees(angle), axis: (x: 0, y: 1, z: 0), perspective: 0.35)
        }
    }
}

/// How far through, as a hairline track filling with ink (flat, no fade).
private struct ProgressLine: View {
    var done: Int
    var of: Int
    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(.line)
                Capsule().fill(.ink).frame(width: of == 0 ? 0 : geo.size.width * CGFloat(done) / CGFloat(of))
            }
        }
        .frame(height: 4)
        .animation(.smooth, value: done)
        .accessibilityHidden(true)
    }
}

// MARK: - Quiz

/// One question at a time: pick one, and the right answer is ringed in ink and ticked; a wrong pick turns muted with a
/// cross; a sentence says which ("Right", "Not quite: the answer is …") and why. At the end, how many you got, the
/// notes, and the ones to look at again.
private struct QuizView: View {
    let questions: [StudyDeck.Question]
    var cardsAfter: Bool
    var readNotes: String
    var onReadNotes: () -> Void
    var onCards: () -> Void

    @State private var index = 0
    /// What you picked for each question so far.
    @State private var picks: [Int: Int] = [:]

    private var score: Int { picks.filter { questions.indices.contains($0.key) && questions[$0.key].answer == $0.value }.count }
    private var wrong: [Int] { picks.keys.sorted().filter { questions.indices.contains($0) && questions[$0].answer != picks[$0] } }

    var body: some View {
        ScrollViewReader { scroll in
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    Color.clear.frame(height: 0).id("top")
                    if questions.isEmpty {
                        EmptyText("There's no quiz this time. Make new ones from ⋯ at the top.").padding(.top, 24)
                    } else if index < questions.count {
                        question(index)
                    } else {
                        finished
                    }
                }
                .padding(.horizontal, Space.pageCompact)
                .padding(.bottom, 32)
                .frame(maxWidth: 640, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
            .onChange(of: index) { _, _ in withAnimation { scroll.scrollTo("top", anchor: .top) } }
        }
        #if DEBUG
        // Pictures of each state: `-quiz-pick <n>` (question 1 answered with choice n), `-quiz-done` (the end).
        .onAppear {
            if let n = DebugLaunch.value("-quiz-pick").flatMap(Int.init), !questions.isEmpty { picks[0] = n }
            if DebugLaunch.flag("-quiz-done") {
                for (i, q) in questions.enumerated() { picks[i] = i == 0 ? (q.answer + 1) % q.choices.count : q.answer }
                index = questions.count
            }
        }
        #endif
    }

    @ViewBuilder private func question(_ n: Int) -> some View {
        let q = questions[n]
        let picked = picks[n]
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text("Question \(n + 1) of \(questions.count)").textStyle(.meta).foregroundStyle(.inkMuted)
                Spacer()
                if n > 0 { Text(score == 1 ? "1 right so far" : "\(score) right so far").textStyle(.meta).foregroundStyle(.inkSubtle) }
            }
            ProgressLine(done: n + (picked == nil ? 0 : 1), of: questions.count)
        }
        .accessibilityElement(children: .combine)
        .padding(.top, 16)

        Text(q.question)
            .textStyle(.docTitle)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.top, 22)
            .padding(.bottom, 20)
            .accessibilityAddTraits(.isHeader)

        VStack(spacing: 10) {
            ForEach(Array(q.choices.enumerated()), id: \.offset) { i, choice in
                ChoiceRow(letter: Self.letter(i), text: choice, mark: mark(i, q, picked)) {
                    guard picks[n] == nil else { return }
                    withAnimation(.smooth(duration: 0.25)) { picks[n] = i }
                    UINotificationFeedbackGenerator().notificationOccurred(i == q.answer ? .success : .error)
                }
            }
        }

        if let picked {
            verdict(q, picked: picked)
                .padding(.top, 20)
                .transition(.opacity)
            Button {
                withAnimation(.smooth) { index += 1 }
            } label: {
                if n + 1 < questions.count {
                    Label("Next question", icon: "arrow_forward").frame(maxWidth: .infinity)
                } else {
                    Text("See how you did").frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.pill(ink: true))
            .padding(.top, 20)
        }
    }

    /// Right or not, in words and a mark, and the AI's one-sentence why.
    private func verdict(_ q: StudyDeck.Question, picked: Int) -> some View {
        let right = picked == q.answer
        return HStack(alignment: .top, spacing: 12) {
            Icon(right ? "check_circle" : "cancel", size: 20, relativeTo: .headline)
                .foregroundStyle(.ink)
                .padding(.top, 1)
            VStack(alignment: .leading, spacing: 6) {
                Text(right ? "Right" : "Not quite: the answer is \(q.choices[q.answer]).")
                    .textStyle(.title)
                    .fixedSize(horizontal: false, vertical: true)
                if let why = q.why, !why.isEmpty {
                    Text(why).textStyle(.summary).foregroundStyle(.inkMuted)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line))
        .accessibilityElement(children: .combine)
    }

    private func mark(_ i: Int, _ q: StudyDeck.Question, _ picked: Int?) -> ChoiceRow.Mark {
        guard let picked else { return .open }
        if i == q.answer { return picked == i ? .rightPicked : .right }
        if i == picked { return .wrong }
        return .other
    }

    // At the end: the score, big; the notes and another go; and the questions you missed with their answers.
    private var finished: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("\(score) of \(questions.count) right")
                .textStyle(.date)
                .padding(.top, 24)
            Text(score == questions.count ? (questions.count == 1 ? "You got it." : "You got every one.")
                 : wrong.count == 1 ? "One to look at again. It's below, with its answer."
                 : "\(wrong.count) to look at again. They're below, with their answers.")
                .textStyle(.lede).foregroundStyle(.inkMuted)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 8)
            FlowLayout(spacing: 10) {
                Button(action: onReadNotes) { Label(readNotes, icon: "description") }.buttonStyle(.pill(ink: true))
                Button { withAnimation(.smooth) { picks = [:]; index = 0 } } label: { Label("Try again", icon: "refresh") }
                    .buttonStyle(.pill)
            }
            .padding(.top, 20)
            if cardsAfter {
                Button("Go over the flashcards", action: onCards).buttonStyle(.textLink).padding(.top, 20)
            }
            if !wrong.isEmpty {
                GroupLabel("To look at again").padding(.top, 36).padding(.bottom, 4)
                ForEach(Array(wrong.enumerated()), id: \.element) { k, n in
                    let q = questions[n]
                    VStack(alignment: .leading, spacing: 6) {
                        Text(q.question).textStyle(.title).fixedSize(horizontal: false, vertical: true)
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            Icon("check", size: 15, relativeTo: .subheadline).foregroundStyle(.ink)
                                .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 3 }
                            Text("The answer is \(q.choices[q.answer])").textStyle(.summary).fontWeight(.semibold).foregroundStyle(.ink)
                        }
                        if let p = picks[n], q.choices.indices.contains(p) {
                            HStack(alignment: .firstTextBaseline, spacing: 8) {
                                Icon("close", size: 15, relativeTo: .subheadline).foregroundStyle(.inkSubtle)
                                    .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 3 }
                                Text("You picked \(q.choices[p])").textStyle(.summary).foregroundStyle(.inkMuted)
                            }
                        }
                        if let why = q.why, !why.isEmpty {
                            Text(why).textStyle(.summary).foregroundStyle(.inkMuted).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.vertical, 14)
                    .overlay(alignment: .top) { if k > 0 { Rule() } }
                    .accessibilityElement(children: .combine)
                }
            }
        }
    }

    static func letter(_ i: Int) -> String { i < 26 ? String(UnicodeScalar(UInt8(65 + i))) : "\(i + 1)" }
}

/// One answer to pick: its letter in a ring, its words, and once you've picked, its mark. The right one is ringed in
/// 2 points of ink with a tick and "Right answer"; a wrong pick turns muted with a cross and "Your answer"; the rest
/// step back to the subtle ink.
private struct ChoiceRow: View {
    enum Mark { case open, right, rightPicked, wrong, other }
    var letter: String
    var text: String
    var mark: Mark
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(alignment: .center, spacing: 12) {
                Text(letter)
                    .textStyle(.buttonSmall)
                    .foregroundStyle(isRight ? Color.surface : mark == .open ? .ink : .inkSubtle)
                    .frame(width: 28, height: 28)
                    .background(isRight ? Color.ink : .clear, in: .circle)
                    .overlay(Circle().strokeBorder(isRight ? Color.ink : .line))
                VStack(alignment: .leading, spacing: 2) {
                    Text(text)
                        .textStyle(.lede)
                        .foregroundStyle(mark == .other ? Color.inkSubtle : mark == .wrong ? .inkMuted : .ink)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                    if let note {
                        Text(note).textStyle(.meta).foregroundStyle(.inkMuted)
                    }
                }
                Spacer(minLength: 8)
                if isRight {
                    Icon("check", size: 20).foregroundStyle(.ink)
                } else if mark == .wrong {
                    Icon("close", size: 20).foregroundStyle(.inkMuted)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, minHeight: 56, alignment: .leading)
            .contentShape(.rect(cornerRadius: Radius.panel))
        }
        .buttonStyle(ChoiceStyle(mark: mark))
        .accessibilityLabel("\(letter). \(text)\(note.map { ". \($0)" } ?? "")")
    }

    private var isRight: Bool { mark == .right || mark == .rightPicked }

    private var note: String? {
        switch mark {
        case .rightPicked: "Your answer, right"
        case .right: "Right answer"
        case .wrong: "Your answer"
        default: nil
        }
    }
}

/// A choice's box: white, a hairline round it, washed while pressed; the right answer ringed in 2 points of ink.
private struct ChoiceStyle: ButtonStyle {
    var mark: ChoiceRow.Mark
    func makeBody(configuration: Configuration) -> some View {
        let shape = RoundedRectangle(cornerRadius: Radius.panel)
        configuration.label
            .background(.surface, in: shape)
            .overlay {
                if configuration.isPressed, mark == .open { shape.fill(.press) }
                switch mark {
                case .right, .rightPicked: shape.strokeBorder(.ink, lineWidth: 2)
                default: shape.strokeBorder(configuration.isPressed && mark == .open ? Color.inkSubtle : .line)
                }
            }
    }
}
