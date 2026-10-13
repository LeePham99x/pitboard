import XCTest

// The fixture's Claude is a stand-in: no real app is quit or opened.
final class ClaudeDesktopTests: FixtureTestCase {
    @MainActor
    func testClaudeDesktopAccountsAreListedUnderTheirTool() throws {
        let app = launch(.claudeDesktop)
        app.openMenu()
        XCTAssertTrue(app.menuItem("Claude Desktop").waitForExistence(timeout: 5))
        // Claude Code has a work of its own, in the section above.
        let titles = app.statusItems.firstMatch.menuItems.allElementsBoundByIndex.map(\.title)
        let heading = try XCTUnwrap(titles.firstIndex(of: "Claude Desktop"))
        XCTAssertEqual(Set(titles.dropFirst(heading + 1).prefix(2)), ["work", "home"])
        app.typeKey(.escape, modifierFlags: [])

        app.openWindow()
        let home = app.accountRow("desktop/home")
        XCTAssertTrue(home.waitForExistence(timeout: 5))
        XCTAssertTrue(home.buttons["Use home (Claude Desktop)"].exists)
    }

    @MainActor
    func testUsingAClaudeDesktopAccountWithClaudeClosedSwitchesAtOnce() {
        let app = launch(.claudeDesktop)
        app.openWindow()
        let home = app.accountRow("desktop/home")
        XCTAssertTrue(home.waitForExistence(timeout: 5))
        home.buttons["Use home (Claude Desktop)"].click()
        let notice = app.descendants(matching: .any)["notice.switch/desktop"]
        XCTAssertTrue(notice.waitForExistence(timeout: 10))
        XCTAssertFalse(app.alert.exists, "Claude was closed, so nothing asked")
    }

    @MainActor
    func testUsingAClaudeDesktopAccountAsksToQuitClaudeFirst() {
        let app = launch(.claudeDesktopOpen)
        app.openWindow()
        let home = app.accountRow("desktop/home")
        XCTAssertTrue(home.waitForExistence(timeout: 5))
        let notice = app.descendants(matching: .any)["notice.switch/desktop"]

        home.buttons["Use home (Claude Desktop)"].click()
        XCTAssertTrue(app.alert.buttons["Cancel"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.alert.staticTexts["Quit Claude to switch?"].exists)
        app.alert.buttons["Cancel"].click()
        XCTAssertTrue(app.alert.waitForNonExistence(timeout: 5))
        XCTAssertFalse(notice.exists, "nothing was switched")

        home.buttons["Use home (Claude Desktop)"].click()
        let quit = app.alert.buttons["Quit Claude and Switch"]
        XCTAssertTrue(quit.waitForExistence(timeout: 5))
        quit.click()
        XCTAssertTrue(notice.waitForExistence(timeout: 10))
    }

    @MainActor
    func testAddingAClaudeDesktopAccountAsksToQuitClaudeOverTheSheet() {
        let app = launch(.claudeDesktopOpen)
        app.openWindow()
        startAddingTravel(app)

        app.buttons["Park work"].click()
        XCTAssertTrue(app.alert.buttons["Cancel"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.alert.staticTexts["Quit Claude to add an account?"].exists)
        XCTAssertTrue(app.alert.buttons["Quit Claude and Park work"].exists)
        let name = app.textFields["sheet.name"]
        XCTAssertTrue(name.exists, "asked over the sheet, which stays")
        app.alert.buttons["Cancel"].click()
        XCTAssertTrue(app.alert.waitForNonExistence(timeout: 5))
        XCTAssertEqual(name.value as? String, "travel")

        let park = app.buttons["Park work"]
        XCTAssertTrue(park.wait(for: \.isEnabled, toEqual: true, timeout: 5))
        park.click()
        let quit = app.alert.buttons["Quit Claude and Park work"]
        XCTAssertTrue(quit.waitForExistence(timeout: 5))
        quit.click()
        let waiting = app.text("BEGINSWITH", "In Claude, sign in to the account you’re adding.")
        XCTAssertTrue(waiting.waitForExistence(timeout: 10))
        // The fixture's Claude signs in to travel and quits 7 seconds after it is opened.
        let travel = app.accountRow("desktop/travel")
        XCTAssertTrue(travel.waitForExistence(timeout: 30))
        XCTAssertTrue(travel.staticTexts["In Use"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.sheets.firstMatch.waitForNonExistence(timeout: 5))
    }

    @MainActor
    func testAnAddLeftHalfwayOffersToPutTheParkedAccountBack() {
        let app = launch(.claudeDesktop)
        app.openWindow()
        startAddingTravel(app)

        app.buttons["Park work"].click()
        let waiting = app.text(
            "BEGINSWITH", "Open Claude and sign in to the account you’re adding.")
        XCTAssertTrue(waiting.waitForExistence(timeout: 10))
        XCTAssertFalse(app.alert.exists, "Claude was closed, so nothing asked")
        app.typeKey(.escape, modifierFlags: [])
        XCTAssertTrue(waiting.exists, "the waiting page stays")
        XCTAssertTrue(app.sheets.firstMatch.buttons["Put work Back"].exists)

        app.sheets.firstMatch.buttons["Later"].click()
        XCTAssertTrue(app.sheets.firstMatch.waitForNonExistence(timeout: 5))
        let notice = app.descendants(matching: .any)["notice.awaiting/desktop"]
        XCTAssertTrue(notice.waitForExistence(timeout: 5))
        XCTAssertTrue(notice.buttons["Finish Adding…"].exists)
        let putBack = notice.buttons["Put work Back"]
        XCTAssertTrue(putBack.exists)

        putBack.click()
        let work = app.accountRow("desktop/work")
        XCTAssertTrue(work.staticTexts["In Use"].waitForExistence(timeout: 10))
        XCTAssertTrue(notice.waitForNonExistence(timeout: 5))
    }

    @MainActor
    private func startAddingTravel(_ app: XCUIApplication) {
        app.toolbars.buttons["Add Account"].click()
        let tool = app.popUpButtons["sheet.tool"]
        XCTAssertTrue(tool.waitForExistence(timeout: 5))
        tool.click()
        // Its own item, not the menu bar item's Claude Desktop heading.
        tool.menuItems["Claude Desktop"].click()
        let name = app.textFields["sheet.name"]
        name.click()
        name.typeText("travel")
    }
}
