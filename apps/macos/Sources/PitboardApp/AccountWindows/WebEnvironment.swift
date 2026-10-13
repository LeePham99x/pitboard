import AppKit
import Foundation
import PitboardKit
import WebKit

/// One value, so a launch decides once which world the windows run in: the sites, or a
/// fixture's stand-in pages that reach no network and keep nothing on disk.
@MainActor
struct WebEnvironment {
    let scheme: String
    let stores: any WebsiteDataStores
    let downloads: URL
    let openElsewhere: @MainActor (URL) -> Void
    let configure: @MainActor (WKWebViewConfiguration) -> Void
    let pause: @MainActor (Duration) async -> Void

    static func live() -> WebEnvironment {
        let home = FileManager.default.homeDirectoryForCurrentUser
        return WebEnvironment(
            scheme: "https",
            stores: WebKitDataStores(),
            downloads: FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)
                .first ?? home.appendingPathComponent("Downloads"),
            openElsewhere: { NSWorkspace.shared.open($0) },
            configure: { _ in },
            pause: { try? await Task.sleep(for: $0) })
    }

    /// The core's directory, standardised as Foundation standardises a file URL: the records
    /// were keyed so before the core said where the directory is. The core gives the path as
    /// the environment does, so a `PITBOARD_HOME` holding a `.`, a `..` or a trailing `/`
    /// would key them differently and orphan every store and page recorded.
    nonisolated static func recordKey(environment: [String: String]) -> String {
        let directory = URL(fileURLWithPath: pitboardDirectory(environment: environment))
        return directory.standardizedFileURL.path
    }

    /// Where the model keeps `windows.json`: under the person's own Library whatever `HOME`
    /// says, as WebKit keeps the stores it describes. A debug build and a release have bundle
    /// ids, stores and records of their own.
    nonisolated static func recordsDirectory(bundle: Bundle = .main) -> URL {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)
            .first
            ?? FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support")
        return support.appendingPathComponent(
            bundle.bundleIdentifier ?? "com.usepitboard.Pitboard", isDirectory: true)
    }
}
