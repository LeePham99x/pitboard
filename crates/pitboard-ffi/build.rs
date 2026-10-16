#[path = "../pitboard/build/versioninfo.rs"]
mod versioninfo;

fn main() {
    versioninfo::embed(
        versioninfo::Kind::Library,
        "pitboard_ffi",
        "Pitboard's core for the Windows app",
    );
}
