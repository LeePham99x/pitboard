import Foundation
import PitboardKit
import WebKit

/// WebKit's side of the account windows. What they show, and which stores go, is the
/// model's, in `AppModel.accountWindows`: this tells it what WebKit and the windows did, and
/// does what it says.
@MainActor
@Observable
final class AccountWindows {
    @ObservationIgnored let model: AppModel
    @ObservationIgnored let presence: AppPresence
    @ObservationIgnored let janitor: StoreJanitor
    let downloads: DownloadCenter
    @ObservationIgnored private let environment: WebEnvironment

    private(set) var sessions: [UUID: WebSession] = [:]
    /// Windows asked for from outside any view, such as the Dock's menu, which the menu bar
    /// item opens, since only a view can open a window.
    private(set) var requested: [UUID] = []

    init(model: AppModel, environment: WebEnvironment, presence: AppPresence) {
        self.model = model
        self.environment = environment
        self.presence = presence
        janitor = StoreJanitor(stores: environment.stores, pause: environment.pause)
        downloads = DownloadCenter(folder: environment.downloads, model: model)
        model.accountWindowsChanged = { [weak self] shown in self?.follow(shown) }
    }

    var accounts: [WindowAccount] { model.accountWindows.accounts }

    var menus: [SiteMenu] { model.accountWindows.menus }

    func account(_ store: UUID) -> WindowAccount? {
        accounts.first { $0.id == store }
    }

    func opened(_ store: UUID) -> OpenWindow? {
        model.accountWindows.open.first { $0.id == store }
    }

    func isClosing(_ store: UUID) -> Bool {
        model.accountWindows.closing.contains { UUID(uuidString: $0) == store }
    }

    func policy(for site: Site) -> NavigationPolicy {
        NavigationPolicy(site: site, scheme: environment.scheme)
    }

    // MARK: - Windows

    func opening(_ store: UUID) {
        model.send(.windowOpened(store: store.uuidString))
    }

    func session(for open: OpenWindow) -> WebSession {
        if let session = sessions[open.id] {
            session.update(open.account)
            session.load(open.load)
            return session
        }
        let session = WebSession(
            open: open, policy: policy(for: open.account.site),
            store: environment.stores.store(for: open.id), environment: environment,
            downloads: downloads)
        sessions[open.id] = session
        return session
    }

    func closed(_ store: UUID) {
        sessions.removeValue(forKey: store)?.close()
        model.send(.windowClosed(store: store.uuidString))
    }

    func request(_ account: WindowAccount) {
        requested.append(account.id)
    }

    func takeRequests() -> [UUID] {
        defer { requested = [] }
        return requested
    }

    func receive(_ link: URL) {
        model.send(.linkArrived(text: link.absoluteString))
    }

    /// Signs the window out of its site without telling the site.
    ///
    /// The signed-in page leaves first, so nothing it still does, a response setting a cookie
    /// or a page leaving and saving its state, lands in the store after it is cleared, and
    /// Back cannot bring it back.
    func removeWebsiteData(of session: WebSession) async {
        let store = session.store
        session.closePopup()
        await session.page.leave()
        session.close()
        model.send(.websiteDataRemoved(store: session.account.store))
        await janitor.wipe(store)
        session.startOver()
    }

    func remember(_ page: URL?, of account: WindowAccount) {
        guard let page else { return }
        model.send(.pageShown(store: account.store, url: page.absoluteString))
    }

    // MARK: - What the model says

    func follow(_ shown: AccountWindowsShown) {
        for store in shown.closing.compactMap(UUID.init(uuidString:)) {
            sessions.removeValue(forKey: store)?.close()
        }
        let model = model
        janitor.delete(shown.deleting) { store, deleted in
            model.send(
                deleted
                    ? .storeDeleted(store: store.uuidString)
                    : .storeHeld(store: store.uuidString))
        }
    }
}
