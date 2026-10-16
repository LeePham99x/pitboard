#[path = "../pitboard/build/versioninfo.rs"]
mod versioninfo;

fn main() {
    versioninfo::embed(
        versioninfo::Kind::Library,
        "pitboard_share_ffi",
        "Pitboard's check of a shared page",
    );
}
