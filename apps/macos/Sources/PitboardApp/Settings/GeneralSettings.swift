import AppKit
import SwiftUI

struct GeneralSettings: View {
    let openAtLogin: OpenAtLogin
    @AppStorage(DefaultsKey.menuBarShows) private var shows = MenuBarShows.nameAndUsage

    var body: some View {
        Form {
            Section {
                Toggle(
                    "Open Pitboard at login",
                    isOn: Binding(
                        get: { openAtLogin.state != .disabled },
                        set: { openAtLogin.set($0) })
                )
                .accessibilityIdentifier("settings.openAtLogin")
                if openAtLogin.state == .requiresApproval {
                    LabeledContent {
                        Button("Open Login Items Settings…") {
                            openAtLogin.openSystemSettings()
                        }
                    } label: {
                        Text("macOS is waiting for you to allow Pitboard in Login Items.")
                            .explanatory()
                    }
                }
                if let failed = openAtLogin.failed {
                    Text(failed).explanatory()
                }
                Picker("Menu bar shows", selection: $shows) {
                    ForEach(MenuBarShows.allCases) { option in
                        Text(option.title).tag(option)
                    }
                }
            }
        }
        .formStyle(.grouped)
        .task {
            openAtLogin.read()
            // Login Items approval happens in System Settings; coming back reruns no `.task`.
            for await _ in NotificationCenter.default.notifications(
                named: NSApplication.didBecomeActiveNotification)
            {
                openAtLogin.read()
            }
        }
    }
}
