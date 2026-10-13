import PitboardKit
import SwiftUI

struct RenewalSettings: View {
    let model: AppModel

    var body: some View {
        let schedule = model.machine.schedule
        let renewal = model.machine.renewal
        Form {
            Section {
                Toggle(
                    "Renew parked logins daily",
                    isOn: Binding(
                        get: { schedule.on }, set: { model.send(.setSchedule(on: $0)) })
                )
                .accessibilityIdentifier("settings.renewDaily")
                .disabled(!schedule.enabled)
                if let runs = schedule.runs {
                    LabeledContent("Runs", value: runs)
                }
                if let path = schedule.scheduledIn {
                    LabeledContent("Scheduled in") {
                        Text(path)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }
                }
                if let note = schedule.note {
                    Text(note).explanatory()
                }
                if let failed = schedule.failed {
                    Text(failed).explanatory()
                }
                LabeledContent {
                    Button("Renew Now") { model.send(.renewNow) }
                        .disabled(renewal.renewing)
                } label: {
                    Text(renewal.note)
                }
            } footer: {
                Text(renewal.about).footnote()
            }
        }
        .formStyle(.grouped)
        // A terminal can change the schedule while the app runs.
        .task { model.send(.readSchedule) }
    }
}
