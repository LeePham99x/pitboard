//! Processes on Windows, and the programs a Windows shell starts.

use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::AsRawHandle;
use std::path::Path;
use windows_sys::Win32::Storage::FileSystem::{
    FILE_ATTRIBUTE_REPARSE_POINT, FILE_ATTRIBUTE_TAG_INFO, FILE_EXECUTE,
    FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OPEN_REPARSE_POINT, FILE_READ_ATTRIBUTES, FILE_READ_DATA,
    FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, FileAttributeTagInfo,
    GetFileInformationByHandleEx,
};
use windows_sys::Win32::System::SystemServices::IO_REPARSE_TAG_APPEXECLINK;

/// Whether process `pid` may still be running: always, until W18 asks Windows. The callers
/// only ever leave alone what a live process may still be using, so this keeps everything.
pub(crate) fn may_be_running(_pid: u32) -> bool {
    true
}

// What a Windows shell starts: an `.exe`, `.bat` or `.cmd` this user may open to run or to
// read, through every link, or an app execution alias in a `WindowsApps` folder, which opens
// as nothing until Windows starts it.
pub(crate) fn can_run(path: &Path) -> bool {
    if !crate::host::program::runnable_on_windows(path) {
        return false;
    }
    if is_an_alias(path) {
        return true;
    }
    let script = !path
        .extension()
        .is_some_and(|extension| extension.eq_ignore_ascii_case("exe"));
    std::fs::metadata(path).is_ok_and(|found| found.is_file())
        && std::fs::OpenOptions::new()
            .access_mode(if script { FILE_READ_DATA } else { FILE_EXECUTE })
            .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE)
            .open(path)
            .is_ok()
}

fn is_an_alias(path: &Path) -> bool {
    let in_windows_apps = path
        .parent()
        .and_then(Path::file_name)
        .is_some_and(|folder| folder.eq_ignore_ascii_case("WindowsApps"));
    in_windows_apps && reparse_tag(path) == Some(IO_REPARSE_TAG_APPEXECLINK)
}

fn reparse_tag(path: &Path) -> Option<u32> {
    let link = std::fs::OpenOptions::new()
        .access_mode(FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE)
        .custom_flags(FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS)
        .open(path)
        .ok()?;
    let mut info = FILE_ATTRIBUTE_TAG_INFO::default();
    // SAFETY: an open handle, and a FILE_ATTRIBUTE_TAG_INFO of this frame of the size given.
    let read = unsafe {
        GetFileInformationByHandleEx(
            link.as_raw_handle(),
            FileAttributeTagInfo,
            (&raw mut info).cast(),
            size_of::<FILE_ATTRIBUTE_TAG_INFO>() as u32,
        )
    };
    (read != 0 && info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT != 0)
        .then_some(info.ReparseTag)
}

/// Runs no program: a warm-up, the only run with a ceiling, stops on Windows before it
/// starts one.
pub(crate) fn run_within(
    _command: std::process::Command,
    _handed: Option<&[u8]>,
    _ceiling: std::time::Duration,
) -> std::io::Result<crate::host::Ran> {
    Err(std::io::Error::new(
        std::io::ErrorKind::Unsupported,
        "no program is run with a ceiling on Windows yet",
    ))
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a test sets up its scratch programs as an installer would, and finds Windows' own"
)]
mod tests {
    use super::*;
    use crate::host::Os;
    use crate::host::fs::testing as files;
    use crate::host::program::{Lookup, find};
    use std::ffi::OsStr;
    use std::path::PathBuf;

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "pitboard-programs-{name}-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("a scratch folder");
        dir
    }

    // Nothing here is ever run.
    fn program(at: &Path) -> PathBuf {
        std::fs::create_dir_all(at.parent().expect("its folder")).expect("a folder");
        std::fs::write(at, "never run").expect("a stand-in program");
        at.to_path_buf()
    }

    fn search(dirs: &[&Path]) -> std::ffi::OsString {
        std::env::join_paths(dirs).expect("a search path")
    }

    /// Each folder is tried with every name `PATHEXT` gives before the next, so a `.cmd`
    /// in an earlier folder wins over an `.exe` in a later one, and within one folder the
    /// `.exe` wins unless `PATHEXT` puts `.cmd` first. A `.ps1`, a folder of the name and a
    /// file this user may not run are passed over.
    #[test]
    fn a_program_is_found_as_a_windows_shell_finds_it() {
        let root = scratch("lookup");
        let (first, second) = (root.join("first"), root.join("second"));
        let cmd = program(&first.join("claude.cmd"));
        let exe = program(&second.join("claude.exe"));
        let windows = Lookup::new(Os::Windows, None);
        let path = search(&[&first, &second]);
        assert_eq!(
            find(Path::new("claude"), &path, &windows),
            Some(cmd.clone())
        );
        let beside = program(&first.join("claude.exe"));
        assert_eq!(
            find(Path::new("claude"), &path, &windows),
            Some(beside.clone())
        );
        let cmd_first = Lookup::new(Os::Windows, Some(OsStr::new(".CMD;.EXE")));
        assert_eq!(find(Path::new("claude"), &path, &cmd_first), Some(cmd));

        files::deny_running(&beside).expect("a program nobody may run");
        std::fs::remove_file(first.join("claude.cmd")).expect("the shim taken away");
        assert_eq!(
            find(Path::new("claude"), &path, &windows),
            Some(exe.clone()),
            "one nobody may run is passed over"
        );

        let only = root.join("only");
        program(&only.join("codex.ps1"));
        std::fs::create_dir_all(only.join("codex.exe")).expect("a folder of the name");
        assert_eq!(find(Path::new("codex"), &search(&[&only]), &windows), None);
        assert_eq!(
            find(&exe, OsStr::new(""), &windows),
            Some(exe.clone()),
            "named in full"
        );
        assert_eq!(
            find(&second.join("claude"), OsStr::new(""), &windows),
            Some(exe),
            "named in full without its extension"
        );
        let _ = std::fs::remove_dir_all(&root);
    }

    /// An app execution alias is told by its reparse tag, read on the link itself: one in a
    /// `WindowsApps` folder runs, and the same alias anywhere else does not. Each of this
    /// account's own aliases, read only, is one that runs.
    #[test]
    fn an_app_execution_alias_runs_from_windows_apps() {
        let root = scratch("aliases");
        let target = std::env::var_os("SystemRoot")
            .map(|windows| PathBuf::from(windows).join(r"System32\cmd.exe"))
            .expect("SystemRoot");
        assert!(can_run(&alias(
            &root.join("WindowsApps").join("pitboard-test.exe"),
            &target
        )));
        assert!(!can_run(&alias(
            &root.join("Elsewhere").join("pitboard-test.exe"),
            &target
        )));
        let _ = std::fs::remove_dir_all(&root);

        let own = std::env::var_os("LOCALAPPDATA")
            .map(|local| PathBuf::from(local).join(r"Microsoft\WindowsApps"));
        let aliases: Vec<PathBuf> = own
            .and_then(|dir| std::fs::read_dir(dir).ok())
            .into_iter()
            .flatten()
            .flatten()
            .map(|entry| entry.path())
            .filter(|path| reparse_tag(path) == Some(IO_REPARSE_TAG_APPEXECLINK))
            .collect();
        for alias in &aliases {
            assert!(can_run(alias), "{}", alias.display());
        }
        println!(
            "{} of this account's own aliases read as programs",
            aliases.len()
        );
    }

    // As Windows writes one: version 3, then the package, the app's id, the program and the
    // kind of app, each ending in a NUL.
    fn alias(at: &Path, target: &Path) -> PathBuf {
        use windows_sys::Win32::System::IO::DeviceIoControl;
        use windows_sys::Win32::System::Ioctl::FSCTL_SET_REPARSE_POINT;
        let mut body: Vec<u8> = 3u32.to_le_bytes().to_vec();
        for field in [
            "Pitboard.Test_0000000000000".into(),
            "Pitboard.Test_0000000000000!App".into(),
            target.to_string_lossy().into_owned(),
            "0".to_string(),
        ] {
            body.extend(field.encode_utf16().chain([0]).flat_map(u16::to_le_bytes));
        }
        let mut data = IO_REPARSE_TAG_APPEXECLINK.to_le_bytes().to_vec();
        data.extend(u16::try_from(body.len()).expect("short").to_le_bytes());
        data.extend(0u16.to_le_bytes());
        data.extend(body);
        std::fs::create_dir_all(at.parent().expect("its folder")).expect("a folder");
        std::fs::write(at, "").expect("the alias's file");
        let file = std::fs::OpenOptions::new()
            .write(true)
            .custom_flags(FILE_FLAG_OPEN_REPARSE_POINT)
            .open(at)
            .expect("the alias's file");
        let mut returned = 0u32;
        // SAFETY: an open file, and reparse data of exactly the length given.
        let set = unsafe {
            DeviceIoControl(
                file.as_raw_handle(),
                FSCTL_SET_REPARSE_POINT,
                data.as_ptr().cast(),
                data.len() as u32,
                std::ptr::null_mut(),
                0,
                &raw mut returned,
                std::ptr::null_mut(),
            )
        };
        assert_ne!(set, 0, "{}", std::io::Error::last_os_error());
        at.to_path_buf()
    }

    #[test]
    fn this_test_is_a_program_and_its_folder_is_not() {
        let exe = std::env::current_exe().expect("this test");
        assert!(can_run(&exe));
        let folder = exe.parent().expect("its folder");
        assert!(!can_run(folder));
        assert!(!can_run(&folder.join("absent.exe")));
    }
}
