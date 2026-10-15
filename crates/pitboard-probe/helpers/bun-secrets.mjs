// G3 from Bun's side: Credential Manager items written through `Bun.secrets` the way Claude
// Code 2.1.300's store writes its login, with no `persist` and no `allowUnrestrictedAccess`:
// the item itself, a piece `#0` of 2400 characters and the manifest `#m` that names the
// pieces. The service is `pitboard-citest-bun`, never a tool's, and every value is a dummy.
// `pitboard-probe credman-item` reads each item's attributes and deletes it; `delete` here
// removes whatever is left.
//
// Usage: bun bun-secrets.mjs <set|delete>
//
// It prints lengths and booleans, never a value.

const service = "pitboard-citest-bun";
const names = ["claude-code-user", "claude-code-user#0", "claude-code-user#m"];
const piece = "A".repeat(2400);
const values = {
  "claude-code-user": '{"dummy":"pitboard-probe"}',
  "claude-code-user#0": piece,
  "claude-code-user#m": JSON.stringify({ n: 1, l: piece.length }),
};

const action = process.argv[2];
if (typeof Bun === "undefined" || !Bun.secrets || (action !== "set" && action !== "delete")) {
  console.error("usage: bun bun-secrets.mjs <set|delete>, under Bun");
  process.exit(2);
}

const out = { runtime: `bun ${Bun.version}`, action, items: {} };
for (const name of names) {
  try {
    if (action === "set") {
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
