export const OWNER_REPO = "datlechin/pitboard";
const API = `https://api.github.com/repos/${OWNER_REPO}`;
const WEB = `https://github.com/${OWNER_REPO}`;

export interface Asset {
  name: string;
  size: number;
  sha256: string | null;
  url: string;
}

export interface Release {
  version: string;
  tag: string;
  assets: Asset[];
  source: "api" | "fallback" | "fixture";
}

export interface Fixture {
  release: unknown;
  sums: string;
}

export interface ReadOptions {
  fetch: typeof fetch;
  token?: string;
  // a CI build that cannot read the release fails, so the last deploy stays live
  ci?: boolean;
  fixture?: Fixture;
  log?: (line: string) => void;
}

interface ApiAsset {
  name: string;
  size: number;
  digest?: string | null;
  browser_download_url: string;
}

interface ApiRelease {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets: ApiAsset[];
}

export function parseSums(text: string): Map<string, string> {
  const sums = new Map<string, string>();
  for (const line of text.split("\n")) {
    if (!line.trim()) continue;
    const m = /^([0-9a-f]{64}) [ *](\S+)$/.exec(line.trim());
    if (!m) throw new Error(`SHA256SUMS has a line it cannot read: ${line}`);
    sums.set(m[2], m[1]);
  }
  return sums;
}

function versionOf(tag: string): string {
  const m = /^v(\d+\.\d+\.\d+)$/.exec(tag);
  if (!m) throw new Error(`the latest release's tag is not a version: ${tag}`);
  return m[1];
}

export function fromApi(json: unknown, sumsText: string): Omit<Release, "source"> {
  const r = json as ApiRelease;
  if (!r || typeof r.tag_name !== "string" || !Array.isArray(r.assets)) {
    throw new Error("the release answer has no tag or no files");
  }
  if (r.draft || r.prerelease) throw new Error(`${r.tag_name} is not a published release`);
  const version = versionOf(r.tag_name);
  const sums = parseSums(sumsText);
  const assets = r.assets.map((a): Asset => {
    const digest = a.digest?.startsWith("sha256:") ? a.digest.slice(7) : null;
    const listed = sums.get(a.name) ?? null;
    if (listed && digest && listed !== digest) {
      throw new Error(`SHA256SUMS and GitHub disagree on ${a.name}`);
    }
    const sha256 = listed ?? digest;
    if (sha256 !== null && !/^[0-9a-f]{64}$/.test(sha256))
      throw new Error(`${a.name} has no SHA-256`);
    if (a.browser_download_url !== `${WEB}/releases/download/${r.tag_name}/${a.name}`) {
      throw new Error(`${a.name} downloads from somewhere else: ${a.browser_download_url}`);
    }
    return { name: a.name, size: a.size, sha256, url: a.browser_download_url };
  });
  for (const name of sums.keys()) {
    if (!assets.some((a) => a.name === name)) {
      throw new Error(`SHA256SUMS lists ${name}, which the release does not have`);
    }
  }
  return { version, tag: r.tag_name, assets };
}

async function get(f: typeof fetch, url: string, init: RequestInit = {}): Promise<Response> {
  return f(url, { ...init, signal: AbortSignal.timeout(20_000) });
}

export async function readRelease(o: ReadOptions): Promise<Release> {
  const log = o.log ?? (() => {});
  if (o.fixture) {
    log("release: the recorded fixture, PITBOARD_SITE_OFFLINE is set");
    return { ...fromApi(o.fixture.release, o.fixture.sums), source: "fixture" };
  }
  log(`release: token set: ${o.token ? "yes" : "no"}`);
  const headers: Record<string, string> = {
    accept: "application/vnd.github+json",
    "x-github-api-version": "2022-11-28",
  };
  if (o.token) headers.authorization = `Bearer ${o.token}`;

  let res: Response | null = null;
  try {
    res = await get(o.fetch, `${API}/releases/latest`, { headers });
  } catch (e) {
    log(`release: api unreachable: ${(e as Error).message}`);
  }
  if (res) {
    const left = res.headers.get("x-ratelimit-remaining") ?? "unknown";
    log(`release: api ${res.status}, rate limit left ${left}`);
    if (res.ok) {
      const json = (await res.json()) as ApiRelease;
      const sums = json.assets?.find((a) => a.name === "SHA256SUMS");
      if (!sums) throw new Error(`${json.tag_name} has no SHA256SUMS`);
      const text = await get(o.fetch, sums.browser_download_url);
      if (!text.ok) throw new Error(`SHA256SUMS answered ${text.status}`);
      return { ...fromApi(json, await text.text()), source: "api" };
    }
    if (res.status === 401 && o.token) {
      throw new Error("GitHub refused the token (401): it has expired or was revoked");
    }
    if (res.status !== 403 && res.status !== 429 && res.status < 500) {
      throw new Error(`GitHub answered ${res.status} for the latest release`);
    }
  }

  try {
    return await fallback(o.fetch, log);
  } catch (e) {
    const why = `neither GitHub's API nor its release pages answered: ${(e as Error).message}`;
    if (o.ci) throw new Error(why);
    throw new Error(`${why}. Build offline with PITBOARD_SITE_OFFLINE=1.`);
  }
}

// The signed URL a download redirects to is never logged or kept.
async function fallback(f: typeof fetch, log: (line: string) => void): Promise<Release> {
  const latest = await get(f, `${WEB}/releases/latest`, { method: "HEAD", redirect: "manual" });
  const where = latest.headers.get("location") ?? "";
  const m = /\/releases\/tag\/([^/?#]+)$/.exec(where);
  if (!m) throw new Error(`releases/latest answered ${latest.status} without a tag`);
  const tag = decodeURIComponent(m[1]);
  const version = versionOf(tag);
  const base = `${WEB}/releases/download/${tag}`;
  const sumsRes = await get(f, `${base}/SHA256SUMS`);
  if (!sumsRes.ok) throw new Error(`SHA256SUMS answered ${sumsRes.status}`);
  const sumsText = await sumsRes.text();
  const sums = parseSums(sumsText);
  const names = [...sums.keys(), "SHA256SUMS"];
  const assets: Asset[] = [];
  for (const name of names) {
    const url = `${base}/${name}`;
    const res = await get(f, url, { method: "HEAD" });
    const size = Number(res.headers.get("content-length"));
    if (!res.ok || !Number.isFinite(size) || size <= 0) {
      throw new Error(`${name} answered ${res.status} without a size`);
    }
    assets.push({ name, size, sha256: sums.get(name) ?? null, url });
  }
  log(`release: ${tag} from the release pages, ${assets.length} files`);
  return { version, tag, assets, source: "fallback" };
}

export async function readStars(o: Omit<ReadOptions, "fixture"> & { offline?: boolean }) {
  const log = o.log ?? (() => {});
  if (o.offline) return null;
  try {
    const headers: Record<string, string> = { accept: "application/vnd.github+json" };
    if (o.token) headers.authorization = `Bearer ${o.token}`;
    const res = await get(o.fetch, API, { headers });
    log(`stars: api ${res.status}`);
    if (!res.ok) return null;
    const n = ((await res.json()) as { stargazers_count?: unknown }).stargazers_count;
    return typeof n === "number" && Number.isInteger(n) && n >= 0 ? n : null;
  } catch (e) {
    log(`stars: ${(e as Error).message}`);
    return null;
  }
}

// 1 MB is 1,000,000 bytes, as GitHub and Finder count.
export function formatSize(bytes: number): string {
  if (bytes < 1000) return `${bytes} B`;
  if (bytes < 10_000) return `${(bytes / 1000).toFixed(1)} KB`;
  if (bytes < 1_000_000) return `${Math.round(bytes / 1000)} KB`;
  return `${(bytes / 1_000_000).toFixed(1)} MB`;
}

export interface FileRow {
  asset: Asset;
  forHtml: string;
  bom: Asset | null;
}

// A file the release gains fails the build until it is described here.
export function fileRows(release: Release): FileRow[] {
  const v = release.version;
  const known: [string, string][] = [
    [`Pitboard-v${v}-macos.zip`, "The app, for Apple silicon and Intel"],
    [`pitboard-v${v}-aarch64-apple-darwin.tar.gz`, "Command line, macOS on Apple silicon"],
    [`pitboard-v${v}-x86_64-apple-darwin.tar.gz`, "Command line, macOS on Intel"],
    [`pitboard-v${v}-x86_64-unknown-linux-musl.tar.gz`, "Command line, Linux on x86_64"],
    [`pitboard-v${v}-aarch64-unknown-linux-musl.tar.gz`, "Command line, Linux on aarch64"],
    ["install.sh", "The install script"],
    ["SHA256SUMS", "Checksums of the tarballs, the app and <code>install.sh</code>"],
    ["appcast.xml", "The feed the app updates from"],
  ];
  const byName = new Map(release.assets.map((a) => [a.name, a]));
  for (const a of release.assets) {
    const main = a.name.endsWith(".cdx.json") ? a.name.slice(0, -".cdx.json".length) : a.name;
    if (!known.some(([n]) => n === main))
      throw new Error(`the release has ${a.name}, which /download does not describe`);
  }
  return known
    .filter(([name]) => byName.has(name))
    .map(([name, forHtml]) => ({
      asset: byName.get(name)!,
      forHtml,
      bom: byName.get(`${name}.cdx.json`) ?? null,
    }));
}

export function appZip(release: Release): Asset {
  const zip = release.assets.find((a) => a.name === `Pitboard-v${release.version}-macos.zip`);
  if (!zip) throw new Error(`${release.tag} has no app`);
  return zip;
}
