import Foundation
import PitboardKit

// Where an account window's navigations go, and what becomes of a response, are the core's
// rules: `decideNavigation`, `decideResponse` and `frameAsker`. This is WebKit's side of them.

extension NavigationPolicy {
    var home: URL { URL(string: windowHome(policy: self))! }
}

extension NavigationTarget {
    /// From `WKNavigationAction.targetFrame?.isMainFrame`.
    init(frameIsMain: Bool?) {
        switch frameIsMain {
        case nil: self = .newWindow
        case true?: self = .page
        case false?: self = .frame
        }
    }
}
