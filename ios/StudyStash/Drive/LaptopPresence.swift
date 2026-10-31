import Foundation

/// Whether the student's laptop is there to answer, as far as the phone can tell: what the banner on the main screens
/// and Ask's first screen say, so a closed or switched-off laptop isn't found out by asking it something.
enum LaptopPresence: Equatable, Sendable {
    /// Not known yet (just opened and not yet read, or not connected over Google): nothing is said.
    case unknown
    /// It said it was there lately.
    case here
    /// Asleep, off or offline. `since` is when it was last there (nil when its file says nothing of it).
    case away(since: Date?)

    /// The laptop writes `seen` every 45 s while it runs, so a few misses in a row (a slow Drive read, a busy laptop)
    /// still count as there; past this it's really gone. Settings uses the same number.
    static let staleAfter: TimeInterval = 180

    /// What the copy of laptop.json says. Only a read made since the app came to the front (`checked`) can say a laptop
    /// is away: the copy kept on the phone is from the last time, hours ago perhaps, and always looks old.
    static func evaluate(hasLaptop: Bool, seen: Date?, checked: Bool, now: Date = .now) -> LaptopPresence {
        guard checked else { return .unknown }
        guard hasLaptop else { return .away(since: nil) }
        guard let seen else { return .unknown }
        return now.timeIntervalSince(seen) < staleAfter ? .here : .away(since: seen)
    }

    var isAway: Bool { if case .away = self { true } else { false } }

    /// "Last here 2 hours ago." (or when it has never checked in: "It hasn't checked in yet.")
    static func lastHere(_ since: Date?, now: Date = .now) -> String {
        guard let since else { return "It hasn't checked in yet." }
        return "Last here \(SettingsTab.ago(since, now: now))."
    }
}
