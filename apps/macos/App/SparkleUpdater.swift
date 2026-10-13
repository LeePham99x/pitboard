import AppKit
import Foundation
import Observation
import PitboardApp
import Sparkle

@MainActor
@Observable
final class SparkleUpdater: NSObject, Updates, SPUStandardUserDriverDelegate {
    private(set) var waiting: String?

    @ObservationIgnored private var controller: SPUStandardUpdaterController?

    override init() {
        super.init()
        // A build from a clone carries no update key, and Sparkle will not run without one.
        guard Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") != nil else { return }
        controller = SPUStandardUpdaterController(
            startingUpdater: true, updaterDelegate: nil, userDriverDelegate: self)
    }

    var available: Bool { controller != nil }

    // Sparkle keeps these settings, so a view is told of a change by hand.
    var checksAutomatically: Bool {
        get {
            access(keyPath: \.checksAutomatically)
            return controller?.updater.automaticallyChecksForUpdates ?? false
        }
        set {
            withMutation(keyPath: \.checksAutomatically) {
                controller?.updater.automaticallyChecksForUpdates = newValue
            }
        }
    }

    var installsAutomatically: Bool {
        get {
            access(keyPath: \.installsAutomatically)
            return controller?.updater.automaticallyDownloadsUpdates ?? false
        }
        set {
            withMutation(keyPath: \.installsAutomatically) {
                controller?.updater.automaticallyDownloadsUpdates = newValue
            }
        }
    }

    func check() {
        waiting = nil
        controller?.updater.checkForUpdates()
    }

    // Pitboard is mostly in the menu bar alone, with nowhere to put a window nobody asked
    // for: the menu says a version is ready, and the person decides when to install it.
    nonisolated var supportsGentleScheduledUpdateReminders: Bool { true }

    nonisolated func standardUserDriver(
        _ driver: SPUStandardUserDriver,
        willShowModalAlert alert: NSAlert
    ) {
        // A modal alert from an app with no Dock icon arrives behind everything otherwise.
        Task { @MainActor in NSApp.activate() }
    }

    nonisolated func standardUserDriverWillHandleShowingUpdate(
        _ handleShowingUpdate: Bool,
        forUpdate update: SUAppcastItem,
        state: SPUUserUpdateState
    ) {
        guard !state.userInitiated else { return }
        let version = update.displayVersionString
        Task { @MainActor in self.waiting = version }
    }
}
