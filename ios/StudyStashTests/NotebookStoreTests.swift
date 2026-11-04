import Foundation
import PDFKit
import PencilKit
import Testing
import UIKit
@testable import StudyStash

/// Notebooks kept on a phone: one saved by an earlier version still opens, and a PDF's pages come in to write on.
@MainActor
@Suite struct NotebookStoreTests {
    /// A notebook.json as the version before paper colours and templates wrote it.
    @Test func aNotebookFromBeforeStillOpens() throws {
        let json = """
        {"id":"6F1C2A44-1B0B-4A43-9E57-2C0B6A6D1E01","title":"Calculus II notes, 14 Oct","className":"Calculus II",
         "pages":[{"id":"0D0E8B5E-4C55-4E4B-8C3E-9F6A3E3B2A11","width":612,"height":792,"paper":"lined","pdfPage":1},
                  {"id":"0D0E8B5E-4C55-4E4B-8C3E-9F6A3E3B2A12","width":612,"height":792,"paper":"wavy","pdfPage":1}],
         "created":"2026-10-14T09:30:00Z","updated":"2026-10-14T10:20:00Z"}
        """
        let n = try JSONDecoder.iso.decode(Notebook.self, from: Data(json.utf8))
        #expect(n.pages.count == 2)
        #expect(n.pages[0].paper == .lined)
        #expect(n.pages[0].colour == .white)
        // A paper this version doesn't know is blank, rather than the notebook not opening.
        #expect(n.pages[1].paper == .plain)
        #expect(n.template == nil)
        // New pages come on its last paper page's paper.
        #expect(n.newPages == Notebook.Template(paper: .plain, colour: .white, size: .letter))
        // Saved again, it reads back the same.
        let again = try JSONDecoder.iso.decode(Notebook.self, from: JSONEncoder.iso.encode(n))
        #expect(again == n)
    }

    /// What's written is on disk moments after each stroke, with the notebook still open (the app can be stopped any
    /// time), and at once when the app goes to the background; the notebooks opened afresh have it, on its own page.
    @Test func strokesWrittenAreKeptWithoutClosing() async throws {
        let folder = FileManager.default.temporaryDirectory.appending(path: "notebooks-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let store = NotebookStore(folder: folder)
        var n = Notebook.blank(title: "Week 6", className: nil, lecture: nil)
        n.pages.append(Notebook.Page(.standard))
        store.save(n)
        let canvas = NotebookCanvas(store: store)
        canvas.attach(n.id)
        func write(page: Int, y: CGFloat) {
            let top = canvas.frames[page].minY
            let points = (0..<20).map { i in
                PKStrokePoint(location: CGPoint(x: 100 + CGFloat(i) * 4, y: top + y), timeOffset: Double(i) * 0.01,
                              size: CGSize(width: 3, height: 3), opacity: 1, force: 1, azimuth: 0, altitude: .pi / 2)
            }
            var d = canvas.canvas.drawing
            d.strokes.append(PKStroke(ink: PKInk(.pen, color: .black), path: PKStrokePath(controlPoints: points, creationDate: .now)))
            canvas.canvas.drawing = d
            // As PencilKit tells it when a stroke ends.
            canvas.canvasViewDrawingDidChange(canvas.canvas)
        }
        func onDisk(_ page: Int) -> PKDrawing {
            let fresh = NotebookStore(folder: folder)
            guard let m = fresh.notebook(n.id) else { return PKDrawing() }
            return fresh.drawing(m, m.pages[page])
        }

        // A line on page 2, the notebook left open: saved soon after, by itself.
        write(page: 1, y: 200)
        let deadline = ContinuousClock.now + .seconds(5)
        while onDisk(1).strokes.isEmpty, ContinuousClock.now < deadline { try await Task.sleep(for: .milliseconds(50)) }
        #expect(onDisk(1).strokes.count == 1)
        // In the page's own units, not the canvas's.
        #expect(abs(onDisk(1).bounds.midY - 200) < 4)

        // A line on page 1, then the app goes to the background straight away (what the editor does then).
        write(page: 0, y: 120)
        canvas.flush()
        #expect(onDisk(0).strokes.count == 1)
        #expect(onDisk(1).strokes.count == 1)
    }

    @Test func aPDFsPagesComeInAtTheirOwnSizes() throws {
        let folder = FileManager.default.temporaryDirectory.appending(path: "notebooks-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let store = NotebookStore(folder: folder)
        var n = Notebook.blank(title: "Week 6", className: nil, lecture: nil, template: .init(paper: .grid, colour: .cream, size: .a4))
        store.save(n)
        // A slide, then a handout page.
        let url = folder.appending(path: "Week 6 slides.pdf")
        var first = CGRect(x: 0, y: 0, width: 960, height: 540)
        let pdfContext = try #require(CGContext(url as CFURL, mediaBox: &first, nil))
        for size in [CGSize(width: 960, height: 540), CGSize(width: 612, height: 792)] {
            var box = CGRect(origin: .zero, size: size)
            pdfContext.beginPDFPage([kCGPDFContextMediaBox as String: Data(bytes: &box, count: MemoryLayout<CGRect>.size)] as CFDictionary)
            pdfContext.fill(CGRect(x: 40, y: 40, width: 100, height: 20))
            pdfContext.endPDFPage()
        }
        pdfContext.closePDF()
        let pages = try #require(store.pages(fromPDF: url, for: n))
        #expect(pages.map(\.size) == [CGSize(width: 960, height: 540), CGSize(width: 612, height: 792)])
        #expect(pages.allSatisfy { $0.pdf != nil })
        n.pages += pages
        store.save(n)
        // The notebook as a PDF: a page each, the first on A4.
        let pdf = try #require(PDFDocument(data: store.pdfData(n)))
        #expect(pdf.pageCount == 3)
        #expect(abs((pdf.page(at: 0)?.bounds(for: .cropBox).width ?? 0) - 595.28) < 1)
        #expect(pdf.page(at: 1)?.bounds(for: .cropBox).size == CGSize(width: 960, height: 540))
        #expect(pdf.page(at: 2)?.bounds(for: .cropBox).size == CGSize(width: 612, height: 792))
        // Pages added later still come on the notebook's paper, not the PDF's.
        #expect(n.newPages.paper == .grid && n.newPages.size == .a4)
    }
}
