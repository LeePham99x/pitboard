// Usage: bun bun-secrets.mjs <set|delete>

const service = "pitboard-citest-bun";
const names = ["claude-code-user", "claude-code-user#0", "claude-code-user#m"];
const piece = "A".repeat(2400);
const values = {
  // Not ASCII, so the blob tells UTF-8 from UTF-16 and from an ANSI code page.
  "claude-code-user": '{"dummy":"pitboard-probe-\u0110\u1ea1t"}',
  "claude-code-user#0": piece,
  "claude-code-user#m": JSON.stringify({ n: 1, l: piece.length }),
};

if (process.env.GITHUB_ACTIONS !== "true" || process.env.RUNNER_ENVIRONMENT !== "github-hosted") {
  console.error("bun-secrets.mjs writes Credential Manager items, so it runs only on a GitHub-hosted runner");
  process.exit(2);
}
const action = process.argv[2];
if (typeof Bun === "undefined" || !Bun.secrets || (action !== "set" && action !== "delete")) {
  console.error("usage: bun bun-secrets.mjs <set|delete>, under Bun");
  process.exit(2);
}

const out = { runtime: `bun ${Bun.version}`, action, items: {} };
for (const name of names) {
  try {
    if (action === "set") {
      // No persist and no allowUnrestrictedAccess, as Claude Code 2.1.300 calls it.
      await Bun.secrets.set({ service, name, value: values[name] });
      const read = await Bun.secrets.get({ service, name });
      out.items[name] = { set: true, read_back_length: read?.length ?? null, matches: read === values[name] };
    } else {
      out.items[name] = { deleted: await Bun.secrets.delete({ service, name }) };
    }
  } catch (e) {
    out.items[name] = { error: String(e?.code ?? e?.name ?? "error") };
  }
}
console.log(JSON.stringify(out));
