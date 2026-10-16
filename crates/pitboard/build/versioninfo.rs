// The version resource SignPath asks of what it signs, ProductName Pitboard at the build's
// version and CompanyName Pitboard, where no certificate has to match it, and pitboard.exe's
// manifest, for each build script that includes this file by #[path].

// Each build script uses the part for its kind of binary, and pitboard's tests the rest.
#![allow(dead_code)]

use std::env;
use std::path::{Path, PathBuf};
use std::process::Command;

// Set by CI's Windows legs and the release, so a binary that ships never lacks the resource.
pub(crate) const REQUIRED: &str = "PITBOARD_REQUIRE_VERSIONINFO";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Kind {
    Program,
    Library,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Compiler {
    Rc(PathBuf),
    LlvmRc(PathBuf),
}

pub(crate) fn required(value: Option<&str>) -> Result<bool, String> {
    match value {
        None | Some("") => Ok(false),
        Some("1") => Ok(true),
        Some(other) => Err(format!("{REQUIRED} takes 1, not {other:?}")),
    }
}

// Part 3's "Version numbers": beta N of X.Y.Z is X.Y.Z.N and any other X.Y.Z is X.Y.Z.1000, so
// each version numbers above the one before it. Numbers are written as windows-versions.sh
// takes them, with no leading zero.
pub(crate) fn file_version(version: &str) -> Result<[u16; 4], String> {
    let refused = || {
        format!("{version} is neither X.Y.Z nor X.Y.Z-beta.N, the versions a Windows build numbers")
    };
    let number = |part: &str| {
        let canonical = !part.is_empty()
            && part.bytes().all(|b| b.is_ascii_digit())
            && (part == "0" || !part.starts_with('0'));
        canonical.then(|| part.parse::<u16>().ok()).flatten()
    };
    let (release, fourth) = match version.split_once('-') {
        Some((release, pre)) => (
            release,
            pre.strip_prefix("beta.")
                .and_then(number)
                .filter(|n| (1..1000).contains(n))
                .ok_or_else(refused)?,
        ),
        None => (version, 1000),
    };
    let fields = release
        .split('.')
        .map(number)
        .collect::<Option<Vec<_>>>()
        .ok_or_else(refused)?;
    match fields[..] {
        [major, minor, patch] => Ok([major, minor, patch, fourth]),
        _ => Err(refused()),
    }
}

pub(crate) fn script(
    kind: Kind,
    version: &str,
    file: [u16; 4],
    name: &str,
    description: &str,
) -> String {
    let numbers = file.map(|n| n.to_string()).join(",");
    let dotted = file.map(|n| n.to_string()).join(".");
    let (file_type, extension) = match kind {
        Kind::Program => (1, "exe"),
        Kind::Library => (2, "dll"),
    };
    // No #include: the flags are winver.h's values, so neither compiler needs the SDK's headers.
    format!(
        r#"1 VERSIONINFO
FILEVERSION {numbers}
PRODUCTVERSION {numbers}
FILEFLAGSMASK 0x3F
FILEFLAGS 0x0
FILEOS 0x40004
FILETYPE {file_type}
FILESUBTYPE 0x0
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904B0"
    BEGIN
      VALUE "CompanyName", "Pitboard"
      VALUE "FileDescription", "{description}"
      VALUE "FileVersion", "{dotted}"
      VALUE "InternalName", "{name}"
      VALUE "OriginalFilename", "{name}.{extension}"
      VALUE "ProductName", "Pitboard"
      VALUE "ProductVersion", "{version}"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
"#
    )
}

// What a build without a resource compiler does: it goes on and says so, unless REQUIRED is set.
pub(crate) fn without_compiler(required: bool) -> Result<String, String> {
    let said = format!(
        "no rc.exe or llvm-rc, so this build has no version resource ({REQUIRED}=1 refuses that)"
    );
    if required { Err(said) } else { Ok(said) }
}

// The Windows SDK's rc.exe, which no plain MSVC shell has on PATH, then rc.exe or llvm-rc on PATH.
pub(crate) fn find_compiler(host: &str, path: Option<&std::ffi::OsStr>) -> Option<Compiler> {
    let on_path = |names: &[&str]| {
        path.into_iter()
            .flat_map(env::split_paths)
            .flat_map(|folder| names.iter().map(move |name| folder.join(name)))
            .find(|candidate| candidate.is_file())
    };
    if host.contains("windows")
        && let Some(rc) = windows_kits().and_then(|kits| sdk_rc(&kits, host))
    {
        return Some(Compiler::Rc(rc));
    }
    on_path(&["rc.exe"])
        .map(Compiler::Rc)
        .or_else(|| on_path(&["llvm-rc", "llvm-rc.exe"]).map(Compiler::LlvmRc))
}

// The SDK's installer writes the key in either registry view, as embed-resource looks for it.
fn windows_kits() -> Option<PathBuf> {
    ["/reg:32", "/reg:64"].into_iter().find_map(|view| {
        let said = Command::new("reg")
            .args([
                "query",
                r"HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots",
                "/v",
                "KitsRoot10",
                view,
            ])
            .output()
            .ok()?;
        String::from_utf8_lossy(&said.stdout)
            .lines()
            .find_map(|line| line.trim().strip_prefix("KitsRoot10")?.split_once("REG_SZ"))
            .map(|(_, root)| PathBuf::from(root.trim()))
    })
}

// The newest SDK's rc.exe for this machine, else one that runs on it under emulation.
fn sdk_rc(kits: &Path, host: &str) -> Option<PathBuf> {
    let mut versions: Vec<(Vec<u32>, PathBuf)> = std::fs::read_dir(kits.join("bin"))
        .ok()?
        .filter_map(Result::ok)
        .filter_map(|entry| {
            let name = entry.file_name().into_string().ok()?;
            let numbers = name
                .split('.')
                .map(str::parse)
                .collect::<Result<Vec<u32>, _>>()
                .ok()?;
            Some((numbers, entry.path()))
        })
        .collect();
    versions.sort();
    let machines: &[&str] = if host.starts_with("aarch64") {
        &["arm64", "x64", "x86"]
    } else {
        &["x64", "x86"]
    };
    versions.iter().rev().find_map(|(_, folder)| {
        machines
            .iter()
            .map(|machine| folder.join(machine).join("rc.exe"))
            .find(|rc| rc.is_file())
    })
}

// In the script's folder, by file name: llvm-rc reads a path that starts with / as an option.
pub(crate) fn compile(
    compiler: &Compiler,
    folder: &Path,
    script: &str,
    resource: &str,
) -> Result<(), String> {
    let mut command = match compiler {
        Compiler::Rc(rc) => {
            let mut command = Command::new(rc);
            command.args(["/nologo", "/fo", resource]);
            command
        }
        Compiler::LlvmRc(rc) => {
            let mut command = Command::new(rc);
            command.args(["/no-preprocess", "/FO", resource]);
            command
        }
    };
    let done = command
        .arg(script)
        .current_dir(folder)
        .output()
        .map_err(|error| format!("{compiler:?} did not start: {error}"))?;
    if done.status.success() {
        Ok(())
    } else {
        Err(format!(
            "{compiler:?} failed: {}{}",
            String::from_utf8_lossy(&done.stdout),
            String::from_utf8_lossy(&done.stderr)
        ))
    }
}

// A build script reads what Cargo sets, not Pitboard's context.
#[allow(clippy::disallowed_methods)]
fn for_msvc() -> bool {
    env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows")
        && env::var("CARGO_CFG_TARGET_ENV").as_deref() == Ok("msvc")
}

// A program's own manifest, which the linker embeds; lld-link takes the same arguments.
pub(crate) fn manifest(file: &Path) {
    println!("cargo:rerun-if-changed={}", file.display());
    if !for_msvc() {
        return;
    }
    println!("cargo:rustc-link-arg-bins=/MANIFEST:EMBED");
    println!(
        "cargo:rustc-link-arg-bins=/MANIFESTINPUT:{}",
        file.display()
    );
    // Ours carries asInvoker, so the linker adds no trustInfo of its own beside it.
    println!("cargo:rustc-link-arg-bins=/MANIFESTUAC:NO");
}

// The build script's whole part: on a Windows target, the resource linked into the binary.
// A build script reads what Cargo sets, not Pitboard's context.
#[allow(clippy::disallowed_methods)]
pub(crate) fn embed(kind: Kind, name: &str, description: &str) {
    println!("cargo:rerun-if-env-changed={REQUIRED}");
    let required =
        required(env::var(REQUIRED).ok().as_deref()).unwrap_or_else(|why| panic!("{why}"));
    if !for_msvc() {
        return;
    }
    let version = env::var("CARGO_PKG_VERSION").expect("Cargo sets CARGO_PKG_VERSION");
    let file = file_version(&version).unwrap_or_else(|refused| panic!("{refused}"));
    let host = env::var("HOST").unwrap_or_default();
    let Some(compiler) = find_compiler(&host, env::var_os("PATH").as_deref()) else {
        match without_compiler(required) {
            Ok(said) => println!("cargo:warning={said}"),
            Err(said) => panic!("{said}"),
        }
        return;
    };
    let out = PathBuf::from(env::var_os("OUT_DIR").expect("Cargo sets OUT_DIR"));
    let (script_name, resource_name) = (format!("{name}.rc"), format!("{name}.res"));
    std::fs::write(
        out.join(&script_name),
        script(kind, &version, file, name, description),
    )
    .unwrap_or_else(|error| panic!("{}: {error}", out.display()));
    compile(&compiler, &out, &script_name, &resource_name)
        .unwrap_or_else(|refused| panic!("{refused}"));
    let linked = match kind {
        Kind::Program => "bins",
        Kind::Library => "cdylib",
    };
    println!(
        "cargo:rustc-link-arg-{linked}={}",
        out.join(resource_name).display()
    );
}
