import SwiftUI

/// The headline reset counted down each second in the pit board's yellow, or dashes in grey
/// while no account in use says when it resets.
struct PitWallCountdown: View {
    let headline: PitWallHeadline?
    var size: CGFloat = 26

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let now = Int64(context.date.timeIntervalSince1970)
            Text(headline.map { PitWallReset.countdown($0.resets - now) } ?? "--:--:--")
                .font(PitWall.mono(size, .bold))
                .foregroundStyle(headline == nil ? PitWall.faint : PitWall.flag)
                .shadow(color: PitWall.flag.opacity(headline == nil ? 0 : 0.35), radius: 6)
                .contentTransition(.numericText(countsDown: true))
                .monospacedDigit()
        }
    }
}
