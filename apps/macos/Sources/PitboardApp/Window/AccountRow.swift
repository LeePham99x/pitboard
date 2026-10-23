import PitboardKit
import SwiftUI

/// The columns of a tool's timing tower in the window: one for each limit any of its accounts
/// has, by the short name the core gives it, "5h" and "week".
enum TowerColumns {
    static func of(_ section: AccountSection) -> [String] {
        var seen: [String] = []
        for item in section.accounts {
            for row in item.limits where !seen.contains(row.short) { seen.append(row.short) }
        }
        return Array(seen.prefix(3))
    }

    static let position: CGFloat = 34
    static let account: CGFloat = 190
    static let limit: CGFloat = 150
    static let reset: CGFloat = 64
    static let action: CGFloat = 82
}

/// A tool's heading over its tower, and the names of its columns.
struct TowerHeader: View {
    let section: AccountSection
    let columns: [String]

    var body: some View {
        let provider = section.accounts.first?.provider ?? ""
        VStack(alignment: .leading, spacing: 8) {
            ToolHeading(provider: provider, name: section.heading ?? PitWall.toolName(provider))
            HStack(spacing: 14) {
                PitWallCaption("Pos").frame(width: TowerColumns.position, alignment: .leading)
                PitWallCaption("Account").frame(
                    width: TowerColumns.account, alignment: .leading)
                ForEach(columns, id: \.self) { column in
                    PitWallCaption(column).frame(width: TowerColumns.limit, alignment: .leading)
                }
                PitWallCaption("Reset").frame(width: TowerColumns.reset, alignment: .leading)
                Spacer(minLength: 0)
            }
            .accessibilityHidden(true)
        }
        .padding(.horizontal, 18)
        .padding(.top, 12)
        .padding(.bottom, 4)
    }
}

/// One account on its tool's tower: position, name and email, a column of LEDs for each
/// limit with its share used and pace, when its tightest limit resets, and its action.
struct AccountRow: View {
    let item: AccountItem
    let position: TowerPosition
    let columns: [String]
    let status: Status?
    let perform: (Intent) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 14) {
                Text(position.label)
                    .font(PitWall.mono(11, .bold))
                    .foregroundStyle(position == .onTrack ? PitWall.flag : PitWall.faint)
                    .frame(width: TowerColumns.position, alignment: .leading)
                VStack(alignment: .leading, spacing: 3) {
                    Text(item.towerName)
                        .font(PitWall.mono(12, .bold))
                        .foregroundStyle(PitWall.ink)
                        .lineLimit(1)
                    if !item.email.isEmpty, item.email != item.title {
                        Text(item.email)
                            .font(PitWall.mono(10.5))
                            .foregroundStyle(PitWall.faint)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }
                }
                .frame(width: TowerColumns.account, alignment: .leading)
                if let problem = item.problem, item.needsSignIn || item.limits.isEmpty {
                    Text(problem.uppercased())
                        .font(PitWall.mono(10.5, .semibold))
                        .tracking(0.6)
                        .foregroundStyle(PitWall.amber)
                        .lineLimit(2)
                        .frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    ForEach(columns, id: \.self) { column in
                        LimitColumn(row: item.limits.first { $0.short == column })
                            .frame(width: TowerColumns.limit, alignment: .leading)
                    }
                    resetColumn
                        .frame(width: TowerColumns.reset, alignment: .leading)
                    Spacer(minLength: 0)
                }
                action
                    .frame(width: TowerColumns.action, alignment: .trailing)
            }
            ForEach(notes, id: \.self) { note in
                Text(note)
                    .font(PitWall.mono(10.5))
                    .foregroundStyle(PitWall.amber.opacity(0.85))
                    .padding(.leading, TowerColumns.position + 14)
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 10)
        .background {
            if item.inUse { PitWall.onTrack }
        }
        .overlay(alignment: .leading) {
            if item.inUse { PitWall.flag.frame(width: 3) }
        }
        .overlay(alignment: .bottom) { PitWall.rule.frame(height: 1) }
        .contentShape(.rect)
        .help(item.help ?? item.summary)
        // One account, read as one thing with its controls in it, rather than a stop for
        // every line on the way past.
        .accessibilityElement(children: .contain)
        .accessibilityLabel(item.spoken)
        .accessibilityIdentifier("account.\(item.qualified ?? item.id)")
    }

    /// What the core says of it besides its limits, which the tower has no column for. A
    /// problem shown in place of the limits is not said twice.
    private var notes: [String] {
        let problem = item.needsSignIn || item.limits.isEmpty ? nil : item.problem
        return [problem, item.staleNote, item.parkedNote, item.warmNote].compactMap { $0 }
    }

    @ViewBuilder private var resetColumn: some View {
        if let row = item.tightest, let resets = PitWallReset.at(row, of: item, in: status) {
            TimelineView(.periodic(from: .now, by: 30)) { context in
                Text(PitWallReset.words(resets - Int64(context.date.timeIntervalSince1970)))
            }
            .font(PitWall.mono(11))
            .foregroundStyle(PitWall.ink)
            .help(row.resets)
        } else {
            Text("—").font(PitWall.mono(11)).foregroundStyle(PitWall.faint)
        }
    }

    @ViewBuilder private var action: some View {
        if item.switching {
            ProgressView().controlSize(.small)
                .accessibilityLabel("Switching")
        } else if item.inUse, item.action == nil {
            Text("ON TRACK")
                .font(PitWall.mono(10, .heavy))
                .tracking(1.2)
                .foregroundStyle(PitWall.flag)
        } else if let action = item.action {
            if item.switchesTo, (item.room ?? 1) <= 0 {
                Button("WAIT") { perform(action.intent) }
                    .buttonStyle(BoxButtonStyle(lit: false))
                    .help("Nothing left until it resets; switching now runs out at once")
                    .accessibilityLabel(action.spoken)
            } else {
                Button(
                    item.switchesTo
                        ? "BOX"
                        : item.needsSignIn ? "LOGIN" : PitWallPanelRow.boxWord(action.title)
                ) { perform(action.intent) }
                .buttonStyle(BoxButtonStyle(lit: item.switchesTo))
                .accessibilityLabel(action.spoken)
            }
        }
    }
}

/// A limit in its column: the share used and the pace above, the LEDs beneath.
private struct LimitColumn: View {
    let row: LimitRow?

    var body: some View {
        if let row {
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(row.figure)
                        .font(PitWall.mono(11, .bold))
                        .foregroundStyle(row.level.figure)
                    Spacer(minLength: 4)
                    if let pace = row.pace {
                        Text(pace.said)
                            .font(PitWall.mono(10))
                            .foregroundStyle(PitWall.dim)
                            .lineLimit(1)
                            .help(pace.help)
                    }
                }
                LEDBar(percent: row.percent, level: row.level, pace: row.pace, height: 8)
            }
            .help(row.resets)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(row.spoken)
        } else {
            Text("—").font(PitWall.mono(11)).foregroundStyle(PitWall.faint)
        }
    }
}
