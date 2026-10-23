import PitboardKit
import SwiftUI

/// One account on a tool's timing tower in the panel: its position, its name with its tightest
/// limit as LEDs beneath, the share used, and what pressing it does.
struct PitWallPanelRow: View {
    let item: AccountItem
    let position: TowerPosition
    let model: AppModel

    var body: some View {
        HStack(spacing: 10) {
            Text(position.label)
                .font(PitWall.mono(10, .bold))
                .foregroundStyle(position == .onTrack ? PitWall.flag : PitWall.faint)
                .frame(width: 22, alignment: .leading)
            VStack(alignment: .leading, spacing: 5) {
                Text(item.towerName)
                    .font(PitWall.mono(11, .bold))
                    .foregroundStyle(PitWall.ink)
                    .lineLimit(1)
                    .truncationMode(.middle)
                beneath
            }
            Spacer(minLength: 6)
            if let row = item.tightest, !item.needsSignIn {
                Text(row.figure)
                    .font(PitWall.mono(11, .bold))
                    .foregroundStyle(row.level.figure)
                    .monospacedDigit()
            }
            action
                .frame(minWidth: 64, alignment: .trailing)
        }
        .padding(.leading, 12)
        .padding(.trailing, 14)
        .padding(.vertical, 9)
        .background {
            if item.inUse { PitWall.onTrack }
        }
        .overlay(alignment: .leading) {
            if item.inUse { PitWall.flag.frame(width: 3) }
        }
        .overlay(alignment: .bottom) { PitWall.rule.frame(height: 1) }
        .contentShape(.rect)
        .help(
            [item.email, item.help ?? item.summary].filter { !$0.isEmpty }.joined(
                separator: "\n")
        )
        .accessibilityElement(children: .combine)
        .accessibilityLabel(item.spoken)
    }

    @ViewBuilder private var beneath: some View {
        if let problem = item.problem, item.needsSignIn || item.limits.isEmpty {
            Text(problem.uppercased())
                .font(PitWall.mono(10, .semibold))
                .tracking(0.6)
                .foregroundStyle(PitWall.amber)
                .lineLimit(1)
        } else if let row = item.tightest {
            LEDBar(percent: row.percent, level: row.level, pace: row.pace, height: 7)
                .frame(maxWidth: 200, alignment: .leading)
        } else {
            Text(item.summary)
                .font(PitWall.mono(10))
                .foregroundStyle(PitWall.faint)
                .lineLimit(1)
        }
    }

    @ViewBuilder private var action: some View {
        if item.switching {
            ProgressView().controlSize(.small)
        } else if item.inUse, item.action == nil {
            Text("ON TRACK")
                .font(PitWall.mono(9.5, .heavy))
                .tracking(1.2)
                .foregroundStyle(PitWall.flag)
        } else if item.switchesTo, let action = item.action {
            if (item.room ?? 1) > 0 {
                Button("BOX") { model.send(action.intent) }
                    .buttonStyle(BoxButtonStyle())
                    .disabled(model.switchUnderWay != nil)
                    .accessibilityLabel(action.spoken)
            } else {
                resetChip
            }
        } else if let action = item.action {
            Button(item.needsSignIn ? "LOGIN" : Self.boxWord(action.title)) {
                model.send(action.intent)
            }
            .buttonStyle(BoxButtonStyle(lit: false))
            .accessibilityLabel(action.spoken)
        }
    }

    /// An account with nothing left counts down to when it has some again.
    @ViewBuilder private var resetChip: some View {
        if let row = item.tightest,
            let resets = PitWallReset.at(row, of: item, in: model.status)
        {
            TimelineView(.periodic(from: .now, by: 30)) { context in
                Text(PitWallReset.short(resets - Int64(context.date.timeIntervalSince1970)))
                    .font(PitWall.mono(10, .heavy))
                    .tracking(1)
                    .foregroundStyle(PitWall.dim)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(PitWall.chip, in: .rect(cornerRadius: 3))
            }
            .help(row.resets)
        }
    }

    /// "Name…" as a pit board writes it: "NAME".
    static func boxWord(_ title: String) -> String {
        title.replacingOccurrences(of: "…", with: "").uppercased()
    }
}
