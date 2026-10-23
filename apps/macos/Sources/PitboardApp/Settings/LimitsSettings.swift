import PitboardKit
import SwiftUI

struct LimitsSettings: View {
    let model: AppModel
    let alerts: LimitAlerts
    @AppStorage(DefaultsKey.limitAlertOn) private var alertOn = true
    @AppStorage(DefaultsKey.limitAlertAt) private var alertAt = LimitAlerts.defaultThreshold

    var body: some View {
        let auto = model.machine.autoSwitch
        let warm = model.machine.warm
        Form {
            Section {
                Toggle("Alert at the notch", isOn: $alertOn)
                    .accessibilityIdentifier("settings.limitAlert")
                Stepper(
                    "Alert at \(alertAt)% used",
                    value: $alertAt, in: LimitAlerts.thresholds
                )
                .accessibilityIdentifier("settings.limitAlertAt")
                .disabled(!alertOn)
                LabeledContent("Island") {
                    Button("Show Preview") { alerts.rehearse(model) }
                        .accessibilityIdentifier("settings.limitAlertPreview")
                }
            } header: {
                Text("Limit alert")
            } footer: {
                Text(
                    "When an account in use passes this share of a limit, and again when it runs out, an island grows from the notch with the account that has the most left to switch to."
                )
                .footnote()
            }

            Section {
                Toggle(
                    "Switch Claude Code automatically",
                    isOn: Binding(
                        get: { auto.on },
                        set: { model.send(.setAutoSwitch(on: $0, at: auto.at)) })
                )
                .accessibilityIdentifier("settings.autoSwitch")
                .disabled(!auto.enabled)
                Stepper(
                    auto.atLabel,
                    value: Binding(
                        get: { Int(auto.at) },
                        set: { model.send(.setAutoSwitch(on: auto.on, at: UInt8($0))) }),
                    in: Int(auto.lowest)...Int(auto.highest)
                )
                .accessibilityIdentifier("settings.autoSwitchAt")
                .disabled(!auto.enabled || !auto.on)
                if let standing = auto.standing {
                    Text(standing)
                        .explanatory()
                        .accessibilityIdentifier("settings.autoSwitchStanding")
                }
            } header: {
                Text("Before an account runs out")
            } footer: {
                Text(auto.note).footnote()
            }

            Section {
                DatePicker(
                    "From",
                    selection: timeOfDay(warm.from) {
                        model.send(.setWarmRange(from: $0, to: warm.to))
                    },
                    displayedComponents: .hourAndMinute
                )
                .accessibilityIdentifier("settings.warmFrom")
                DatePicker(
                    "To",
                    selection: timeOfDay(warm.to) {
                        model.send(.setWarmRange(from: warm.from, to: $0))
                    },
                    displayedComponents: .hourAndMinute
                )
                .accessibilityIdentifier("settings.warmTo")
                if warm.accounts.isEmpty {
                    Text("No Claude Code account is enrolled yet.").explanatory()
                }
                ForEach(warm.accounts, id: \.qualified) { account in
                    Toggle(
                        isOn: Binding(
                            get: { account.on },
                            set: { model.send(.setWarm(qualified: account.qualified, on: $0)) })
                    ) {
                        Text(account.plan.map { "\(account.title) · \($0)" } ?? account.title)
                        if let standing = account.standing {
                            Text(standing)
                        }
                    }
                    .accessibilityIdentifier("settings.warm.\(account.qualified)")
                }
                if let standing = warm.standing {
                    Text(standing)
                        .explanatory()
                        .accessibilityIdentifier("settings.warmStanding")
                }
            } header: {
                Text("Warm up limits")
            } footer: {
                Text(warm.note).footnote()
            }
            .disabled(!warm.enabled)
        }
        .formStyle(.grouped)
    }
}

@MainActor
private func timeOfDay(
    _ minutes: UInt16, set: @escaping @MainActor @Sendable (UInt16) -> Void
) -> Binding<Date> {
    Binding(
        get: {
            // 24:00, the end of a day, shows as midnight.
            let minutes = Int(minutes) % (24 * 60)
            return Calendar.current.date(
                bySettingHour: minutes / 60, minute: minutes % 60, second: 0, of: .now) ?? .now
        },
        set: { date in
            let parts = Calendar.current.dateComponents([.hour, .minute], from: date)
            let picked = (parts.hour ?? 0) * 60 + (parts.minute ?? 0)
            set(UInt16(picked))
        })
}
