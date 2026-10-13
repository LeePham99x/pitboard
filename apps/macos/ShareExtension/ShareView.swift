import SwiftUI

@MainActor
@Observable
final class ShareState {
    enum Phase: Equatable {
        case opening
        case refused(String)
        case failed(String)
    }

    var phase = Phase.opening
}

struct ShareView: View {
    let state: ShareState
    let done: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            switch state.phase {
            case .opening:
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Opening in Pitboard…")
                }
            case .refused(let reason):
                said("Can’t Open This Link", reason)
            case .failed(let reason):
                said("Couldn’t Open Pitboard", reason)
            }
            if state.phase != .opening {
                HStack {
                    Spacer()
                    Button("OK", action: done)
                        .keyboardShortcut(.defaultAction)
                }
            }
        }
        .padding(20)
        .frame(width: 360, alignment: .leading)
    }

    private func said(_ title: String, _ reason: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            Text(reason)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
