//! Processes on Windows, before this face is written.

use std::path::Path;

/// Whether process `pid` may still be running: always, until W18 asks Windows. The callers
/// only ever leave alone what a live process may still be using, so this keeps everything.
pub(crate) fn may_be_running(_pid: u32) -> bool {
    true
}

/// Whether `path` is a program this user may run: none is, until W17 finds programs as a
/// Windows shell does, by `PATHEXT`. So no tool's program is found, and no sign-in starts.
pub(crate) fn can_run(_path: &Path) -> bool {
    false
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
