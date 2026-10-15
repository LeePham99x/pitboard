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
RELEASE_KIND = REPO / ".github" / "scripts" / "release-kind.sh"
KEYS = ["version", "base", "kind", "prerelease", "windows", "crates", "latest"]
NOT_A_TAG = "is not a release tag: vX.Y.Z, vX.Y.Z-rcN or vX.Y.Z-beta.N"


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


class ReleaseKind(unittest.TestCase):
    def setUp(self):
        self.work = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.work)

    def kind(self, tag, manifest, published=()):
        listed = self.work / "published"
        listed.write_text("".join(f"{line}\n" for line in published))
        return run(RELEASE_KIND, tag, manifest, listed)

    def outputs(self, tag, manifest, published=()):
        done = self.kind(tag, manifest, published)
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(done.stderr, "")
        pairs = [line.split("=", 1) for line in done.stdout.splitlines()]
        self.assertEqual([key for key, _ in pairs], KEYS)
        return dict(pairs)

    def assertRefused(self, tag, manifest, message, published=()):
        done = self.kind(tag, manifest, published)
        self.assertEqual(done.returncode, 1, done.stdout)
        self.assertEqual(done.stdout, "")
        self.assertIn(message, done.stderr)

    def test_each_kind_of_release(self):
        # tag, Cargo.toml, published full releases, kind, windows, crates, latest
        rows = [
            ("v0.15.0", "0.15.0", ["v0.14.0"], "full", "false", "true", "true"),
            ("v0.15.0-rc1", "0.15.0", ["v0.14.0"], "rc", "false", "false", "false"),
            ("v0.15.2", "0.15.2", ["v1.0.0"], "full", "false", "true", "false"),
            ("v1.0.0-beta.1", "1.0.0-beta.1", ["v0.15.0"], "beta", "true", "true", "false"),
            ("v1.0.0-rc1", "1.0.0", ["v0.15.0"], "rc", "true", "false", "false"),
            ("v1.0.0", "1.0.0", ["v0.15.2"], "full", "true", "true", "true"),
            ("v1.1.0-beta.1", "1.1.0-beta.1", ["v1.0.0"], "beta", "true", "true", "false"),
        ]
        for tag, manifest, published, kind, windows, crates, latest in rows:
            with self.subTest(tag=tag):
                self.assertEqual(
                    self.outputs(tag, manifest, published),
                    {
                        "version": tag[1:],
                        "base": manifest,
                        "kind": kind,
                        "prerelease": str(kind != "full").lower(),
                        "windows": windows,
                        "crates": crates,
                        "latest": latest,
                    },
                )

    def test_only_a_full_release_nothing_published_outranks_is_latest(self):
        cases = [
            ("v0.15.1", ["v0.15.0", "v0.14.0"], "true"),
            ("v0.15.2", ["v1.0.0", "v0.15.1"], "false"),
            ("v1.0.1", ["v1.0.0", "v0.15.2"], "true"),
            ("v1.0.0", ["v1.0.0", "v0.15.2"], "true"),
            ("v0.9.0", ["v0.10.0"], "false"),
            ("v0.10.0", ["v0.9.0"], "true"),
            ("v2.0.0", [], "true"),
        ]
        for tag, published, latest in cases:
            with self.subTest(tag=tag, published=published):
                self.assertEqual(self.outputs(tag, tag[1:], published)["latest"], latest)

    def test_a_tag_or_manifest_that_disagrees_is_refused(self):
        for tag, manifest in (
            ("v1.0.0-beta.1", "1.0.0"),
            ("v1.0.0", "1.0.0-beta.1"),
            ("v1.0.0-rc1", "1.0.0-beta.1"),
            ("v1.0.0-rc1", "1.0.0-rc1"),
            ("v1.0.1", "1.0.0"),
        ):
            with self.subTest(tag=tag, manifest=manifest):
                self.assertRefused(
                    tag, manifest, f"tag {tag} does not match Cargo.toml's {manifest}"
                )
        self.assertRefused(
            "v0.15.0-beta.1",
            "0.15.0-beta.1",
            "v0.15.0-beta.1 is a beta of a 0.x release: betas are Windows betas, which start at "
            "1.0.0",
        )

    def test_a_tag_that_is_none_of_the_three_is_refused(self):
        for tag in (
            "v1.0.0-alpha.1",
            "v1.0.0-beta.1-rc1",
            "v1.0.0-beta.0",
            "v1.0.0-beta.99",
            "v1.0.0-beta.01",
            "v1.0.0-rc0",
            "v1.0.0-rc01",
            "v1.0.0-rc",
            "v1.0.0-",
            "v1.0",
            "v01.0.0",
            "1.0.0",
            "v",
            "",
            "v1.0.0$(id)",
            "v1.0.0`id`",
            "v1.0.0\nlatest=true",
            "v1.0.0 ",
        ):
            with self.subTest(tag=tag):
                self.assertRefused(tag, tag[1:], NOT_A_TAG)

    def test_a_published_full_release_with_another_tag_is_refused(self):
        self.assertRefused(
            "v1.0.1",
            "1.0.1",
            "the published full release nightly has no vX.Y.Z tag",
            ["v1.0.0", "nightly"],
        )

    def test_it_takes_three_arguments(self):
        done = run(RELEASE_KIND, "v1.0.0", "1.0.0")
        self.assertEqual(done.returncode, 2)
        self.assertIn("usage:", done.stderr)


if __name__ == "__main__":
    unittest.main()
