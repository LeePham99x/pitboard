import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import {
  fileRows,
  formatSize,
  parseSums,
  readPublished,
  readRelease,
  readStars,
  type Release,
} from "./github.ts";

const recorded = JSON.parse(
  readFileSync(new URL("../data/fixtures/release-latest.json", import.meta.url), "utf8"),
);
const sums = readFileSync(new URL("../data/fixtures/SHA256SUMS", import.meta.url), "utf8");

const API = "https://api.github.com/repos/datlechin/pitboard";
const WEB = "https://github.com/datlechin/pitboard";
const SUMS_URL = `${WEB}/releases/download/v0.12.0/SHA256SUMS`;

type Route = (init: RequestInit) => Response | Promise<Response>;

// A fetch that answers only the URLs a test names and records what was asked.
function server(routes: Record<string, Route>) {
  const asked: { url: string; method: string; auth: string | null }[] = [];
  const f = (async (input: string | URL | Request, init: RequestInit = {}) => {
    const url = String(input);
    const headers = new Headers(init.headers);
    asked.push({ url, method: init.method ?? "GET", auth: headers.get("authorization") });
    const route = routes[`${init.method ?? "GET"} ${url}`];
    if (!route) throw new TypeError(`fetch failed: ${url}`);
    return route(init);
  }) as typeof fetch;
  return { f, asked };
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", ...headers },
  });

test("reads the latest release from the API, with digests from SHA256SUMS", async () => {
  const { f, asked } = server({
    [`GET ${API}/releases/latest`]: () => json(recorded, 200, { "x-ratelimit-remaining": "4999" }),
    [`GET ${SUMS_URL}`]: () => new Response(sums),
  });
  const lines: string[] = [];
  const r = await readRelease({ fetch: f, token: "t0ken", log: (l) => lines.push(l) });
  assert.equal(r.version, "0.12.0");
  assert.equal(r.source, "api");
  assert.equal(r.assets.length, 13);
  const zip = r.assets.find((a) => a.name === "Pitboard-v0.12.0-macos.zip");
  assert.deepEqual(zip, {
    name: "Pitboard-v0.12.0-macos.zip",
    size: 13801164,
    sha256: "03e9a7864bea72b6f04a951ff8e0b918c70503645760f9c2f766ea8fc3bb7465",
    url: "https://github.com/datlechin/pitboard/releases/download/v0.12.0/Pitboard-v0.12.0-macos.zip",
  });
  assert.equal(asked[0].auth, "Bearer t0ken");
  // the token goes to the API only, never to a download
  assert.equal(asked[1].auth, null);
  assert.ok(lines.includes("release: token set: yes"));
  assert.ok(!lines.some((l) => l.includes("t0ken")));
});

test("stops when SHA256SUMS and GitHub's digest disagree", async () => {
  const tampered = sums.replace(/^03e9a7864bea/m, "ffffffffffff");
  const { f } = server({
    [`GET ${API}/releases/latest`]: () => json(recorded),
    [`GET ${SUMS_URL}`]: () => new Response(tampered),
  });
  await assert.rejects(readRelease({ fetch: f }), /disagree on Pitboard-v0\.12\.0-macos\.zip/);
});

test("stops on a 401 when a token is set, rather than build without it", async () => {
  const { f } = server({
    [`GET ${API}/releases/latest`]: () => json({ message: "Bad credentials" }, 401),
  });
  await assert.rejects(readRelease({ fetch: f, token: "old" }), /refused the token/);
});

test("falls back to the release pages on a rate limit, and keeps no signed URL", async () => {
  const signed =
    "https://release-assets.githubusercontent.com/github-production-release-asset/1?sig=secret";
  const routes: Record<string, Route> = {
    [`GET ${API}/releases/latest`]: () => json({ message: "API rate limit exceeded" }, 403),
    [`HEAD ${WEB}/releases/latest`]: () =>
      new Response(null, { status: 302, headers: { location: `${WEB}/releases/tag/v0.12.0` } }),
    [`GET ${SUMS_URL}`]: () => new Response(sums),
  };
  for (const a of recorded.assets as { name: string; size: number }[]) {
    // fetch follows the redirect to the signed URL, so the answer is the file's own
    routes[`HEAD ${WEB}/releases/download/v0.12.0/${a.name}`] = () =>
      new Response(null, {
        status: 200,
        headers: { "content-length": String(a.size), "x-final": signed },
      });
  }
  const { f } = server(routes);
  const lines: string[] = [];
  const r = await readRelease({ fetch: f, log: (l) => lines.push(l) });
  assert.equal(r.source, "fallback");
  assert.equal(r.version, "0.12.0");
  assert.deepEqual(
    r.assets.map((a) => a.name).sort(),
    [...parseSums(sums).keys(), "SHA256SUMS"].sort(),
  );
  assert.equal(r.assets.find((a) => a.name === "install.sh")?.size, 7818);
  assert.ok(r.assets.every((a) => a.url.startsWith(`${WEB}/releases/download/v0.12.0/`)));
  assert.ok(!JSON.stringify(r).includes("sig="));
  assert.ok(!lines.some((l) => l.includes("sig=")));
});

test("fails when neither the API nor the release pages answer", async () => {
  const { f } = server({});
  await assert.rejects(
    readRelease({ fetch: f, ci: true }),
    /neither GitHub's API nor its release pages answered/,
  );
  await assert.rejects(readRelease({ fetch: f }), /PITBOARD_SITE_OFFLINE=1/);
});

test("refuses a pre-release or a tag that is not a version", async () => {
  for (const change of [{ prerelease: true }, { draft: true }, { tag_name: "nightly" }]) {
    const { f } = server({
      [`GET ${API}/releases/latest`]: () => json({ ...recorded, ...change }),
      [`GET ${SUMS_URL}`]: () => new Response(sums),
    });
    await assert.rejects(readRelease({ fetch: f }));
  }
});

test("reads the recorded fixture offline without the network", async () => {
  const { f, asked } = server({});
  const r = await readRelease({ fetch: f, fixture: { release: recorded, sums } });
  assert.equal(r.source, "fixture");
  assert.equal(r.version, "0.12.0");
  assert.equal(asked.length, 0);
});

test("reads which betas GitHub has published, and a draft as not", async () => {
  const beta = (tag: string, draft = false) =>
    json({ ...recorded, tag_name: tag, draft, prerelease: true });
  const { f, asked } = server({
    [`GET ${API}/releases/tags/v1.0.0-beta.3`]: () => json({ message: "Not Found" }, 404),
    [`GET ${API}/releases/tags/v1.0.0-beta.2`]: () => beta("v1.0.0-beta.2", true),
    [`GET ${API}/releases/tags/v1.0.0-beta.1`]: () => beta("v1.0.0-beta.1"),
  });
  const published = await readPublished({ fetch: f, token: "t0ken" }, [
    "1.0.0-beta.3",
    "1.0.0-beta.2",
    "1.0.0-beta.1",
  ]);
  assert.deepEqual([...published], ["1.0.0-beta.1"]);
  assert.ok(asked.every((a) => a.auth === "Bearer t0ken"));
  assert.deepEqual([...(await readPublished({ fetch: server({}).f }, []))], []);
});

test("stops when GitHub cannot say whether a beta is published", async () => {
  const limited = server({
    [`GET ${API}/releases/tags/v1.0.0-beta.1`]: () => json({ message: "rate limit" }, 403),
  });
  await assert.rejects(readPublished({ fetch: limited.f }, ["1.0.0-beta.1"]), /answered 403/);
  const other = server({
    [`GET ${API}/releases/tags/v1.0.0-beta.1`]: () => json({ ...recorded, tag_name: "v1.0.0" }),
  });
  await assert.rejects(readPublished({ fetch: other.f }, ["1.0.0-beta.1"]), /v1\.0\.0/);
});

test("takes the recorded betas offline without the network", async () => {
  const { f, asked } = server({});
  const published = await readPublished({ fetch: f, fixture: ["1.0.0-beta.1"] }, [
    "1.0.0-beta.2",
    "1.0.0-beta.1",
  ]);
  assert.deepEqual([...published], ["1.0.0-beta.1"]);
  assert.equal(asked.length, 0);
});

test("gives no star count rather than a guessed one", async () => {
  const ok = server({ [`GET ${API}`]: () => json({ stargazers_count: 46 }) });
  assert.equal(await readStars({ fetch: ok.f }), 46);
  const limited = server({ [`GET ${API}`]: () => json({ message: "rate limit" }, 403) });
  assert.equal(await readStars({ fetch: limited.f }), null);
  const down = server({});
  assert.equal(await readStars({ fetch: down.f }), null);
  assert.equal(await readStars({ fetch: ok.f, offline: true }), null);
  const odd = server({ [`GET ${API}`]: () => json({ stargazers_count: "46" }) });
  assert.equal(await readStars({ fetch: odd.f }), null);
});

test("sizes read as /download prints them", () => {
  const cases: [number, string][] = [
    [624, "624 B"],
    [874, "874 B"],
    [7818, "7.8 KB"],
    [98355, "98 KB"],
    [98541, "99 KB"],
    [196025, "196 KB"],
    [1741831, "1.7 MB"],
    [2027528, "2.0 MB"],
    [13801164, "13.8 MB"],
  ];
  for (const [bytes, text] of cases) assert.equal(formatSize(bytes), text);
});

test("lists the release's files in order and fails on one it does not know", async () => {
  const r = await readRelease({ fetch: server({}).f, fixture: { release: recorded, sums } });
  const rows = fileRows(r);
  assert.deepEqual(
    rows.map((row) => row.asset.name),
    [
      "Pitboard-v0.12.0-macos.zip",
      "pitboard-v0.12.0-aarch64-apple-darwin.tar.gz",
      "pitboard-v0.12.0-x86_64-apple-darwin.tar.gz",
      "pitboard-v0.12.0-x86_64-unknown-linux-musl.tar.gz",
      "pitboard-v0.12.0-aarch64-unknown-linux-musl.tar.gz",
      "install.sh",
      "SHA256SUMS",
      "appcast.xml",
    ],
  );
  assert.equal(rows[0].bom?.name, "Pitboard-v0.12.0-macos.zip.cdx.json");
  assert.equal(rows[5].bom, null);
  const more: Release = {
    ...r,
    assets: [
      ...r.assets,
      { name: "pitboard-v0.12.0-riscv64.tar.gz", size: 1, sha256: null, url: "" },
    ],
  };
  assert.throws(() => fileRows(more), /does not describe/);
});

test("lists a release's Windows zips after the Linux tarballs", async () => {
  const r = await readRelease({ fetch: server({}).f, fixture: { release: recorded, sums } });
  const zips = ["x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc"].flatMap((triple) =>
    [".zip", ".zip.cdx.json"].map((end) => ({
      name: `pitboard-v0.12.0-${triple}${end}`,
      size: 1,
      sha256: null,
      url: "",
    })),
  );
  const rows = fileRows({ ...r, assets: [...r.assets, ...zips] });
  assert.deepEqual(
    rows.slice(5, 7).map((row) => [row.asset.name, row.forHtml, row.bom?.name]),
    [
      [
        "pitboard-v0.12.0-x86_64-pc-windows-msvc.zip",
        "Command line, Windows on x64",
        "pitboard-v0.12.0-x86_64-pc-windows-msvc.zip.cdx.json",
      ],
      [
        "pitboard-v0.12.0-aarch64-pc-windows-msvc.zip",
        "Command line, Windows on ARM64",
        "pitboard-v0.12.0-aarch64-pc-windows-msvc.zip.cdx.json",
      ],
    ],
  );
});

test("reads SHA256SUMS in text and binary mode, and refuses anything else", () => {
  const a = "a".repeat(64);
  assert.equal(parseSums(`${a}  x.tar.gz\n${a} *y.zip\n`).size, 2);
  assert.throws(() => parseSums("not a sum\n"), /cannot read/);
});
