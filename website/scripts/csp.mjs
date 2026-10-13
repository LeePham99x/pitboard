// Report-only until a Preview shows a clean console; PITBOARD_CSP_ENFORCE=1 enforces it.
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { elements, parse } from "./html.mjs";

const DIST = new URL("../dist/", import.meta.url).pathname;
const BEACON = "https://static.cloudflareinsights.com/";

function htmlFiles(dir) {
  return readdirSync(dir, { recursive: true })
    .filter((f) => f.endsWith(".html"))
    .map((f) => join(dir, f));
}

const sha = (text) => `'sha256-${createHash("sha256").update(text, "utf8").digest("base64")}'`;

export function policy(pages) {
  const scripts = new Set();
  const styles = new Set();
  let beacon = false;
  const problems = [];
  for (const [file, html] of pages) {
    const root = parse(html);
    for (const el of elements(root)) {
      if (el.tag === "script") {
        const src = el.attrs.src;
        if (src) {
          if (src.startsWith(BEACON)) beacon = true;
          else if (/^(https?:)?\/\//.test(src)) problems.push(`${file}: a script from ${src}`);
        } else if (el.attrs.type !== "application/ld+json") {
          scripts.add(sha(el.children[0]?.raw ?? ""));
        }
      }
      if (el.tag === "style") styles.add(sha(el.children[0]?.raw ?? ""));
    }
  }
  const directives = [
    "default-src 'self'",
    ["script-src 'self'", ...scripts, ...(beacon ? [BEACON.slice(0, -1)] : [])].join(" "),
    ["style-src 'self'", ...styles].join(" "),
    "img-src 'self'",
    "font-src 'self'",
    ["connect-src 'self'", ...(beacon ? ["https://cloudflareinsights.com"] : [])].join(" "),
    "media-src 'self'",
    "manifest-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'none'",
    "frame-ancestors 'none'",
    "upgrade-insecure-requests",
  ];
  return { csp: directives.join("; "), problems };
}

export function headers(csp, enforce) {
  const name = enforce ? "Content-Security-Policy" : "Content-Security-Policy-Report-Only";
  return `/*
  ${name}: ${csp}
  Strict-Transport-Security: max-age=31536000
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
  Cross-Origin-Opener-Policy: same-origin

/_astro/*
  Cache-Control: public, max-age=31536000, immutable
`;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const pages = htmlFiles(DIST).map((f) => [relative(DIST, f), readFileSync(f, "utf8")]);
  const { csp, problems } = policy(pages);
  if (problems.length) {
    console.error(problems.join("\n"));
    process.exit(1);
  }
  const enforce = process.env.PITBOARD_CSP_ENFORCE === "1";
  writeFileSync(join(DIST, "_headers"), headers(csp, enforce));
  console.log(`csp: ${enforce ? "enforced" : "report only"}, ${pages.length} pages`);
}
