//! A tool's own headless run, which starts an account's period: the program, what it is
//! given, how long it may take, and what it came to.
//!
//! The run is given the login's access token on file descriptor 3 and nothing else of it, so
//! it can spend no refresh token a park or the tool itself still holds. Its environment is
//! built from nothing: where programs are, who the person is, the proxy Pitboard was given,
//! and a home of its own under Pitboard's directory, so no variable that signs the tool in
//! some other way reaches it (`settings::OVERRIDING_ENV`), and nothing of the person's own
//! configuration, history or sessions is read or written.

use crate::context::Context;
use crate::provider::ProviderId;
use crate::service::Permit;
use std::ffi::OsString;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;

/// How long a run is given before it is stopped. The measured runs took 7 to 15 seconds.
const CEILING: Duration = Duration::from_secs(120);

/// A run about to start: the command, and the access token it reads on descriptor 3.
pub(crate) struct Headless {
    pub(crate) command: Command,
    pub(crate) token: Option<String>,
}

/// What a run came to: its exit status, `None` where a signal or the ceiling ended it, and
/// what it printed on standard output.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Ran {
    pub exit: Option<i32>,
    pub stdout: String,
    pub timed_out: bool,
}

/// What a run came to, as the tool's own result says it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Verdict {
    /// The request was answered.
    Answered,
    /// A limit of the account is used up, and the request was refused.
    LimitReached,
    /// The service refused the access token.
    LoginRefused,
    /// The service could not be reached, or was overloaded.
    Unreachable,
    /// Anything else, in the tool's own words.
    Failed(String),
}

/// The private home a tool's warm-up runs in, under Pitboard's directory: `home` for `HOME`,
/// `tmp` for `TMPDIR`, `work` to run from, and whatever the tool keeps of its own beside them.
pub(crate) fn home(ctx: &Context, tool: ProviderId) -> PathBuf {
    crate::home::dir(ctx).join("warm").join(tool.code())
}

/// Makes `home` afresh, each folder private to this user. Nothing one run left is there for
/// the next, which may be another account's: what a tool keeps of its own, such as the
/// policies and profile it fetches with a login, is that login's. Every run was measured
/// this way, from an empty home.
pub(crate) fn prepare(permit: Permit, home: &Path) -> std::io::Result<()> {
    if home.exists() {
        crate::host::fs::remove_dir_all(permit, home)?;
    }
    if let Some(parent) = home.parent().filter(|parent| !parent.is_dir()) {
        crate::host::fs::create_private_dir(permit, parent)?;
    }
    for dir in [
        home.to_path_buf(),
        home.join("home"),
        home.join("tmp"),
        home.join("work"),
    ] {
        crate::host::fs::create_private_dir(permit, &dir)?;
    }
    Ok(())
}

/// `tool`'s own program, in `home`, with nothing of this process's environment but `PATH`, the
/// person's name and the proxy Pitboard was given.
pub(crate) fn command(ctx: &Context, permit: Permit, tool: ProviderId, home: &Path) -> Command {
    let mut command = crate::provider::command(ctx, permit, tool);
    let path: Option<OsString> = command
        .get_envs()
        .find(|(name, _)| *name == "PATH")
        .and_then(|(_, value)| value.map(ToOwned::to_owned));
    command.env_clear();
    if let Some(path) = path {
        command.env("PATH", path);
    }
    command
        .env("HOME", home.join("home"))
        .env("TMPDIR", home.join("tmp"))
        .env("LANG", "en_US.UTF-8")
        .env("TERM", "dumb")
        .env("NO_COLOR", "1")
        .current_dir(home.join("work"))
        .stdin(Stdio::null());
    if let Some(user) = &ctx.user {
        command.env("USER", user);
    }
    for (name, value) in ctx.proxy.variables() {
        command.env(name, value);
    }
    command
}

/// What a test or a fixture plays a run as, in place of starting a program: given what the run
/// would have been started as, what it came to.
#[cfg(any(test, feature = "test-support"))]
pub trait WarmScript: Send + Sync + std::fmt::Debug {
    fn run(&self, started: &Started) -> Ran;
}

/// What a run was started as, for a [`WarmScript`]: the program, its arguments, its whole
/// environment, the folder it runs in, and what it was handed on descriptor 3.
#[cfg(any(test, feature = "test-support"))]
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Started {
    pub program: PathBuf,
    pub args: Vec<String>,
    pub env: Vec<(String, String)>,
    pub dir: Option<PathBuf>,
    pub token: Option<String>,
}

/// Runs `headless` for at most [`CEILING`], or plays it where a test or a fixture said to.
pub(crate) fn run(ctx: &Context, headless: Headless) -> std::io::Result<Ran> {
    #[cfg(any(test, feature = "test-support"))]
    if let Some(script) = ctx.warm_script() {
        let command = &headless.command;
        let text = |s: &std::ffi::OsStr| s.to_string_lossy().into_owned();
        let started = Started {
            program: PathBuf::from(command.get_program()),
            args: command.get_args().map(text).collect(),
            env: command
                .get_envs()
                .filter_map(|(name, value)| Some((text(name), text(value?))))
                .collect(),
            dir: command.get_current_dir().map(Path::to_path_buf),
            token: headless.token.clone(),
        };
        return Ok(script.run(&started));
    }
    #[cfg(not(any(test, feature = "test-support")))]
    let _ = ctx;
    spawn(headless, CEILING)
}

#[cfg(unix)]
fn spawn(headless: Headless, ceiling: Duration) -> std::io::Result<Ran> {
    use std::io::{Read, Write};
    use std::os::fd::AsRawFd;
    use std::os::unix::process::CommandExt;

    let Headless { mut command, token } = headless;
    command
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .process_group(0);
    let reader = match token {
        Some(token) => {
            let (reader, mut writer) = std::io::pipe()?;
            writer.write_all(token.as_bytes())?;
            drop(writer);
            Some(reader)
        }
        None => None,
    };
    if let Some(reader) = &reader {
        let fd = reader.as_raw_fd();
        // SAFETY: the closure runs in the child between fork and exec, where only
        // async-signal-safe calls are allowed; `dup2` and `fcntl` are, and it touches no memory
        // the parent shares. The pipe's own descriptor is closed on exec; descriptor 3, which
        // the tool reads, is not.
        unsafe {
            command.pre_exec(move || {
                let handed = if fd == 3 {
                    libc::fcntl(3, libc::F_SETFD, 0)
                } else {
                    libc::dup2(fd, 3)
                };
                if handed == -1 {
                    return Err(std::io::Error::last_os_error());
                }
                Ok(())
            });
        }
    }
    let mut child = command.spawn()?;
    drop(reader);
    let mut stdout = child.stdout.take();
    let reading = std::thread::spawn(move || {
        let mut said = String::new();
        if let Some(stdout) = stdout.as_mut() {
            let _ = stdout.take(1 << 20).read_to_string(&mut said);
        }
        said
    });
    let started = std::time::Instant::now();
    let (status, timed_out) = loop {
        if let Some(status) = child.try_wait()? {
            break (Some(status), false);
        }
        if started.elapsed() >= ceiling {
            stop(&mut child);
            break (child.wait().ok(), true);
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    Ok(Ran {
        exit: status
            .and_then(|status| status.code())
            .filter(|_| !timed_out),
        stdout: reading.join().unwrap_or_default(),
        timed_out,
    })
}

/// Stops a run that took too long, with whatever it started: its whole process group, asked
/// first and then made to.
#[cfg(unix)]
fn stop(child: &mut std::process::Child) {
    let Ok(group) = i32::try_from(child.id()) else {
        let _ = child.kill();
        return;
    };
    // SAFETY: `killpg` takes plain integers and touches no memory; the group is the one the
    // run was started in, of which it is the leader.
    unsafe {
        libc::killpg(group, libc::SIGTERM);
    }
    for _ in 0..20 {
        if child.try_wait().ok().flatten().is_some() {
            return;
        }
        std::thread::sleep(Duration::from_millis(100));
    }
    // SAFETY: as above.
    unsafe {
        libc::killpg(group, libc::SIGKILL);
    }
}

#[cfg(not(unix))]
fn spawn(_: Headless, _: Duration) -> std::io::Result<Ran> {
    Err(std::io::Error::new(
        std::io::ErrorKind::Unsupported,
        "a warm-up runs on macOS and Linux",
    ))
}

#[cfg(all(test, unix))]
mod tests {
    use super::*;

    fn shell(script: &str, token: Option<&str>) -> Headless {
        let mut command = Command::new("/bin/sh");
        command.args(["-c", script]).stdin(Stdio::null());
        Headless {
            command,
            token: token.map(str::to_owned),
        }
    }

    /// The token arrives on descriptor 3 and nowhere else: not on standard input, which print
    /// mode adds to its prompt, nor in an argument or a variable.
    #[test]
    fn the_token_is_handed_on_descriptor_three_alone() {
        let ran = spawn(
            shell(
                r#"printf '%s|' "$(cat <&3)" "$(cat)" "$*""#,
                Some("the-token"),
            ),
            Duration::from_secs(10),
        )
        .expect("ran");
        assert_eq!(ran.exit, Some(0));
        assert_eq!(ran.stdout, "the-token|||");
        assert!(!ran.timed_out);
    }

    #[test]
    fn with_no_token_descriptor_three_is_closed() {
        let ran = spawn(
            shell("cat <&3 2>/dev/null; echo $?", None),
            Duration::from_secs(10),
        )
        .expect("ran");
        assert_ne!(ran.stdout.trim(), "0");
    }

    /// A run past the ceiling is stopped with whatever it started, and says it was.
    #[test]
    fn a_run_past_the_ceiling_is_stopped_with_its_children() {
        let marker = std::env::temp_dir().join(format!("pitboard-warm-run-{}", std::process::id()));
        let _ = std::fs::remove_file(&marker);
        let script = format!("(sleep 3; touch {}) & sleep 30", marker.display());
        let began = std::time::Instant::now();
        let ran = spawn(shell(&script, None), Duration::from_secs(1)).expect("ran");
        assert!(ran.timed_out);
        assert_eq!(ran.exit, None);
        assert!(began.elapsed() < Duration::from_secs(10));
        std::thread::sleep(Duration::from_secs(4));
        assert!(!marker.exists(), "the child it started was stopped too");
    }

    #[test]
    fn a_home_is_made_afresh_for_each_run() {
        let dir = std::env::temp_dir().join(format!("pitboard-warm-home-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let home = dir.join("warm").join("claude");
        prepare(Permit::for_a_test(), &home).expect("made");
        std::fs::write(home.join("home").join("left.json"), "{}").expect("written");
        prepare(Permit::for_a_test(), &home).expect("made again");
        assert!(!home.join("home").join("left.json").exists());
        for sub in ["home", "tmp", "work"] {
            assert!(home.join(sub).is_dir(), "{sub}");
        }
        std::fs::remove_dir_all(&dir).expect("cleanup");
    }
}
