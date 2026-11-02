import AppIntents
import Foundation

/// "Ask Syft Notes …": a question answered from your notes and lectures, read out or shown by Siri.
struct AskIntent: AppIntent {
    static let title: LocalizedStringResource = "Ask about my notes"
    static let description = IntentDescription("Answers a question from your Syft Notes notes and lectures.")

    @Parameter(title: "Question", requestValueDialog: "What do you want to know?")
    var question: String

    func perform() async throws -> some IntentResult & ProvidesDialog & ReturnsValue<String> {
        guard let api = await AppModel.current?.api else {
            return .result(value: "", dialog: "Open Syft Notes and add this phone to your library first.")
        }
        // The app says where a question goes before the first one is sent (AskConsent): Siri doesn't send it before.
        guard AskConsent.agreed else {
            return .result(value: "", dialog: "Open Syft Notes and ask there once first. It shows where your questions go.")
        }
        var answer = ""
        do {
            for try await event in try await api.ask(question) {
                switch event {
                case .text(let t): answer += t
                case .answer(let t): answer = t
                case .done(let reply): if !reply.answer.isEmpty { answer = reply.answer }
                case .error(_, let detail): return .result(value: "", dialog: IntentDialog(stringLiteral: detail))
                case .chat: break
                }
            }
        } catch {
            return .result(value: "", dialog: IntentDialog(stringLiteral: (error as? APIError)?.detail ?? "Couldn't reach your library."))
        }
        let plain = answer.replacingOccurrences(of: "**", with: "").replacingOccurrences(of: "#", with: "")
        return .result(value: plain, dialog: IntentDialog(stringLiteral: plain.isEmpty ? "No answer came back." : plain))
    }
}

struct StudyStashShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: StartRecordingIntent(), phrases: ["Record a lecture in \(.applicationName)", "Start \(.applicationName)"],
                    shortTitle: "Record", systemImageName: "mic.fill")
        AppShortcut(intent: AskIntent(), phrases: ["Ask \(.applicationName)", "Ask \(.applicationName) about my notes"],
                    shortTitle: "Ask my notes", systemImageName: "sparkles")
    }
}
