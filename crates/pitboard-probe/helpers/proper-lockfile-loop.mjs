// proper-lockfile's side of C5, with the options Claude Code 2.1.300 passes for its write
// lock (stale 15000 ms, so a heartbeat every 7500 ms, ten retries, 100 ms to 1000 ms of
// backoff) or for its refresh lock (stale 60000 ms, a heartbeat every 5000 ms). Run it
// against the target `pitboard-probe lock` uses, so each side can see the other's lock:
//
//   hold:  take the lock and keep it for <seconds>, then say whether it was ever reported
//          compromised. Run `pitboard-probe lock --mode check` meanwhile.
//   check: for <seconds>, once a second, ask whether the lock is held and try to take it
//          without retrying. Run `pitboard-probe lock --mode hold` meanwhile.
//
// Usage: bun proper-lockfile-loop.mjs <scratch> <hold|check> [seconds] [write|refresh]
//
// It touches only `<scratch>\pitboard-probe-locktarget` and the lock beside it, and prints
// counts and error codes only.

import { existsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import lockfile from "proper-lockfile";

const scratch = process.argv[2];
const mode = process.argv[3];
const seconds = Number(process.argv[4] ?? "30");
const timing = process.argv[5] ?? "write";
const timings = {
  write: {
    stale: 15000,
    retries: { retries: 10, minTimeout: 100, maxTimeout: 1000 },
    realpath: false,
  },
  refresh: { stale: 60000, update: 5000, realpath: false },
};
if (!scratch || (mode !== "hold" && mode !== "check") || !(timing in timings)) {
  console.error("usage: proper-lockfile-loop.mjs <scratch> <hold|check> [seconds] [write|refresh]");
  process.exit(2);
}
const target = join(scratch, "pitboard-probe-locktarget");
if (!existsSync(target)) writeFileSync(target, "x");

const options = timings[timing];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const runtime = typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`;

if (mode === "hold") {
  let compromised = null;
  try {
    const release = await lockfile.lock(target, {
      ...options,
      onCompromised: (err) => {
        compromised = String(err?.code ?? err?.message);
      },
    });
    await sleep(seconds * 1000);
    await release();
    console.log(JSON.stringify({ runtime, mode, timing, acquired: true, compromised }));
  } catch (err) {
    console.log(JSON.stringify({ runtime, mode, timing, acquired: false, code: String(err?.code) }));
    process.exit(1);
  }
} else {
  const counts = { held: 0, free: 0, locked_refusals: 0, other_errors: {} };
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    const held = await lockfile.check(target, { stale: options.stale, realpath: false });
    counts[held ? "held" : "free"] += 1;
    try {
      const release = await lockfile.lock(target, { stale: options.stale, retries: 0, realpath: false });
      await release();
    } catch (err) {
      if (err?.code === "ELOCKED") counts.locked_refusals += 1;
      else counts.other_errors[String(err?.code)] = (counts.other_errors[String(err?.code)] ?? 0) + 1;
    }
    await sleep(1000);
  }
  console.log(JSON.stringify({ runtime, mode, timing, ...counts }));
}
