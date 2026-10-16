import PitboardKit
import SwiftUI

extension View {
    // A window that has a sheet holds back another until it goes, so these show from the sheet.
    func windowAlerts(_ model: AppModel, shown: Bool) -> some View {
        modifier(WindowAlerts(model: model, shown: shown))
    }
}

private struct WindowAlerts: ViewModifier {
    let model: AppModel
    let shown: Bool

    func body(content: Content) -> some View {
        content
            .alert(
                model.failureAlert?.title ?? "",
                isPresented: Binding(
                    get: { shown && model.failureAlert != nil },
                    set: { if !$0 { model.send(.dismissFailure) } }),
                presenting: model.failureAlert
            ) { _ in
                Button("OK") {}
            } message: { alert in
                Text(alert.message)
            }
            // The answer names its account, so the close it also sends may arrive before it.
            .alert(
                quitAsked?.question.title ?? "",
                isPresented: Binding(
                    get: { shown && quitAsked != nil },
                    set: { if !$0 { model.send(.keepAppOpen) } }),
                presenting: quitAsked
            ) { asked in
                Button(asked.question.confirm) {
                    model.send(.quitAndSwitch(qualified: asked.qualified))
                }
                Button("Cancel", role: .cancel) {}
            } message: { asked in
                Text(asked.question.message)
            }
            // Answered as the quit question is: the close it also sends may arrive first.
            .alert(
                model.replaceQuestion?.question.title ?? "",
                isPresented: Binding(
                    get: { shown && model.replaceQuestion != nil },
                    set: { if !$0 { model.send(.keepLogin) } }),
                presenting: model.replaceQuestion
            ) { asked in
                Button(asked.question.confirm) {
                    model.send(.replaceAndSwitch(qualified: asked.qualified))
                }
                Button("Cancel", role: .cancel) {}
            } message: { asked in
                Text(asked.question.message)
            }
    }

    private var quitAsked: QuitAsked? {
        guard let question = model.quitConfirmation, let asked = model.quitQuestion else {
            return nil
        }
        return QuitAsked(question: question, qualified: asked.qualified)
    }
}

private struct QuitAsked: Equatable {
    let question: Question
    let qualified: String
}
