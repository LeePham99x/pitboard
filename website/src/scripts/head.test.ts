import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

const script = readFileSync(new URL("./head.js", import.meta.url), "utf8");

const MAC =
  "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
const WINDOWS =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";
const LINUX = "Mozilla/5.0 (X11; Linux x86_64; rv:143.0) Gecko/20100101 Firefox/143.0";
const CHROMEOS =
  "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
const IPHONE =
  "Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1";
const ANDROID =
  "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36";

interface Visit {
  ua: string;
  touch?: number;
  hint?: string;
  // null when the browser refuses storage
  stored?: Record<string, string> | null;
  hash?: string;
}

// runs head.js against a stand-in page and returns the attributes it set on the root
function visit({ ua, touch = 0, hint, stored = {}, hash = "" }: Visit) {
  const attrs: Record<string, string> = {};
  runInNewContext(script, {
    document: {
      documentElement: {
        setAttribute: (k: string, v: string) => (attrs[k] = v),
        classList: { add: (c: string) => (attrs.class = c) },
      },
      querySelectorAll: () => [],
    },
    location: { hash },
    navigator: {
      userAgent: ua,
      maxTouchPoints: touch,
      userAgentData: hint ? { platform: hint } : undefined,
    },
    localStorage: {
      getItem: (k: string) => {
        if (stored === null) throw new Error("storage is blocked");
        return stored[k] ?? null;
      },
    },
    matchMedia: () => ({ matches: true }),
  });
  return attrs;
}

test("shows each system its own platform on a first visit, and macOS to one it cannot tell", () => {
  assert.equal(visit({ ua: MAC })["data-platform"], "macos");
  assert.equal(visit({ ua: WINDOWS })["data-platform"], "windows");
  assert.equal(visit({ ua: LINUX })["data-platform"], "linux");
  assert.equal(visit({ ua: CHROMEOS })["data-platform"], "linux");
  assert.equal(visit({ ua: "Mozilla/5.0", hint: "Windows" })["data-platform"], "windows");
  assert.equal(visit({ ua: "Mozilla/5.0" })["data-platform"], "macos");
});

test("shows a phone or a tablet macOS, and marks it as one", () => {
  for (const v of [{ ua: IPHONE }, { ua: ANDROID }, { ua: MAC, touch: 5 }]) {
    const attrs = visit(v);
    assert.equal(attrs["data-platform"], "macos");
    assert.equal(attrs["data-os"], "mobile");
  }
  assert.equal(visit({ ua: WINDOWS })["data-os"], undefined);
  assert.equal(visit({ ua: LINUX })["data-os"], undefined);
});

test("keeps the platform a reader chose, and detects one past a value it does not know", () => {
  assert.equal(visit({ ua: MAC, stored: { platform: "windows" } })["data-platform"], "windows");
  assert.equal(visit({ ua: WINDOWS, stored: { platform: "linux" } })["data-platform"], "linux");
  assert.equal(visit({ ua: WINDOWS, stored: { platform: "beos" } })["data-platform"], "windows");
  assert.equal(visit({ ua: LINUX, stored: null })["data-platform"], "linux");
});

test("opens the command line for Linux until the reader chooses a surface", () => {
  assert.equal(visit({ ua: LINUX })["data-surface"], "cli");
  assert.equal(visit({ ua: MAC, stored: { platform: "linux" } })["data-surface"], "cli");
  assert.equal(visit({ ua: LINUX, stored: { tab: "app" } })["data-surface"], "app");
  assert.equal(visit({ ua: WINDOWS })["data-surface"], "app");
  assert.equal(visit({ ua: MAC, stored: { tab: "cli" } })["data-surface"], "cli");
});

test("shows the platform a link names for the visit, over the stored one and the detected one", () => {
  assert.equal(visit({ ua: MAC, hash: "#windows" })["data-platform"], "windows");
  assert.equal(visit({ ua: WINDOWS, hash: "#linux" })["data-platform"], "linux");
  assert.equal(
    visit({ ua: LINUX, stored: { platform: "windows" }, hash: "#macos" })["data-platform"],
    "macos",
  );
  assert.equal(visit({ ua: WINDOWS, hash: "#release-files" })["data-platform"], "windows");
  assert.equal(visit({ ua: MAC, stored: null, hash: "#linux" })["data-platform"], "linux");
});
