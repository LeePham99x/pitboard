import PitboardKit
import SwiftUI

/// The pit wall: how the menu bar panel and the accounts window look. A timing tower's
/// near-black board, figures in a monospaced face, a limit as a row of LEDs, and the yellow
/// of a pit board for what can be pressed. Colour still only repeats what a word or a shape
/// says: a limit's LEDs take its level's colour and its figure is written beside them.
enum PitWall {
    static let board = Color(red: 0.035, green: 0.039, blue: 0.047)
    static let raised = Color(red: 0.051, green: 0.055, blue: 0.067)
    static let rule = Color(red: 0.110, green: 0.118, blue: 0.141)
    static let unlit = Color(red: 0.122, green: 0.133, blue: 0.161)
    static let chip = Color(red: 0.114, green: 0.122, blue: 0.145)
    static let ink = Color(red: 0.914, green: 0.914, blue: 0.918)
    static let dim = Color(red: 0.541, green: 0.557, blue: 0.592)
    static let faint = Color(red: 0.435, green: 0.451, blue: 0.486)
    /// The pit board's yellow: the account on track, and what switches.
    static let flag = Color(red: 1, green: 0.839, blue: 0.039)
    static let amber = Color(red: 1, green: 0.702, blue: 0.251)
    static let red = Color(red: 1, green: 0.412, blue: 0.380)

    static func mono(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }

    /// The row of the account in use: lit from its leading edge, as a timing tower marks
    /// the car on track.
    static let onTrack = LinearGradient(
        colors: [flag.opacity(0.11), flag.opacity(0)], startPoint: .leading,
        endPoint: .trailing)

    /// What a tool's dot is filled with beside its name.
    static func toolColor(_ provider: String) -> Color {
        switch provider {
        case "claude", "desktop": Color(red: 0.851, green: 0.467, blue: 0.341)
        default: Color(red: 0.91, green: 0.91, blue: 0.93)
        }
    }

    /// A tool's name, for the words the core leaves to the app: "Claude Code".
    static func toolName(_ provider: String) -> String {
        switch provider {
        case "claude": "Claude Code"
        case "codex": "Codex"
        case "desktop": "Claude Desktop"
        default: provider
        }
    }
}

extension UsageLevel {
    /// The colour a limit's LEDs light in at this step: green, amber, red.
    var led: Color {
        switch self {
        case .plenty: Color(red: 0.188, green: 0.820, blue: 0.345)
        case .low: Color(red: 1, green: 0.624, blue: 0.039)
        case .out: Color(red: 1, green: 0.271, blue: 0.227)
        }
    }

    /// The figure beside the LEDs: plain while there is plenty, then the LEDs' colour.
    var figure: Color {
        switch self {
        case .plenty: PitWall.ink
        case .low: PitWall.amber
        case .out: PitWall.red
        }
    }
}

extension AccountItem {
    /// The most used of its limits, the one that runs out first.
    var tightest: LimitRow? {
        limits.max { $0.percent < $1.percent }
    }

    /// The share of its tightest limit left, or nil while nothing says.
    var room: Double? {
        tightest.map { max(0, 100 - $0.percent) }
    }

    /// "WORK · MAX": the name as a timing tower writes it.
    var towerName: String {
        (plan.map { "\(title) · \($0)" } ?? title).uppercased()
    }
}

/// A limit as a row of LEDs, one lit for each step of it used, glowing in its level's
/// colour, with the mark where an even use would be by now across it.
struct LEDBar: View {
    let percent: Double
    let level: UsageLevel
    var pace: LimitPace?
    var height: CGFloat = 9

    private nonisolated static let segment: CGFloat = 6
    private nonisolated static let gap: CGFloat = 2
    private nonisolated static let overhang: CGFloat = 3

    var body: some View {
        Canvas { context, size in
            let bar = CGRect(
                x: 0, y: Self.overhang, width: size.width,
                height: size.height - 2 * Self.overhang)
            let step = Self.segment + Self.gap
            let count = max(Int((bar.width + Self.gap) / step), 1)
            let used = min(max(percent, 0), 100) / 100
            // Anything used lights at least one, so 1% does not read as none.
            let lit = percent > 0 ? max(Int((used * Double(count)).rounded()), 1) : 0
            func segment(_ index: Int) -> Path {
                Path(
                    roundedRect: CGRect(
                        x: CGFloat(index) * step, y: bar.minY, width: Self.segment,
                        height: bar.height),
                    cornerRadius: 1.5)
            }
            for index in lit..<max(count, lit) {
                context.fill(segment(index), with: .color(PitWall.unlit))
            }
            context.drawLayer { glow in
                glow.addFilter(.shadow(color: level.led.opacity(0.8), radius: 2.5))
                for index in 0..<lit {
                    glow.fill(segment(index), with: .color(level.led))
                }
            }
            if let pace, pace.standing != .even {
                let x = size.width * min(max(pace.expected, 0), 100) / 100
                let mark = CGRect(x: x - 1, y: 0, width: 2, height: size.height)
                context.fill(
                    Path(roundedRect: mark.insetBy(dx: -1.5, dy: 0), cornerRadius: 2),
                    with: .color(PitWall.board))
                context.fill(Path(roundedRect: mark, cornerRadius: 1), with: .color(.white))
            }
        }
        .frame(height: height + 2 * Self.overhang)
        .accessibilityHidden(true)
    }
}

/// What can be pressed on the pit wall: BOX to switch, LOGIN to sign in. Lit in the pit
/// board's yellow when it does something now, and dark when it only says something.
struct BoxButtonStyle: ButtonStyle {
    var lit = true
    /// Tighter, for a row of them along the foot of the panel.
    var compact = false

    func makeBody(configuration: Configuration) -> some View {
        BoxLabel(
            label: configuration.label, lit: lit, compact: compact,
            pressed: configuration.isPressed)
    }
}

private struct BoxLabel<Label: View>: View {
    let label: Label
    let lit: Bool
    let compact: Bool
    let pressed: Bool
    @Environment(\.isEnabled) private var enabled

    var body: some View {
        let shown = lit && enabled
        label
            .font(PitWall.mono(compact ? 9.5 : 10, .heavy))
            .tracking(compact ? 0.4 : 1.2)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, compact ? 6 : 8)
            .padding(.vertical, 4)
            .foregroundStyle(shown ? Color.black : PitWall.dim)
            .background(shown ? PitWall.flag : PitWall.chip, in: .rect(cornerRadius: 3))
            .opacity(pressed ? 0.75 : 1)
            .scaleEffect(pressed ? 0.95 : 1)
            .animation(.easeOut(duration: 0.12), value: pressed)
            .contentShape(.rect)
    }
}

/// A heading on the pit wall: small, spaced capitals in the faint grey.
struct PitWallCaption: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text.uppercased())
            .font(PitWall.mono(9.5))
            .tracking(1.6)
            .foregroundStyle(PitWall.faint)
    }
}

/// A tool's heading over its accounts: its dot and its name.
struct ToolHeading: View {
    let provider: String
    let name: String

    var body: some View {
        HStack(spacing: 8) {
            RoundedRectangle(cornerRadius: 3)
                .fill(PitWall.toolColor(provider))
                .frame(width: 9, height: 9)
            PitWallCaption(name)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(name)
        .accessibilityAddTraits(.isHeader)
    }
}
