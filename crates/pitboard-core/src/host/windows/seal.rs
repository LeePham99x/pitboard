//! DPAPI under the person's own key, which seals each park to their Windows account.

use crate::store::vault::Seal;
use std::io;
use windows_sys::Win32::Foundation::LocalFree;
use windows_sys::Win32::Security::Cryptography::{
    CRYPT_INTEGER_BLOB, CRYPTPROTECT_UI_FORBIDDEN, CryptProtectData, CryptUnprotectData,
};
use windows_sys::core::BOOL;

// CurrentUser scope: never CRYPTPROTECT_LOCAL_MACHINE, which every account here could open.
pub(crate) struct Dpapi;

impl Seal for Dpapi {
    fn seal(&self, entropy: &[u8], plain: &[u8]) -> io::Result<Vec<u8>> {
        let (data, entropy) = (blob(plain)?, blob(entropy)?);
        let mut out = CRYPT_INTEGER_BLOB::default();
        // SAFETY: both blobs point at buffers that outlive the call, which reads them and
        // hands `out` a buffer of its own.
        let done = unsafe {
            CryptProtectData(
                &data,
                std::ptr::null(),
                &entropy,
                std::ptr::null(),
                std::ptr::null(),
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out,
            )
        };
        taken(done, out)
    }

    fn open(&self, entropy: &[u8], sealed: &[u8]) -> io::Result<Vec<u8>> {
        let (data, entropy) = (blob(sealed)?, blob(entropy)?);
        let mut out = CRYPT_INTEGER_BLOB::default();
        // SAFETY: as in `seal`; no description is asked for.
        let done = unsafe {
            CryptUnprotectData(
                &data,
                std::ptr::null_mut(),
                &entropy,
                std::ptr::null(),
                std::ptr::null(),
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out,
            )
        };
        taken(done, out)
    }
}

fn blob(bytes: &[u8]) -> io::Result<CRYPT_INTEGER_BLOB> {
    Ok(CRYPT_INTEGER_BLOB {
        cbData: u32::try_from(bytes.len()).map_err(|_| io::ErrorKind::InvalidInput)?,
        pbData: bytes.as_ptr().cast_mut(),
    })
}

fn taken(done: BOOL, out: CRYPT_INTEGER_BLOB) -> io::Result<Vec<u8>> {
    if done == 0 {
        return Err(io::Error::last_os_error());
    }
    // SAFETY: the call succeeded, so `pbData` holds `cbData` bytes allocated with LocalAlloc,
    // which this process frees once and nothing else holds.
    let bytes = unsafe { std::slice::from_raw_parts(out.pbData, out.cbData as usize) }.to_vec();
    // SAFETY: as above.
    unsafe { LocalFree(out.pbData.cast()) };
    Ok(bytes)
}
