# The release workflow's scripts, run on what a tag and crates.io could hand them.
import io
import re
import shutil
import subprocess
import tarfile
import tempfile
import tomllib
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CORE_UNCHANGED = REPO / ".github" / "scripts" / "core-unchanged.sh"
RELEASE_KIND = REPO / ".github" / "scripts" / "release-kind.sh"
BUILD_NUMBER = REPO / "apps" / "macos" / "scripts" / "build-number.sh"
PACKAGE_WINDOWS = REPO / ".github" / "scripts" / "package-windows.ps1"
WINDOWS_TARGETS = ("x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc")
TARBALL_TARGETS = (
    "aarch64-apple-darwin",
    "x86_64-apple-darwin",
    "x86_64-unknown-linux-musl",
    "aarch64-unknown-linux-musl",
)
KEYS = ["version", "base", "kind", "prerelease", "windows", "crates", "latest", "signed"]
NOT_A_TAG = "is not a release tag: vX.Y.Z, vX.Y.Z-rcN or vX.Y.Z-beta.N"
NO_SIGNER = (
    "Pitboard for Windows has no signer yet, and a release candidate or final release is never "
    "unsigned."
)
NORMALIZED = """[package]
name = "pitboard-core"
version = "0.15.0"

[dependencies.ureq]
version = "3"
features = [
    "rustls-no-provider",
    "platform-verifier",
]
default-features = false
"""
NO_BUILD_NUMBER = (
    "has no build number: it takes X.Y.Z or X.Y.Z-beta.N, with Y and Z at most 99 and N from 1 "
    "to 98"
)


def run(*args):
    return subprocess.run(["sh", *map(str, args)], capture_output=True, text=True)


class CoreUnchanged(unittest.TestCase):
    def setUp(self):
        self.work = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.work)

    def crate(self, name, changes=None, top="pitboard-core-0.15.0"):
        files = {
            "src/lib.rs": "pub mod host;\n",
            "src/host/mod.rs": "pub fn os() {}\n",
            "Cargo.toml.orig": (
                '[package]\nname = "pitboard-core"\nversion = "0.15.0"\n\n'
                "[dependencies]\nureq.workspace = true\n"
            ),
            "Cargo.toml": NORMALIZED,
            "Cargo.lock": "version = 4\n",
            "README.md": "Pitboard\n",
            ".cargo_vcs_info.json": '{"git": {"sha1": "af8cdf19"}}\n',
        }
        files.update(changes or {})
        path = self.work / f"{name}.crate"
        with tarfile.open(path, "w:gz") as tar:
            for file, text in files.items():
                if text is None:
                    continue
                data = text.encode()
                info = tarfile.TarInfo(f"{top}/{file}")
                info.size = len(data)
                tar.addfile(info, io.BytesIO(data))
        return path

    def compared(self, published, packaged=None):
        return run(CORE_UNCHANGED, published, packaged or self.crate("packaged"))

    def test_the_same_source_and_manifests_pass(self):
        published = self.crate(
            "published",
            {
                "Cargo.lock": "version = 3\n",
                "README.md": "An older README\n",
                ".cargo_vcs_info.json": '{"git": {"sha1": "22722731"}}\n',
            },
        )
        done = self.compared(published)
        self.assertEqual(done.returncode, 0, done.stderr)

    def test_a_change_without_a_new_version_is_refused(self):
        for changes in (
            {"src/host/mod.rs": "pub fn os() -> u8 { 0 }\n"},
            {"src/host/mod.rs": None},
            {"src/extra.rs": "\n"},
            {"Cargo.toml.orig": '[package]\nname = "pitboard-core"\nversion.workspace = true\n'},
            # Only the root's [workspace.dependencies] moved: ureq lost a feature.
            {"Cargo.toml": NORMALIZED.replace('    "platform-verifier",\n', "")},
        ):
            with self.subTest(changes=list(changes)):
                done = self.compared(self.crate("published", changes))
                self.assertEqual(done.returncode, 1)
                self.assertIn(
                    "pitboard-core-0.15.0 on crates.io is not what this tree packages: raise the "
                    "version in crates/pitboard-core/Cargo.toml",
                    done.stderr,
                )

    def test_it_refuses_what_is_not_one_crate(self):
        empty = self.work / "empty.crate"
        with tarfile.open(empty, "w:gz"):
            pass
        unnamed = self.crate("unnamed", {"Cargo.toml.orig": None})
        pairs = ((empty, None), (unnamed, None), (self.crate("published"), empty))
        for published, packaged in pairs:
            with self.subTest(published=published.name, packaged=packaged and packaged.name):
                done = self.compared(published, packaged)
                self.assertEqual(done.returncode, 1)
                self.assertIn("is not one crate", done.stderr)

    def test_it_takes_two_arguments(self):
        done = run(CORE_UNCHANGED, self.crate("published"))
        self.assertEqual(done.returncode, 2)
        self.assertIn("usage:", done.stderr)


class ReleaseKind(unittest.TestCase):
    def setUp(self):
        self.work = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.work)

    def kind(self, tag, manifest, published=(), signer="signpath"):
        listed = self.work / "published"
        listed.write_text("".join(f"{line}\n" for line in published))
        return run(RELEASE_KIND, tag, manifest, listed, signer)

    def outputs(self, tag, manifest, published=(), signer="signpath"):
        done = self.kind(tag, manifest, published, signer)
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(done.stderr, "")
        pairs = [line.split("=", 1) for line in done.stdout.splitlines()]
        self.assertEqual([key for key, _ in pairs], KEYS)
        return dict(pairs)

    def assertRefused(self, tag, manifest, message, published=(), signer="signpath"):
        done = self.kind(tag, manifest, published, signer)
        self.assertEqual(done.returncode, 1, done.stdout)
        self.assertEqual(done.stdout, "")
        self.assertIn(message, done.stderr)

    def test_each_kind_of_release(self):
        # tag, Cargo.toml, published full releases, kind, windows, crates, latest, with a signer set
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
                        "signed": windows,
                    },
                )

    def test_a_windows_release_is_signed_once_a_signer_is_set(self):
        # tag, Cargo.toml, signer, signed
        for tag, manifest, signer, signed in (
            ("v1.0.0-beta.1", "1.0.0-beta.1", "", "false"),
            ("v1.0.0-beta.2", "1.0.0-beta.2", "signpath", "true"),
            ("v1.0.0-rc1", "1.0.0", "sslcom", "true"),
            ("v1.0.0", "1.0.0", "signpath", "true"),
            ("v0.15.0", "0.15.0", "signpath", "false"),
            ("v0.15.0-rc1", "0.15.0", "", "false"),
        ):
            with self.subTest(tag=tag, signer=signer):
                self.assertEqual(self.outputs(tag, manifest, signer=signer)["signed"], signed)

    def test_a_windows_release_candidate_or_final_release_needs_a_signer(self):
        for tag, manifest in (("v1.0.0-rc1", "1.0.0"), ("v1.0.0", "1.0.0"), ("v2.3.4", "2.3.4")):
            with self.subTest(tag=tag):
                self.assertRefused(tag, manifest, NO_SIGNER, signer="")

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

    def test_it_takes_four_arguments(self):
        listed = self.work / "published"
        listed.write_text("")
        for args in (("v1.0.0", "1.0.0", listed), ("v1.0.0", "1.0.0", listed, "", "x")):
            with self.subTest(count=len(args)):
                done = run(RELEASE_KIND, *args)
                self.assertEqual(done.returncode, 2)
                self.assertIn("usage:", done.stderr)


class Binstall(unittest.TestCase):
    # As cargo-binstall 1.25.3 merges them (binstalk-types, PkgMeta::merge_overrides): a target's
    # override replaces pkg-url, pkg-fmt and bin-dir, and adds its disabled-strategies to the
    # package's.
    def resolved(self, target):
        crate = tomllib.loads((REPO / "crates" / "pitboard" / "Cargo.toml").read_text())
        meta = dict(crate["package"]["metadata"]["binstall"])
        override = meta.pop("overrides").get(target, {})
        disabled = set(meta.get("disabled-strategies", []))
        disabled |= set(override.get("disabled-strategies", []))
        meta.update({k: override[k] for k in ("pkg-url", "pkg-fmt", "bin-dir") if k in override})
        meta["disabled-strategies"] = disabled
        return meta

    def expand(self, template, target, version):
        workspace = tomllib.loads((REPO / "Cargo.toml").read_text())
        values = {
            "repo": workspace["workspace"]["package"]["repository"],
            "name": "pitboard",
            "version": version,
            "target": target,
            "bin": "pitboard",
            "binary-ext": ".exe" if "windows" in target else "",
        }
        return re.sub(r"\{ ([a-z-]+) \}", lambda m: values[m.group(1)], template)

    def test_windows_installs_the_zip_package_windows_writes(self):
        script = PACKAGE_WINDOWS.read_text()
        for line in (
            '$name = "pitboard-v$Version-$Triple"',
            '$zip = Join-Path $dist "$name.zip"',
            "$entries = 'pitboard.exe',",
        ):
            self.assertIn(line, script)
        version = "1.0.0-beta.1"
        for target in WINDOWS_TARGETS:
            with self.subTest(target=target):
                meta = self.resolved(target)
                name = f"pitboard-v{version}-{target}"
                self.assertEqual(
                    self.expand(meta["pkg-url"], target, version),
                    f"https://github.com/datlechin/pitboard/releases/download/v{version}/{name}.zip",
                )
                self.assertEqual(meta["pkg-fmt"], "zip")
                self.assertEqual(
                    self.expand(meta["bin-dir"], target, version), f"{name}/pitboard.exe"
                )
                self.assertEqual(meta["disabled-strategies"], {"quick-install", "compile"})

    def test_every_other_target_installs_its_tarball(self):
        version = "1.0.0"
        for target in TARBALL_TARGETS:
            with self.subTest(target=target):
                meta = self.resolved(target)
                name = f"pitboard-v{version}-{target}"
                self.assertEqual(
                    self.expand(meta["pkg-url"], target, version),
                    f"https://github.com/datlechin/pitboard/releases/download/v{version}/"
                    f"{name}.tar.gz",
                )
                self.assertEqual(meta["pkg-fmt"], "tgz")
                self.assertEqual(self.expand(meta["bin-dir"], target, version), f"{name}/pitboard")
                self.assertEqual(meta["disabled-strategies"], {"quick-install"})


class BuildNumber(unittest.TestCase):
    def test_numbers_rise_with_the_version(self):
        numbers = {
            "0.14.0": 140099,
            "0.15.0": 150099,
            "0.15.1": 150199,
            "1.0.0-beta.1": 1000001,
            "1.0.0-beta.2": 1000002,
            "1.0.0": 1000099,
            "1.0.1": 1000199,
            "1.1.0-beta.1": 1010001,
            "1.1.0-beta.2": 1010002,
            "1.99.99-beta.98": 1999998,
            "2.0.0": 2000099,
        }
        for version, number in numbers.items():
            with self.subTest(version=version):
                done = run(BUILD_NUMBER, version)
                self.assertEqual(done.returncode, 0, done.stderr)
                self.assertEqual(done.stdout, f"{number}\n")
        ordered = [n for _, n in sorted(numbers.items(), key=lambda kv: kv[1])]
        self.assertEqual(list(numbers.values()), ordered)
        self.assertEqual(len(set(ordered)), len(ordered))
        # 0.14.0 shipped as 1400, by the scheme before this one.
        self.assertGreater(numbers["0.15.0"], 1400)

    def test_a_version_it_cannot_number_is_refused(self):
        for version in (
            "1.100.0",
            "1.0.100",
            "1.0.0-beta.99",
            "1.0.0-beta.0",
            "1.0.0-rc1",
            "01.0.0",
            "1.01.0",
            "1.0",
            "",
            "1.0.0\n2.0.0",
        ):
            with self.subTest(version=version):
                done = run(BUILD_NUMBER, version)
                self.assertEqual(done.returncode, 1)
                self.assertEqual(done.stdout, "")
                self.assertIn(NO_BUILD_NUMBER, done.stderr)

    def test_it_takes_one_argument(self):
        done = run(BUILD_NUMBER)
        self.assertEqual(done.returncode, 2)
        self.assertIn("usage:", done.stderr)


if __name__ == "__main__":
    unittest.main()
