//! Processes, the POSIX way.

use crate::host::Ran;
use std::ffi::CString;
use std::io::{self, Read, Write};
use std::os::fd::AsRawFd;
use std::os::unix::ffi::OsStrExt;
use std::os::unix::process::CommandExt;
use std::path::Path;
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

/// How long a run past its ceiling is given to end once asked, before it is made to.
const GRACE: Duration = Duration::from_secs(2);

/// How long what a run printed is waited for once it ended. Anything still holding its output
/// open then is not of its process group, and is not waited on.
const DRAINED: Duration = Duration::from_secs(5);

/// Runs `command` in a process group of its own for at most `ceiling`, with `handed` to read on
/// descriptor 3 and nothing on standard input. Past the ceiling, the group is asked to end and
/// then made to; once the run ended, whatever it left in its group is ended too, so nothing it
/// started outlives it or keeps its output open.
pub(crate) fn run_within(
    mut command: Command,
    handed: Option<&[u8]>,
    ceiling: Duration,
) -> io::Result<Ran> {
    command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .process_group(0);
    let reader = match handed {
        Some(bytes) => {
            let (reader, mut writer) = io::pipe()?;
            writer.write_all(bytes)?;
            drop(writer);
            Some(reader)
        }
        None => None,
    };
    if let Some(reader) = &reader {
        let fd = reader.as_raw_fd();
        // SAFETY: the closure runs in the child between fork and exec, where only
        // async-signal-safe calls are allowed; `dup2` and `fcntl` are, and it touches no memory
        // the parent shares. The pipe's own descriptor is closed on exec; descriptor 3 is not.
        unsafe {
            command.pre_exec(move || {
                let handed = if fd == 3 {
                    libc::fcntl(3, libc::F_SETFD, 0)
                } else {
                    libc::dup2(fd, 3)
                };
                if handed == -1 {
                    return Err(io::Error::last_os_error());
                }
                Ok(())
            });
        }
    }
    let mut child = command.spawn()?;
    drop(reader);
    let stdout = child.stdout.take();
    let (said, printed) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let mut text = String::new();
        if let Some(stdout) = stdout {
            let _ = stdout.take(1 << 20).read_to_string(&mut text);
        }
        let _ = said.send(text);
    });
    let began = Instant::now();
    let mut timed_out = false;
    let status = loop {
        if let Some(status) = child.try_wait()? {
            break Some(status);
        }
        if began.elapsed() >= ceiling {
            timed_out = true;
            signal(&child, libc::SIGTERM);
            break ended_within(&mut child, GRACE);
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    signal(&child, libc::SIGKILL);
    let status = match status {
        Some(status) => Some(status),
        None => child.wait().ok(),
    };
    Ok(Ran {
        exit: status
            .and_then(|status| status.code())
            .filter(|_| !timed_out),
        stdout: printed.recv_timeout(DRAINED).unwrap_or_default(),
        timed_out,
    })
}

/// How `child` ended, where it did within `grace`.
fn ended_within(child: &mut Child, grace: Duration) -> Option<std::process::ExitStatus> {
    let began = Instant::now();
    while began.elapsed() < grace {
        if let Some(status) = child.try_wait().ok().flatten() {
            return Some(status);
        }
        std::thread::sleep(Duration::from_millis(100));
    }
    None
}

/// Sends `sig` to the process group `child` leads.
fn signal(child: &Child, sig: libc::c_int) {
    let Ok(group) = libc::pid_t::try_from(child.id()) else {
        return;
    };
    // SAFETY: `killpg` takes plain integers and touches no memory; the group is the one the
    // run was started in, of which `child` is the leader.
    unsafe {
        libc::killpg(group, sig);
    }
}

/// Whether process `pid` may still be running. A pid that cannot be probed is taken as
/// running: the callers only ever leave alone what a live process may still be using.
pub(crate) fn may_be_running(pid: u32) -> bool {
    let Ok(pid) = libc::pid_t::try_from(pid) else {
        return true;
    };
    // SAFETY: signal 0 is never delivered; `kill` only reports whether `pid` exists and may
    // be signalled, and touches no memory of this process.
    let probed = unsafe { libc::kill(pid, 0) };
    probed == 0 || io::Error::last_os_error().raw_os_error() == Some(libc::EPERM)
}

/// Whether `path` is a program this user may run, as `execvp` judges it: a regular file,
/// once every link is followed, that `access` lets this user execute. An execute bit for
/// somebody else is not enough, and `access` alone would take a directory, which this user
/// may search, for one.
pub(crate) fn can_run(path: &Path) -> bool {
    let Ok(name) = CString::new(path.as_os_str().as_bytes()) else {
        return false;
    };
    // SAFETY: `name` is a NUL-terminated string that outlives the call, and `access` only
    // reads it.
    let executable = unsafe { libc::access(name.as_ptr(), libc::X_OK) } == 0;
    executable && std::fs::metadata(path).is_ok_and(|found| found.is_file())
}

// Claude Code records a process's creation time as `procStartFt` on Windows alone.
pub(crate) fn started(_pid: u32) -> Option<u64> {
    None
}

// Only a Windows program carries a version resource.
pub(crate) fn product_version(_path: &Path) -> Option<[u16; 4]> {
    None
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a test sets up its scratch files as a person or a tool would"
)]
mod tests {
    use super::*;

    #[test]
    fn this_process_is_running_and_one_that_exited_is_not() {
        assert!(may_be_running(std::process::id()));
        let exited = {
            let mut child = Command::new("true").spawn().unwrap();
            let pid = child.id();
            child.wait().unwrap();
            pid
        };
        assert!(!may_be_running(exited));
    }

    /// A program is what `execvp` would start: a regular file, once every link is followed,
    /// that this user may execute. One only its group and others may execute is not this
    /// user's to run, however many execute bits it has; nor is a directory, which `access`
    /// would let this user search.
    #[test]
    fn a_program_is_a_regular_file_this_user_may_execute() {
        use std::os::unix::fs::PermissionsExt;
        let dir = std::env::temp_dir().join(format!("pitboard-can-run-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("a scratch directory");
        let made = |name: &str, mode: u32| {
            let path = dir.join(name);
            std::fs::write(&path, "#!/bin/sh\n").expect("a file");
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(mode))
                .expect("its mode");
            path
        };
        let everyone = made("everyone", 0o755);
        let mine = made("mine", 0o700);
        let theirs = made("theirs", 0o611);
        let plain = made("plain", 0o644);
        let folder = dir.join("folder");
        std::fs::create_dir(&folder).expect("a directory");
        let link = dir.join("link");
        std::os::unix::fs::symlink(&mine, &link).expect("a link");
        let dangling = dir.join("dangling");
        std::os::unix::fs::symlink(dir.join("gone"), &dangling).expect("a link");

        assert!(can_run(&everyone));
        assert!(can_run(&mine));
        assert!(can_run(&link), "a link to a program is that program");
        assert!(!can_run(&plain));
        assert!(!can_run(&folder), "a directory is not a program");
        assert!(!can_run(&dangling));
        assert!(!can_run(&dir.join("absent")));
        // SAFETY: geteuid only reports this process's effective user id.
        if unsafe { libc::geteuid() } != 0 {
            assert!(
                !can_run(&theirs),
                "only its group and others may execute it"
            );
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    fn shell(script: &str) -> Command {
        let mut command = Command::new("/bin/sh");
        command.args(["-c", script]);
        command
    }

    /// What is handed arrives on descriptor 3 and nowhere else: not on standard input, which
    /// Claude Code's print mode adds to its prompt, nor in an argument or a variable.
    #[test]
    fn what_is_handed_arrives_on_descriptor_three_alone() {
        let ran = run_within(
            shell(r#"printf '%s|' "$(cat <&3)" "$(cat)" "$*""#),
            Some(b"the-token"),
            Duration::from_secs(10),
        )
        .expect("ran");
        assert_eq!(ran.exit, Some(0));
        assert_eq!(ran.stdout, "the-token|||");
        assert!(!ran.timed_out);
    }

    #[test]
    fn with_nothing_handed_descriptor_three_is_closed() {
        let ran = run_within(
            shell("cat <&3 2>/dev/null; echo $?"),
            None,
            Duration::from_secs(10),
        )
        .expect("ran");
        assert_ne!(ran.stdout.trim(), "0");
    }

    /// Past the ceiling the run is stopped, and so is the child it started, which holds its
    /// output open: left running, it would keep what the run printed from being read whole.
    #[test]
    fn a_run_past_the_ceiling_is_stopped_with_its_children() {
        let ran = run_within(
            shell("(sleep 30; echo late) & echo started; sleep 30"),
            None,
            Duration::from_secs(1),
        )
        .expect("ran");
        assert!(ran.timed_out);
        assert_eq!(ran.exit, None);
        assert_eq!(
            ran.stdout, "started\n",
            "the child it started was stopped too"
        );
    }

    /// A run that ended, leaving a child behind with its output open, is not waited on until
    /// that child ends: the child is stopped, and what the run printed is read whole.
    #[test]
    fn what_a_run_left_behind_holding_its_output_is_stopped() {
        let ran =
            run_within(shell("sleep 30 & echo done"), None, Duration::from_secs(60)).expect("ran");
        assert_eq!(ran.exit, Some(0));
        assert_eq!(ran.stdout, "done\n");
    }
}
