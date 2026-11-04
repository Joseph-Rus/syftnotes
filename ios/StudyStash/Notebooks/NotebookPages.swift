import PencilKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Changing the pages

extension NotebookCanvas {
    /// Change the notebook's pages: what's written is kept first, then the change is saved and the pages laid out
    /// again, showing `page` (the one showing now, if nil).
    func changePages(showing page: Int? = nil, _ change: (inout Notebook) -> Void) {
        flush()
        guard let id, var n = store.notebook(id) else { return }
        change(&n)
        n.updated = .now
        store.save(n)
        load(scrollTo: page ?? current)
    }

    /// A page on the notebook's paper, after page `index` (or at the end).
    @discardableResult
    func addPage(after index: Int? = nil) -> Int {
        guard let id, let n = store.notebook(id) else { return 0 }
        let at = min((index ?? n.pages.count - 1) + 1, n.pages.count)
        changePages(showing: at) { $0.pages.insert(Notebook.Page($0.newPages), at: at) }
        return at
    }

    func duplicatePage(_ index: Int) {
        guard let id, let n = store.notebook(id), n.pages.indices.contains(index) else { return }
        flush()
        var copy = n.pages[index]
        copy.id = UUID()
        store.copyInk(n, from: n.pages[index].id, to: copy.id)
        changePages(showing: index + 1) { $0.pages.insert(copy, at: index + 1) }
    }

    func deletePage(_ index: Int) {
        guard let id, let n = store.notebook(id), n.pages.count > 1, n.pages.indices.contains(index) else { return }
        let gone = n.pages[index].id
        changePages(showing: min(index, n.pages.count - 2)) { $0.pages.remove(at: index) }
        store.removeInk(n, gone)
    }

    /// Move page `from` to be page `to`.
    func movePage(_ from: Int, to: Int) {
        guard let id, let n = store.notebook(id), n.pages.indices.contains(from), n.pages.indices.contains(to), from != to
        else { return }
        changePages(showing: to) { $0.pages.insert($0.pages.remove(at: from), at: to) }
    }

    /// This page's paper (or every page's that isn't a PDF's); new pages come on it from now on.
    func applyPaper(_ t: Notebook.Template, to index: Int?) {
        guard let id, let n = store.notebook(id) else { return }
        let targets = index.map { [$0] } ?? Array(n.pages.indices)
        let resized = targets.contains { i in
            n.pages.indices.contains(i) && n.pages[i].pdf == nil && Notebook.PageSize(n.pages[i].size) != t.size
        }
        var changed = n
        for i in targets where changed.pages.indices.contains(i) && changed.pages[i].pdf == nil {
            changed.pages[i].paper = t.paper
            changed.pages[i].colour = t.colour
            changed.pages[i].width = t.size.size.width
            changed.pages[i].height = t.size.size.height
        }
        changed.template = t
        if resized {
            changePages { $0 = changed }
        } else {
            // Same sizes: the paper is drawn again under the ink, and undo carries on.
            flush()
            changed.updated = .now
            store.save(changed)
            repaint()
        }
    }

    /// A PDF's pages (slides, a handout) after page `index`, to write on.
    func addPDF(_ url: URL, after index: Int) -> Bool {
        guard let id, let n = store.notebook(id), let pages = store.pages(fromPDF: url, for: n) else { return false }
        let at = min(index + 1, n.pages.count)
        changePages(showing: at) { $0.pages.insert(contentsOf: pages, at: at) }
        return true
    }
}

// MARK: - The pages view

/// Every page small, to go to one, add, duplicate, move or delete: beside the page on an iPad (`sidebar`), or a grid
/// of its own. Tap a page to go to it; hold one for what can be done to it, or drag it to its new place.
struct NotebookPages: View {
    var canvas: NotebookCanvas
    var sidebar = false
    /// After going to a page (the grid closes).
    var went: (() -> Void)?
    var addPDF: (() -> Void)?
    @State private var store = NotebookStore.shared
    @State private var deleting: Int?

    private var notebook: Notebook? { canvas.id.flatMap(store.notebook) }

    var body: some View {
        let pages = notebook?.pages ?? []
        ScrollViewReader { proxy in
            ScrollView {
                LazyVGrid(columns: sidebar ? [GridItem(.flexible())] : [GridItem(.adaptive(minimum: 128, maximum: 176), spacing: 20)],
                          spacing: sidebar ? 18 : 24) {
                    ForEach(Array(pages.enumerated()), id: \.element.id) { i, page in
                        cell(page, i, count: pages.count).id(page.id)
                    }
                    addCell
                }
                .padding(sidebar ? 16 : 20)
            }
            .onAppear {
                if pages.indices.contains(canvas.current) { proxy.scrollTo(pages[canvas.current].id, anchor: .center) }
            }
        }
        .accessibilityIdentifier("page.notebookPages")
        .confirmDeletingPage($deleting, canvas: canvas)
        // Beside the page, the pictures follow the writing (elsewhere they're drawn when the pages open).
        .onAppear { if sidebar { canvas.picturesShowing = true; canvas.flush() } }
        .onDisappear { if sidebar { canvas.picturesShowing = false } }
    }

    private func cell(_ page: Notebook.Page, _ i: Int, count: Int) -> some View {
        let showing = i == canvas.current
        let width: CGFloat = sidebar ? 132 : 150
        return VStack(spacing: 6) {
            Group {
                if let n = notebook {
                    Image(uiImage: store.thumbnail(n, page, width: width))
                        .resizable()
                        .aspectRatio(CGFloat(page.width / page.height), contentMode: .fit)
                }
            }
            .frame(maxWidth: width)
            .overlay(Rectangle().strokeBorder(Color.line, lineWidth: 1))
            .padding(3)
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(showing ? Color.ink : .clear, lineWidth: 2))
            Text("\(i + 1)")
                .textStyle(.meta)
                .monospacedDigit()
                .fontWeight(showing ? .bold : .medium)
                .foregroundStyle(showing ? Color.ink : .inkSubtle)
        }
        .contentShape(.rect)
        .onTapGesture {
            canvas.go(to: i)
            went?()
        }
        .contextMenu {
            Button("Add a page after", icon: "page_add") { canvas.addPage(after: i) }
            Button("Duplicate", icon: "content_copy") { canvas.duplicatePage(i) }
            if i > 0 { Button("Move earlier", icon: "arrow_upward") { canvas.movePage(i, to: i - 1) } }
            if i + 1 < count { Button("Move later", icon: "arrow_downward") { canvas.movePage(i, to: i + 1) } }
            if count > 1 {
                Divider()
                Button("Delete page", icon: "delete", role: .destructive) {
                    if canvas.pageIsBlank(i) { canvas.deletePage(i) } else { deleting = i }
                }
            }
        }
        .draggable(page.id.uuidString) {
            if let n = notebook {
                Image(uiImage: store.thumbnail(n, page, width: width)).resizable().scaledToFit().frame(width: width * 0.8)
            }
        }
        .dropDestination(for: String.self) { items, _ in
            guard let moved = items.first, let from = notebook?.pages.firstIndex(where: { $0.id.uuidString == moved }) else { return false }
            canvas.movePage(from, to: i)
            return true
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Page \(i + 1), \(page.pdf == nil ? page.paper.name : "from a PDF")\(showing ? ", showing" : "")")
        .accessibilityAddTraits(.isButton)
        .accessibilityIdentifier("notebook.page.\(i + 1)")
    }

    private var addCell: some View {
        Menu {
            Button("Add a page", icon: "page_add") { canvas.addPage(); went?() }
            if let addPDF { Button("Add pages from a PDF", icon: "picture_as_pdf") { addPDF() } }
        } label: {
            VStack(spacing: 6) {
                RoundedRectangle(cornerRadius: 3)
                    .strokeBorder(Color.line, lineWidth: 1)
                    .aspectRatio(612.0 / 792.0, contentMode: .fit)
                    .frame(maxWidth: sidebar ? 132 : 150)
                    .overlay { Icon("add", size: 22).foregroundStyle(.inkMuted) }
                    .padding(3)
                Text("Add a page").textStyle(.meta).foregroundStyle(.inkMuted)
            }
        } primaryAction: {
            canvas.addPage()
            went?()
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("notebook.pages.add")
    }
}

extension View {
    /// "Delete page 3?": asked before a page with writing on it goes (deleting a page can't be undone).
    func confirmDeletingPage(_ page: Binding<Int?>, canvas: NotebookCanvas) -> some View {
        confirmationDialog(page.wrappedValue.map { "Delete page \($0 + 1)?" } ?? "Delete this page?",
                           isPresented: Binding(get: { page.wrappedValue != nil }, set: { if !$0 { page.wrappedValue = nil } }),
                           titleVisibility: .visible, presenting: page.wrappedValue) { i in
            Button("Delete page", role: .destructive) { canvas.deletePage(i) }
        } message: { _ in
            Text("What's written on it goes too. This can't be undone.")
        }
    }
}

/// The pages as a grid of their own (a phone, or beside a lecture): Done closes it.
struct NotebookPagesSheet: View {
    var canvas: NotebookCanvas
    var addPDF: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            NotebookPages(canvas: canvas, went: { dismiss() }, addPDF: { dismiss(); addPDF() })
                .page()
                .navigationTitle("Pages")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .confirmationAction) {
                        Button { dismiss() } label: { Text("Done").textStyle(.button) }
                    }
                }
        }
    }
}
