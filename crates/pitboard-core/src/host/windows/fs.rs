//! Files on Windows, before this face is written: nothing is made, changed, moved or removed,
//! and nobody can tell who may read a file.
//!
//! A file on Windows is private through its access control list, not a mode, and replacing
//! one atomically needs more than a rename on some volumes. W15 makes files private to the
//! person and reads who else may read one; W16 replaces, moves and removes them. Until then
//! every change here fails as unsupported, which every caller already reports as a write that
//! failed, and [`access`] says nothing.

use crate::host::{Access, Private};
use crate::service::Permit;
use std::fs::File;
use std::io;
use std::path::Path;
use std::time::SystemTime;

/// What every change here answers until W15 and W16 write it.
fn not_yet() -> io::Error {
    io::Error::new(
        io::ErrorKind::Unsupported,
        "Pitboard does not change files on Windows yet",
    )
}

/// W15 makes it private to the person.
pub(crate) fn create_private_dir(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W15 makes it private to the person.
pub(crate) fn create_private(_: Permit, _path: &Path) -> io::Result<File> {
    Err(not_yet())
}

/// W15 makes it private to the person.
pub(crate) fn open_private_append(_: Permit, _path: &Path) -> io::Result<File> {
    Err(not_yet())
}

/// W15 makes it private to the person, and W16 says how a lock is held on Windows.
pub(crate) fn open_private_lock(_: Permit, _path: &Path) -> io::Result<File> {
    Err(not_yet())
}

/// W15 copies another program's access onto what replaces its file.
pub(crate) fn copy_access(_: Permit, _existing: &Path, _temp: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// Nothing is renamed here, so there is nothing to make durable. W16 says what a rename
/// needs to last on each kind of volume.
pub(crate) fn sync_dir(_dir: &Path) {}

/// W16.
pub(crate) fn touch_dir(_: Permit, _path: &Path, _at: SystemTime) -> io::Result<()> {
    Err(not_yet())
}

/// W16.
pub(crate) fn create_dir_all(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W16, with the locks made of a directory.
pub(crate) fn create_dir(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W16.
pub(crate) fn copy(_: Permit, _from: &Path, _to: &Path) -> io::Result<u64> {
    Err(not_yet())
}

/// W16 replaces a file atomically on every kind of volume.
pub(crate) fn rename(_: Permit, _from: &Path, _to: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W16.
pub(crate) fn remove_file(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W16.
pub(crate) fn remove_dir(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

/// W16.
pub(crate) fn remove_dir_all(_: Permit, _path: &Path) -> io::Result<()> {
    Err(not_yet())
}

// Claude Desktop is macOS only, so nothing asks these here.
pub(crate) fn private_dir(_path: &Path) -> io::Result<Private> {
    Err(not_yet())
}

pub(crate) fn lock_dir(_path: &Path) -> io::Result<File> {
    Err(not_yet())
}

pub(crate) fn move_new(_: Permit, _from: &Path, _to: &Path) -> io::Result<()> {
    Err(not_yet())
}

pub(crate) fn flush_dir(_dir: &Path) -> io::Result<()> {
    Err(not_yet())
}

pub(crate) fn same_volume(_a: &Path, _b: &Path) -> io::Result<bool> {
    Err(not_yet())
}

// The 8.3 name Windows keeps beside a long one, where the volume keeps them (RF-B2).
pub(crate) fn short_path(path: &Path) -> Option<std::path::PathBuf> {
    use std::os::windows::ffi::{OsStrExt, OsStringExt};
    use windows_sys::Win32::Storage::FileSystem::GetShortPathNameW;
    let wide: Vec<u16> = path.as_os_str().encode_wide().chain([0]).collect();
    // SAFETY: a NUL-terminated name; with no buffer it answers the length it needs.
    let needed = unsafe { GetShortPathNameW(wide.as_ptr(), std::ptr::null_mut(), 0) };
    if needed == 0 {
        return None;
    }
    let mut short = vec![0u16; needed as usize];
    // SAFETY: a buffer of the `needed` units it asked for.
    let len = unsafe { GetShortPathNameW(wide.as_ptr(), short.as_mut_ptr(), needed) };
    (len != 0 && len < needed)
        .then(|| std::path::PathBuf::from(std::ffi::OsString::from_wide(&short[..len as usize])))
}

// The volume and the file's id on it (MC-fileid), a folder's included, through a junction.
#[cfg(any(test, feature = "test-support"))]
pub(crate) fn file_id(path: &Path) -> Option<(u64, u128)> {
    use std::os::windows::fs::OpenOptionsExt;
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::Storage::FileSystem::{
        FILE_FLAG_BACKUP_SEMANTICS, FILE_ID_INFO, FILE_READ_ATTRIBUTES, FILE_SHARE_DELETE,
        FILE_SHARE_READ, FILE_SHARE_WRITE, FileIdInfo, GetFileInformationByHandleEx,
    };
    let file = std::fs::OpenOptions::new()
        .access_mode(FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE)
        .custom_flags(FILE_FLAG_BACKUP_SEMANTICS)
        .open(path)
        .ok()?;
    let mut info = FILE_ID_INFO::default();
    // SAFETY: an open handle, and a FILE_ID_INFO buffer of exactly the length given.
    let read = unsafe {
        GetFileInformationByHandleEx(
            file.as_raw_handle(),
            FileIdInfo,
            (&raw mut info).cast(),
            size_of::<FILE_ID_INFO>() as u32,
        )
    };
    (read != 0).then(|| {
        (
            info.VolumeSerialNumber,
            u128::from_le_bytes(info.FileId.Identifier),
        )
    })
}

/// Who besides its owner can reach `path`: nobody can tell until W15 reads its access
/// control list.
pub(crate) fn access(_path: &Path) -> Option<Access> {
    None
}

/// What a test or a fixture does to a file's access, or to make a link, on Windows: an
/// access control list and a link Windows lets this account make, which W15 sets and makes.
/// Until then each says it cannot, as an app that cannot make a fixture says so, but for
/// making a file runnable.
#[cfg(any(test, feature = "test-support"))]
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub mod testing {
    use std::io;
    use std::path::Path;

    fn not_yet() -> io::Error {
        io::Error::new(
            io::ErrorKind::Unsupported,
            "a file's access on Windows is set by W15",
        )
    }

    /// A file anybody may read.
    pub fn open_to_others(_path: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A file or a directory only its owner may reach, as Pitboard makes its own.
    pub fn make_private(_path: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// Whether `path` is private as Pitboard makes its own files and directories.
    pub fn is_private(_path: &Path) -> io::Result<bool> {
        Err(not_yet())
    }

    /// A file its owner may read and nobody may change.
    pub fn read_only_for_owner(_path: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A file nobody may read or change, its owner included.
    pub fn deny_reading(_path: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A file anybody may run.
    // Windows has no run bit: a file runs by its extension and the ACL it inherits.
    pub fn make_runnable(path: &Path) -> io::Result<()> {
        std::fs::metadata(path).map(drop)
    }

    /// A file nobody may run, which anybody may still read.
    pub fn deny_running(_path: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A directory nothing can be added to or removed from.
    pub fn deny_changes(_dir: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A directory its owner may change again.
    pub fn allow_changes(_dir: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A link at `at` that leads to the file `target`.
    pub fn link(_target: &Path, _at: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    /// A link at `at` that leads to the directory `target`.
    pub fn link_dir(_target: &Path, _at: &Path) -> io::Result<()> {
        Err(not_yet())
    }

    pub fn inode(_path: &Path) -> io::Result<u64> {
        Err(not_yet())
    }
}

#[cfg(test)]
mod tests {
    use super::testing;
    use std::io;
    use std::path::Path;

    /// Until W15 sets a Windows access control list, every way a test or a fixture changes a
    /// file's access, or makes a link, says it cannot, and none pretends it did.
    #[test]
    fn every_change_to_a_files_access_waits_for_w15() {
        let path = Path::new("never-made");
        let answers: [(&str, io::Result<()>); 10] = [
            ("open_to_others", testing::open_to_others(path)),
            ("make_private", testing::make_private(path)),
            ("read_only_for_owner", testing::read_only_for_owner(path)),
            ("deny_reading", testing::deny_reading(path)),
            ("deny_running", testing::deny_running(path)),
            ("deny_changes", testing::deny_changes(path)),
            ("allow_changes", testing::allow_changes(path)),
            ("link", testing::link(path, path)),
            ("link_dir", testing::link_dir(path, path)),
            ("inode", testing::inode(path).map(drop)),
        ];
        for (what, answer) in answers {
            let refused = answer.expect_err(what);
            assert_eq!(refused.kind(), io::ErrorKind::Unsupported, "{what}");
        }
        let private = testing::is_private(path).expect_err("is_private");
        assert_eq!(private.kind(), io::ErrorKind::Unsupported);
        assert!(!path.exists(), "nothing was made");
    }

    #[test]
    fn a_path_with_a_space_has_a_short_name_that_leads_to_it() {
        let long = std::env::temp_dir().join(format!("pitboard short {}", std::process::id()));
        std::fs::create_dir_all(&long).expect("a scratch folder");
        let short = super::short_path(&long).expect("a short name");
        assert!(
            !short.to_string_lossy().contains(' '),
            "{}",
            short.display()
        );
        assert_eq!(
            std::fs::canonicalize(&short).expect("there"),
            std::fs::canonicalize(&long).expect("there")
        );
        assert_eq!(super::short_path(&long.join("never-made")), None);
        let _ = std::fs::remove_dir_all(&long);
    }

    // A junction needs no right of its own, unlike a symbolic link.
    #[test]
    fn a_folder_has_one_id_through_a_junction() {
        let root = std::env::temp_dir().join(format!("pitboard-file-id-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let (target, other, junction) = (
            root.join("target"),
            root.join("other"),
            root.join("junction"),
        );
        for folder in [&target, &other] {
            std::fs::create_dir_all(folder).expect("a scratch folder");
        }
        let made = std::process::Command::new("cmd.exe")
            .args(["/d", "/c", "mklink", "/J"])
            .arg(&junction)
            .arg(&target)
            .output()
            .expect("cmd runs");
        assert!(made.status.success(), "{made:?}");
        let id = super::file_id(&target).expect("an id");
        assert_eq!(super::file_id(&junction), Some(id));
        assert_ne!(super::file_id(&other), Some(id));
        assert_eq!(super::file_id(&root.join("never-made")), None);
        let _ = std::fs::remove_dir(&junction);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn a_file_a_test_wrote_is_runnable_as_it_is() {
        let dir = std::env::temp_dir().join(format!("pitboard-runnable-{}", std::process::id()));
        std::fs::create_dir_all(&dir).expect("a scratch folder");
        let program = dir.join("program.exe");
        std::fs::copy(std::env::current_exe().expect("this test"), &program).expect("copied");
        let before = std::fs::read(&program).expect("read");
        testing::make_runnable(&program).expect("runnable as it is");
        assert_eq!(std::fs::read(&program).expect("read"), before, "unchanged");
        let missing = testing::make_runnable(&dir.join("absent.exe")).expect_err("absent");
        assert_eq!(missing.kind(), io::ErrorKind::NotFound);
        let ran = std::process::Command::new(&program)
            .args(["--list", "--format=terse"])
            .output()
            .expect("the copy runs");
        assert!(ran.status.success(), "{ran:?}");
        let _ = std::fs::remove_dir_all(&dir);
    }
}
