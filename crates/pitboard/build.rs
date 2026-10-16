#![allow(
    clippy::disallowed_methods,
    reason = "a build script reads what Cargo sets for it, not Pitboard's environment"
)]

#[path = "build/resources.rs"]
mod resources;

use std::path::{Path, PathBuf};

fn main() {
    println!("cargo:rerun-if-changed=build/resources.rs");
    println!("cargo:rerun-if-env-changed={}", resources::REQUIRE);
    let var = |name: &str| std::env::var(name).unwrap_or_default();
    let required = resources::required(std::env::var(resources::REQUIRE).ok().as_deref())
        .unwrap_or_else(|why| panic!("{why}"));
    let build = resources::Build {
        target_os: var("CARGO_CFG_TARGET_OS"),
        target_env: var("CARGO_CFG_TARGET_ENV"),
        host: var("HOST"),
        out_dir: PathBuf::from(var("OUT_DIR")),
        path: std::env::var_os("PATH"),
        required,
    };
    let manifest = Path::new(env!("CARGO_MANIFEST_DIR")).join("build/pitboard.exe.manifest");
    resources::embed(
        &resources::Resource {
            file_name: "pitboard.exe",
            description: "Pitboard command line",
            version: env!("CARGO_PKG_VERSION"),
            manifest: &manifest,
        },
        &build,
    );
}
