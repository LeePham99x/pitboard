// pitboard.exe in Windows' consoles and shells, as Claude Code, a task or a person starts it.
#![cfg(windows)]

mod common;

use common::Env;
use std::io::Write;
use std::os::windows::process::CommandExt;
use std::process::Stdio;
use std::time::{Duration, Instant};
use windows_sys::Win32::System::Threading::{CREATE_NO_WINDOW, DETACHED_PROCESS};

fn wait_for<T>(what: &str, mut found: impl FnMut() -> Option<T>) -> T {
    let started = Instant::now();
    loop {
        if let Some(found) = found() {
            return found;
        }
        assert!(
            started.elapsed() < Duration::from_secs(30),
            "no {what} after 30 s"
        );
        std::thread::sleep(Duration::from_millis(50));
    }
}

fn child_of(parent: u32, program: &str) -> Option<u32> {
    pitboard_core::testing::processes()
        .expect("Windows lists its processes")
        .into_iter()
        .find(|(_, of, name)| *of == parent && name.eq_ignore_ascii_case(program))
        .map(|(pid, _, _)| pid)
}

#[derive(Debug, PartialEq)]
enum Console {
    None,
    WithoutWindow,
    WithWindow,
}

// As another process finds it, by attaching to it: this one leaves the console it had first.
fn console_of(pid: u32) -> Console {
    use windows_sys::Win32::System::Console::{AttachConsole, FreeConsole, GetConsoleWindow};
    use windows_sys::Win32::UI::WindowsAndMessaging::IsWindowVisible;
    // SAFETY: none takes a pointer, and the window is only asked whether it is visible.
    unsafe {
        FreeConsole();
        if AttachConsole(pid) == 0 {
            return Console::None;
        }
        let window = GetConsoleWindow();
        let shown = !window.is_null() && IsWindowVisible(window) != 0;
        FreeConsole();
        if shown {
            Console::WithWindow
        } else {
            Console::WithoutWindow
        }
    }
}

// `pitboard statusline` started by cmd.exe, itself started with `flags`, looked at while it waits
// for its input. A console it made would be there within moments of its start.
fn console_of_a_run_from_a_parent_started_with(env: &Env, flags: u32) -> Console {
    let mut parent = env.started("cmd.exe", &[]);
    parent
        .raw_arg("/d /s /c \"pitboard statusline\"")
        .creation_flags(flags)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null());
    let mut parent = parent.spawn().expect("cmd.exe starts");
    let pitboard = wait_for("pitboard.exe under cmd.exe", || {
        child_of(parent.id(), "pitboard.exe")
    });
    let looking = Instant::now();
    let mut seen = Console::None;
    while seen == Console::None && looking.elapsed() < Duration::from_secs(3) {
        seen = console_of(pitboard);
        std::thread::sleep(Duration::from_millis(100));
    }
    parent
        .stdin
        .take()
        .expect("its input")
        .write_all(b"{}")
        .expect("the session written");
    let out = parent.wait_with_output().expect("cmd.exe ends");
    assert!(out.status.success(), "{:?}", out.status);
    seen
}

// consoleAllocationPolicy=detached, which RF-E4 measured on the probe's builds.
#[test]
fn a_run_with_no_console_to_share_makes_none_and_one_shares_its_parents() {
    let env = Env::new("console-allocation");
    env.put_pitboard_on_path();
    assert_eq!(
        console_of_a_run_from_a_parent_started_with(&env, DETACHED_PROCESS),
        Console::None
    );
    assert_eq!(
        console_of_a_run_from_a_parent_started_with(&env, CREATE_NO_WINDOW),
        Console::WithoutWindow
    );
}
