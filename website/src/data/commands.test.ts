import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { attestCommand, commands } from "./commands.ts";

const repo = (path: string) => readFileSync(new URL(`../../../${path}`, import.meta.url), "utf8");
const pages = ["install", "install/other-ways", "install/verify"]
  .map((page) => repo(`docs/${page}.mdx`))
  .join("\n");

test("each install command is one the install pages give", () => {
  for (const [name, command] of Object.entries(commands)) {
    assert.ok(pages.includes(command.copy), `${name}: ${command.copy}`);
  }
});

test("an attestation check is the one Verify a download gives", () => {
  const shown = [...pages.matchAll(/^(gh attestation verify (\S+).*?)(?: \\)?$/gm)];
  assert.ok(shown.length > 0, "Verify a download checks an attestation");
  for (const [, line, file] of shown) assert.equal(attestCommand(file).copy, line);
});

test("llms.txt installs with the install page's script", () => {
  const llms = repo("website/public/llms.txt");
  const shown = /```sh\n(.+)\n```/.exec(llms)?.[1];
  assert.equal(shown, commands.installScript.copy);
});
