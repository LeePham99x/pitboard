//! Where Codex CLI keeps things.
//!
//! Read from codex-cli 0.154.0: the binary on this machine, and the matching public source
//! at tag `rust-v0.154.0`. Which store it keeps its login in is read from 0.160.0, in
//! [`super::layers`].

use super::layers;
use crate::context::Context;
use crate::host::Os;
use std::path::{Path, PathBuf};

/// The file Codex keeps its login in, inside [`home`].
pub(crate) const AUTH_FILE: &str = "auth.json";

// Unset on Windows, in `FOLDERID_Profile` wherever `USERPROFILE` points (`codex_windows_home`).
pub(crate) fn home(ctx: &Context) -> PathBuf {
    match ctx.codex_home() {
        Some(dir) if !dir.is_empty() => match ctx.os {
            Os::MacOs | Os::Linux => PathBuf::from(dir),
            Os::Windows => std::fs::canonicalize(dir)
                .map_or_else(|_| PathBuf::from(dir), crate::host::without_verbatim_prefix),
        },
        _ => ctx.profile.as_deref().unwrap_or(ctx.home()).join(".codex"),
    }
}

// Codex does not start where `CODEX_HOME` names no folder. Asked on Windows alone until
// macOS and Linux refuse it too, which would be a change in what they accept.
pub(crate) fn missing_home(ctx: &Context) -> Option<PathBuf> {
    let dir = ctx.codex_home().filter(|dir| !dir.is_empty())?;
    (ctx.os == Os::Windows && !Path::new(dir).is_dir()).then(|| PathBuf::from(dir))
}

pub(crate) fn auth_file(ctx: &Context) -> PathBuf {
    home(ctx).join(AUTH_FILE)
}

/// Where Codex's installers put `codex`, in the order an app looks there after the login
/// shell's `PATH`: the link its standalone installer makes in `~/.local/bin`, then where the
/// system's package managers put it, which is where its Homebrew cask and a global npm
/// install go (`codex_install_places` in the register).
pub(crate) fn install_places(home: &Path) -> Vec<PathBuf> {
    std::iter::once(home.join(".local/bin"))
        .chain(crate::host::OS.package_bins().iter().map(PathBuf::from))
        .collect()
}

/// Which store this machine's Codex is configured to keep its login in.
///
/// The shipped default is `file`, and it is a packaged default rather than a line in
/// anybody's `config.toml`, so an absent setting means `file` and not "unknown".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Backend {
    File,
    Keyring,
    /// `auto`: the keyring, falling back to the file.
    Either,
    /// In memory for the life of one process. Nothing Pitboard can park.
    Ephemeral,
    /// `[features] secret_auth_storage` with a keyring store: an encrypted file whose key
    /// is in the keychain.
    Secrets,
    /// A layer Codex reads is there and cannot be read as Codex reads it, which stops Codex
    /// from starting, so nobody can tell. Refused like a store Pitboard does not handle.
    Unknown,
}

/// The person's own `config.toml`, in Codex's home.
pub(crate) fn config_file(ctx: &Context) -> PathBuf {
    home(ctx).join("config.toml")
}

/// Where this machine's Codex keeps its login, from every layer Codex 0.160.0 reads outside
/// a project, in its order ([`layers`]): its own default, `/etc/codex`, the person's own
/// `config.toml` and, on macOS, the managed preferences an administrator forces.
///
/// It read only `$CODEX_HOME/config.toml` before, so a store `/etc/codex` or a managed
/// profile chose was taken for the file, and Pitboard read an `auth.json` Codex did not use.
pub(crate) fn store(ctx: &Context) -> layers::Store {
    layers::of(ctx, &config_file(ctx))
}

/// Where a sign-in Pitboard runs keeps the login it signs in to: its private home holds no
/// `config.toml`, and its command line names the file store ([`layers::FILE_STORE`]), so
/// only what an administrator set over that moves it.
pub(crate) fn sign_in_store(ctx: &Context) -> layers::Store {
    layers::of_a_sign_in(ctx, &config_file(ctx))
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a test sets up its scratch files as a person or a tool would"
)]
mod tests {
    use super::*;

    fn backend(ctx: &Context) -> Backend {
        store(ctx).backend
    }

    fn at(dir: &Path, config: &str) -> Context {
        std::fs::create_dir_all(dir).expect("a scratch codex home");
        std::fs::write(dir.join("config.toml"), config).expect("a config");
        Context::for_unit_test().with_codex_home(dir.to_string_lossy().into())
    }

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "pitboard-codex-{name}-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    // Codex 0.162.1's find_codex_home_from_env: a set `CODEX_HOME` must be a folder and is
    // canonicalised, and unset it is `.codex` in `FOLDERID_Profile`, which dirs 6.0.0 reads.
    #[test]
    fn codex_finds_its_home_on_windows_as_codex_does() {
        use crate::context::Environment;
        use crate::host::Os;
        let profile = scratch("profile");
        std::fs::create_dir_all(profile.join("codex")).expect("a scratch codex home");
        std::fs::write(profile.join("file"), "").expect("a file");
        let named = |path: PathBuf| path.to_string_lossy().into_owned();
        let read = |os: Os, codex: Option<PathBuf>| {
            let pairs = [
                ("USERPROFILE", named(profile.clone())),
                ("HOME", named(profile.clone())),
            ]
            .into_iter()
            .chain(codex.map(|dir| ("CODEX_HOME", named(dir))));
            Context::read(&Environment::on(os, pairs), "cli")
        };

        let handed = Context::new(profile.clone());
        assert_eq!(home(&handed), profile.join(".codex"), "a handed home's");
        let unset = read(Os::Windows, None);
        assert_eq!(
            home(&unset),
            profile.join(".codex"),
            "no profile is looked up in a test"
        );
        let elsewhere = profile.join("Profile");
        assert_eq!(
            home(&unset.with_profile(elsewhere.clone())),
            elsewhere.join(".codex"),
            "not the home, where the profile is elsewhere"
        );
        let given = read(Os::Windows, Some(profile.join(".").join("codex")));
        let canonical = std::fs::canonicalize(profile.join("codex")).expect("there");
        assert_eq!(
            home(&given),
            crate::host::without_verbatim_prefix(canonical)
        );
        assert_eq!(missing_home(&given), None);
        for nothing in [profile.join("never-made"), profile.join("file")] {
            assert_eq!(
                missing_home(&read(Os::Windows, Some(nothing.clone()))),
                Some(nothing)
            );
        }
        for os in [Os::MacOs, Os::Linux] {
            let missing = profile.join("never-made");
            let ctx = read(os, Some(missing.clone()));
            assert_eq!((home(&ctx), missing_home(&ctx)), (missing, None), "{os:?}");
        }
        let _ = std::fs::remove_dir_all(&profile);
    }

    /// The shipped default is a packaged one rather than a line anybody wrote, so an absent
    /// setting means the file and not "cannot tell".
    #[test]
    #[cfg_attr(windows, ignore = "W21: switching Codex on Windows")]
    fn no_setting_means_the_file() {
        let dir = scratch("default");
        let ctx = at(&dir, "model = \"gpt-5\"\n");
        assert_eq!(backend(&ctx), Backend::File);
        assert_eq!(auth_file(&ctx), dir.join("auth.json"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Each store is read from the person's own `config.toml` as TOML. A value that is not a
    /// store Codex has stops Codex from starting, so it is no longer taken for the file.
    #[test]
    #[cfg_attr(windows, ignore = "W21: switching Codex on Windows")]
    fn each_store_is_recognised() {
        let dir = scratch("stores");
        for (written, expected) in [
            ("cli_auth_credentials_store = \"keyring\"", Backend::Keyring),
            ("cli_auth_credentials_store=\"auto\"", Backend::Either),
            (
                "  cli_auth_credentials_store = 'ephemeral'",
                Backend::Ephemeral,
            ),
            ("cli_auth_credentials_store = \"file\"", Backend::File),
            (
                "cli_auth_credentials_store = \"nonsense\"",
                Backend::Unknown,
            ),
            ("cli_auth_credentials_store = keyring", Backend::Unknown),
        ] {
            let ctx = at(&dir, written);
            assert_eq!(backend(&ctx), expected, "{written}");
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A trailing comment is not part of the value. Read as part of it, a keyring store
    /// looked like the default file, and Pitboard would read a file Codex had deleted.
    #[test]
    #[cfg_attr(windows, ignore = "W21: switching Codex on Windows")]
    fn a_comment_after_the_value_is_not_the_value() {
        let dir = scratch("comment");
        let ctx = at(
            &dir,
            "cli_auth_credentials_store = \"keyring\"  # on this mac\n",
        );
        assert_eq!(backend(&ctx), Backend::Keyring);
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A key of the same name inside another table is not the setting.
    #[test]
    #[cfg_attr(windows, ignore = "W21: switching Codex on Windows")]
    fn only_the_top_level_key_is_the_setting() {
        let dir = scratch("tables");
        for (config, expected, why) in [
            (
                "[profiles.work]\ncli_auth_credentials_store = \"keyring\"\n",
                Backend::File,
                "a key inside another table is not the setting",
            ),
            (
                "cli_auth_credentials_store = \"keyring\"\n\
                 [features]\nsecret_auth_storage = true\n",
                Backend::Secrets,
                "an encrypted file whose key is in the keychain is a store of its own",
            ),
            (
                "[features]\nsecret_auth_storage = true\n",
                Backend::File,
                "the feature only changes a keyring store",
            ),
        ] {
            assert_eq!(backend(&at(&dir, config)), expected, "{why}");
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// What `/etc/codex` says is read as well as the person's own file, where Pitboard read
    /// only the person's.
    #[test]
    #[cfg_attr(windows, ignore = "W21: switching Codex on Windows")]
    fn the_system_layers_are_read_as_well_as_the_persons_own() {
        let dir = scratch("system");
        let host = crate::host::memory::MemoryHost::new();
        let ctx = at(&dir, "model = \"gpt-5\"\n").with_memory_stores(host.clone());
        host.administers(
            "/etc/codex/config.toml",
            "cli_auth_credentials_store = \"keyring\"\n",
        );
        assert_eq!(backend(&ctx), Backend::Keyring);
        std::fs::write(
            dir.join("config.toml"),
            "cli_auth_credentials_store = \"file\"\n",
        )
        .expect("a config");
        assert_eq!(backend(&ctx), Backend::File);
        host.administers(
            "/etc/codex/requirements.toml",
            "cli_auth_credentials_store = \"keyring\"\n",
        );
        assert_eq!(backend(&ctx), Backend::Keyring);
        assert_eq!(
            sign_in_store(&ctx).backend,
            Backend::Keyring,
            "a requirement pins a sign-in's store too"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }
}
