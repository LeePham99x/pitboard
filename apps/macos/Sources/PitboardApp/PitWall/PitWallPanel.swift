import AppKit
import PitboardKit
import SwiftUI

/// What the menu bar item opens: a timing tower for each tool, the account on track first and
/// the rest ranked by what they have left, each limit as a row of LEDs, and BOX to switch.
/// Everything that needs typing or room still happens in the window, which the panel's own
/// buttons open.
struct PitWallPanel: View {
    let model: AppModel
    let windows: AccountWindows
    let updates: any Updates
    @Environment(\.openURL) private var openURL
    @Environment(\.openSettings) private var openSettings
    @State private var window: NSWindow?

    static let width: CGFloat = 384
    /// Past this the towers scroll, so the panel still clears the Dock on a 768-point screen.
    static let tallestTowers: CGFloat = 520

    /// Whether the towers are too tall to show whole: a row is about 50 points and a tool's
    /// heading 30, and a panel cannot ask a scroll view how tall its content would be.
    static func scrolls(_ sections: [AccountSection]) -> Bool {
        let rows = sections.reduce(0) { $0 + $1.accounts.count }
        return CGFloat(rows) * 50 + CGFloat(sections.count) * 30 > tallestTowers
    }

    var body: some View {
        VStack(spacing: 0) {
            PitWallHeader(model: model)
            attention
            if Self.scrolls(model.sections) {
                ScrollView { towers.padding(.bottom, 4) }
                    .frame(height: Self.tallestTowers)
            } else {
                towers.padding(.bottom, 4)
            }
            footer
        }
        .frame(width: Self.width)
        .background(PitWall.board)
        .environment(\.colorScheme, .dark)
        .background(PanelWindowReader { window = $0 })
        // A panel says nothing when it opens, and the glance is what the app exists for.
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.didBecomeKeyNotification))
        { note in
            if let window, note.object as? NSWindow === window { model.send(.glanced) }
        }
    }

    // MARK: - What needs attention

    @ViewBuilder private var attention: some View {
        let notices = model.menuNotices
        let waiting = updates.available ? updates.waiting : nil
        if notices.install != nil || !notices.switches.isEmpty || notices.others != nil
            || waiting != nil
        {
            VStack(spacing: 0) {
                if let install = notices.install {
                    PitWallNotice(entry: install, symbol: "questionmark.circle") {
                        if let link = install.link.flatMap(URL.init(string:)) { openURL(link) }
                    }
                }
                ForEach(Array(notices.switches.enumerated()), id: \.offset) { _, advice in
                    PitWallNotice(
                        entry: advice, symbol: advice.severity?.symbol ?? Symbol.switchAccount
                    ) {
                        if let intent = advice.intent { model.send(intent) }
                    }
                }
                if let others = notices.others {
                    PitWallNotice(entry: others, symbol: (others.severity ?? .warning).symbol) {
                        if let intent = others.intent { model.send(intent) }
                    }
                }
                if let waiting {
                    PitWallNotice(
                        title: "Install Pitboard \(waiting)…", subtitle: nil,
                        symbol: Symbol.update, enabled: true
                    ) { updates.check() }
                }
            }
            .padding(.vertical, 4)
            .overlay(alignment: .bottom) { PitWall.rule.frame(height: 1) }
        }
    }

    // MARK: - Towers

    @ViewBuilder private var towers: some View {
        if let note = model.menuAccountsNote {
            Text(note)
                .font(PitWall.mono(11))
                .foregroundStyle(PitWall.dim)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .padding(.horizontal, 20)
                .padding(.vertical, 28)
        } else {
            VStack(spacing: 0) {
                ForEach(model.sections, id: \.id) { section in
                    let provider = section.accounts.first?.provider ?? ""
                    ToolHeading(
                        provider: provider,
                        name: section.heading ?? PitWall.toolName(provider)
                    )
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 14)
                    .padding(.top, 10)
                    .padding(.bottom, 6)
                    ForEach(PitWallOrder.tower(section.accounts), id: \.item.id) { entry in
                        PitWallPanelRow(
                            item: entry.item, position: entry.position, model: model
                        )
                    }
                }
            }
        }
    }

    // MARK: - Footer

    private var footer: some View {
        HStack(spacing: 6) {
            Button("⌘N ADD") {
                model.send(.presentSheet(sheet: .add(provider: nil)))
                close()
            }
            .keyboardShortcut("n")
            Button("⌘R REFRESH") { model.send(.refresh(asked: true)) }
                .keyboardShortcut("r")
                .disabled(model.reading)
            Button("⌘0 OPEN") {
                model.send(.showWindow(pane: nil))
                close()
            }
            .keyboardShortcut("0")
            Button("⌘, SETTINGS") {
                // Choosing from a menu bar item does not make the app active, and settings
                // already open would come forward behind the app in front.
                windows.presence.activate()
                openSettings()
                close()
            }
            .keyboardShortcut(",")
            Spacer(minLength: 0)
            more
        }
        .buttonStyle(BoxButtonStyle(lit: false, compact: true))
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .overlay(alignment: .top) { PitWall.rule.frame(height: 1) }
    }

    private var more: some View {
        Menu {
            if !windows.menus.isEmpty {
                Section { SiteMenuItems(windows: windows) }
            }
            if updates.available {
                Button("Check for Updates…") { updates.check() }
            }
            Button("About Pitboard") { AppVersion.showAboutPanel() }
            Link("Pitboard Help", destination: Links.documentation)
            Divider()
            Button("Quit Pitboard") { NSApp.terminate(nil) }
                .keyboardShortcut("q")
        } label: {
            Image(systemName: "ellipsis")
                .font(.system(size: 11, weight: .bold))
                .foregroundStyle(PitWall.dim)
                .frame(width: 26, height: 20)
                .background(PitWall.chip, in: .rect(cornerRadius: 3))
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .accessibilityLabel("More")
    }

    private func close() {
        window?.close()
    }
}

/// The panel's header: what the tool in use resets in, counted down in the pit board's yellow,
/// and when the numbers were read.
private struct PitWallHeader: View {
    let model: AppModel

    var body: some View {
        let headline = PitWallHeadline.of(model)
        HStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 4) {
                PitWallCaption(headline.map { "Pitboard · \($0.caption)" } ?? "Pitboard")
                PitWallCountdown(headline: headline)
            }
            Spacer()
            PitWallUpdated(model: model)
        }
        .padding(.horizontal, 14)
        .padding(.top, 14)
        .padding(.bottom, 12)
        .overlay(alignment: .bottom) { PitWall.rule.frame(height: 1) }
        .accessibilityElement(children: .combine)
    }
}

/// When the numbers were read, or that they are being read.
struct PitWallUpdated: View {
    let model: AppModel

    var body: some View {
        VStack(alignment: .trailing, spacing: 3) {
            PitWallCaption(model.reading ? "Reading" : "Updated")
            Group {
                if model.reading {
                    ProgressView().controlSize(.mini)
                } else if let at = model.updatedAt {
                    Text(
                        Date(timeIntervalSince1970: TimeInterval(at)),
                        format: .dateTime.hour().minute())
                } else {
                    Text("—")
                }
            }
            .font(PitWall.mono(12, .bold))
            .foregroundStyle(PitWall.ink)
        }
    }
}

/// Something that needs attention, above the towers, in amber.
private struct PitWallNotice: View {
    let title: String
    let subtitle: String?
    let symbol: String
    let enabled: Bool
    let perform: () -> Void

    init(
        title: String, subtitle: String?, symbol: String, enabled: Bool,
        perform: @escaping () -> Void
    ) {
        self.title = title
        self.subtitle = subtitle
        self.symbol = symbol
        self.enabled = enabled
        self.perform = perform
    }

    init(entry: MenuEntry, symbol: String, perform: @escaping () -> Void) {
        self.init(
            title: entry.title, subtitle: entry.subtitle, symbol: symbol,
            enabled: entry.enabled, perform: perform)
    }

    var body: some View {
        Button(action: perform) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Image(systemName: symbol)
                    .foregroundStyle(PitWall.amber)
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(PitWall.ink)
                    if let subtitle {
                        Text(subtitle)
                            .font(.system(size: 11))
                            .foregroundStyle(PitWall.dim)
                    }
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 6)
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }
}

/// Hands over the window a view is in, once it is in one.
struct PanelWindowReader: NSViewRepresentable {
    let found: (NSWindow) -> Void

    func makeNSView(context: Context) -> NSView { WindowSpy(found: found) }
    func updateNSView(_ view: NSView, context: Context) {}

    private final class WindowSpy: NSView {
        let found: (NSWindow) -> Void

        init(found: @escaping (NSWindow) -> Void) {
            self.found = found
            super.init(frame: .zero)
        }

        required init?(coder: NSCoder) { nil }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let window { found(window) }
        }
    }
}

enum Links {
    static let documentation = URL(string: "https://docs.usepitboard.com")!
}
