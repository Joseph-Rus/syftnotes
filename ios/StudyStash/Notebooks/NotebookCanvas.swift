import PencilKit
import SwiftUI
import UIKit
import UIKit.UIGestureRecognizerSubclass

/// What a finger does on the page: draws, or scrolls and zooms (the Pencil always draws). Automatic: fingers draw
/// until an Apple Pencil touches the page (or the system's "Only Draw with Apple Pencil" is on), then they scroll.
enum FingerMode: String {
    case auto, draw, scroll
    static let key = "notebook.fingers"
    /// An Apple Pencil has written in a notebook on this device.
    static let pencilSeenKey = "notebook.pencilSeen"
}

/// A notebook's pages one under another on one PencilKit canvas, as a pad of paper: scrolled down through, pinched to
/// zoom, the ink of every page in one drawing (so the lasso moves writing between pages and undo goes back through
/// all of it). Each page's ink is still kept apart on disk, in the page's own units: when the canvas changes, each
/// stroke goes to the page it's on. The paper is drawn in tiles under the ink, sharp at any zoom.
@MainActor @Observable
final class NotebookCanvas: NSObject {
    /// The page most of the screen shows (from 0), and how many there are.
    private(set) var current = 0
    private(set) var pageCount = 1
    private(set) var canUndo = false
    private(set) var canRedo = false
    /// Fingers draw right now (no Pencil, or the student chose so).
    private(set) var fingersDraw = true

    @ObservationIgnored let canvas = PKCanvasView()
    @ObservationIgnored let picker = PKToolPicker()
    /// What Apple's pens are shown for. Not the canvas: a phone showed the keyboard (made first responder, the canvas
    /// asks for one whenever a menu opens over it).
    @ObservationIgnored let tools = ToolsResponder()
    @ObservationIgnored private let underlay = UIView()
    @ObservationIgnored private let hint = UILabel()
    @ObservationIgnored private var tiles: [CALayer] = []
    @ObservationIgnored private(set) var frames: [CGRect] = []
    @ObservationIgnored private var contentWidth: CGFloat = Notebook.letter.width
    @ObservationIgnored private var contentHeight: CGFloat = Notebook.letter.height
    @ObservationIgnored private(set) var id: UUID?
    @ObservationIgnored let store: NotebookStore
    @ObservationIgnored private var saveTask: Task<Void, Never>?
    @ObservationIgnored private var dirty = false
    @ObservationIgnored private var settingDrawing = false
    /// What each page's ink was when last written, so only a page that changed is written again.
    @ObservationIgnored private var written: [UUID: Int] = [:]
    /// The bars over the canvas (title bar, home indicator), from SwiftUI.
    @ObservationIgnored private var chrome: UIEdgeInsets = .zero
    @ObservationIgnored private var obscured: CGFloat = 0
    @ObservationIgnored private var fitWidth: CGFloat = 0
    /// The zoom as a share of fitting the page to the width: kept when the window changes size.
    @ObservationIgnored private var zoomFactor: CGFloat = 1
    @ObservationIgnored private var pendingScroll: Int?
    @ObservationIgnored private var pulling = false
    /// The last page has writing on it (so pulling past it adds one).
    @ObservationIgnored private var lastInked = false
    @ObservationIgnored private var grounds: [CALayer] = []
    @ObservationIgnored private var laidOutSize: CGSize = .zero
    @ObservationIgnored nonisolated(unsafe) private var observers: [NSObjectProtocol] = []
    /// Pulled up past the last page: add a page (the editor does it, so it's said the same way).
    @ObservationIgnored var onPullToAdd: (() -> Void)?

    static let gap: CGFloat = 28
    private static let pullDistance: CGFloat = 72

    init(store: NotebookStore = .shared) {
        self.store = store
        super.init()
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        canvas.delegate = self
        canvas.alwaysBounceVertical = true
        canvas.contentInsetAdjustmentBehavior = .never
        canvas.overrideUserInterfaceStyle = .light
        canvas.bouncesZoom = true
        canvas.showsHorizontalScrollIndicator = false
        canvas.decelerationRate = .normal

        underlay.isUserInteractionEnabled = false
        underlay.layer.anchorPoint = .zero
        canvas.insertSubview(underlay, at: 0)
        tools.isUserInteractionEnabled = false
        canvas.addSubview(tools)

        hint.font = UIFont(name: Atkinson.face(.medium), size: 13) ?? .systemFont(ofSize: 13, weight: .medium)
        hint.textColor = Palette.inkSubtle
        hint.textAlignment = .center
        hint.isUserInteractionEnabled = false
        hint.isHidden = true
        canvas.addSubview(hint)

        // The Pencil draws; whether a finger does is the student's (FingerMode). A Pencil's first touch tells.
        let watcher = PencilWatcher { [weak self] in self?.sawPencil() }
        watcher.delegate = self
        canvas.addGestureRecognizer(watcher)
        // Two fingers tapped undo, three redo, as in other notebook apps.
        let undoTap = UITapGestureRecognizer(target: self, action: #selector(tappedUndo))
        undoTap.numberOfTouchesRequired = 2
        undoTap.allowedTouchTypes = [NSNumber(value: UITouch.TouchType.direct.rawValue)]
        undoTap.delegate = self
        canvas.addGestureRecognizer(undoTap)
        let redoTap = UITapGestureRecognizer(target: self, action: #selector(tappedRedo))
        redoTap.numberOfTouchesRequired = 3
        redoTap.allowedTouchTypes = [NSNumber(value: UITouch.TouchType.direct.rawValue)]
        redoTap.delegate = self
        canvas.addGestureRecognizer(redoTap)

        // Pictures: `-notebook-zoom 0.5` opens at half the fitted size.
        if let zoom = DebugLaunch.value("-notebook-zoom").flatMap(Double.init) { zoomFactor = zoom }

        picker.stateAutosaveName = "notebook"
        picker.colorUserInterfaceStyle = .light
        picker.addObserver(canvas)
        picker.addObserver(self)
        applyFingers()

        let center = NotificationCenter.default
        for name in [Notification.Name.NSUndoManagerDidCloseUndoGroup, .NSUndoManagerDidUndoChange, .NSUndoManagerDidRedoChange] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshUndo() }
            })
        }
    }

    deinit {
        for o in observers { NotificationCenter.default.removeObserver(o) }
    }

    // MARK: the notebook

    private var notebook: Notebook? { id.flatMap(store.notebook) }

    /// Show this notebook (from `page`, or the first).
    func attach(_ id: UUID, page: Int? = nil) {
        guard self.id != id else { return }
        self.id = id
        load(scrollTo: page ?? 0)
    }

    /// Lay the pages out again and draw them from disk: after pages are added, moved, removed or resized. What was
    /// undoable stops being (the strokes are where the pages put them now).
    func load(scrollTo page: Int?) {
        guard let n = notebook else { return }
        layOut(n)
        var strokes: [PKStroke] = []
        for (i, p) in n.pages.enumerated() {
            strokes += store.drawing(n, p).transformed(using: CGAffineTransform(translationX: frames[i].minX, y: frames[i].minY)).strokes
        }
        settingDrawing = true
        canvas.drawing = PKDrawing(strokes: strokes)
        settingDrawing = false
        // What each page holds now, as `flush` will see it, so only a page written on is written again.
        let pages = strokesByPage()
        written = Dictionary(zip(n.pages.map(\.id), pages.map(Self.signature)), uniquingKeysWith: { a, _ in a })
        noteLastPage(pages)
        canvas.undoManager?.removeAllActions()
        refreshUndo()
        pageCount = n.pages.count
        pendingScroll = page.map { min(max(0, $0), n.pages.count - 1) }
        fit()
        if let page = pendingScroll, canvas.bounds.width > 0 { go(to: page, animated: false) }
    }

    /// The pages' places on the canvas, and a tile of paper under each.
    private func layOut(_ n: Notebook) {
        contentWidth = n.pages.map { CGFloat($0.width) }.max() ?? Notebook.letter.width
        var y: CGFloat = 0
        frames = n.pages.map { p in
            defer { y += CGFloat(p.height) + Self.gap }
            return CGRect(x: ((contentWidth - CGFloat(p.width)) / 2).rounded(), y: y, width: CGFloat(p.width), height: CGFloat(p.height))
        }
        contentHeight = max(y - Self.gap, 1)
        underlay.layer.sublayers?.forEach { $0.removeFromSuperlayer() }
        tiles = []
        grounds = []
        let screen = canvas.window?.screen.scale ?? canvas.traitCollection.displayScale
        for (i, p) in n.pages.enumerated() {
            let shadow = CALayer()
            shadow.frame = frames[i]
            shadow.backgroundColor = PaperDrawing.inks(p.colour).ground.cgColor
            shadow.shadowColor = UIColor.black.cgColor
            shadow.shadowOpacity = 0.14
            shadow.shadowRadius = 3
            shadow.shadowOffset = CGSize(width: 0, height: 1)
            shadow.shadowPath = CGPath(rect: CGRect(origin: .zero, size: frames[i].size), transform: nil)
            shadow.borderColor = UIColor(white: 0, alpha: 0.07).cgColor
            shadow.borderWidth = 0.5
            underlay.layer.addSublayer(shadow)
            grounds.append(shadow)
            let tile = PaperTile()
            tile.frame = frames[i]
            tile.contentsScale = max(screen, 1)
            tile.set(p, pdf: store.pdf(n, p)?.pageRef)
            underlay.layer.addSublayer(tile)
            tiles.append(tile)
        }
        underlay.bounds = CGRect(x: 0, y: 0, width: contentWidth, height: contentHeight)
    }

    /// The paper of each page drawn again (its ruling or colour changed; the pages kept their sizes).
    func repaint() {
        guard let n = notebook, n.pages.count == tiles.count else { load(scrollTo: current); return }
        for (i, p) in n.pages.enumerated() {
            guard let tile = tiles[i] as? PaperTile else { continue }
            tile.set(p, pdf: store.pdf(n, p)?.pageRef)
            grounds[i].backgroundColor = PaperDrawing.inks(p.colour).ground.cgColor
            tile.setNeedsDisplay()
        }
        updateHint()
    }

    // MARK: size and zoom

    /// The window's size or the bars over it changed.
    func boundsChanged() {
        guard let host = canvas.superview, host.bounds.size != laidOutSize || pendingScroll != nil else { return }
        laidOutSize = host.bounds.size
        canvas.frame = host.bounds
        fit()
        if let page = pendingScroll, canvas.bounds.width > 0 { go(to: page, animated: false) }
    }

    /// The desk follows the app's light or dark; the canvas is always paper (light), so its words are told which.
    func setAppearance(_ style: UIUserInterfaceStyle) {
        hint.overrideUserInterfaceStyle = style
    }

    func setChrome(_ insets: UIEdgeInsets) {
        guard insets != chrome else { return }
        chrome = insets
        updateInsets()
    }

    /// Fit the widest page to the width (less a little desk each side), keeping the zoom the student chose.
    private func fit() {
        let width = canvas.bounds.width
        guard width > 0 else { return }
        let side: CGFloat = width > 700 ? 28 : 10
        let fit = max(0.1, (width - 2 * side) / contentWidth)
        let keep = pendingScroll == nil ? visibleTop() : nil
        if abs(fit - fitWidth) > 0.0001 {
            fitWidth = fit
            canvas.minimumZoomScale = fit * 0.35
            canvas.maximumZoomScale = max(fit * 5, 3)
            canvas.setZoomScale(fit * zoomFactor, animated: false)
        }
        zoomed()
        if let keep { canvas.contentOffset.y = keep * canvas.zoomScale - canvas.contentInset.top }
    }

    private func zoomed() {
        let s = canvas.zoomScale
        if fitWidth > 0 { zoomFactor = s / fitWidth }
        underlay.transform = CGAffineTransform(scaleX: s, y: s)
        underlay.layer.position = .zero
        canvas.contentSize = CGSize(width: contentWidth * s, height: contentHeight * s)
        updateInsets()
    }

    private func updateInsets() {
        let s = canvas.zoomScale
        let x = max((canvas.bounds.width - contentWidth * s) / 2, 0)
        let bottom = max(chrome.bottom, obscured) + 76
        let insets = UIEdgeInsets(top: chrome.top + 14, left: x, bottom: bottom, right: x)
        if canvas.contentInset != insets { canvas.contentInset = insets }
        canvas.verticalScrollIndicatorInsets = UIEdgeInsets(top: chrome.top, left: 0, bottom: max(chrome.bottom, obscured), right: 0)
        updateHint()
    }

    /// The top of the screen, in page units down the canvas.
    private func visibleTop() -> CGFloat? {
        guard canvas.zoomScale > 0, !frames.isEmpty else { return nil }
        return (canvas.contentOffset.y + canvas.contentInset.top) / canvas.zoomScale
    }

    /// Scroll so page `i`'s top is at the top of the screen.
    func go(to i: Int, animated: Bool = true) {
        guard frames.indices.contains(i), canvas.bounds.width > 0 else { pendingScroll = i; return }
        pendingScroll = nil
        let maxY = max(canvas.contentSize.height + canvas.contentInset.bottom - canvas.bounds.height, -canvas.contentInset.top)
        let y = min(frames[i].minY * canvas.zoomScale - canvas.contentInset.top, maxY)
        canvas.setContentOffset(CGPoint(x: canvas.contentOffset.x, y: y), animated: animated)
        if !animated { updateCurrent() }
    }

    /// The page at 40% down the screen.
    private func updateCurrent() {
        guard !frames.isEmpty, canvas.zoomScale > 0 else { return }
        let visible = canvas.bounds.height - canvas.contentInset.top - max(chrome.bottom, obscured)
        let y = (canvas.contentOffset.y + canvas.contentInset.top + visible * 0.4) / canvas.zoomScale
        let i = frames.firstIndex { y < $0.maxY + Self.gap / 2 } ?? frames.count - 1
        if i != current {
            current = i
            fitInkToPaper()
        }
    }

    // MARK: pulling up for a page

    /// "Pull up to add a page" under the last page, once it has writing on it.
    private func updateHint() {
        hint.isHidden = frames.isEmpty || !lastInked
        guard !hint.isHidden else { return }
        let s = canvas.zoomScale
        hint.text = pulling ? "Let go to add a page" : "Pull up to add a page"
        hint.sizeToFit()
        // Under the middle of the pages, which the insets keep in the middle of the screen.
        hint.center = CGPoint(x: contentWidth * s / 2, y: contentHeight * s + 30)
    }

    private func noteLastPage(_ pages: [[PKStroke]]) {
        lastInked = !(pages.last?.isEmpty ?? true)
        updateHint()
    }

    private func overscroll() -> CGFloat {
        canvas.contentOffset.y + canvas.bounds.height - (canvas.contentSize.height + canvas.contentInset.bottom)
    }

    // MARK: ink

    /// Write each page's ink that changed, now (closing, the pages view opening, the app going away).
    func flush() {
        saveTask?.cancel()
        saveTask = nil
        guard dirty, let n = notebook, n.pages.count == frames.count else { return }
        dirty = false
        let pages = strokesByPage()
        noteLastPage(pages)
        var changed = false
        for (i, p) in n.pages.enumerated() {
            let sig = Self.signature(pages[i])
            guard written[p.id] != sig else { continue }
            // Only a page written on is moved into its own units (its strokes measured again) and written.
            let ink = PKDrawing(strokes: pages[i]).transformed(using: CGAffineTransform(translationX: -frames[i].minX, y: -frames[i].minY))
            store.setDrawing(ink, n, p)
            written[p.id] = sig
            changed = true
        }
        if changed, var latest = notebook {
            latest.updated = .now
            store.save(latest)
        }
    }

    /// Nothing on page `i`: no writing and no PDF (deleting it loses nothing, so it isn't asked about).
    func pageIsBlank(_ i: Int) -> Bool {
        guard let n = notebook, n.pages.indices.contains(i), frames.indices.contains(i) else { return true }
        return n.pages[i].pdf == nil && !canvas.drawing.strokes.contains { page(at: $0.renderBounds.midY) == i }
    }

    /// The canvas's strokes, each with the page it's on, as they are on the canvas: their sizes are known already, so
    /// this stays quick in a notebook of thousands of strokes (a stroke moved is measured afresh).
    private func strokesByPage() -> [[PKStroke]] {
        var pages = [[PKStroke]](repeating: [], count: frames.count)
        guard !frames.isEmpty else { return pages }
        for stroke in canvas.drawing.strokes { pages[page(at: stroke.renderBounds.midY)].append(stroke) }
        return pages
    }

    /// The page a point down the canvas belongs to: the gaps are split between the pages either side.
    /// Looked up by halves (the pages run down in order): every stroke is looked up each save.
    private func page(at y: CGFloat) -> Int {
        var low = 0, high = max(frames.count - 1, 0)
        while low < high {
            let mid = (low + high) / 2
            if y < frames[mid].maxY + Self.gap / 2 { high = mid } else { low = mid + 1 }
        }
        return low
    }

    private static func signature(_ strokes: [PKStroke]) -> Int {
        var h = Hasher()
        h.combine(strokes.count)
        for s in strokes {
            let b = s.renderBounds
            h.combine(b.minX); h.combine(b.minY); h.combine(b.width); h.combine(b.height)
            h.combine(s.path.count)
            h.combine(s.ink.color.hashValue)
            h.combine(s.mask == nil)
        }
        return h.finalize()
    }

    func undo() { canvas.undoManager?.undo(); refreshUndo() }
    func redo() { canvas.undoManager?.redo(); refreshUndo() }

    @objc private func tappedUndo() { if canUndo { undo() } }
    @objc private func tappedRedo() { if canRedo { redo() } }

    /// Set only when it changes: each set redraws the bars, and this runs after every stroke.
    private func refreshUndo() {
        let undo = canvas.undoManager?.canUndo ?? false, redo = canvas.undoManager?.canRedo ?? false
        if undo != canUndo { canUndo = undo }
        if redo != canRedo { canRedo = redo }
    }

    // MARK: the pens

    /// Apple's pens, shown while the page is written on (hidden under a sheet or popover).
    func showTools(_ on: Bool) {
        picker.setVisible(on, forFirstResponder: tools)
        if on { tools.becomeFirstResponder() }
    }

    /// A dark pen on dark paper (or a white one on light) can't be seen: it's turned to white (or black) on that page.
    private func fitInkToPaper() {
        guard let n = notebook, n.pages.indices.contains(current), n.pages[current].pdf == nil,
              let tool = picker.selectedTool as? PKInkingTool, tool.inkType != .marker else { return }
        let dark = n.pages[current].colour == .dark
        var white: CGFloat = 0
        tool.color.getWhite(&white, alpha: nil)
        if dark, white < 0.22 { picker.selectedTool = PKInkingTool(tool.inkType, color: .white, width: tool.width) }
        if !dark, white > 0.92 { picker.selectedTool = PKInkingTool(tool.inkType, color: .black, width: tool.width) }
    }

    func applyFingers() {
        let mode = FingerMode(rawValue: UserDefaults.standard.string(forKey: FingerMode.key) ?? "") ?? .auto
        let pencil = UserDefaults.standard.bool(forKey: FingerMode.pencilSeenKey) || UIPencilInteraction.prefersPencilOnlyDrawing
        switch mode {
        case .draw: canvas.drawingPolicy = .anyInput
        case .scroll: canvas.drawingPolicy = .pencilOnly
        case .auto: canvas.drawingPolicy = pencil ? .pencilOnly : .default
        }
        fingersDraw = canvas.drawingPolicy != .pencilOnly
    }

    private func sawPencil() {
        guard !UserDefaults.standard.bool(forKey: FingerMode.pencilSeenKey) else { return }
        UserDefaults.standard.set(true, forKey: FingerMode.pencilSeenKey)
        applyFingers()
    }
}

extension NotebookCanvas: PKCanvasViewDelegate {
    func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
        guard !settingDrawing else { return }
        // Nothing here goes through the strokes: it runs after every stroke, however full the notebook. The save soon
        // after (a moment's pause in the writing) sorts them into pages, and says whether the last page has writing.
        dirty = true
        refreshUndo()
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(0.5))
            guard !Task.isCancelled else { return }
            self?.flush()
        }
    }

    func scrollViewDidZoom(_ scrollView: UIScrollView) { zoomed() }

    func scrollViewDidScroll(_ scrollView: UIScrollView) {
        updateCurrent()
        let pull = !hint.isHidden && scrollView.isDragging && overscroll() > Self.pullDistance
        if pull != pulling {
            pulling = pull
            if pull { UIImpactFeedbackGenerator(style: .light).impactOccurred() }
            updateHint()
        }
    }

    func scrollViewWillEndDragging(_ scrollView: UIScrollView, withVelocity velocity: CGPoint, targetContentOffset: UnsafeMutablePointer<CGPoint>) {
        guard pulling else { return }
        pulling = false
        updateHint()
        DispatchQueue.main.async { [weak self] in self?.onPullToAdd?() }
    }
}

extension NotebookCanvas: PKToolPickerObserver {
    /// Docked along the foot (a phone), the pens cover the page's foot: the canvas scrolls clear of them.
    func toolPickerFramesObscuredDidChange(_ toolPicker: PKToolPicker) {
        guard let host = canvas.superview else { return }
        let frame = toolPicker.frameObscured(in: host)
        obscured = frame.isNull ? 0 : max(0, host.bounds.maxY - frame.minY)
        updateInsets()
    }
}

extension NotebookCanvas: UIGestureRecognizerDelegate {
    func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool { true }
}

/// Holds first responder while a notebook is open, so Apple's pens show, and never asks for a keyboard.
final class ToolsResponder: UIView {
    override var canBecomeFirstResponder: Bool { true }
    /// With Apple's pens docked on a phone, a menu opening brought up the keyboard in their place: an empty input view
    /// stands there instead.
    private let nothing = UIView(frame: .zero)
    override var inputView: UIView? { nothing }
}

/// Watches for an Apple Pencil touching the canvas, and lets every touch through.
private final class PencilWatcher: UIGestureRecognizer {
    private let saw: () -> Void

    init(saw: @escaping () -> Void) {
        self.saw = saw
        super.init(target: nil, action: nil)
        allowedTouchTypes = [NSNumber(value: UITouch.TouchType.pencil.rawValue)]
        cancelsTouchesInView = false
        delaysTouchesBegan = false
        delaysTouchesEnded = false
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        if touches.contains(where: { $0.type == .pencil }) { saw() }
        state = .failed
    }
}

/// One page's paper (or PDF page), drawn in tiles as the zoom asks: sharp however close the page is looked at.
final class PaperTile: CATiledLayer {
    nonisolated(unsafe) private var paper: Notebook.Paper = .plain
    nonisolated(unsafe) private var colour: Notebook.PaperColour = .white
    nonisolated(unsafe) private var pdf: CGPDFPage?
    nonisolated(unsafe) private var size: CGSize = .zero

    override init() {
        super.init()
        levelsOfDetail = 4
        levelsOfDetailBias = 4
        tileSize = CGSize(width: 512, height: 512)
        isOpaque = true
        needsDisplayOnBoundsChange = true
    }

    override init(layer: Any) { super.init(layer: layer) }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override class func fadeDuration() -> CFTimeInterval { 0 }

    func set(_ page: Notebook.Page, pdf: CGPDFPage?) {
        paper = page.paper
        colour = page.colour
        size = page.size
        self.pdf = pdf
        backgroundColor = (pdf == nil ? PaperDrawing.inks(page.colour).ground : .white).cgColor
    }

    nonisolated override func draw(in ctx: CGContext) {
        let rect = CGRect(origin: .zero, size: size)
        if let pdf {
            ctx.setFillColor(UIColor.white.cgColor)
            ctx.fill(rect)
            PaperDrawing.drawPDF(pdf, in: rect, ctx: ctx)
        } else {
            PaperDrawing.draw(paper, colour: colour, in: rect, ctx: ctx)
        }
    }
}

/// The canvas in SwiftUI. `insets` are the bars over it (it runs under them, to the screen's edges).
struct NotebookCanvasView: UIViewRepresentable {
    var model: NotebookCanvas
    var id: UUID
    var page: Int?
    var insets: EdgeInsets

    func makeUIView(context: Context) -> Host {
        let host = Host(model: model)
        model.attach(id, page: page)
        return host
    }

    func updateUIView(_ host: Host, context: Context) {
        model.setChrome(UIEdgeInsets(top: insets.top, left: insets.leading, bottom: insets.bottom, right: insets.trailing))
        model.attach(id)
    }

    final class Host: UIView {
        let model: NotebookCanvas
        init(model: NotebookCanvas) {
            self.model = model
            super.init(frame: .zero)
            addSubview(model.canvas)
            model.setAppearance(traitCollection.userInterfaceStyle)
            registerForTraitChanges([UITraitUserInterfaceStyle.self]) { (host: Host, _: UITraitCollection) in
                host.model.setAppearance(host.traitCollection.userInterfaceStyle)
            }
        }
        required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

        override func layoutSubviews() {
            super.layoutSubviews()
            model.boundsChanged()
        }

        override func didMoveToWindow() {
            super.didMoveToWindow()
            guard window != nil else { return }
            model.showTools(true)
            model.boundsChanged()
        }
    }
}
