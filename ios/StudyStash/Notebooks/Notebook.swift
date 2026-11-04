import CoreText
import Foundation
import Observation
import PDFKit
import PencilKit
import UIKit

/// A handwritten notebook on this phone: pages of ink (PencilKit), each on its paper (blank, ruled, grid, dotted or
/// Cornell notes, on white, cream or dark) or over a PDF page (one imported from GoodNotes keeps its PDFs, and slides
/// can be written on). Saved to the library as a PDF in a class (and a lecture), where it's read, handwriting too, for
/// notes and Ask; saved again, it replaces the copy there.
struct Notebook: Codable, Identifiable, Hashable, Sendable {
    /// What a page is printed with, in the picker's order. The raw value is what a saved notebook keeps, so it's never
    /// renamed (`lined` is wide ruled, its name from before narrow ruling came); a paper this version doesn't know
    /// opens blank. Each paper's name, line spacing and drawing are its one entry in `PaperDrawing.style(_:)`.
    enum Paper: String, Codable, CaseIterable, Sendable {
        case plain, narrow, lined, grid, dotted, cornell

        var name: String { PaperDrawing.style(self).name }
    }

    /// The paper's own colour. The ink is the student's, whatever the paper.
    enum PaperColour: String, Codable, CaseIterable, Sendable {
        case white, cream, dark

        var name: String {
            switch self {
            case .white: "White"
            case .cream: "Cream"
            case .dark: "Dark"
            }
        }
    }

    /// A page's size, at 72 points an inch.
    enum PageSize: String, Codable, CaseIterable, Sendable {
        case letter, a4

        var name: String { self == .letter ? "US Letter" : "A4" }
        var size: CGSize { self == .letter ? CGSize(width: 612, height: 792) : CGSize(width: 595.28, height: 841.89) }

        /// The size a page of these dimensions is, if it's one of these.
        init?(_ size: CGSize) {
            guard let match = Self.allCases.first(where: { abs($0.size.width - size.width) < 1 && abs($0.size.height - size.height) < 1 })
            else { return nil }
            self = match
        }
    }

    /// The paper new pages come on: its ruling, colour and size.
    struct Template: Codable, Hashable, Sendable {
        var paper: Paper = .narrow
        var colour: PaperColour = .white
        var size: PageSize = .letter

        static let standard = Template()

        /// "Narrow ruled, white, US Letter".
        var words: String { "\(paper.name), \(colour.name.lowercased()), \(size.name)" }
    }

    struct Page: Codable, Hashable, Identifiable, Sendable {
        var id = UUID()
        var width: Double
        var height: Double
        var paper: Paper = .narrow
        var colour: PaperColour = .white
        /// A PDF beside the notebook, and its page (from 1), drawn under the ink instead of the paper.
        var pdf: String?
        var pdfPage: Int = 1

        var size: CGSize { CGSize(width: width, height: height) }

        init(id: UUID = UUID(), width: Double, height: Double, paper: Paper = .narrow, colour: PaperColour = .white,
             pdf: String? = nil, pdfPage: Int = 1) {
            self.id = id
            self.width = width
            self.height = height
            self.paper = paper
            self.colour = colour
            self.pdf = pdf
            self.pdfPage = pdfPage
        }

        init(_ t: Template) {
            self.init(width: t.size.size.width, height: t.size.size.height, paper: t.paper, colour: t.colour)
        }
    }

    var id = UUID()
    var title: String
    var className: String?
    var lectureID: String?
    var lectureTitle: String?
    var pages: [Page]
    var created = Date.now
    var updated = Date.now
    /// The copy in the library, so saving again replaces it.
    var attachmentID: String?
    var savedAt: Date?
    var importedFrom: String?
    /// The paper new pages come on; nil in notebooks from before it was chosen (`newPages` works it out).
    var template: Template?

    /// US Letter at 72 points an inch, the paper a new page is unless A4 is chosen.
    static let letter = PageSize.letter.size

    static func blank(title: String, className: String?, lecture: (id: String, title: String)?,
                      template: Template = .standard) -> Notebook {
        Notebook(title: title, className: className, lectureID: lecture?.id, lectureTitle: lecture?.title,
                 pages: [Page(template)], template: template)
    }

    var needsSaving: Bool { savedAt.map { $0 < updated } ?? true }

    /// The paper a page added now comes on: the notebook's, or for one from before, its last paper page's.
    var newPages: Template {
        if let template { return template }
        guard let last = pages.last(where: { $0.pdf == nil }) else { return .standard }
        return Template(paper: last.paper, colour: last.colour, size: PageSize(last.size) ?? .letter)
    }
}

// Notebooks saved on a phone before a field was there still open: anything missing takes its default, and a paper
// this version doesn't know is blank.
extension Notebook.Page {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        width = try c.decodeIfPresent(Double.self, forKey: .width) ?? Notebook.letter.width
        height = try c.decodeIfPresent(Double.self, forKey: .height) ?? Notebook.letter.height
        paper = (try? c.decodeIfPresent(String.self, forKey: .paper)).flatMap { $0.flatMap(Notebook.Paper.init(rawValue:)) } ?? .plain
        colour = (try? c.decodeIfPresent(String.self, forKey: .colour)).flatMap { $0.flatMap(Notebook.PaperColour.init(rawValue:)) } ?? .white
        pdf = try c.decodeIfPresent(String.self, forKey: .pdf)
        pdfPage = try c.decodeIfPresent(Int.self, forKey: .pdfPage) ?? 1
    }
}

extension Notebook.Template {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        paper = (try? c.decodeIfPresent(String.self, forKey: .paper)).flatMap { $0.flatMap(Notebook.Paper.init(rawValue:)) } ?? .narrow
        colour = (try? c.decodeIfPresent(String.self, forKey: .colour)).flatMap { $0.flatMap(Notebook.PaperColour.init(rawValue:)) } ?? .white
        size = (try? c.decodeIfPresent(String.self, forKey: .size)).flatMap { $0.flatMap(Notebook.PageSize.init(rawValue:)) } ?? .letter
    }
}

extension Notebook {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? "Notes"
        className = try c.decodeIfPresent(String.self, forKey: .className)
        lectureID = try c.decodeIfPresent(String.self, forKey: .lectureID)
        lectureTitle = try c.decodeIfPresent(String.self, forKey: .lectureTitle)
        pages = try c.decodeIfPresent([Page].self, forKey: .pages) ?? []
        if pages.isEmpty { pages = [Page(.standard)] }
        created = try c.decodeIfPresent(Date.self, forKey: .created) ?? .now
        updated = try c.decodeIfPresent(Date.self, forKey: .updated) ?? created
        attachmentID = try c.decodeIfPresent(String.self, forKey: .attachmentID)
        savedAt = try c.decodeIfPresent(Date.self, forKey: .savedAt)
        importedFrom = try c.decodeIfPresent(String.self, forKey: .importedFrom)
        template = try? c.decodeIfPresent(Template.self, forKey: .template)
    }
}

/// The notebooks on this phone: Application Support/Notebooks/<id>/ holds notebook.json, each page's ink
/// (<page id>.drawing) and the PDFs pages are drawn over.
@MainActor @Observable
final class NotebookStore {
    static let shared = NotebookStore()

    private(set) var notebooks: [Notebook] = []
    /// Goes up each time a page's ink is written, so its picture is drawn again.
    private(set) var inkRevision: [UUID: Int] = [:]
    let folder: URL

    init(folder: URL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appending(path: "Notebooks")) {
        self.folder = folder
        #if DEBUG
        // Tests: `-fresh-notebooks` starts with none on this phone.
        if DebugLaunch.flag("-fresh-notebooks") { try? FileManager.default.removeItem(at: folder) }
        #endif
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let dirs = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        notebooks = dirs.compactMap { try? JSONDecoder.iso.decode(Notebook.self, from: Data(contentsOf: $0.appending(path: "notebook.json"))) }
            .sorted { $0.updated > $1.updated }
    }

    func dir(_ n: Notebook) -> URL { folder.appending(path: n.id.uuidString) }

    func notebook(_ id: UUID) -> Notebook? { notebooks.first { $0.id == id } }

    func save(_ n: Notebook) {
        try? FileManager.default.createDirectory(at: dir(n), withIntermediateDirectories: true)
        if let data = try? JSONEncoder.iso.encode(n) { try? data.write(to: dir(n).appending(path: "notebook.json"), options: .atomic) }
        if let i = notebooks.firstIndex(where: { $0.id == n.id }) { notebooks[i] = n } else { notebooks.insert(n, at: 0) }
        notebooks.sort { $0.updated > $1.updated }
    }

    func delete(_ n: Notebook) {
        try? FileManager.default.removeItem(at: dir(n))
        notebooks.removeAll { $0.id == n.id }
    }

    private func inkFile(_ n: Notebook, _ pageID: UUID) -> URL { dir(n).appending(path: "\(pageID.uuidString).drawing") }

    func drawing(_ n: Notebook, _ page: Notebook.Page) -> PKDrawing {
        (try? Data(contentsOf: inkFile(n, page.id))).flatMap { try? PKDrawing(data: $0) } ?? PKDrawing()
    }

    func setDrawing(_ d: PKDrawing, _ n: Notebook, _ page: Notebook.Page) {
        try? FileManager.default.createDirectory(at: dir(n), withIntermediateDirectories: true)
        try? d.dataRepresentation().write(to: inkFile(n, page.id), options: .atomic)
        inkRevision[page.id, default: 0] += 1
    }

    /// A page's ink copied to another page (a duplicate).
    func copyInk(_ n: Notebook, from: UUID, to: UUID) {
        try? FileManager.default.copyItem(at: inkFile(n, from), to: inkFile(n, to))
        inkRevision[to, default: 0] += 1
    }

    /// A page's ink thrown away (the page was deleted).
    func removeInk(_ n: Notebook, _ pageID: UUID) {
        try? FileManager.default.removeItem(at: inkFile(n, pageID))
        inkRevision[pageID] = nil
    }

    func pdf(_ n: Notebook, _ page: Notebook.Page) -> PDFPage? {
        guard let name = page.pdf, let doc = pdfCache[dir(n).appending(path: name)] ?? PDFDocument(url: dir(n).appending(path: name)) else { return nil }
        pdfCache[dir(n).appending(path: name)] = doc
        return doc.page(at: max(0, page.pdfPage - 1))
    }

    @ObservationIgnored private var pdfCache: [URL: PDFDocument] = [:]
    @ObservationIgnored private let pictures = NSCache<NSString, UIImage>()

    // MARK: rendering

    /// The page as it looks: its paper or PDF, then the ink. `inkScale` is how finely the ink is drawn.
    func render(_ n: Notebook, _ page: Notebook.Page, in ctx: CGContext, includeInk: Bool = true, inkScale: CGFloat = 2,
                hairline: CGFloat = 0) {
        let rect = CGRect(origin: .zero, size: page.size)
        if let pdfPage = pdf(n, page) {
            ctx.setFillColor(UIColor.white.cgColor)
            ctx.fill(rect)
            if let ref = pdfPage.pageRef { PaperDrawing.drawPDF(ref, in: rect, ctx: ctx) }
        } else {
            PaperDrawing.draw(page.paper, colour: page.colour, in: rect, ctx: ctx, hairline: hairline)
        }
        if includeInk {
            // PencilKit lightens dark ink for dark mode; a page is always paper, its ink as the student chose it.
            var ink = UIImage()
            UITraitCollection(userInterfaceStyle: .light).performAsCurrent { ink = drawing(n, page).image(from: rect, scale: inkScale) }
            UIGraphicsPushContext(ctx)
            ink.draw(in: rect)
            UIGraphicsPopContext()
        }
    }

    /// The whole notebook as one PDF, a page each, each its own size (a Letter page after a slide stays Letter).
    func pdfData(_ n: Notebook) -> Data {
        let data = NSMutableData()
        var first = CGRect(origin: .zero, size: n.pages.first?.size ?? Notebook.letter)
        guard let consumer = CGDataConsumer(data: data as CFMutableData),
              let ctx = CGContext(consumer: consumer, mediaBox: &first, nil) else { return Data() }
        for page in n.pages {
            var box = CGRect(origin: .zero, size: page.size)
            ctx.beginPDFPage([kCGPDFContextMediaBox as String: Data(bytes: &box, count: MemoryLayout<CGRect>.size)] as CFDictionary)
            ctx.saveGState()
            // The page is drawn y down, as on screen.
            ctx.translateBy(x: 0, y: box.height)
            ctx.scaleBy(x: 1, y: -1)
            render(n, page, in: ctx)
            ctx.restoreGState()
            ctx.endPDFPage()
        }
        ctx.closePDF()
        return data as Data
    }

    /// The first page, small: a notebook on a shelf.
    func thumbnail(_ n: Notebook, width: CGFloat = 240) -> UIImage? {
        guard let page = n.pages.first else { return nil }
        return thumbnail(n, page, width: width)
    }

    /// A page as small as `width` points, its paper and ink as they are now (kept until either changes).
    func thumbnail(_ n: Notebook, _ page: Notebook.Page, width: CGFloat) -> UIImage {
        let key = "\(n.id)|\(page.id)|\(page.paper)|\(page.colour)|\(page.width)x\(page.height)|\(page.pdf ?? "")\(page.pdfPage)|\(inkRevision[page.id] ?? 0)|\(width)" as NSString
        if let image = pictures.object(forKey: key) { return image }
        let scale = width / page.size.width
        let size = CGSize(width: width, height: (page.size.height * scale).rounded())
        let screen = UITraitCollection.current.displayScale > 0 ? UITraitCollection.current.displayScale : 2
        let image = UIGraphicsImageRenderer(size: size).image { r in
            r.cgContext.scaleBy(x: scale, y: scale)
            render(n, page, in: r.cgContext, inkScale: max(scale * screen, 0.5), hairline: 0.7 / (scale * screen))
        }
        pictures.setObject(image, forKey: key)
        return image
    }

    /// Save it to the library: the PDF into its class (and lecture), replacing the copy saved before.
    /// It's written in while it's on its way (the notebook open sends what was waiting): what's marked as saved is the
    /// notebook as it is now, saved as of when its PDF was made, so a page added or a rename meanwhile is kept, and is
    /// still to be sent. One deleted meanwhile stays deleted.
    func upload(_ id: UUID, api: APIClient) async throws {
        guard let n = notebooks.first(where: { $0.id == id }) else { return }
        let name = n.title.replacingOccurrences(of: "/", with: "-") + ".pdf"
        let made = Date.now
        let added = try await api.upload(files: [(name, "application/pdf", pdfData(n))], class: n.className, lecture: n.lectureID)
        if let old = n.attachmentID, old != added.first?.id { try? await api.deleteAttachment(old) }
        guard var latest = notebook(id) else { return }
        latest.attachmentID = added.first?.id
        latest.savedAt = made
        save(latest)
    }

    // MARK: PDFs to write on

    /// A PDF's pages as notebook pages, each its own size with the PDF under the ink (slides to write on). The PDF is
    /// kept beside the notebook. Nil if it isn't a PDF that can be read.
    func pages(fromPDF url: URL, for n: Notebook) -> [Notebook.Page]? {
        let access = url.startAccessingSecurityScopedResource()
        defer { if access { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url), let doc = PDFDocument(data: data), doc.pageCount > 0 else { return nil }
        try? FileManager.default.createDirectory(at: dir(n), withIntermediateDirectories: true)
        let name = "pdf-\(UUID().uuidString.prefix(8)).pdf"
        guard (try? data.write(to: dir(n).appending(path: name))) != nil else { return nil }
        return (0..<doc.pageCount).compactMap { i in
            guard let p = doc.page(at: i) else { return nil }
            // The page as a viewer shows it: its crop box, turned as the PDF says.
            var box = p.bounds(for: .cropBox).size
            if p.rotation % 180 != 0 { box = CGSize(width: box.height, height: box.width) }
            return Notebook.Page(width: box.width, height: box.height, paper: .plain, pdf: name, pdfPage: i + 1)
        }
    }

    // MARK: GoodNotes

    /// A GoodNotes notebook, as one of these: each page's size and PDF kept, its strokes as PencilKit ink.
    func importGoodNotes(_ g: GoodNotesNotebook, className: String?, lecture: (id: String, title: String)?) -> Notebook {
        var n = Notebook(title: g.title, className: className, lectureID: lecture?.id, lectureTitle: lecture?.title, pages: [],
                         importedFrom: "GoodNotes")
        try? FileManager.default.createDirectory(at: dir(n), withIntermediateDirectories: true)
        var pdfNames: [Data: String] = [:]
        for gp in g.pages {
            var page = Notebook.Page(width: gp.size.width, height: gp.size.height, paper: .plain)
            if let bg = gp.background {
                let name = pdfNames[bg.pdf] ?? "background-\(pdfNames.count + 1).pdf"
                if pdfNames[bg.pdf] == nil {
                    try? bg.pdf.write(to: dir(n).appending(path: name))
                    pdfNames[bg.pdf] = name
                }
                page.pdf = name
                page.pdfPage = bg.page
            }
            setDrawing(Self.drawing(from: gp.strokes), n, page)
            n.pages.append(page)
        }
        if n.pages.isEmpty { n.pages = [Notebook.Page(width: Notebook.letter.width, height: Notebook.letter.height, paper: .plain)] }
        n.template = Notebook.Template(paper: .plain, colour: .white, size: Notebook.PageSize(n.pages[0].size) ?? .letter)
        save(n)
        return n
    }

    nonisolated static func drawing(from strokes: [GoodNotesNotebook.Stroke]) -> PKDrawing {
        var out: [PKStroke] = []
        for s in strokes {
            let color = UIColor(red: s.color.r, green: s.color.g, blue: s.color.b, alpha: s.highlighter ? min(s.color.a, 0.35) : s.color.a)
            let inkType: PKInk.InkType = s.highlighter ? .marker : .pen
            let ink = PKInk(inkType, color: color)
            for path in s.paths {
                var t: TimeInterval = 0
                let points = path.map { p -> PKStrokePoint in
                    defer { t += 0.008 }
                    // GoodNotes keeps a pen's half-width (its nib's radius). PencilKit draws nothing at all for a stroke
                    // made in code narrower than about 2.4 (whatever validWidthRange says), so a fine pen comes in at 2.5.
                    let w = max(p.width * 2, 2.5)
                    return PKStrokePoint(location: p.point, timeOffset: t, size: CGSize(width: w, height: w),
                                         opacity: 1, force: 1, azimuth: 0, altitude: .pi / 2)
                }
                // A dot: one point, drawn as a short stroke so it shows.
                let all = points.count == 1 ? [points[0], PKStrokePoint(location: CGPoint(x: points[0].location.x + 0.3, y: points[0].location.y),
                                                                           timeOffset: 0.01, size: points[0].size, opacity: 1, force: 1, azimuth: 0, altitude: .pi / 2)] : points
                out.append(PKStroke(ink: ink, path: PKStrokePath(controlPoints: all, creationDate: .now)))
            }
        }
        return PKDrawing(strokes: out)
    }
}

/// Paper as it's printed, in a page's own units (points): fine, quiet ruling, as on a good pad. Used for the page
/// being written on, its picture in the pages view and the template picker, and the PDF sent to the library, so all
/// four look the same. Thread-safe: the page being written on is drawn in tiles off the main thread.
enum PaperDrawing {
    /// The paper's colours: its ground, the ruling, the stronger lines (a header, Cornell's sections), the margin
    /// line, and the dots.
    struct Inks: Sendable {
        var ground: UIColor
        var rule: UIColor
        var strong: UIColor
        var margin: UIColor
        var dot: UIColor
        var label: UIColor
    }

    static func inks(_ colour: Notebook.PaperColour) -> Inks {
        switch colour {
        case .white:
            Inks(ground: UIColor(hex: "#FFFFFF"), rule: UIColor(hex: "#CBD5E0"), strong: UIColor(hex: "#AEBBCB"),
                 margin: UIColor(hex: "#EBB5B5"), dot: UIColor(hex: "#B4BDC8"), label: UIColor(hex: "#9AA4B1"))
        case .cream:
            Inks(ground: UIColor(hex: "#FBF6EA"), rule: UIColor(hex: "#DCD1BC"), strong: UIColor(hex: "#C7B99F"),
                 margin: UIColor(hex: "#E6B3A6"), dot: UIColor(hex: "#C8BBA2"), label: UIColor(hex: "#AA9C83"))
        case .dark:
            Inks(ground: UIColor(hex: "#242629"), rule: UIColor(hex: "#3B3F45"), strong: UIColor(hex: "#50555C"),
                 margin: UIColor(hex: "#6B4A4C"), dot: UIColor(hex: "#53585F"), label: UIColor(hex: "#6E747C"))
        }
    }

    /// A paper: its name in the picker, how far apart its lines are (0 for none), and how it's printed on a page.
    struct Style {
        var name: String
        var spacing: CGFloat
        var draw: (Sheet) -> Void
    }

    /// Every paper, one entry each. A new paper is a case in `Notebook.Paper` and its entry here (the compiler asks
    /// for it); the picker, the page, the pages view and the PDF all draw it from this.
    static func style(_ paper: Notebook.Paper) -> Style {
        switch paper {
        case .plain: Style(name: "Blank", spacing: 0) { _ in }
        case .narrow: Style(name: "Narrow ruled", spacing: 20.25, draw: ruled) // 9/32 inch, college ruled
        case .lined: Style(name: "Wide ruled", spacing: 26, draw: ruled) // as notebooks from before had it
        case .grid: Style(name: "Grid", spacing: 18, draw: grid) // a quarter inch
        case .dotted: Style(name: "Dotted", spacing: 18, draw: dotted)
        case .cornell: Style(name: "Cornell notes", spacing: 20.25, draw: cornell)
        }
    }

    /// Where the ruling starts, and how far apart a paper's lines are.
    static let top: CGFloat = 72
    static let margin: CGFloat = 60
    static func spacing(_ paper: Notebook.Paper) -> CGFloat { style(paper).spacing }

    /// Cornell notes: the cue column's width, and the summary box's height at the foot.
    static let cueWidth: CGFloat = 180
    static let summaryHeight: CGFloat = 144

    /// A page being printed, for a paper's drawing: where, its colours, its line spacing, and the thinnest a line may
    /// be in page units (a picture drawn small keeps its lines visible).
    struct Sheet {
        var rect: CGRect
        var ctx: CGContext
        var ink: Inks
        var step: CGFloat
        var hairline: CGFloat
        var fine: CGFloat { max(0.5, hairline) }

        func hline(_ y: CGFloat, from x0: CGFloat? = nil, to x1: CGFloat? = nil) {
            ctx.move(to: CGPoint(x: x0 ?? rect.minX, y: y)); ctx.addLine(to: CGPoint(x: x1 ?? rect.maxX, y: y))
        }
        func vline(_ x: CGFloat, from y0: CGFloat? = nil, to y1: CGFloat? = nil) {
            ctx.move(to: CGPoint(x: x, y: y0 ?? rect.minY)); ctx.addLine(to: CGPoint(x: x, y: y1 ?? rect.maxY))
        }
    }

    /// `hairline` is the thinnest a line may be, in page units (a picture drawn small keeps its lines visible).
    static func draw(_ paper: Notebook.Paper, colour: Notebook.PaperColour, in rect: CGRect, ctx: CGContext, hairline: CGFloat = 0) {
        let ink = inks(colour)
        let style = style(paper)
        ctx.saveGState()
        ctx.setFillColor(ink.ground.cgColor)
        ctx.fill(rect)
        style.draw(Sheet(rect: rect, ctx: ctx, ink: ink, step: style.spacing, hairline: hairline))
        ctx.restoreGState()
    }

    /// Narrow and wide ruled: the header line, then the ruling to half an inch from the foot, and the margin.
    private static func ruled(_ s: Sheet) {
        let (rect, ctx, ink, step, hairline) = (s.rect, s.ctx, s.ink, s.step, s.hairline)
        ctx.setStrokeColor(ink.rule.cgColor)
        ctx.setLineWidth(s.fine)
        var y = rect.minY + top + step
        while y < rect.maxY - 36 { s.hline(y); y += step }
        ctx.strokePath()
        ctx.setStrokeColor(ink.strong.cgColor)
        ctx.setLineWidth(max(0.75, hairline))
        s.hline(rect.minY + top)
        ctx.strokePath()
        ctx.setStrokeColor(ink.margin.cgColor)
        ctx.setLineWidth(max(0.6, hairline))
        s.vline(rect.minX + margin)
        ctx.strokePath()
    }

    /// Square to the edges, centred so the page has the same part-square at each side.
    private static func grid(_ s: Sheet) {
        let (rect, ctx, ink, step, hairline) = (s.rect, s.ctx, s.ink, s.step, s.hairline)
        ctx.setStrokeColor(ink.rule.cgColor)
        ctx.setLineWidth(max(0.4, hairline))
        let ox = rect.width.truncatingRemainder(dividingBy: step) / 2, oy = rect.height.truncatingRemainder(dividingBy: step) / 2
        var x = rect.minX + ox + (ox < 1 ? step : 0)
        while x < rect.maxX - 0.5 { s.vline(x); x += step }
        var y = rect.minY + oy + (oy < 1 ? step : 0)
        while y < rect.maxY - 0.5 { s.hline(y); y += step }
        ctx.strokePath()
    }

    /// Dots where a grid's lines would cross.
    private static func dotted(_ s: Sheet) {
        let (rect, ctx, ink, step, hairline) = (s.rect, s.ctx, s.ink, s.step, s.hairline)
        ctx.setFillColor(ink.dot.cgColor)
        let r = max(0.85, hairline * 0.9)
        let ox = rect.width.truncatingRemainder(dividingBy: step) / 2, oy = rect.height.truncatingRemainder(dividingBy: step) / 2
        var y = rect.minY + oy + (oy < 1 ? step : 0)
        while y < rect.maxY - 0.5 {
            var x = rect.minX + ox + (ox < 1 ? step : 0)
            while x < rect.maxX - 0.5 { ctx.fillEllipse(in: CGRect(x: x - r, y: y - r, width: 2 * r, height: 2 * r)); x += step }
            y += step
        }
    }

    /// Cornell notes: a title band, the cue column and the notes ruled across, and the summary at the foot.
    private static func cornell(_ s: Sheet) {
        let (rect, ctx, ink, step, hairline) = (s.rect, s.ctx, s.ink, s.step, s.hairline)
        let summaryTop = rect.maxY - summaryHeight
        ctx.setStrokeColor(ink.rule.cgColor)
        ctx.setLineWidth(s.fine)
        var y = rect.minY + top + step
        while y < summaryTop - 4 { s.hline(y); y += step }
        y = summaryTop + step + 6
        while y < rect.maxY - 24 { s.hline(y, from: rect.minX + 18, to: rect.maxX - 18); y += step }
        ctx.strokePath()
        ctx.setStrokeColor(ink.strong.cgColor)
        ctx.setLineWidth(max(0.8, hairline))
        s.hline(rect.minY + top)
        s.hline(summaryTop)
        s.vline(rect.minX + cueWidth, from: rect.minY + top, to: summaryTop)
        ctx.strokePath()
        if hairline < 0.6 {
            label("Cues", at: CGPoint(x: rect.minX + 18, y: rect.minY + top - 6), ink: ink.label, ctx: ctx)
            label("Notes", at: CGPoint(x: rect.minX + cueWidth + 14, y: rect.minY + top - 6), ink: ink.label, ctx: ctx)
            label("Summary", at: CGPoint(x: rect.minX + 18, y: summaryTop + 15), ink: ink.label, ctx: ctx)
        }
    }

    /// A PDF's page filling `rect` (y down), as a viewer shows it: its crop box, turned as the PDF says.
    static func drawPDF(_ page: CGPDFPage, in rect: CGRect, ctx: CGContext) {
        let box = page.getBoxRect(.cropBox)
        let angle = ((Int(page.rotationAngle) % 360) + 360) % 360
        let upright = angle % 180 == 0 ? box.size : CGSize(width: box.height, height: box.width)
        guard upright.width > 0, upright.height > 0 else { return }
        ctx.saveGState()
        ctx.clip(to: rect)
        // The PDF's own space is y up.
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: rect.width / upright.width, y: -rect.height / upright.height)
        switch angle {
        case 90: ctx.translateBy(x: 0, y: upright.height); ctx.rotate(by: -.pi / 2)
        case 180: ctx.translateBy(x: upright.width, y: upright.height); ctx.rotate(by: .pi)
        case 270: ctx.translateBy(x: upright.width, y: 0); ctx.rotate(by: .pi / 2)
        default: break
        }
        ctx.translateBy(x: -box.minX, y: -box.minY)
        ctx.drawPDFPage(page)
        ctx.restoreGState()
    }

    /// A small printed word on the paper, as shapes rather than text (a PDF of the notebook keeps only the student's
    /// words for the library to read). `at` is its baseline's start.
    private static func label(_ word: String, at p: CGPoint, ink: UIColor, ctx: CGContext) {
        guard let path = labelPaths[word] else { return }
        ctx.saveGState()
        ctx.translateBy(x: p.x, y: p.y)
        ctx.scaleBy(x: 1, y: -1)
        ctx.addPath(path)
        ctx.setFillColor(ink.cgColor)
        ctx.fillPath()
        ctx.restoreGState()
    }

    // Made once, never changed: safe to read from the tiles' threads.
    nonisolated(unsafe) private static let labelPaths: [String: CGPath] = {
        let font = CTFontCreateWithName((UIFont(name: Atkinson.face(.semibold), size: 8)?.fontName ?? "Helvetica") as CFString, 8, nil)
        var out: [String: CGPath] = [:]
        for word in ["Cues", "Notes", "Summary"] { out[word] = GlyphPath.path(word, font: font) }
        return out
    }()
}

/// Words as one shape, from their font's glyph outlines: baseline at y 0, y going up (the font's way).
enum GlyphPath {
    static func path(_ text: String, font: CTFont) -> CGPath {
        let attributed = NSAttributedString(string: text, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
        let line = CTLineCreateWithAttributedString(attributed)
        let out = CGMutablePath()
        for run in (CTLineGetGlyphRuns(line) as? [CTRun]) ?? [] {
            let attributes = CTRunGetAttributes(run) as NSDictionary
            let runFont = attributes[kCTFontAttributeName as String].map { $0 as! CTFont } ?? font
            let count = CTRunGetGlyphCount(run)
            var glyphs = [CGGlyph](repeating: 0, count: count)
            var positions = [CGPoint](repeating: .zero, count: count)
            CTRunGetGlyphs(run, CFRange(location: 0, length: count), &glyphs)
            CTRunGetPositions(run, CFRange(location: 0, length: count), &positions)
            for i in 0..<count {
                if let g = CTFontCreatePathForGlyph(runFont, glyphs[i], nil) {
                    out.addPath(g, transform: CGAffineTransform(translationX: positions[i].x, y: positions[i].y))
                }
            }
        }
        return out
    }
}
