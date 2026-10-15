//! Where Claude Code keeps things, and who it currently thinks is signed in. Read-only.

use crate::context::Context;
use crate::error::{Error, Result};
use crate::provider::claude::daemon;
use crate::provider::claude::slot;
use serde_json::Value;
use std::path::PathBuf;

/// `CLAUDE_CONFIG_DIR ?? ~/.claude`, as Claude Code 2.1.289 reads it: set, even empty, it
/// is the directory, so an empty one is the empty path, which is refused before anything
/// is read under it ([`crate::home::check_absolute`]).
pub(crate) fn config_dir(ctx: &Context) -> PathBuf {
    ctx.claude_config_dir
        .as_ref()
        .map(PathBuf::from)
        .unwrap_or_else(|| ctx.home.join(".claude"))
}

// As Claude Code chooses it: the register's `config_file_location`.
pub(crate) fn config_file(ctx: &Context) -> PathBuf {
    let file = ctx
        .claude_config_dir
        .as_deref()
        .filter(|dir| !dir.is_empty())
        .map_or_else(|| ctx.home.clone(), PathBuf::from)
        .join(".claude.json");
    let legacy = config_dir(ctx).join(".config.json");
    match std::fs::metadata(&legacy) {
        Ok(meta)
            if meta.is_file()
                && (meta.len() > 0 || (!meta.permissions().readonly() && !file.exists())) =>
        {
            legacy
        }
        _ => file,
    }
}

/// Where the `claude` that signs someone in actually is, if it is anywhere. A bare name is
/// looked up on the context's search path the way a shell would.
pub(crate) fn program(ctx: &Context) -> Option<PathBuf> {
    crate::provider::program_of(ctx, crate::provider::ProviderId::Claude)
}

/// Where Claude Code's installers put `claude`, in the order an app looks there after the
/// login shell's `PATH`: the native installer's launcher in `~/.local/bin`, then where the
/// system's package managers put it, which is where its Homebrew cask goes, and a global npm
/// install where Homebrew or nodejs.org installed Node (`install_places` in the register).
pub(crate) fn install_places(home: &std::path::Path) -> Vec<PathBuf> {
    std::iter::once(home.join(".local/bin"))
        .chain(crate::host::OS.package_bins().iter().map(PathBuf::from))
        .collect()
}

/// Which Claude Code is installed here, read off disk and never by running it.
///
/// Running `claude --version` would be the obvious way and is the wrong one: it starts the
/// program Pitboard is trying to describe, which starts a daemon, which writes. Three
/// layouts cover how it is installed. The native installer puts the build at
/// `<...>/versions/<version>` and points a symlink at it, so the version is the file's own
/// name. An npm install has a `package.json` beside the resolved program. And a machine
/// that has run Claude Code at all left `daemon.lock` behind, which records the version
/// that wrote it.
pub(crate) fn installed_version(ctx: &Context) -> Option<String> {
    let resolved = program(ctx).and_then(|p| std::fs::canonicalize(p).ok());
    if let Some(path) = &resolved
        && let Some(name) = path.file_name().and_then(|n| n.to_str())
        && looks_like_a_version(name)
    {
        return Some(name.to_string());
    }
    if let Some(dir) = resolved.as_ref().and_then(|p| p.parent())
        && let Some(version) = version_in_package_json(&dir.join("package.json"))
            .or_else(|| version_in_package_json(&dir.join("../package.json")))
    {
        return Some(version);
    }
    daemon::read(ctx).and_then(|d| d.version)
}

fn looks_like_a_version(name: &str) -> bool {
    let parts: Vec<&str> = name.split('.').collect();
    parts.len() == 3
        && parts
            .iter()
            .all(|p| !p.is_empty() && p.bytes().all(|b| b.is_ascii_digit()))
}

fn version_in_package_json(path: &std::path::Path) -> Option<String> {
    let raw = std::fs::read_to_string(path).ok()?;
    let json: Value = serde_json::from_str(&raw).ok()?;
    let name = json.get("name")?.as_str()?;
    if name != "@anthropic-ai/claude-code" {
        return None;
    }
    Some(json.get("version")?.as_str()?.to_string())
}

/// The directory whose path string selects the credential slot.
pub(crate) fn storage_dir(ctx: &Context) -> String {
    storage_dir_from(
        ctx.secure_storage_dir.as_deref(),
        &ctx.home,
        &config_dir(ctx).to_string_lossy(),
    )
}

/// What the lock a token refresh of the login takes first guards, as [`crate::lock::acquire`]
/// takes it: `.oauth_refresh` in the storage directory, locked as `.oauth_refresh.lock`
/// (the register's `refresh_lock`).
pub(crate) fn refresh_lock(ctx: &Context) -> PathBuf {
    PathBuf::from(storage_dir(ctx)).join(".oauth_refresh")
}

/// What the legacy lock a token refresh takes second guards: the storage directory itself,
/// its links resolved where it is there, locked beside it.
pub(crate) fn legacy_refresh_lock(ctx: &Context) -> PathBuf {
    let dir = PathBuf::from(storage_dir(ctx));
    std::fs::canonicalize(&dir).unwrap_or(dir)
}

fn storage_dir_from(secure: Option<&str>, home: &std::path::Path, config_dir: &str) -> String {
    use unicode_normalization::UnicodeNormalization;
    match secure {
        Some("") => home.join(".claude").to_string_lossy().nfc().collect(),
        Some(v) => v.nfc().collect(),
        None => config_dir.to_string(),
    }
}

/// Whether this process reads the unsuffixed slot. An empty
/// `CLAUDE_SECURESTORAGE_CONFIG_DIR` pins it even when `CLAUDE_CONFIG_DIR` is set, and
/// without one, an empty `CLAUDE_CONFIG_DIR` names it, since Claude Code tests
/// `!CLAUDE_CONFIG_DIR`.
pub(crate) fn is_default_slot(ctx: &Context) -> bool {
    is_default_slot_from(
        ctx.secure_storage_dir.as_deref(),
        ctx.claude_config_dir.as_deref(),
    )
}

fn is_default_slot_from(secure: Option<&str>, config_dir: Option<&str>) -> bool {
    match secure {
        Some(v) => v.is_empty(),
        None => config_dir.is_none_or(str::is_empty),
    }
}

/// The keychain service this process would read.
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub fn live_service(ctx: &Context) -> String {
    if is_default_slot(ctx) {
        slot::LIVE_SERVICE.to_string()
    } else {
        slot::service_for_dir(&storage_dir(ctx))
    }
}

pub(crate) fn load_config(ctx: &Context) -> Result<Value> {
    let path = config_file(ctx);
    let raw = std::fs::read_to_string(&path).map_err(|source| {
        if source.kind() == std::io::ErrorKind::NotFound {
            Error::ClaudeConfigMissing { path: path.clone() }
        } else {
            Error::ClaudeConfigUnreadable {
                path: path.clone(),
                source,
            }
        }
    })?;
    serde_json::from_str(&raw).map_err(|source| Error::ClaudeConfigNotJson { path, source })
}

/// The account Claude Code's config names, which can be another than the login stored's: a
/// Claude Code process started or signed in on another login can write its own there (the
/// register's `config_identity_is_the_last_writers`). Only Anthropic says whose a login is.
#[derive(Debug, Clone, PartialEq)]
pub(crate) struct ConfigAccount {
    pub(crate) email: String,
    pub(crate) account_uuid: String,
    pub(crate) organization_uuid: String,
    pub(crate) organization_name: Option<String>,
}

/// The three organization fields are written by separate fetches, not by the profile save,
/// so a config that has never made them is normal and they stay `None`.
pub(crate) fn config_account(config: &Value) -> Option<ConfigAccount> {
    let o = config.get("oauthAccount")?.as_object()?;
    let s = |k: &str| o.get(k).and_then(Value::as_str).map(str::to_owned);
    Some(ConfigAccount {
        email: s("emailAddress")?,
        account_uuid: s("accountUuid")?,
        organization_uuid: s("organizationUuid").unwrap_or_default(),
        organization_name: s("organizationName"),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A bare name is looked up the way a shell looks it up, so Pitboard and the person's
    /// own shell disagree about whether Claude Code is installed only if PATH differs.
    #[test]
    #[cfg_attr(windows, ignore = "W17: finding programs on Windows")]
    fn a_program_is_found_on_path_and_a_missing_one_is_not() {
        let ctx = Context::new(PathBuf::from("/home/x"));
        let found = program(&ctx.clone().with_claude_program("ls".into()))
            .expect("every machine that runs these tests has ls on PATH");
        assert!(found.ends_with("ls"), "{found:?}");
        assert!(found.is_absolute(), "a shell would get an absolute path");
        assert!(std::fs::metadata(&found).is_ok());
        assert_eq!(
            program(
                &ctx.clone()
                    .with_claude_program("no-such-program-anywhere".into())
            ),
            None
        );
        assert_eq!(
            program(&ctx.with_claude_program("/nowhere/at/all/claude".into())),
            None
        );
    }

    #[test]
    fn an_empty_storage_dir_pins_the_default_slot_even_with_a_config_dir() {
        assert!(is_default_slot_from(None, None));
        assert!(!is_default_slot_from(None, Some("/cfg")));
        assert!(is_default_slot_from(Some(""), Some("/cfg")));
        assert!(!is_default_slot_from(Some("/elsewhere"), None));
        assert!(
            is_default_slot_from(None, Some("")),
            "Claude Code tests !CLAUDE_CONFIG_DIR"
        );
    }

    /// Claude Code 2.1.289 reads `CLAUDE_CONFIG_DIR` two ways: `??` for its config dir, so
    /// an empty one is the empty path, and `||` for its config file's base, so an empty one
    /// is the home. Both are read here as it reads them, though a context holding an empty
    /// one is refused before either is used.
    #[test]
    fn an_empty_config_dir_is_read_as_claude_code_reads_it() {
        let ctx =
            Context::new(PathBuf::from("/nowhere/home")).with_claude_config_dir(String::new());
        assert_eq!(config_dir(&ctx), PathBuf::new());
        assert_eq!(
            config_file(&ctx),
            std::path::Path::new("/nowhere/home/.claude.json")
        );
        assert!(is_default_slot(&ctx));
    }

    fn scratch_home() -> (tempfile::TempDir, Context, PathBuf) {
        let home = tempfile::tempdir().expect("a scratch home");
        std::fs::create_dir(home.path().join(".claude")).expect("a config dir");
        let ctx = Context::new(home.path().to_path_buf());
        let legacy = home.path().join(".claude/.config.json");
        (home, ctx, legacy)
    }

    #[test]
    fn an_empty_config_json_gives_way_to_claude_json() {
        let (home, ctx, legacy) = scratch_home();
        std::fs::write(&legacy, "").unwrap();
        std::fs::write(home.path().join(".claude.json"), "{}").unwrap();
        assert_eq!(config_file(&ctx), home.path().join(".claude.json"));
    }

    #[test]
    fn an_empty_config_json_is_taken_only_while_writable_and_claude_json_is_missing() {
        let (home, ctx, legacy) = scratch_home();
        std::fs::write(&legacy, "").unwrap();
        assert_eq!(config_file(&ctx), legacy);

        let mut read_only = std::fs::metadata(&legacy).unwrap().permissions();
        read_only.set_readonly(true);
        std::fs::set_permissions(&legacy, read_only).unwrap();
        assert_eq!(config_file(&ctx), home.path().join(".claude.json"));
    }

    #[test]
    fn a_config_json_wins_when_it_is_a_file_that_holds_anything() {
        let (home, ctx, legacy) = scratch_home();
        std::fs::write(home.path().join(".claude.json"), "{}").unwrap();
        std::fs::write(&legacy, "{}").unwrap();
        assert_eq!(config_file(&ctx), legacy);

        std::fs::remove_file(&legacy).unwrap();
        std::fs::create_dir(&legacy).unwrap();
        assert_eq!(config_file(&ctx), home.path().join(".claude.json"));
    }

    /// Claude Code: `if (n !== undefined) return (n || join(homedir(), ".claude"))`. The
    /// empty string would make every credential and lock path relative to the working
    /// directory.
    #[test]
    #[cfg_attr(windows, ignore = "W22: Claude Code's storage folder on Windows")]
    fn an_empty_storage_dir_means_the_default_directory_not_the_current_one() {
        let home = std::path::Path::new("/home/x");
        assert_eq!(storage_dir_from(Some(""), home, "/cfg"), "/home/x/.claude");
        assert_eq!(
            storage_dir_from(Some("/elsewhere"), home, "/cfg"),
            "/elsewhere"
        );
        assert_eq!(storage_dir_from(None, home, "/cfg"), "/cfg");
    }

    #[test]
    fn the_config_account_needs_an_email_and_an_account() {
        let full = serde_json::json!({"oauthAccount": {
            "emailAddress": "a@b.c", "accountUuid": "u", "organizationUuid": "o",
            "organizationName": "Org", "organizationType": "claude_max",
            "organizationRateLimitTier": "default_claude_max_20x"}});
        let id = config_account(&full).expect("should parse");
        assert_eq!(id.email, "a@b.c");
        assert_eq!(id.organization_name.as_deref(), Some("Org"));

        assert!(config_account(&serde_json::json!({})).is_none());
        assert!(
            config_account(&serde_json::json!({"oauthAccount": {"accountUuid": "u"}})).is_none()
        );
    }

    /// What 2.1.278 actually writes when it saves a profile: the organization fields come
    /// from other fetches and are often absent. An account is still identified.
    #[test]
    fn the_config_account_survives_a_config_with_no_organization_fields() {
        let written = serde_json::json!({"oauthAccount": {
            "accountUuid": "u", "emailAddress": "a@b.c", "organizationUuid": "o",
            "billingType": "subscription", "seatTier": "max_20x"}});
        let id = config_account(&written).expect("should parse");
        assert_eq!(id.organization_uuid, "o");
        assert_eq!(id.organization_name, None);
    }
}
