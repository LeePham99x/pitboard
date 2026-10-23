import PitboardKit
import SwiftUI

/// The island: black like the notch it grows from, with an aura in the alert's colour, a ring
/// that pulses with the share used, the account to switch to, and a ring that counts down to
/// the island closing. After a switch it shrinks to a green check.
struct NotchIslandView: View {
    let state: IslandState
    let onSwitch: () -> Void
    let onClose: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// How far the island's top corners curve out into the screen's edge, as the notch's do.
    private static let ear: CGFloat = 8

    var body: some View {
        let alert = state.alert
        let open = state.expanded && alert != nil
        let size =
            open
            ? grown(alert!)
            : CGSize(
                width: state.notch.width + 2 * Self.ear, height: state.notch.height)
        ZStack(alignment: .top) {
            IslandShape(ear: Self.ear, bottom: open ? 28 : 10)
                .fill(.black)
                .frame(width: size.width, height: size.height)
                .shadow(color: aura(alert).opacity(open ? 0.55 : 0), radius: 24, y: 8)
                .overlay(alignment: .top) {
                    if open, let alert {
                        content(alert)
                            .padding(.top, state.notch.height - 2)
                            .padding(.horizontal, Self.ear + 18)
                            .frame(width: size.width, height: size.height, alignment: .top)
                            .transition(
                                reduceMotion
                                    ? .opacity
                                    : .opacity.combined(with: .scale(scale: 0.92, anchor: .top))
                            )
                    }
                }
                .onHover { state.hovering = $0 }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .environment(\.colorScheme, .dark)
        .animation(reduceMotion ? .easeInOut(duration: 0.2) : nil, value: state.expanded)
    }

    private func grown(_ alert: LimitAlert) -> CGSize {
        switch alert.kind {
        case .switched:
            CGSize(width: 340 + 2 * Self.ear, height: state.notch.height + 50)
        case .warning, .out:
            CGSize(
                width: NotchIsland.widest - 20 + 2 * Self.ear, height: state.notch.height + 78)
        }
    }

    private func aura(_ alert: LimitAlert?) -> Color {
        switch alert?.kind {
        case .switched: UsageLevel.plenty.led
        case .out: UsageLevel.out.led
        default: UsageLevel.low.led
        }
    }

    @ViewBuilder private func content(_ alert: LimitAlert) -> some View {
        switch alert.kind {
        case .switched:
            HStack(spacing: 12) {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 24))
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(.black, UsageLevel.plenty.led)
                    .symbolEffect(.bounce, value: alert.id)
                words(alert, captionShown: false)
                Spacer(minLength: 0)
            }
            .frame(maxHeight: .infinity)
            .padding(.bottom, 6)
        case .warning, .out:
            HStack(spacing: 14) {
                PulseRing(
                    percent: alert.percent ?? 0, level: alert.level ?? .low,
                    pulsing: !reduceMotion)
                words(alert, captionShown: true)
                Spacer(minLength: 6)
                if alert.target != nil {
                    Button(action: onSwitch) {
                        Group {
                            if state.switching {
                                ProgressView().controlSize(.small).tint(.white)
                            } else {
                                Text("Switch")
                            }
                        }
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(.white)
                        .frame(minWidth: 52)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 5)
                        .background(Color.accentColor, in: .capsule)
                        .shadow(color: Color.accentColor.opacity(0.5), radius: 8)
                    }
                    .buttonStyle(.plain)
                    .disabled(state.switching)
                    .accessibilityLabel("Switch to \(alert.target?.title ?? "")")
                }
                CountdownRing(
                    remaining: state.remaining, lasting: state.lasting, action: onClose)
            }
            .frame(maxHeight: .infinity)
            .padding(.bottom, 8)
        }
    }

    private func words(_ alert: LimitAlert, captionShown: Bool) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            if captionShown {
                Text(alert.caption)
                    .font(.system(size: 11))
                    .foregroundStyle(.white.opacity(0.55))
            }
            Text(alert.title)
                .font(.system(size: captionShown ? 15 : 14, weight: .semibold))
                .foregroundStyle(.white)
            Text(alert.detail)
                .font(.system(size: captionShown ? 12 : 11.5))
                .foregroundStyle(
                    alert.target != nil ? UsageLevel.plenty.led : .white.opacity(0.55))
        }
        .lineLimit(1)
        .accessibilityElement(children: .combine)
    }
}

/// The share used as a ring in its level's colour, with a wave rolling off it while it waits.
private struct PulseRing: View {
    let percent: Double
    let level: UsageLevel
    let pulsing: Bool
    @State private var wave = false

    var body: some View {
        ZStack {
            if pulsing {
                Circle()
                    .stroke(level.led, lineWidth: 2)
                    .scaleEffect(wave ? 1.45 : 1)
                    .opacity(wave ? 0 : 0.7)
            }
            Circle().stroke(.white.opacity(0.12), lineWidth: 4.5)
            Circle()
                .trim(from: 0, to: min(max(percent, 0), 100) / 100)
                .stroke(level.led, style: StrokeStyle(lineWidth: 4.5, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .shadow(color: level.led.opacity(0.7), radius: 4)
            Text("\(Int(percent.rounded()))%")
                .font(
                    .system(size: percent >= 100 ? 11 : 12.5, weight: .bold, design: .rounded)
                )
                .monospacedDigit()
                .foregroundStyle(.white)
        }
        .frame(width: 46, height: 46)
        .onAppear {
            guard pulsing else { return }
            withAnimation(.easeOut(duration: 1.6).repeatForever(autoreverses: false)) {
                wave = true
            }
        }
        .accessibilityHidden(true)
    }
}

/// The seconds before the island closes by itself, draining; pressing it closes it now.
private struct CountdownRing: View {
    let remaining: Double
    let lasting: Double
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle().stroke(Color.accentColor.opacity(0.25), lineWidth: 2.5)
                Circle()
                    .trim(from: 0, to: max(remaining, 0) / max(lasting, 0.1))
                    .stroke(
                        Color.accentColor, style: StrokeStyle(lineWidth: 2.5, lineCap: .round)
                    )
                    .rotationEffect(.degrees(-90))
                    .animation(.linear(duration: 0.1), value: remaining)
                Text("\(Int(remaining.rounded(.up)))")
                    .font(.system(size: 10.5, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(.white)
            }
            .frame(width: 26, height: 26)
            .contentShape(.circle)
        }
        .buttonStyle(.plain)
        .help("Close")
        .accessibilityLabel("Close")
    }
}

/// The notch's outline grown: a flat top whose corners curve out into the screen's edge, and
/// round corners below.
struct IslandShape: Shape {
    var ear: CGFloat
    var bottom: CGFloat

    var animatableData: CGFloat {
        get { bottom }
        set { bottom = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let ear = min(ear, rect.width / 4, rect.height / 2)
        let bottom = max(min(bottom, (rect.width - 2 * ear) / 2, rect.height - ear), 0)
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addQuadCurve(
            to: CGPoint(x: rect.minX + ear, y: rect.minY + ear),
            control: CGPoint(x: rect.minX + ear, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.minX + ear, y: rect.maxY - bottom))
        path.addQuadCurve(
            to: CGPoint(x: rect.minX + ear + bottom, y: rect.maxY),
            control: CGPoint(x: rect.minX + ear, y: rect.maxY))
        path.addLine(to: CGPoint(x: rect.maxX - ear - bottom, y: rect.maxY))
        path.addQuadCurve(
            to: CGPoint(x: rect.maxX - ear, y: rect.maxY - bottom),
            control: CGPoint(x: rect.maxX - ear, y: rect.maxY))
        path.addLine(to: CGPoint(x: rect.maxX - ear, y: rect.minY + ear))
        path.addQuadCurve(
            to: CGPoint(x: rect.maxX, y: rect.minY),
            control: CGPoint(x: rect.maxX - ear, y: rect.minY))
        path.closeSubpath()
        return path
    }
}
