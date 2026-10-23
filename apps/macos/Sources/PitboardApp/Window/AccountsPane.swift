import AppKit
import PitboardKit
import SwiftUI

struct AccountsPane: View {
    let model: AppModel
    let windows: AccountWindows
    @State private var selection: String?
    /// A question the model asks before something that cannot be undone, and what to send
    /// once it is answered.
    @State private var asking: Asking?
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(spacing: 0) {
            TimingHeader(model: model)
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(PitWall.board)
        .navigationTitle("Accounts")
        .navigationSubtitle(model.updatedWindow)
        .focusedSceneValue(
            \.refresh,
            RefreshCommand(title: "Refresh", disabled: model.reading) {
                model.send(.refresh(asked: true))
            }
        )
        .task { model.send(.paneShown(pane: .accounts)) }
        .alert(
            asking?.question.title ?? "",
            isPresented: Binding(
                get: { asking != nil }, set: { if !$0 { asking = nil } }),
            presenting: asking
        ) { asked in
            Button(asked.question.confirm, role: .destructive) { model.send(asked.intent) }
            Button("Cancel", role: .cancel) {}
        } message: { asked in
            Text(asked.question.message)
        }
    }

    @ViewBuilder private var content: some View {
        switch model.accountsShown {
        case .noTool(let title, let detail, let linkTitle, let link):
            ContentUnavailableView {
                Label(title, systemImage: Symbol.terminal)
            } description: {
                Text(detail)
            } actions: {
                if let link = URL(string: link) {
                    Link(linkTitle, destination: link)
                        .buttonStyle(.borderedProminent)
                }
            }
        case .readFailed(let title, let detail, let retry):
            // Nothing to list, and the read said why: that is the thing to say, in full,
            // with a way to try again, and not a spinner that never stops.
            ContentUnavailableView {
                Label(title, systemImage: Severity.error.symbol)
            } description: {
                Text(detail)
            } actions: {
                Button(retry.title) { model.send(retry.intent) }
                    .disabled(!retry.enabled)
            }
        case .noAccounts(let title, let detail, let add):
            ContentUnavailableView {
                Label(title, systemImage: Symbol.accounts)
            } description: {
                Text(detail)
            } actions: {
                Button(add.title) { model.send(add.intent) }
                    .buttonStyle(.borderedProminent)
                    .disabled(!add.enabled)
            }
        case .reading(let title):
            ProgressView(title)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .list:
            list
        }
    }

    private var list: some View {
        List(selection: $selection) {
            ForEach(model.notices, id: \.id) { notice in
                NoticeRow(notice: notice) { send($0.intent, askingFirst: $0.confirm) }
                    .padding(.horizontal, 18)
                    .padding(.vertical, 4)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(PitWall.amber.opacity(0.06))
                    .selectionDisabled()
            }
            if let step = model.setup {
                SetupTip(step: step, footing: model.footing) { model.send($0) }
                    .padding(.horizontal, 18)
                    .listRowInsets(EdgeInsets())
            }
            ForEach(model.sections, id: \.id) { section in
                let columns = TowerColumns.of(section)
                TowerHeader(section: section, columns: columns)
                    .listRowInsets(EdgeInsets())
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .selectionDisabled()
                rows(of: section, columns: columns)
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .environment(\.defaultMinListRowHeight, 0)
        .contextMenu(forSelectionType: String.self) { ids in
            if let item = ids.first.flatMap(model.item) {
                menu(for: item)
            }
        } primaryAction: { ids in
            if let action = ids.first.flatMap(model.item)?.action {
                model.send(action.intent)
            }
        }
        .onDeleteCommand {
            if let item = selection.flatMap(model.item) { forget(item) }
        }
    }

    private func rows(of section: AccountSection, columns: [String]) -> some View {
        ForEach(PitWallOrder.tower(section.accounts), id: \.item.id) { entry in
            AccountRow(
                item: entry.item, position: entry.position, columns: columns,
                status: model.status
            ) { model.send($0) }
            .tag(entry.item.id)
            .listRowInsets(EdgeInsets())
            .listRowSeparator(.hidden)
            .listRowBackground(Color.clear)
        }
    }

    // MARK: - What can be done to an account

    @ViewBuilder private func menu(for item: AccountItem) -> some View {
        ForEach(item.offers, id: \.title) { offer in
            if let checked = offer.checked {
                // A check mark beside it, as a menu shows one setting that is on.
                Toggle(
                    offer.title,
                    isOn: Binding(
                        get: { checked },
                        set: { _ in send(offer.intent, askingFirst: offer.confirm) })
                )
                .disabled(!offer.enabled)
            } else {
                Button(offer.title) { send(offer.intent, askingFirst: offer.confirm) }
                    .disabled(!offer.enabled)
            }
        }
        if !item.windows.isEmpty {
            Divider()
            ForEach(item.windows, id: \.window.store) { offer in
                Button(offer.title) {
                    windows.presence.activate()
                    openWindow(id: AccountWindowScene.id, value: offer.window.id)
                }
            }
        }
        if !item.email.isEmpty {
            Divider()
            Button("Copy Email Address") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(item.email, forType: .string)
            }
        }
        if let forget = item.forget {
            Divider()
            Button(forget.title, role: .destructive) {
                send(forget.intent, askingFirst: forget.confirm)
            }
            .disabled(!forget.enabled)
        }
    }

    private func forget(_ item: AccountItem) {
        if let forget = item.forget, forget.enabled {
            send(forget.intent, askingFirst: forget.confirm)
        }
    }

    private func send(_ intent: Intent, askingFirst question: Question?) {
        if let question {
            asking = Asking(question: question, intent: intent)
        } else {
            model.send(intent)
        }
    }
}

private struct Asking: Equatable {
    let question: Question
    let intent: Intent
}

private struct SetupTip: View {
    let step: SetupStep
    let footing: Footing
    let perform: (Intent) -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: Design.iconSpacing) {
            Image(systemName: symbol)
                .foregroundStyle(.tint)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: Design.rowSpacing) {
                Text(step.title).fontWeight(.medium)
                Text(step.detail).explanatory()
                HStack {
                    ForEach(Array(step.actions.enumerated()), id: \.offset) { index, choice in
                        if index == 0 {
                            Button(choice.title) { perform(choice.intent) }
                                .buttonStyle(.borderedProminent)
                                .disabled(!choice.enabled)
                        } else {
                            Button(choice.title) { perform(choice.intent) }
                                .disabled(!choice.enabled)
                        }
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 4)
        .selectionDisabled()
    }

    private var symbol: String {
        if case .unnamed = footing { return "tag" }
        return Symbol.switchAccount
    }
}

/// The accounts pane's header: the reset the tool in use counts down to, when the numbers
/// were read, and the pane's two commands.
private struct TimingHeader: View {
    let model: AppModel

    var body: some View {
        let headline = PitWallHeadline.of(model)
        HStack(alignment: .bottom, spacing: 22) {
            VStack(alignment: .leading, spacing: 2) {
                PitWallCaption("Accounts")
                Text("TIMING")
                    .font(PitWall.mono(20, .semibold))
                    .tracking(1.5)
                    .foregroundStyle(PitWall.ink)
            }
            VStack(alignment: .leading, spacing: 2) {
                PitWallCaption(headline?.caption ?? "No reset yet")
                PitWallCountdown(headline: headline, size: 28)
            }
            Spacer()
            PitWallUpdated(model: model)
            Button("↻ REFRESH") { model.send(.refresh(asked: true)) }
                .buttonStyle(BoxButtonStyle(lit: false))
                .help("Read every account’s usage again")
                .disabled(model.reading)
                .accessibilityLabel("Refresh")
            // Command-N is the window's own command, so it works from every pane.
            Button("+ ADD") { model.send(.presentSheet(sheet: .add(provider: nil))) }
                .buttonStyle(BoxButtonStyle())
                .help("Sign in to another account and park its login")
                .accessibilityLabel("Add Account")
        }
        .padding(.horizontal, 18)
        .padding(.top, 14)
        .padding(.bottom, 12)
        .overlay(alignment: .bottom) { PitWall.rule.frame(height: 1) }
    }
}
