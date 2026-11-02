@preconcurrency import AVFoundation
@preconcurrency import ActivityKit
import Foundation
import Observation
import Speech
import UIKit

/// Records a lecture and writes it down as it's said, on this phone (SpeechAnalyzer, nothing leaves the phone until
/// it's sent to the library). Keeps the audio too, as AAC beside the recording. Keeps going with the screen locked.
@MainActor @Observable
final class Recorder {
    static let shared = Recorder()

    enum Phase: Equatable {
        case idle
        /// Getting the microphone, and the language's speech model (downloaded once).
        case preparing(String)
        case recording
        case paused
        /// Writing down the last words after Stop.
        case finishing
        case failed(String)
    }

    private(set) var phase: Phase = .idle
    /// The recording under way, as it stands.
    var recording: Recording?
    /// What's being said now, not settled yet.
    private(set) var volatile = ""
    /// How loud it is now, 0–1, for the waveform.
    private(set) var level: Float = 0
    private(set) var levels: [Float] = Array(repeating: 0, count: 48)
    private(set) var elapsed: TimeInterval = 0

    var isActive: Bool { phase == .recording || phase == .paused || phase == .finishing }

    @ObservationIgnored private var engine: AVAudioEngine?
    @ObservationIgnored private var analyzer: SpeechAnalyzer?
    @ObservationIgnored private var input: AsyncStream<AnalyzerInput>.Continuation?
    @ObservationIgnored private var resultsTask: Task<Void, Never>?
    @ObservationIgnored private var tickTask: Task<Void, Never>?
    @ObservationIgnored private var feeder: Feeder?
    @ObservationIgnored private var resumedAt: Date?
    @ObservationIgnored private var before: TimeInterval = 0
    @ObservationIgnored private let store: RecordingStore
    @ObservationIgnored private var activity: Activity<RecordingAttributes>?

    // MARK: the Live Activity

    private var activityState: RecordingAttributes.ContentState {
        RecordingAttributes.ContentState(countingFrom: Date.now.addingTimeInterval(-elapsed), paused: phase == .paused,
                                         elapsed: elapsed, finishing: phase == .finishing)
    }

    private func startActivity() {
        guard ActivityAuthorizationInfo().areActivitiesEnabled, let r = recording else {
            NSLog("[live] activities off")
            return
        }
        let attributes = RecordingAttributes(title: r.displayTitle, className: r.className, colorHex: nil)
        do {
            activity = try Activity.request(attributes: attributes, content: .init(state: activityState, staleDate: nil))
        } catch {
            NSLog("[live] couldn't start: %@", "\(error)")
        }
    }

    private func updateActivity() {
        guard let activity else { return }
        let state = activityState
        Task { @MainActor in await activity.update(.init(state: state, staleDate: nil)) }
    }

    private func endActivity() {
        guard let activity else { return }
        let state = activityState
        self.activity = nil
        Task { @MainActor in await activity.end(.init(state: state, staleDate: nil), dismissalPolicy: .immediate) }
    }

    init(store: RecordingStore = .shared) { self.store = store }

    // MARK: start, pause, stop

    /// Start a new recording for `className` (or none: the library sorts it).
    func start(className: String?, title: String = "") async {
        guard !isActive else { return }
        phase = .preparing("Getting the microphone…")
        guard await AVAudioApplication.requestRecordPermission() else {
            phase = .failed("Syft Notes can't use the microphone. Turn it on in Settings, then Syft Notes.")
            return
        }
        let id = Recording.newID()
        // A title handed in is a working one (an event's, "Calculus II, Fri 16 Oct"), as the desktop's: the library
        // still names the lecture from what's said unless the student types a title of their own.
        var r = Recording(id: id, started: .now, className: className, title: title, titleTyped: false)
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playAndRecord, mode: .spokenAudio, options: [.allowBluetoothHFP, .defaultToSpeaker, .mixWithOthers])
            try session.setActive(true)

            phase = .preparing("Getting ready to write down what's said…")
            #if targetEnvironment(simulator)
            // The simulator has no speech model: lines of a lecture stand in for what's heard.
            if DebugLaunch.fakeMicrophone {
                return startSimulated(r)
            }
            #endif
            let (transcriber, results, locale) = try await Self.transcriber()
            r.language = locale.identifier
            let analyzer = SpeechAnalyzer(modules: [transcriber])
            guard let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber]) else {
                throw RecorderError("This phone can't write down speech in \(locale.localizedString(forIdentifier: locale.identifier) ?? "this language").")
            }
            let (stream, continuation) = AsyncStream<AnalyzerInput>.makeStream()
            try await analyzer.start(inputSequence: stream)

            let engine = AVAudioEngine()
            let node = engine.inputNode
            let micFormat = node.outputFormat(forBus: 0)
            r.audio = "\(id).m4a"
            let feeder = try Feeder(from: micFormat, to: format, input: continuation,
                                    file: store.folder.appending(path: r.audio!)) { [weak self] level in
                Task { @MainActor in self?.heard(level) }
            }
            // @Sendable: the microphone calls this on its own audio thread. Written plainly inside this @MainActor
            // class, Swift 6 makes it main-actor-only and stops the app on the first buffer (on a phone, never in the
            // simulator, whose pretend microphone doesn't tap). Feeder is safe on any thread.
            node.installTap(onBus: 0, bufferSize: 4096, format: micFormat) { @Sendable buffer, _ in feeder.feed(buffer) }
            engine.prepare()
            try engine.start()

            self.engine = engine
            self.analyzer = analyzer
            self.input = continuation
            self.feeder = feeder
            recording = r
            store.save(r)
            before = 0
            resumedAt = .now
            elapsed = 0
            volatile = ""
            phase = .recording
            UIApplication.shared.isIdleTimerDisabled = false
            resultsTask = Task { [weak self] in await self?.read(results) }
            startActivity()
            tickTask = Task { [weak self] in
                while !Task.isCancelled {
                    try? await Task.sleep(for: .milliseconds(250))
                    self?.tick()
                }
            }
            UIImpactFeedbackGenerator(style: .medium).impactOccurred()
        } catch {
            teardown()
            phase = .failed((error as? RecorderError)?.words ?? "Couldn't start recording: \(error.localizedDescription)")
        }
    }

    #if targetEnvironment(simulator)
    private func startSimulated(_ r: Recording) {
        var r = r
        // `-record-typed "<lines>"`: notes already typed, for pictures of a recording with the student's own lines.
        if let typed = DebugLaunch.value("-record-typed") { r.notes = typed }
        recording = r
        store.save(r)
        before = 0
        resumedAt = .now
        elapsed = 0
        phase = .recording
        let lines = ["Okay, let's get started. Today is bottom-up parsing.",
                     "An LR parser reads the input left to right and builds a rightmost derivation in reverse.",
                     "It keeps a stack of states, and the parse table says whether to shift or reduce.",
                     "A shift pushes the next token. A reduce pops a handle and pushes the left-hand side.",
                     "If two actions land in one cell, that's a conflict, and the grammar isn't LR(1)."]
        let (stream, continuation) = AsyncThrowingStream<Heard, Error>.makeStream()
        resultsTask = Task { [weak self] in await self?.read(stream) }
        startActivity()
        tickTask = Task { [weak self] in
            var i = 0
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(150))
                guard let self else { return }
                self.tick()
                self.heard(self.phase == .recording ? Float.random(in: 0.2...0.9) : 0)
                if self.phase == .recording, i < lines.count * 20 {
                    let line = lines[(i / 20) % lines.count]
                    let words = line.split(separator: " ")
                    let shown = min(words.count, (i % 20) + 1)
                    continuation.yield(Heard(text: words.prefix(shown).joined(separator: " "), isFinal: i % 20 == 19, start: Double(i / 20) * 6))
                    i += 1
                }
            }
            continuation.finish()
        }
        input = nil
    }
    #endif

    func pause() {
        guard phase == .recording else { return }
        engine?.pause()
        before += resumedAt.map { Date.now.timeIntervalSince($0) } ?? 0
        resumedAt = nil
        phase = .paused
        tick()
        updateActivity()
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
    }

    func resume() {
        guard phase == .paused else { return }
        do {
            try engine?.start()
            resumedAt = .now
            phase = .recording
            updateActivity()
        } catch {
            phase = .failed("Couldn't carry on recording: \(error.localizedDescription)")
        }
    }

    /// Stop, write down the last words, keep it, and send it to the library.
    func stop(api: APIClient?) async {
        guard isActive, var r = recording else { return }
        tick()
        phase = .finishing
        updateActivity()
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        input?.finish()
        tickTask?.cancel()
        try? await analyzer?.finalizeAndFinishThroughEndOfInput()
        // The last words, for up to five seconds: a transcriber that never says it's done doesn't hold the recording.
        if let results = resultsTask {
            let waited = Task { await results.value }
            let timeout = Task { try? await Task.sleep(for: .seconds(5)); results.cancel() }
            await waited.value
            timeout.cancel()
        }
        feeder?.close()
        r = recording ?? r
        if !volatile.isEmpty { r.segments.append(.init(start: r.seconds, text: volatile)) }
        r.seconds = elapsed
        r.state = .waiting
        store.save(r)
        teardown()
        UINotificationFeedbackGenerator().notificationOccurred(.success)
        if let api {
            await store.send(r.id, api: api)
            // Ink written during it goes with it, once the library has the lecture.
            if store.recordings.first(where: { $0.id == r.id })?.state == .sent {
                for n in NotebookStore.shared.notebooks where n.lectureID == r.id {
                    let inked = n.pages.contains { !NotebookStore.shared.drawing(n, $0).strokes.isEmpty }
                    if inked { try? await NotebookStore.shared.upload(n.id, api: api) }
                }
            }
        }
    }

    /// Throw it away.
    func discard() {
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        input?.finish()
        feeder?.close()
        if let r = recording { store.delete(r) }
        teardown()
    }

    private func teardown() {
        endActivity()
        resultsTask?.cancel()
        tickTask?.cancel()
        engine = nil
        analyzer = nil
        input = nil
        feeder = nil
        recording = nil
        volatile = ""
        levels = Array(repeating: 0, count: levels.count)
        phase = .idle
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    /// "This matters": the moment, marked in what's typed, so the notes make something of it.
    func mark() {
        guard isActive else { return }
        let at = Format.clock(elapsed)
        let said = (recording?.segments.last?.text ?? volatile).split(separator: " ").suffix(8).joined(separator: " ")
        update { r in
            let line = "- Important at \(at)" + (said.isEmpty ? "" : ": “…\(said)”")
            r.notes = r.notes.isEmpty || r.notes.hasSuffix("\n") ? r.notes + line + "\n" : r.notes + "\n" + line + "\n"
        }
        UIImpactFeedbackGenerator(style: .rigid).impactOccurred()
    }

    /// Edit what was typed, the title or the class while it records.
    func update(_ change: (inout Recording) -> Void) {
        guard var r = recording else { return }
        change(&r)
        recording = r
        store.save(r)
    }

    // MARK: what's heard

    private func read(_ results: AsyncThrowingStream<Heard, Error>) async {
        do {
            for try await result in results {
                let text = result.text
                if result.isFinal {
                    let start = result.start.isFinite ? result.start : elapsed
                    update { r in
                        // A new line about every sentence or two, as the library's transcripts read.
                        if let last = r.segments.last, start - last.start < 8, last.text.count < 160 {
                            r.segments[r.segments.count - 1].text += " " + text.trimmingCharacters(in: .whitespaces)
                        } else {
                            r.segments.append(.init(start: start, text: text.trimmingCharacters(in: .whitespaces)))
                        }
                    }
                    volatile = ""
                } else {
                    volatile = text
                }
            }
        } catch {
            if isActive && phase != .finishing { phase = .failed("Stopped writing down what's said: \(error.localizedDescription)") }
        }
    }

    private func heard(_ level: Float) {
        self.level = level
        levels.removeFirst()
        levels.append(level)
    }

    private func tick() {
        guard phase == .recording || phase == .paused else { return }
        elapsed = before + (resumedAt.map { Date.now.timeIntervalSince($0) } ?? 0)
        if var r = recording, Int(r.seconds) != Int(elapsed) {
            r.seconds = elapsed
            recording = r
            if Int(elapsed) % 10 == 0 { store.save(r) }
        }
    }

    // MARK: the speech model

    /// What the transcriber heard: settled, or still being worked out.
    struct Heard: Sendable {
        var text: String
        var isFinal: Bool
        var start: Double
    }

    /// The transcriber for the phone's language (or English), its model downloaded the first time: SpeechTranscriber,
    /// made for long talks, where the phone has it, else DictationTranscriber (older phones, and the simulator).
    static func transcriber() async throws -> (any SpeechModule, AsyncThrowingStream<Heard, Error>, Locale) {
        if SpeechTranscriber.isAvailable {
            var found = await SpeechTranscriber.supportedLocale(equivalentTo: .current)
            if found == nil { found = await SpeechTranscriber.supportedLocale(equivalentTo: Locale(identifier: "en-US")) }
            if let locale = found {
                let t = SpeechTranscriber(locale: locale, transcriptionOptions: [], reportingOptions: [.volatileResults],
                                          attributeOptions: [.audioTimeRange])
                try await install(t)
                return (t, relay(t.results) { Heard(text: String($0.text.characters), isFinal: $0.isFinal, start: $0.range.start.seconds) }, locale)
            }
        }
        var found = await DictationTranscriber.supportedLocale(equivalentTo: .current)
        if found == nil { found = await DictationTranscriber.supportedLocale(equivalentTo: Locale(identifier: "en-US")) }
        guard let locale = found else { throw RecorderError("This phone can't write down speech in its language yet.") }
        let t = DictationTranscriber(locale: locale, contentHints: [], transcriptionOptions: [.punctuation],
                                     reportingOptions: [.volatileResults], attributeOptions: [.audioTimeRange])
        try await install(t)
        return (t, relay(t.results) { Heard(text: String($0.text.characters), isFinal: $0.isFinal, start: $0.range.start.seconds) }, locale)
    }

    private static func install(_ module: any SpeechModule) async throws {
        do {
            if let request = try await AssetInventory.assetInstallationRequest(supporting: [module]) { try await request.downloadAndInstall() }
        } catch {
            throw RecorderError("Couldn't get this phone's speech model. Connect to the internet once, then try again.")
        }
    }

    private static func relay<S: AsyncSequence & Sendable>(_ results: S, _ map: @escaping @Sendable (S.Element) -> Heard)
        -> AsyncThrowingStream<Heard, Error> where S.Element: Sendable {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    for try await r in results { continuation.yield(map(r)) }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }
}

struct RecorderError: Error {
    var words: String
    init(_ words: String) { self.words = words }
}

/// On the audio thread: each buffer from the microphone, converted to what the transcriber takes and handed to it,
/// written to the AAC file, and measured for the waveform.
private final class Feeder: @unchecked Sendable {
    private let converter: AVAudioConverter
    private let target: AVAudioFormat
    private let input: AsyncStream<AnalyzerInput>.Continuation
    private var file: AVAudioFile?
    private let onLevel: @Sendable (Float) -> Void
    private let lock = NSLock()

    init(from source: AVAudioFormat, to target: AVAudioFormat, input: AsyncStream<AnalyzerInput>.Continuation, file url: URL,
         onLevel: @escaping @Sendable (Float) -> Void) throws {
        guard let converter = AVAudioConverter(from: source, to: target) else { throw RecorderError("Couldn't read the microphone.") }
        self.converter = converter
        self.target = target
        self.input = input
        self.onLevel = onLevel
        let settings: [String: Any] = [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 32_000, AVNumberOfChannelsKey: 1,
                                       AVEncoderBitRateKey: 48_000]
        file = try? AVAudioFile(forWriting: url, settings: settings, commonFormat: source.commonFormat, interleaved: source.isInterleaved)
    }

    func feed(_ buffer: AVAudioPCMBuffer) {
        lock.lock()
        defer { lock.unlock() }
        try? file?.write(from: buffer)
        onLevel(Self.level(buffer))

        let ratio = target.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount((Double(buffer.frameLength) * ratio).rounded(.up)) + 16
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var given = false
        var error: NSError?
        converter.convert(to: out, error: &error) { _, status in
            if given { status.pointee = .noDataNow; return nil }
            given = true
            status.pointee = .haveData
            return buffer
        }
        if error == nil, out.frameLength > 0 { input.yield(AnalyzerInput(buffer: out)) }
    }

    func close() {
        lock.lock()
        file = nil
        lock.unlock()
    }

    /// Loudness 0–1 from the buffer's RMS, on a decibel scale.
    static func level(_ buffer: AVAudioPCMBuffer) -> Float {
        guard let data = buffer.floatChannelData?[0], buffer.frameLength > 0 else { return 0 }
        var sum: Float = 0
        for i in 0..<Int(buffer.frameLength) { sum += data[i] * data[i] }
        let rms = sqrt(sum / Float(buffer.frameLength))
        let db = 20 * log10(max(rms, 0.000_01))
        return max(0, min(1, (db + 55) / 50))
    }
}
