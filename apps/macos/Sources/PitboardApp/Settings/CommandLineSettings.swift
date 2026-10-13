import PitboardKit
import SwiftUI

struct CommandLineSettings: View {
    let model: AppModel
    let link: CommandLineLink

    var body: some View {
        let shown = model.machine.commandLine
        Form {
            Section {
                LabeledContent("In your terminal") {
                    if let found = shown.inTerminal {
                        Text(found).foregroundStyle(.secondary).textSelection(.enabled)
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                if shown.offersLink {
                    LabeledContent {
                        Button("Install Command Line Tool…") {
                            Task { await link.install() }
                        }
                        .disabled(link.linking)
                    } label: {
                        Text(
                            "Links \(link.target) to the one inside this app. macOS asks for "
                                + "an administrator’s password."
                        )
                        .explanatory()
                    }
                } else if let cannot = shown.cannotLink {
                    Text(cannot).explanatory()
                }
                if let failed = link.failed {
                    Text(failed).explanatory()
                }
            } footer: {
                VStack(alignment: .leading, spacing: Design.rowSpacing) {
                    if let note = shown.updateNote {
                        Text(note).footnote()
                    }
                    // One literal, so its code spans are drawn as code.
                    Text(
                        """
                        The command line does everything the app does, and more: \
                        `pitboard status` in a script, and `pitboard repair` for a parked \
                        login Pitboard’s records have lost track of.
                        """
                    )
                    .footnote()
                }
            }
        }
        .formStyle(.grouped)
        .task { model.send(.lookForCommandLine) }
    }
}
