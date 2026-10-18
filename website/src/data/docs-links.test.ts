import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { DOCS } from "./site.ts";

const root = new URL("../../../", import.meta.url);
const read = (path: string) => readFileSync(new URL(path, root), "utf8");

const SOURCES = [
  "website/src",
  "website/public",
  "README.md",
  "SECURITY.md",
  "CHANGELOG.md",
  "CONTRIBUTING.md",
  "ARCHITECTURE.md",
  "packaging",
  "apps",
  "crates",
];

// The id Mintlify gives a heading: slugify in @mintlify/common 1.0.1159, less the punctuation
// cleanHeadingId drops from a slug or a link.
const bare = (id: string) => decodeURIComponent(id).replace(/[?,;:!'"()[\]{}]/g, "");

function slug(heading: string): string {
  const id = encodeURIComponent(heading.toLowerCase().trim().replace(/\s+/g, "-"));
  const drop = /%[0-9A-F]{2}/.test(id) ? /[^a-zA-Z\d%_]+/g : /[^a-z\d_]+/g;
  return bare(
    id
      .replace(drop, "-")
      .replace(/([a-zA-Z\d]+)-([ts])(-|$)/g, "$1$2$3")
      .replace(/-{2,}/g, "-")
      .replace(/^-|-$/g, ""),
  );
}

function anchors(mdx: string): Set<string> {
  const count = new Map<string, number>();
  const out = new Set<string>();
  let fenced = false;
  for (const line of mdx.split("\n")) {
    if (/^\s*```/.test(line)) fenced = !fenced;
    const heading = !fenced && /^#{1,4}\s+(.+?)\s*$/.exec(line);
    if (!heading) continue;
    const text = heading[1].replace(/`([^`]*)`/g, "$1").replace(/\[([^\]]*)\]\([^)]*\)/g, "$1");
    const s = slug(text);
    const n = (count.get(s) ?? 0) + 1;
    count.set(s, n);
    out.add(n === 1 ? s : `${s}-${n}`);
  }
  return out;
}

const redirects = new Map<string, string>(
  (JSON.parse(read("docs/docs.json")).redirects ?? []).map(
    (r: { source: string; destination: string }) => [r.source, r.destination],
  ),
);

function page(path: string): string | null {
  const to = redirects.get(path) ?? path;
  const name = to.replace(/^\/|\/$/g, "") || "index";
  for (const file of [`docs/${name}.mdx`, `docs/${name}/index.mdx`]) {
    if (existsSync(new URL(file, root))) return file;
  }
  return null;
}

interface Link {
  file: string;
  url: string;
  path: string;
  id: string | null;
}

function links(): Link[] {
  const files = execFileSync("git", ["ls-files", "-z", "--", ...SOURCES], {
    cwd: root,
    encoding: "utf8",
  })
    .split("\0")
    .filter(Boolean);
  const out: Link[] = [];
  for (const file of files) {
    const text = read(file);
    if (text.includes("\0")) continue;
    const resolved = text.replaceAll("${DOCS}", DOCS);
    for (const m of resolved.matchAll(
      /https:\/\/docs\.usepitboard\.com(\/[\w./-]*)?(?:#([\w%-]+))?(\$\{)?/g,
    )) {
      // a path a template finishes, such as `${DOCS}/guides/${guide[tool]}`
      if (m[3]) continue;
      const path = (m[1] ?? "/").replace(/\.+$/, "");
      // a file Mintlify serves beside the pages, such as /llms.txt
      if (/\.\w+$/.test(path)) continue;
      out.push({ file, url: m[0].replace(/\.+$/, ""), path, id: m[2] ?? null });
    }
  }
  return out;
}

test("the slug is Mintlify's for the headings the docs write", () => {
  assert.equal(slug("Put the folder on your PATH"), "put-the-folder-on-your-path");
  assert.equal(slug("Upgrade from 0.3.0 or earlier"), "upgrade-from-0-3-0-or-earlier");
  assert.equal(slug("pitboard uninstall"), "pitboard-uninstall");
  assert.equal(slug("PITBOARD_HOME"), "pitboard_home");
  assert.equal(
    slug("Why Codex logins are moved, not copied"),
    "why-codex-logins-are-moved-not-copied",
  );
  assert.deepEqual(
    [...anchors("## Install\n\n```sh\n# a comment\n```\n\n### `uninstall`\n\n## Install\n")],
    ["install", "uninstall", "install-2"],
  );
});

test("every docs link outside docs/ goes to a page, and to a heading on it", () => {
  const found = links();
  assert.ok(
    found.some((l) => l.id),
    "some link names a heading",
  );
  const problems: string[] = [];
  for (const link of found) {
    const file = page(link.path);
    if (!file) problems.push(`${link.file}: ${link.url} goes to no page`);
    else if (link.id && !anchors(read(file)).has(bare(link.id)))
      problems.push(`${link.file}: ${link.url} names no heading of ${file}`);
  }
  assert.deepEqual(problems, []);
});
