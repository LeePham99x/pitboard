import Foundation
import PitboardKit
import Testing
import UserNotifications

@testable import PitboardApp

// How long an app is given to quit, what a run-out notice says and when a clock time names
// its day are the model's, tested in `pitboard-ffi`. These test what macOS is asked.

/// Quits when asked only if it would: an app busy with work, or whose person said no, does
/// not.
private final class StandInRunningApp: RunningApp {
    let bundleURL: URL?
    var isTerminated = false
    let quits: Bool
    private(set) var asked: [String] = []

    init(at path: String, quits: Bool) {
        bundleURL = URL(fileURLWithPath: path)
        self.quits = quits
    }

    func terminate() -> Bool {
        asked.append("terminate")
        if quits { isTerminated = true }
        return quits
    }

    func forceTerminate() -> Bool {
        asked.append("forceTerminate")
        isTerminated = true
        return true
    }
}

/// Every running copy is asked the way Command-Q asks. One that declines is still running,
/// and is asked again only when the model asks again.
@Test func anAppIsAskedToQuitAndNeverForced() throws {
    let busy = StandInRunningApp(at: "/Applications/ChatGPT.app", quits: false)
    let other = StandInRunningApp(at: "/Users/x/Applications/ChatGPT.app", quits: true)
    let opened = Opened()
    let apps = MacAppControl(
        copies: { $0 == "com.openai.codex" ? [busy, other] : [] },
        open: { url, front in opened.keep(Opening(url: url, front: front)) })

    #expect(try apps.running(app: "com.openai.codex") == "/Applications/ChatGPT.app")
    #expect(try apps.running(app: "com.example.none") == nil)

    try apps.requestQuit(app: "com.openai.codex")
    #expect(busy.asked == ["terminate"])
    #expect(other.asked == ["terminate"])
    #expect(try apps.running(app: "com.openai.codex") == "/Applications/ChatGPT.app")

    try apps.requestQuit(app: "com.openai.codex")
    #expect(busy.asked == ["terminate", "terminate"], "a copy that quit is not asked again")
    #expect(other.asked == ["terminate"])

    try apps.reopen(location: "/Applications/ChatGPT.app", front: false)
    #expect(
        opened.asked == [
            Opening(url: URL(fileURLWithPath: "/Applications/ChatGPT.app"), front: false)
        ])
}

@Test func anAppComesForwardOnlyWhenAsked() throws {
    let opened = Opened()
    let apps = MacAppControl(
        copies: { _ in [] },
        open: { url, front in opened.keep(Opening(url: url, front: front)) })
    try apps.reopen(location: "/Applications/Claude.app", front: true)
    try apps.reopen(location: "/Applications/Claude.app", front: false)
    let claude = URL(fileURLWithPath: "/Applications/Claude.app")
    #expect(
        opened.asked == [Opening(url: claude, front: true), Opening(url: claude, front: false)])
    #expect(MacAppControl.opening(front: true).activates)
    #expect(!MacAppControl.opening(front: false).activates)
}

private struct Opening: Equatable {
    let url: URL
    let front: Bool
}

private final class Opened: @unchecked Sendable {
    // Unchecked because every read and write holds `lock`.
    private let lock = NSLock()
    private var kept: [Opening] = []
    var asked: [Opening] { lock.withLock { kept } }
    func keep(_ opening: Opening) { lock.withLock { kept.append(opening) } }
}

/// macOS keeps the person's 12 or 24 hours in their locale.
@Test func theClockFollowsThe12Or24HourSetting() throws {
    let utc = TimeZone(identifier: "UTC")!
    // 14:05 UTC on Wednesday 14 January 2026.
    let at: Int64 = 1_768_399_500
    let twelve = MacLocalTime(
        locale: Locale(identifier: "en_US"), calendar: Calendar(identifier: .gregorian),
        timeZone: utc)
    let twentyFour = MacLocalTime(
        locale: Locale(identifier: "en_US@hours=h23"),
        calendar: Calendar(identifier: .gregorian), timeZone: utc)

    #expect(try twelve.clock(epoch: at, withWeekday: false) == "2:05\u{202F}PM")
    #expect(try twentyFour.clock(epoch: at, withWeekday: false) == "14:05")
    #expect(try twentyFour.clock(epoch: at, withWeekday: true) == "Wed 14:05")
    #expect(try twentyFour.dateAndTime(epoch: at) == "Jan 14, 2026 at 14:05")

    #expect(try twentyFour.sameDay(first: at, second: at + 9 * 3600))
    #expect(try !twentyFour.sameDay(first: at, second: at + 10 * 3600), "past midnight in UTC")
}

@Test func aRunOutIsPostedWithTheAccountItsButtonSwitchesTo() {
    let content = MacNotifications.content(
        of: RunOutNotice(
            id: "claude/work/session/1768399500", title: "work has no 5-hour limit left",
            subtitle: "Claude Code", body: "spare has 80% of its own left.",
            switchTo: "claude/spare"))
    #expect(content.title == "work has no 5-hour limit left")
    #expect(content.subtitle == "Claude Code")
    #expect(content.body == "spare has 80% of its own left.")
    #expect(content.categoryIdentifier == MacNotifications.category)
    #expect(content.userInfo["label"] as? String == "claude/spare")
}

/// No category, so no Switch button.
@Test func aSwitchMadeByItselfIsPostedWithNoButton() {
    let content = MacNotifications.content(
        of: RunOutNotice(
            id: "auto/work/spare/1768399500", title: "Switched Claude Code to spare",
            subtitle: nil,
            body: "work had used 96% of its 5-hour limit. "
                + "Sessions already running follow within 33 seconds.",
            switchTo: nil))
    #expect(content.title == "Switched Claude Code to spare")
    #expect(content.categoryIdentifier.isEmpty)
    #expect(content.userInfo.isEmpty)
}
