import AppKit
import Observation
import SwiftUI

/// What the island shows and how far along it is, which its view draws from.
@MainActor @Observable
final class IslandState {
    var alert: LimitAlert?
    var expanded = false
    /// The Switch button was pressed and the switch has not landed yet.
    var switching = false
    /// The pointer is over the island, which holds it open.
    var hovering = false
    /// Seconds left before the island closes by itself, of `lasting`.
    var remaining: Double = 0
    var lasting: Double = 1
    /// The notch's size, which the island grows from and shrinks back into. A screen with
    /// none gets a pill the menu bar's height.
    var notch = CGSize(width: 190, height: 32)
}

/// An island that grows out of the MacBook's notch to say an account in use is nearly out, as
/// a phone's does: in a borderless panel over the menu bar, on every Space and over full-screen
/// apps, that never makes Pitboard the active app.
@MainActor
final class NotchIsland {
    let state = IslandState()
    /// Asked to make the switch the alert offers.
    var onSwitch: ((LimitAlert) -> Void)?

    private var panel: NSPanel?
    private var timer: Timer?
    private var closing: Task<Void, Never>?

    /// Room around the island for its aura, which a window cannot draw outside of.
    private static let margin = CGSize(width: 70, height: 60)
    static let widest: CGFloat = 460
    static let tallest: CGFloat = 96

    func show(_ alert: LimitAlert) {
        closing?.cancel()
        let panel = self.panel ?? makePanel()
        self.panel = panel
        place(panel)
        state.switching = false
        state.lasting = alert.kind == .switched ? 4 : 12
        state.remaining = state.lasting
        if state.alert != nil, state.expanded {
            withAnimation(.spring(response: 0.42, dampingFraction: 0.8)) { state.alert = alert }
        } else {
            state.alert = alert
            panel.orderFrontRegardless()
            // A frame to lay the collapsed island out at the notch before it grows.
            DispatchQueue.main.async { [state] in
                withAnimation(.spring(response: 0.5, dampingFraction: 0.72)) {
                    state.expanded = true
                }
            }
        }
        panel.ignoresMouseEvents = false
        startTimer()
        NSAccessibility.post(
            element: NSApp as Any, notification: .announcementRequested,
            userInfo: [
                .announcement: "\(alert.title). \(alert.caption). \(alert.detail)",
                .priority: NSAccessibilityPriorityLevel.high.rawValue,
            ])
    }

    /// The Switch button: the switch is the model's, and the island waits to see it land.
    func pressSwitch() {
        guard let alert = state.alert, alert.target != nil else { return }
        state.switching = true
        onSwitch?(alert)
    }

    func dismiss() {
        timer?.invalidate()
        timer = nil
        guard state.expanded || state.alert != nil else { return }
        panel?.ignoresMouseEvents = true
        withAnimation(.spring(response: 0.38, dampingFraction: 0.9)) { state.expanded = false }
        closing = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(450))
            guard let self, !Task.isCancelled else { return }
            self.state.alert = nil
            self.state.switching = false
            self.panel?.orderOut(nil)
        }
    }

    // MARK: - Counting down

    private func startTimer() {
        timer?.invalidate()
        let timer = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        guard !state.hovering, !state.switching else { return }
        state.remaining = max(state.remaining - 0.1, 0)
        if state.remaining <= 0 { dismiss() }
    }

    // MARK: - The panel

    private func makePanel() -> NSPanel {
        let panel = IslandPanel(
            contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.isFloatingPanel = true
        // After `isFloatingPanel`, which sets the level to floating, under the menu bar.
        panel.level = .statusBar
        panel.collectionBehavior = [
            .canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle,
        ]
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.isMovable = false
        panel.isReleasedWhenClosed = false
        let host = IslandHostingView(
            rootView: NotchIslandView(
                state: state,
                onSwitch: { [weak self] in self?.pressSwitch() },
                onClose: { [weak self] in self?.dismiss() }))
        panel.contentView = host
        return panel
    }

    /// Over the notch of the screen that has one, or at the top middle of the main screen.
    private func place(_ panel: NSPanel) {
        guard
            let screen = NSScreen.screens.first(where: { $0.safeAreaInsets.top > 0 })
                ?? NSScreen.main ?? NSScreen.screens.first
        else { return }
        if let left = screen.auxiliaryTopLeftArea, let right = screen.auxiliaryTopRightArea {
            state.notch = CGSize(
                width: screen.frame.width - left.width - right.width,
                height: screen.safeAreaInsets.top)
        } else {
            let menuBar = screen.frame.maxY - screen.visibleFrame.maxY
            state.notch = CGSize(width: 190, height: max(menuBar, 24))
        }
        let size = CGSize(
            width: Self.widest + 2 * Self.margin.width,
            height: state.notch.height + Self.tallest + Self.margin.height)
        panel.setFrame(
            CGRect(
                x: screen.frame.midX - size.width / 2, y: screen.frame.maxY - size.height,
                width: size.width, height: size.height),
            display: true)
    }
}

/// A panel that may sit over the menu bar, which AppKit otherwise moves a window below.
private final class IslandPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        frameRect
    }
}

/// Its buttons work on the first click, though the panel never makes Pitboard active.
private final class IslandHostingView: NSHostingView<NotchIslandView> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
