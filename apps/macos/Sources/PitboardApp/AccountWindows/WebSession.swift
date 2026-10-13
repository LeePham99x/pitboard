import AppKit
import Foundation
import PitboardKit
import WebKit

@MainActor
@Observable
final class WebSession {
    private(set) var account: WindowAccount
    let policy: NavigationPolicy
    private(set) var page: Page
    var note: WindowNote?
    var removalAsked = false

    @ObservationIgnored private let delegate: PageDelegate
    @ObservationIgnored private let downloads: DownloadCenter
    @ObservationIgnored private let openElsewhere: @MainActor (URL) -> Void
    @ObservationIgnored private let configuration: () -> WKWebViewConfiguration
    /// One at a time: a second replaces the first, as a site starting its sign-in again
    /// expects.
    @ObservationIgnored private(set) var popup: PopupWindow?
    @ObservationIgnored private var loaded: UInt64

    init(
        open: OpenWindow, policy: NavigationPolicy, store: WKWebsiteDataStore,
        environment: WebEnvironment, downloads: DownloadCenter
    ) {
        account = open.account
        self.policy = policy
        self.downloads = downloads
        openElsewhere = environment.openElsewhere
        let configure = environment.configure
        configuration = { Page.configuration(store: store, configure: configure) }
        page = Page(role: .window, configuration: configuration())
        delegate = PageDelegate()
        loaded = open.load.serial
        delegate.attach(to: self, page: page)
        note = open.note.map { WindowNote($0, for: open.account) }
        page.load(URL(string: open.load.url) ?? policy.home)
    }

    /// A rename retitles the window and keeps everything else.
    func update(_ account: WindowAccount) {
        guard account != self.account else { return }
        self.account = account
    }

    /// A link chosen for this window while it is open. Back returns to where the window was.
    func load(_ load: PageLoad) {
        guard load.serial != loaded, let url = URL(string: load.url) else { return }
        loaded = load.serial
        page.load(url)
    }

    func close() {
        popup?.close()
        page.close()
    }

    var store: WKWebsiteDataStore {
        page.webView.configuration.websiteDataStore
    }

    func startOver() {
        page = Page(role: .window, configuration: configuration())
        delegate.attach(to: self, page: page)
        note = openingNote(storeMadeBefore: false).map { WindowNote($0, for: account) }
        page.load(policy.home)
    }

    // MARK: - What the page's delegate hands over

    func request(for action: WKNavigationAction, of page: Page) -> NavigationRequest {
        let target = NavigationTarget(frameIsMain: action.targetFrame?.isMainFrame)
        let frame = Self.askingFrame(of: action)
        let asker: Asker
        switch page.role {
        case .window:
            asker = self.asker(frame)
        case .popup:
            // Only the sign-in window's own page asks for itself; its opener does not.
            let own = frame?.isMainFrame == true && frame?.webView === page.webView
            asker = own ? .signIn : .other
        }
        return NavigationRequest(
            // A window asked for with no address yet comes with an empty one.
            url: (action.request.url ?? URL(string: "about:blank")!).absoluteString,
            target: target,
            clicked: [.linkActivated, .formSubmitted].contains(action.navigationType),
            download: action.shouldPerformDownload,
            asker: asker)
    }

    func asker(_ frame: WKFrameInfo?) -> Asker {
        guard let frame else { return .other }
        let origin = frame.securityOrigin
        return frameAsker(
            policy: policy,
            frame: FrameOrigin(
                mainFrame: frame.isMainFrame, scheme: origin.protocol, host: origin.host,
                port: Int64(origin.port)))
    }

    /// The frame that asked for `action`. WebKit's header says there always is one, and WebKit
    /// leaves it out for a load the app starts (WebKit bug 173235), so it is read as one that
    /// may be missing rather than trusted to be there.
    static func askingFrame(of action: WKNavigationAction) -> WKFrameInfo? {
        action.value(forKey: #keyPath(WKNavigationAction.sourceFrame)) as? WKFrameInfo
    }

    func say(_ kind: WindowNoteKind) {
        note = WindowNote(kind, for: account)
    }

    /// A web page the site sent to the browser without a click is usually a sign-in to connect
    /// something, which the browser connects to whichever account of the site it is signed in
    /// to: hence the note.
    func handOver(_ url: String, note: WindowNoteKind?) {
        guard let url = URL(string: url) else { return }
        openElsewhere(url)
        if let note { say(note) }
    }

    /// The sign-in window's page must be made from the `configuration` WebKit hands over, a
    /// copy of the opener's: that is what shares the account's store and keeps
    /// `window.opener`.
    func openPopup(
        configuration: WKWebViewConfiguration, features: WKWindowFeatures,
        over parent: NSWindow?
    ) -> WKWebView {
        popup?.close()
        let page = Page(role: .popup, configuration: configuration)
        let size = signInWindowSize(
            width: features.width?.doubleValue, height: features.height?.doubleValue)
        let opened = PopupWindow(
            page: page, session: self, size: CGSize(width: size.width, height: size.height),
            over: parent)
        opened.onClose = { [weak self, weak opened] in
            if self?.popup === opened { self?.popup = nil }
        }
        popup = opened
        return page.webView
    }

    func closePopup() {
        popup?.close()
    }

    func download(_ download: WKDownload, ask: Bool) {
        downloads.start(download, for: account, asking: ask)
    }
}
