# The release workflow's scripts, run on what a tag and crates.io could hand them.
import io
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CORE_UNCHANGED = REPO / ".github" / "scripts" / "core-unchanged.sh"


def run(*args):
    return subprocess.run(["sh", *map(str, args)], capture_output=True, text=True)


class CoreUnchanged(unittest.TestCase):
    def setUp(self):
        self.work = Path(tempfile.mkdtemp())
        self.core = self.work / "core"
        (self.core / "src" / "host").mkdir(parents=True)
        (self.core / "src" / "lib.rs").write_text("pub mod host;\n")
        (self.core / "src" / "host" / "mod.rs").write_text("pub fn os() {}\n")
        (self.core / "Cargo.toml").write_text(
            '[package]\nname = "pitboard-core"\nversion = "0.15.0"\n'
        )
        (self.core / "README.md").write_text("Pitboard\n")

    def tearDown(self):
        shutil.rmtree(self.work)

    def crate(self, changes=None, top="pitboard-core-0.15.0"):
        files = {
            "src/lib.rs": "pub mod host;\n",
            "src/host/mod.rs": "pub fn os() {}\n",
            "Cargo.toml.orig": (self.core / "Cargo.toml").read_text(),
            "Cargo.toml": "# normalized by cargo\n",
            "Cargo.lock": "version = 4\n",
            "README.md": "An older README\n",
            ".cargo_vcs_info.json": '{"git": {"sha1": "af8cdf19"}}\n',
        }
        files.update(changes or {})
        path = self.work / f"{top}.crate"
        with tarfile.open(path, "w:gz") as tar:
            for name, text in files.items():
                if text is None:
                    continue
                data = text.encode()
                info = tarfile.TarInfo(f"{top}/{name}")
                info.size = len(data)
                tar.addfile(info, io.BytesIO(data))
        return path

    def test_the_same_source_and_manifest_pass(self):
        done = run(CORE_UNCHANGED, self.crate(), self.core)
        self.assertEqual(done.returncode, 0, done.stderr)

    def test_a_change_without_a_new_version_is_refused(self):
        for changes in (
            {"src/host/mod.rs": "pub fn os() -> u8 { 0 }\n"},
            {"src/host/mod.rs": None},
            {"src/extra.rs": "\n"},
            {"Cargo.toml.orig": '[package]\nname = "pitboard-core"\nversion.workspace = true\n'},
        ):
            with self.subTest(changes=list(changes)):
                done = run(CORE_UNCHANGED, self.crate(changes), self.core)
                self.assertEqual(done.returncode, 1)
                self.assertIn(
                    f"pitboard-core-0.15.0 on crates.io is not what {self.core} holds: "
                    f"raise the version in {self.core}/Cargo.toml",
                    done.stderr,
                )

    def test_it_refuses_what_is_not_one_crate(self):
        empty = self.work / "empty.crate"
        with tarfile.open(empty, "w:gz"):
            pass
        for crate in (empty, self.crate({"Cargo.toml.orig": None})):
            with self.subTest(crate=crate.name):
                done = run(CORE_UNCHANGED, crate, self.core)
                self.assertEqual(done.returncode, 1)
                self.assertIn("is not one crate", done.stderr)

    def test_it_takes_two_arguments(self):
        done = run(CORE_UNCHANGED, self.crate())
        self.assertEqual(done.returncode, 2)
        self.assertIn("usage:", done.stderr)


if __name__ == "__main__":
    unittest.main()
