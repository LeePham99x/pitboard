//! Credential slot naming, transcribed from Claude Code: it chooses the keychain service a
//! process sees by hashing a directory path. If Claude Code changes that, only this file
//! changes.

use crate::context::Context;
use crate::host::Os;
use sha2::{Digest, Sha256};
use unicode_normalization::UnicodeNormalization;

/// The service name used when no slot is selected.
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub const LIVE_SERVICE: &str = "Claude Code-credentials";

/// The credential file Claude Code falls back to where its keychain write fails, and the
/// only store it has on Linux.
pub(crate) const CRED_FILE: &str = ".credentials.json";

/// Claude Code's fallback when no usable name can be found at all.
const FALLBACK_ACCOUNT: &str = "claude-code-user";

/// The keychain account name Claude Code stores under.
///
/// Claude Code reads `process.env.USER || os.userInfo().username`, so the passwd entry is
/// what it uses wherever the environment carries no `USER`: a launchd agent, a cron job, an
/// app opened from Finder. Stopping at the literal there would send Pitboard to a different
/// keychain item than the one Claude Code reads.
pub(crate) fn account_name(ctx: &Context) -> String {
    // Its Windows build names its Credential Manager item with the literal alone.
    if ctx.os == Os::Windows {
        return FALLBACK_ACCOUNT.to_string();
    }
    let from_env = ctx.user.as_deref().filter(|u| !u.is_empty());
    match from_env
        .map(str::to_owned)
        .or_else(crate::host::user::login_name)
    {
        Some(name) if is_accepted_account(&name) => name,
        _ => FALLBACK_ACCOUNT.to_string(),
    }
}

/// Claude Code accepts `^[a-zA-Z0-9._-]+$` and falls back to a literal otherwise.
fn is_accepted_account(s: &str) -> bool {
    !s.is_empty()
        && s.bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b'-'))
}

/// The first 8 hex characters of the SHA-256 of the NFC-normalised path string.
///
/// The path is hashed verbatim: no tilde expansion, no symlink resolution, no
/// trailing-slash normalisation. A path that differs by one byte is a different slot.
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub fn dir_hash(dir: &str) -> String {
    let normalised: String = dir.nfc().collect();
    hex::encode(&Sha256::digest(normalised.as_bytes())[..4])
}

/// The keychain service name for the slot selected by `dir`.
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub fn service_for_dir(dir: &str) -> String {
    format!("{LIVE_SERVICE}-{}", dir_hash(dir))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Claude Code reads `process.env.USER || os.userInfo().username`. An app opened from
    /// Finder and a shell must land on the same keychain item, so an absent `USER` has to
    /// reach the passwd entry rather than the literal fallback.
    #[test]
    fn an_absent_user_falls_back_to_the_passwd_name() {
        let Some(expected) = crate::host::user::login_name() else {
            return; // No passwd entry here; the literal is then correct.
        };
        let home = std::path::PathBuf::from("/home/x");
        assert_eq!(account_name(&Context::new(home.clone())), expected);
        assert_eq!(
            account_name(&Context::new(home).with_user(String::new())),
            expected
        );
    }

    /// These three vectors were measured on a real machine on 2026-09-21: each path was
    /// given to Claude Code, and the keychain item it created carried exactly this suffix.
    /// They are the regression test for the whole derivation.
    #[test]
    fn matches_measured_vectors() {
        let cases = [
            ("/Users/ngoquocdat/.claude", "e80beed8"),
            (
                "/private/tmp/claude-501/-Users-ngoquocdat/32ac806a-a8b6-4258-8ba1-548995f8d827/scratchpad/m0/s1",
                "a4bc5512",
            ),
            (
                "/private/tmp/claude-501/-Users-ngoquocdat/32ac806a-a8b6-4258-8ba1-548995f8d827/scratchpad/m0/s2",
                "79d45fb9",
            ),
        ];
        for (path, expected) in cases {
            assert_eq!(dir_hash(path), expected, "derivation drifted for {path}");
        }
    }

    #[test]
    fn builds_the_full_service_name() {
        assert_eq!(
            service_for_dir("/Users/ngoquocdat/.claude"),
            "Claude Code-credentials-e80beed8"
        );
    }

    #[test]
    fn a_trailing_slash_is_a_different_slot() {
        assert_ne!(dir_hash("/Users/x/.claude"), dir_hash("/Users/x/.claude/"));
    }

    // `credman_target`: the string is hashed as given on Windows too, so each spelling of one
    // folder is an item of its own, and the home's own `.claude` named outright is one too.
    #[test]
    fn each_spelling_of_a_windows_folder_is_a_slot_of_its_own() {
        let spelled: std::collections::BTreeSet<String> = [r"C:\x", "C:/x", r"c:\x", r"C:\x\"]
            .into_iter()
            .map(dir_hash)
            .collect();
        assert_eq!(spelled.len(), 4);
        let named = Context::new(std::path::PathBuf::from(r"C:\Users\x"))
            .with_claude_config_dir(r"C:\Users\x\.claude".into());
        assert!(!super::super::paths::is_default_slot(&named));
        assert_eq!(
            super::super::paths::live_service(&named),
            service_for_dir(r"C:\Users\x\.claude")
        );
    }

    #[test]
    fn windows_names_every_persons_item_claude_code_user() {
        let ctx = |os: Os| {
            Context::for_command_line(&crate::context::Environment::on(
                os,
                [
                    ("HOME", "/home/x"),
                    ("USERPROFILE", r"C:\Users\x"),
                    ("USER", "ngoquocdat"),
                    ("USERNAME", "ngoquocdat"),
                ],
            ))
        };
        assert_eq!(account_name(&ctx(Os::Windows)), "claude-code-user");
        assert_eq!(account_name(&ctx(Os::MacOs)), "ngoquocdat");
    }

    #[test]
    fn account_names_are_screened_like_claude_code_screens_them() {
        for good in ["ngoquocdat", "a.b_c-1", "X"] {
            assert!(is_accepted_account(good), "{good} should be accepted");
        }
        for bad in [
            "",
            "has space",
            "quote\"",
            "sl/ash",
            "semi;colon",
            "dollar$",
        ] {
            assert!(!is_accepted_account(bad), "{bad:?} should be rejected");
        }
    }
}
