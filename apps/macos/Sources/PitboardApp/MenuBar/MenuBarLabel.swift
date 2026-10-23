import AppKit
import PitboardKit
import SwiftUI

/// The one view alive from launch to quit, so it is also what opens a window asked for away
/// from any view: the main window whenever the model asks for it, from the menu, from a
/// notification's button, or on the first launch ever, and an account's window from the
/// Dock icon's menu.
struct MenuBarLabel: View {
    let model: AppModel
    let windows: AccountWindows
    let requests: WindowRequests
    let alerts: LimitAlerts
    @Environment(\.openWindow) private var openWindow
    @AppStorage(DefaultsKey.menuBarShows) private var shows = MenuBarShows.nameAndUsage

    var body: some View {
        let title = shows.text(of: model.menuBar)
        // A mark as well as words: on a crowded menu bar macOS drops the widest items
        // first, and an item that is only text is the widest thing up there. A Label would
        // render as the icon alone, so both are placed by hand.
        HStack(spacing: 4) {
            Image(nsImage: MenuBarMark.image)
            if !title.isEmpty {
                Text(title).monospacedDigit()
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(model.menuBar.spoken)
        // As it first appears too: the first launch's request can be in before it is drawn.
        .onChange(of: model.windowRequest.serial, initial: true) {
            guard requests.opens(model.windowRequest) else { return }
            // An app with no Dock icon has nothing to bring forward but itself, and the
            // window opens behind whatever is in front otherwise.
            windows.presence.activate()
            openWindow(id: MainWindow.id)
        }
        // Alive from launch to quit, so the island hears of every change to the accounts.
        .onChange(of: model.sections, initial: true) { alerts.check(model) }
        .onChange(of: model.switchUnderWay) { alerts.check(model) }
        .onChange(of: windows.requested) {
            for store in windows.takeRequests() {
                windows.presence.activate()
                openWindow(id: AccountWindowScene.id, value: store)
            }
        }
    }
}
