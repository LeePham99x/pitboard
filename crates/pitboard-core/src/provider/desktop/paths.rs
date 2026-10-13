use crate::context::Context;
use crate::host::{self, Os};
use crate::provider::folder::Unfit;
use crate::provider::{ProviderError, ProviderId};
use std::path::{Path, PathBuf};

// A constant: nothing a build for tests is told lets it reach the account's own data folder.
pub(super) const REAL: bool = !cfg!(any(test, feature = "test-support"));

const NOT_NAMED: &str = "this build is for tests and was given no Claude data folder";
const MACOS_ONLY: &str = "Pitboard switches Claude Desktop accounts on macOS only";

pub(crate) fn root(ctx: &Context) -> Result<PathBuf, ProviderError> {
    // cfg! is a value, so this branch compiles in a release too, where scratch_claude is None.
    if !REAL {
        return ctx
            .scratch_claude()
            .map(|scratch| scratch.data.clone())
            .ok_or_else(|| unsupported(NOT_NAMED));
    }
    match host::OS {
        Os::MacOs => real(ctx),
        Os::Linux => Err(unsupported(MACOS_ONLY)),
        Os::Windows => Err(unsupported(MACOS_ONLY)),
    }
}

fn real(ctx: &Context) -> Result<PathBuf, ProviderError> {
    real_for(host::user::home(), ctx.home())
}

// The passwd home comes in as an argument, so a unit test never asks for the real one.
fn real_for(own: Option<PathBuf>, home: &Path) -> Result<PathBuf, ProviderError> {
    let own =
        own.ok_or_else(|| unsupported("this system names no home folder for this account"))?;
    if !same_folder(&own, home) {
        return Err(unsupported(
            "`HOME` names another folder than this account's own, where Claude keeps its data, \
             so Pitboard leaves Claude Desktop alone here",
        ));
    }
    Ok(own.join("Library/Application Support/Claude"))
}

fn same_folder(a: &Path, b: &Path) -> bool {
    let resolved = |p: &Path| std::fs::canonicalize(p).unwrap_or_else(|_| p.to_path_buf());
    resolved(a) == resolved(b)
}

// This account's own home where `HOME` names another folder, for `doctor`'s advice.
pub(crate) fn home_elsewhere(ctx: &Context) -> Option<PathBuf> {
    if !REAL {
        return None;
    }
    match host::OS {
        Os::MacOs => elsewhere_for(host::user::home(), ctx.home()),
        Os::Linux | Os::Windows => None,
    }
}

fn elsewhere_for(own: Option<PathBuf>, home: &Path) -> Option<PathBuf> {
    own.filter(|own| !same_folder(own, home))
}

fn unsupported(reason: &str) -> ProviderError {
    ProviderError::Unsupported {
        provider: ProviderId::Desktop,
        reason: reason.into(),
    }
}

pub(super) fn refuses(root: &Path) -> Option<Unfit> {
    if std::fs::symlink_metadata(root).is_ok_and(|found| found.file_type().is_symlink()) {
        return Some(Unfit::Link);
    }
    let mut beside = root.as_os_str().to_owned();
    beside.push("-3p");
    std::fs::symlink_metadata(PathBuf::from(beside))
        .is_ok()
        .then_some(Unfit::ThirdParty("Claude-3p"))
}

#[cfg(test)]
mod tests {
    use super::*;

    const _: () = assert!(!REAL, "a unit test is a build for tests");

    fn reason(error: ProviderError) -> String {
        match error {
            ProviderError::Unsupported { provider, reason } => {
                assert_eq!(provider, ProviderId::Desktop);
                reason
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn a_build_for_tests_cannot_name_the_accounts_own_data_folder() {
        let refused = root(&Context::for_unit_test()).expect_err("no scratch was named");
        assert_eq!(reason(refused), NOT_NAMED);
        let scratch = std::env::temp_dir().join(format!("pitboard-paths-{}", std::process::id()));
        let named = Context::for_unit_test().with_scratch_claude(scratch.clone());
        assert_eq!(root(&named).unwrap(), scratch.join("data"));
    }

    #[test]
    #[cfg_attr(
        windows,
        ignore = "Claude Desktop is macOS only: Windows makes no link"
    )]
    fn home_naming_another_folder_makes_the_tool_absent() {
        let base = std::env::temp_dir().join(format!("pitboard-homes-{}", std::process::id()));
        let (own, other) = (base.join("own"), base.join("other"));
        std::fs::create_dir_all(&own).unwrap();
        std::fs::create_dir_all(&other).unwrap();
        assert_eq!(
            real_for(Some(own.clone()), &own).unwrap(),
            own.join("Library/Application Support/Claude")
        );
        let link = base.join("link");
        let _ = std::fs::remove_file(&link);
        crate::host::fs::testing::link_dir(&own, &link).unwrap();
        assert!(
            real_for(Some(own.clone()), &link).is_ok(),
            "a link to it is it"
        );
        assert!(reason(real_for(Some(own.clone()), &other).unwrap_err()).starts_with("`HOME`"));
        assert_eq!(elsewhere_for(Some(own.clone()), &other), Some(own.clone()));
        assert_eq!(elsewhere_for(Some(own.clone()), &link), None);
        assert_eq!(elsewhere_for(None, &other), None);
        assert_eq!(home_elsewhere(&Context::new(other.clone())), None);
        assert_eq!(
            reason(real_for(None, &own).unwrap_err()),
            "this system names no home folder for this account"
        );
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    #[cfg_attr(
        windows,
        ignore = "Claude Desktop is macOS only: Windows makes no link"
    )]
    fn a_link_or_a_third_party_folder_beside_it_is_refused() {
        let base = std::env::temp_dir().join(format!("pitboard-refuses-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&base);
        let root = base.join("Claude");
        std::fs::create_dir_all(&root).unwrap();
        assert_eq!(refuses(&root), None);
        std::fs::create_dir_all(base.join("Claude-3p")).unwrap();
        let beside = refuses(&root).expect("refused");
        assert_eq!(beside, Unfit::ThirdParty("Claude-3p"));
        assert_eq!(
            beside.to_string(),
            "has a `Claude-3p` folder beside it, which a third-party deployment of Claude uses"
        );
        let link = base.join("Linked");
        crate::host::fs::testing::link_dir(&root, &link).unwrap();
        assert_eq!(refuses(&link), Some(Unfit::Link));
        assert_eq!(Unfit::Link.to_string(), "is a link");
        let _ = std::fs::remove_dir_all(&base);
    }
}
