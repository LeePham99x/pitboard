import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { gzipSync } from "node:zlib";
import { closest, elements, parse, walk } from "./html.mjs";

const DIST = new URL("../dist/", import.meta.url).pathname;
const FONTS = new URL("../src/assets/fonts/", import.meta.url).pathname;
const DOCS_RULES = new URL("../../docs/AGENTS.md", import.meta.url).pathname;

const BUDGETS = {
  html: {
    "index.html": 30_000,
    "download.html": 30_000,
    "404.html": 30_000,
    "changelog.html": 80_000,
  },
  css: 20_000,
  js: 10_000,
  fonts: { files: 2, bytes: 90_000, preloads: 2 },
};

// `key` is banned as an adjective only, which a word match cannot tell.
export function bannedFromDocs(markdown) {
  const section = markdown.split(/^## Banned\s*$/m)[1]?.split(/^## /m)[0];
  if (!section) throw new Error("docs/AGENTS.md has no Banned section");
  const out = [];
  for (const line of section.split(/\n(?=- )/)) {
    if (!/^- (Words|Phrases):/.test(line)) continue;
    for (const m of line.matchAll(/`([^`]+)`/g)) if (m[1] !== "key") out.push(m[1]);
  }
  if (out.length < 40) throw new Error("docs/AGENTS.md's Banned section reads short");
  return out;
}

export const SITE_BANNED = [
  "instant",
  "blazing",
  "magical",
  "supercharge",
  "elevate",
  "game-changing",
  "the only",
  "the first",
  "the best",
  "workflow",
  "experience",
  "so you can",
  "so you never",
  "making it",
  "not just",
  "not only",
  "introducing",
  "trusted by",
  "never hit",
  "never lose",
  "official app",
  "unlimited",
  "pool your",
  "one click away",
  "signed in forever",
  "works offline",
  "bank-grade",
];

const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
export function wordPattern(words) {
  const parts = words.map((w) => {
    const head = /^\w/.test(w) ? "\\b" : "";
    const tail = /\w$/.test(w) ? "\\b" : "";
    return `${head}${escape(w).replace(/ /g, "\\s+")}${tail}`;
  });
  return new RegExp(parts.join("|"), "gi");
}

// Program output, the app's own strings and the changelog keep their own words.
const quoted = (el) =>
  ["code", "pre", "kbd", "script", "style"].includes(el.tag) ||
  "data-output" in el.attrs ||
  el.attrs["data-source"] === "changelog";

// CHANGELOG.md quotes a full-width c on purpose, in a look-alike claude.ai address.
const FALLBACK = new Set([0xff43]);

const MONO = (el) => ["code", "pre", "kbd"].includes(el.tag) || "data-output" in el.attrs;

export function checkPage(name, html, rules) {
  const problems = [];
  const fail = (m) => problems.push(`${name}: ${m}`);
  const root = parse(html);
  const all = elements(root);
  const meta = (key, value) => all.find((e) => e.tag === "meta" && e.attrs[key] === value);

  if (html.includes("astro-island")) fail("holds an astro-island");
  const h1 = all.filter((e) => e.tag === "h1");
  if (h1.length !== 1) fail(`has ${h1.length} h1`);
  const title = all.find((e) => e.tag === "title");
  if (!title?.children[0]?.value.trim()) fail("has no title");
  if (!meta("name", "description")?.attrs.content) fail("has no description");
  const noindex = meta("name", "robots")?.attrs.content?.includes("noindex");
  if (!noindex && !all.some((e) => e.tag === "link" && e.attrs.rel === "canonical"))
    fail("has no canonical");
  if (!meta("property", "og:image")?.attrs.content) fail("has no og:image");
  if (!meta("property", "og:image:alt")?.attrs.content) fail("has no og:image:alt");
  if (meta("name", "twitter:card")?.attrs.content !== "summary_large_image")
    fail("has no twitter:card");
  if (!all.some((e) => e.tag === "main" && e.attrs.id === "main"))
    fail("has no main#main for the skip link");
  if (all.some((e) => "style" in e.attrs)) fail("has a style attribute");
  const preloads = all.filter(
    (e) => e.tag === "link" && e.attrs.rel === "preload" && e.attrs.as === "font",
  );
  if (preloads.length > BUDGETS.fonts.preloads) fail(`preloads ${preloads.length} fonts`);

  const ids = new Set(all.map((e) => e.attrs.id).filter(Boolean));
  for (const e of all) {
    for (const key of ["aria-labelledby", "aria-controls", "aria-describedby"]) {
      for (const id of (e.attrs[key] ?? "").split(/\s+/).filter(Boolean)) {
        if (!ids.has(id)) fail(`${key} names #${id}, which is not on the page`);
      }
    }
  }

  const texts = [...walk(root)].filter(
    (n) => n.type === "text" && !closest(n.parent, (e) => ["script", "style"].includes(e.tag)),
  );
  const attrText = all.flatMap((e) =>
    ["alt", "aria-label", "title"].filter((a) => a in e.attrs).map((a) => e.attrs[a]),
  );
  for (const t of [
    ...texts.map((n) => n.value),
    ...attrText,
    meta("name", "description")?.attrs.content ?? "",
  ]) {
    if (/[–—]/.test(t)) fail(`an en or em dash in "${t.trim().slice(0, 60)}"`);
  }

  for (const n of texts) {
    const inside = closest(n.parent, quoted);
    if (inside) continue;
    const t = n.value;
    for (const m of t.matchAll(rules.banned)) fail(`"${m[0]}" in "${t.trim().slice(0, 70)}"`);
    if (/[·●■◆▪•]/.test(t)) fail(`a dot or badge character in "${t.trim().slice(0, 70)}"`);
    if (/\bpitboard\b/.test(t))
      fail(`lower-case pitboard outside code in "${t.trim().slice(0, 70)}"`);
  }

  // outside mono contexts a character needs both faces: headings and labels are mono too
  for (const n of texts) {
    const mono = closest(n.parent, MONO);
    for (const ch of n.value) {
      const cp = ch.codePointAt(0);
      if (cp < 0x20 || ch === " ") continue;
      const ok =
        FALLBACK.has(cp) || (mono ? rules.mono.has(cp) : rules.mono.has(cp) && rules.text.has(cp));
      if (!ok)
        fail(`U+${cp.toString(16).toUpperCase().padStart(4, "0")} "${ch}" is in no committed face`);
    }
  }
  return { problems, root, all };
}

function checkLinks(pages) {
  const problems = [];
  const ids = new Map(
    [...pages].map(([name, p]) => [name, new Set(p.all.map((e) => e.attrs.id).filter(Boolean))]),
  );
  const resolve = (path) => {
    if (path === "/") return "index.html";
    const clean = path.replace(/^\//, "");
    if (existsSync(join(DIST, clean)) && statSync(join(DIST, clean)).isFile()) return clean;
    if (existsSync(join(DIST, `${clean}.html`))) return `${clean}.html`;
    return null;
  };
  for (const [name, p] of pages) {
    for (const e of p.all) {
      const href = e.tag === "a" || e.tag === "link" ? e.attrs.href : null;
      if (!href || /^(https?:|mailto:)/.test(href) || href.startsWith("//")) continue;
      const [path, frag] = href.split("#");
      const target = path ? resolve(path) : name;
      if (!target) {
        problems.push(`${name}: ${href} goes to no file`);
        continue;
      }
      if (frag && target.endsWith(".html") && !ids.get(target)?.has(decodeURIComponent(frag))) {
        problems.push(`${name}: ${href} names an anchor ${target} does not have`);
      }
    }
  }
  return problems;
}

export function checkVersions(pages) {
  const problems = [];
  const seen = new Set();
  for (const [, p] of pages) {
    for (const e of p.all) {
      const m = /\/releases\/download\/v(\d+\.\d+\.\d+)\//.exec(e.attrs.href ?? "");
      if (m) seen.add(m[1]);
    }
  }
  // a published beta goes above the release /download offers
  const newest = pages
    .get("changelog.html")
    ?.all.find((e) => e.tag === "article" && /^v\d+\.\d+\.\d+$/.test(e.attrs.id ?? ""))
    ?.attrs.id?.slice(1);
  if (seen.size !== 1)
    problems.push(`download links name ${seen.size} versions: ${[...seen].join(", ")}`);
  if (newest && !seen.has(newest))
    problems.push(`/changelog starts at ${newest}, the downloads at ${[...seen]}`);
  return problems;
}

function gz(file) {
  return gzipSync(readFileSync(file)).length;
}

function main() {
  const problems = [];
  const files = readdirSync(DIST, { recursive: true }).map(String);

  if (files.some((f) => f.split("/").pop() === "install.sh"))
    problems.push("dist holds an install.sh");
  for (const f of ["_redirects", "_headers"]) {
    const p = join(DIST, f);
    if (!existsSync(p)) continue;
    const text = readFileSync(p, "utf8");
    if (text.includes("install.sh"))
      problems.push(`${f} names install.sh, which a zone rule serves`);
    if (f === "_redirects" && /^\/\*\s/m.test(text))
      problems.push("_redirects sends every path somewhere");
  }
  if (!existsSync(join(DIST, "_headers"))) problems.push("dist has no _headers: run csp.mjs first");
  const wrangler = readFileSync(new URL("../wrangler.jsonc", import.meta.url), "utf8");
  for (const key of ["single-page-application", '"routes"', '"route"', '"custom_domain"']) {
    if (wrangler.includes(key)) problems.push(`wrangler.jsonc names ${key}`);
  }
  if (!wrangler.includes('"previews"')) problems.push("wrangler.jsonc has no previews block");
  if (!existsSync(join(DIST, "404.html"))) problems.push("dist has no 404.html");
  const sitemaps = files.filter((f) => /^sitemap.*\.xml$/.test(f));
  if (!sitemaps.length) problems.push("dist has no sitemap");
  for (const s of sitemaps) {
    if (/404/.test(readFileSync(join(DIST, s), "utf8"))) problems.push(`${s} lists the 404 page`);
  }

  const astro = files.filter((f) => f.startsWith("_astro/"));
  const sum = (ext) =>
    astro.filter((f) => f.endsWith(ext)).reduce((n, f) => n + gz(join(DIST, f)), 0);
  const css = sum(".css");
  const js = sum(".js");
  const fonts = astro.filter((f) => f.endsWith(".woff2"));
  const fontBytes = fonts.reduce((n, f) => n + statSync(join(DIST, f)).size, 0);
  if (css > BUDGETS.css) problems.push(`CSS is ${css} bytes gzipped, over ${BUDGETS.css}`);
  if (js > BUDGETS.js) problems.push(`JS is ${js} bytes gzipped, over ${BUDGETS.js}`);
  if (fonts.length > BUDGETS.fonts.files) problems.push(`${fonts.length} font files`);
  if (fontBytes > BUDGETS.fonts.bytes)
    problems.push(`fonts are ${fontBytes} bytes, over ${BUDGETS.fonts.bytes}`);

  const face = (name) =>
    new Set(JSON.parse(readFileSync(join(FONTS, `${name}.codepoints.json`), "utf8")));
  const rules = {
    banned: wordPattern([...bannedFromDocs(readFileSync(DOCS_RULES, "utf8")), ...SITE_BANNED]),
    text: face("Inter-4.1-opsz-wght"),
    mono: face("JetBrainsMono-2.304-wght"),
  };

  const pages = new Map();
  const report = [];
  for (const f of files.filter((f) => f.endsWith(".html"))) {
    const html = readFileSync(join(DIST, f), "utf8");
    const size = gzipSync(html).length;
    const budget = BUDGETS.html[f];
    if (budget === undefined) problems.push(`${f} has no HTML budget`);
    else if (size > budget) problems.push(`${f} is ${size} bytes gzipped, over ${budget}`);
    const page = checkPage(f, html, rules);
    problems.push(...page.problems);
    pages.set(f, page);
    report.push(`${f} ${size}`);
  }
  problems.push(...checkLinks(pages), ...checkVersions(pages));

  console.log(
    `guard: html gzipped ${report.join(", ")}; css ${css}; js ${js}; fonts ${fontBytes} in ${fonts.length}`,
  );
  if (problems.length) {
    console.error(problems.map((p) => `guard: ${p}`).join("\n"));
    process.exit(1);
  }
}

if (import.meta.url === `file://${process.argv[1]}`) main();
