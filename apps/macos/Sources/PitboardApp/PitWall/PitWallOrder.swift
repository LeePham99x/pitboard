import PitboardKit

/// Where an account stands on a tool's timing tower: the one on track, then the ones a switch
/// could go to by how much they have left, then those nothing can switch to now.
enum TowerPosition: Equatable {
    /// In use: "▶".
    case onTrack
    /// The nth best to switch to, from 1: "P1".
    case next(Int)
    /// Nothing switches to it now: it needs signing in, naming or placing, or says nothing yet.
    case parked

    var label: String {
        switch self {
        case .onTrack: "▶"
        case .next(let place): "P\(place)"
        case .parked: "—"
        }
    }
}

extension AccountItem {
    /// Whether pressing its action switches the tool to it, rather than signing in or naming.
    var switchesTo: Bool {
        guard !inUse, !needsSignIn, !unplaced, let action else { return false }
        if case .switchTo = action.intent { return true }
        return false
    }
}

enum PitWallOrder {
    typealias Entry = (item: AccountItem, position: TowerPosition)

    /// A section's accounts as its tower lists them, each with its position. Accounts with as
    /// much left keep the order the core gave them.
    static func tower(_ accounts: [AccountItem]) -> [Entry] {
        let onTrack = accounts.filter(\.inUse)
        let ready = accounts.enumerated()
            .filter { $0.element.switchesTo }
            .sorted { lhs, rhs in
                let left = lhs.element.room ?? -1
                let right = rhs.element.room ?? -1
                return left != right ? left > right : lhs.offset < rhs.offset
            }
            .map(\.element)
        let parked = accounts.filter { !$0.inUse && !$0.switchesTo }
        return onTrack.map { ($0, .onTrack) }
            + ready.enumerated().map { ($0.element, .next($0.offset + 1)) }
            + parked.map { ($0, .parked) }
    }

    /// The account a switch away from `item` should go to: the one in its section with the
    /// most left, if any has more than nothing.
    static func bestAlternative(to item: AccountItem, in sections: [AccountSection])
        -> AccountItem?
    {
        guard let section = sections.first(where: { $0.accounts.contains { $0.id == item.id } })
        else { return nil }
        return section.accounts
            .filter { $0.id != item.id && $0.switchesTo && ($0.room ?? 0) > 0 }
            .max { ($0.room ?? 0) < ($1.room ?? 0) }
    }
}
