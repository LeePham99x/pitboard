import PitboardKit
import PitboardTestSupport
import Testing

@testable import PitboardApp

// What the pit wall decides itself: the order of a tower, when the island speaks, and how a
// reset counts down. Every sentence it shows besides is the model's.

private func limit(_ percent: Double) -> LimitRow {
    LimitRow(
        name: "5-hour", short: "5h", percent: percent, figure: "\(Int(percent))%",
        level: percent >= 90 ? .out : percent >= 70 ? .low : .plenty,
        resets: "resets in 2h 05m", pace: nil, spoken: "")
}

private func item(
    _ title: String, used: Double?, inUse: Bool = false, needsSignIn: Bool = false,
    provider: String = "claude"
) -> AccountItem {
    let qualified = "\(provider)/\(title)"
    let action: ItemAction? =
        inUse
        ? nil
        : needsSignIn
            ? ItemAction(
                title: "Sign In Again…", spoken: "",
                intent: .presentSheet(sheet: .add(provider: provider)))
            : ItemAction(title: "Use", spoken: "", intent: .switchTo(qualified: qualified))
    return AccountItem(
        id: "\(provider):\(title)", provider: provider, qualified: qualified, title: title,
        email: "\(title)@example.com", plan: nil, menuTitle: title, spokenName: title,
        spoken: title, inUse: inUse, needsSignIn: needsSignIn, unplaced: false,
        switching: false, action: action, summary: "", problem: nil, staleNote: nil,
        pace: nil, parkedNote: nil, warmNote: nil, help: nil,
        limits: used.map { [limit($0)] } ?? [], offers: [], windows: [], forget: nil)
}

private func section(_ accounts: AccountItem...) -> AccountSection {
    AccountSection(id: "claude", heading: "Claude Code", accounts: accounts)
}

@Test func theTowerPutsTheAccountOnTrackFirstThenTheRoomiest() {
    let tower = PitWallOrder.tower([
        item("old", used: nil, needsSignIn: true),
        item("personal", used: 100),
        item("work", used: 84, inUse: true),
        item("team", used: 6),
    ])
    #expect(tower.map(\.item.title) == ["work", "team", "personal", "old"])
    #expect(tower.map(\.position.label) == ["▶", "P1", "P2", "—"])
}

@Test func theBestAlternativeHasRoomLeftAndCanBeSwitchedTo() {
    let work = item("work", used: 92, inUse: true)
    let sections = [
        section(work, item("full", used: 100), item("team", used: 40), item("spare", used: 10))
    ]
    #expect(PitWallOrder.bestAlternative(to: work, in: sections)?.title == "spare")
    let none = [section(work, item("full", used: 100), item("old", used: 0, needsSignIn: true))]
    #expect(PitWallOrder.bestAlternative(to: work, in: none) == nil)
}

@Test func theIslandSpeaksOnceAtTheThresholdAndOnceMoreWhenOut() {
    var watch = LimitWatch()
    func check(_ used: Double) -> LimitAlert? {
        watch.check(
            [section(item("work", used: used, inUse: true), item("team", used: 6))],
            threshold: 85)
    }
    #expect(check(70) == nil)
    let warning = check(86)
    #expect(warning?.kind == .warning)
    #expect(warning?.detail == "team has 94% left")
    #expect(warning?.target?.intent == .switchTo(qualified: "claude/team"))
    #expect(check(90) == nil, "said once while past the threshold")
    #expect(check(100)?.kind == .out)
    #expect(check(100) == nil, "said once while out")
    #expect(check(3) == nil, "a reset")
    #expect(check(88)?.kind == .warning, "and said again after it")
}

@Test func anAccountNotInUseNeverRaisesTheIsland() {
    var watch = LimitWatch()
    let alert = watch.check(
        [section(item("work", used: 10, inUse: true), item("team", used: 99))], threshold: 85)
    #expect(alert == nil)
}

@Test func aResetCountsDownInHoursThenDays() {
    #expect(PitWallReset.countdown(2 * 3_600 + 9 * 60 + 41) == "02:09:41")
    #expect(PitWallReset.countdown(-5) == "00:00:00")
    #expect(PitWallReset.countdown(5 * 86_400 + 18 * 3_600 + 22 * 60) == "5D 18:22")
    #expect(PitWallReset.short(78 * 60) == "01:18")
    #expect(PitWallReset.short(2 * 86_400 + 3 * 3_600) == "2D 03H")
}

@Test func aResetIsFoundByTheLimitWithTheNearestShareUsed() {
    var work = account("work", signedIn: true)
    work = Account(
        id: work.id, provider: work.provider, label: work.label, qualified: work.qualified,
        unplaced: false, email: work.email, accountId: work.accountId, signedIn: true,
        switchable: false, parked: nil,
        usage: Usage(
            source: .live, observedAt: 0,
            windows: [
                Limit(
                    kind: "five_hour", lengthSeconds: 18_000, scope: nil, percent: 84,
                    resetsAt: 7_000, severity: nil, isActive: true),
                Limit(
                    kind: "seven_day", lengthSeconds: 604_800, scope: nil, percent: 30,
                    resetsAt: 90_000, severity: nil, isActive: true),
            ],
            listsEveryLimit: true),
        stale: nil, staleExplanation: nil, plan: nil, readAt: nil)
    let shown = item("work", used: 84, inUse: true)
    #expect(PitWallReset.at(limit(84), of: shown, in: status([work])) == 7_000)
    #expect(PitWallReset.at(limit(31), of: shown, in: status([work])) == 90_000)
    #expect(PitWallReset.at(limit(84), of: shown, in: nil) == nil)
}
