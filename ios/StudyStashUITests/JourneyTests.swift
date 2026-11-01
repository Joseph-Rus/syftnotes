import XCTest

/// The journeys a student takes (iphone-look-refs/FLOWS.md, "First run" and "The core journeys"), walked with real
/// taps on the demo library, each checked to land on the right page. Pages carry `page.<name>` identifiers.
/// `-fake-mic` lets the recorder run in the simulator (made-up lines stand in for the microphone).
@MainActor
final class JourneyTests: XCTestCase {
    override func setUp() async throws {
        continueAfterFailure = false
    }

    // MARK: first run

    func testSampleClassesOpenHome() {
        let app = XCUIApplication()
        app.launchArguments = ["-unpaired", "-quiet"]
        app.launch()
        // Google first, the laptop's code second; the sample classes are under Other ways.
        expect(app.buttons["Continue with Google"], "setup's Continue with Google")
        tap(app.buttons["Other ways"], "setup's Other ways")
        tap(app.buttons["Try it with sample classes"], "setup's Try it with sample classes")
        expect(page(app, "home"), "Home after Try it with sample classes")
        expect(app.staticTexts["These are sample classes."], "the sample classes banner on Home")
        // A phone's tabs are Home, Due, Ask and Search: the classes are an iPad's sidebar, never a More tab.
        XCTAssertEqual(app.tabBars.buttons.count, 4, "four tabs")
        XCTAssertFalse(app.tabBars.buttons["More"].exists, "no More tab")
        // Connect your computer ends the sample classes on setup's first screen, Continue with Google first.
        tap(app.buttons["Connect your computer"], "the banner's Connect your computer")
        expect(app.buttons["Continue with Google"], "setup's Continue with Google")
    }

    /// A QR code that brings its 6 digits pairs without typing them; one whose code is refused opens the code page with
    /// the library's words. (`-pair-scan` goes through setup as if it had been scanned; the demo pairs only 123456.)
    func testScannedCodePairsWithoutTyping() {
        let app = XCUIApplication()
        app.launchArguments = ["-unpaired", "-quiet", "-pair-scan", "https://demo.studystash.app/app/#code=123456"]
        app.launch()
        expect(app.staticTexts["You're in"], "You're in, straight from the scan", timeout: 15)
        XCTAssertFalse(app.staticTexts["Found Demo library"].exists, "no code page")
        tap(app.buttons["Go to Home"], "Go to Home")
        expect(page(app, "home"), "Home after pairing")

        app.terminate()
        app.launchArguments = ["-unpaired", "-quiet", "-pair-scan", "https://demo.studystash.app/app/#code=000000"]
        app.launch()
        expect(app.staticTexts["Found Demo library"], "the code page, for a refused code", timeout: 15)
        expect(app.staticTexts["That code isn't right, or it's more than ten minutes old. Make a new one in Syft Notes' Settings."],
               "the library's words")
    }

    // MARK: recording

    func testUpNextRecordsIntoItsClassAndStopOpensTheLecture() {
        let app = launch()
        expect(page(app, "home"), "Home")
        tap(button(app, prefix: "Record Calculus II as a"), "Calculus II in Up next")
        allowMicrophone()
        expect(app.buttons["Stop recording"], "the recorder, recording", timeout: 15)
        expect(app.buttons["In Calculus II. Change class"], "the recording filed in Calculus II")
        sleep(2)
        app.buttons["Stop recording"].tap()
        expect(page(app, "lecture"), "the lecture after Stop", timeout: 20)
        expect(app.buttons["Calculus II. Open the class"], "the new lecture in Calculus II")
    }

    // MARK: reading, studying, asking about a lecture

    func testNoteOpensTheLectureThenStudyThenAskAboutIt() {
        let app = launch()
        let note = button(app, containing: "Integration by Parts and the LIATE Rule")
        reveal(note, in: page(app, "home"))
        note.tap()
        expect(page(app, "lecture"), "the lecture from its note row")

        tap(app.buttons["Study"], "Study")
        expect(page(app, "study"), "Study")
        tap(app.buttons["Done"], "Study's Done")
        expect(page(app, "lecture"), "the lecture again after Study")

        tap(app.buttons["Ask about this lecture"], "Ask about this lecture")
        expect(app.navigationBars["Ask about this lecture"], "the Ask sheet, titled for this lecture")
        ask(app, "How do I choose u?")
        tap(app.buttons["ask.source"].firstMatch, "the answer's source", timeout: 15)
        expect(page(app, "lecture"), "the lecture at the moment")
        XCTAssertFalse(app.navigationBars["Ask about this lecture"].exists, "the Ask sheet closed")
        expectMoment(app)
        // The same lecture moved to that moment, not a second copy of it pushed on top: Back is Home.
        goBack(app)
        expect(page(app, "home"), "Home after Back")
    }

    // MARK: due

    func testAssignmentOpensItsLecturesAndItsClass() {
        let app = launch()
        tap(app.tabBars.buttons["Due"], "the Due tab")
        expect(page(app, "due"), "Due")
        let quiz = button(app, prefix: "Quiz 4: Memory")
        reveal(quiz, in: page(app, "due"))
        quiz.tap()
        expect(page(app, "assignment"), "the assignment")

        let lecture = button(app, containing: "Memory: Encoding, Storage and Retrieval")
        reveal(lecture, in: page(app, "assignment"))
        lecture.tap()
        expect(page(app, "lecture"), "a lecture from From your lectures")
        expect(app.buttons["Intro to Psychology. Open the class"], "the lecture in the assignment's class")
        goBack(app)

        expect(page(app, "assignment"), "the assignment again")
        let cls = app.buttons["Intro to Psychology. Open the class"]
        reveal(cls, in: page(app, "assignment"), up: true)
        cls.tap()
        expect(page(app, "class"), "the assignment's class")
        expect(app.buttons["Record Intro to Psychology"], "the Record bar recording into the class")
    }

    // MARK: Ask

    func testAskSourceOpensTheLectureAtThatMinuteOnItsTranscript() {
        let app = launch()
        tap(app.tabBars.buttons["Ask"], "the Ask tab")
        XCTAssertFalse(app.buttons["Record Calculus II"].exists, "no Record bar on Ask")
        ask(app, "How do I choose u in integration by parts?")
        tap(app.buttons["ask.source"].firstMatch, "the answer's source", timeout: 15)
        expect(page(app, "lecture"), "the lecture the answer came from")
        expectMoment(app)
    }

    // MARK: Search

    func testSearchResultOpensTheLecture() {
        let app = launch()
        tap(app.tabBars.buttons["Search"], "the Search tab")
        let field = app.searchFields.firstMatch
        tap(field, "the search field")
        field.typeText("integration")
        tap(button(app, containing: "Integration by Parts and the LIATE Rule"), "a lecture found", timeout: 10)
        expect(page(app, "lecture"), "the lecture from Search")
    }

    // MARK: Settings

    func testSettingsFromHomeAndBack() {
        let app = launch()
        expect(page(app, "home"), "Home")
        tap(app.navigationBars.buttons["Settings"], "Settings on Home's toolbar")
        expect(page(app, "settings"), "Settings")
        goBack(app)
        expect(page(app, "home"), "Home after Back")
        XCTAssertFalse(page(app, "settings").exists, "Settings closed")
    }

    // MARK: a class

    func testClassAttachOpensTheOneChooser() {
        let app = launch()
        let row = button(app, prefix: "Organic Chemistry,")
        reveal(row, in: page(app, "home"))
        row.tap()
        expect(page(app, "class"), "the class page")
        expect(app.buttons["Record Organic Chemistry"], "the Record bar recording into the class")
        tap(page(app, "class").buttons["Attach"], "the class's Attach")
        expect(button(app, prefix: "Scan pages"), "the Attach chooser")
        expect(button(app, prefix: "Import a recording"), "the Attach chooser's recording row")
    }

    // MARK: an iPad's sidebar

    func testIPadSidebarOpensAClass() throws {
        try XCTSkipUnless(UIDevice.current.userInterfaceIdiom == .pad, "The class sidebar is an iPad's")
        let app = launch()
        expect(page(app, "home"), "Home")
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label == %@ AND (elementType == %d OR elementType == %d)", "Organic Chemistry",
                                  XCUIElement.ElementType.cell.rawValue, XCUIElement.ElementType.button.rawValue)).firstMatch
        tap(row, "Organic Chemistry in the sidebar")
        expect(page(app, "class"), "the class's page from the sidebar")
        expect(app.buttons["Record Organic Chemistry"], "the Record bar recording into the class")
    }

    // MARK: links (widgets, the Live Activity, notifications)

    func testLinksOpenTheirPages() {
        let app = launch(["-tab", "search"])
        expect(app.searchFields.firstMatch, "Search")

        follow("studystash://class/Calculus%20II")
        expect(page(app, "class"), "a class's link")
        follow("studystash://lecture/demo-calc-ibp?at=220")
        expect(page(app, "lecture"), "a lecture's link")
        expectMoment(app)
        follow("studystash://assignment/Calculus%20II/1")
        expect(page(app, "assignment"), "an assignment's link")
        expect(app.staticTexts["Problem Set 6: Integration by Parts"], "the assignment the link names")
        XCTAssertTrue(app.tabBars.buttons["Due"].isSelected, "an assignment opens on Due")
        follow("studystash://due")
        expect(page(app, "due"), "Due's link")
        follow("studystash://ask")
        expect(app.descendants(matching: .any).matching(identifier: "ask.field").firstMatch, "Ask's link")
        follow("studystash://recording")
        expect(page(app, "home"), "the recording's link with nothing recording: Home")
        follow("studystash://search")
        expect(page(app, "home"), "a link the app doesn't know: Home")

        follow("studystash://record")
        allowMicrophone()
        expect(app.buttons["Stop recording"], "Record's link, recording", timeout: 15)
        expect(app.buttons["In Calculus II. Change class"], "Record's link recording the class on now")
        app.buttons["Hide. It keeps recording"].tap()
        expect(page(app, "home"), "Home under the recorder")
        follow("studystash://recording")
        expect(app.buttons["Stop recording"], "the recording's link while recording: the recorder")
        app.buttons["Stop recording"].tap()
        expect(page(app, "lecture"), "the lecture after Stop", timeout: 20)

        follow("studystash://record?class=Organic%20Chemistry")
        expect(app.buttons["In Organic Chemistry. Change class"], "Record's link recording into the class it names", timeout: 15)
        follow("studystash://home")
        expect(page(app, "home"), "Home's link")
        XCTAssertFalse(app.buttons["Stop recording"].exists, "Home's link put the recorder away")
    }

    // MARK: helpers

    /// Opens a link as a widget does, saying yes when the system asks whether to open it in the app.
    private func follow(_ link: String) {
        XCUIDevice.shared.system.open(URL(string: link)!)
        let open = XCUIApplication(bundleIdentifier: "com.apple.springboard").buttons["Open"]
        if open.waitForExistence(timeout: 3) { open.tap() }
    }

    private func launch(_ extra: [String] = []) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = ["-demo", "-quiet", "-fake-mic", "-ask-new", "-ask-agreed", "yes"] + extra
        app.launch()
        return app
    }

    private func page(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: "page.\(name)").firstMatch
    }

    private func button(_ app: XCUIApplication, prefix: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    private func button(_ app: XCUIApplication, containing words: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", words)).firstMatch
    }

    private func expect(_ e: XCUIElement, _ what: String, timeout: Double = 8, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(e.waitForExistence(timeout: timeout), "Didn't reach \(what)", file: file, line: line)
    }

    private func tap(_ e: XCUIElement, _ what: String, timeout: Double = 8, file: StaticString = #filePath, line: UInt = #line) {
        expect(e, what, timeout: timeout, file: file, line: line)
        e.tap()
    }

    /// Scrolls `scroll` a little at a time until `e` can be tapped clear of the bars over the page (the Record bar and
    /// the tabs float over its foot; a tap there would land on them).
    private func reveal(_ e: XCUIElement, in scroll: XCUIElement, up: Bool = false, file: StaticString = #filePath, line: UInt = #line) {
        expect(scroll, "the page", file: file, line: line)
        let height = scroll.frame.height
        func clear() -> Bool {
            e.exists && e.isHittable && e.frame.minY > 110 && e.frame.maxY < height - 160
        }
        for _ in 0..<14 where !clear() {
            let from = scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: up ? 0.35 : 0.65))
            from.press(forDuration: 0.05, thenDragTo: scroll.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: up ? 0.65 : 0.35)))
        }
        XCTAssertTrue(clear(), "Couldn't bring \(e) on screen", file: file, line: line)
    }

    /// Back, as the navigation bar has it.
    private func goBack(_ app: XCUIApplication) {
        let back = app.navigationBars.buttons["BackButton"]
        (back.exists ? back : app.navigationBars.buttons.element(boundBy: 0)).tap()
    }

    /// Types `question` in the Ask bar and sends it.
    private func ask(_ app: XCUIApplication, _ question: String) {
        let field = app.descendants(matching: .any).matching(identifier: "ask.field").firstMatch
        tap(field, "the Ask bar")
        field.typeText(question)
        tap(app.buttons["ask.send"], "Ask's send")
    }

    /// The lecture opened on its Transcript at the line the demo's answer used (3:40, "So how do you choose u?").
    private func expectMoment(_ app: XCUIApplication, file: StaticString = #filePath, line: UInt = #line) {
        let transcript = app.buttons["Transcript"]
        expect(transcript, "the lecture's Transcript tab", file: file, line: line)
        XCTAssertTrue(transcript.isSelected, "the lecture opened on its Transcript", file: file, line: line)
        let said = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", "how do you choose u")).firstMatch
        expect(said, "the line said at 3:40", file: file, line: line)
        XCTAssertTrue(said.isHittable, "the line said at 3:40 on screen", file: file, line: line)
    }

    /// The system's microphone question, when the simulator asks it.
    private func allowMicrophone() {
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        let allow = springboard.alerts.buttons["Allow"]
        if allow.waitForExistence(timeout: 4) { allow.tap() }
    }
}
