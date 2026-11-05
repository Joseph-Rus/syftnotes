import Foundation
import Observation

/// A lecture recorded on this phone: what was said (written down here, as it was said), what the student typed
/// while it recorded, and where it's got to on its way to the library.
struct Recording: Codable, Identifiable, Hashable, Sendable {
    struct Segment: Codable, Hashable, Sendable {
        /// Seconds into the recording.
        var start: Double
        var text: String
    }

    enum State: String, Codable, Sendable {
        /// Still recording (or the app was closed while it was: it's kept, and sent as it stands).
        case recording
        /// Stopped, waiting to reach the library.
        case waiting
        case sending
        /// The library has it; `lecture` is its id there.
        case sent
        case failed
    }

    var id: String
    var started: Date
    var className: String?
    var title: String = ""
    var titleTyped = false
    var notes: String = ""
    var segments: [Segment] = []
    var seconds: Double = 0
    var language: String = Locale.current.identifier
    var state: State = .recording
    var error: String?
    /// The audio, beside the recording's .json, when it was kept.
    var audio: String?

    /// "phone-20261015-091500-a1b2c3": when it started, and a little randomness so two phones never clash.
    static func newID(_ now: Date = .now) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyyMMdd-HHmmss"
        let hex = (0..<3).map { _ in String(format: "%02x", UInt8.random(in: 0...255)) }.joined()
        return "phone-\(f.string(from: now))-\(hex)"
    }

    /// The transcript as the library reads it: "[mm:ss] words" a line ("[h:mm:ss]" past the first hour).
    var transcript: String {
        segments.compactMap { s in
            let text = s.text.trimmingCharacters(in: .whitespacesAndNewlines)
            return text.isEmpty ? nil : "[\(Self.stamp(s.start))] \(text)"
        }.joined(separator: "\n")
    }

    static func stamp(_ seconds: Double) -> String {
        let s = max(0, Int(seconds))
        return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60) : String(format: "%02d:%02d", s / 60, s % 60)
    }

    /// Its title, or until it has one the desktop's working title: "Calculus II, Fri 16 Oct", or with no class
    /// "Recording, Fri 16 Oct at 9:41 AM".
    var displayTitle: String {
        if !title.isEmpty { return title }
        return Nouns.untitled(folder: className, date: Self.localISO(started))
    }

    /// What POST /api/v2/lectures takes.
    struct Payload: Encodable {
        var id: String
        var date: String
        var `class`: String?
        var title: String?
        var title_typed: Bool
        var transcript: String
        var notes: String?
        var seconds: Double
        var language: String
    }

    /// "2026-10-15T22:07:38-07:00": the time here, with its offset, so the library names it by the student's day.
    static func localISO(_ d: Date) -> String {
        let f = ISO8601DateFormatter()
        f.timeZone = .current
        return f.string(from: d)
    }

    var payload: Payload {
        Payload(id: id, date: Self.localISO(started), class: className, title: title.isEmpty ? nil : title,
                title_typed: titleTyped && !title.isEmpty, transcript: transcript,
                notes: notes.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : notes, seconds: seconds, language: language)
    }
}

/// The recordings on this phone, each a .json (and its audio) in Application Support/Recordings. Ones that haven't
/// reached the library are sent when they're stopped, and again whenever the app comes back, until they do.
@MainActor @Observable
final class RecordingStore {
    static let shared = RecordingStore()

    private(set) var recordings: [Recording] = []
    let folder: URL

    init(folder: URL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appending(path: "Recordings")) {
        self.folder = folder
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        recordings = files.filter { $0.pathExtension == "json" }
            .compactMap { try? JSONDecoder.iso.decode(Recording.self, from: Data(contentsOf: $0)) }
            .sorted { $0.started > $1.started }
        // One left recording when the app was closed is kept as it stands, to be sent.
        for i in recordings.indices where recordings[i].state == .recording || recordings[i].state == .sending {
            recordings[i].state = .waiting
            write(recordings[i])
        }
    }

    /// The ones the Library shows: not yet in the library, or sent in the last day (until its lecture shows up).
    var unsent: [Recording] { recordings.filter { $0.state != .sent } }

    func audioURL(_ r: Recording) -> URL? { r.audio.map { folder.appending(path: $0) } }

    func save(_ r: Recording) {
        if let i = recordings.firstIndex(where: { $0.id == r.id }) { recordings[i] = r } else { recordings.insert(r, at: 0) }
        write(r)
    }

    private func write(_ r: Recording) {
        guard let data = try? JSONEncoder.iso.encode(r) else { return }
        try? data.write(to: folder.appending(path: "\(r.id).json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    func delete(_ r: Recording) {
        recordings.removeAll { $0.id == r.id }
        try? FileManager.default.removeItem(at: folder.appending(path: "\(r.id).json"))
        if let a = audioURL(r) { try? FileManager.default.removeItem(at: a) }
        if r.state != .sent { PhotoStore.shared.discard(lecture: r.id) }
    }

    /// Send one to the library. Nothing said means nothing to send: it's kept, and says so.
    func send(_ id: String, api: APIClient) async {
        guard var r = recordings.first(where: { $0.id == id }), r.state != .sent, r.state != .recording else { return }
        guard !r.transcript.isEmpty else {
            r.state = .failed
            r.error = "Nothing was heard in this recording."
            save(r)
            return
        }
        r.state = .sending
        r.error = nil
        save(r)
        do {
            struct Reply: Decodable { var id: String }
            let _: Reply = try await api.post("/lectures", r.payload)
            r.state = .sent
        } catch let e as APIError where e.status == 404 {
            // A library from before phones could send lectures: kept, and sent once it's updated.
            r.state = .waiting
            r.error = "Update Syft Notes on your computer to send recordings from this phone. It's sent then, on its own."
        } catch let e as APIError {
            r.state = e.unreachable ? .waiting : .failed
            r.error = e.unreachable ? nil : e.detail
        } catch {
            r.state = .waiting
            r.error = nil
        }
        save(r)
        // The whiteboard photos taken during it go once the lecture is there.
        if r.state == .sent { await PhotoStore.shared.sendWaiting(api: api) }
    }

    /// Everything waiting, sent now (when the app comes back, or the library can be reached again).
    func sendWaiting(api: APIClient) async {
        for r in recordings where r.state == .waiting { await send(r.id, api: api) }
        await PhotoStore.shared.sendWaiting(api: api)
        await NotebookStore.shared.sendWaiting(api: api)
    }
}

extension JSONDecoder {
    static let iso: JSONDecoder = { let d = JSONDecoder(); d.dateDecodingStrategy = .iso8601; return d }()
}

extension JSONEncoder {
    static let iso: JSONEncoder = { let e = JSONEncoder(); e.dateEncodingStrategy = .iso8601; return e }()
}
