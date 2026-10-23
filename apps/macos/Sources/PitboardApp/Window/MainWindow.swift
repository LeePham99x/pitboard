import PitboardKit
import SwiftUI

struct MainWindow: View {
    static let id = "main"

    let model: AppModel
    let windows: AccountWindows
    let requests: WindowRequests
    @SceneStorage("pane") private var pane = WindowPane.accounts

    var body: some View {
        NavigationSplitView {
            PitWallSidebar(pane: $pane, model: model)
                .navigationSplitViewColumnWidth(min: 170, ideal: 190, max: 230)
        } detail: {
            switch pane {
            case .accounts: AccountsPane(model: model, windows: windows)
            case .activity: ActivityPane(model: model)
            case .machine: MachinePane(model: model)
            }
        }
        .frame(minWidth: 960, minHeight: 460)
        .preferredColorScheme(.dark)
        .appWindow(windows.presence)
        // The model keeps the sheet; this one goes however it goes, and the model is told.
        .sheet(
            item: Binding(
                get: { model.sheet },
                set: { if $0 == nil { model.send(.closeSheet) } })
        ) { sheet in
            AccountSheetView(model: model, sheet: sheet)
        }
        .windowAlerts(model, shown: model.sheet == nil)
        // A request for the window can want a pane: a sheet is about accounts, and so is a
        // notice. Asked for when the window opens as well, since a window opened by the
        // request is not there to see it change; once for each request.
        .onChange(of: model.windowRequest.serial, initial: true) {
            pane = requests.pane(for: model.windowRequest, from: pane)
        }
    }
}

/// A sheet the model keeps over the window, told apart from another so that one replacing
/// it is put up afresh.
extension Sheet: Identifiable {
    public var id: String {
        switch self {
        case .add(let provider): "add/\(provider ?? "")"
        case .signInAgain(let provider, let label): "again/\(provider)/\(label)"
        case .name(let provider, _): "name/\(provider)"
        case .rename(let provider, let label): "rename/\(provider)/\(label)"
        case .stow: "stow"
        }
    }
}
