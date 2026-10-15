//! Processes on Windows, and the programs a Windows shell starts.

use crate::host::Listed;
use crate::host::owner::{self, Owner, Told};
use std::collections::HashMap;
use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::AsRawHandle;
use std::path::{Path, PathBuf};
use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_INSUFFICIENT_BUFFER, ERROR_INVALID_PARAMETER, ERROR_SUCCESS, FILETIME,
    HANDLE, INVALID_HANDLE_VALUE, WAIT_OBJECT_0,
};
use windows_sys::Win32::Storage::FileSystem::{
    FILE_ATTRIBUTE_REPARSE_POINT, FILE_ATTRIBUTE_TAG_INFO, FILE_EXECUTE,
    FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OPEN_REPARSE_POINT, FILE_READ_ATTRIBUTES, FILE_READ_DATA,
    FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, FileAttributeTagInfo,
    GetFileInformationByHandleEx,
};
use windows_sys::Win32::Storage::Packaging::Appx::GetPackageFamilyName;
use windows_sys::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};
use windows_sys::Win32::System::RemoteDesktop::{
    WTS_CURRENT_SERVER_HANDLE, WTS_INFO_CLASS, WTS_PROCESS_INFO_EXW, WTSDomainName,
    WTSEnumerateProcessesExW, WTSFreeMemory, WTSFreeMemoryExW, WTSQuerySessionInformationW,
    WTSTypeProcessInfoLevel1, WTSUserName,
};
use windows_sys::Win32::System::SystemServices::IO_REPARSE_TAG_APPEXECLINK;
use windows_sys::Win32::System::Threading::{
    GetProcessTimes, OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
    PROCESS_SYNCHRONIZE, QueryFullProcessImageNameW, WaitForSingleObject,
};

// wtsapi32.h's, which windows-sys does not carry.
const WTS_ANY_SESSION: u32 = 0xFFFF_FFFE;

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

// Toolhelp for the parents, which WTS does not give; WTS for owners without opening anything.
pub(crate) fn listing() -> Result<Vec<Listed>, String> {
    let own = super::user::sid().ok_or("Windows did not say who this account is")?;
    let all = snapshot().map_err(|e| format!("Windows did not list its processes: {e}"))?;
    let listed = listed_by_wts();
    let mut sessions: HashMap<u32, bool> = HashMap::new();
    let mut found = Vec::new();
    for (pid, parent, name) in all {
        let said = listed.get(&pid);
        let owner = owner::owner(
            &own,
            said.and_then(|(_, sid)| sid.as_deref()),
            || {
                said.is_some_and(|&(session, _)| {
                    *sessions
                        .entry(session)
                        .or_insert_with(|| session_user(session).is_some_and(|its| its != own))
                })
            },
            || token_owner(pid),
        );
        let unplaced = || Listed {
            pid,
            parent,
            path: PathBuf::from(&name),
            package: None,
            placed: false,
        };
        match owner {
            Owner::Other => {}
            Owner::Unread => found.push(unplaced()),
            Owner::Own => match open(pid, PROCESS_QUERY_LIMITED_INFORMATION) {
                Open::Gone => {}
                Open::Refused => found.push(unplaced()),
                Open::Handle(process) => found.push(match image(&process) {
                    Some(path) => Listed {
                        pid,
                        parent,
                        path,
                        package: package_family(&process),
                        placed: true,
                    },
                    None => unplaced(),
                }),
            },
        }
    }
    Ok(found)
}

fn snapshot() -> std::io::Result<Vec<(u32, u32, String)>> {
    // SAFETY: a snapshot of every process; 0 names no one process.
    let snap = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if snap == INVALID_HANDLE_VALUE {
        return Err(std::io::Error::last_os_error());
    }
    let snap = Handle(snap);
    let mut entry = PROCESSENTRY32W {
        dwSize: size_of::<PROCESSENTRY32W>() as u32,
        ..PROCESSENTRY32W::default()
    };
    let mut all = Vec::new();
    // SAFETY: an open snapshot, and an entry whose size it carries.
    let mut more = unsafe { Process32FirstW(snap.0, &mut entry) } != 0;
    while more {
        let length = entry
            .szExeFile
            .iter()
            .position(|&unit| unit == 0)
            .unwrap_or(entry.szExeFile.len());
        all.push((
            entry.th32ProcessID,
            entry.th32ParentProcessID,
            String::from_utf16_lossy(&entry.szExeFile[..length]),
        ));
        // SAFETY: as above.
        more = unsafe { Process32NextW(snap.0, &mut entry) } != 0;
    }
    Ok(all)
}

// Each process's session, and its user's SID where WTS gives one.
fn listed_by_wts() -> HashMap<u32, (u32, Option<String>)> {
    let (mut level, mut count) = (1u32, 0u32);
    let mut info: windows_sys::core::PWSTR = std::ptr::null_mut();
    // SAFETY: level 1 answers WTS_PROCESS_INFO_EXW, in memory freed below.
    let listed = unsafe {
        WTSEnumerateProcessesExW(
            WTS_CURRENT_SERVER_HANDLE,
            &mut level,
            WTS_ANY_SESSION,
            &mut info,
            &mut count,
        )
    };
    if listed == 0 || info.is_null() {
        return HashMap::new();
    }
    let entries = info.cast::<WTS_PROCESS_INFO_EXW>();
    let told = (0..count as usize)
        .map(|index| {
            // SAFETY: `count` entries, alive until freed below.
            let entry = unsafe { &*entries.add(index) };
            let sid = (!entry.pUserSid.is_null())
                .then(|| super::user::sid_text(entry.pUserSid))
                .flatten();
            (entry.ProcessId, (entry.SessionId, sid))
        })
        .collect();
    // SAFETY: the memory WTSEnumerateProcessesExW gave, of `count` level 1 entries.
    unsafe {
        WTSFreeMemoryExW(WTSTypeProcessInfoLevel1, info.cast(), count);
    }
    told
}

fn session_user(session: u32) -> Option<String> {
    let name = session_text(session, WTSUserName).filter(|name| !name.is_empty())?;
    let domain = session_text(session, WTSDomainName).unwrap_or_default();
    super::user::account_sid(&format!("{domain}\\{name}"))
}

fn session_text(session: u32, class: WTS_INFO_CLASS) -> Option<String> {
    let mut text: windows_sys::core::PWSTR = std::ptr::null_mut();
    let mut bytes = 0u32;
    // SAFETY: WTS allocates the answer, freed below.
    let read = unsafe {
        WTSQuerySessionInformationW(
            WTS_CURRENT_SERVER_HANDLE,
            session,
            class,
            &mut text,
            &mut bytes,
        )
    };
    if read == 0 || text.is_null() {
        return None;
    }
    let units = bytes as usize / size_of::<u16>();
    // SAFETY: `bytes` bytes of UTF-16, its NUL among them.
    let said = String::from_utf16(unsafe { std::slice::from_raw_parts(text, units) }).ok();
    // SAFETY: the memory WTSQuerySessionInformationW gave, freed this once.
    unsafe { WTSFreeMemory(text.cast()) };
    said.map(|said| said.trim_end_matches('\0').to_owned())
}

fn token_owner(pid: u32) -> Told {
    match open(pid, PROCESS_QUERY_LIMITED_INFORMATION) {
        Open::Handle(process) => {
            super::user::token_user(process.0).map_or(Told::Refused, Told::User)
        }
        Open::Refused => Told::Refused,
        Open::Gone => Told::Gone,
    }
}

fn image(process: &Handle) -> Option<PathBuf> {
    let mut buffer = vec![0u16; 1024];
    loop {
        let mut length = buffer.len() as u32;
        // SAFETY: an open handle, and a buffer of the length given.
        let read = unsafe {
            QueryFullProcessImageNameW(
                process.0,
                PROCESS_NAME_WIN32,
                buffer.as_mut_ptr(),
                &mut length,
            )
        };
        if read != 0 {
            return Some(PathBuf::from(String::from_utf16_lossy(
                &buffer[..length as usize],
            )));
        }
        let short = std::io::Error::last_os_error().raw_os_error()
            == Some(ERROR_INSUFFICIENT_BUFFER as i32);
        if !short || buffer.len() >= 32_768 {
            return None;
        }
        buffer.resize(buffer.len() * 2, 0);
    }
}

fn package_family(process: &Handle) -> Option<String> {
    let mut buffer = vec![0u16; 128];
    let mut length = buffer.len() as u32;
    // SAFETY: an open handle, and a buffer of the length given.
    let mut said = unsafe { GetPackageFamilyName(process.0, &mut length, buffer.as_mut_ptr()) };
    if said == ERROR_INSUFFICIENT_BUFFER {
        buffer.resize(length as usize, 0);
        // SAFETY: as above, with the length it asked for.
        said = unsafe { GetPackageFamilyName(process.0, &mut length, buffer.as_mut_ptr()) };
    }
    // APPMODEL_ERROR_NO_PACKAGE outside a package.
    (said == ERROR_SUCCESS)
        .then(|| String::from_utf16_lossy(&buffer[..(length as usize).saturating_sub(1)]))
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

    // System's, whose token a standard user is not expected to read.
    #[test]
    fn pid_4_is_running_and_never_absent_or_this_users() {
        assert!(may_be_running(4));
        let told = token_owner(4);
        println!("pid 4's token: {told:?}");
        assert_ne!(told, Told::Gone);
        assert_ne!(
            told,
            Told::User(crate::host::windows::user::sid().expect("this user"))
        );
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
        assert_eq!(token_owner(never), Told::Gone);
        assert_eq!(started(never), None);

        let me = std::process::id();
        assert!(may_be_running(me));
        assert_eq!(
            token_owner(me),
            crate::host::windows::user::sid().map_or(Told::Refused, Told::User)
        );
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
