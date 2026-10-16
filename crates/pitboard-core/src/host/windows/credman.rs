// Names and whether an item is there only: CredEnumerateW and CredReadW hand back each item's
// blob too, which nothing here reads.

use std::io;
use windows_sys::Win32::Foundation::ERROR_NOT_FOUND;
use windows_sys::Win32::Security::Credentials::{
    CRED_TYPE_GENERIC, CREDENTIALW, CredEnumerateW, CredFree, CredReadW,
};

// `filter` is a prefix and `*`, as CredEnumerateW takes one.
pub(crate) fn names(filter: &str) -> io::Result<Vec<String>> {
    let filter: Vec<u16> = filter.encode_utf16().chain([0]).collect();
    let mut count = 0u32;
    let mut found: *mut *mut CREDENTIALW = std::ptr::null_mut();
    // SAFETY: a NUL-terminated filter, and two out-params of this frame; on success `found` is
    // an array of `count` items, freed below.
    if unsafe { CredEnumerateW(filter.as_ptr(), 0, &raw mut count, &raw mut found) } == 0 {
        let failed = io::Error::last_os_error();
        return match failed.raw_os_error() {
            Some(code) if code as u32 == ERROR_NOT_FOUND => Ok(Vec::new()),
            _ => Err(failed),
        };
    }
    let names = (0..count as usize)
        .map(|at| {
            // SAFETY: `found` holds `count` pointers to items, each with a NUL-terminated name.
            let name = unsafe { (**found.add(at)).TargetName };
            let mut len = 0;
            // SAFETY: read up to the name's NUL.
            while unsafe { *name.add(len) } != 0 {
                len += 1;
            }
            // SAFETY: `name` holds `len` units before its NUL.
            String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(name, len) })
        })
        .collect();
    // SAFETY: the array CredEnumerateW allocated, freed this once.
    unsafe { CredFree(found.cast()) };
    Ok(names)
}

// A generic item named `target`, as `Bun.secrets` writes one.
pub(crate) fn held(target: &str) -> io::Result<bool> {
    let target: Vec<u16> = target.encode_utf16().chain([0]).collect();
    let mut found: *mut CREDENTIALW = std::ptr::null_mut();
    // SAFETY: a NUL-terminated name, and an out-param of this frame, an item freed below.
    if unsafe { CredReadW(target.as_ptr(), CRED_TYPE_GENERIC, 0, &raw mut found) } == 0 {
        let failed = io::Error::last_os_error();
        return match failed.raw_os_error() {
            Some(code) if code as u32 == ERROR_NOT_FOUND => Ok(false),
            _ => Err(failed),
        };
    }
    // SAFETY: the item CredReadW allocated, freed this once.
    unsafe { CredFree(found.cast()) };
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;

    // Never a family's prefix: the account running the tests may hold real logins.
    #[test]
    fn a_prefix_nothing_has_lists_nothing() {
        assert_eq!(
            names("pitboard-citest-w21-nothing-*").expect("listed"),
            Vec::<String>::new()
        );
    }

    #[test]
    fn an_item_nothing_has_is_not_held() {
        assert!(!held("pitboard-citest-w22-nothing/claude-code-user").expect("asked"));
    }
}
