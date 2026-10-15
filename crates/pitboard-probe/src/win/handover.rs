//! RF-WW1: hand a dummy on an anonymous pipe as C runtime descriptor 3, the way a parent
//! hands a child an extra stdio handle on Windows, and read back what `bun fd3-read.mjs` made
//! of it. The descriptor goes through `STARTUPINFOW.lpReserved2`, the C runtime's inheritance
//! table (an `int` count, a flag byte per descriptor, then a handle per descriptor), which is
//! the route libuv, Node and Bun take. The dummy is never a real token, and nothing a login
//! holds is read or written.

use super::ffi::{self, Owned};
use super::logon_now;
use crate::report::Report;
use serde_json::json;
use std::path::Path;

use windows_sys::Win32::Foundation::{HANDLE, HANDLE_FLAG_INHERIT, SetHandleInformation};
use windows_sys::Win32::Security::SECURITY_ATTRIBUTES;
use windows_sys::Win32::Storage::FileSystem::{
    CreateFileW, FILE_ATTRIBUTE_NORMAL, FILE_GENERIC_READ, FILE_GENERIC_WRITE, OPEN_EXISTING,
    ReadFile, WriteFile,
};
use windows_sys::Win32::System::Pipes::CreatePipe;
use windows_sys::Win32::System::Threading::{
    CREATE_NO_WINDOW, CreateProcessW, GetExitCodeProcess, PROCESS_INFORMATION,
    STARTF_USESTDHANDLES, STARTUPINFOW, WaitForSingleObject,
};

// The C runtime's inheritance flags, as it writes them into lpReserved2.
const FOPEN: u8 = 0x01;
const FPIPE: u8 = 0x08;
const FDEV: u8 = 0x40;

pub(super) fn fd_handover(bun: &Path, script: &Path, token: &[u8]) -> Report {
    let logon = logon_now();
    match run(bun, script, token) {
        Ok(data) => Report::ok("fd-handover", logon, data),
        Err(reason) => Report::refused("fd-handover", logon, reason),
    }
}

fn inheritable() -> SECURITY_ATTRIBUTES {
    SECURITY_ATTRIBUTES {
        nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: std::ptr::null_mut(),
        bInheritHandle: 1,
    }
}

/// An anonymous pipe, both ends inheritable.
fn pipe(sa: &SECURITY_ATTRIBUTES) -> Result<(Owned, Owned), String> {
    let mut read: HANDLE = std::ptr::null_mut();
    let mut write: HANDLE = std::ptr::null_mut();
    // SAFETY: `read` and `write` are valid out-params; `sa` lives through the call.
    let ok = unsafe { CreatePipe(&mut read, &mut write, sa, 0) };
    if ok == 0 {
        return Err(format!("CreatePipe failed: {}", ffi::last_error()));
    }
    let (Some(read), Some(write)) = (Owned::new(read), Owned::new(write)) else {
        return Err("CreatePipe returned no handle".into());
    };
    Ok((read, write))
}

/// Take `handle` off the inheritance list, so the child does not keep the parent's end open.
fn keep_in_parent(handle: &Owned) {
    // SAFETY: `handle` is an open handle this process owns.
    unsafe {
        SetHandleInformation(handle.raw(), HANDLE_FLAG_INHERIT, 0);
    }
}

/// An inheritable handle to the NUL device, for a descriptor the child does not use.
fn nul(access: u32, sa: &SECURITY_ATTRIBUTES) -> Result<Owned, String> {
    let name = ffi::wide("NUL");
    // SAFETY: `name` is NUL-terminated; `sa` lives through the call; the handle is owned.
    let handle = unsafe {
        CreateFileW(
            name.as_ptr(),
            access,
            0,
            sa,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            std::ptr::null_mut(),
        )
    };
    Owned::new(handle).ok_or_else(|| format!("opening NUL failed: {}", ffi::last_error()))
}

/// The C runtime inheritance table for descriptors 0 to 3, as lpReserved2 holds it.
fn reserved2(handles: [HANDLE; 4], flags: [u8; 4]) -> Vec<u8> {
    let mut buf = Vec::with_capacity(4 + 4 + 4 * size_of::<usize>());
    buf.extend_from_slice(&(handles.len() as i32).to_ne_bytes());
    buf.extend_from_slice(&flags);
    for h in handles {
        buf.extend_from_slice(&(h as usize).to_ne_bytes());
    }
    buf
}

fn run(bun: &Path, script: &Path, token: &[u8]) -> Result<serde_json::Value, String> {
    let sa = inheritable();
    let (token_read, token_write) = pipe(&sa)?;
    let (stdout_read, stdout_write) = pipe(&sa)?;
    keep_in_parent(&token_write);
    keep_in_parent(&stdout_read);
    let nul_in = nul(FILE_GENERIC_READ, &sa)?;
    let nul_err = nul(FILE_GENERIC_WRITE, &sa)?;

    // Descriptor 3 is the token pipe's read end; 1 is the child's stdout; 0 and 2 are NUL.
    let mut reserved = reserved2(
        [
            nul_in.raw(),
            stdout_write.raw(),
            nul_err.raw(),
            token_read.raw(),
        ],
        [FOPEN | FDEV, FOPEN | FPIPE, FOPEN | FDEV, FOPEN | FPIPE],
    );
    let si = STARTUPINFOW {
        cb: size_of::<STARTUPINFOW>() as u32,
        dwFlags: STARTF_USESTDHANDLES,
        hStdInput: nul_in.raw(),
        hStdOutput: stdout_write.raw(),
        hStdError: nul_err.raw(),
        cbReserved2: reserved.len() as u16,
        lpReserved2: reserved.as_mut_ptr(),
        ..Default::default()
    };

    let app = ffi::wide(bun);
    let mut line = ffi::quote_arg(bun.as_os_str());
    line.push(u16::from(b' '));
    line.extend(ffi::quote_arg(script.as_os_str()));
    line.push(0);
    let mut pi = PROCESS_INFORMATION::default();
    // SAFETY: `app` and `line` are NUL-terminated and `line` is mutable; `si` is sized and
    // holds the inheritance table; inherited handles are marked so; `pi` receives two handles.
    let ok = unsafe {
        CreateProcessW(
            app.as_ptr(),
            line.as_mut_ptr(),
            std::ptr::null(),
            std::ptr::null(),
            1,
            CREATE_NO_WINDOW,
            std::ptr::null(),
            std::ptr::null(),
            &si,
            &mut pi,
        )
    };
    if ok == 0 {
        return Err(format!(
            "CreateProcessW({}) failed: {}",
            bun.display(),
            ffi::last_error()
        ));
    }
    let process = Owned::new(pi.hProcess).ok_or_else(|| ffi::last_error().to_string())?;
    drop(Owned::new(pi.hThread));
    // The child holds its own copies now; drop the parent's, so a read on stdout ends at EOF.
    drop(token_read);
    drop(stdout_write);
    drop(nul_in);
    drop(nul_err);

    write_all(&token_write, token)?;
    drop(token_write);
    let printed = read_all(&stdout_read);

    // SAFETY: `process` is an open process handle.
    let waited = unsafe { WaitForSingleObject(process.raw(), 30_000) };
    let exit = if waited == 0 {
        let mut code = 0u32;
        // SAFETY: `process` is open; `code` is a valid out-param.
        (unsafe { GetExitCodeProcess(process.raw(), &mut code) } != 0).then_some(code)
    } else {
        None
    };

    let script_report: Option<serde_json::Value> = serde_json::from_str(printed.trim()).ok();
    let r = ffi::redactor();
    Ok(json!({
        "handed_bytes": token.len(),
        "bun": r.redact(&bun.display().to_string()),
        "script": script.file_name().map(|n| n.to_string_lossy().into_owned()),
        "child_exit": exit,
        "child_timed_out": exit.is_none(),
        "fd3": script_report,
        "raw_output": if script_report.is_none() { Some(printed) } else { None },
    }))
}

fn write_all(handle: &Owned, bytes: &[u8]) -> Result<(), String> {
    let mut written = 0u32;
    // SAFETY: `handle` is an open write end; `bytes` is valid for its length.
    let ok = unsafe {
        WriteFile(
            handle.raw(),
            bytes.as_ptr(),
            bytes.len() as u32,
            &mut written,
            std::ptr::null_mut(),
        )
    };
    if ok == 0 {
        Err(format!("WriteFile failed: {}", ffi::last_error()))
    } else {
        Ok(())
    }
}

/// Read the pipe to its end: a closed write end gives a zero read or `ERROR_BROKEN_PIPE`.
fn read_all(handle: &Owned) -> String {
    let mut out = Vec::new();
    let mut buf = [0u8; 4096];
    loop {
        let mut read = 0u32;
        // SAFETY: `handle` is an open read end; `buf` holds `buf.len()` bytes.
        let ok = unsafe {
            ReadFile(
                handle.raw(),
                buf.as_mut_ptr(),
                buf.len() as u32,
                &mut read,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 || read == 0 {
            break;
        }
        out.extend_from_slice(&buf[..read as usize]);
        if out.len() > 1 << 20 {
            break;
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}
