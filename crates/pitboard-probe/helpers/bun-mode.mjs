// C1 from Bun's side: a login file made the way Claude Code 2.1.300's plaintext store makes
// `.credentials.json`: the folder with `mkdir -p` and no mode, the file written to a
// temporary one with mode 0o600 and renamed over, then `chmod 0o600`. What that leaves in the
// file's DACL is read afterwards with `pitboard-probe acl --describe`.
//
// Usage: bun bun-mode.mjs <scratch>
//
// It makes only `<scratch>\pitboard-probe-bunmode\` and the file in it, writes a dummy, and
// prints the modes Bun reports.

import { chmodSync, mkdirSync, renameSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const scratch = process.argv[2];
if (!scratch) {
  console.error("usage: bun-mode.mjs <scratch>");
  process.exit(2);
}
const dir = join(scratch, "pitboard-probe-bunmode");
const file = join(dir, "pitboard-probe-bunmode.json");
const tmp = `${file}.tmp`;
const mode = (p) => statSync(p).mode & 0o777;
const runtime = typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`;

const steps = {};
const step = (name, f) => {
  try {
    f();
    steps[name] = "ok";
  } catch (e) {
    steps[name] = String(e?.code ?? e?.message);
  }
};
step("mkdir", () => mkdirSync(dir, { recursive: true }));
step("write_tmp", () => writeFileSync(tmp, '{"dummy":true}', { mode: 0o600, flag: "wx" }));
step("rename", () => renameSync(tmp, file));
let afterWrite = null;
step("stat_after_write", () => {
  afterWrite = mode(file);
});
step("chmod", () => chmodSync(file, 0o600));
let afterChmod = null;
step("stat_after_chmod", () => {
  afterChmod = mode(file);
});

console.log(
  JSON.stringify({
    runtime,
    steps,
    dir_mode: (() => {
      try {
        return mode(dir).toString(8);
      } catch {
        return null;
      }
    })(),
    file_mode_after_write: afterWrite?.toString(8) ?? null,
    file_mode_after_chmod: afterChmod?.toString(8) ?? null,
  }),
);
