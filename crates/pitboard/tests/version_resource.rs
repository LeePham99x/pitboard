// What the build script decides, and on Windows what pitboard.exe carries once built.

#[allow(dead_code)]
#[path = "../build/resources.rs"]
mod resources;

#[test]
fn the_file_version_is_the_release_tables() {
    for (version, file_version) in [
        ("1.0.0-beta.1", [1, 0, 0, 1]),
        ("1.0.0", [1, 0, 0, 1000]),
        ("1.0.1", [1, 0, 1, 1000]),
        ("1.1.0-beta.2", [1, 1, 0, 2]),
        ("0.15.0", [0, 15, 0, 1000]),
        ("2.10.3-beta.999", [2, 10, 3, 999]),
    ] {
        assert_eq!(
            resources::file_version(version),
            Some(file_version),
            "{version}"
        );
    }
    for refused in [
        "",
        "1.0",
        "1.0.0.0",
        "1.0.0-rc1",
        "1.0.0-rc.1",
        "1.0.0-beta",
        "1.0.0-beta.0",
        "1.0.0-beta.01",
        "1.0.0-beta.1000",
        "01.0.0",
        "1.0.0+build",
        "65536.0.0",
    ] {
        assert_eq!(resources::file_version(refused), None, "{refused}");
    }
    assert_eq!(
        resources::file_version(env!("CARGO_PKG_VERSION")).map(|parts| parts[3]),
        Some(1000),
        "a 0.x release follows the final rule"
    );
}

#[test]
fn without_a_resource_compiler_a_build_warns_unless_one_is_required() {
    assert_eq!(resources::required(None), Ok(false));
    assert_eq!(resources::required(Some("")), Ok(false));
    assert_eq!(resources::required(Some("1")), Ok(true));
    assert!(resources::required(Some("yes")).is_err());
    match resources::without_a_compiler(false) {
        resources::Without::Warn(said) => assert!(
            said.contains("PITBOARD_REQUIRE_VERSIONINFO=1 makes this an error"),
            "{said}"
        ),
        resources::Without::Fail(why) => panic!("{why}"),
    }
    match resources::without_a_compiler(true) {
        resources::Without::Fail(why) => assert!(
            why.starts_with("PITBOARD_REQUIRE_VERSIONINFO=1, and there is no resource compiler"),
            "{why}"
        ),
        resources::Without::Warn(said) => panic!("{said}"),
    }
}

#[test]
fn the_version_resource_names_pitboard_its_version_and_the_mapped_file_version() {
    let manifest = std::path::Path::new("pitboard.exe.manifest");
    let resource = resources::Resource {
        file_name: "pitboard.exe",
        description: "Pitboard command line",
        version: "1.1.0-beta.2",
        manifest,
    };
    let script = resources::script(&resource, [1, 1, 0, 2]);
    for line in [
        "FILEVERSION 1,1,0,2",
        "PRODUCTVERSION 1,1,0,2",
        r#"VALUE "CompanyName", "Pitboard""#,
        r#"VALUE "FileDescription", "Pitboard command line""#,
        r#"VALUE "FileVersion", "1.1.0.2""#,
        r#"VALUE "InternalName", "pitboard""#,
        r#"VALUE "OriginalFilename", "pitboard.exe""#,
        r#"VALUE "ProductName", "Pitboard""#,
        r#"VALUE "ProductVersion", "1.1.0-beta.2""#,
        "VALUE \"Translation\", 0x409, 1200",
    ] {
        assert!(
            script.lines().any(|said| said.trim() == line),
            "{line}: {script}"
        );
    }
}

#[test]
fn the_manifest_says_what_pitboard_exe_needs() {
    let manifest = include_str!("../build/pitboard.exe.manifest");
    for element in [
        "<consoleAllocationPolicy>detached</consoleAllocationPolicy>",
        "<asmv3:windowsSettings xmlns=\"http://schemas.microsoft.com/SMI/2024/WindowsSettings\">",
        r#"<longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>"#,
        r#"<supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>"#,
        r#"<requestedExecutionLevel level="asInvoker" uiAccess="false"/>"#,
    ] {
        assert!(manifest.contains(element), "{element}: {manifest}");
    }
    assert!(!manifest.contains("activeCodePage"), "every call is wide");
}

#[cfg(windows)]
mod built {
    use pitboard_core::testing::version_resource as read;
    use std::path::Path;

    const RT_MANIFEST: usize = 24;
    // The program's own manifest, the one CreateProcess reads.
    const ITS_OWN: usize = 1;

    fn pitboard() -> &'static Path {
        Path::new(env!("CARGO_BIN_EXE_pitboard"))
    }

    fn manifest(program: &Path) -> Option<String> {
        use std::os::windows::ffi::OsStrExt;
        use windows_sys::Win32::Foundation::FreeLibrary;
        use windows_sys::Win32::System::LibraryLoader::{
            FindResourceW, LOAD_LIBRARY_AS_DATAFILE, LOAD_LIBRARY_AS_IMAGE_RESOURCE,
            LoadLibraryExW, LoadResource, LockResource, SizeofResource,
        };
        let name: Vec<u16> = program.as_os_str().encode_wide().chain([0]).collect();
        // SAFETY: a NUL-terminated name; the program is mapped as data alone, and freed below.
        let module = unsafe {
            LoadLibraryExW(
                name.as_ptr(),
                std::ptr::null_mut(),
                LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE,
            )
        };
        if module.is_null() {
            return None;
        }
        // SAFETY: the module mapped above, and integer ids passed as MAKEINTRESOURCE passes them;
        // the resource's bytes stay mapped until the module is freed.
        let text = unsafe {
            let found = FindResourceW(module, ITS_OWN as *const u16, RT_MANIFEST as *const u16);
            let data = if found.is_null() {
                std::ptr::null_mut()
            } else {
                LockResource(LoadResource(module, found))
            };
            (!data.is_null()).then(|| {
                let size = SizeofResource(module, found) as usize;
                String::from_utf8_lossy(std::slice::from_raw_parts(data.cast::<u8>(), size))
                    .into_owned()
            })
        };
        // SAFETY: the module mapped above.
        unsafe { FreeLibrary(module) };
        text
    }

    // CI builds with PITBOARD_REQUIRE_VERSIONINFO=1, so a build without one fails before this.
    #[test]
    fn pitboard_exe_carries_its_version_resource() {
        let version = env!("CARGO_PKG_VERSION");
        let mapped = super::resources::file_version(version).expect("a version the table maps");
        assert_eq!(read::file_version(pitboard()), Some(mapped));
        assert_eq!(read::product_version(pitboard()), Some(mapped));
        for (name, value) in [
            ("ProductName", "Pitboard"),
            ("ProductVersion", version),
            ("FileVersion", &mapped.map(|n| n.to_string()).join(".")[..]),
            ("CompanyName", "Pitboard"),
            ("OriginalFilename", "pitboard.exe"),
        ] {
            assert_eq!(
                read::string(pitboard(), name).as_deref(),
                Some(value),
                "{name}"
            );
        }
    }

    #[test]
    fn pitboard_exe_carries_its_manifest() {
        let manifest = manifest(pitboard()).expect("an RT_MANIFEST resource");
        let flat: String = manifest.split_whitespace().collect::<Vec<_>>().join(" ");
        for said in [
            ">detached</consoleAllocationPolicy>",
            ">true</longPathAware>",
            "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}",
            r#"level="asInvoker""#,
        ] {
            assert!(flat.contains(said), "{said}: {flat}");
        }
        assert_eq!(
            flat.matches("<requestedExecutionLevel").count(),
            1,
            "the linker added a trustInfo of its own: {flat}"
        );
    }
}
