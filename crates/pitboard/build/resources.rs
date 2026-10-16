// A Windows program's version resource and manifest, which its build script gives it. One file,
// so pitboard-ffi's build script can include it by #[path] as crates/pitboard/build.rs does.

use std::ffi::OsString;
use std::path::{Path, PathBuf};
use std::process::Command;

pub(crate) const REQUIRE: &str = "PITBOARD_REQUIRE_VERSIONINFO";

pub(crate) struct Resource<'a> {
    pub(crate) file_name: &'a str,
    pub(crate) description: &'a str,
    pub(crate) version: &'a str,
    pub(crate) manifest: &'a Path,
}

// What Cargo tells the build script.
pub(crate) struct Build {
    pub(crate) target_os: String,
    pub(crate) target_env: String,
    pub(crate) host: String,
    pub(crate) out_dir: PathBuf,
    pub(crate) path: Option<OsString>,
    pub(crate) required: bool,
}

pub(crate) enum Without {
    Warn(String),
    Fail(String),
}

// CI's Windows legs and the release set it, so what ships never depends on the image.
pub(crate) fn required(value: Option<&str>) -> Result<bool, String> {
    match value {
        None | Some("") => Ok(false),
        Some("1") => Ok(true),
        Some(other) => Err(format!("{REQUIRE} takes 1, not {other:?}")),
    }
}

// Part 3's "Version numbers": beta N of X.Y.Z is X.Y.Z.N, and X.Y.Z itself X.Y.Z.1000.
pub(crate) fn file_version(version: &str) -> Option<[u16; 4]> {
    let number = |part: &str| -> Option<u16> {
        let canonical = !part.is_empty()
            && part.bytes().all(|b| b.is_ascii_digit())
            && (part == "0" || !part.starts_with('0'));
        canonical.then(|| part.parse().ok()).flatten()
    };
    let (base, beta) = match version.split_once('-') {
        None => (version, None),
        Some((base, pre)) => (base, Some(pre.strip_prefix("beta.")?)),
    };
    let parts: Vec<u16> = base.split('.').map(number).collect::<Option<_>>()?;
    let [major, minor, patch] = parts[..] else {
        return None;
    };
    let fourth = match beta {
        None => 1000,
        Some(n) => number(n).filter(|n| (1..1000).contains(n))?,
    };
    Some([major, minor, patch, fourth])
}

pub(crate) fn script(resource: &Resource, numbers: [u16; 4]) -> String {
    let joined = |by: &str| numbers.map(|n| n.to_string()).join(by);
    let Resource {
        file_name,
        description,
        version,
        ..
    } = resource;
    let internal = file_name.strip_suffix(".exe").unwrap_or(file_name);
    // VOS_NT_WINDOWS32 and VFT_APP, with neither header to include.
    format!(
        r#"1 VERSIONINFO
FILEVERSION {numbers}
PRODUCTVERSION {numbers}
FILEFLAGSMASK 0x3F
FILEFLAGS 0x0
FILEOS 0x40004
FILETYPE 0x1
FILESUBTYPE 0x0
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904B0"
    BEGIN
      VALUE "CompanyName", "Pitboard"
      VALUE "FileDescription", "{description}"
      VALUE "FileVersion", "{dotted}"
      VALUE "InternalName", "{internal}"
      VALUE "OriginalFilename", "{file_name}"
      VALUE "ProductName", "Pitboard"
      VALUE "ProductVersion", "{version}"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
"#,
        numbers = joined(","),
        dotted = joined("."),
    )
}

pub(crate) fn without_a_compiler(required: bool) -> Without {
    let missing = "no resource compiler: rc.exe in the newest Windows SDK for this machine, or \
                   llvm-rc on PATH where the build crosses from another system";
    if required {
        Without::Fail(format!("{REQUIRE}=1, and there is {missing}"))
    } else {
        Without::Warn(format!(
            "built without a version resource, since there is {missing}; {REQUIRE}=1 makes \
             this an error"
        ))
    }
}

pub(crate) fn embed(resource: &Resource, build: &Build) {
    if build.target_os != "windows" || build.target_env != "msvc" {
        return;
    }
    // Ours carries asInvoker, so the linker adds no trustInfo of its own beside it.
    println!("cargo:rerun-if-changed={}", resource.manifest.display());
    println!("cargo:rustc-link-arg-bins=/MANIFEST:EMBED");
    println!(
        "cargo:rustc-link-arg-bins=/MANIFESTINPUT:{}",
        resource.manifest.display()
    );
    println!("cargo:rustc-link-arg-bins=/MANIFESTUAC:NO");

    let Some(numbers) = file_version(resource.version) else {
        panic!(
            "{} has no FileVersion: it takes X.Y.Z or X.Y.Z-beta.N, N from 1 to 999",
            resource.version
        );
    };
    let Some(compiler) = compiler(build) else {
        match without_a_compiler(build.required) {
            Without::Warn(said) => println!("cargo:warning={said}"),
            Without::Fail(why) => panic!("{why}"),
        }
        return;
    };
    let rc = build.out_dir.join("pitboard.rc");
    let res = build.out_dir.join("pitboard.res");
    std::fs::write(&rc, script(resource, numbers))
        .unwrap_or_else(|e| panic!("{} could not be written: {e}", rc.display()));
    let mut command = Command::new(&compiler);
    if is_llvm_rc(&compiler) {
        command.arg("/no-preprocess");
    } else {
        command.arg("/nologo");
    }
    // llvm-rc off Windows takes a path that starts with / for an option.
    let out = command
        .current_dir(&build.out_dir)
        .args(["/FO", "pitboard.res", "pitboard.rc"])
        .output()
        .unwrap_or_else(|e| panic!("{} could not be run: {e}", compiler.display()));
    if !out.status.success() {
        panic!(
            "{} could not compile {}: {}{}",
            compiler.display(),
            rc.display(),
            String::from_utf8_lossy(&out.stdout),
            String::from_utf8_lossy(&out.stderr)
        );
    }
    println!("cargo:rustc-link-arg-bins={}", res.display());
}

fn is_llvm_rc(compiler: &Path) -> bool {
    compiler
        .file_stem()
        .is_some_and(|stem| stem.eq_ignore_ascii_case("llvm-rc"))
}

// rc.exe as embed-resource finds it, since a plain MSVC shell has none on PATH.
fn compiler(build: &Build) -> Option<PathBuf> {
    if build.host.contains("-windows-") {
        let arch = match build.host.split('-').next()? {
            "x86_64" => "x64",
            "aarch64" => "arm64",
            "i686" | "i586" => "x86",
            _ => return None,
        };
        return newest_rc(&kits_root()?, arch);
    }
    std::env::split_paths(build.path.as_ref()?)
        .map(|dir| dir.join("llvm-rc"))
        .find(|candidate| candidate.is_file())
}

fn kits_root() -> Option<PathBuf> {
    let key = r"HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots";
    ["/reg:32", "/reg:64"].into_iter().find_map(|view| {
        let out = Command::new("reg")
            .args(["query", key, "/v", "KitsRoot10", view])
            .output()
            .ok()?;
        let text = String::from_utf8_lossy(&out.stdout).into_owned();
        text.lines().find_map(|line| {
            let (_, value) = line
                .trim()
                .strip_prefix("KitsRoot10")?
                .split_once("REG_SZ")?;
            Some(PathBuf::from(value.trim()))
        })
    })
}

fn newest_rc(root: &Path, arch: &str) -> Option<PathBuf> {
    let mut versions: Vec<(Vec<u32>, PathBuf)> = std::fs::read_dir(root.join("bin"))
        .ok()?
        .filter_map(|entry| {
            let path = entry.ok()?.path();
            let name = path.file_name()?.to_str()?.to_owned();
            let parts = name
                .split('.')
                .map(|part| part.parse().ok())
                .collect::<Option<Vec<u32>>>()?;
            Some((parts, path))
        })
        .collect();
    versions.sort();
    versions
        .into_iter()
        .rev()
        .map(|(_, dir)| dir.join(arch).join("rc.exe"))
        .find(|rc| rc.is_file())
}
