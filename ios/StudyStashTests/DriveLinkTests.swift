import Foundation
import Testing
@testable import StudyStash

// The phone over Google Drive (docs/phone-google.md), against a folder in memory with the test playing the laptop.
// Only what could really break for a student: reads come from the copy without asking, a recording isn't lost or sent
// twice, Ask streams, and a laptop that's away says so.

/// Syft Notes' hidden folder, in memory.
actor FakeDrive: DriveFiles {
    struct File {
        var name: String
        var data: Data
        var modified: Date
    }

    private(set) var files: [String: File] = [:]
    /// Every file made, by name, in order.
    private(set) var made: [String] = []
    var offline = false
    private var next = 0

    func setOffline(_ on: Bool) { offline = on }

    private func reach() throws { if offline { throw DriveError.offline } }

    func find(_ name: String) async throws -> String? {
        try reach()
        return files.first { $0.value.name == name }?.key
    }

    func list(prefix: String) async throws -> [DriveFile] {
        try reach()
        return files.filter { $0.value.name.hasPrefix(prefix) }.map { DriveFile(id: $0.key, name: $0.value.name, modified: $0.value.modified) }
    }

    func read(_ id: String) async throws -> Data {
        try reach()
        guard let f = files[id] else { throw DriveError.notFound }
        return f.data
    }

    func create(_ name: String, _ data: Data, type: String) async throws -> String {
        try reach()
        next += 1
        let id = "f\(next)"
        files[id] = File(name: name, data: data, modified: .now)
        made.append(name)
        return id
    }

    func update(_ id: String, _ data: Data, type: String) async throws {
        try reach()
        guard files[id] != nil else { throw DriveError.notFound }
        files[id]?.data = data
        files[id]?.modified = .now
    }

    func delete(_ id: String) async throws {
        try reach()
        files[id] = nil
    }

    func named(_ name: String) -> (id: String, data: Data)? {
        files.first { $0.value.name == name }.map { ($0.key, $0.value.data) }
    }

    func names() -> [String] { files.values.map(\.name) }
}

/// The laptop's side, played by the test: it writes laptop.json and the copy, reads the phone's boxes and answers.
struct FakeLaptop {
    let drive: FakeDrive

    static var lectures: [[String: Any]] {
        [lecture("rec-3", "Integration by Parts", "Calculus II", "2026-10-22T09:00:00"),
         lecture("rec-2", "", "Unsorted", "2026-10-20T16:00:00"),
         lecture("rec-1", "Limits", "Calculus II", "2026-10-15T09:00:00")]
    }

    static func lecture(_ id: String, _ title: String, _ cls: String, _ date: String) -> [String: Any] {
        ["id": id, "title": title, "class": cls, "date": date, "seconds": 3000, "owner": "You", "status": "done",
         "summary": "", "topics": [], "has_transcript": true]
    }

    static var library: [String: Any] {
        ["name": "Joey's MacBook Air", "version": "0.17.3", "unsorted": 1, "writing": 0, "ask": true, "uses": ["student"],
         "for_school": true, "classes": [["name": "Calculus II", "lectures": 2, "color": 0, "group": "", "code": "", "aliases": []]]]
    }

    /// laptop.json and a copy- file for each answer.
    func publish(_ copy: [String: Any] = ["GET /library": library, "GET /lectures?limit=1000": lectures]) async throws {
        var index: [String: Any] = [:]
        for (key, body) in copy {
            let data = try JSONSerialization.data(withJSONObject: body)
            let id = try await drive.create("copy-\(DriveLink.fileName(key)).json", data, type: "application/json")
            index[key] = ["file": id, "sha": DriveLink.sha256(data), "size": data.count]
        }
        let laptop: [String: Any] = ["v": 1, "laptop": ["id": "l1", "name": "Joey's MacBook Air", "version": "0.17.3"],
                                     "seen": ISO8601DateFormatter().string(from: .now), "copy": index, "answered": []]
        _ = try await drive.create("laptop.json", try JSONSerialization.data(withJSONObject: laptop), type: "application/json")
    }

    /// The requests in every phone's box.
    func requests() async -> [[String: Any]] {
        var out: [[String: Any]] = []
        for name in await drive.names() where name.hasPrefix("phone-") {
            guard let (_, data) = await drive.named(name),
                  let box = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { continue }
            out += box["requests"] as? [[String: Any]] ?? []
        }
        return out
    }

    /// Write a request's answer into the answer file the phone made.
    func answer(_ request: [String: Any], _ answer: [String: Any]) async throws {
        let id = request["answer"] as! String
        try await drive.update(id, try JSONSerialization.data(withJSONObject: answer), type: "application/json")
    }
}

@Suite(.serialized) struct DriveLinkTests {
    let drive = FakeDrive()
    var laptop: FakeLaptop { FakeLaptop(drive: drive) }
    let folder = FileManager.default.temporaryDirectory.appending(path: "drive-test-\(UUID().uuidString)")
    static let fast = DriveLink.Timing(poll: .milliseconds(50), giveUp: .milliseconds(600), stall: .seconds(5),
                                       copyEvery: .seconds(10), openEvery: .seconds(60), watchEvery: .seconds(3))

    func link() -> DriveLink {
        let l = DriveLink(drive: drive, account: "sam@example.com", folder: folder, phoneName: "Sam's iPhone", phoneModel: "iPhone", timing: Self.fast)
        DriveLibrary.link = l
        return l
    }

    /// The app's own client, reaching the laptop through the URLProtocol as the screens do.
    var api: APIClient {
        let c = URLSessionConfiguration.ephemeral
        c.protocolClasses = [DriveLibrary.self]
        return APIClient(base: DriveLibrary.base, session: URLSession(configuration: c))
    }

    struct Recording: Encodable {
        var id = "phone-20261024-091500-a1b2c3"
        var date = "2026-10-24T09:15:00-07:00"
        var transcript = "[00:00] Today, limits."
        var seconds = 60.0
    }

    @Test func theCopyAnswersAReadWithoutAskingTheLaptop() async throws {
        try await laptop.publish()
        try await link().refresh()

        let calc = try await api.lectures(class: "Calculus II", limit: 1)
        #expect(calc.map(\.id) == ["rec-3"])
        #expect(try await api.lectures().map(\.id) == ["rec-3", "rec-2", "rec-1"])
        #expect(try await api.lectures(class: "Unsorted").map(\.id) == ["rec-2"])
        #expect(try await api.lectures(before: "2026-10-20T16:00:00").map(\.id) == ["rec-1"])
        #expect(try await api.library().name == "Joey's MacBook Air")
        let me = try await api.me()
        #expect(me.paired && me.library.name == "Joey's MacBook Air")
        // Nothing was asked: no box, no answer file.
        #expect(await drive.names().allSatisfy { !$0.hasPrefix("answer-") && !$0.hasPrefix("phone-") })
    }

    @Test func aRecordingSurvivesARestartAndIsSentOnce() async throws {
        try await laptop.publish()
        let before = link()
        try await before.refresh()
        await drive.setOffline(true)
        await #expect(throws: APIError.self) { let _: [String: String] = try await api.post("/lectures", Recording()) }

        // The app closes; it opens again, online.
        let after = link()
        await drive.setOffline(false)
        await after.watchWaiting()
        let asked = await laptop.requests()
        #expect(asked.count == 1)
        #expect(asked.first?["method"] as? String == "POST" && asked.first?["path"] as? String == "/lectures")
        #expect((asked.first?["json"] as? [String: Any])?["id"] as? String == Recording().id)

        try await laptop.answer(asked[0], ["id": asked[0]["id"]!, "status": 200, "body": ["id": Recording().id, "status": "queued"], "done": true])
        await after.watchWaiting()
        // Sent again (RecordingStore does, when the app comes back): the laptop's answer, without asking again.
        let reply: [String: String] = try await api.post("/lectures", Recording())
        #expect(reply["id"] == Recording().id)
        try await Task.sleep(for: .milliseconds(200))
        #expect(await laptop.requests().isEmpty)
        #expect(await drive.made.filter { $0.hasPrefix("answer-") }.count == 1)
    }

    /// Send the recording; the words if it failed.
    func send(_ api: APIClient) async -> String? {
        do {
            let _: [String: String] = try await api.post("/lectures", Recording())
            return nil
        } catch {
            return (error as? APIError)?.detail ?? "\(error)"
        }
    }

    @Test func theSameRecordingTwiceIsOneRequest() async throws {
        try await laptop.publish()
        try await link().refresh()
        let api = api
        async let first = send(api)
        async let second = send(api)
        // The laptop's asleep: both hear so, and the recording waits in the box, once.
        #expect(await first == DriveLink.notAnswering)
        #expect(await second == DriveLink.notAnswering)
        #expect(await send(api) == DriveLink.notAnswering)
        #expect(await laptop.requests().count == 1)
        #expect(await drive.made.filter { $0.hasPrefix("answer-") }.count == 1)
    }

    @Test func aStreamedAnswerReachesAskLineByLine() async throws {
        try await laptop.publish()
        try await link().refresh()
        let laptop = laptop
        let heard = Heard()
        // The laptop: the warm-up answered at once; the question in two pieces, the second only once the phone has
        // handed the first on.
        let playing = Task {
            var done = Set<String>()
            while !Task.isCancelled {
                for r in await laptop.requests() where !done.contains(r["id"] as! String) {
                    done.insert(r["id"] as! String)
                    if r["path"] as? String == "/ai/warm" {
                        try await laptop.answer(r, ["id": r["id"]!, "status": 200, "body": [:], "done": true])
                        continue
                    }
                    let one = #"{"kind":"text","text":"Integration "}"#
                    try await laptop.answer(r, ["id": r["id"]!, "status": 200, "lines": [one], "done": false])
                    var waited = 0
                    while await !heard.first, waited < 60 {
                        try await Task.sleep(for: .milliseconds(50))
                        waited += 1
                    }
                    await heard.laptopFinishing()
                    let two = #"{"kind":"text","text":"by parts"}"#
                    let end = #"{"kind":"done","reply":{"answer":"Integration by parts","sources":[],"engine":"claude","engine_name":"Claude","asked":"claude","asked_name":"Claude","fell_back":false,"why":""}}"#
                    try await laptop.answer(r, ["id": r["id"]!, "status": 200, "lines": [one, two, end], "done": true])
                }
                try await Task.sleep(for: .milliseconds(20))
            }
        }
        defer { playing.cancel() }

        await api.warm()
        let madeBefore = await drive.made.filter { $0.hasPrefix("answer-") }.count
        var texts: [String] = []
        var finished = false
        for try await event in try await api.ask("How do I choose u?") {
            switch event {
            case .text(let t):
                texts.append(t)
                await heard.heard()
            case .done: finished = true
            default: break
            }
        }
        #expect(texts == ["Integration ", "by parts"])
        #expect(finished)
        // The first piece reached the screen before the laptop had finished.
        #expect(await heard.firstInTime)
        // The answer file was made while typing (with the warm-up): asking added no file.
        #expect(await drive.made.filter { $0.hasPrefix("answer-") }.count == madeBefore)
    }

    @Test func twoFilesAttachedAtOnceAreTwoRequestsInOrderAnsweredTogether() async throws {
        try await laptop.publish()
        try await link().refresh()
        let laptop = laptop
        let order = Heard.Order()
        let playing = Task {
            var done = Set<String>()
            while !Task.isCancelled {
                for r in await laptop.requests() where !done.contains(r["id"] as! String) {
                    done.insert(r["id"] as! String)
                    let name = (r["upload"] as? [String: Any])?["name"] as? String ?? ""
                    await order.add(name)
                    try await laptop.answer(r, ["id": r["id"]!, "status": 200, "type": "application/json",
                                                "body": ["attachments": [["id": name, "name": name, "class": "Calculus II", "size": 3, "type": "image/jpeg",
                                                                          "added": "", "hasText": false, "reading": true]]], "done": true])
                }
                try await Task.sleep(for: .milliseconds(20))
            }
        }
        defer { playing.cancel() }
        let added = try await api.upload(files: [("page 1.jpg", "image/jpeg", Data("one".utf8)), ("page 2.jpg", "image/jpeg", Data("two".utf8))],
                                         class: "Calculus II", lecture: "rec-3")
        #expect(added.map(\.name) == ["page 1.jpg", "page 2.jpg"])
        // In the box together, in the order chosen (the laptop does a phone's writes in that order).
        #expect(await order.names == ["page 1.jpg", "page 2.jpg"])
        #expect(await drive.made.filter { $0.hasPrefix("upload-") }.count == 2)
    }

    @Test func aLaptopThatDoesntAnswerSaysSo() async throws {
        try await laptop.publish()
        try await link().refresh()
        do {
            _ = try await api.search("limits")
            Issue.record("a search with no laptop answered")
        } catch let e as APIError {
            #expect(e.unreachable)
            #expect(e.detail == "Your laptop isn't answering. Is it on and awake?")
        }
        // A read no one waits for any more is taken out of the box.
        try await Task.sleep(for: .milliseconds(300))
        #expect(await laptop.requests().isEmpty)
    }
}

/// Whether the laptop is there, as the banner and Ask's first screen tell the student before they try anything.
@Suite(.serialized) struct LaptopPresenceTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func aLaptopHeardFromLatelyIsHereAndOneQuietForMinutesIsAway() {
        #expect(LaptopPresence.evaluate(hasLaptop: true, seen: now.addingTimeInterval(-100), checked: true, now: now) == .here)
        let old = now.addingTimeInterval(-2 * 3600)
        #expect(LaptopPresence.evaluate(hasLaptop: true, seen: old, checked: true, now: now) == .away(since: old))
    }

    @Test func nothingIsSaidUntilTheLaptopsFileWasReadSinceTheAppOpened() {
        // The copy on the phone is from the last time: its `seen` always looks old.
        let old = now.addingTimeInterval(-2 * 3600)
        #expect(LaptopPresence.evaluate(hasLaptop: true, seen: old, checked: false, now: now) == .unknown)
        #expect(LaptopPresence.evaluate(hasLaptop: false, seen: nil, checked: false, now: now) == .unknown)
    }

    @Test func noLaptopInTheFolderIsAway() {
        #expect(LaptopPresence.evaluate(hasLaptop: false, seen: nil, checked: true, now: now) == .away(since: nil))
        // An older laptop that never wrote `seen` isn't called away.
        #expect(LaptopPresence.evaluate(hasLaptop: true, seen: nil, checked: true, now: now) == .unknown)
    }

    @Test func theWordsSayHowLongAgo() {
        #expect(LaptopPresence.lastHere(now.addingTimeInterval(-2 * 3600), now: now) == "Last here 2 hours ago.")
        #expect(LaptopPresence.lastHere(nil, now: now) == "It hasn't checked in yet.")
    }

    @Test func theLinkSaysTheFileWasReadOnceItHasBeen() async throws {
        let drive = FakeDrive()
        let folder = FileManager.default.temporaryDirectory.appending(path: "presence-test-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let link = DriveLink(drive: drive, account: "sam@example.com", folder: folder, phoneName: "Sam's iPhone", phoneModel: "iPhone")
        #expect(await link.status.checked == false)
        // A folder with no laptop in it is a read too: the laptop isn't there.
        try await link.refresh()
        let none = await link.status
        #expect(none.checked && none.laptop == nil)
        #expect(LaptopPresence.evaluate(hasLaptop: none.laptop != nil, seen: none.seen, checked: none.checked) == .away(since: nil))
    }
}

/// Whether the Ask screen had the first piece before the laptop wrote the rest.
actor Heard {
    private(set) var first = false
    private(set) var firstInTime = false
    private var finishing = false
    func heard() {
        if !first, !finishing { firstInTime = true }
        first = true
    }
    func laptopFinishing() { finishing = true }

    /// What the laptop saw, in order.
    actor Order {
        private(set) var names: [String] = []
        func add(_ name: String) { names.append(name) }
    }
}
