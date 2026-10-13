import XCTest

final class PanesAndSettingsTests: XCTestCase {
    override func setUp() {
        continueAfterFailure = false
    }

    /// Newest first: the fixture's two switches are newer than both its enrolments.
    @MainActor
    func testActivityListsChanges() {
        let app = XCUIApplication.launched(.oneTool)
        app.openWindow()
        app.descendants(matching: .any)["sidebar.activity"].click()
        let switched = app.text("Switch")
        let enrolled = app.text("Enrol")
        XCTAssertTrue(switched.waitForExistence(timeout: 5))
        XCTAssertTrue(enrolled.exists)
        XCTAssertLessThan(switched.frame.minY, enrolled.frame.minY, "newest first")
    }

    /// The one thing worth looking at in this fixture is personal's parked login, which
    /// lapses soon.
    @MainActor
    func testThisMacShowsTheChecks() {
        let app = XCUIApplication.launched(.oneTool)
        app.openWindow()
        app.descendants(matching: .any)["sidebar.machine"].click()
        XCTAssertTrue(app.staticTexts["account personal"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["One thing is worth looking at."].exists)
    }

    @MainActor
    func testDailyRenewal() {
        let app = XCUIApplication.launched(.oneTool)
        app.openSettings()
        app.toolbars.buttons["Renewal"].click()
        let renew = app.control("settings.renewDaily")
        XCTAssertTrue(renew.waitForExistence(timeout: 5))
        renew.click()
        XCTAssertTrue(app.text("Every day").waitForExistence(timeout: 5))
        app.buttons["Renew Now"].click()
        XCTAssertTrue(app.text("Renewed one.").waitForExistence(timeout: 5))
    }

    /// Opening at login turns on and off without registering the test build.
    @MainActor
    func testOpenAtLogin() {
        let app = XCUIApplication.launched(.oneTool)
        app.openSettings()
        let toggle = app.control("settings.openAtLogin")
        XCTAssertTrue(toggle.waitForExistence(timeout: 5))
        XCTAssertEqual(toggle.value as? Int, 0)
        toggle.click()
        XCTAssertEqual(toggle.value as? Int, 1)
        toggle.click()
        XCTAssertEqual(toggle.value as? Int, 0)
    }

    /// The model answers a change after the click, so each is waited for on the stepper,
    /// which follows the switch.
    @MainActor
    func testSwitchingBeforeAnAccountRunsOut() {
        let app = XCUIApplication.launched(.oneTool)
        app.openSettings()
        app.toolbars.buttons["Limits"].click()
        let toggle = app.control("settings.autoSwitch")
        let share = app.control("settings.autoSwitchAt")
        let standing = app.control("settings.autoSwitchStanding")
        XCTAssertTrue(toggle.waitForExistence(timeout: 5))
        XCTAssertTrue(share.exists)
        // Both are held back until the app's preferences have been read.
        XCTAssertTrue(toggle.wait(for: \.isEnabled, toEqual: true, timeout: 5))
        XCTAssertEqual(toggle.value as? Int, 0)
        XCTAssertFalse(share.isEnabled, "no share to change while it is off")
        XCTAssertFalse(standing.exists, "nothing said while it is off")

        toggle.click()
        XCTAssertTrue(share.wait(for: \.isEnabled, toEqual: true, timeout: 5))
        XCTAssertEqual(toggle.value as? Int, 1)
        XCTAssertTrue(says(app, "Switch when a limit reaches 95%").waitForExistence(timeout: 5))
        XCTAssertTrue(standing.waitForExistence(timeout: 5), "what the look came to")

        let raise = share.incrementArrows.firstMatch
        XCTAssertTrue(raise.exists, "a stepper shows its arrows")
        raise.click()
        XCTAssertTrue(says(app, "Switch when a limit reaches 96%").waitForExistence(timeout: 5))

        toggle.click()
        XCTAssertTrue(share.wait(for: \.isEnabled, toEqual: false, timeout: 5))
        XCTAssertEqual(toggle.value as? Int, 0)
        XCTAssertTrue(standing.waitForNonExistence(timeout: 5))
    }

    /// The fixture's personal is on for warm-ups with no five-hour period running, so it
    /// warms up as the app starts.
    @MainActor
    func testWarmingUpAccounts() {
        let app = XCUIApplication.launched(.warmUp)
        app.openWindow()
        let warmed = app.text("BEGINSWITH", "Warms up again at ")
        XCTAssertTrue(warmed.waitForExistence(timeout: 30), "personal is warmed up")

        app.openSettings()
        app.toolbars.buttons["Limits"].click()
        let personal = app.control("settings.warm.claude/personal")
        let work = app.control("settings.warm.claude/work")
        XCTAssertTrue(personal.waitForExistence(timeout: 5))
        XCTAssertTrue(personal.wait(for: \.isEnabled, toEqual: true, timeout: 5))
        XCTAssertEqual(personal.value as? Int, 1)
        XCTAssertEqual(work.value as? Int, 0)
        XCTAssertTrue(app.control("settings.warmFrom").exists)
        XCTAssertTrue(app.control("settings.warmStanding").waitForExistence(timeout: 5))

        // The model answers once the core has kept it, so the switch is waited for.
        work.click()
        let on = XCTNSPredicateExpectation(
            predicate: NSPredicate(format: "value == 1"), object: work)
        XCTAssertEqual(XCTWaiter.wait(for: [on], timeout: 5), .completed)
    }

    /// What names the share a limit is switched at, as `words`: a text beside the stepper,
    /// or the stepper itself where macOS reads its label as the stepper's.
    @MainActor
    private func says(_ app: XCUIApplication, _ words: String) -> XCUIElement {
        let format = "label == %@ OR CAST(value, \"NSString\") == %@"
        let named = NSPredicate(format: format, words, words)
        return app.descendants(matching: .any).matching(named).firstMatch
    }

    /// The fixture has no Pitboard of its own, so the tab offers to link the one inside the
    /// app.
    @MainActor
    func testInstallingTheCommandLine() {
        let app = XCUIApplication.launched(.oneTool)
        app.openSettings()
        app.toolbars.buttons["Command Line"].click()
        let install = app.buttons["Install Command Line Tool…"]
        XCTAssertTrue(install.waitForExistence(timeout: 5))
        install.click()
        XCTAssertTrue(install.waitForNonExistence(timeout: 5))
        XCTAssertTrue(app.text("ENDSWITH", "/bin/pitboard").waitForExistence(timeout: 5))
    }

    /// A debug build carries no update key, and the Updates tab says so.
    @MainActor
    func testUpdatesSayWhenTheBuildCannotUpdate() {
        let app = XCUIApplication.launched(.oneTool)
        app.openSettings()
        app.toolbars.buttons["Updates"].click()
        let prefix = "This copy of Pitboard can"
        let note = app.staticTexts.matching(
            NSPredicate(format: "value BEGINSWITH %@ OR label BEGINSWITH %@", prefix, prefix)
        ).firstMatch
        XCTAssertTrue(note.waitForExistence(timeout: 5))
    }
}
