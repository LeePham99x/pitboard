// Usage: bun os-homedir.mjs <profile>   (and: node os-homedir.mjs <profile>)

import os from "node:os";

// Given, not read: Bun's os.userInfo().homedir follows USERPROFILE.
const profile = process.argv[2];
if (!profile) {
  console.error("usage: os-homedir.mjs <the account's profile folder>");
  process.exit(2);
}
const runtime = typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`;
const info = os.userInfo();
const user = info.username;

function redact(value) {
  if (value == null) return null;
  let v = String(value);
  const lower = v.toLowerCase();
  const p = profile.toLowerCase();
  let out = "";
  let i = 0;
  while (i < v.length) {
    const end = i + p.length;
    const boundary = end === v.length || v[end] === "\\" || v[end] === "/";
    if (p.length > 0 && lower.startsWith(p, i) && boundary) {
      out += "<profile>";
      i = end;
    } else {
      out += v[i];
      i += 1;
    }
  }
  return out
    .split(/([\\/])/)
    .map((part) => (part.toLowerCase() === user.toLowerCase() ? "<user>" : part))
    .join("");
}

const home = os.homedir();
console.log(
  JSON.stringify(
    {
      runtime,
      os_homedir: redact(home),
      os_homedir_is_the_account_profile: home.toLowerCase() === profile.toLowerCase(),
      os_homedir_follows_USERPROFILE: home === process.env.USERPROFILE,
      userinfo_homedir_is_the_account_profile: info.homedir.toLowerCase() === profile.toLowerCase(),
      userinfo_homedir_follows_USERPROFILE: info.homedir === process.env.USERPROFILE,
      os_homedir_is_nfc: home === home.normalize("NFC"),
      env: Object.fromEntries(
        [
          "USERPROFILE",
          "HOME",
          "HOMEDRIVE",
          "HOMEPATH",
          "APPDATA",
          "LOCALAPPDATA",
          "CODEX_HOME",
          "CLAUDE_CONFIG_DIR",
          "PITBOARD_HOME",
        ].map((k) => [k, redact(process.env[k])]),
      ),
    },
    null,
    2,
  ),
);
