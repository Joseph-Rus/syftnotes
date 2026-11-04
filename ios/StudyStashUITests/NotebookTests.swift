import XCTest

/// Writing by hand: a new notebook from Attach, its paper chosen, then changed, and pages added, with real taps on
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

        // A page added at the end: the line can still be undone (and is done again, so the notebook isn't empty).
        if app.buttons["notebook.addPage"].exists {
            app.buttons["notebook.addPage"].tap()
        } else {
            tap(app.buttons["notebook.more"], "the bar's More")
            tap(app.buttons["Add a page after this one"], "More's Add a page after this one")
        }
        expect(text(app, containing: "page 3 of 3"), "page 3 of 3 under the title")
        XCTAssertTrue(undo.isEnabled, "the line can still be undone after a page is added")
        undo.tap()
        XCTAssertTrue(waitUntil { !undo.isEnabled && redo.isEnabled }, "the line undone after a page was added")
        redo.tap()
        XCTAssertTrue(waitUntil { undo.isEnabled }, "done again")

        // The pages, each on Cornell notes.
        XCTAssertFalse(app.keyboards.firstMatch.exists, "no keyboard over the notebook")
        tap(app.buttons["notebook.pages"], "Pages")
        expect(element(app, "Page 1, Cornell notes"), "page 1 on Cornell notes in the pages")
        expect(element(app, "Page 3, Cornell notes, showing"), "page 3, showing, on Cornell notes")
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

    /// Apple's pens stay while a menu is open over the page (the lasso's), so a selection can be recoloured.
    func testPensStayOverTheLassosMenu() {
        let app = XCUIApplication()
        app.launchArguments = ["-demo", "-quiet", "-show", "notebook"]
        app.launch()
        let desk = page(app, "notebook")
        expect(desk, "the sample notebook")
        tap(app.buttons["Lasso"], "the pens' lasso")
        desk.coordinate(withNormalizedOffset: CGVector(dx: 0.12, dy: 0.12))
            .press(forDuration: 0.05, thenDragTo: desk.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.14)))
        desk.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.14))
            .press(forDuration: 0.05, thenDragTo: desk.coordinate(withNormalizedOffset: CGVector(dx: 0.88, dy: 0.3)))
        // A tap on the page opens its menu (tapped again if the first only ends the lasso).
        for _ in 0..<3 where !app.menuItems.firstMatch.waitForExistence(timeout: 2) {
            desk.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.2)).tap()
        }
        expect(app.menuItems.firstMatch, "the menu over the page")
        XCTAssertTrue(app.buttons["Lasso"].exists, "the pens still there with the menu open")
        XCTAssertFalse(app.keyboards.firstMatch.exists, "no keyboard with the menu")
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
