#[path = "build/versioninfo.rs"]
mod versioninfo;

fn main() {
    versioninfo::manifest(
        &std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("build/pitboard.exe.manifest"),
    );
    versioninfo::embed(
        versioninfo::Kind::Program,
        "pitboard",
        "Pitboard command line",
    );
}
