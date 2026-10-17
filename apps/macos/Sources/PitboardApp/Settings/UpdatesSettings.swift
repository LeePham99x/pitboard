import SwiftUI

struct UpdatesSettings: View {
    let updates: any Updates

    var body: some View {
        Form {
            if updates.available {
                Section {
                    Toggle(
                        "Check for updates automatically",
                        isOn: Binding(
                            get: { updates.checksAutomatically },
                            set: { updates.checksAutomatically = $0 }))
                    Toggle(
                        "Download and install updates automatically",
                        isOn: Binding(
                            get: { updates.installsAutomatically },
                            set: { updates.installsAutomatically = $0 })
                    )
                    .disabled(!updates.checksAutomatically)
                    LabeledContent {
                        Button("Check Now") { updates.check() }
                    } label: {
                        Text(
                            updates.waiting.map { "Pitboard \($0) is ready to install." }
                                ?? AppVersion.named())
                    }
                }
            } else {
                Section {
                    LabeledContent("Version", value: AppVersion.named())
                } footer: {
                    // Without this, a build with no update key would look broken.
                    Text(
                        "This copy of Pitboard can’t update itself: it was built from source "
                            + "and carries no update key. A copy from a release keeps itself up "
                            + "to date."
                    )
                    .footnote()
                }
            }
        }
        .formStyle(.grouped)
    }
}
