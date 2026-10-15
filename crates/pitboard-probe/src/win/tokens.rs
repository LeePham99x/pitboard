//! A1 and the runner facts: the process token as Pitboard reads it, the owner a file made
//! with it gets, and a Safer normal-user token computed from it (the CI fallback the check
//! asks about: it must read as not elevated, and a program started with it must run).

use super::ffi::{self, Token};
use super::{acl, logon_now, take_child_report};
use crate::cli::DpapiAction;
use crate::elevation::{self, integrity_word};
use crate::report::Report;
use serde_json::{Value, json};
use std::ffi::OsStr;
use std::path::Path;

use windows_sys::Win32::Foundation::{HANDLE, LUID};
use windows_sys::Win32::Security::AppLocker::{
    SAFER_LEVEL_OPEN, SAFER_LEVELID_NORMALUSER, SAFER_SCOPEID_USER, SaferCloseLevel,
    SaferComputeTokenFromLevel, SaferCreateLevel,
};
use windows_sys::Win32::Security::Authorization::ConvertStringSecurityDescriptorToSecurityDescriptorW;
use windows_sys::Win32::Security::{
    AdjustTokenPrivileges, CreateRestrictedToken, CreateWellKnownSid, DISABLE_MAX_PRIVILEGE,
    GetTokenInformation, LUA_TOKEN, PSID, SAFER_LEVEL_HANDLE, SE_PRIVILEGE_ENABLED,
    SECURITY_ATTRIBUTES, SID_AND_ATTRIBUTES, TOKEN_ADJUST_DEFAULT, TOKEN_ADJUST_PRIVILEGES,
    TOKEN_ASSIGN_PRIMARY, TOKEN_DUPLICATE, TOKEN_GROUPS, TOKEN_PRIVILEGES, TOKEN_QUERY,
    TokenGroups, WRITE_RESTRICTED, WinWorldSid,
};
use windows_sys::Win32::Security::{LookupPrivilegeValueW, PSECURITY_DESCRIPTOR};
use windows_sys::Win32::Storage::FileSystem::CreateDirectoryW;
use windows_sys::Win32::System::Threading::{
    CREATE_NO_WINDOW, GetCurrentProcess, OpenProcessToken,
};

/// What Pitboard reads from a token, with every principal named by relation.
pub(super) fn describe(token: &Token) -> Value {
    let facts = token.facts();
    let reading = elevation::reading(facts);
    let user = token.user_sid();
    json!({
        "elevation_type": facts.elevation_type.map(elevation::ElevationType::as_str),
        "token_is_elevated": facts.is_elevated,
        "user_is_service_account": facts.user_is_service,
        "integrity": facts.integrity_rid.map(integrity_word),
        "default_owner": token
            .default_owner_sid()
            .map(|o| elevation::relation(&o, user.as_deref())),
        "reading": reading.as_str(),
        "reading_reason": reading.reason(),
    })
}

pub(super) fn tokens(scratch: Option<&Path>) -> Report {
    let logon = logon_now();
    let token = match Token::current() {
        Ok(t) => t,
        Err(e) => return Report::refused("tokens", logon, format!("OpenProcessToken failed: {e}")),
    };
    let mut data = describe(&token);
    if let Some(scratch) = scratch {
        data["file_made_in_scratch"] = match file_owner(scratch, token.user_sid().as_deref()) {
            Ok(v) => v,
            Err(reason) => return Report::refused("tokens", logon, reason),
        };
    }
    Report::ok("tokens", logon, data)
}

/// Make a file under `scratch`, read its owner's relation to this token, and remove it.
fn file_owner(scratch: &Path, user: Option<&str>) -> Result<Value, String> {
    if let Some(reason) = ffi::refuse_write(scratch) {
        return Err(reason);
    }
    std::fs::create_dir_all(scratch).map_err(|e| format!("cannot make the scratch folder: {e}"))?;
    let file = scratch.join(format!(
        "{}owner-{}.txt",
        crate::PROBE_PREFIX,
        std::process::id()
    ));
    std::fs::write(&file, b"owner").map_err(|e| format!("cannot write a file there: {e}"))?;
    let owner = acl::owner_sid(&file);
    let _ = std::fs::remove_file(&file);
    Ok(match owner {
        Ok(sid) => json!({ "owner": elevation::relation(&sid, user) }),
        Err(code) => json!({ "owner_error": code }),
    })
}

/// A Safer normal-user token computed from this process's token.
fn safer_token() -> Result<Token, String> {
    let mut base: HANDLE = std::ptr::null_mut();
    // SAFETY: a pseudo-handle for this process; `base` receives a token handle.
    let ok = unsafe {
        OpenProcessToken(
            GetCurrentProcess(),
            TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY,
            &mut base,
        )
    };
    if ok == 0 {
        return Err(format!("OpenProcessToken failed: {}", ffi::last_error()));
    }
    let base = Token::owned(base);
    let mut level: SAFER_LEVEL_HANDLE = std::ptr::null_mut();
    // SAFETY: the documented scope and level; `level` receives a handle closed below.
    let ok = unsafe {
        SaferCreateLevel(
            SAFER_SCOPEID_USER,
            SAFER_LEVELID_NORMALUSER,
            SAFER_LEVEL_OPEN,
            &mut level,
            std::ptr::null(),
        )
    };
    if ok == 0 {
        return Err(format!("SaferCreateLevel failed: {}", ffi::last_error()));
    }
    let mut out: HANDLE = std::ptr::null_mut();
    // SAFETY: `level` and `base` are open; `out` receives a new primary token.
    let ok =
        unsafe { SaferComputeTokenFromLevel(level, base.raw(), &mut out, 0, std::ptr::null_mut()) };
    let err = ffi::last_error();
    // SAFETY: `level` came from SaferCreateLevel.
    unsafe {
        SaferCloseLevel(level);
    }
    if ok == 0 {
        return Err(format!("SaferComputeTokenFromLevel failed: {err}"));
    }
    Ok(Token::owned(out))
}

pub(super) fn safer(spawn: bool, scratch: Option<&Path>) -> Report {
    let logon = logon_now();
    let token = match safer_token() {
        Ok(t) => t,
        Err(reason) => return Report::refused("safer", logon, reason),
    };
    let mut data = json!({ "safer_normal_user_token": describe(&token) });
    if spawn {
        let Some(scratch) = scratch else {
            return Report::refused("safer", logon, "--spawn needs --scratch");
        };
        if let Some(reason) = ffi::refuse_write(scratch) {
            return Report::refused("safer", logon, reason);
        }
        let _ = std::fs::create_dir_all(scratch);
        let out = scratch.join(format!(
            "{}safer-{}.json",
            crate::PROBE_PREFIX,
            std::process::id()
        ));
        let exe = std::env::current_exe().unwrap_or_default();
        let args: [&OsStr; 3] = [OsStr::new("--out"), out.as_os_str(), OsStr::new("tokens")];
        data["spawned"] = match ffi::spawn(Some(token.raw()), &exe, &args, CREATE_NO_WINDOW, None) {
            Ok(child) => json!({
                "started": true,
                "exit": child.wait_ms(30_000),
                "report": take_child_report(&out),
            }),
            Err(code) => json!({ "started": false, "error": code }),
        };
    }
    Report::ok("safer", logon, data)
}

// Codex 0.162.1's unelevated sandbox flags (codex-rs/windows-sandbox-rs/src/token.rs).
const SE_GROUP_LOGON_ID: u32 = 0xC000_0000;

/// A copy of the logon session SID in `token`'s groups, which Codex uses as a restricting SID.
fn logon_sid_bytes(token: &Token) -> Option<Vec<u8>> {
    let mut len = 0u32;
    // SAFETY: a sizing call with a null buffer returns the needed length.
    unsafe {
        GetTokenInformation(token.raw(), TokenGroups, std::ptr::null_mut(), 0, &mut len);
    }
    if len == 0 {
        return None;
    }
    let mut buf = vec![0u64; (len as usize).div_ceil(8)];
    // SAFETY: `buf` holds at least `len` bytes.
    let ok = unsafe {
        GetTokenInformation(
            token.raw(),
            TokenGroups,
            buf.as_mut_ptr().cast(),
            len,
            &mut len,
        )
    };
    if ok == 0 {
        return None;
    }
    // SAFETY: the buffer begins with a TOKEN_GROUPS; its Groups array holds GroupCount entries.
    let groups = unsafe { &*(buf.as_ptr() as *const TOKEN_GROUPS) };
    let count = groups.GroupCount as usize;
    let first = std::ptr::addr_of!(groups.Groups) as *const SID_AND_ATTRIBUTES;
    for i in 0..count {
        // SAFETY: `i` < GroupCount, so this entry lies inside the buffer.
        let entry = unsafe { &*first.add(i) };
        if entry.Attributes & SE_GROUP_LOGON_ID == SE_GROUP_LOGON_ID {
            return copy_sid(entry.Sid);
        }
    }
    None
}

/// A well-known SID's bytes, owned.
fn well_known_sid(kind: i32) -> Option<Vec<u8>> {
    let mut len = 0u32;
    // SAFETY: a sizing call with a null buffer returns the needed length.
    unsafe {
        CreateWellKnownSid(kind, std::ptr::null_mut(), std::ptr::null_mut(), &mut len);
    }
    if len == 0 {
        return None;
    }
    let mut buf = vec![0u8; len as usize];
    // SAFETY: `buf` holds `len` bytes, the size the first call asked for.
    let ok = unsafe {
        CreateWellKnownSid(
            kind,
            std::ptr::null_mut(),
            buf.as_mut_ptr().cast(),
            &mut len,
        )
    };
    (ok != 0).then_some(buf)
}

/// The bytes of the SID at `sid`, by its own length (8 + 4 per sub-authority), owned.
fn copy_sid(sid: PSID) -> Option<Vec<u8>> {
    if sid.is_null() {
        return None;
    }
    // SAFETY: a SID's second byte is its sub-authority count; the whole SID is that long.
    let count = unsafe { *(sid as *const u8).add(1) } as usize;
    let len = 8 + 4 * count;
    // SAFETY: the SID is `len` bytes, per its own layout.
    Some(unsafe { std::slice::from_raw_parts(sid as *const u8, len) }.to_vec())
}

/// A restricted token shaped like Codex 0.162.1's unelevated sandbox: the same three flags,
/// and the tail of its restricting-SID order, the logon SID then Everyone (its unelevated
/// path adds no capability or user SID). SeChangeNotifyPrivilege is enabled so a directory can
/// be traversed, as Codex enables it; its default-DACL step, which bears only on the token's
/// own objects, is left out. The restricting-SID labels go back with the token.
fn restricted_token() -> Result<(Token, Vec<&'static str>), String> {
    let mut base: HANDLE = std::ptr::null_mut();
    // SAFETY: a pseudo-handle for this process; `base` receives a token handle.
    let ok = unsafe {
        OpenProcessToken(
            GetCurrentProcess(),
            TOKEN_DUPLICATE
                | TOKEN_QUERY
                | TOKEN_ASSIGN_PRIMARY
                | TOKEN_ADJUST_DEFAULT
                | TOKEN_ADJUST_PRIVILEGES,
            &mut base,
        )
    };
    if ok == 0 {
        return Err(format!("OpenProcessToken failed: {}", ffi::last_error()));
    }
    let base = Token::owned(base);
    let mut logon = logon_sid_bytes(&base).ok_or("no logon SID on this token")?;
    let mut everyone =
        well_known_sid(WinWorldSid).ok_or("CreateWellKnownSid for Everyone failed")?;
    let entries = [logon.as_mut_ptr(), everyone.as_mut_ptr()].map(|p| SID_AND_ATTRIBUTES {
        Sid: p.cast(),
        Attributes: 0,
    });
    let mut out: HANDLE = std::ptr::null_mut();
    // SAFETY: `base` is open for duplication; `entries` point at live SID buffers; `out`
    // receives a new primary token.
    let ok = unsafe {
        CreateRestrictedToken(
            base.raw(),
            DISABLE_MAX_PRIVILEGE | LUA_TOKEN | WRITE_RESTRICTED,
            0,
            std::ptr::null(),
            0,
            std::ptr::null(),
            entries.len() as u32,
            entries.as_ptr(),
            &mut out,
        )
    };
    if ok == 0 {
        return Err(format!(
            "CreateRestrictedToken failed: {}",
            ffi::last_error()
        ));
    }
    let token = Token::owned(out);
    enable_change_notify(&token);
    Ok((token, vec!["logon", "everyone"]))
}

/// Enable SeChangeNotifyPrivilege on `token`, so a run under it may traverse a directory.
fn enable_change_notify(token: &Token) {
    let mut luid = LUID {
        LowPart: 0,
        HighPart: 0,
    };
    let name = ffi::wide("SeChangeNotifyPrivilege");
    // SAFETY: `name` is NUL-terminated; `luid` is a valid out-param.
    if unsafe { LookupPrivilegeValueW(std::ptr::null(), name.as_ptr(), &mut luid) } == 0 {
        return;
    }
    let tp = TOKEN_PRIVILEGES {
        PrivilegeCount: 1,
        Privileges: [windows_sys::Win32::Security::LUID_AND_ATTRIBUTES {
            Luid: luid,
            Attributes: SE_PRIVILEGE_ENABLED,
        }],
    };
    // SAFETY: `token` is open for adjusting privileges; `tp` names one privilege.
    unsafe {
        AdjustTokenPrivileges(
            token.raw(),
            0,
            &tp,
            0,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
        );
    }
}

/// A directory the restricted token may write: Everyone is granted, and Everyone is one of
/// the token's restricting SIDs, so a write-restricted child can leave its report there.
fn make_shared_dir(path: &Path) -> Result<(), String> {
    let sddl = ffi::wide("D:(A;OICI;FA;;;WD)(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
    let mut sd: PSECURITY_DESCRIPTOR = std::ptr::null_mut();
    // SAFETY: `sddl` is NUL-terminated; `sd` receives a LocalAlloc descriptor freed below.
    let ok = unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.as_ptr(),
            1,
            &mut sd,
            std::ptr::null_mut(),
        )
    };
    if ok == 0 || sd.is_null() {
        return Err(format!("SDDL conversion failed: {}", ffi::last_error()));
    }
    let attrs = SECURITY_ATTRIBUTES {
        nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: sd,
        bInheritHandle: 0,
    };
    let w = ffi::wide(path);
    // SAFETY: `w` is NUL-terminated; `attrs` holds a valid descriptor.
    let made = unsafe { CreateDirectoryW(w.as_ptr(), &attrs) };
    let err = ffi::last_error();
    // SAFETY: `sd` is the LocalAlloc descriptor from the conversion.
    unsafe {
        windows_sys::Win32::Foundation::LocalFree(sd);
    }
    if made == 0 {
        Err(format!("cannot make the shared out folder: {err}"))
    } else {
        Ok(())
    }
}

/// D2b/H7: whether a restricted token like Codex's unelevated sandbox unseals a DPAPI park
/// and opens a vault-style file. The unrestricted parent seals a dummy park and makes a
/// protected file; a child under the restricted token then runs `dpapi --action unseal` and
/// `acl --open` against them, writing its report to a folder Everyone may write.
pub(super) fn restricted(scratch: &Path) -> Report {
    let logon = logon_now();
    let own = Token::current().ok().and_then(|t| t.user_sid());
    if let Some(reason) = ffi::refuse_write(scratch) {
        return Report::refused("restricted-token", logon, reason);
    }
    if let Err(e) = std::fs::create_dir_all(scratch) {
        return Report::refused(
            "restricted-token",
            logon,
            format!("cannot make the scratch folder: {e}"),
        );
    }
    let sealed_name = "pitboard-probe-d2b-sealed.bin";
    let protected_name = format!("{}protected", crate::PROBE_PREFIX);
    let seal = super::dpapi::dpapi(scratch, DpapiAction::Seal, sealed_name);
    let protected = acl::create_protected(scratch, own.as_deref(), true);
    let out_dir = scratch.join("pitboard-probe-d2b-out");
    let _ = std::fs::remove_dir_all(&out_dir);
    if let Err(reason) = make_shared_dir(&out_dir) {
        return Report::refused("restricted-token", logon, reason);
    }

    let (token, restricting) = match restricted_token() {
        Ok(t) => t,
        Err(reason) => return Report::refused("restricted-token", logon, reason),
    };
    let exe = std::env::current_exe().unwrap_or_default();
    let scratch_arg = scratch.as_os_str();
    let protected_path = scratch.join(&protected_name);

    let unseal = spawn_under(
        &token,
        &exe,
        &out_dir,
        "unseal",
        &[
            OsStr::new("dpapi"),
            OsStr::new("--scratch"),
            scratch_arg,
            OsStr::new("--action"),
            OsStr::new("unseal"),
            OsStr::new("--file"),
            OsStr::new(sealed_name),
        ],
    );
    let open = spawn_under(
        &token,
        &exe,
        &out_dir,
        "open",
        &[
            OsStr::new("acl"),
            OsStr::new("--scratch"),
            scratch_arg,
            OsStr::new("--open"),
            protected_path.as_os_str(),
        ],
    );

    let _ = std::fs::remove_file(scratch.join(sealed_name));
    let _ = std::fs::remove_file(&protected_path);
    let _ = std::fs::remove_dir_all(&out_dir);

    Report::ok(
        "restricted-token",
        logon,
        json!({
            "flags": "DISABLE_MAX_PRIVILEGE | LUA_TOKEN | WRITE_RESTRICTED",
            "restricting_sids": restricting,
            "restricted_token": describe(&token),
            "sealed": seal.to_json(),
            "protected": protected.unwrap_or_else(|e| json!({ "error": e })),
            "unseal": unseal,
            "open": open,
        }),
    )
}

/// Start the probe under `token` with `--out` in `out_dir`, wait, and read its report back.
fn spawn_under(token: &Token, exe: &Path, out_dir: &Path, label: &str, args: &[&OsStr]) -> Value {
    let out = out_dir.join(format!("pitboard-probe-d2b-{label}.json"));
    let mut full: Vec<&OsStr> = vec![OsStr::new("--out"), out.as_os_str()];
    full.extend_from_slice(args);
    match ffi::spawn(Some(token.raw()), exe, &full, CREATE_NO_WINDOW, None) {
        Ok(child) => json!({
            "started": true,
            "exit": child.wait_ms(30_000),
            "report": take_child_report(&out),
        }),
        Err(code) => json!({ "started": false, "error": code }),
    }
}
