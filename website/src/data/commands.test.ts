import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { attestCommand, commands, windowsWays } from "./commands.ts";

const repo = (path: string) => readFileSync(new URL(`../../../${path}`, import.meta.url), "utf8");
const pages = ["install", "install/script", "install/other-ways", "install/verify"]
  .map((page) => repo(`docs/${page}.mdx`))
  .join("\n");

test("each install command is one the install pages give", () => {
  for (const [name, command] of Object.entries(commands)) {
    assert.ok(pages.includes(command.copy), `${name}: ${command.copy}`);
  }
});

test("an attestation check is the one Verify a download gives", () => {
  const shown = [...pages.matchAll(/^(gh attestation verify (\S+) --repo \S+)/gm)];
  assert.ok(shown.length > 0, "Verify a download checks an attestation");
  for (const [, line, file] of shown) assert.equal(attestCommand(file).copy, line);
});

test("llms.txt installs with the install page's script", () => {
  const llms = repo("website/public/llms.txt");
  const shown = /```sh\n(.+)\n```/.exec(llms)?.[1];
  assert.equal(shown, commands.installScript.copy);
});

test("compares Windows checksums with Verify a download's PowerShell line", () => {
  const shown = /^Select-String .+$/m.exec(pages)?.[0];
  assert.ok(shown, "Verify a download compares checksums in PowerShell");
  assert.equal(commands.selectString?.copy, shown);
});

// GitHub's release of each kind, as the site reads it
const beta = { version: "1.0.0-beta.1", prerelease: true };
const full = { version: "1.0.0", prerelease: false };

test("names the version Windows downloads to Cargo, as the release checks it, until a full release", () => {
  const checked = /'cargo-binstall\.exe'\) "(pitboard@)\$env:VERSION"/.exec(
    repo(".github/workflows/release.yml"),
  );
  assert.ok(checked, "release.yml installs the release with cargo-binstall");
  const { binstall, cargoInstall } = windowsWays(beta);
  assert.equal(binstall.copy, `cargo binstall ${checked[1]}1.0.0-beta.1`);
  assert.equal(cargoInstall.copy, `cargo install --locked ${checked[1]}1.0.0-beta.1`);
  assert.equal(windowsWays(full).binstall.copy, commands.binstall.copy);
  assert.equal(windowsWays(full).cargoInstall.copy, commands.cargoInstall.copy);
});

test("offers Scoop with the bucket's own lines, from the first full release it holds", () => {
  const bucket = /```powershell\n([^`]+)```/.exec(repo("packaging/scoop/README.md"))?.[1];
  assert.ok(bucket, "the bucket's README gives its install lines");
  assert.equal(windowsWays(beta).scoop, null);
  assert.deepEqual(
    windowsWays(full).scoop?.map((c) => c.copy),
    bucket.trim().split("\n"),
  );
});
