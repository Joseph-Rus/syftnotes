import XCTest

/// Writing by hand: a new notebook from Attach, its paper chosen, then changed, and a page added, with real taps on
/// the demo library. The same on an iPhone (Paper and Add a page are in the bar's More) and an iPad (in the bar).
@MainActor
final class NotebookTests: XCTestCase {
    override func setUp() async throws {
        continueAfterFailure = false
    }

    func testNewNotebookThenItsPaperChangedAndAPageAdded() {
        let app = XCUIApplication()
        app.launchArguments = ["-demo", "-quiet", "-fresh-notebooks"]
        app.launch()
        tap(app.buttons["Attach"], "Home's Attach")
        let write = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Write notes")).firstMatch
        // An iPad opens with its sidebar over Home: the first tap only puts it away.
        if !write.waitForExistence(timeout: 3) { app.buttons["Attach"].tap() }
        tap(write, "Attach's Write notes")

        // Its paper is chosen first, shown as small pages.
        expect(page(app, "newNotebook"), "New notebook")
        tap(app.buttons["paper.grid"], "Grid paper")
        XCTAssertTrue(app.buttons["paper.grid"].isSelected, "Grid chosen")
        tap(app.buttons["notebook.create"], "Create notebook")
        expect(page(app, "notebook"), "the notebook, open over the whole screen")
        expect(text(app, containing: "page 1 of 1"), "page 1 of 1 under the title")

        // This page on Cornell notes.
        if app.buttons["notebook.paper"].waitForExistence(timeout: 2) {
            app.buttons["notebook.paper"].tap()
        } else {
            tap(app.buttons["notebook.more"], "the bar's More")
            XCTAssertFalse(app.keyboards.firstMatch.waitForExistence(timeout: 1), "no keyboard over the notebook")
            tap(app.buttons["Paper"], "More's Paper")
        }
        expect(page(app, "paper"), "the paper picker")
        tap(app.buttons["paper.cornell"], "Cornell notes")
        tap(app.buttons["paper.apply.page"], "Apply to page 1")
        XCTAssertTrue(page(app, "paper").waitForNonExistence(timeout: 4), "the paper picker closes")

        // A page after it, on the same paper.
        if app.buttons["notebook.addPage"].waitForExistence(timeout: 2) {
            app.buttons["notebook.addPage"].tap()
        } else {
            tap(app.buttons["notebook.more"], "the bar's More")
            tap(app.buttons["Add a page after this one"], "More's Add a page after this one")
        }
        expect(text(app, containing: "page 2 of 2"), "page 2 of 2 under the title")

        // A line written with a finger (there's no Pencil here), undone, and done again.
        let undo = app.buttons["notebook.undo"], redo = app.buttons["notebook.redo"]
        expect(undo, "Undo")
        XCTAssertFalse(undo.isEnabled, "nothing to undo yet")
        let desk = page(app, "notebook")
        desk.coordinate(withNormalizedOffset: CGVector(dx: 0.3, dy: 0.42))
            .press(forDuration: 0.1, thenDragTo: desk.coordinate(withNormalizedOffset: CGVector(dx: 0.7, dy: 0.47)))
        XCTAssertTrue(waitUntil { undo.isEnabled }, "a line written can be undone")
        undo.tap()
        XCTAssertTrue(waitUntil { !undo.isEnabled && redo.isEnabled }, "undone, it can be done again")
        redo.tap()
        XCTAssertTrue(waitUntil { undo.isEnabled }, "done again")

        // The pages, each on Cornell notes.
        XCTAssertFalse(app.keyboards.firstMatch.exists, "no keyboard over the notebook")
        tap(app.buttons["notebook.pages"], "Pages")
        expect(element(app, "Page 1, Cornell notes"), "page 1 on Cornell notes in the pages")
        expect(element(app, "Page 2, Cornell notes, showing"), "page 2, showing, on Cornell notes")
        // A phone's pages are a sheet of their own: closed first.
        if app.navigationBars["Pages"].exists {
            app.navigationBars["Pages"].buttons["Done"].tap()
            XCTAssertTrue(app.navigationBars["Pages"].waitForNonExistence(timeout: 4), "the pages close")
        }

        // Done saves it to its class in the library ("Saved to Calculus II." for a moment), and Home's shelf says so.
        tap(app.buttons["Done"].firstMatch, "the notebook's Done")
        expect(page(app, "home"), "Home again", timeout: 12)
        expect(app.buttons["Calculus II notes, \(Self.today), in Calculus II"], "the notebook on Home's shelf, in Calculus II")
    }

    /// An iPad's Write alongside: the notebook beside the lecture's notes, with Ask and Study under the notes rather than
    /// across it. When the window narrows to a phone's width (Stage Manager, a window made small) the notebook steps
    /// aside, and what was written in it is saved first: Home's shelf has it in the lecture's class.
    func testIPadNotebookBesideStepsAsideSavedWhenTheWindowNarrows() throws {
        try XCTSkipUnless(UIDevice.current.userInterfaceIdiom == .pad, "Write alongside is an iPad's")
        let app = XCUIApplication()
        app.launchArguments = ["-demo", "-quiet", "-fresh-notebooks", "-tab-bar", "-open", "lecture:demo-calc-ibp", "-ink", "1"]
        app.launch()
        fillScreen(app)
        addTeardownBlock { @MainActor in self.fillScreen(app) }

        let paper = page(app, "newNotebook")
        expect(paper, "a new notebook beside the notes")
        for name in ["Ask about this lecture", "Study"] {
            expect(app.buttons[name], name)
            XCTAssertLessThanOrEqual(app.buttons[name].frame.maxX, paper.frame.minX, "\(name) under the notes, clear of the notebook")
        }
        tap(app.buttons["notebook.create"], "Create notebook")
        let desk = page(app, "notebook")
        expect(desk, "the notebook beside the notes")
        // A line written with a finger (the row over it has Undo, but unlabelled, so the shelf below is the proof).
        desk.coordinate(withNormalizedOffset: CGVector(dx: 0.3, dy: 0.42))
            .press(forDuration: 0.1, thenDragTo: desk.coordinate(withNormalizedOffset: CGVector(dx: 0.7, dy: 0.47)))

        // The window's corner dragged in: as narrow as a window goes, a phone's width.
        let window = app.windows.firstMatch.frame
        let origin = app.windows.firstMatch.coordinate(withNormalizedOffset: .zero)
        origin.withOffset(CGVector(dx: window.maxX - 6, dy: window.maxY - 6))
            .press(forDuration: 0.6, thenDragTo: origin.withOffset(CGVector(dx: window.minX + 100, dy: window.maxY * 0.85)))
        XCTAssertTrue(waitUntil { app.windows.firstMatch.frame.width < 500 }, "the window narrowed")
        XCTAssertTrue(desk.waitForNonExistence(timeout: 5), "the notebook steps aside in a phone-wide window")
        expect(page(app, "lecture"), "the notes, the whole window")

        // Saved as it went: on Home's shelf, in the lecture's class.
        fillScreen(app)
        tap(app.navigationBars.buttons["BackButton"], "Back")
        expect(page(app, "home"), "Home")
        expect(app.buttons["Notes, Integration by Parts and the LIATE Rule, in Calculus II"], "the notebook saved, on Home's shelf", timeout: 15)
    }

    /// The window made the whole screen again, with its own controls (Zoom held, then Fill), when it isn't.
    private func fillScreen(_ app: XCUIApplication) {
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        let screen = springboard.frame.width
        guard app.windows.firstMatch.frame.width < screen else { return }
        let controls = springboard.buttons["window-controls:app.studystash.ios"]
        guard controls.waitForExistence(timeout: 5) else { return }
        controls.tap()
        let zoom = springboard.buttons["Zoom-button"]
        guard zoom.waitForExistence(timeout: 3) else { return }
        zoom.press(forDuration: 1.2)
        let fill = springboard.buttons["Fill"]
        if fill.waitForExistence(timeout: 3) { fill.tap() }
        _ = waitUntil { app.windows.firstMatch.frame.width >= screen }
    }

    private func waitUntil(_ timeout: Double = 5, _ ok: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(timeout)
        while Date() < end {
            if ok() { return true }
            RunLoop.current.run(until: Date().addingTimeInterval(0.2))
        }
        return ok()
    }

    // MARK: helpers

    /// "Fri 16 Oct", as a new notebook's title has it.
    private static var today: String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_GB")
        f.dateFormat = "EEE d MMM"
        return f.string(from: .now)
    }

    private func page(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: "page.\(name)").firstMatch
    }

    private func text(_ app: XCUIApplication, containing words: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS[c] %@", words)).firstMatch
    }

    private func element(_ app: XCUIApplication, _ label: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    private func expect(_ e: XCUIElement, _ what: String, timeout: Double = 8, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(e.waitForExistence(timeout: timeout), "Didn't reach \(what)", file: file, line: line)
    }

    private func tap(_ e: XCUIElement, _ what: String, timeout: Double = 8, file: StaticString = #filePath, line: UInt = #line) {
        expect(e, what, timeout: timeout, file: file, line: line)
        e.tap()
    }
}
