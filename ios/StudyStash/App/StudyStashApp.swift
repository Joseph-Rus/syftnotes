import CoreSpotlight
import SwiftUI
import UserNotifications

@main
struct StudyStashApp: App {
    @State private var app = AppModel()
    /// Light, dark, or as the phone is (Settings' choice).
    @AppStorage(Appearance.key) private var appearance = Appearance.system.rawValue

    init() {
        UNUserNotificationCenter.current().delegate = NotificationRouter.shared
        SystemLook.apply()
        // Pull to refresh shows the pixel s's Row Shuffle (syftRefreshable), not the system's spinner.
        UIRefreshControl.appearance().tintColor = .clear
        // Pause and Stop on the lock screen: wired as the app starts, not when its first screen appears, so a tap that
        // wakes the app in the background still reaches the recorder.
        RecordingControl.stop = { await Recorder.shared.stop(api: AppModel.current?.api) }
        RecordingControl.togglePause = { Recorder.shared.phase == .paused ? Recorder.shared.resume() : Recorder.shared.pause() }
        RecordingControl.isRecording = { Recorder.shared.isActive }
        // A Live Activity left by a recording the app didn't finish (closed, or it crashed): nothing records at launch.
        Task { @MainActor in
            if !Recorder.shared.isActive { await RecordingControl.endLeftovers() }
        }
    }

    var body: some Scene {
        WindowGroup {
            RootView()
                // The pixel s sorting itself in, over the first screen until it's ready.
                .overlay { StartSplash() }
                .environment(app)
                // The desktop's look: no accent (the tint is ink), one typeface, the page's white behind.
                .tint(.ink)
                .font(.app(.body))
                .preferredColorScheme(Appearance(rawValue: appearance)?.scheme)
                .task { NotificationRouter.shared.app = app }
        }
        .backgroundTask(.appRefresh(Reminders.refreshTask)) {
            await app.refreshInBackground()
            await Reminders.scheduleRefresh()
        }
    }
}

/// Where a tap goes inside a tab: push one with `NavigationLink(value:)`, or from anywhere with `app.go(_:tab:root:)`.
enum Route: Hashable {
    case classDetail(String)
    /// A lecture; with `at`, opened on its transcript at that second (`@Environment(\.lectureMoment)`).
    case lecture(String, at: Double? = nil)
    case assignment(class: String, id: Int)
    case search(String)
    /// A recording on this phone that hasn't reached the library (its id).
    case recording(String)
    /// Settings, pushed from Home's toolbar.
    case settings
}

extension EnvironmentValues {
    /// The second a lecture page opens at (on its transcript), when it was opened from a moment: an Ask source or a
    /// search passage. Nil opens it at the top.
    @Entry var lectureMoment: Double? = nil
}

struct RootView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        Group {
            switch app.phase {
            case .unpaired:
                PairView().transition(.opacity)
            case .checking, .ready:
                MainTabs().id(app.api?.base).transition(.opacity)
            }
        }
        .animation(.smooth, value: app.phase)
        .task {
            #if DEBUG
            await DebugLaunch.pairIfAsked(app)
            #endif
            if app.phase == .checking { await app.check() }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active, app.phase != .unpaired, DebugLaunch.value("-library") == nil { Task { await app.check() } }
            if phase == .background { Reminders.scheduleRefresh() }
            if phase == .active || phase == .background { app.driveForeground(phase == .active) }
        }
    }
}

/// The tabs (Home, Due, Ask, Search), the Record bar above them, and what the app presents over everything.
struct MainTabs: View {
    /// The tabs, by the name pages used before.
    typealias TabID = StudyStash.TabID
    @Environment(AppModel.self) private var app
    /// Light or dark, for the swatches in an iPad's sidebar (pictures, so they don't follow it themselves).
    @Environment(\.colorScheme) private var scheme
    /// The classes are tabs only where there's a sidebar (an iPad, not split narrow): a phone counted them as tabs and
    /// put a More tab in its bar.
    @Environment(\.horizontalSizeClass) private var sizeClass
    #if DEBUG
    @State private var debugSheet: DebugScreen?
    @State private var debugCover: DebugScreen?
    @State private var debugUses = DebugLaunch.flag("-uses") || DebugLaunch.value("-show") == "uses"
    #endif

    var body: some View {
        @Bindable var app = app
        // What's presented: a notebook over the whole screen, anything else in a sheet (read here, so a change shows).
        let sheet = app.incoming?.isNotebook == false ? app.incoming : nil
        let notebook = app.incoming?.isNotebook == true ? app.incoming : nil
        TabView(selection: $app.tab) { tabs }
        .tabViewStyle(.sidebarAdaptable)
        // An iPad opens on the sidebar, as the desktop always has it (the tabs along the top stay a tap away).
        .defaultAdaptableTabBarPlacement(.sidebar)
        .tabViewSidebarBottomBar { SidebarFooter() }
        .tabBarMinimizeBehavior(.onScrollDown)
        .modifier(RecordBar(enabled: app.tab != .ask && !app.recordBarHidden))
        // A class's tab goes when the sidebar does (an iPad split narrow): its page shows on Home instead.
        .onChange(of: sizeClass) { _, size in
            if size != .regular, case .folder(let name) = app.tab { app.go(.classDetail(name), tab: .home, root: true) }
        }
        #if DEBUG
        // What it's for is a step of setup, or a row on Home for a library never told: never a sheet at launch. This
        // one is only for pictures (`-show uses`).
        .sheet(isPresented: $debugUses) { UsesView(onboarding: true) }
        #endif
        .sheet(isPresented: $app.showingRecorder) {
            RecordView(startClass: app.recordFor)
        }
        .sheet(item: Binding(get: { sheet }, set: { app.incoming = $0 })) { incoming in
            switch incoming {
            case let .attach(cls, lecture): AddFilesView(className: cls, lecture: lecture)
            case let .files(files, cls, lecture): AddFilesView(className: cls, lecture: lecture, files: files)
            case .audio(let file): ImportAudioView(file: file)
            case .goodnotes(let url): GoodNotesImportView(url: url)
            case .handwriting, .notebook: EmptyView()
            }
        }
        // A notebook is the whole screen, as paper is: never a card over Home.
        .fullScreenCover(item: Binding(get: { notebook }, set: { app.incoming = $0 })) { incoming in
            switch incoming {
            case let .handwriting(cls, lecture): HandwritingView(className: cls, lecture: lecture)
            case .notebook(let id): NotebookEditor(id: id)
            default: EmptyView()
            }
        }
        #if DEBUG
        .sheet(item: $debugSheet) { s in
            if case .ask(let scope) = s { AskSheet(scope: scope) }
        }
        .fullScreenCover(item: $debugCover) { s in
            switch s {
            case let .study(id, _): StudyView(lectureID: id, title: DebugSamples.title(of: id))
            case .notebook: NotebookEditor(id: DebugSamples.notebook())
            case .components(let page): ComponentGallery(page: page)
            default: WidgetGallery()
            }
        }
        #endif
        .onOpenURL { app.open($0) }
        .onContinueUserActivity(CSSearchableItemActionType) { activity in
            if let id = activity.userInfo?[CSSearchableItemActivityIdentifier] as? String { app.go(.lecture(id), root: true) }
        }
        .task {
            #if DEBUG
            if let r = DebugLaunch.route {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { app.go(r) }
            }
            #endif
            // Record from Control Center and Siri (Pause and Stop are wired in init).
            RecordingControl.stop = { [app] in await Recorder.shared.stop(api: app.api) }
            RecordingControl.start = { [app] in app.record(for: app.recordClass) }
            // Notifications are asked for in setup's last step, with the reason said; here only their Record button.
            Reminders.registerActions()
            if let u = try? await app.api?.upcoming() { app.upcoming = u }
            await app.refreshInBackground()
            #if DEBUG
            if DebugLaunch.simulatesRecording { app.record(for: app.recordClass) }
            switch DebugLaunch.show {
            case .addFiles: app.incoming = .files(DebugSamples.files(), className: "Calculus II", lecture: nil)
            case .importAudio: app.incoming = .audio(DebugSamples.audio())
            case .goodnotes: app.incoming = .goodnotes(DebugSamples.goodNotes())
            case .handwriting: app.incoming = .handwriting(className: "Calculus II", lecture: nil)
            case let s? where s.isCover: debugCover = s
            case .ask?: debugSheet = DebugLaunch.show
            default: break
            }
            if let path = DebugLaunch.value("-open-file") { app.open(URL(fileURLWithPath: path)) }
            if let dir = DebugLaunch.value("-export-notebooks") {
                for n in NotebookStore.shared.notebooks {
                    try? NotebookStore.shared.pdfData(n).write(to: URL(fileURLWithPath: dir).appending(path: "\(n.title).pdf"))
                }
            }
            #endif
        }
    }

    /// The tabs. Made here, apart from the TabView, so Swift type-checks them in parts (the iPad's class sections inline
    /// in the body were too much for Swift 6.2's type checker).
    @TabContentBuilder<TabID> private var tabs: some TabContent<TabID> {
        // The desktop's names and icons: Home and Due as its sidebar has them. Settings is on Home's toolbar.
        Tab("Home", image: "home", value: TabID.home) { LibraryTab() }
        if app.forSchool { Tab("Due", image: "event_upcoming", value: TabID.due) { DueTab() } }
        Tab("Ask", image: "chat", value: TabID.ask) { AskTab() }
        Tab("Search", image: "search", value: TabID.search, role: .search) { SearchTab() }
        if sizeClass == .regular { folderSections }
    }

    /// An iPad's sidebar: each class under its group, as the desktop's sidebar (never in a phone's tab bar).
    @TabContentBuilder<TabID> private var folderSections: some TabContent<TabID> {
        ForEach(app.folderGroups) { group in
            TabSection(group.name) {
                ForEach(group.classes) { c in folderTab(c) }
            }
            .tabPlacement(.sidebarOnly)
        }
    }

    /// A class in the sidebar: its swatch and name; its page, on a stack of its own.
    private func folderTab(_ c: ClassInfo) -> some TabContent<TabID> {
        Tab(value: TabID.folder(c.name)) {
            FolderTab(name: c.name)
        } label: {
            Label { Text(c.name) } icon: { SwatchImage.of(app.hue(of: c.name), dark: scheme == .dark) }
        }
    }
}

/// A class's page as an iPad sidebar tab, with its own stack.
private struct FolderTab: View {
    @Environment(AppModel.self) private var app
    let name: String

    var body: some View {
        NavigationStack(path: app.stack(.folder(name))) { ClassView(name: name).routes() }
    }
}

/// Record, above the tabs on every tab but Ask: "Record Calculus II" (the page's class, else the one on now), or
/// "Record"; while recording, the recording. Hidden on pages that hide the tab bar.
private struct RecordBar: ViewModifier {
    var enabled: Bool
    @Environment(AppModel.self) private var app

    func body(content: Content) -> some View {
        if #available(iOS 26.1, *) {
            content.tabViewBottomAccessory(isEnabled: enabled) { RecordAccessory(classNow: app.recordClass) }
        } else {
            content.tabViewBottomAccessory { RecordAccessory(classNow: app.recordClass) }
        }
    }
}

/// The destinations every tab's stack can push.
struct RouteDestinations: ViewModifier {
    func body(content: Content) -> some View {
        content.navigationDestination(for: Route.self) { route in
            switch route {
            case .classDetail(let name): ClassView(name: name)
            case let .lecture(id, at): LectureView(id: id).environment(\.lectureMoment, at)
            case let .assignment(cls, id): AssignmentView(className: cls, id: id)
            case .search(let q): SearchResultsView(query: q)
            case .recording(let id): PhoneRecordingView(id: id)
            case .settings: SettingsTab()
            }
        }
    }
}

extension View {
    func routes() -> some View { modifier(RouteDestinations()) }
}

/// Launch arguments for pictures and testing in the simulator (debug builds only), where there's no camera to scan
/// with and no one to tap. Every screen can be opened from them; ios/DESIGN.md lists them all.
/// - `-demo` uses the demo library in the app; `-unpaired` forgets any library and shows setup (pairing), and with
///   `-pair-code` its second step; `-pair <address> <code>` pairs; `-library <address>` uses a library with no password.
/// - `-tab home|due|ask|search` opens a tab (`settings` pushes Settings on Home); `-search <words>` opens Search
///   with them typed.
/// - `-open lecture:<id>`, `class:<name>`, `assignment:<class>:<id>`, `search:<words>`, `recording:<id>` or
///   `settings` pushes that screen on Home (through `app.go`);
///   `-pane notes|transcript|files` picks a lecture's tab.
/// - `-show <screen>` opens a sheet: `record`, `ask`, `ask:lecture:<id>`, `ask:class:<name>`, `add-files`,
///   `import-audio`, `goodnotes`, `handwriting`, `notebook`, `study:<id>`, `quiz:<id>`, `uses`, `widgets`.
/// - `-laptop-away` (with `-demo`) shows the laptop asleep or off, two hours since it was last there (the banner, Ask's
///   first screen); `-laptop-back [seconds]` starts the same and has it come back after that long (4 s).
/// - `-record` records (in the simulator, made-up lines stand in for what's heard), `-ask "<question>"` asks it on
///   the Ask tab, `-quiet` never asks to send notifications.
/// A screen's own file can read a flag of its own with `DebugLaunch.flag("-x")` or `DebugLaunch.value("-x")`.
enum DebugLaunch {
    /// What follows `flag` (`offset` places on).
    static func value(_ flag: String, _ offset: Int = 1) -> String? {
        #if DEBUG
        let args = ProcessInfo.processInfo.arguments
        guard let i = args.firstIndex(of: flag), args.indices.contains(i + offset) else { return nil }
        return args[i + offset]
        #else
        return nil
        #endif
    }

    /// Whether `flag` was given at all.
    static func flag(_ flag: String) -> Bool {
        #if DEBUG
        ProcessInfo.processInfo.arguments.contains(flag)
        #else
        false
        #endif
    }

    static var tab: TabID? {
        switch value("-tab") {
        case "home", "library", "settings": .home
        case "due": .due
        case "ask": .ask
        case "search": .search
        default: value("-search") != nil ? .search : nil
        }
    }

    static var route: Route? {
        if value("-tab") == "settings" { return .settings }
        guard let v = value("-open") else { return nil }
        if v.hasPrefix("lecture:") { return .lecture(String(v.dropFirst(8))) }
        if v.hasPrefix("class:") { return .classDetail(String(v.dropFirst(6))) }
        if v.hasPrefix("search:") { return .search(String(v.dropFirst(7))) }
        if v.hasPrefix("recording:") { return .recording(String(v.dropFirst(10))) }
        if v == "settings" { return .settings }
        if v.hasPrefix("assignment:"), let colon = v.lastIndex(of: ":"), let id = Int(v[v.index(after: colon)...]) {
            return .assignment(class: String(v[v.index(v.startIndex, offsetBy: 11)..<colon]), id: id)
        }
        return nil
    }

    /// The made-up lecture lines stand in for the microphone (the simulator has no speech model).
    static var simulatesRecording: Bool { flag("-record") || value("-show") == "record" }

    /// The made-up lines stand in for the microphone whenever Record is tapped (`-fake-mic`, for the UI tests), without
    /// opening the recorder at launch.
    static var fakeMicrophone: Bool { simulatesRecording || flag("-fake-mic") }

    /// A lecture opens on this tab (`-pane transcript`), as LectureView's `Pane` names them.
    static var pane: String? { value("-pane")?.capitalized }

    /// Study opens on the quiz (`-show quiz:<id>`).
    static var studyQuiz: Bool { value("-show")?.hasPrefix("quiz") == true }

    #if DEBUG
    static var show: DebugScreen? { value("-show").flatMap(DebugScreen.init) }
    #endif

    @MainActor static func pairIfAsked(_ app: AppModel) async {
        #if DEBUG
        if flag("-unpaired") { app.forgetHere(); return }
        // `-drive-token-file <path>` (with `-drive-space <prefix>`): over Google Drive with a test account's token,
        // no one pressing Allow. In when the laptop's in the folder; else setup waits for it.
        if value("-drive-token-file") != nil {
            guard let account = await GoogleSignIn.debugAccount() else { return }
            app.startDrive(account)
            guard let link = DriveLibrary.link else { return }
            try? await link.refresh()
            let s = await link.status
            if let name = s.laptop { app.connectedOverGoogle(Me.Library(name: name, version: s.version ?? "")) } else { app.forgetHere(keepGoogle: true) }
            return
        }
        #endif
        if flag("-demo") { app.tryDemo(); return }
        // `-library <address>`: use a library with no password (a test one) without pairing.
        if let address = value("-library"), let base = LibraryAddress.candidates(from: address).first,
           let me = try? await APIClient(base: base, session: AppModel.session).me() {
            app.paired(base: base, library: me.library)
            return
        }
        guard let address = value("-pair"), let code = value("-pair", 2) else { return }
        for base in LibraryAddress.candidates(from: address) {
            let api = APIClient(base: base, session: AppModel.session)
            guard let me = try? await api.me() else { continue }
            if (try? await api.pair(code: code, name: "Simulator")) != nil { app.paired(base: base, library: me.library) }
            return
        }
    }
}
