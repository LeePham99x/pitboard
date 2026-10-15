//! Processes on Windows, and the programs a Windows shell starts.

use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::AsRawHandle;
use std::path::Path;
use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_INVALID_PARAMETER, FILETIME, HANDLE, WAIT_OBJECT_0,
};
use windows_sys::Win32::Storage::FileSystem::{
    FILE_ATTRIBUTE_REPARSE_POINT, FILE_ATTRIBUTE_TAG_INFO, FILE_EXECUTE,
    FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OPEN_REPARSE_POINT, FILE_READ_ATTRIBUTES, FILE_READ_DATA,
    FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, FileAttributeTagInfo,
    GetFileInformationByHandleEx,
};
use windows_sys::Win32::System::SystemServices::IO_REPARSE_TAG_APPEXECLINK;
use windows_sys::Win32::System::Threading::{
    GetProcessTimes, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE,
    WaitForSingleObject,
};

struct Handle(HANDLE);

impl Drop for Handle {
    fn drop(&mut self) {
        // SAFETY: a handle Windows gave this process, closed this once.
        unsafe {
            CloseHandle(self.0);
        }
    }
}

enum Open {
    Handle(Handle),
    Refused,
    Gone,
}

fn open(pid: u32, access: u32) -> Open {
    // SAFETY: plain values; a handle that comes back is closed by `Handle`.
    let handle = unsafe { OpenProcess(access, 0, pid) };
    if !handle.is_null() {
        return Open::Handle(Handle(handle));
    }
    match std::io::Error::last_os_error().raw_os_error() {
        Some(code) if code == ERROR_INVALID_PARAMETER as i32 => Open::Gone,
        _ => Open::Refused,
    }
}

// Never `GetExitCodeProcess`: a process may exit with 259, which it gives for one running.
pub(crate) fn may_be_running(pid: u32) -> bool {
    match open(pid, PROCESS_SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION) {
        Open::Handle(process) => {
            // SAFETY: an open handle with SYNCHRONIZE, waited on for no time at all.
            unsafe { WaitForSingleObject(process.0, 0) != WAIT_OBJECT_0 }
        }
        Open::Refused => true,
        Open::Gone => false,
    }
}

// As Claude Code records it: the FILETIME as one number.
pub(crate) fn started(pid: u32) -> Option<u64> {
    let Open::Handle(process) = open(pid, PROCESS_QUERY_LIMITED_INFORMATION) else {
        return None;
    };
    let (mut created, mut exited) = (FILETIME::default(), FILETIME::default());
    let (mut kernel, mut user) = (FILETIME::default(), FILETIME::default());
    // SAFETY: an open handle, and four FILETIMEs of this frame.
    let read =
        unsafe { GetProcessTimes(process.0, &mut created, &mut exited, &mut kernel, &mut user) };
    (read != 0)
        .then(|| (u64::from(created.dwHighDateTime) << 32) | u64::from(created.dwLowDateTime))
}

// An app execution alias opens as nothing, so only its tag and its `WindowsApps` folder tell it.
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

pub(crate) fn product_version(path: &Path) -> Option<[u16; 4]> {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Storage::FileSystem::{
        GetFileVersionInfoSizeW, GetFileVersionInfoW, VS_FIXEDFILEINFO, VerQueryValueW,
    };
    let name: Vec<u16> = path.as_os_str().encode_wide().chain([0]).collect();
    let mut ignored = 0u32;
    // SAFETY: a NUL-terminated name, and a u32 of this frame for the handle it ignores.
    let size = unsafe { GetFileVersionInfoSizeW(name.as_ptr(), &raw mut ignored) };
    if size == 0 {
        return None;
    }
    let mut block = vec![0u8; size as usize];
    // SAFETY: a NUL-terminated name, and a buffer of the size it asked for.
    if unsafe { GetFileVersionInfoW(name.as_ptr(), 0, size, block.as_mut_ptr().cast()) } == 0 {
        return None;
    }
    let root: Vec<u16> = "\\".encode_utf16().chain([0]).collect();
    let mut fixed: *mut core::ffi::c_void = std::ptr::null_mut();
    let mut length = 0u32;
    // SAFETY: the block GetFileVersionInfoW filled, which the answer points into.
    let found = unsafe {
        VerQueryValueW(
            block.as_ptr().cast(),
            root.as_ptr(),
            &raw mut fixed,
            &raw mut length,
        )
    };
    if found == 0 || fixed.is_null() || (length as usize) < size_of::<VS_FIXEDFILEINFO>() {
        return None;
    }
    // SAFETY: a VS_FIXEDFILEINFO inside `block`, which is still alive.
    let info = unsafe { fixed.cast::<VS_FIXEDFILEINFO>().read_unaligned() };
    (info.dwSignature == 0xFEEF_04BD).then_some([
        (info.dwProductVersionMS >> 16) as u16,
        info.dwProductVersionMS as u16,
        (info.dwProductVersionLS >> 16) as u16,
        info.dwProductVersionLS as u16,
    ])
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

    // This account's own aliases are only read.
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

    // Version 3, then the package, the app's id, the program and its kind, each NUL-ended.
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

    // An enablement update, as 26200 is over 26100, may leave the binaries' build behind.
    #[test]
    fn cmd_exe_carries_the_build_of_the_windows_it_ships_in() {
        let cmd = std::env::var_os("SystemRoot")
            .map(|windows| PathBuf::from(windows).join(r"System32\cmd.exe"))
            .expect("SystemRoot");
        let [major, minor, build, revision] = product_version(&cmd).expect("its version resource");
        let (_, _, running) = crate::host::windows::user::version().expect("RtlGetVersion");
        println!("cmd.exe {major}.{minor}.{build}.{revision} on build {running}");
        assert_eq!((major, minor), (10, 0));
        assert!(
            (crate::host::WINDOWS_FLOOR..=running).contains(&u32::from(build)),
            "{build} on {running}"
        );
        assert_eq!(
            product_version(&std::env::temp_dir()),
            None,
            "a folder has none"
        );
        let script = scratch("versions").join("plain.cmd");
        std::fs::write(&script, "@echo off").expect("a script");
        assert_eq!(product_version(&script), None, "nor has a script");
    }

    // System's.
    #[test]
    fn pid_4_is_running() {
        assert!(may_be_running(4));
    }

    #[test]
    fn a_process_that_exited_and_one_that_never_was_are_gone() {
        let exited = {
            let mut child = std::process::Command::new(std::env::current_exe().expect("this test"))
                .arg("--list")
                .stdout(std::process::Stdio::null())
                .spawn()
                .expect("it starts");
            let pid = child.id();
            child.wait().expect("it ends");
            pid
        };
        assert!(!may_be_running(exited));
        let never = u32::MAX - 3;
        assert!(!may_be_running(never));
        assert_eq!(started(never), None);

        let me = std::process::id();
        assert!(may_be_running(me));
        assert!(started(me).is_some());
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
