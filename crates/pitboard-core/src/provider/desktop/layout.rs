use crate::provider::folder::Class;
use std::ffi::OsStr;

// Read from a listing of a data folder under 2.7032.0 and the items E2 moved under 2.19675.0.
const ACCOUNT: &[&str] = &[
    "Cookies",
    "Cookies-journal",
    "Local Storage",
    "Session Storage",
    "IndexedDB",
    "WebStorage",
    "File System",
];

const STAYS: &[&str] = &[
    "config.json",
    "config.json.journal",
    "claude-code-sessions",
    "local-agent-mode-sessions",
    "spaces-present",
    "fcache",
    "plan-usage-history.json",
    "cowork-enabled-cli-ops.json",
    "ant-device-registry.json",
    "git-worktrees.json",
    "vm-support-probe.json",
    "extensions-blocklist.json",
    "ca-bundle.pem",
    "declarative_performance_observer.db",
    "ant-did",
    "claude_desktop_config.json",
    "claude-code",
    "window-state.json",
    "Local State",
    "Preferences",
    "Crashpad",
    "sentry",
    "Cache",
    "Code Cache",
    "GPUCache",
    "DawnGraphiteCache",
    "DawnWebGPUCache",
    "blob_storage",
    "Shared Dictionary",
    "DIPS",
    "Network Persistent State",
    "TransportSecurity",
    "Trust Tokens",
    "Claude Extensions",
    "Claude Extensions Settings",
    "extensions-installations.json",
    ".DS_Store",
];

pub(super) fn classify(name: &OsStr) -> Class {
    let Some(name) = name.to_str() else {
        return Class::Unread;
    };
    if ACCOUNT.contains(&name) {
        Class::Account
    } else if STAYS.contains(&name) {
        Class::Stays
    } else if passing(name) {
        Class::Passing
    } else {
        Class::Unread
    }
}

fn passing(name: &str) -> bool {
    crate::atomic::temp_owner(name).is_some()
        || name
            .strip_prefix("config.json.corrupt-")
            .is_some_and(|digits| !digits.is_empty() && digits.bytes().all(|b| b.is_ascii_digit()))
        || name
            .strip_prefix(".org.chromium.Chromium.")
            .is_some_and(|rest| rest.chars().count() == 6)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_name_classifies_as_its_row_says() {
        // Written out, not read from the tables `classify` reads: a name dropped there fails.
        let account = [
            "Cookies",
            "Cookies-journal",
            "Local Storage",
            "Session Storage",
            "IndexedDB",
            "WebStorage",
            "File System",
        ];
        let stays = [
            "config.json",
            "config.json.journal",
            "claude-code-sessions",
            "local-agent-mode-sessions",
            "spaces-present",
            "fcache",
            "plan-usage-history.json",
            "cowork-enabled-cli-ops.json",
            "ant-device-registry.json",
            "git-worktrees.json",
            "vm-support-probe.json",
            "extensions-blocklist.json",
            "ca-bundle.pem",
            "declarative_performance_observer.db",
            "ant-did",
            "claude_desktop_config.json",
            "claude-code",
            "window-state.json",
            "Local State",
            "Preferences",
            "Crashpad",
            "sentry",
            "Cache",
            "Code Cache",
            "GPUCache",
            "DawnGraphiteCache",
            "DawnWebGPUCache",
            "blob_storage",
            "Shared Dictionary",
            "DIPS",
            "Network Persistent State",
            "TransportSecurity",
            "Trust Tokens",
            "Claude Extensions",
            "Claude Extensions Settings",
            "extensions-installations.json",
            ".DS_Store",
        ];
        for name in account {
            assert_eq!(classify(OsStr::new(name)), Class::Account, "{name}");
        }
        for name in stays {
            assert_eq!(classify(OsStr::new(name)), Class::Stays, "{name}");
        }
        assert_eq!((account.len(), ACCOUNT.len()), (7, 7));
        assert_eq!((stays.len(), STAYS.len()), (37, 37));
        for name in [
            ".config.json.4242.1.pitboard",
            ".config.json.4242.pitboard",
            "config.json.corrupt-1791000000123",
            ".org.chromium.Chromium.a1B2c3",
        ] {
            assert_eq!(classify(OsStr::new(name)), Class::Passing, "{name}");
        }
        for name in [
            "Partitions",
            "Service Worker",
            "session-popouts.json",
            "cowork-plugin-oauth.json",
            "cowork-plugin-env.json",
            "ccd-environment-config.json",
            "ccd-session-presets.json",
            "buddy-tokens.json",
            "developer_settings.json",
            "configLibrary",
            "Network",
            "Cookies-wal",
            "Cookies-shm",
            "SingletonLock",
            "SingletonSocket",
            "SingletonCookie",
            "config.json.corrupt-",
            "config.json.corrupt-12a",
            ".org.chromium.Chromium.abc",
            "something-new",
        ] {
            assert_eq!(classify(OsStr::new(name)), Class::Unread, "{name}");
        }
    }
}
