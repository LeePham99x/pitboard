import Foundation
import PitboardKit

/// What this app kept in UserDefaults before the model kept it in files of its own: the
/// preferences, now in `app.json` in Pitboard's directory, and the account windows' records,
/// now in `windows.json` in the app's own folder. Each is handed to the model once, and taken
/// out of UserDefaults once its file is there.
///
/// Which of the two wins, and what an old key means, is the model's: where the file is there
/// it reads that and nothing else. So the keys are kept until the file is there, and a launch
/// that stops before the model wrote it hands them over again.
struct EarlierStore {
    let defaults: UserDefaults
    let appFile: URL
    let windowsFile: URL

    init(defaults: UserDefaults, environment: [String: String], windowsDirectory: URL) {
        self.init(
            defaults: defaults,
            appFile: URL(fileURLWithPath: pitboardDirectory(environment: environment))
                .appendingPathComponent("app.json"),
            windowsFile: windowsDirectory.appendingPathComponent("windows.json"))
    }

    init(defaults: UserDefaults, appFile: URL, windowsFile: URL) {
        self.defaults = defaults
        self.appFile = appFile
        self.windowsFile = windowsFile
    }

    func handOver() -> EarlierPreferences? {
        guard !forgetOnceKept() else { return nil }
        guard DefaultsKey.Earlier.all.contains(where: { defaults.object(forKey: $0) != nil })
        else { return nil }
        return EarlierPreferences(
            secondAccountDeclined: defaults.stringArray(
                forKey: DefaultsKey.Earlier.secondAccountDeclined) ?? [],
            hasBeenSeen: defaults.bool(forKey: DefaultsKey.Earlier.hasBeenSeen),
            secondAccountNudgeHidden: defaults.bool(
                forKey: DefaultsKey.Earlier.hideSecondAccountNudge))
    }

    func handOverWindows() -> EarlierWindowRecords? {
        guard !forgetWindowsOnceKept() else { return nil }
        let stores =
            defaults.object(forKey: DefaultsKey.Earlier.webStores) as? [String: [String]]
        let pages =
            defaults.object(forKey: DefaultsKey.Earlier.windowPages)
            as? [String: [String: String]]
        guard stores != nil || pages != nil else { return nil }
        return EarlierWindowRecords(stores: stores ?? [:], pages: pages ?? [:])
    }

    /// Whether `app.json` is there, and so the keys are gone.
    @discardableResult
    func forgetOnceKept() -> Bool {
        forget(DefaultsKey.Earlier.all, onceThere: appFile)
    }

    /// As `forgetOnceKept`, for `windows.json`.
    @discardableResult
    func forgetWindowsOnceKept() -> Bool {
        forget(DefaultsKey.Earlier.windows, onceThere: windowsFile)
    }

    private func forget(_ keys: [String], onceThere file: URL) -> Bool {
        guard FileManager.default.fileExists(atPath: file.path) else { return false }
        for key in keys { defaults.removeObject(forKey: key) }
        return true
    }
}
