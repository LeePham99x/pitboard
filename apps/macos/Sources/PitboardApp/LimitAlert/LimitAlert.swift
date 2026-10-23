import Foundation
import PitboardKit

/// What the island at the notch says: an account in use nearly out or out of a limit, with the
/// account to switch to, or a switch made.
struct LimitAlert: Equatable, Identifiable {
    enum Kind: Equatable {
        /// Past the share somebody asked to hear about.
        case warning
        /// Nothing left of a limit.
        case out
        /// A switch made, by the island's button or by Pitboard by itself.
        case switched
    }

    /// The account the island's Switch button goes to.
    struct Target: Equatable {
        /// The item's id, to see the switch land by; nil where only the core's label is known.
        let id: String?
        let provider: String
        let title: String
        let intent: Intent
    }

    let id = UUID()
    let kind: Kind
    /// The share used of the limit the ring shows, where there is one.
    let percent: Double?
    let level: UsageLevel?
    /// "Claude Code · work · 5-hour".
    let caption: String
    let title: String
    let detail: String
    let target: Target?
    /// Shown from Settings: Switch only plays what a switch would look like.
    var rehearsal = false

    static func == (lhs: Self, rhs: Self) -> Bool { lhs.id == rhs.id }

    /// An account in use past the threshold or out, and the best account to switch to.
    static func about(_ item: AccountItem, row: LimitRow, instead: AccountItem?) -> LimitAlert {
        let out = row.percent >= 100
        let tool = PitWall.toolName(item.provider)
        let target = instead.flatMap { other in
            other.action.map {
                Target(
                    id: other.id, provider: other.provider, title: other.title,
                    intent: $0.intent)
            }
        }
        let detail: String
        if let instead, let room = instead.room {
            detail = "\(instead.title) has \(Int(room.rounded()))% left"
        } else if !row.resets.isEmpty {
            detail = "No other account has room · \(row.resets)"
        } else {
            detail = "No other account has room"
        }
        return LimitAlert(
            kind: out ? .out : .warning,
            percent: row.percent,
            // Amber while there is some left, as the design's warning is, and red once out.
            level: out ? .out : .low,
            caption: "\(tool) · \(item.title) · \(row.name)",
            title: out ? "Out of limit" : "Almost out of limit",
            detail: detail,
            target: target)
    }

    static func switched(tool: String, to title: String, detail: String) -> LimitAlert {
        LimitAlert(
            kind: .switched, percent: nil, level: nil, caption: tool,
            title: "Switched \(tool) to \(title)", detail: detail, target: nil)
    }
}

/// Which accounts in use have been warned about, so each crossing is said once: once past the
/// threshold, and once more when nothing is left. Falling back under either, as a limit does
/// when it resets, lets it be said again.
struct LimitWatch {
    private(set) var warned: Set<String> = []
    private(set) var emptied: Set<String> = []

    /// The alert to show now, if any: the emptiest account in use that has crossed a line
    /// since it was last looked at.
    mutating func check(_ sections: [AccountSection], threshold: Double) -> LimitAlert? {
        var found: (item: AccountItem, row: LimitRow)?
        for section in sections {
            for item in section.accounts where item.inUse {
                guard let row = item.tightest else { continue }
                if row.percent < threshold { warned.remove(item.id) }
                if row.percent < 100 { emptied.remove(item.id) }
                let crossed =
                    row.percent >= 100
                    ? !emptied.contains(item.id)
                    : row.percent >= threshold && !warned.contains(item.id)
                guard crossed else { continue }
                warned.insert(item.id)
                if row.percent >= 100 { emptied.insert(item.id) }
                if found.map({ row.percent > $0.row.percent }) ?? true {
                    found = (item, row)
                }
            }
        }
        guard let found else { return nil }
        return LimitAlert.about(
            found.item, row: found.row,
            instead: PitWallOrder.bestAlternative(to: found.item, in: sections))
    }

    /// Forgets every warning, so turning alerts back on says what stands now.
    mutating func reset() {
        warned = []
        emptied = []
    }
}
