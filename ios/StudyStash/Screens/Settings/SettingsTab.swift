import SwiftUI

/// Settings, pushed from Home's toolbar (`Route.settings`), in the desktop's settings look: a heading over each
/// grouped box of rows, a row's words on the left (its title in ink, a line under it muted) and its control on the
/// right. Your library (this phone and the computer it reads, joined while the library answers), Appearance, Reminders,
/// What it's for, This phone, About. It has no stack of its own: it's a page on Home's.
struct SettingsTab: View {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.openURL) private var openURL
    @AppStorage(Appearance.key) private var appearance = Appearance.system.rawValue
    // Reminders.Settings' own keys and defaults, so a switch shows what's saved and redraws when it changes.
    @AppStorage("remind.class") private var remindClass = true
    @AppStorage("remind.due") private var remindDue = true
    @AppStorage("remind.notes") private var remindNotes = true

    @State private var me: Me?
    @State private var reach: Reach = .checking
    @State private var confirming = false
    @State private var signingOut = false
    @State private var showUses = false
    @State private var debugOpened = false

    /// Whether the library answered the last time this page asked.
    enum Reach { case checking, connected, unreachable }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    Text("Settings").textStyle(.date).foregroundStyle(.ink)
                        .accessibilityAddTraits(.isHeader)
                        .padding(.top, 4)

                    library.id(Part.library)
                    appearanceSection.id(Part.appearance)
                    reminders.id(Part.reminders)
                    uses.id(Part.uses)
                    if !demo && !app.isDrive { thisPhone.id(Part.phone) }
                    about.id(Part.about)
                }
                .foregroundStyle(.ink)
                .frame(maxWidth: 620, alignment: .leading)
                .padding(.horizontal, sizeClass == .regular ? Space.pageRegular : Space.pageCompact)
                .padding(.bottom, 48)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollIndicators(.hidden)
            .accessibilityIdentifier("page.settings")
            .onAppear {
                #if DEBUG
                if let part = DebugLaunch.value("-scroll").flatMap(Part.init) {
                    Task { try? await Task.sleep(for: .milliseconds(400)); proxy.scrollTo(part, anchor: .top) }
                }
                #endif
            }
        }
        .page()
        .titleOnScroll("Settings", after: 40)
        .navigationDestination(isPresented: $showUses) { UsesView() }
        .task { await look() }
        .onAppear {
            #if DEBUG
            if DebugLaunch.value("-settings") == "uses", !debugOpened {
                debugOpened = true
                Task { try? await Task.sleep(for: .milliseconds(300)); showUses = true }
            }
            #endif
        }
        .confirmationDialog("Sign out of Google on this \(device)?", isPresented: $signingOut, titleVisibility: .visible) {
            Button("Sign out", role: .destructive) { Task { await app.signOutOfGoogle() } }
        } message: {
            Text("This \(device) stops reading your library until you continue with Google again. Recordings not sent yet stay on it.")
        }
        .confirmationDialog("Remove this \(device) from \(app.libraryName)?", isPresented: $confirming, titleVisibility: .visible) {
            Button("Remove this \(device)", role: .destructive) { Task { await app.forget() } }
        } message: {
            Text("To use it again, add it from Settings, then Phone, on your computer.")
        }
    }

    /// Ask the library who this phone is, and so whether it answers.
    private func look() async {
        #if DEBUG
        if DebugLaunch.flag("-unreachable") { reach = .unreachable; return }
        #endif
        // Over Google, the laptop's file says when it was last there; read it fresh.
        if app.isDrive {
            try? await DriveLibrary.link?.refresh()
            return
        }
        guard let api = app.api else { reach = .unreachable; return }
        do {
            me = try await api.me()
            reach = .connected
        } catch {
            reach = .unreachable
        }
    }

    private func tryAgain() async {
        reach = .checking
        await app.check()
        await look()
    }

    /// The demo library, inside the app. (`-as-paired` in a debug build pictures the demo as a library on a computer.)
    private var demo: Bool {
        #if DEBUG
        if DebugLaunch.flag("-as-paired") { return false }
        #endif
        return app.isDemo
    }

    private var device: String { UIDevice.current.userInterfaceIdiom == .pad ? "iPad" : "phone" }

    // MARK: - Your library

    private var library: some View {
        Group {
            Heading("Your library", first: true)
            if demo {
                SettingsBox {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("These are sample classes.").textStyle(.body).foregroundStyle(.ink)
                        Text("Connect your computer to read your own lectures here, and record into your library.")
                            .font(.atkinson(13, relativeTo: .footnote)).foregroundStyle(.inkMuted)
                            .fixedSize(horizontal: false, vertical: true)
                        // Ends the demo and goes to setup's "Open Syft Notes on your computer" step.
                        Button("Connect your computer") { Task { await app.endDemo() } }
                            .buttonStyle(.pill(small: true, ink: true))
                            .padding(.top, 10)
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
            } else if app.isDrive {
                googleLibrary
            } else {
                SettingsBox {
                    LinkPicture(device: device, name: deviceName, library: libraryName, computer: computer, reach: reach)
                        .padding(.horizontal, 12)
                        .padding(.top, 18)
                        .padding(.bottom, 14)
                    SettingsRowRule()
                    SettingsRow(title: libraryName, detail: libraryDetail) {
                        switch reach {
                        case .connected: Text("Connected").textStyle(.body).foregroundStyle(.ink).fixedSize()
                        case .checking: Text("Asking…").textStyle(.body).foregroundStyle(.inkSubtle).fixedSize()
                        case .unreachable:
                            Button("Try again") { Task { await tryAgain() } }.buttonStyle(.pill(small: true)).fixedSize()
                        }
                    }
                    if let base = app.api?.base {
                        if let computer {
                            SettingsRowRule()
                            SettingsRow(title: "Computer") { Value(computer) }
                        }
                        SettingsRowRule()
                        SettingsRow(title: "Address") { Value(address(base)) }
                            .contextMenu {
                                Button("Copy", icon: "content_copy") { UIPasteboard.general.string = address(base) }
                            }
                    }
                }
            }
        }
    }

    private var libraryName: String { me?.library.name ?? app.libraryName }

    // MARK: - Your library, over Google

    /// Connected through Google Drive: the phone and the laptop, the account, when the laptop was last there, and Sign
    /// out.
    private var googleLibrary: some View {
        let d = app.drive
        let laptop = d?.laptop ?? app.libraryName
        return Group {
            SettingsBox {
                LinkPicture(device: device, name: nil, library: laptop, computer: nil, reach: laptopReach, libraryIcon: "laptop_mac",
                            via: "Google Drive")
                    .padding(.horizontal, 12)
                    .padding(.top, 18)
                    .padding(.bottom, 14)
                SettingsRowRule()
                SettingsRow(title: laptop, detail: laptopDetail) {
                    if laptopReach == .connected { Text("Connected").textStyle(.body).foregroundStyle(.ink).fixedSize() }
                }
                SettingsRowRule()
                SettingsRow(title: "Google account") { Value(d?.email ?? "") }
                SettingsRowRule()
                Button { signingOut = true } label: {
                    SettingsRow(title: "Sign out of Google") { EmptyView() }
                }
                .buttonStyle(SettingsRowButtonStyle())
            }
            if let waiting = d?.waiting, waiting > 0 {
                Say(waiting == 1 ? "One change from this \(device) is on its way to your laptop. It's made when your laptop is on."
                    : "\(waiting) changes from this \(device) are on their way to your laptop. They're made when your laptop is on.")
            }
            Say("Your notes reach this \(device) through a private Syft Notes folder in your Google Drive that only Syft Notes can see.")
        }
    }

    /// The laptop's there if it said so in the last 3 minutes (it does every minute while it runs).
    private var laptopReach: Reach {
        guard let seen = app.drive?.seen else { return .checking }
        return Date.now.timeIntervalSince(seen) < LaptopPresence.staleAfter ? .connected : .unreachable
    }

    /// "Syft Notes 0.17.3", or when it's away, "Your laptop was last here 3 hours ago."
    private var laptopDetail: String {
        let version = app.drive?.version.flatMap { $0.isEmpty ? nil : "Syft Notes \($0)" } ?? ""
        guard let seen = app.drive?.seen, laptopReach != .connected else { return version }
        return "Your laptop was last here \(Self.ago(seen)). New notes show when it's on."
    }

    /// "3 hours ago", "2 days ago", "a minute ago".
    static func ago(_ d: Date, now: Date = .now) -> String {
        let s = max(0, now.timeIntervalSince(d))
        func words(_ n: Int, _ unit: String) -> String { n == 1 ? "a\(unit == "hour" ? "n" : "") \(unit) ago" : "\(n) \(unit)s ago" }
        if s < 3600 { return words(max(1, Int(s / 60)), "minute") }
        if s < 86_400 { return words(Int(s / 3600), "hour") }
        return words(Int(s / 86_400), "day")
    }

    private var libraryDetail: String {
        switch reach {
        case .unreachable:
            "Can’t reach it right now. This \(device) reaches your computer through Tailscale: turn it on here and on your computer, and keep Syft Notes open there. Recordings wait on this \(device) until then."
        case .connected, .checking:
            me.map { "Syft Notes \($0.library.version)" } ?? ""
        }
    }

    /// This phone's name in the library ("Sam's iPhone"), when it isn't just "This phone".
    private var deviceName: String? {
        guard let n = me?.device?.name, !n.isEmpty, n.lowercased() != "this \(device)" else { return nil }
        return n
    }

    /// The computer the library runs on, from its address: "mac-mini" from mac-mini.tail1234.ts.net, an IP as it is.
    private var computer: String? {
        guard let host = app.api?.base.host(), !host.isEmpty else { return nil }
        if host.contains(":") || host.allSatisfy({ $0.isNumber || $0 == "." }) { return host }
        return String(host.split(separator: ".").first ?? Substring(host))
    }

    private func address(_ base: URL) -> String {
        var s = base.absoluteString
        while s.hasSuffix("/") { s.removeLast() }
        return s
    }

    // MARK: - Appearance

    private var appearanceSection: some View {
        Group {
            Heading("Appearance")
            Segmented(
                selection: Binding(get: { Appearance(rawValue: appearance) ?? .system }, set: { appearance = $0.rawValue }),
                options: Appearance.allCases.map { (value: $0, title: $0.title) }
            )
            .accessibilityLabel("Appearance")
            Say("Light and dark set the whole app at once. Match system follows this \(device)’s own setting.")
        }
    }

    // MARK: - Reminders

    private var reminders: some View {
        Group {
            Heading("Reminders")
            SettingsBox {
                Toggle("When a class starts, offer to record it", isOn: $remindClass)
                    .toggleStyle(SettingsSwitchStyle())
                SettingsRowRule()
                Toggle("Before work is due: the evening before and 2 hours before", isOn: $remindDue)
                    .toggleStyle(SettingsSwitchStyle())
                SettingsRowRule()
                Toggle("When notes for a recording from this \(device) are ready", isOn: $remindNotes)
                    .toggleStyle(SettingsSwitchStyle())
            }
            .onChange(of: remindClass) { reschedule() }
            .onChange(of: remindDue) { reschedule() }
            if app.forSchool {
                Say("Class times come from the calendars in your library, and work that’s due from Canvas.")
            }
        }
    }

    /// Class and due reminders are scheduled again from what the library says.
    private func reschedule() {
        Task { await app.refreshInBackground() }
    }

    // MARK: - What it's for

    private var uses: some View {
        Group {
            Heading("What it’s for")
            SettingsBox {
                Button { showUses = true } label: {
                    SettingsRow(title: usesWords, detail: usesDetail) {
                        Icon("chevron_right", size: 16).foregroundStyle(.inkSubtle)
                    }
                }
                .buttonStyle(SettingsRowButtonStyle())
                .accessibilityLabel("What it’s for: \(usesWords)")
            }
        }
    }

    private var usesWords: String {
        let said = app.uses ?? []
        return UsesView.kinds.filter { said.isEmpty ? $0.id == "student" : said.contains($0.id) }.map(\.title).joined(separator: ", ")
    }

    private var usesDetail: String {
        app.forSchool ? "Classes, Canvas and what’s due" : "Folders, and notes written for meetings and talks"
    }

    // MARK: - This phone

    private var thisPhone: some View {
        Group {
            Heading("This \(device)")
            SettingsBox {
                SettingsRow(title: "Name") { Value(me?.device?.name ?? UIDevice.current.name) }
                if let added = me?.device?.added, let d = Format.parse(added) {
                    SettingsRowRule()
                    SettingsRow(title: "Added") { Value(Format.day(d)) }
                }
                SettingsRowRule()
                Button { confirming = true } label: {
                    SettingsRow(title: "Remove this \(device)") { EmptyView() }
                }
                .buttonStyle(SettingsRowButtonStyle())
            }
            Say("This \(device) reads your library until it’s removed, here or on your computer: open Settings, then Phone.")
        }
    }

    // MARK: - About

    private var about: some View {
        Group {
            Heading("About")
            SettingsBox {
                HStack(spacing: 14) {
                    StudyStashLogo(size: 48)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Syft Notes").textStyle(.title).foregroundStyle(.ink)
                        Text(versionLine).textStyle(.meta).foregroundStyle(.inkMuted)
                    }
                    .accessibilityElement(children: .combine)
                    Spacer(minLength: 8)
                    Button("Help") { openURL(URL(string: "https://studystash.app")!) }
                        .buttonStyle(.textLink)
                        .accessibilityHint("Opens studystash.app")
                }
                .padding(16)
            }
        }
    }

    private var versionLine: String {
        let v = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        let on = UIDevice.current.userInterfaceIdiom == .pad ? "iPad" : "iPhone"
        return v.isEmpty ? "For \(on)" : "Version \(v) for \(on)"
    }

    /// The page's parts, for `-scroll <part>` (a debug build's pictures).
    private enum Part: String { case library, appearance, reminders, uses, phone, about }
}

// MARK: - The settings look (the desktop's SettingsView: section, group, row, row-title, row-sub, say, switch)

/// A part of the page's heading ("Your library", "Appearance"), with room above it.
private struct Heading: View {
    var title: String
    var first = false
    init(_ title: String, first: Bool = false) {
        self.title = title
        self.first = first
    }
    var body: some View {
        Text(title).textStyle(.h2).foregroundStyle(.ink)
            .accessibilityAddTraits(.isHeader)
            .padding(.top, first ? 20 : 32)
            .padding(.bottom, 10)
    }
}

/// A group of rows: the sunken grey box with a hairline round it, the rows edge to edge inside.
private struct SettingsBox<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        Panel(padding: 0) {
            VStack(alignment: .leading, spacing: 0) { content }
        }
    }
}

/// A line under a group, as the desktop's "say": small, muted, plain.
private struct Say: View {
    var text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text)
            .font(.atkinson(13, relativeTo: .footnote))
            .lineSpacing(3)
            .foregroundStyle(.inkMuted)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.top, 10)
            .padding(.horizontal, 4)
    }
}

/// One row in a group: its words on the left (the title in ink, a line under it muted), whatever it holds (a value, a
/// button, a chevron) on the right, on its centre line.
private struct SettingsRow<Trailing: View>: View {
    var title: String
    var detail: String?
    @ViewBuilder var trailing: Trailing

    init(title: String, detail: String? = nil, @ViewBuilder trailing: () -> Trailing) {
        self.title = title
        self.detail = detail
        self.trailing = trailing()
    }

    var body: some View {
        HStack(spacing: 12) {
            // The words first: a short title keeps its width, a value beside it takes what's left (and loses its
            // middle if it must); long words wrap beside a button.
            VStack(alignment: .leading, spacing: 3) {
                Text(title).textStyle(.body).foregroundStyle(.ink)
                    .fixedSize(horizontal: false, vertical: true)
                if let detail, !detail.isEmpty {
                    Text(detail)
                        .font(.atkinson(13, relativeTo: .footnote))
                        .lineSpacing(2)
                        .foregroundStyle(.inkMuted)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .layoutPriority(1)
            Spacer(minLength: 0)
            trailing
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .frame(minHeight: 54)
        .contentShape(.rect)
    }
}

/// A row's value on its right: muted, one line.
private struct Value: View {
    var text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text).textStyle(.body).foregroundStyle(.inkMuted)
            .lineLimit(1)
            .truncationMode(.middle)
    }
}

/// A hairline between two rows of a group, from the rows' text edge to the right.
private struct SettingsRowRule: View {
    var body: some View { Rule().padding(.leading, 16) }
}

/// A whole row that does something: washed while pressed.
private struct SettingsRowButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Color.press : .clear)
    }
}

/// The desktop's settings switch, at a row's end: its words on the left, the track on the right (grey off, ink on,
/// the page's colour for its knob). The whole row turns it.
private struct SettingsSwitchStyle: ToggleStyle {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @ScaledMetric(relativeTo: .body) private var height: CGFloat = 26

    func makeBody(configuration: Configuration) -> some View {
        Button {
            withAnimation(reduceMotion ? nil : .snappy(duration: 0.18)) { configuration.isOn.toggle() }
        } label: {
            HStack(spacing: 14) {
                configuration.label
                    .textStyle(.body)
                    .foregroundStyle(.ink)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
                Capsule()
                    .fill(configuration.isOn ? Color.ink : .press)
                    .frame(width: height * 44 / 26, height: height)
                    .overlay(alignment: configuration.isOn ? .trailing : .leading) {
                        Circle().fill(.surface)
                            .frame(width: height - 4, height: height - 4)
                            .padding(.horizontal, 2)
                            .shadow(color: .black.opacity(0.25), radius: 1.5, y: 1)
                    }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .frame(minHeight: 54)
            .contentShape(.rect)
        }
        .buttonStyle(SettingsRowButtonStyle())
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isToggle)
        .accessibilityValue(configuration.isOn ? "On" : "Off")
    }
}

/// This phone and the library it reads, side by side, joined by a line: whole, with a check, while the library
/// answers; broken while it doesn't. The library's side names its computer.
private struct LinkPicture: View {
    var device: String
    var name: String?
    var library: String
    var computer: String?
    var reach: SettingsTab.Reach
    var libraryIcon = "dns"
    /// What joins them, said under the line ("Google Drive"); nil for a direct line (Tailscale).
    var via: String?

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            end(icon: "smartphone", title: "This \(device)", detail: name)
            join
                .frame(height: 44)
                .frame(maxWidth: .infinity)
                .overlay(alignment: .bottom) {
                    if let via {
                        Text(via).font(.atkinson(12.5, .medium, relativeTo: .caption)).foregroundStyle(.inkMuted)
                            .fixedSize()
                            .offset(y: 20)
                    }
                }
            end(icon: libraryIcon, title: library, detail: computer.map { "on \($0)" })
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibility)
    }

    private var accessibility: String {
        let on = computer.map { " on \($0)" } ?? via.map { " through \($0)" } ?? ""
        return switch reach {
        case .connected: "This \(device) reads \(library)\(on)"
        case .checking: "Asking \(library)\(on)"
        case .unreachable: "This \(device) can't reach \(library)\(on) right now"
        }
    }

    private func end(icon: String, title: String, detail: String?) -> some View {
        VStack(spacing: 6) {
            Icon(icon, size: 22)
                .foregroundStyle(.ink)
                .frame(width: 44, height: 44)
                .background(.surface, in: .rect(cornerRadius: Radius.panel))
                .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line))
            VStack(spacing: 1) {
                Text(title).font(.atkinson(13.5, .bold, relativeTo: .footnote)).foregroundStyle(.ink)
                if let detail {
                    Text(detail).font(.atkinson(12.5, .medium, relativeTo: .caption)).foregroundStyle(.inkMuted)
                }
            }
            .multilineTextAlignment(.center)
            .lineLimit(2)
        }
        .frame(width: 116)
    }

    private var join: some View {
        ZStack {
            switch reach {
            case .connected:
                Rectangle().fill(Color.inkSubtle).frame(height: 1.5)
                badge("check", .ink)
            case .checking:
                Line().stroke(Color.line, style: StrokeStyle(lineWidth: 1.5, lineCap: .round, dash: [3, 5]))
                    .frame(height: 1.5)
            case .unreachable:
                Line().stroke(Color.inkSubtle, style: StrokeStyle(lineWidth: 1.5, lineCap: .round, dash: [3, 5]))
                    .frame(height: 1.5)
                badge("wifi_off", .inkMuted)
            }
        }
        .padding(.horizontal, -22)
    }

    private func badge(_ icon: String, _ ink: Color) -> some View {
        Icon(icon, size: 14)
            .foregroundStyle(ink)
            .frame(width: 26, height: 26)
            .background(.surface, in: .circle)
            .overlay(Circle().strokeBorder(.line))
    }

    private struct Line: Shape {
        func path(in rect: CGRect) -> Path {
            Path { p in
                p.move(to: CGPoint(x: rect.minX, y: rect.midY))
                p.addLine(to: CGPoint(x: rect.maxX, y: rect.midY))
            }
        }
    }
}
