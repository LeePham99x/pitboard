import Foundation
import PitboardKit

// Where an account window's navigations go, and what becomes of a response, are the core's
// rules: `decideNavigation`, `decideResponse` and `frameAsker`, given a `NavigationPolicy`,
// the site and the scheme its pages are on. What is left here is WebKit's side of them: its
// URLs, and the target of a navigation as WebKit gives it.

extension NavigationPolicy {
    /// Where a window of the site starts.
    var home: URL { URL(string: windowHome(policy: self))! }
}

extension NavigationTarget {
    /// The target of a navigation whose target frame is main, is not, or is not there at all,
    /// as `WKNavigationAction.targetFrame?.isMainFrame` says.
    init(frameIsMain: Bool?) {
        switch frameIsMain {
        case nil: self = .newWindow
        case true?: self = .page
        case false?: self = .frame
        }
    }
}
