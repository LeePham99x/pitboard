import Foundation
import PitboardKit

/// When a limit resets, as the pit wall counts down to it.
enum PitWallReset {
    /// The second `row` of the account `item` resets at, from the status the row was made
    /// from. A row carries the reset only in words, so its limit is found as the one of the
    /// account's with the nearest share used.
    static func at(_ row: LimitRow, of item: AccountItem, in status: Status?) -> Int64? {
        guard let account = status?.accounts.first(where: { $0.id == item.id }),
            let windows = account.usage?.windows
        else { return nil }
        return
            windows
            .filter { $0.resetsAt != nil }
            .min { abs($0.percent - row.percent) < abs($1.percent - row.percent) }?
            .resetsAt
    }

    /// "02:09:41" under a day, "5D 18:22" from a day on, and "00:00:00" once it has passed.
    static func countdown(_ seconds: Int64) -> String {
        let left = max(seconds, 0)
        let days = left / 86_400
        let hours = left % 86_400 / 3_600
        let minutes = left % 3_600 / 60
        if days > 0 {
            return String(format: "%lldD %02lld:%02lld", days, hours, minutes)
        }
        return String(format: "%02lld:%02lld:%02lld", hours, minutes, left % 60)
    }

    /// "01:18" for a chip: hours and minutes, or days and hours from a day on.
    static func short(_ seconds: Int64) -> String {
        let left = max(seconds, 0)
        if left >= 86_400 {
            return String(format: "%lldD %02lldH", left / 86_400, left % 86_400 / 3_600)
        }
        return String(format: "%02lld:%02lld", left / 3_600, left % 3_600 / 60)
    }
}

extension PitWallReset {
    /// "2h 09m", "5d 18h", "now": a reset in a column, in the core's own way of writing time.
    static func words(_ seconds: Int64) -> String {
        let left = max(seconds, 0)
        if left < 60 { return "now" }
        if left >= 86_400 { return "\(left / 86_400)d \(left % 86_400 / 3_600)h" }
        if left >= 3_600 {
            return String(format: "%lldh %02lldm", left / 3_600, left % 3_600 / 60)
        }
        return "\(left / 60)m"
    }
}

/// The reset the pit wall leads with: the account in use's tightest limit, Claude Code's
/// first, as the tool whose limits run out soonest, then any other tool's.
struct PitWallHeadline {
    let item: AccountItem
    let row: LimitRow
    let resets: Int64

    /// "Claude Code 5h reset".
    var caption: String { "\(PitWall.toolName(item.provider)) \(row.short) reset" }

    @MainActor static func of(_ model: AppModel) -> PitWallHeadline? {
        let inUse = model.sections.flatMap(\.accounts).filter(\.inUse)
        let ordered =
            inUse.filter { $0.provider == "claude" } + inUse.filter { $0.provider != "claude" }
        for item in ordered {
            if let row = item.tightest,
                let resets = PitWallReset.at(row, of: item, in: model.status)
            {
                return PitWallHeadline(item: item, row: row, resets: resets)
            }
        }
        return nil
    }
}
