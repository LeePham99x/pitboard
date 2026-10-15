//! Finding a program somebody named, the way the system's own launcher finds it.

use super::Os;
use std::ffi::{OsStr, OsString};
use std::path::{Path, PathBuf};

// What starts a program on Windows without a script host, in the order Windows' default
// `PATHEXT` gives them (RF-B1). Never `.ps1`, `.js`, `.vbs` or `.com`.
const RUNNABLE_ON_WINDOWS: [&str; 3] = [".exe", ".bat", ".cmd"];

/// The names a shell tries for a program in one folder: the name itself on macOS and Linux,
/// and on Windows the name with each of `PATHEXT`'s extensions that starts a program, in
/// `PATHEXT`'s own order, unless the name has one of them already.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct Lookup {
    extensions: Option<Vec<&'static str>>,
}

impl Lookup {
    pub(crate) fn new(os: Os, path_ext: Option<&OsStr>) -> Lookup {
        let extensions = match os {
            Os::MacOs | Os::Linux => None,
            Os::Windows => Some(kept(path_ext)),
        };
        Lookup { extensions }
    }

    // The build's own system, with `PATHEXT` as Windows sets it by default.
    pub(crate) fn here() -> Lookup {
        Lookup::new(super::OS, None)
    }

    pub(crate) fn spellings(&self, named: &Path) -> Vec<PathBuf> {
        match &self.extensions {
            Some(extensions) if !runnable_on_windows(named) => extensions
                .iter()
                .map(|extension| {
                    let mut spelt = OsString::from(named);
                    spelt.push(extension);
                    PathBuf::from(spelt)
                })
                .collect(),
            _ => vec![named.to_path_buf()],
        }
    }
}

// `PATHEXT` unset or empty is Windows' default.
fn kept(path_ext: Option<&OsStr>) -> Vec<&'static str> {
    let Some(named) = path_ext
        .and_then(OsStr::to_str)
        .filter(|named| !named.trim().is_empty())
    else {
        return RUNNABLE_ON_WINDOWS.to_vec();
    };
    let mut kept = Vec::new();
    for extension in named.split(';').map(str::trim) {
        if let Some(runnable) = RUNNABLE_ON_WINDOWS
            .into_iter()
            .find(|runnable| runnable.eq_ignore_ascii_case(extension))
            && !kept.contains(&runnable)
        {
            kept.push(runnable);
        }
    }
    kept
}

/// Whether `path` names a file Windows starts as a program: an `.exe`, `.bat` or `.cmd`, in
/// any case.
pub(crate) fn runnable_on_windows(path: &Path) -> bool {
    path.extension()
        .and_then(OsStr::to_str)
        .is_some_and(|extension| {
            RUNNABLE_ON_WINDOWS
                .iter()
                .any(|runnable| runnable[1..].eq_ignore_ascii_case(extension))
        })
}

/// Where a program somebody named is: the path itself, made absolute, when it has a
/// directory in it, or the first file on `search`, a list in `PATH`'s form, that can be run,
/// each folder tried with every name `lookup` gives before the next.
///
/// Found the way `execvp` finds one, which passes over a directory of that name and a file
/// this user may not run, so what is found here is what starts. Only a directory named from
/// the root is looked in: a relative one names a place relative to wherever Pitboard was
/// started, which says nothing about where a tool is installed, and a sign-in that runs
/// from a directory of its own would read it as somewhere else again. Windows' own shell
/// looks in the folder it runs in first, which Pitboard never does (RF-F3).
#[cfg(any(target_os = "linux", test))]
pub(crate) fn find(named: &Path, search: &OsStr, lookup: &Lookup) -> Option<PathBuf> {
    find_in(
        named,
        std::env::split_paths(search),
        lookup,
        super::proc::can_run,
    )
}

// `find` over `dirs` in their order, with whether a file can be run handed in.
pub(crate) fn find_in(
    named: &Path,
    dirs: impl IntoIterator<Item = PathBuf>,
    lookup: &Lookup,
    mut runnable: impl FnMut(&Path) -> bool,
) -> Option<PathBuf> {
    if named.components().count() > 1 {
        let named = std::path::absolute(named).ok()?;
        return lookup
            .spellings(&named)
            .into_iter()
            .find(|candidate| runnable(candidate));
    }
    find_among(named, dirs, lookup, runnable)
}

/// The first file called `named` in `dirs` that can be run, in their order, for a list of
/// places that is not in `PATH`'s form. Only a directory named from the root is looked in,
/// as on `PATH`.
pub(crate) fn find_among(
    named: &Path,
    dirs: impl IntoIterator<Item = PathBuf>,
    lookup: &Lookup,
    mut runnable: impl FnMut(&Path) -> bool,
) -> Option<PathBuf> {
    dirs.into_iter()
        .filter(|dir| dir.is_absolute())
        .flat_map(|dir| lookup.spellings(&dir.join(named)))
        .find(|candidate| runnable(candidate))
}

// The program an npm `.cmd` shim at `shim` starts, read and never run: the last file it names
// under `%dp0%`, its own folder, after the `node.exe` it would run a script with.
pub(crate) fn npm_shim_target(shim: &Path) -> Option<PathBuf> {
    let is_cmd = shim
        .extension()
        .is_some_and(|extension| extension.eq_ignore_ascii_case("cmd"));
    if !is_cmd {
        return None;
    }
    const FROM_ITS_FOLDER: &str = "\"%dp0%\\";
    let text = std::fs::read_to_string(shim).ok()?;
    let named = text
        .match_indices(FROM_ITS_FOLDER)
        .filter_map(|(at, _)| {
            let rest = &text[at + FROM_ITS_FOLDER.len()..];
            Some(&rest[..rest.find('"')?])
        })
        .last()?;
    let mut target = shim.parent()?.to_path_buf();
    target.extend(named.split('\\').filter(|part| !part.is_empty()));
    Some(target)
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a test sets up its scratch files as npm would"
)]
mod tests {
    use super::*;

    /// Only a directory named from the root is looked in. An empty entry and a relative one
    /// both name somewhere relative to wherever Pitboard was started, and a sign-in that
    /// runs from a directory of its own would start something else from there.
    #[test]
    fn only_a_directory_named_from_the_root_is_looked_in() {
        let mut looked = Vec::new();
        let root = std::env::temp_dir().join("bin");
        let search = std::env::join_paths([
            "".into(),
            "bin".into(),
            "./node_modules/.bin".into(),
            root.clone(),
        ])
        .expect("a search path");
        let found = find_in(
            Path::new("codex"),
            std::env::split_paths(&search),
            &Lookup::here(),
            |candidate| {
                looked.push(candidate.to_path_buf());
                false
            },
        );
        assert_eq!(found, None);
        assert_eq!(
            looked,
            Lookup::here().spellings(&root.join("codex")),
            "every name a shell tries, in the one folder named from the root"
        );
    }

    /// A program named with a directory relative to where Pitboard was started is found as
    /// that place, by its full path, so the sign-in that runs from a directory of its own
    /// starts the same program.
    #[test]
    fn a_program_named_relative_to_here_is_found_by_its_full_path() {
        let unix = Lookup::new(Os::Linux, None);
        let found = find_in(Path::new("./bin/codex"), Vec::new(), &unix, |_| true).expect("found");
        assert!(found.is_absolute(), "{}", found.display());
        assert_eq!(
            found,
            std::env::current_dir()
                .expect("a working directory")
                .join("bin/codex")
        );
    }

    fn looked_for(lookup: &Lookup, named: &str, dirs: &[&str]) -> Vec<PathBuf> {
        let mut looked = Vec::new();
        let found = find_among(
            Path::new(named),
            dirs.iter().map(|dir| std::env::temp_dir().join(dir)),
            lookup,
            |candidate| {
                looked.push(candidate.to_path_buf());
                false
            },
        );
        assert_eq!(found, None);
        looked
    }

    fn at(spelt: &[&str]) -> Vec<PathBuf> {
        spelt
            .iter()
            .map(|path| {
                let (dir, name) = path.split_once('/').expect("a folder and a name");
                std::env::temp_dir().join(dir).join(name)
            })
            .collect()
    }

    /// A Windows shell tries, in each folder, the name with each extension of `PATHEXT` in
    /// `PATHEXT`'s own order before the next folder. Only `.exe`, `.bat` and `.cmd` are kept,
    /// so a `.ps1`, `.js` or `.com` is never found, and Windows' default `PATHEXT` gives
    /// `.exe`, `.bat`, `.cmd` (RF-B1).
    #[test]
    fn a_windows_shell_tries_pathexts_programs_in_its_order_in_each_folder() {
        let measured = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC;.CPL";
        for path_ext in [None, Some(""), Some(measured)] {
            let lookup = Lookup::new(Os::Windows, path_ext.map(OsStr::new));
            assert_eq!(
                looked_for(&lookup, "claude", &["a", "b"]),
                at(&[
                    "a/claude.exe",
                    "a/claude.bat",
                    "a/claude.cmd",
                    "b/claude.exe",
                    "b/claude.bat",
                    "b/claude.cmd"
                ]),
                "{path_ext:?}"
            );
        }
        let reordered = Lookup::new(Os::Windows, Some(OsStr::new(".PS1; .cmd;.JS;.Exe;.CMD")));
        assert_eq!(
            looked_for(&reordered, "codex", &["a"]),
            at(&["a/codex.cmd", "a/codex.exe"])
        );
        let none = Lookup::new(Os::Windows, Some(OsStr::new(".PS1;.JS;.COM")));
        assert_eq!(looked_for(&none, "codex", &["a"]), Vec::<PathBuf>::new());
        let windows = Lookup::new(Os::Windows, None);
        assert_eq!(
            looked_for(&windows, "codex.CMD", &["a"]),
            at(&["a/codex.CMD"]),
            "a name with one of them is tried as it is"
        );
        assert_eq!(
            looked_for(&windows, "codex.js", &["a"]),
            at(&["a/codex.js.exe", "a/codex.js.bat", "a/codex.js.cmd"])
        );
        for os in [Os::MacOs, Os::Linux] {
            assert_eq!(
                looked_for(
                    &Lookup::new(os, Some(OsStr::new(measured))),
                    "codex",
                    &["a"]
                ),
                at(&["a/codex"])
            );
        }
    }

    #[test]
    fn only_an_exe_a_bat_or_a_cmd_runs_on_windows() {
        for runs in ["claude.exe", "claude.EXE", "codex.cmd", "x.Bat"] {
            assert!(runnable_on_windows(Path::new(runs)), "{runs}");
        }
        for not in [
            "claude",
            "claude.ps1",
            "codex.js",
            "x.com",
            "x.vbs",
            ".exe",
            "exe",
        ] {
            assert!(!runnable_on_windows(Path::new(not)), "{not}");
        }
    }

    // As npm's cmd-shim writes one for a native program and for a script.
    #[test]
    fn an_npm_shim_names_the_program_it_starts() {
        let dir = tempfile::tempdir().expect("a scratch npm prefix");
        let native = dir.path().join("claude.cmd");
        std::fs::write(
            &native,
            "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\n\
             SETLOCAL\r\nCALL :find_dp0\r\n\
             \"%dp0%\\node_modules\\@anthropic-ai\\claude-code\\bin\\claude.exe\"   %*\r\n",
        )
        .expect("a shim");
        let script = dir.path().join("codex.CMD");
        std::fs::write(
            &script,
            "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\n\
             SETLOCAL\r\nCALL :find_dp0\r\n\r\nIF EXIST \"%dp0%\\node.exe\" (\r\n  \
             SET \"_prog=%dp0%\\node.exe\"\r\n) ELSE (\r\n  SET \"_prog=node\"\r\n  \
             SET PATHEXT=%PATHEXT:;.JS;=;%\r\n)\r\n\r\nendLocal & goto #_undefined_# 2>NUL || \
             title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n",
        )
        .expect("a shim");
        let under = |parts: &[&str]| {
            let mut path = dir.path().to_path_buf();
            path.extend(parts);
            path
        };
        assert_eq!(
            npm_shim_target(&native),
            Some(under(&[
                "node_modules",
                "@anthropic-ai",
                "claude-code",
                "bin",
                "claude.exe"
            ]))
        );
        assert_eq!(
            npm_shim_target(&script),
            Some(under(&[
                "node_modules",
                "@openai",
                "codex",
                "bin",
                "codex.js"
            ]))
        );
        let plain = dir.path().join("other.cmd");
        std::fs::write(&plain, "@echo off\r\necho hi\r\n").expect("a script");
        assert_eq!(npm_shim_target(&plain), None);
        assert_eq!(npm_shim_target(&dir.path().join("absent.cmd")), None);
        assert_eq!(npm_shim_target(&dir.path().join("claude.exe")), None);
    }
}
