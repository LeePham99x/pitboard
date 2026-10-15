// Files on Windows. What Pitboard makes is private through its access list from the moment it
// is made. Replacing, moving and removing one is W16's: until then each refuses as
// unsupported, which every caller reports as a write that failed.

use crate::host::acl::{self, Ace};
use crate::host::{Access, Private};
use crate::service::Permit;
use std::fs::File;
use std::io;
use std::os::windows::ffi::OsStrExt;
use std::os::windows::io::FromRawHandle;
use std::path::Path;
use std::time::SystemTime;
use windows_sys::Win32::Foundation::{GENERIC_WRITE, INVALID_HANDLE_VALUE, LocalFree};
use windows_sys::Win32::Security::Authorization::{
    ConvertStringSecurityDescriptorToSecurityDescriptorW, GetNamedSecurityInfoW, SDDL_REVISION_1,
    SE_FILE_OBJECT,
};
use windows_sys::Win32::Security::{
    ACCESS_ALLOWED_ACE, ACE_HEADER, ACL, DACL_SECURITY_INFORMATION, GetAce,
    OWNER_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR, PSID, SECURITY_ATTRIBUTES,
};
use windows_sys::Win32::Storage::FileSystem::{
    CREATE_NEW, CreateDirectoryW, CreateFileW, FILE_FLAG_OPEN_REPARSE_POINT, FILE_GENERIC_WRITE,
    FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, FILE_WRITE_DATA, OPEN_ALWAYS,
};

fn not_yet() -> io::Error {
    io::Error::new(
        io::ErrorKind::Unsupported,
        "Pitboard does not move, replace or remove files on Windows yet",
    )
}

fn wide(path: &Path) -> Vec<u16> {
    path.as_os_str().encode_wide().chain([0]).collect()
}

// Each missing parent is made the same way. A folder already there keeps its own access list,
// which `pitboard doctor` reads.
pub(crate) fn create_private_dir(_: Permit, path: &Path) -> io::Result<()> {
    make_all(path, &Descriptor::private(true)?)
}

// CREATE_NEW refuses whatever is there, and a link is not followed to find out.
pub(crate) fn create_private(_: Permit, path: &Path) -> io::Result<File> {
    let made = Descriptor::private(false)?;
    open(
        path,
        GENERIC_WRITE,
        CREATE_NEW,
        FILE_FLAG_OPEN_REPARSE_POINT,
        &made,
    )
}

// The descriptor is read only to make the file: one already there keeps its own access list.
pub(crate) fn open_private_append(_: Permit, path: &Path) -> io::Result<File> {
    let made = Descriptor::private(false)?;
    open(
        path,
        FILE_GENERIC_WRITE & !FILE_WRITE_DATA,
        OPEN_ALWAYS,
        0,
        &made,
    )
}

pub(crate) fn open_private_lock(_: Permit, path: &Path) -> io::Result<File> {
    let made = Descriptor::private(false)?;
    open(path, GENERIC_WRITE, OPEN_ALWAYS, 0, &made)
}

// As std's `OpenOptions` opens a file, sharing all three ways, but made with `made`.
fn open(
    path: &Path,
    access: u32,
    disposition: u32,
    flags: u32,
    made: &Descriptor,
) -> io::Result<File> {
    let name = wide(path);
    let attributes = made.attributes();
    // SAFETY: a NUL-terminated name, and a descriptor that outlives the call.
    let handle = unsafe {
        CreateFileW(
            name.as_ptr(),
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            &raw const attributes,
            disposition,
            flags,
            std::ptr::null_mut(),
        )
    };
    if handle == INVALID_HANDLE_VALUE {
        return Err(io::Error::last_os_error());
    }
    // SAFETY: a handle CreateFileW just opened, which the File owns from here.
    Ok(unsafe { File::from_raw_handle(handle) })
}

// As std's `DirBuilder` makes a folder and its parents, each with `made`.
fn make_all(path: &Path, made: &Descriptor) -> io::Result<()> {
    if path == Path::new("") {
        return Ok(());
    }
    match make(path, made) {
        Ok(()) => return Ok(()),
        Err(e) if e.kind() == io::ErrorKind::NotFound => {}
        Err(_) if path.is_dir() => return Ok(()),
        Err(e) => return Err(e),
    }
    match path.parent() {
        Some(parent) => make_all(parent, made)?,
        None => return Err(io::Error::other("failed to create whole tree")),
    }
    match make(path, made) {
        Ok(()) => Ok(()),
        Err(_) if path.is_dir() => Ok(()),
        Err(e) => Err(e),
    }
}

fn make(path: &Path, made: &Descriptor) -> io::Result<()> {
    let name = wide(path);
    let attributes = made.attributes();
    // SAFETY: a NUL-terminated name, and a descriptor that outlives the call.
    if unsafe { CreateDirectoryW(name.as_ptr(), &raw const attributes) } == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(())
}

// A security descriptor made from SDDL, freed when dropped.
struct Descriptor(PSECURITY_DESCRIPTOR);

impl Descriptor {
    fn from_sddl(sddl: &str) -> io::Result<Descriptor> {
        let text: Vec<u16> = sddl.encode_utf16().chain([0]).collect();
        let mut made: PSECURITY_DESCRIPTOR = std::ptr::null_mut();
        // SAFETY: a NUL-terminated SDDL, and `made` is LocalAlloc'd on success, freed on drop.
        let converted = unsafe {
            ConvertStringSecurityDescriptorToSecurityDescriptorW(
                text.as_ptr(),
                SDDL_REVISION_1,
                &raw mut made,
                std::ptr::null_mut(),
            )
        };
        if converted == 0 || made.is_null() {
            return Err(io::Error::last_os_error());
        }
        Ok(Descriptor(made))
    }

    fn private(folder: bool) -> io::Result<Descriptor> {
        Descriptor::from_sddl(&acl::private(&you()?, folder))
    }

    fn attributes(&self) -> SECURITY_ATTRIBUTES {
        SECURITY_ATTRIBUTES {
            nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: self.0,
            bInheritHandle: 0,
        }
    }
}

impl Drop for Descriptor {
    fn drop(&mut self) {
        // SAFETY: the LocalAlloc'd descriptor the conversion gave, freed this once.
        unsafe { LocalFree(self.0) };
    }
}

fn you() -> io::Result<String> {
    crate::host::user::sid()
        .ok_or_else(|| io::Error::other("this Windows account's SID could not be read"))
}

// A path's owner and access list as Windows reads them, freed when dropped.
struct Security {
    descriptor: PSECURITY_DESCRIPTOR,
    owner: PSID,
    dacl: *mut ACL,
}

impl Security {
    fn of(path: &Path) -> io::Result<Security> {
        let name = wide(path);
        let mut read = Security {
            descriptor: std::ptr::null_mut(),
            owner: std::ptr::null_mut(),
            dacl: std::ptr::null_mut(),
        };
        // SAFETY: a NUL-terminated name; the owner and the list point into the descriptor,
        // which is freed when `read` drops.
        let failed = unsafe {
            GetNamedSecurityInfoW(
                name.as_ptr(),
                SE_FILE_OBJECT,
                OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
                &raw mut read.owner,
                std::ptr::null_mut(),
                &raw mut read.dacl,
                std::ptr::null_mut(),
                &raw mut read.descriptor,
            )
        };
        if failed != 0 {
            return Err(io::Error::from_raw_os_error(failed as i32));
        }
        Ok(read)
    }

    // `None` for a NULL DACL, which lets everyone in.
    fn aces(&self) -> Option<Vec<Ace>> {
        if self.dacl.is_null() {
            return None;
        }
        // SAFETY: the ACL GetNamedSecurityInfoW gave, inside the descriptor.
        let count = u32::from(unsafe { (*self.dacl).AceCount });
        Some((0..count).filter_map(|at| self.ace(at)).collect())
    }

    fn ace(&self, at: u32) -> Option<Ace> {
        let mut ace: *mut core::ffi::c_void = std::ptr::null_mut();
        // SAFETY: a valid ACL, and an index below its count.
        if unsafe { GetAce(self.dacl, at, &raw mut ace) } == 0 || ace.is_null() {
            return None;
        }
        // SAFETY: every ACE starts with its header.
        let header = unsafe { &*ace.cast::<ACE_HEADER>() };
        // A conditional allow is taken as met, and a conditional deny as not.
        let allows = match header.AceType {
            0 | 9 => true,
            1 => false,
            _ => return None,
        };
        // SAFETY: an allow, a deny and a conditional allow share this layout, the SID at
        // SidStart.
        let body = unsafe { &*ace.cast::<ACCESS_ALLOWED_ACE>() };
        let sid: PSID = (&raw const body.SidStart).cast_mut().cast();
        Some(Ace {
            allows,
            sid: crate::host::user::sid_text(sid).unwrap_or_default(),
            mask: body.Mask,
            flags: header.AceFlags,
        })
    }

    #[cfg(any(test, feature = "test-support"))]
    fn protected(&self) -> bool {
        use windows_sys::Win32::Security::{GetSecurityDescriptorControl, SE_DACL_PROTECTED};
        let (mut control, mut revision) = (0u16, 0u32);
        // SAFETY: the descriptor GetNamedSecurityInfoW gave.
        let read = unsafe {
            GetSecurityDescriptorControl(self.descriptor, &raw mut control, &raw mut revision)
        };
        read != 0 && control & SE_DACL_PROTECTED != 0
    }

    #[cfg(test)]
    fn owner(&self) -> Option<String> {
        (!self.owner.is_null())
            .then(|| crate::host::user::sid_text(self.owner))
            .flatten()
    }
}

impl Drop for Security {
    fn drop(&mut self) {
        // SAFETY: null, or the LocalAlloc'd descriptor GetNamedSecurityInfoW gave.
        unsafe { LocalFree(self.descriptor) };
    }
}

// Who besides the person reaches `path`, from its access list. `None` where it cannot be read.
pub(crate) fn access(path: &Path) -> Option<Access> {
    let read = Security::of(path).ok()?;
    let you = crate::host::user::sid();
    Some(acl::access(read.aces().as_deref(), you.as_deref()))
}

// W16 copies another program's access onto what replaces its file.
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
    use std::os::windows::ffi::OsStringExt;
    use windows_sys::Win32::Storage::FileSystem::GetShortPathNameW;
    let wide = wide(path);
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

// MC-sync: asked of the deepest folder there, since a folder not made yet is in no root.
pub(crate) fn in_a_sync_root(path: &Path) -> bool {
    use windows_sys::Win32::Storage::CloudFilters::{
        CF_SYNC_ROOT_BASIC_INFO, CF_SYNC_ROOT_INFO_BASIC, CfGetSyncRootInfoByPath,
    };
    let Some(there) = path.ancestors().find(|above| above.exists()) else {
        return false;
    };
    let wide = wide(there);
    let mut info = CF_SYNC_ROOT_BASIC_INFO::default();
    let mut written = 0u32;
    // SAFETY: a NUL-terminated name, and a buffer of exactly the length given.
    let answer = unsafe {
        CfGetSyncRootInfoByPath(
            wide.as_ptr(),
            CF_SYNC_ROOT_INFO_BASIC,
            (&raw mut info).cast(),
            size_of::<CF_SYNC_ROOT_BASIC_INFO>() as u32,
            &raw mut written,
        )
    };
    answer >= 0
}

// The volume and the file's id on it (MC-fileid), a folder's included, through a junction.
#[cfg(any(test, feature = "test-support"))]
pub(crate) fn file_id(path: &Path) -> Option<(u64, u128)> {
    id(path, 0).ok()
}

// `FILE_FLAG_OPEN_REPARSE_POINT` in `flags` reads a link's own.
#[cfg(any(test, feature = "test-support"))]
fn id(path: &Path, flags: u32) -> io::Result<(u64, u128)> {
    use std::os::windows::fs::OpenOptionsExt;
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::Storage::FileSystem::{
        FILE_FLAG_BACKUP_SEMANTICS, FILE_ID_INFO, FILE_READ_ATTRIBUTES, FileIdInfo,
        GetFileInformationByHandleEx,
    };
    let file = std::fs::OpenOptions::new()
        .access_mode(FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE)
        .custom_flags(FILE_FLAG_BACKUP_SEMANTICS | flags)
        .open(path)?;
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
    if read == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok((
        info.VolumeSerialNumber,
        u128::from_le_bytes(info.FileId.Identifier),
    ))
}

// A junction needs no right of its own, unlike a symbolic link, and leads to a full path on a
// local volume: std's own maker is not stable yet, so this writes the mount point as it does.
#[cfg(any(test, feature = "test-support"))]
fn junction(target: &Path, at: &Path) -> io::Result<()> {
    use std::os::windows::fs::OpenOptionsExt;
    use std::os::windows::io::AsRawHandle;
    use std::path::{Component, Prefix};
    use windows_sys::Win32::Storage::FileSystem::FILE_FLAG_BACKUP_SEMANTICS;
    use windows_sys::Win32::System::IO::DeviceIoControl;
    use windows_sys::Win32::System::Ioctl::FSCTL_SET_REPARSE_POINT;
    use windows_sys::Win32::System::SystemServices::IO_REPARSE_TAG_MOUNT_POINT;

    let refused = |why: &str| io::Error::new(io::ErrorKind::InvalidInput, why);
    let full = std::path::absolute(at.parent().unwrap_or(Path::new("")).join(target))?;
    let full = crate::host::without_verbatim_prefix(full);
    let local = match full.components().next() {
        Some(Component::Prefix(prefix)) => matches!(prefix.kind(), Prefix::Disk(_)),
        _ => false,
    };
    if !local {
        return Err(refused("a junction leads to a folder on a local volume"));
    }
    let name: Vec<u16> = r"\??\"
        .encode_utf16()
        .chain(full.as_os_str().encode_wide())
        .collect();
    let bytes = u16::try_from(name.len() * 2)
        .ok()
        .filter(|bytes| *bytes <= u16::MAX - 12)
        .ok_or_else(|| refused("the junction's target is too long"))?;
    let mut data: Vec<u8> = Vec::new();
    data.extend(IO_REPARSE_TAG_MOUNT_POINT.to_le_bytes());
    data.extend((bytes + 12).to_le_bytes());
    data.extend(0u16.to_le_bytes());
    // The substitute name first, then an empty print name, each ending in a NUL.
    for field in [0, bytes, bytes + 2, 0] {
        data.extend(field.to_le_bytes());
    }
    data.extend(name.iter().flat_map(|unit| unit.to_le_bytes()));
    data.extend([0u8; 4]);

    std::fs::create_dir(at)?;
    let set = std::fs::OpenOptions::new()
        .write(true)
        .custom_flags(FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT)
        .open(at)
        .and_then(|folder| {
            let mut returned = 0u32;
            // SAFETY: an open folder, and a mount point's reparse data of exactly the length
            // given.
            let set = unsafe {
                DeviceIoControl(
                    folder.as_raw_handle(),
                    FSCTL_SET_REPARSE_POINT,
                    data.as_ptr().cast(),
                    data.len() as u32,
                    std::ptr::null_mut(),
                    0,
                    &raw mut returned,
                    std::ptr::null_mut(),
                )
            };
            if set == 0 {
                Err(io::Error::last_os_error())
            } else {
                Ok(())
            }
        });
    if set.is_err() {
        let _ = std::fs::remove_dir(at);
    }
    set
}

// Each sets a whole protected access list, as `chmod` sets a whole mode, granting the person,
// SYSTEM and Administrators what the mode gives its owner.
#[cfg(any(test, feature = "test-support"))]
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub mod testing {
    use super::{Descriptor, Security, you};
    use crate::host::acl;
    use std::io;
    use std::path::Path;
    use windows_sys::Win32::Security::Authorization::{SE_FILE_OBJECT, SetNamedSecurityInfoW};
    use windows_sys::Win32::Security::{
        DACL_SECURITY_INFORMATION, GetSecurityDescriptorDacl, PROTECTED_DACL_SECURITY_INFORMATION,
    };

    // Read and run, as a folder's 5 or a program's 755 give others.
    const READ_AND_RUN: &str = "0x1200a9";
    const EXECUTE: u32 = 0x20;

    pub(super) fn set(path: &Path, dacl: &str) -> io::Result<()> {
        let made = Descriptor::from_sddl(dacl)?;
        let (mut present, mut defaulted) = (0, 0);
        let mut list = std::ptr::null_mut();
        // SAFETY: a descriptor just made, whose list stays in it while `made` lives.
        let read = unsafe {
            GetSecurityDescriptorDacl(made.0, &raw mut present, &raw mut list, &raw mut defaulted)
        };
        if read == 0 {
            return Err(io::Error::last_os_error());
        }
        let name = super::wide(path);
        // SAFETY: a NUL-terminated name, and a list that outlives the call.
        let failed = unsafe {
            SetNamedSecurityInfoW(
                name.as_ptr(),
                SE_FILE_OBJECT,
                DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                list,
                std::ptr::null(),
            )
        };
        if failed != 0 {
            return Err(io::Error::from_raw_os_error(failed as i32));
        }
        Ok(())
    }

    fn folder(path: &Path) -> io::Result<bool> {
        Ok(std::fs::metadata(path)?.is_dir())
    }

    fn passed_on(folder: bool) -> &'static str {
        if folder { "OICI" } else { "" }
    }

    /// A file anybody may read.
    pub fn open_to_others(path: &Path) -> io::Result<()> {
        let folder = folder(path)?;
        let on = passed_on(folder);
        let you = you()?;
        set(
            path,
            &format!("D:P{}(A;{on};FR;;;BU)", acl::private_aces(&you, folder)),
        )
    }

    /// A file or a directory only its owner may reach, as Pitboard makes its own.
    pub fn make_private(path: &Path) -> io::Result<()> {
        let folder = folder(path)?;
        set(path, &format!("D:P{}", acl::private_aces(&you()?, folder)))
    }

    /// Whether `path` is private as Pitboard makes its own files and directories.
    pub fn is_private(path: &Path) -> io::Result<bool> {
        let folder = folder(path)?;
        let read = Security::of(path)?;
        let you = you()?;
        Ok(read
            .aces()
            .is_some_and(|aces| acl::is_private(read.protected(), &aces, &you, folder)))
    }

    /// A file its owner may read and nobody may change.
    pub fn read_only_for_owner(path: &Path) -> io::Result<()> {
        let on = passed_on(folder(path)?);
        set(path, &format!("D:P(A;{on};FR;;;{})", you()?))
    }

    /// A file nobody may read or change, its owner included.
    pub fn deny_reading(path: &Path) -> io::Result<()> {
        let folder = folder(path)?;
        let you = you()?;
        set(
            path,
            &format!("D:P(D;;0x7;;;WD){}", acl::private_aces(&you, folder)),
        )
    }

    /// A file anybody may run.
    // Windows has no run bit: a file runs by its extension and its access list, so only what
    // `deny_running` set is undone.
    pub fn make_runnable(path: &Path) -> io::Result<()> {
        folder(path)?;
        let denied = Security::of(path)?.aces().is_some_and(|aces| {
            aces.iter()
                .any(|ace| !ace.allows && ace.mask & EXECUTE != 0)
        });
        if !denied {
            return Ok(());
        }
        let you = you()?;
        set(
            path,
            &format!(
                "D:P{}(A;;{READ_AND_RUN};;;BU)",
                acl::private_aces(&you, false)
            ),
        )
    }

    /// A file nobody may run, which anybody may still read.
    pub fn deny_running(path: &Path) -> io::Result<()> {
        folder(path)?;
        let you = you()?;
        set(
            path,
            &format!(
                "D:P(D;;0x20;;;WD){}(A;;FR;;;BU)",
                acl::private_aces(&you, false)
            ),
        )
    }

    /// A directory nothing can be added to or removed from.
    // Nothing is added to the folder or removed from it, and nothing in it is removed: except
    // what keeps an access list of its own, as Pitboard's files do, which Windows asks alone.
    pub fn deny_changes(dir: &Path) -> io::Result<()> {
        let you = you()?;
        set(
            dir,
            &format!(
                "D:P(D;;0x46;;;WD)(D;OICIIO;SD;;;WD){}(A;OICI;{READ_AND_RUN};;;BU)",
                acl::private_aces(&you, true)
            ),
        )
    }

    /// A directory its owner may change again.
    pub fn allow_changes(dir: &Path) -> io::Result<()> {
        let you = you()?;
        set(
            dir,
            &format!(
                "D:P{}(A;OICI;{READ_AND_RUN};;;BU)",
                acl::private_aces(&you, true)
            ),
        )
    }

    /// A link at `at` that leads to the file `target`.
    // Developer Mode or the symbolic-link right, which CI's runners have (AGENTS.md).
    pub fn link(target: &Path, at: &Path) -> io::Result<()> {
        std::os::windows::fs::symlink_file(target, at)
    }

    /// A link at `at` that leads to the directory `target`.
    pub fn link_dir(target: &Path, at: &Path) -> io::Result<()> {
        super::junction(target, at)
    }

    // A link's own, folded to 64 bits: how a test tells a move from a copy.
    pub fn inode(path: &Path) -> io::Result<u64> {
        let (_, id) = super::id(path, super::FILE_FLAG_OPEN_REPARSE_POINT)?;
        Ok((id as u64) ^ ((id >> 64) as u64))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::host::{Kind, Os};
    use std::io::Write;

    fn scratch(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "pitboard-host-fs-{name}-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    fn private_to_you(path: &Path, folder: bool) {
        let you = crate::host::user::sid().expect("this account's SID");
        let read = Security::of(path).expect("its security");
        assert!(read.protected(), "{}", path.display());
        assert_eq!(read.owner(), Some(you.clone()), "{}", path.display());
        let aces = read.aces().expect("a list");
        assert!(
            acl::is_private(true, &aces, &you, folder),
            "{}: {aces:?}",
            path.display()
        );
    }

    #[test]
    fn what_pitboard_makes_is_the_persons_alone_and_what_is_made_inside_takes_it() {
        let dir = scratch("private");
        let permit = Permit::for_a_test();
        let nested = dir.join("a").join("b");
        create_private_dir(permit, &nested).expect("made");
        for folder in [&dir, &dir.join("a"), &nested] {
            private_to_you(folder, true);
        }
        let (fresh, history, lock) = (
            nested.join(".state.json.1.0.pitboard"),
            nested.join("history.jsonl"),
            nested.join("state.lock"),
        );
        writeln!(create_private(permit, &fresh).expect("made"), "{{}}").expect("written");
        writeln!(open_private_append(permit, &history).expect("made"), "one").expect("written");
        open_private_lock(permit, &lock).expect("made");
        for made in [&fresh, &history, &lock] {
            private_to_you(made, false);
            assert_eq!(
                access(made),
                Some(Access {
                    shared: false,
                    described: "open to you, SYSTEM and Administrators".into(),
                }),
                "{}",
                made.display()
            );
        }

        let inside = nested.join("inside");
        std::fs::write(&inside, "x").expect("written");
        let read = Security::of(&inside).expect("its security");
        assert!(!read.protected());
        let you = crate::host::user::sid().expect("this account's SID");
        let mut inherited: Vec<(String, u32, u8)> = read
            .aces()
            .expect("a list")
            .into_iter()
            .map(|ace| (ace.sid, ace.mask, ace.flags))
            .collect();
        inherited.sort();
        let mut wanted: Vec<(String, u32, u8)> = [you.as_str(), acl::SYSTEM, acl::ADMINISTRATORS]
            .map(|who| (who.to_owned(), acl::ALL, 0x10))
            .to_vec();
        wanted.sort();
        assert_eq!(inherited, wanted);
        std::fs::remove_dir_all(&dir).expect("cleared");
    }

    #[test]
    fn what_is_there_already_keeps_its_access_and_its_contents() {
        let dir = scratch("there");
        let permit = Permit::for_a_test();
        std::fs::create_dir_all(&dir).expect("a folder");
        testing::open_to_others(&dir).expect("opened");
        create_private_dir(permit, &dir).expect("there already");
        assert!(
            access(&dir).expect("read").shared,
            "a folder keeps its list"
        );

        let (history, lock) = (dir.join("history.jsonl"), dir.join("state.lock"));
        for there in [&history, &lock] {
            std::fs::write(there, "one\n").expect("written");
            testing::open_to_others(there).expect("opened");
        }
        writeln!(
            open_private_append(permit, &history).expect("opened"),
            "two"
        )
        .expect("added");
        open_private_lock(permit, &lock).expect("opened");
        assert_eq!(
            std::fs::read_to_string(&history).expect("read"),
            "one\ntwo\n"
        );
        assert_eq!(std::fs::read_to_string(&lock).expect("read"), "one\n");
        for there in [&history, &lock] {
            assert!(access(there).expect("read").shared, "{}", there.display());
        }

        let fresh = dir.join(".state.json.1.0.pitboard");
        create_private(permit, &fresh).expect("made");
        let refused = create_private(permit, &fresh).expect_err("a file is there");
        assert_eq!(refused.kind(), io::ErrorKind::AlreadyExists);
        let planted = dir.join(".usage.json.1.0.pitboard");
        testing::link(&dir.join("elsewhere"), &planted).expect("a link");
        assert!(create_private(permit, &planted).is_err(), "a link is there");
        assert!(
            !dir.join("elsewhere").exists(),
            "and nothing is written through it"
        );
        testing::make_private(&dir).expect("private");
        std::fs::remove_dir_all(&dir).expect("cleared");
    }

    #[test]
    fn each_way_a_test_sets_a_file_up_does_what_it_says() {
        let dir = scratch("testing");
        std::fs::create_dir_all(&dir).expect("a folder");
        let file = dir.join("file");
        std::fs::write(&file, "x").expect("written");
        let described = |path: &Path| access(path).expect("read").described;

        testing::open_to_others(&file).expect("opened");
        assert!(access(&file).expect("read").shared);
        assert_eq!(
            described(&file),
            "open to Users, you, SYSTEM and Administrators"
        );
        assert!(!testing::is_private(&file).expect("read"));
        testing::make_private(&file).expect("private");
        assert!(testing::is_private(&file).expect("read"));
        assert_eq!(std::fs::read(&file).expect("its owner reads it"), b"x");

        testing::read_only_for_owner(&file).expect("read only");
        assert_eq!(described(&file), "open to you");
        assert_eq!(std::fs::read(&file).expect("read"), b"x");
        let refused = std::fs::write(&file, "y").expect_err("nobody changes it");
        assert_eq!(refused.kind(), io::ErrorKind::PermissionDenied);

        testing::deny_reading(&file).expect("denied");
        let refused = std::fs::read(&file).expect_err("nobody reads it");
        assert_eq!(refused.kind(), io::ErrorKind::PermissionDenied);
        testing::make_private(&file).expect("private again");
        assert_eq!(std::fs::read(&file).expect("read again"), b"x");

        let program = dir.join("program.exe");
        std::fs::copy(std::env::current_exe().expect("this test"), &program).expect("copied");
        testing::deny_running(&program).expect("denied");
        let refused = std::process::Command::new(&program)
            .arg("--list")
            .output()
            .expect_err("nobody runs it");
        assert_eq!(refused.kind(), io::ErrorKind::PermissionDenied);
        assert!(std::fs::read(&program).is_ok(), "anybody still reads it");
        testing::make_runnable(&program).expect("runnable");
        let ran = std::process::Command::new(&program)
            .args(["--list", "--format=terse"])
            .output()
            .expect("it runs again");
        assert!(ran.status.success(), "{ran:?}");

        let folder = dir.join("folder");
        std::fs::create_dir(&folder).expect("a folder");
        std::fs::write(folder.join("inner"), "x").expect("written");
        testing::make_private(&folder).expect("private");
        assert!(testing::is_private(&folder).expect("read"));
        testing::deny_changes(&folder).expect("read-only");
        let nothing_added = std::fs::write(folder.join("new"), "x").expect_err("added");
        assert_eq!(nothing_added.kind(), io::ErrorKind::PermissionDenied);
        assert!(
            std::fs::remove_file(folder.join("inner")).is_err(),
            "removed"
        );
        let moved = std::fs::rename(folder.join("inner"), dir.join("inner"));
        assert!(moved.is_err(), "moved");
        assert_eq!(std::fs::read(folder.join("inner")).expect("read"), b"x");
        testing::allow_changes(&folder).expect("changeable again");
        std::fs::write(folder.join("new"), "x").expect("added");
        std::fs::remove_file(folder.join("inner")).expect("removed");
        assert!(access(&folder).expect("read").shared, "as 755 is");

        testing::link(Path::new("file"), &dir.join("to-file")).expect("a link");
        assert_eq!(
            std::fs::read_link(dir.join("to-file")).expect("a link"),
            Path::new("file")
        );
        assert!(
            dir.join("to-file").is_file(),
            "read from the link's own folder"
        );
        testing::link_dir(&folder, &dir.join("to-folder")).expect("a junction");
        assert!(dir.join("to-folder").is_dir());
        assert!(dir.join("to-folder").join("new").is_file());
        assert_eq!(file_id(&dir.join("to-folder")), file_id(&folder));
        testing::link_dir(Path::new("folder"), &dir.join("relative")).expect("a junction");
        assert_eq!(file_id(&dir.join("relative")), file_id(&folder));

        let inode = testing::inode(&file).expect("there");
        assert_ne!(
            testing::inode(&dir.join("to-folder")).expect("there"),
            testing::inode(&folder).expect("there"),
            "a junction's own"
        );
        std::fs::rename(&file, dir.join("moved")).expect("moved");
        assert_eq!(
            testing::inode(&dir.join("moved")).expect("there"),
            inode,
            "a move keeps it"
        );
        std::fs::copy(dir.join("moved"), &file).expect("copied");
        assert_ne!(
            testing::inode(&file).expect("there"),
            inode,
            "a copy does not"
        );
        std::fs::remove_dir_all(&dir).expect("cleared");
    }

    #[test]
    fn access_names_who_else_reads_a_file_in_windows_words() {
        let dir = scratch("access");
        let folder = dir.join("folder");
        std::fs::create_dir_all(&folder).expect("a folder");
        let file = dir.join("file");
        std::fs::write(&file, "x").expect("written");
        let you = crate::host::user::sid().expect("this account's SID");
        let private = acl::private_aces(&you, false);
        let found = |path: &Path, dacl: &str| {
            testing::set(path, dacl).expect(dacl);
            access(path).expect("read")
        };
        for (sid, name) in [
            ("WD", "Everyone"),
            ("BU", "Users"),
            ("AU", "Authenticated Users"),
            (
                "S-1-5-21-1004336348-1177238915-682003330-4242",
                "another account",
            ),
        ] {
            let read = found(&file, &format!("D:P{private}(A;;FR;;;{sid})"));
            assert!(read.shared, "{sid}");
            assert_eq!(
                read.described,
                format!("open to {name}, you, SYSTEM and Administrators")
            );
            assert!(!read.described.contains("S-1-"), "{}", read.described);
        }
        let folders = acl::private_aces(&you, true);
        let below = found(&folder, &format!("D:P{folders}(A;OICIIO;FA;;;WD)"));
        assert!(!below.shared, "inherit-only");
        let users = "(A;;FR;;;BU)";
        assert!(
            found(&file, &format!("D:P(D;;0x1;;;WD){private}{users}")).shared,
            "Everyone denied"
        );
        assert!(
            !found(&file, &format!("D:P(D;;0x1;;;BU){private}{users}")).shared,
            "Users denied"
        );
        let open = found(&file, "D:NO_ACCESS_CONTROL");
        assert!(open.shared, "a NULL DACL");
        assert_eq!(open.described, "open to everyone, with no access list");
        testing::make_private(&file).expect("private");
        assert_eq!(access(&dir.join("never-made")), None);
        std::fs::remove_dir_all(&dir).expect("cleared");
    }

    // PowerShell's -EncodedCommand takes the command as base64 of its UTF-16, so nothing in it
    // is quoted again on the way.
    fn encoded(command: &str) -> String {
        const DIGITS: &[u8; 64] =
            b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let bytes: Vec<u8> = command.encode_utf16().flat_map(u16::to_le_bytes).collect();
        bytes
            .chunks(3)
            .flat_map(|chunk| {
                let n = chunk.iter().enumerate().fold(0u32, |n, (at, byte)| {
                    n | (u32::from(*byte) << (16 - 8 * at))
                });
                (0..4).map(move |at| {
                    if at <= chunk.len() {
                        char::from(DIGITS[((n >> (18 - 6 * at)) & 63) as usize])
                    } else {
                        '='
                    }
                })
            })
            .collect()
    }

    // Each line as a person pastes it, into cmd or into PowerShell.
    fn run_advice(shell: &str, kind: Kind, path: &Path) {
        use std::os::windows::process::CommandExt;
        let you = crate::host::user::sid().expect("this account's SID");
        let named = path.display().to_string();
        let lines = Os::Windows.make_private_commands(kind, &[&named], Some(&you));
        assert_eq!(lines.len(), 1, "{lines:?}");
        for line in lines {
            let ran = match shell {
                "cmd" => std::process::Command::new("cmd.exe")
                    .args(["/d", "/c"])
                    .raw_arg(&line)
                    .output(),
                _ => std::process::Command::new("powershell.exe")
                    .args(["-NoProfile", "-NonInteractive", "-EncodedCommand"])
                    .arg(encoded(&line))
                    .output(),
            }
            .expect(shell);
            assert!(ran.status.success(), "{shell}: {line}: {ran:?}");
        }
    }

    #[test]
    fn the_icacls_advice_closes_a_login_to_others_and_its_owner_still_reads_it() {
        let dir = scratch("icacls Đạt");
        std::fs::create_dir_all(&dir).expect("a folder");
        testing::open_to_others(&dir).expect("opened");
        // As a folder made under one others read is open: through what it inherits.
        let shared = dir.join("shared folder");
        std::fs::create_dir(&shared).expect("a folder");
        assert!(access(&shared).expect("read").shared, "its parent's list");
        let private = Access {
            shared: false,
            described: "open to you, SYSTEM and Administrators".into(),
        };
        for (shell, kind) in [("cmd", Kind::File), ("powershell", Kind::Any)] {
            let login = shared.join(format!("{shell} Đạt's login.json"));
            std::fs::write(&login, "x").expect("written");
            assert!(access(&login).expect("read").shared, "its folder's list");
            run_advice(shell, kind, &login);
            assert_eq!(access(&login), Some(private.clone()), "{shell}");
            assert_eq!(std::fs::read(&login).expect("its owner reads it"), b"x");
        }
        let inside = shared.join("inside");
        std::fs::write(&inside, "x").expect("written");
        run_advice("powershell", Kind::Directory, &shared);
        assert_eq!(access(&shared), Some(private.clone()));
        assert_eq!(access(&inside), Some(private), "what is inside takes it");
        assert_eq!(std::fs::read(&inside).expect("its owner reads it"), b"x");
        std::fs::remove_dir_all(&dir).expect("cleared");
    }

    #[test]
    fn a_path_with_a_space_has_a_short_name_that_leads_to_it() {
        let long = std::env::temp_dir().join(format!("pitboard short {}", std::process::id()));
        std::fs::create_dir_all(&long).expect("a scratch folder");
        let short = short_path(&long).expect("a short name");
        assert!(
            !short.to_string_lossy().contains(' '),
            "{}",
            short.display()
        );
        assert_eq!(
            std::fs::canonicalize(&short).expect("there"),
            std::fs::canonicalize(&long).expect("there")
        );
        assert_eq!(short_path(&long.join("never-made")), None);
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
        let id = file_id(&target).expect("an id");
        assert_eq!(file_id(&junction), Some(id));
        assert_ne!(file_id(&other), Some(id));
        assert_eq!(file_id(&root.join("never-made")), None);
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
