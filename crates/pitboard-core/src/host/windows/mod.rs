//! Windows, while its face is being written: every answer not written yet refuses, through an
//! error or an answer its callers already have, so that nothing of Windows is read or changed
//! by a part that is not built yet.
//!
//! A Windows build of a 0.x release refuses before it gets here ([`crate::release`]). A build
//! opened to Windows, for working on Pitboard and for CI, reaches this face, and finds:
//!
//! - no store of secrets for a tool (W23 offers Credential Manager);
//! - no scheduler (W25).
//!
//! Which store a tool keeps its login in is the tool's to say, in its provider's code: Codex's
//! file is read there, and Claude Code's chain refuses (W22, W23).

mod credman;
#[allow(
    clippy::disallowed_methods,
    reason = "the face every file and folder Pitboard makes is made through"
)]
pub(crate) mod fs;
mod job;
pub(crate) mod proc;
pub(super) mod seal;
pub(crate) mod user;

use super::administered::{Administered, Hive};
use super::{Elevation, Floor, Host, Jar, JarError, Listed, LoginPath, Os, Scheduler};
use crate::context::{Context, Environment};
use crate::store::vault::FileVault;
use crate::store::{PlainFile, RawStore};
use std::path::{Path, PathBuf};
use std::sync::Arc;

pub(super) const OS: Os = Os::Windows;

// Instant reads QueryPerformanceCounter here, which counts a sleep; unbiased interrupt time
// does not.
pub(super) fn awake() -> std::time::Duration {
    let mut units = 0u64;
    // SAFETY: the pointer is to a u64 of this frame, which the call writes and does not keep.
    unsafe {
        windows_sys::Win32::System::WindowsProgramming::QueryUnbiasedInterruptTimePrecise(
            &raw mut units,
        );
    }
    super::from_interrupt_time(units)
}

// What a program started now is given, which one started before a change was not.
pub(super) fn login_path(_env: &Environment) -> LoginPath {
    if cfg!(any(test, feature = "test-support")) {
        return LoginPath::Unknown;
    }
    registry_path()
}

fn registry_path() -> LoginPath {
    use windows_registry::{CURRENT_USER, LOCAL_MACHINE};
    let parts: Vec<String> = [
        (
            LOCAL_MACHINE,
            r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
        ),
        (CURRENT_USER, "Environment"),
    ]
    .into_iter()
    .filter_map(|(hive, key)| expanded(hive.open(key).ok()?, "Path"))
    .filter(|part| !part.is_empty())
    .collect();
    if parts.is_empty() {
        return LoginPath::Unknown;
    }
    LoginPath::Said(parts.join(";"))
}

fn expanded(key: windows_registry::Key, name: &str) -> Option<String> {
    use windows_registry::Type;
    let kind = key.get_type(name).ok()?;
    if !matches!(kind, Type::String | Type::ExpandString) {
        return None;
    }
    let text = key.get_hstring(name).ok()?;
    if kind == Type::String {
        return Some(text.to_string_lossy());
    }
    let unexpanded: Vec<u16> = text.iter().copied().chain([0]).collect();
    let mut out = vec![0u16; 1024];
    loop {
        // SAFETY: a NUL-terminated string, and a buffer of the length given.
        let needed = unsafe {
            windows_sys::Win32::System::Environment::ExpandEnvironmentStringsW(
                unexpanded.as_ptr(),
                out.as_mut_ptr(),
                out.len() as u32,
            )
        } as usize;
        if needed == 0 {
            return None;
        }
        if needed <= out.len() {
            return Some(String::from_utf16_lossy(&out[..needed - 1]));
        }
        out.resize(needed, 0);
    }
}

// As `reg.exe query` prints it for Claude Code: unexpanded, trimmed, and only a string.
fn policy(hive: Hive, key: &str, value: &str) -> Administered {
    use windows_registry::{CURRENT_USER, LOCAL_MACHINE, Type};
    let root = match hive {
        Hive::Machine => LOCAL_MACHINE,
        Hive::User => CURRENT_USER,
    };
    let read = root
        .open(key)
        .and_then(|opened| match opened.get_type(value)? {
            Type::String | Type::ExpandString => opened.get_hstring(value).map(Some),
            _ => Ok(None),
        });
    match read {
        Ok(Some(text)) => Administered::Set(text.to_string_lossy().trim_end().to_owned()),
        Ok(None) => Administered::Unset,
        // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND): no such key or value.
        Err(e) if e.code().0 as u32 == 0x8007_0002 => Administered::Unset,
        Err(e) => Administered::Unreadable(format!("cannot be read: {e}")),
    }
}

#[derive(Debug)]
struct Windows;

impl Host for Windows {
    /// Credential Manager, where Claude Code may keep its login, is offered by W23. Until
    /// then there is no store of secrets here, and each tool's chain refuses on Windows in
    /// its own provider's code, so this answer builds no chain.
    fn foreign_secrets(&self, _ctx: &Context, _account: &str) -> Option<Box<dyn RawStore>> {
        None
    }

    fn file(&self, path: PathBuf) -> Box<dyn RawStore> {
        Box::new(PlainFile::at(path))
    }

    fn vault(&self, ctx: &Context) -> Box<dyn RawStore> {
        Box::new(FileVault::sealed(ctx, &seal::Dpapi))
    }

    // Inside this home, as on Linux.
    fn vault_is_shared(&self) -> bool {
        false
    }

    fn listing(&self) -> Result<Vec<Listed>, String> {
        proc::listing()
    }

    fn cookies(&self, _jar: &Path, _hosts: &[&str], _names: &[&str]) -> Result<Jar, JarError> {
        Err(JarError::Unsupported)
    }

    fn app_version(&self, _bundle: &Path) -> Option<String> {
        None
    }

    fn app_id(&self, _bundle: &Path) -> Option<String> {
        None
    }

    /// Task Scheduler is written for by W25.
    fn scheduler(&self) -> Option<&dyn Scheduler> {
        None
    }

    fn elevation(&self, ctx: &Context) -> Elevation {
        user::elevation(ctx.sudo())
    }

    fn floor(&self) -> Floor {
        user::floor()
    }

    // Windows has none: what an administrator sets for Codex there is in files.
    fn managed_preference(&self, _domain: &str, _key: &str) -> Administered {
        Administered::Unset
    }

    fn policy(&self, hive: Hive, key: &str, value: &str) -> Administered {
        if super::administered::READ_BY_REAL_HOSTS {
            policy(hive, key, value)
        } else {
            Administered::Unset
        }
    }

    fn program_data(&self) -> Option<PathBuf> {
        user::program_data()
    }

    // The account's own items: no test's business, as what an administrator set is not.
    fn credential_names(&self, prefix: &str) -> Result<Vec<String>, String> {
        if !super::administered::READ_BY_REAL_HOSTS {
            return Ok(Vec::new());
        }
        credman::names(&format!("{prefix}*")).map_err(|e| e.to_string())
    }
}

#[cfg(any(test, feature = "test-support"))]
pub(crate) fn write_jar(_path: &Path, _cookies: &[super::Cookie]) -> std::io::Result<()> {
    Err(std::io::ErrorKind::Unsupported.into())
}

pub(super) fn host() -> Arc<dyn Host> {
    Arc::new(Windows)
}

// Whether a link or a shim started it is W25's to read.
pub(super) fn current_program() -> std::io::Result<PathBuf> {
    std::env::current_exe()
}

/// Task Scheduler as a machine in memory has it, which W25 writes with the scheduler itself.
/// Until then a machine in memory on Windows has no scheduler, as this machine has none.
#[cfg(any(test, feature = "test-support"))]
pub(super) fn pretend_scheduler(
    _refuse_start: Arc<std::sync::atomic::AtomicBool>,
) -> Option<Box<dyn Scheduler>> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::provider::ProviderId;
    use crate::store::Error;

    /// What a unit test's context reaches here: this face, with every home its own.
    fn ctx() -> Context {
        Context::for_unit_test()
    }

    fn unreadable<T: std::fmt::Debug>(read: Result<T, Error>, why: &str) {
        match read {
            Err(Error::Unreadable(said)) => assert_eq!(said, why),
            other => panic!("expected the store to be unreadable, {why}: {other:?}"),
        }
    }

    /// Claude Code's chain on Windows is a store nobody can read yet, never the file alone:
    /// Claude Code may keep its login in Credential Manager there, and a chain of the file
    /// alone would read a login that is not the one in use, and write one nothing reads.
    /// A sign-in's login is refused the same way, and so is taking one away.
    #[test]
    fn claude_codes_chain_is_unreadable_and_never_the_file() {
        let ctx = ctx();
        let why = "Pitboard does not switch Claude Code on Windows yet";
        let live = crate::provider::document(ProviderId::Claude)
            .live(&ctx)
            .expect("a chain");
        assert_eq!(
            crate::store::resolve(&live.chain, &live.service).map_err(|e| e.to_string()),
            Err(format!("the credential store could not be read: {why}"))
        );
        unreadable(crate::store::read_raw(&live.chain, &live.service), why);
        let dir = std::env::temp_dir();
        unreadable(crate::provider::claude::live::read_signin(&ctx, &dir), why);
        unreadable(
            crate::provider::claude::live::discard_signin(
                &ctx,
                crate::service::Permit::for_a_test(),
                &dir,
            ),
            why,
        );
    }

    #[test]
    #[allow(
        clippy::disallowed_methods,
        reason = "a test writes a login as Codex does"
    )]
    fn codexs_live_login_is_auth_json_in_its_home() {
        let home = tempfile::tempdir().expect("a scratch Codex home");
        let ctx = ctx().with_codex_home(home.path().to_string_lossy().into());
        let live = crate::provider::document(ProviderId::Codex)
            .live(&ctx)
            .expect("the file store");
        assert_eq!(
            crate::store::read_raw(&live.chain, &live.service).expect("readable"),
            None
        );
        std::fs::write(home.path().join("auth.json"), r#"{"tokens":{}}"#).expect("a login");
        assert_eq!(
            crate::store::read_raw(&live.chain, &live.service)
                .expect("readable")
                .as_deref(),
            Some(r#"{"tokens":{}}"#)
        );
    }

    #[test]
    fn the_vault_seals_each_park_in_this_home_alone() {
        let scratch = tempfile::tempdir().expect("a scratch home");
        let ctx = Context::new(scratch.path().to_path_buf())
            .with_pitboard_home(scratch.path().join("pitboard"));
        let vault = ctx.host().vault(&ctx);
        let (park, login) = (
            "pitboard-park-1f0e2d3c-4b5a-4968-8776-a5b4c3d2e1f0-1789935600123",
            r#"{"claudeAiOauth":{"refreshToken":"r"}}"#,
        );
        assert_eq!(vault.kind(), crate::store::Backend::SealedFile);
        assert!(!host().vault_is_shared());
        vault
            .write(crate::service::Permit::for_a_test(), park, login)
            .expect("sealed");
        assert_eq!(vault.read(park).expect("opened").as_deref(), Some(login));
        assert_eq!(vault.list().expect("listed"), Some(vec![park.to_string()]));
        let held = std::fs::read(crate::store::vault_dir(&ctx).join(format!("{park}.json")))
            .expect("the file");
        assert!(
            !held
                .windows(login.len())
                .any(|bytes| bytes == login.as_bytes())
        );
    }

    // Copies of this test's program, each kept running by a helper until it is let go.
    #[test]
    #[allow(
        clippy::disallowed_methods,
        reason = "a test puts its stand-ins where a person would install a program"
    )]
    fn a_codex_this_user_runs_is_found_where_it_runs_from_in_any_case() {
        use crate::helper::{Helper, Job};
        let dir = std::env::temp_dir().join(format!("pitboard-codex-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let programs = [
            dir.join("a").join("codex.exe"),
            dir.join("b").join("Codex.EXE"),
        ];
        let helpers: Vec<Helper> = programs
            .iter()
            .enumerate()
            .map(|(index, program)| {
                std::fs::create_dir_all(program.parent().expect("its folder")).expect("a folder");
                std::fs::copy(std::env::current_exe().expect("this test"), program)
                    .expect("a stand-in");
                Helper::start_as(program, Job::Locks, &dir.join(format!("{index}.lock")))
            })
            .collect();
        let found = host().processes("codex").expect("the process list reads");
        for (program, helper) in programs.iter().zip(&helpers) {
            let one = found
                .iter()
                .find(|one| one.pid == helper.pid())
                .unwrap_or_else(|| panic!("{} runs: {found:?}", program.display()));
            assert_eq!(
                std::fs::canonicalize(&one.path).expect("the path it runs from"),
                std::fs::canonicalize(program).expect("the stand-in"),
                "{}",
                one.path.display()
            );
            assert_eq!(one.package, None);
        }
        let listed = host().listing().expect("the process list reads");
        let me = listed
            .iter()
            .find(|one| one.pid == std::process::id())
            .expect("this test is listed");
        assert!(me.placed);
        for helper in helpers {
            assert_eq!(
                listed
                    .iter()
                    .find(|one| one.pid == helper.pid())
                    .map(|one| one.parent),
                Some(std::process::id()),
                "a helper's parent is this test"
            );
            helper.stop();
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn no_cookie_jar_or_app_is_read() {
        assert!(matches!(
            host().cookies(Path::new("C:/x/Cookies"), &["claude.ai"], &["sessionKey"]),
            Err(JarError::Unsupported)
        ));
        assert_eq!(host().app_version(Path::new("C:/x")), None);
    }

    /// There is no scheduler, on this machine or on one in memory.
    #[test]
    fn there_is_no_scheduler() {
        assert!(host().scheduler().is_none());
        let memory = crate::host::memory::MemoryHost::new();
        assert!(memory.scheduler().is_none());
        assert!(matches!(
            crate::schedule::status(&ctx()),
            crate::schedule::Installed::Unsupported
        ));
    }

    #[test]
    fn as_the_person_the_gate_lets_a_change_through() {
        let ctx = ctx();
        assert_eq!(
            ctx.host().elevation(&ctx),
            Elevation::Normal,
            "Pitboard's tests run as the person: run them from a terminal that is not elevated"
        );
        assert_eq!(user::elevation(true), Elevation::Normal);
        assert_eq!(ctx.host().floor(), Floor::Met);
        assert!(crate::service::gate(&ctx).is_ok());
    }

    // Claude Desktop's folders are never moved here.
    #[test]
    fn no_apps_folder_is_moved() {
        let dir = std::env::temp_dir().join(format!("pitboard-windows-{}", std::process::id()));
        let permit = crate::service::Permit::for_a_test();
        let unsupported = |made: std::io::Result<()>| {
            assert_eq!(
                made.map_err(|e| e.kind()),
                Err(std::io::ErrorKind::Unsupported)
            );
        };
        unsupported(fs::private_dir(&dir).map(drop));
        unsupported(fs::lock_dir(&dir).map(drop));
        unsupported(fs::move_new(permit, &dir, &dir.join("x")));
        unsupported(fs::same_volume(&dir, &dir).map(drop));
        unsupported(write_jar(&dir.join("Cookies"), &[]));
        assert!(!dir.exists(), "nothing was made");
        assert_eq!(user::login_name(), None);
    }

    #[test]
    fn a_policy_is_read_as_reg_exe_prints_it() {
        let current = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        let (_, _, build) = user::version().expect("RtlGetVersion");
        assert_eq!(
            policy(Hive::Machine, current, "CurrentBuild"),
            Administered::Set(build.to_string())
        );
        assert_eq!(
            policy(Hive::Machine, current, "pitboard-absent"),
            Administered::Unset
        );
        for hive in [Hive::Machine, Hive::User] {
            assert_eq!(
                policy(hive, r"SOFTWARE\Policies\pitboard-absent", "Settings"),
                Administered::Unset
            );
        }
        assert_eq!(
            host().policy(Hive::Machine, current, "CurrentBuild"),
            Administered::Unset
        );
    }

    // The machine's `Path` holds `%SystemRoot%\System32`, expanded.
    #[test]
    #[allow(
        clippy::disallowed_methods,
        reason = "where Windows is, as it names it"
    )]
    fn the_path_a_program_started_now_gets_is_read_from_the_registry() {
        assert_eq!(login_path(&Environment::default()), LoginPath::Unknown);
        let LoginPath::Said(path) = registry_path() else {
            panic!("every Windows has a Path");
        };
        let system = std::env::var_os("SystemRoot")
            .map(|windows| PathBuf::from(windows).join("System32"))
            .expect("SystemRoot");
        assert!(
            std::env::split_paths(&path)
                .any(|dir| crate::host::same_path_in_any_case(&dir, &system)),
            "{path}"
        );
    }
}
