import SwiftUI

// Before the first question leaves the phone, where it goes, said once and plainly, as App Review asks of an app that
// shares what people type with an AI (guideline 5.1.2(i)): the question goes to Syft Notes on the student's own laptop,
// and the AI they chose there (Claude, ChatGPT or Gemini) reads their notes and answers, under their own account with
// that company. Ask, flashcards and quizzes and Siri's "Ask Syft Notes" wait for the student to agree; Settings, Your
// AI, takes it back. The sample classes ask it too (so a reviewer sees it), and leaving them forgets the answer.

/// Whether the student has agreed that what they ask from this phone goes to their laptop and the AI they chose there.
/// Kept on this phone.
enum AskConsent {
    static let key = "ask.agreed"

    static var agreed: Bool { UserDefaults.standard.bool(forKey: key) }

    static func agree() { UserDefaults.standard.set(true, forKey: key) }

    /// Asked again before the next question (Settings' switch turned off, or the sample classes left).
    static func forget() { UserDefaults.standard.removeObject(forKey: key) }

    #if DEBUG
    /// `-ask-agreed yes` (pictures and tests that ask straight away) or `-ask-agreed no` (the sheet shows again).
    static func applyDebugLaunch() {
        switch DebugLaunch.value("-ask-agreed") {
        case "yes": agree()
        case "no": forget()
        default: break
        }
    }
    #endif
}

/// The words over the sheet's Agree, for what's waiting on it.
enum AskConsentPurpose {
    /// A question in Ask.
    case ask
    /// A lecture's (or a class's) flashcards and quiz.
    case study
}

/// Where a question goes, in one calm sheet: this phone, Syft Notes on the laptop, the AI the student chose there; then
/// the privacy policy, Agree, and Not now (the question stays in the bar).
struct AskConsentSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @Environment(\.horizontalSizeClass) private var sizeClass
    var purpose: AskConsentPurpose = .ask
    var onAgree: () -> Void

    /// The AI the library says answers, when it has said.
    private var engine: AskEngine? { AskEngineWatch.shared.engine }

    /// Over Google Drive (and in the sample classes) it's the laptop; over Tailscale, the computer the library is on.
    private var computer: String { app.isDrive || app.isDemo ? "laptop" : "computer" }

    private var device: String { UIDevice.current.userInterfaceIdiom == .pad ? "iPad" : "phone" }

    var body: some View {
        let margin = sizeClass == .regular ? Space.s10 : Space.pageCompact
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                Text("Where your questions go").textStyle(.docTitle).foregroundStyle(.ink)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)
                Text(lede).textStyle(.lede).foregroundStyle(.inkMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 10)

                Panel(padding: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        Stop(icon: "smartphone", title: "This \(device)", about: phoneWords)
                        Rule().padding(.leading, 52)
                        Stop(icon: computer == "laptop" ? "laptop_mac" : "dns", title: "Syft Notes on your \(computer)",
                             about: "Finds the notes that answer it.")
                        Rule().padding(.leading, 52)
                        Stop(icon: "public", title: aiTitle, about: aiWords)
                    }
                }
                .padding(.top, 24)

                Text("Flashcards and quizzes are made the same way. Nothing is sent to the people who make Syft Notes. You can turn this off in Settings, under Your AI.")
                    .textStyle(.summary).foregroundStyle(.inkMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 16)
                if app.isDemo {
                    Text("These are sample classes, so here the answers are made on this \(device) and nothing leaves it.")
                        .textStyle(.summary).foregroundStyle(.inkMuted)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(.top, 10)
                }
                Button("How Syft Notes handles your data") { openURL(Self.privacy) }
                    .buttonStyle(.textLink)
                    .padding(.top, 16)
                    .accessibilityHint("Opens the privacy policy on syftnotes.com")
                    .accessibilityIdentifier("askConsent.privacy")
            }
            .frame(maxWidth: 560, alignment: .leading)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, margin)
            .padding(.top, 28)
            .padding(.bottom, 24)
        }
        .scrollBounceBehavior(.basedOnSize)
        .safeAreaInset(edge: .bottom, spacing: 0) {
            VStack(spacing: 14) {
                Button {
                    AskConsent.agree()
                    dismiss()
                    onAgree()
                } label: {
                    Text(purpose == .study ? "Agree and make them" : "Agree and ask").frame(maxWidth: .infinity)
                }
                .buttonStyle(.pill(ink: true))
                .accessibilityIdentifier("askConsent.agree")
                Button("Not now") { dismiss() }
                    .buttonStyle(.textLink)
                    .accessibilityIdentifier("askConsent.notNow")
            }
            .frame(maxWidth: sizeClass == .regular ? 560 : .infinity)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, margin)
            .padding(.top, 12)
            .padding(.bottom, 8)
            .background(Color.surface)
        }
        .foregroundStyle(.ink)
        .page()
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("page.askConsent")
        .presentationDetents([.large])
        .presentationDragIndicator(.visible)
    }

    static let privacy = URL(string: "https://syftnotes.com/privacy/")!

    private var lede: String {
        switch purpose {
        case .ask: "Ask answers from your own notes. Before your first question, here's what happens to it."
        case .study: "Your AI writes flashcards and a quiz from your notes. Before the first ones, here's what it's sent."
        }
    }

    private var phoneWords: String {
        let what = purpose == .study ? "Asks for flashcards and a quiz" : "Sends your question"
        return app.isDrive || app.isDemo
            ? "\(what) to your laptop, through a private Syft Notes folder in your Google Drive."
            : "\(what) to your \(computer)."
    }

    private var aiTitle: String {
        switch engine {
        case .some(.ollama): "Ollama, on your \(computer)"
        case .some(let e): "\(e.name), the AI you chose"
        case nil: "The AI you chose"
        }
    }

    private var aiWords: String {
        switch engine {
        case .some(.ollama):
            "Reads those notes and writes the answer on your \(computer), so they don't leave it. If you choose Claude, ChatGPT or Gemini there instead, they go to that company."
        case .some(let e):
            "Reads those notes and writes the answer, under your own account with \(Self.company(e)). You can choose another AI in Syft Notes on your \(computer)."
        case nil:
            "Claude, ChatGPT or Gemini, whichever you chose on your \(computer), reads those notes and writes the answer, under your own account with that company."
        }
    }

    /// Who runs an AI the student signs in to.
    static func company(_ e: AskEngine) -> String {
        switch e {
        case .claude: "Anthropic"
        case .chatgpt: "OpenAI"
        case .gemini: "Google"
        case .ollama: "Ollama"
        }
    }

    /// One stop on the way: its icon, where it is, what happens there.
    private struct Stop: View {
        var icon: String
        var title: String
        var about: String

        var body: some View {
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                Icon(icon, size: 20).foregroundStyle(.ink)
                    .frame(width: 22)
                    .alignmentGuide(.firstTextBaseline) { $0[VerticalAlignment.center] + 5 }
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(.atkinson(15.5, .bold, relativeTo: .body)).foregroundStyle(.ink)
                    Text(about).textStyle(.summary).foregroundStyle(.inkMuted)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 14)
            .accessibilityElement(children: .combine)
        }
    }
}
