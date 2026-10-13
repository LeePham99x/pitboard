import Foundation

/// Where a Pitboard link goes on this Mac: the scheme a build answers, and the app a Share
/// extension is inside. Both are read from bundles, so they stay native.
public enum LinkTarget {
    /// The Info.plist key the app and its Share extension each read their build's scheme from.
    public static let schemeKey = "PitboardURLScheme"

    public static func scheme(in bundle: Bundle) -> String? {
        bundle.object(forInfoDictionaryKey: schemeKey) as? String
    }

    /// `Pitboard.app` for `Pitboard.app/Contents/PlugIns/PitboardShare.appex`, or nil when the
    /// extension is inside no app.
    public static func containingApp(of extensionURL: URL) -> URL? {
        var folder = extensionURL.standardizedFileURL.deletingLastPathComponent()
        while folder.path != "/" && !folder.path.isEmpty {
            if folder.pathExtension == "app" { return folder }
            folder = folder.deletingLastPathComponent()
        }
        return nil
    }
}
