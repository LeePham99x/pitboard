import Foundation

/// The updater is Sparkle, which the app target links and this library does not, so the
/// library's tests need no framework that only a signed bundle can load.
@MainActor
public protocol Updates: AnyObject, Observable {
    /// Whether this build can update itself.
    var available: Bool { get }
    /// The version waiting to be installed, once a scheduled check has found one.
    var waiting: String? { get }
    var checksAutomatically: Bool { get set }
    var installsAutomatically: Bool { get set }
    /// Shows the updater's own window: what it finds, what changed, and its install button.
    func check()
}

@MainActor
@Observable
final class NoUpdates: Updates {
    init() {}
    var available: Bool { false }
    var waiting: String? { nil }
    var checksAutomatically = false
    var installsAutomatically = false
    func check() {}
}
