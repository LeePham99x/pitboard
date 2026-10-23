import PitboardKit
import SwiftUI

/// The window's sidebar on the pit wall: each pane a word in spaced capitals, the one shown
/// lit in the pit board's yellow, and at its foot whether Pitboard switches by itself and
/// where the island at the notch speaks.
struct PitWallSidebar: View {
    @Binding var pane: WindowPane
    let model: AppModel
    @AppStorage(DefaultsKey.limitAlertOn) private var alertOn = true
    @AppStorage(DefaultsKey.limitAlertAt) private var alertAt = LimitAlerts.defaultThreshold

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(WindowPane.allCases) { shown in
                Button {
                    pane = shown
                } label: {
                    Text(shown.title.uppercased())
                        .font(PitWall.mono(11.5, shown == pane ? .heavy : .medium))
                        .tracking(1.6)
                        .foregroundStyle(shown == pane ? Color.black : PitWall.dim)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 7)
                        .background(
                            shown == pane ? PitWall.flag : Color.clear,
                            in: .rect(cornerRadius: 4)
                        )
                        .contentShape(.rect)
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier("sidebar.\(shown.rawValue)")
                .accessibilityAddTraits(shown == pane ? .isSelected : [])
            }
            Spacer()
            footing
        }
        .padding(.horizontal, 10)
        .padding(.top, 8)
        .padding(.bottom, 12)
        .frame(maxHeight: .infinity, alignment: .top)
        .background(PitWall.board)
    }

    private var footing: some View {
        let auto = model.machine.autoSwitch
        return VStack(alignment: .leading, spacing: 6) {
            line("Auto-switch", auto.on ? "On · \(auto.at)%" : "Off", lit: auto.on)
            line("Alert", alertOn ? "\(alertAt)%" : "Off", lit: alertOn)
        }
        .padding(10)
        .background(PitWall.raised, in: .rect(cornerRadius: 5))
        .overlay(RoundedRectangle(cornerRadius: 5).stroke(PitWall.rule))
        .accessibilityElement(children: .combine)
    }

    private func line(_ name: String, _ value: String, lit: Bool) -> some View {
        HStack {
            PitWallCaption(name)
            Spacer(minLength: 4)
            Text(value.uppercased())
                .font(PitWall.mono(10, .bold))
                .foregroundStyle(lit ? PitWall.flag : PitWall.faint)
        }
    }
}
