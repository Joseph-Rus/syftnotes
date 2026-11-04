import PencilKit
import SwiftUI
import UniformTypeIdentifiers

/// Write notes for a class or lecture: the notebook already kept for the lecture, or a new one (its paper chosen
/// first).
struct HandwritingView: View {
    var className: String?
    var lecture: (id: String, title: String)?
    /// Beside a lecture on an iPad, or inside the recorder, not a screen of its own: no Done, it's closed from there.
    var embedded = false
    @Environment(\.dismiss) private var dismiss
    @State private var store = NotebookStore.shared
    @State private var id: UUID?
    @State private var choosing = false

    var body: some View {
        Group {
            if let id {
                NotebookEditor(id: id, embedded: embedded) { self.id = nil }
            } else if choosing {
                NewNotebookView(className: className, lecture: lecture, embedded: embedded) { n in
                    id = n.id
                } cancel: {
                    dismiss()
                }
            } else {
                Color.surface
            }
        }
        .task(id: id) {
            if id != nil { return }
            #if DEBUG
            // Pictures: `-sample-notebook` writes the sample notebook for this lecture.
            if DebugLaunch.flag("-sample-notebook"), let lecture { id = NotebookSamples.make(lecture: lecture, className: className); return }
            #endif
            if let lecture, let existing = store.notebooks.first(where: { $0.lectureID == lecture.id }) {
                id = existing.id
                return
            }
            choosing = true
        }
    }
}

/// One notebook, its pages one under another as a pad of paper, the whole screen (or the half beside a lecture):
/// the Pencil writes and a finger scrolls (a finger writes where there's no Pencil), pinch zooms, Apple's pens along
/// the foot or floating. The bar over it is small: Done, the title (tap to rename), undo and redo, the paper, a page
/// added, the pages (a sidebar on an iPad). Pull up past the last page for a new one. Done saves it to the library
/// when anything changed and says so ("Saved to Calculus II.", or, offline, that it's kept here and sent later); a
/// notebook closed with nothing written in it is let go. There's no Save button.
struct NotebookEditor: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.scenePhase) private var scenePhase
    let id: UUID
    var embedded = false
    /// Beside a lecture, after Delete notebook: the page beside starts a fresh one.
    var onDeleted: (() -> Void)?
    @State private var store = NotebookStore.shared
    @State private var canvas = NotebookCanvas()
    @State private var closing = false
    @State private var message: String?
    @State private var renaming = false
    @State private var draftTitle = ""
    @State private var confirmingDelete = false
    @State private var deletingPage: Int?
    @State private var paperPopover = false
    @State private var paperSheet = false
    @State private var pagesSheet = false
    @State private var importing = false
    @State private var sidebar = DebugLaunch.flag("-notebook-sidebar")
    @AppStorage(FingerMode.key) private var fingers = FingerMode.auto.rawValue

    private var notebook: Notebook? { store.notebook(id) }
    private var regular: Bool { sizeClass == .regular }
    private var startPage: Int? { DebugLaunch.value("-notebook-page").flatMap(Int.init).map { $0 - 1 } }

    var body: some View {
        Group {
            if embedded {
                // Beside a lecture: inside its navigation, so a bar of its own rather than a second NavigationStack.
                VStack(spacing: 0) {
                    embeddedBar
                    desk
                }
            } else {
                NavigationStack {
                    HStack(spacing: 0) {
                        if sidebar && regular {
                            NotebookPages(canvas: canvas, sidebar: true, addPDF: { importing = true })
                                .frame(width: 184)
                                .background(.surface)
                                .overlay(alignment: .trailing) { Rectangle().fill(.line).frame(width: 1) }
                                .transition(.move(edge: .leading))
                        }
                        desk
                    }
                    .animation(.smooth(duration: 0.25), value: sidebar)
                    .navigationTitle(notebook?.title ?? "Notes")
                    .navigationSubtitle(regular ? subtitle : "")
                    .navigationBarTitleDisplayMode(.inline)
                    .modifier(TitleMenu(on: regular) {
                        // An iPad's title: tap to rename. (A phone's bar has undo and redo there instead.)
                        Button("Rename", icon: "edit") { startRenaming() }
                        if let n = notebook {
                            ShareLink(item: PDFExport(id: n.id, name: n.title), preview: SharePreview(n.title)) {
                                Label("Share as PDF", icon: "ios_share")
                            }
                        }
                    })
                    .toolbar { toolbar }
                }
            }
        }
        .task {
            #if DEBUG
            // Pictures: `-notebook-sheet paper|pages` opens the paper or the pages over the notebook.
            try? await Task.sleep(for: .seconds(0.8))
            switch DebugLaunch.value("-notebook-sheet") {
            case "paper": if regular && !embedded { paperPopover = true } else { paperSheet = true }
            case "pages": pagesSheet = true
            default: break
            }
            #endif
        }
        .onAppear { canvas.onPullToAdd = { addPage(after: nil) } }
        .onChange(of: overlaid) { _, over in canvas.showTools(!over) }
        .onChange(of: scenePhase) { _, phase in if phase != .active { canvas.flush() } }
        // Closed some other way (beside a lecture, or the app going elsewhere): saved all the same, quietly.
        .onDisappear { saveQuietly() }
        .sheet(isPresented: $paperSheet) { paperSheetView.presentationDetents([.medium, .large]) }
        .sheet(isPresented: $pagesSheet) {
            NotebookPagesSheet(canvas: canvas) { importing = true }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.pdf]) { result in
            if case .success(let url) = result {
                if !canvas.addPDF(url, after: canvas.current) { message = "That PDF couldn't be opened." }
            }
        }
        .confirmDeletingPage($deletingPage, canvas: canvas)
        .alert("Rename notebook", isPresented: $renaming) {
            TextField("Title", text: $draftTitle)
            Button("Cancel", role: .cancel) {}
            Button("Rename") { titleBinding.wrappedValue = draftTitle }
        }
        .confirmationDialog("Delete this notebook from this phone?", isPresented: $confirmingDelete, titleVisibility: .visible) {
            Button("Delete notebook", role: .destructive) { deleteNotebook() }
        } message: {
            Text(notebook?.savedAt == nil ? "It hasn't been saved to your library, so it'll be gone." : "The copy saved to your library stays there.")
        }
    }

    /// Something over the page: Apple's pens step aside for it.
    private var overlaid: Bool {
        paperPopover || paperSheet || pagesSheet || importing || renaming || confirmingDelete || deletingPage != nil
    }

    /// The pages on the desk, to the screen's edges, under the bars.
    private var desk: some View {
        GeometryReader { geo in
            NotebookCanvasView(model: canvas, id: id, page: startPage, insets: geo.safeAreaInsets)
                .ignoresSafeArea()
        }
        .background(Color.canvas.ignoresSafeArea())
        .overlay(alignment: .bottom) {
            if let message {
                Toast(message)
                    .padding(.horizontal, 20)
                    .padding(.bottom, regular ? 24 : 120)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .task(id: message) {
                        guard !closing else { return }
                        try? await Task.sleep(for: .seconds(3))
                        withAnimation { self.message = nil }
                    }
            }
        }
        .animation(.smooth, value: message)
        .accessibilityIdentifier("page.notebook")
    }

    private var titleBinding: Binding<String> {
        Binding(get: { notebook?.title ?? "Notes" }, set: { t in
            let title = t.trimmingCharacters(in: .whitespacesAndNewlines)
            guard var n = notebook, !title.isEmpty, title != n.title else { return }
            n.title = title
            n.updated = .now
            store.save(n)
        })
    }

    /// "Calculus II, page 2 of 3".
    private var subtitle: String {
        let page = "page \(canvas.current + 1) of \(canvas.pageCount)"
        guard let n = notebook else { return "Page 1 of 1" }
        if let c = n.className, !c.isEmpty { return "\(c), \(page)" }
        return page.prefix(1).uppercased() + page.dropFirst()
    }

    // MARK: the bars

    @ToolbarContentBuilder private var toolbar: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            Button { Task { await close() } } label: {
                if closing { StudyLoader(.inline, color: .inkSubtle) } else { Text("Done").textStyle(.button) }
            }
            .disabled(closing)
            .accessibilityLabel("Done")
        }
        ToolbarItem(placement: .topBarLeading) {
            Button {
                if regular { sidebar.toggle() } else { canvas.flush(); pagesSheet = true }
            } label: {
                if regular {
                    Label("Pages", icon: "sidebar")
                } else {
                    // A phone's bar has no room for the title: the page it's on is here, and opens the pages.
                    Text("\(canvas.current + 1) of \(canvas.pageCount)").textStyle(.buttonSmall).monospacedDigit()
                }
            }
            .accessibilityLabel("Pages, page \(canvas.current + 1) of \(canvas.pageCount)")
            .accessibilityIdentifier("notebook.pages")
        }
        if regular {
            ToolbarItemGroup(placement: .topBarTrailing) { undoRedo }
            ToolbarSpacer(.fixed, placement: .topBarTrailing)
            ToolbarItemGroup(placement: .topBarTrailing) {
                Button { canvas.flush(); paperPopover = true } label: { Label("Paper", icon: "description") }
                    .accessibilityIdentifier("notebook.paper")
                    .popover(isPresented: $paperPopover) {
                        paperSheetView.frame(width: 400).frame(minHeight: 540).presentationCompactAdaptation(.sheet)
                    }
                addPageButton
                Menu { moreItems } label: { Label("More", icon: "more_horiz") }
                    .accessibilityIdentifier("notebook.more")
            }
        } else {
            // A phone: undo and redo where the title would be (at the end, the system folds them into its overflow, and
            // the middle takes one item).
            ToolbarItem(placement: .principal) {
                HStack(spacing: 6) {
                    Button { canvas.undo() } label: { Label("Undo", icon: "undo") }
                        .disabled(!canvas.canUndo).opacity(canvas.canUndo ? 1 : 0.35)
                        .accessibilityIdentifier("notebook.undo")
                    Button { canvas.redo() } label: { Label("Redo", icon: "redo") }
                        .disabled(!canvas.canRedo).opacity(canvas.canRedo ? 1 : 0.35)
                        .accessibilityIdentifier("notebook.redo")
                }
                .buttonStyle(.icon)
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button("Paper", icon: "description") { canvas.flush(); paperSheet = true }
                    addPageItems
                    Divider()
                    Button("Rename", icon: "edit") { startRenaming() }
                    moreItems
                } label: { Label("More", icon: "more_horiz") }
                .accessibilityIdentifier("notebook.more")
            }
        }
    }

    @ViewBuilder private var undoRedo: some View {
        Button { canvas.undo() } label: { Label("Undo", icon: "undo") }
            .disabled(!canvas.canUndo)
            .accessibilityIdentifier("notebook.undo")
        Button { canvas.redo() } label: { Label("Redo", icon: "redo") }
            .disabled(!canvas.canRedo)
            .accessibilityIdentifier("notebook.redo")
    }

    /// Beside a lecture: the title, where it's got to, and the same buttons, in one row.
    private var embeddedBar: some View {
        HStack(spacing: 4) {
            Button { startRenaming() } label: {
                VStack(alignment: .leading, spacing: 0) {
                    Text(notebook?.title ?? "Notes").textStyle(.title).foregroundStyle(.ink).lineLimit(1)
                    Text("Page \(canvas.current + 1) of \(canvas.pageCount)").textStyle(.micro).foregroundStyle(.inkMuted)
                        .monospacedDigit()
                }
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Rename \(notebook?.title ?? "notebook")")
            Spacer(minLength: 8)
            Button { canvas.undo() } label: { Label("Undo", icon: "undo") }
                .buttonStyle(.icon).disabled(!canvas.canUndo).opacity(canvas.canUndo ? 1 : 0.35)
            Button { canvas.redo() } label: { Label("Redo", icon: "redo") }
                .buttonStyle(.icon).disabled(!canvas.canRedo).opacity(canvas.canRedo ? 1 : 0.35)
            Button { canvas.flush(); paperPopover = true } label: { Label("Paper", icon: "description") }
                .buttonStyle(.icon)
                .accessibilityIdentifier("notebook.paper")
                .popover(isPresented: $paperPopover) {
                    paperSheetView.frame(width: 400).frame(minHeight: 540).presentationCompactAdaptation(.sheet)
                }
            Button { canvas.flush(); pagesSheet = true } label: { Label("Pages", icon: "grid_view") }
                .buttonStyle(.icon)
                .accessibilityIdentifier("notebook.pages")
            Menu {
                addPageItems
                Divider()
                Button("Rename", icon: "edit") { startRenaming() }
                moreItems
            } label: { Label("More", icon: "more_horiz") }
            .buttonStyle(.icon)
            .accessibilityIdentifier("notebook.more")
        }
        .padding(.leading, 16)
        .padding(.trailing, 8)
        .padding(.vertical, 8)
        .background(.surface)
        .overlay(alignment: .bottom) { Rule() }
    }

    /// A tap adds a page after the one showing; held, at the end instead.
    private var addPageButton: some View {
        Menu { addPageItems } label: { Label("Add a page", icon: "page_add") } primaryAction: { addPage(after: canvas.current) }
            .accessibilityIdentifier("notebook.addPage")
    }

    @ViewBuilder private var addPageItems: some View {
        Button("Add a page after this one", icon: "page_add") { addPage(after: canvas.current) }
        if canvas.current + 1 < canvas.pageCount {
            Button("Add a page at the end", icon: "page_add") { addPage(after: nil) }
        }
        Button("Add pages from a PDF", icon: "picture_as_pdf") { importing = true }
    }

    @ViewBuilder private var moreItems: some View {
        if !embedded, notebook?.lectureID == nil, !app.classes.isEmpty {
            Menu {
                ForEach(app.classes) { c in
                    Button { file(in: c.name) } label: {
                        if notebook?.className == c.name { Label(c.name, icon: "check") } else { Text(c.name) }
                    }
                }
                Button { file(in: nil) } label: {
                    if notebook?.className == nil { Label("Unsorted", icon: "check") } else { Text("Unsorted") }
                }
            } label: { Label("File in", icon: "drive_file_move") }
        }
        Toggle(isOn: Binding(get: { canvas.fingersDraw }, set: { on in
            fingers = (on ? FingerMode.draw : .scroll).rawValue
            canvas.applyFingers()
        })) {
            Label("Draw with your finger", icon: "pan_tool")
        }
        if let n = notebook {
            ShareLink(item: PDFExport(id: n.id, name: n.title), preview: SharePreview(n.title)) {
                Label("Share as PDF", icon: "ios_share")
            }
        }
        Divider()
        Button("Delete this page", icon: "page_remove", role: .destructive) {
            if canvas.pageIsBlank(canvas.current) { canvas.deletePage(canvas.current) } else { deletingPage = canvas.current }
        }
            .disabled(canvas.pageCount < 2)
        Button("Delete notebook", icon: "delete", role: .destructive) { confirmingDelete = true }
    }

    private var paperSheetView: some View {
        let n = notebook
        let page = n.flatMap { $0.pages.indices.contains(canvas.current) ? $0.pages[canvas.current] : nil }
        return PaperSheet(page: page, pageNumber: canvas.current + 1, newPages: n?.newPages ?? .standard) { t, all in
            canvas.applyPaper(t, to: all ? nil : canvas.current)
        } done: {
            paperPopover = false
            paperSheet = false
        }
    }

    private func startRenaming() {
        draftTitle = notebook?.title ?? ""
        renaming = true
    }

    // MARK: pages

    private func addPage(after index: Int?) {
        let at = canvas.addPage(after: index)
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        UIAccessibility.post(notification: .announcement, argument: "Page \(at + 1) added")
    }

    private func file(in cls: String?) {
        guard var n = notebook, n.className != cls else { return }
        n.className = cls
        n.updated = .now
        store.save(n)
    }

    private func deleteNotebook() {
        guard let n = notebook else { return }
        store.delete(n)
        if embedded { onDeleted?() } else { dismiss() }
    }

    // MARK: Saving

    /// Where it's saved, in words: its class, else its lecture, else the library.
    private func place(_ n: Notebook) -> String { n.className ?? n.lectureTitle ?? app.libraryName }

    /// Done: save to the library if anything changed, say so, then close. A notebook with nothing in it, never saved,
    /// is let go instead.
    private func close() async {
        canvas.flush()
        guard let n = notebook else { dismiss(); return }
        if n.savedAt == nil, store.isBlank(n) {
            store.delete(n)
            dismiss()
            return
        }
        guard n.needsSaving, let api = app.api else { dismiss(); return }
        closing = true
        canvas.showTools(false)
        let outcome = await store.send(n.id, api: api, waitingAtMost: .seconds(6))
        switch outcome {
        case .saved:
            UINotificationFeedbackGenerator().notificationOccurred(.success)
            message = "Saved to \(place(n))."
        case .kept: message = "Saved on this phone. It's sent when your library can be reached."
        case .stillSending: message = "Saving to \(place(n)). It carries on after this closes."
        }
        try? await Task.sleep(for: .seconds(outcome == .saved ? 1.2 : 2.4))
        dismiss()
    }

    private func saveQuietly() {
        canvas.flush()
        guard !closing, let n = notebook else { return }
        if n.savedAt == nil, store.isBlank(n) {
            store.delete(n)
            return
        }
        if n.needsSaving, let api = app.api {
            let store = store
            Task { _ = await store.send(n.id, api: api, waitingAtMost: .seconds(120)) }
        }
    }
}

/// The title's menu, only where the title shows (an iPad): a phone's bar shows no chevron for an empty one.
private struct TitleMenu<Items: View>: ViewModifier {
    var on: Bool
    @ViewBuilder var items: Items

    func body(content: Content) -> some View {
        if on { content.toolbarTitleMenu { items } } else { content }
    }
}

// MARK: - Saving to the library

/// How a save went: in the library, kept on this phone (the library couldn't be reached), or still on its way.
enum NotebookSend: Sendable { case saved, kept, stillSending }

extension NotebookStore {
    /// Notebooks being sent right now, so one isn't sent twice at once (a second copy in the library).
    @MainActor private static var sending: Set<UUID> = []

    /// Nothing written, drawn or brought in: every page paper with no ink.
    func isBlank(_ n: Notebook) -> Bool {
        n.importedFrom == nil && n.pages.allSatisfy { $0.pdf == nil && drawing(n, $0).strokes.isEmpty }
    }

    /// Send a notebook to the library, waiting at most `wait` to hear how it went (it carries on after that).
    func send(_ id: UUID, api: APIClient, waitingAtMost wait: Duration) async -> NotebookSend {
        guard !Self.sending.contains(id) else { return .stillSending }
        Self.sending.insert(id)
        let upload = Task { @MainActor in
            defer { Self.sending.remove(id) }
            return (try? await self.upload(id, api: api)) != nil
        }
        return await withTaskGroup(of: NotebookSend.self) { group in
            group.addTask { await upload.value ? .saved : .kept }
            group.addTask {
                try? await Task.sleep(for: wait)
                return .stillSending
            }
            let first = await group.next() ?? .stillSending
            group.cancelAll()
            return first
        }
    }

    /// Every notebook with something in it that changed since it last reached the library (closed while it couldn't
    /// be reached), sent now: with the recordings waiting, when the library answers (the app coming back, its refresh).
    func sendWaiting(api: APIClient) async {
        for n in notebooks where n.needsSaving && !isBlank(n) && !Self.sending.contains(n.id) {
            _ = await send(n.id, api: api, waitingAtMost: .seconds(120))
        }
    }
}

/// A notebook's PDF to share, named for it, made only when it's shared.
struct PDFExport: Transferable {
    var id: UUID
    var name: String
    static var transferRepresentation: some TransferRepresentation {
        DataRepresentation(exportedContentType: .pdf) { export in
            await MainActor.run {
                guard let n = NotebookStore.shared.notebook(export.id) else { return Data() }
                return NotebookStore.shared.pdfData(n)
            }
        }
        .suggestedFileName { $0.name + ".pdf" }
    }
}
