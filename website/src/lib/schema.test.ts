import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { application } from "./schema.ts";

const repo = (path: string) => readFileSync(new URL(`../../../${path}`, import.meta.url), "utf8");
const site = new URL("https://usepitboard.com");
const from = (macos: string, windows: string) => ({
  macos: { release: { version: macos } },
  windows: { release: { version: windows } },
  linux: { release: { version: macos } },
});

test("names each system Pitboard runs on, from the oldest version it supports", () => {
  assert.ok(repo("apps/macos/project.yml").includes('MACOSX_DEPLOYMENT_TARGET: "14.0"'));
  assert.ok(
    repo("crates/pitboard-core/src/host/mod.rs").includes(
      "// Windows 11 24H2's build, which Windows Server 2025 shares.",
    ),
  );
  assert.equal(
    application(site, from("0.15.0", "1.0.0-beta.1")).operatingSystem,
    "macOS 14 or later, Windows 11 24H2 or later, Linux",
  );
});

test("sends a search engine's reader to the download page, which offers every system", () => {
  const app = application(site, from("0.15.0", "1.0.0-beta.1"));
  assert.equal(app.downloadUrl, "https://usepitboard.com/download");
  assert.equal(app.url, "https://usepitboard.com/");
});

test("gives a version only while every system downloads the same release", () => {
  assert.equal("softwareVersion" in application(site, from("0.15.0", "1.0.0-beta.1")), false);
  assert.equal(application(site, from("1.0.0", "1.0.0")).softwareVersion, "1.0.0");
});
