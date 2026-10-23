import Foundation
import PitboardKit

/// Watches the accounts in use and raises the island when one passes the share somebody chose
/// in Settings › Limits, and again when it runs out; shows the core's own run-out and
/// automatic-switch notices there too. The switch itself stays the model's: the island's
/// button sends what the account's own BOX would.
@MainActor
final class LimitAlerts {
    static let defaultThreshold = 85
    static let thresholds = 50...99

    let island = NotchIsland()
    private let defaults: UserDefaults
    private var watch = LimitWatch()
    private weak var model: AppModel?
    /// The switch the island asked for, until it lands.
    private var awaiting: (target: LimitAlert.Target, asked: Date)?
    /// When the island last said an account ran out, so the core's notice of the same
    /// run-out, arriving alongside, does not say it twice.
    private var saidOut: Date?

    init(defaults: UserDefaults) {
        self.defaults = defaults
        island.onSwitch = { [weak self] alert in self?.switchTo(alert) }
    }

    var on: Bool {
        defaults.object(forKey: DefaultsKey.limitAlertOn) as? Bool ?? true
    }

    var threshold: Int {
        let set =
            defaults.object(forKey: DefaultsKey.limitAlertAt) as? Int ?? Self.defaultThreshold
        return min(max(set, Self.thresholds.lowerBound), Self.thresholds.upperBound)
    }

    /// Looks again at the accounts in use, as the model's sections change.
    func check(_ model: AppModel) {
        self.model = model
        if let awaiting {
            if let id = awaiting.target.id, model.item(id)?.inUse == true {
                landed(awaiting.target, in: model)
            } else if model.switchUnderWay == nil, Date().timeIntervalSince(awaiting.asked) > 4
            {
                // The switch ended without landing: the window says why.
                self.awaiting = nil
                island.dismiss()
            }
        }
        guard on else {
            watch.reset()
            return
        }
        guard let alert = watch.check(model.sections, threshold: Double(threshold)) else {
            return
        }
        if alert.kind == .out { saidOut = Date() }
        island.show(alert)
    }

    /// A notice the core posts to Notification Center, on the main thread. A run-out offers
    /// its switch on the island as well, and a switch Pitboard made by itself shows as made.
    func receive(_ notice: RunOutNotice) {
        if let label = notice.switchTo {
            guard on, saidOut.map({ Date().timeIntervalSince($0) > 120 }) ?? true else {
                return
            }
            let target = model?.sections.flatMap(\.accounts).first { $0.qualified == label }
            island.show(
                LimitAlert(
                    kind: .out, percent: 100, level: .out,
                    caption: notice.subtitle ?? "Pitboard", title: notice.title,
                    detail: notice.body,
                    target: LimitAlert.Target(
                        id: target?.id, provider: target?.provider ?? "",
                        title: target?.title ?? label,
                        intent: .switchTo(qualified: label))))
        } else if notice.id.hasPrefix("auto/") {
            island.show(
                LimitAlert(
                    kind: .switched, percent: nil, level: nil, caption: "Pitboard",
                    title: notice.title, detail: notice.body, target: nil))
        }
    }

    /// Settings › Limits shows the island as it would look now, and its Switch only plays the
    /// switch.
    func rehearse(_ model: AppModel) {
        self.model = model
        let inUse = model.sections.flatMap(\.accounts).first { $0.inUse && $0.tightest != nil }
        var alert: LimitAlert
        if let inUse, let row = inUse.tightest {
            let shown = LimitRow(
                name: row.name, short: row.short,
                percent: max(row.percent, Double(threshold) + 7),
                figure: row.figure, level: .out, resets: row.resets, pace: nil,
                spoken: row.spoken)
            alert = .about(
                inUse, row: shown,
                instead: PitWallOrder.bestAlternative(to: inUse, in: model.sections))
        } else {
            alert = LimitAlert(
                kind: .warning, percent: 92, level: .out,
                caption: "Claude Code · work · 5-hour", title: "Almost out of limit",
                detail: "team has 94% left",
                target: LimitAlert.Target(
                    id: nil, provider: "claude", title: "team",
                    intent: .refresh(asked: false)))
        }
        alert.rehearsal = true
        island.show(alert)
    }

    private func switchTo(_ alert: LimitAlert) {
        guard let target = alert.target else { return }
        if alert.rehearsal {
            Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(900))
                self?.island.show(
                    .switched(
                        tool: PitWall.toolName(
                            target.provider.isEmpty ? "claude" : target.provider),
                        to: target.title, detail: "Open sessions follow within 30 seconds"))
            }
            return
        }
        awaiting = (target, Date())
        model?.send(target.intent)
    }

    private func landed(_ target: LimitAlert.Target, in model: AppModel) {
        awaiting = nil
        let follows = model.lastSwitches.first { $0.provider == target.provider }?.followsAt
        let seconds = follows.map { $0 - Int64(Date().timeIntervalSince1970) } ?? 0
        island.show(
            .switched(
                tool: PitWall.toolName(target.provider), to: target.title,
                detail: seconds > 0
                    ? "Open sessions follow within \(seconds) seconds"
                    : "Open sessions follow shortly"))
    }
}
