import CryptoKit
import Foundation

/// How the phone is connected over Google Drive, for Settings and setup: the account, the laptop and when it was
/// last there.
struct DriveStatus: Sendable, Equatable {
    var email: String
    /// Nil until the laptop has put its file in the folder (or after it disconnected).
    var laptop: String?
    var version: String?
    /// When the laptop last said it was there (`seen` in laptop.json).
    var seen: Date?
    /// Writes waiting in the box for the laptop.
    var waiting: Int
    /// Google stopped letting the phone in (the sign-in ended, the Drive box was unticked): its words.
    var problem: String?
    /// The laptop's file was read since the app last came to the front: only then does `seen` say anything about now
    /// (before it, it's the copy from the last time).
    var checked = false
}

/// The phone's side of the phone over Google Drive (docs/phone-google.md). The laptop keeps a copy of its library's
/// answers in Syft Notes' hidden Drive folder; this keeps it on the phone and answers reads from it at once (offline
/// too). Anything else is a request in the phone's box: the laptop does it and writes the answer into the answer file
/// the phone made for it, which the phone reads by id until it's done. Writes are kept on the phone until the laptop
/// has done them, through restarts; asking for the same write again joins the one waiting.
actor DriveLink {
    struct Timing: Sendable {
        /// Reading an answer while a screen waits for it.
        var poll: Duration = .milliseconds(500)
        /// No answer at all by then: the laptop isn't there.
        var giveUp: Duration = .seconds(25)
        /// A streamed answer that stops changing for this long has stopped.
        var stall: Duration = .seconds(120)
        var copyEvery: Duration = .seconds(10)
        var openEvery: Duration = .seconds(60)
        /// Writes no screen is waiting for any more.
        var watchEvery: Duration = .seconds(3)
        /// How far ahead the box says the phone is open.
        var openFor: TimeInterval = 90
    }

    /// A request as the app's API client made it.
    struct Incoming: Sendable {
        var method: String
        /// After /api/v2, decoded ("/lectures/rec-abc"), without the query.
        var path: String
        /// After /api/v2 as it was sent, with its query ("/search?class=Calculus%20II&q=limits"): what the laptop replays.
        var raw: String
        var query: [String: String]
        var body: Data?
        var type: String?
    }

    /// What a request answers with, piece by piece: the status and type first, then the body (all at once, or line by
    /// line for a streamed answer).
    enum Piece: Sendable {
        case head(Int, String)
        case data(Data)
    }

    /// The laptop's words when it doesn't answer.
    static let notAnswering = "Your laptop isn't answering. Is it on and awake?"
    static let noLaptop = "Your laptop isn't connected to Google yet. On your laptop, open Settings, then Phone, and choose Connect with Google."

    struct Failure: Error, Equatable {
        var words: String
    }

    let drive: any DriveFiles
    let prefix: String
    let account: String
    let timing: Timing
    private let folder: URL
    private let phoneName: String
    private let phoneModel: String
    private var state: State
    private var laptop: Laptop?
    /// The copy on the phone: each key's sha (its body is a file in copy/).
    private var index: [String: String]
    /// Answers finished lately, by request id, for anyone else waiting on the same one (the last 50).
    private var results: [String: Finished] = [:]
    private var resultOrder: [String] = []
    private var problem: GoogleProblem?
    /// laptop.json was read (found or not) since the app last came to the front.
    private var checked = false
    private var preparing: Set<String> = []
    private var boxBusy = false
    private var boxWaiters: [CheckedContinuation<Void, Never>] = []
    /// Until when the box says the phone's open (now, until the app comes to the front).
    private var openUntil = Date()
    private var loop: Task<Void, Never>?
    private var refreshing: Task<Void, Error>?

    /// Told when something the screens show has changed (on the main actor): the laptop, the copy, a write done.
    private var onChange: (@Sendable (DriveStatus, _ wrote: String?) -> Void)?

    init(drive: any DriveFiles, account: String, prefix: String = "", folder: URL, phoneName: String, phoneModel: String,
         timing: Timing = Timing()) {
        self.drive = drive
        self.account = account
        self.prefix = prefix
        self.folder = folder
        self.phoneName = phoneName
        self.phoneModel = phoneModel
        self.timing = timing
        for sub in ["copy", "uploads", "blobs"] {
            try? FileManager.default.createDirectory(at: folder.appending(path: sub), withIntermediateDirectories: true)
        }
        var s = (try? JSONDecoder.iso.decode(State.self, from: Data(contentsOf: folder.appending(path: "state.json"))))
            ?? State(phoneID: UUID().uuidString.lowercased())
        // Nobody waits for what was asked before the app last closed: questions are dropped (their files go when the
        // box is next written), writes are watched until the laptop does them.
        for i in s.requests.indices { s.requests[i].gaveUp = true }
        s.dropped += s.requests.filter { !$0.write }.compactMap(\.answer)
        s.requests.removeAll { !$0.write }
        state = s
        laptop = (try? Data(contentsOf: folder.appending(path: "laptop.json"))).flatMap(Laptop.init(data:))
        index = (try? JSONDecoder().decode([String: String].self, from: Data(contentsOf: folder.appending(path: "copy/index.json")))) ?? [:]
        try? JSONEncoder.iso.encode(s).write(to: folder.appending(path: "state.json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    func onChange(_ tell: @escaping @Sendable (DriveStatus, _ wrote: String?) -> Void) {
        onChange = tell
        tell(status, nil)
    }

    var status: DriveStatus {
        DriveStatus(email: account, laptop: laptop?.name, version: laptop?.version, seen: laptop?.seen,
                    waiting: state.requests.filter { $0.write }.count, problem: problem?.words, checked: checked)
    }

    var phoneID: String { state.phoneID }

    /// Whether the laptop has put its file in the folder (as far as the phone last knew).
    var hasLaptop: Bool { laptop != nil }

    // MARK: answering the app

    /// Answer one request from the app (`DriveLibrary`, the URLProtocol, hands them over).
    nonisolated func handle(_ r: Incoming) -> AsyncThrowingStream<Piece, Error> {
        AsyncThrowingStream { out in
            let task = Task { await self.serve(r, out) }
            out.onTermination = { _ in task.cancel() }
        }
    }

    private typealias Out = AsyncThrowingStream<Piece, Error>.Continuation

    private func serve(_ r: Incoming, _ out: Out) async {
        if r.method == "GET", let (type, body) = await answerHere(r) {
            out.yield(.head(200, type))
            out.yield(.data(body))
            out.finish()
            return
        }
        if laptop == nil, !Self.isWrite(r.method, r.path) {
            // Nothing to ask: the laptop hasn't connected to Google (or disconnected).
            out.finish(throwing: Failure(words: Self.noLaptop))
            return
        }
        if r.method == "POST", r.path == "/ai/warm" { Task { await self.makeSpare() } }
        let parts: [Request]
        do {
            parts = try requests(for: r)
        } catch {
            out.finish(throwing: Failure(words: "This phone couldn't send that. Try again."))
            return
        }
        if parts.count == 1 {
            await run(parts[0], out)
        } else {
            await runAll(parts, path: r.path, out)
        }
    }

    // MARK: reads, from the copy

    /// A read the phone answers itself: from the copy, `/me` from the laptop's file, a file it already fetched.
    private func answerHere(_ r: Incoming) async -> (String, Data)? {
        let json = "application/json"
        switch r.path {
        case "/me":
            let me: [String: Any] = ["paired": laptop != nil,
                                     "library": ["name": laptop?.name ?? "Your laptop", "version": laptop?.version ?? ""]]
            return (json, (try? JSONSerialization.data(withJSONObject: me)) ?? Data())
        case "/lectures":
            guard let all = await copied(laptop?.keys.lectures) else { return nil }
            return (json, Self.lectures(all, class: r.query["class"], limit: r.query["limit"].flatMap(Int.init), before: r.query["before"]))
        case "/attachments":
            guard let all = await copied(laptop?.keys.attachments) else { return nil }
            return (json, Self.attachments(all, class: r.query["class"], lecture: r.query["lecture"]))
        default:
            if let id = Self.rawAttachment(r.path), let kept = blob(id) { return kept }
            guard let key = laptop?.keys.find(method: r.method, path: r.path, query: r.query), let body = await copied(key) else { return nil }
            return (json, body)
        }
    }

    /// A copied answer's body: the one on the phone if it's the laptop's latest, else fetched now (it's newer, or
    /// the first copy hasn't reached it yet).
    private func copied(_ key: String?) async -> Data? {
        guard let key, let entry = laptop?.copy[key] else { return nil }
        let file = folder.appending(path: "copy/\(Self.fileName(key))")
        let kept = try? Data(contentsOf: file)
        if index[key] == entry.sha, let kept { return kept }
        do {
            let data = try await drive.read(entry.file)
            keep(key, data, sha: entry.sha)
            saveIndex()
            return data
        } catch {
            return kept
        }
    }

    private func keep(_ key: String, _ data: Data, sha: String) {
        let got = Self.sha256(data)
        try? data.write(to: folder.appending(path: "copy/\(Self.fileName(key))"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        // Changed again since the laptop's file was read: kept as it is, and fetched again when the file says so.
        index[key] = got == sha ? sha : got
    }

    private func saveIndex() {
        try? JSONEncoder().encode(index).write(to: folder.appending(path: "copy/index.json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    /// GET /lectures as the library answers it (`Reader.Lectures`), from every lecture newest first: one class's
    /// (exactly its name, "Unsorted" too), dated before `before` (compared as text), at most `limit` (50 unless said,
    /// 1 to 500).
    static func lectures(_ all: Data, class cls: String?, limit: Int?, before: String?) -> Data {
        var list = (try? JSONSerialization.jsonObject(with: all) as? [[String: Any]]) ?? []
        if let cls, !cls.isEmpty { list = list.filter { $0["class"] as? String == cls } }
        if let before, !before.isEmpty { list = list.filter { ($0["date"] as? String ?? "") < before } }
        let n = min(max(limit ?? 50, 1), 500)
        return (try? JSONSerialization.data(withJSONObject: Array(list.prefix(n)))) ?? Data("[]".utf8)
    }

    /// GET /attachments as the library answers it: a lecture's (when `lecture` is given), else a class's, else all.
    static func attachments(_ all: Data, class cls: String?, lecture: String?) -> Data {
        let o = (try? JSONSerialization.jsonObject(with: all) as? [String: Any]) ?? [:]
        var list = o["attachments"] as? [[String: Any]] ?? []
        let lecture = lecture?.trimmingCharacters(in: .whitespaces), cls = cls?.trimmingCharacters(in: .whitespaces)
        if let lecture, !lecture.isEmpty {
            list = list.filter { $0["lecture"] as? String == lecture }
        } else if let cls, !cls.isEmpty {
            list = list.filter { $0["class"] as? String == cls }
        }
        return (try? JSONSerialization.data(withJSONObject: ["attachments": list])) ?? Data()
    }

    // MARK: the copy, kept up to date

    /// Read the laptop's file, and fetch whatever in the copy changed. Once at a time; a second caller waits for it.
    func refresh() async throws {
        if let running = refreshing {
            try await running.value
            return
        }
        let t = Task { try await self.refreshNow() }
        refreshing = t
        defer { refreshing = nil }
        do {
            try await t.value
        } catch let e as GoogleProblem {
            trouble(e)
            throw e
        }
    }

    /// Google stopped letting the phone in: the app hears it (and sends the student to sign in again).
    private func trouble(_ e: GoogleProblem) {
        guard e == .ended || e == .noFolder || e == .removed, problem != e else { return }
        problem = e
        tell()
    }

    private func refreshNow() async throws {
        let raw = try await laptopFile()
        // Read (or found not to be there): `seen` now means something. Told once what was read has been taken in.
        let firstRead = !checked
        defer {
            if firstRead {
                checked = true
                tell()
            }
        }
        guard let raw else {
            // No laptop in the folder: never connected, or it disconnected (and took its copy away).
            if laptop != nil {
                laptop = nil
                index = [:]
                try? FileManager.default.removeItem(at: folder.appending(path: "laptop.json"))
                try? FileManager.default.removeItem(at: folder.appending(path: "copy"))
                try? FileManager.default.createDirectory(at: folder.appending(path: "copy"), withIntermediateDirectories: true)
                tell()
            }
            return
        }
        guard let read = Laptop(data: raw) else { return }
        // Removed on the laptop (Settings → Remove): it won't answer this phone again. Signing in again makes a new
        // box (signing out forgets this one's id).
        if read.removed.contains(state.phoneID) {
            trouble(.removed)
            return
        }
        let before = laptop
        laptop = read
        try? raw.write(to: folder.appending(path: "laptop.json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        // Answers the laptop finished whose files are gone (it deletes its own after a day): done.
        for r in state.requests where read.answered.contains(r.id) && r.sent && r.gaveUp {
            if (try? await drive.read(r.answer ?? "")) == nil { complete(r.id, Finished(status: 200, type: "application/json", body: Data("{}".utf8))) }
        }
        // What changed, the list and the newest lectures first; what's gone, dropped.
        let wanted = read.copy.filter { index[$0.key] != $0.value.sha }.map(\.key).sorted { read.priority($0) < read.priority($1) }
        for gone in index.keys where read.copy[gone] == nil {
            index[gone] = nil
            try? FileManager.default.removeItem(at: folder.appending(path: "copy/\(Self.fileName(gone))"))
        }
        var fetched = 0
        for chunk in stride(from: 0, to: wanted.count, by: 6).map({ Array(wanted[$0..<min($0 + 6, wanted.count)]) }) {
            let got = await withTaskGroup(of: (String, Data?).self) { group in
                for key in chunk {
                    let file = read.copy[key]!.file
                    group.addTask { (key, try? await self.drive.read(file)) }
                }
                var out: [(String, Data?)] = []
                for await x in group { out.append(x) }
                return out
            }
            for (key, data) in got {
                guard let data, let sha = laptop?.copy[key]?.sha else { continue }
                keep(key, data, sha: sha)
                fetched += 1
            }
            saveIndex()
        }
        if before?.name != read.name || before?.seen != read.seen || before?.version != read.version || fetched > 0 { tell() }
    }

    /// laptop.json's contents: by its id once known (fast), else found by name. Nil when there's none.
    private func laptopFile() async throws -> Data? {
        if let id = state.laptopFile {
            do {
                return try await drive.read(id)
            } catch DriveError.notFound {
                state.laptopFile = nil
                save()
            }
        }
        guard let id = try await drive.find(name("laptop.json")) else { return nil }
        state.laptopFile = id
        save()
        return try await drive.read(id)
    }

    // MARK: requests (phone → laptop)

    /// The request(s) for what the app sent: a multipart upload with several files is one request a file (the box
    /// carries one file a request), answered together.
    private func requests(for r: Incoming) throws -> [Request] {
        let now = ISO8601DateFormatter().string(from: .now)
        let write = Self.isWrite(r.method, r.path)
        if let type = r.type, type.hasPrefix("multipart/form-data"), let body = r.body {
            let form = try Multipart(body: body, type: type)
            return try form.files.map { f in
                let id = UUID().uuidString.lowercased()
                let local = id
                try f.data.write(to: folder.appending(path: "uploads/\(local)"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
                let sig = Self.sha256(Data("\(r.method) \(r.raw) \(form.fields.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }.joined(separator: "&")) \(f.name) \(f.type) ".utf8) + Data(Self.sha256(f.data).utf8))
                return Request(id: id, at: now, method: r.method, path: r.raw, json: nil,
                               upload: Upload(file: nil, name: f.name, type: f.type, fields: form.fields, local: local),
                               answer: nil, signature: sig, write: write)
            }
        }
        // The body as JSON (keys sorted, so the same request twice is the same text).
        let json = r.body.flatMap { $0.isEmpty ? nil : $0 }.flatMap { try? JSONSerialization.jsonObject(with: $0, options: .fragmentsAllowed) }
            .flatMap { try? JSONSerialization.data(withJSONObject: $0, options: [.sortedKeys, .fragmentsAllowed]) }
        let sig = Self.sha256(Data("\(r.method) \(r.raw) ".utf8) + (json ?? Data()))
        return [Request(id: UUID().uuidString.lowercased(), at: now, method: r.method, path: r.raw, json: json, upload: nil,
                        answer: nil, signature: sig, write: write)]
    }

    /// A write stays in the box until the laptop does it; reads, questions and the warm-up don't.
    static func isWrite(_ method: String, _ path: String) -> Bool {
        method != "GET" && !["/ai/ask", "/chat", "/ai/warm"].contains(path)
    }

    static func isStreamed(_ method: String, _ path: String) -> Bool {
        method == "POST" && (path == "/ai/ask" || path == "/chat")
    }

    private enum Submitted {
        /// Already answered (a write done after its screen stopped waiting).
        case done(Finished)
        /// In the box (or joined to the same write already there): its id, and how long to wait for an answer.
        case waiting(String, Duration)
    }

    /// Put a request in the box, in the order asked; the same write already waiting is joined instead.
    private func submit(_ req: Request) -> Submitted {
        var req = req
        if req.write, let waiting = state.requests.first(where: { $0.write && $0.signature == req.signature }) {
            // The same write is already waiting: this joins it instead of asking twice. One the laptop hasn't done
            // in all the time it's waited isn't waited for again here (it's still watched).
            discardLocal(req)
            return .waiting(waiting.id, waiting.gaveUp ? .seconds(1) : timing.giveUp)
        }
        if req.write, let i = state.unseen.firstIndex(where: { $0.signature == req.signature }) {
            // Done after its screen stopped waiting: this is its answer.
            discardLocal(req)
            let done = state.unseen.remove(at: i)
            save()
            return .done(done.result)
        }
        if Self.isStreamed(req.method, req.path.split(separator: "?").first.map(String.init) ?? req.path),
           let spare = state.spare, spare.at > .now.addingTimeInterval(-3600) {
            // Ready since the student started typing: asking is one write (the box).
            req.id = spare.id
            req.answer = spare.file
            state.spare = nil
        }
        state.requests.append(req)
        save()
        return .waiting(req.id, timing.giveUp)
    }

    /// Send one request and hand on its answer.
    private func run(_ req: Request, _ out: Out) async {
        switch submit(req) {
        case .done(let f): deliver(f, out)
        case let .waiting(id, giveUp): await watch(id, giveUp: giveUp, out)
        }
    }

    /// Several requests (a multi-file upload) answered as one: all in the box at once, in order (the laptop does a
    /// phone's writes in its box's order), then the attachments of each, together. One not answered leaves the rest
    /// waiting with it.
    private func runAll(_ parts: [Request], path: String, _ out: Out) async {
        let submitted = parts.map(submit)
        for case let .waiting(id, _) in submitted where request(id)?.sent == false { try? await prepare(id) }
        try? await pushBox()
        var all: [Any] = []
        for (n, sub) in submitted.enumerated() {
            let later = submitted.dropFirst(n + 1).compactMap { s -> String? in if case let .waiting(id, _) = s { id } else { nil } }
            let finished: Finished
            switch sub {
            case .done(let f):
                finished = f
            case let .waiting(id, giveUp):
                let (stream, inner) = AsyncThrowingStream<Piece, Error>.makeStream()
                let task = Task { await self.watch(id, giveUp: giveUp, inner) }
                inner.onTermination = { _ in task.cancel() }
                var status = 0
                var type = "application/json"
                var body = Data()
                do {
                    for try await p in stream {
                        switch p {
                        case let .head(s, t): status = s; type = t
                        case .data(let d): body.append(d)
                        }
                    }
                } catch {
                    // Not answered: the files after it wait in the box with it.
                    for id in later { markGaveUp(id) }
                    out.finish(throwing: error)
                    return
                }
                _ = await task.value
                finished = Finished(status: status, type: type, body: body)
            }
            guard (200..<300).contains(finished.status) else {
                for id in later { markGaveUp(id) }
                deliver(finished, out)
                return
            }
            let o = (try? JSONSerialization.jsonObject(with: finished.body)) as? [String: Any]
            all += o?["attachments"] as? [Any] ?? []
        }
        out.yield(.head(200, "application/json"))
        out.yield(.data((try? JSONSerialization.data(withJSONObject: ["attachments": all])) ?? Data()))
        out.finish()
    }

    /// Wait for a request's answer, reading it by id, and hand it on as it comes. No answer by `giveUp`: a read or a
    /// question is taken out of the box; a write stays (watched in the background) and the screen hears the laptop's
    /// words.
    private func watch(_ id: String, giveUp: Duration, _ out: Out) async {
        let clock = ContinuousClock()
        let start = clock.now
        var lastSend: ContinuousClock.Instant?
        var head = false
        var handed = 0
        var lastRaw: Data?
        var changed = start
        while true {
            if Task.isCancelled {
                // The screen went away: a question goes, a write keeps waiting.
                if let r = request(id) { r.write ? markGaveUp(id) : drop(id) }
                out.finish(throwing: CancellationError())
                return
            }
            guard let r = request(id) else {
                if let done = results[id] { deliver(done, out) } else { out.finish(throwing: Failure(words: Self.notAnswering)) }
                return
            }
            if !r.sent, lastSend.map({ clock.now - $0 > .seconds(2) }) ?? true {
                lastSend = clock.now
                do {
                    try await prepare(id)
                    try await pushBox()
                } catch {
                    // Google turned the phone away, or there's no internet: said at once. A write is kept, and sent
                    // when it can be.
                    let words: String
                    if let e = error as? GoogleProblem {
                        trouble(e)
                        words = e.words
                    } else if error as? DriveError == .offline {
                        words = GoogleProblem.offline.words
                    } else {
                        words = Self.notAnswering
                    }
                    if r.write { markGaveUp(id) } else { drop(id) }
                    out.finish(throwing: Failure(words: words))
                    return
                }
            }
            if let r = request(id), r.sent, let file = r.answer {
                let raw: Data?
                do {
                    raw = try await drive.read(file)
                } catch DriveError.notFound {
                    raw = nil
                    if laptop?.answered.contains(id) == true {
                        complete(id, Finished(status: 200, type: "application/json", body: Data("{}".utf8)))
                        continue
                    }
                } catch {
                    raw = nil
                }
                if let raw, let a = Answer(data: raw), let status = a.status {
                    if raw != lastRaw {
                        lastRaw = raw
                        changed = clock.now
                    }
                    if let lines = a.lines {
                        if !head {
                            head = true
                            out.yield(.head(status, a.type ?? "application/x-ndjson"))
                        }
                        for line in lines.dropFirst(handed) { out.yield(.data(Data((line + "\n").utf8))) }
                        handed = max(handed, lines.count)
                        if a.done {
                            out.finish()
                            complete(id, nil)
                            return
                        }
                    } else if a.done {
                        let finished: Finished
                        if let blob = a.blob {
                            do {
                                let data = try await drive.read(blob)
                                if let att = Self.rawAttachment(r.path) { keepBlob(att, data, type: a.type ?? "application/octet-stream") }
                                finished = Finished(status: status, type: a.type ?? "application/octet-stream", body: data)
                            } catch {
                                try? await Task.sleep(for: timing.poll)
                                continue
                            }
                        } else {
                            finished = Finished(status: status, type: a.type ?? "application/json", body: a.body ?? Data("{}".utf8))
                        }
                        complete(id, finished)
                        deliver(finished, out)
                        return
                    } else {
                        head = true
                    }
                }
            }
            if !head, clock.now - start >= giveUp {
                if let r = request(id), r.write { markGaveUp(id) } else { drop(id) }
                out.finish(throwing: Failure(words: Self.notAnswering))
                return
            }
            if head, clock.now - changed >= timing.stall {
                drop(id)
                out.finish(throwing: Failure(words: Self.notAnswering))
                return
            }
            try? await Task.sleep(for: timing.poll)
        }
    }

    private func deliver(_ f: Finished, _ out: Out) {
        out.yield(.head(f.status, f.type))
        out.yield(.data(f.body))
        out.finish()
    }

    /// A request's answer is in: out of the box, its answer file deleted (the phone deletes those once read), and
    /// kept for anyone else waiting on it, or for the next time the same write is sent if no screen saw it.
    private func complete(_ id: String, _ result: Finished?) {
        guard let i = state.requests.firstIndex(where: { $0.id == id }) else { return }
        let r = state.requests.remove(at: i)
        if let result {
            results[id] = result
            resultOrder.append(id)
            if resultOrder.count > 50 { results[resultOrder.removeFirst()] = nil }
            if r.gaveUp, r.write { state.unseen = Array((state.unseen + [Unseen(signature: r.signature, result: result, at: .now)]).suffix(50)) }
        }
        if let local = r.upload?.local { try? FileManager.default.removeItem(at: folder.appending(path: "uploads/\(local)")) }
        if let file = r.answer { state.dropped.append(file) }
        save()
        Task { try? await self.pushBox() }
        if r.write {
            onChange?(status, r.path)
            // The laptop refreshes its copy straight after a change: read it again soon.
            Task {
                try? await Task.sleep(for: .seconds(2))
                try? await self.refresh()
            }
        }
    }

    /// Out of the box without an answer (a read or a question no one waits for any more).
    private func drop(_ id: String) {
        guard let i = state.requests.firstIndex(where: { $0.id == id }) else { return }
        let r = state.requests.remove(at: i)
        if let file = r.answer { state.dropped.append(file) }
        if let file = r.upload?.file { state.dropped.append(file) }
        if let local = r.upload?.local { try? FileManager.default.removeItem(at: folder.appending(path: "uploads/\(local)")) }
        save()
        if r.sent { Task { try? await self.pushBox() } }
    }

    private func markGaveUp(_ id: String) {
        guard let i = state.requests.firstIndex(where: { $0.id == id }) else { return }
        state.requests[i].gaveUp = true
        save()
        onChange?(status, nil)
    }

    private func discardLocal(_ r: Request) {
        if let local = r.upload?.local { try? FileManager.default.removeItem(at: folder.appending(path: "uploads/\(local)")) }
    }

    private func request(_ id: String) -> Request? { state.requests.first { $0.id == id } }

    /// Make what the box will point at: the answer file (`{}`) and the uploaded file. Each made once.
    private func prepare(_ id: String) async throws {
        while preparing.contains(id) { try await Task.sleep(for: .milliseconds(50)) }
        guard let r = request(id), r.answer == nil || (r.upload != nil && r.upload?.file == nil) else { return }
        preparing.insert(id)
        defer { preparing.remove(id) }
        if r.answer == nil {
            let file = try await drive.create(name("answer-\(id).json"), Data("{}".utf8), type: "application/json")
            if let i = state.requests.firstIndex(where: { $0.id == id }) { state.requests[i].answer = file } else { state.dropped.append(file) }
            save()
        }
        if let u = request(id)?.upload, u.file == nil {
            let bytes = try Data(contentsOf: folder.appending(path: "uploads/\(u.local)"))
            let file = try await drive.create(name("upload-\(id)"), bytes, type: u.type)
            if let i = state.requests.firstIndex(where: { $0.id == id }) { state.requests[i].upload?.file = file } else { state.dropped.append(file) }
            save()
        }
    }

    /// The phone's box written as it is now: who the phone is, whether it's open, and every request that's ready.
    /// One writer at a time, so a newer box is never overwritten by an older one.
    func pushBox() async throws {
        await lockBox()
        defer { unlockBox() }
        let ready = state.requests.filter(\.ready)
        let iso = ISO8601DateFormatter()
        let box: [String: Any] = [
            "v": 1,
            "phone": ["id": state.phoneID, "name": phoneName, "model": phoneModel],
            "open_until": iso.string(from: openUntil),
            "requests": ready.map { $0.boxJSON },
        ]
        let data = try JSONSerialization.data(withJSONObject: box, options: [.sortedKeys])
        if let id = state.boxFile {
            do {
                try await drive.update(id, data, type: "application/json")
            } catch DriveError.notFound {
                state.boxFile = nil
            }
        }
        if state.boxFile == nil {
            let name = name("phone-\(state.phoneID).json")
            if let found = try await drive.find(name) {
                try await drive.update(found, data, type: "application/json")
                state.boxFile = found
            } else {
                state.boxFile = try await drive.create(name, data, type: "application/json")
            }
        }
        let sent = Set(ready.map(\.id))
        for i in state.requests.indices where sent.contains(state.requests[i].id) { state.requests[i].sent = true }
        save()
        // Files no request points at any more: the answers read, questions given up on.
        let dropped = state.dropped
        if !dropped.isEmpty {
            state.dropped = []
            save()
            Task { for f in dropped { try? await self.drive.delete(f) } }
        }
    }

    private func lockBox() async {
        if boxBusy {
            await withCheckedContinuation { boxWaiters.append($0) }
        } else {
            boxBusy = true
        }
    }

    private func unlockBox() {
        if boxWaiters.isEmpty { boxBusy = false } else { boxWaiters.removeFirst().resume() }
    }

    /// An answer file made ahead, as soon as the student starts typing a question, so sending it is one write.
    private func makeSpare() async {
        if let s = state.spare, s.at > .now.addingTimeInterval(-3600) { return }
        if let old = state.spare { state.dropped.append(old.file) }
        let id = UUID().uuidString.lowercased()
        state.spare = nil
        guard let file = try? await drive.create(name("answer-\(id).json"), Data("{}".utf8), type: "application/json") else { return }
        if state.spare == nil { state.spare = Spare(id: id, file: file, at: .now) } else { state.dropped.append(file) }
        save()
    }

    // MARK: in the front, in the back

    /// The app came to the front (or went to the back): the box says so, and while it's in front the copy is read
    /// every 10 s, the box rewritten every 60 s, and writes waiting watched.
    func foreground(_ on: Bool) async {
        loop?.cancel()
        loop = nil
        openUntil = on ? .now.addingTimeInterval(timing.openFor) : .now
        // The copy of the laptop's file is as old as the last time the app was open: until it's read again (the loop's
        // first thing, straight away) nothing is said about whether the laptop's there.
        if on, checked {
            checked = false
            tell()
        }
        try? await pushBox()
        guard on else { return }
        loop = Task { await self.run() }
    }

    private func run() async {
        let clock = ContinuousClock()
        var nextCopy = clock.now
        var nextOpen = clock.now + timing.openEvery
        var nextWatch = clock.now
        while !Task.isCancelled {
            if clock.now >= nextCopy {
                nextCopy = clock.now + timing.copyEvery
                try? await refresh()
            }
            if clock.now >= nextOpen {
                nextOpen = clock.now + timing.openEvery
                openUntil = .now.addingTimeInterval(timing.openFor)
                try? await pushBox()
            }
            if clock.now >= nextWatch {
                nextWatch = clock.now + timing.watchEvery
                await watchWaiting()
            }
            try? await Task.sleep(for: .milliseconds(250))
        }
    }

    /// Once, in the background (the system's app refresh): the copy read, waiting writes sent and looked at, old
    /// stray uploads cleared.
    func backgroundPass() async {
        try? await refresh()
        await watchWaiting()
        await clean()
    }

    /// Writes no screen waits for: sent if they aren't yet, and finished when their answer is in.
    func watchWaiting() async {
        for r in state.requests where r.gaveUp {
            if !r.sent {
                do {
                    try await prepare(r.id)
                    try await pushBox()
                } catch {
                    return
                }
                continue
            }
            guard let file = r.answer else { continue }
            let raw: Data
            do {
                raw = try await drive.read(file)
            } catch DriveError.notFound {
                if laptop?.answered.contains(r.id) == true {
                    complete(r.id, Finished(status: 200, type: "application/json", body: Data("{}".utf8)))
                } else if let i = state.requests.firstIndex(where: { $0.id == r.id }) {
                    // Gone without an answer (the laptop disconnected, which deletes answer files): a new one, so the
                    // laptop can answer it when it's back.
                    state.requests[i].answer = nil
                    state.requests[i].sent = false
                    save()
                }
                continue
            } catch {
                continue
            }
            guard let a = Answer(data: raw), a.done, let s = a.status else { continue }
            complete(r.id, Finished(status: s, type: a.type ?? "application/json", body: a.body ?? Data("{}".utf8)))
        }
    }

    /// Uploads of this phone's kind left in the folder for more than a day (nothing needs them now), once a day.
    func clean() async {
        if let last = state.cleaned, last > .now.addingTimeInterval(-86_400) { return }
        guard let files = try? await drive.list(prefix: name("upload-")) else { return }
        let mine = Set(state.requests.compactMap { $0.upload?.file })
        for f in files where f.modified < .now.addingTimeInterval(-86_400) && !mine.contains(f.id) { try? await drive.delete(f.id) }
        state.cleaned = .now
        save()
    }

    /// Signing out: this phone's box, uploads and answer files go from the folder, and its copy from the phone.
    func signOut() async {
        loop?.cancel()
        loop = nil
        var files = [state.boxFile, state.spare?.file].compactMap { $0 } + state.dropped
        for r in state.requests { files += [r.answer, r.upload?.file].compactMap { $0 } }
        for f in files { try? await drive.delete(f) }
        try? FileManager.default.removeItem(at: folder)
    }

    // MARK: files the phone fetched (an attachment's bytes)

    private func blob(_ attachment: String) -> (String, Data)? {
        let base = folder.appending(path: "blobs/\(Self.fileName(attachment))")
        guard let data = try? Data(contentsOf: base), let type = try? String(contentsOf: base.appendingPathExtension("type"), encoding: .utf8) else { return nil }
        return (type, data)
    }

    private func keepBlob(_ attachment: String, _ data: Data, type: String) {
        let base = folder.appending(path: "blobs/\(Self.fileName(attachment))")
        try? data.write(to: base, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        try? Data(type.utf8).write(to: base.appendingPathExtension("type"), options: .atomic)
    }

    /// The attachment's id in "/attachments/<id>/raw".
    static func rawAttachment(_ path: String) -> String? {
        let p = path.split(separator: "?").first.map(String.init) ?? path
        let parts = p.split(separator: "/").map(String.init)
        guard parts.count == 3, parts[0] == "attachments", parts[2] == "raw" else { return nil }
        return parts[1].removingPercentEncoding ?? parts[1]
    }

    // MARK: names and saving

    private func name(_ base: String) -> String { prefix + base }

    private func save() {
        try? JSONEncoder.iso.encode(state).write(to: folder.appending(path: "state.json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    private func tell() { onChange?(status, nil) }

    /// The copy- file's name for a key (and the phone's own name for it): the first 16 hex of its sha256.
    static func fileName(_ key: String) -> String { String(sha256(Data(key.utf8)).prefix(16)) }

    static func sha256(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
}

// MARK: - What's kept

extension DriveLink {
    struct State: Codable, Sendable {
        var phoneID: String
        var boxFile: String?
        var laptopFile: String?
        var requests: [Request] = []
        /// Writes the laptop did after their screen stopped waiting: the answer for the next send of the same one.
        var unseen: [Unseen] = []
        var spare: Spare?
        /// Files of the phone's to delete from the folder at the next chance.
        var dropped: [String] = []
        var cleaned: Date?
    }

    struct Request: Codable, Sendable {
        var id: String
        var at: String
        var method: String
        /// After /api/v2, with its query, as sent.
        var path: String
        /// The body, JSON.
        var json: Data?
        var upload: Upload?
        /// The answer file's id in the folder, once made.
        var answer: String?
        /// The same method, path and body give the same signature.
        var signature: String
        var write: Bool
        /// No screen waits for it now (it gave up, or the app closed): watched in the background.
        var gaveUp = false
        /// In the box in the folder.
        var sent = false

        var ready: Bool { answer != nil && (upload == nil || upload?.file != nil) }

        var boxJSON: [String: Any] {
            var o: [String: Any] = ["id": id, "at": at, "method": method, "path": path, "answer": answer ?? NSNull(),
                                    "json": json.flatMap { try? JSONSerialization.jsonObject(with: $0, options: .fragmentsAllowed) } ?? NSNull()]
            if let u = upload {
                o["upload"] = ["file": u.file ?? NSNull(), "name": u.name, "type": u.type, "fields": u.fields] as [String: Any]
            }
            return o
        }
    }

    struct Upload: Codable, Sendable {
        var file: String?
        var name: String
        var type: String
        var fields: [String: String]
        /// The bytes, waiting on the phone (uploads/<local>) until the laptop has them.
        var local: String
    }

    struct Spare: Codable, Sendable {
        var id: String
        var file: String
        var at: Date
    }

    struct Finished: Codable, Sendable {
        var status: Int
        var type: String
        var body: Data
    }

    struct Unseen: Codable, Sendable {
        var signature: String
        var result: Finished
        var at: Date
    }

    /// An answer file as the laptop writes it.
    struct Answer {
        var status: Int?
        var type: String?
        var body: Data?
        var lines: [String]?
        var blob: String?
        var done: Bool

        init?(data: Data) {
            guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
            status = (o["status"] as? NSNumber)?.intValue
            type = o["type"] as? String
            // A body that isn't JSON comes as a JSON string: handed on as its text.
            if let text = o["body"] as? String, !(type ?? "").lowercased().contains("json") {
                body = Data(text.utf8)
            } else {
                body = o["body"].flatMap { $0 is NSNull ? nil : try? JSONSerialization.data(withJSONObject: $0, options: .fragmentsAllowed) }
            }
            lines = o["lines"] as? [String]
            blob = o["blob"] as? String
            done = o["done"] as? Bool ?? false
        }
    }

    /// laptop.json, as read.
    struct Laptop: Sendable {
        var id: String
        var name: String
        var version: String
        var seen: Date?
        var copy: [String: (file: String, sha: String)]
        var answered: Set<String>
        /// Phones removed on the laptop (their ids).
        var removed: Set<String>
        var keys: Keys

        init?(data: Data) {
            guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
                  let l = o["laptop"] as? [String: Any] else { return nil }
            id = l["id"] as? String ?? ""
            name = l["name"] as? String ?? "Your laptop"
            version = l["version"] as? String ?? ""
            seen = (o["seen"] as? String).flatMap { ISO8601DateFormatter().date(from: $0) }
            var c: [String: (file: String, sha: String)] = [:]
            for (k, v) in o["copy"] as? [String: Any] ?? [:] {
                guard let e = v as? [String: Any], let file = e["file"] as? String, let sha = e["sha"] as? String else { continue }
                c[k] = (file, sha)
            }
            copy = c
            answered = Set(o["answered"] as? [String] ?? [])
            removed = Set(o["removed"] as? [String] ?? [])
            keys = Keys(c.keys)
        }

        /// What to fetch first: the library, the list and what Home shows, then lectures (their notes before their
        /// pictures), then the rest.
        func priority(_ key: String) -> Int {
            if key == "GET /library" || key == keys.lectures { return 0 }
            if ["GET /canvas/due", "GET /calendar/upcoming", "GET /chats"].contains(key) || key == keys.attachments { return 1 }
            if key.hasPrefix("GET /lectures/") { return key.hasSuffix("/rendered") ? 3 : 2 }
            return 4
        }
    }

    /// The copy's keys, found by what they ask for (method, path decoded, query decoded and sorted), whatever the
    /// laptop's encoding.
    struct Keys: Sendable {
        private var byNormal: [String: String] = [:]
        /// Every lecture ("GET /lectures?limit=…"), and every attachment ("GET /attachments").
        var lectures: String?
        var attachments: String?

        init(_ keys: some Sequence<String>) {
            for k in keys {
                guard let (method, path, query) = Self.parse(k) else { continue }
                byNormal[Self.normal(method, path, query)] = k
                if method == "GET", path == "/lectures", Set(query.keys).isSubset(of: ["limit"]) { lectures = k }
                if method == "GET", path == "/attachments", query.isEmpty { attachments = k }
            }
        }

        func find(method: String, path: String, query: [String: String]) -> String? { byNormal[Self.normal(method, path, query)] }

        static func parse(_ key: String) -> (String, String, [String: String])? {
            let parts = key.split(separator: " ", maxSplits: 1).map(String.init)
            guard parts.count == 2, let c = URLComponents(string: parts[1]) else { return nil }
            let query = Dictionary((c.queryItems ?? []).map { ($0.name, $0.value ?? "") }, uniquingKeysWith: { a, _ in a })
            return (parts[0], c.path, query)
        }

        static func normal(_ method: String, _ path: String, _ query: [String: String]) -> String {
            "\(method) \(path)?" + query.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }.joined(separator: "&")
        }
    }
}

// MARK: - A multipart upload, taken apart

/// The parts of `multipart/form-data`, as `APIClient.upload` makes them: plain fields, and files.
struct Multipart {
    struct File {
        var name: String
        var type: String
        var data: Data
    }

    var fields: [String: String] = [:]
    var files: [File] = []

    struct Unreadable: Error {}

    init(body: Data, type: String) throws {
        guard let b = type.components(separatedBy: "boundary=").last?.trimmingCharacters(in: CharacterSet(charactersIn: "\" ")),
              !b.isEmpty else { throw Unreadable() }
        let delimiter = Data("--\(b)".utf8)
        let headEnd = Data("\r\n\r\n".utf8)
        var cursor = body.startIndex
        guard let first = body.range(of: delimiter, in: cursor..<body.endIndex) else { throw Unreadable() }
        cursor = first.upperBound
        while cursor < body.endIndex {
            // "--" after the delimiter ends it.
            if body[cursor...].starts(with: Data("--".utf8)) { break }
            guard let h = body.range(of: headEnd, in: cursor..<body.endIndex) else { throw Unreadable() }
            let head = String(decoding: body[cursor..<h.lowerBound], as: UTF8.self)
            guard let next = body.range(of: Data("\r\n".utf8) + delimiter, in: h.upperBound..<body.endIndex) else { throw Unreadable() }
            let content = body[h.upperBound..<next.lowerBound]
            var name = "", filename: String?, ctype = "application/octet-stream"
            for line in head.components(separatedBy: "\r\n") {
                let lower = line.lowercased()
                if lower.hasPrefix("content-disposition:") {
                    name = Self.param("name", in: line) ?? ""
                    filename = Self.param("filename", in: line)
                } else if lower.hasPrefix("content-type:") {
                    ctype = line.dropFirst("content-type:".count).trimmingCharacters(in: .whitespaces)
                }
            }
            if let filename {
                files.append(File(name: filename, type: ctype, data: Data(content)))
            } else if !name.isEmpty {
                fields[name] = String(decoding: content, as: UTF8.self)
            }
            cursor = next.upperBound
        }
    }

    private static func param(_ key: String, in line: String) -> String? {
        guard let r = line.range(of: " \(key)=\"") else { return nil }
        let rest = line[r.upperBound...]
        guard let end = rest.firstIndex(of: "\"") else { return nil }
        return String(rest[..<end])
    }
}
