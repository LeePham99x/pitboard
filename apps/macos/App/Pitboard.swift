import PitboardApp
import SwiftUI

/// `Main` starts it.
struct Pitboard: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var updater = SparkleUpdater()

    var body: some Scene {
        PitboardScenes(delegate: delegate, updates: updater)
    }
}
