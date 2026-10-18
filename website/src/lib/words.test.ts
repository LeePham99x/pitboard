import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { toolsOn } from "./words.ts";

const provider = readFileSync(
  new URL("../../../crates/pitboard-core/src/provider/mod.rs", import.meta.url),
  "utf8",
);

test("each system lists the tools Pitboard supports on it, Claude Desktop on macOS alone", () => {
  assert.ok(provider.includes("(ProviderId::Claude | ProviderId::Codex, _) => true,"));
  assert.ok(provider.includes("(ProviderId::Desktop, Os::MacOs) => true,"));
  assert.ok(provider.includes("(ProviderId::Desktop, Os::Linux | Os::Windows) => false,"));
  const listed = (p: Parameters<typeof toolsOn>[0]) => toolsOn(p).map(([tool]) => tool);
  assert.deepEqual(listed("macos"), ["claude", "codex", "desktop"]);
  assert.deepEqual(listed("windows"), ["claude", "codex"]);
  assert.deepEqual(listed("linux"), ["claude", "codex"]);
});
