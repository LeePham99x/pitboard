import changelogText from "../../../CHANGELOG.md?raw";
import fixtureRelease from "../data/fixtures/release-latest.json";
import fixtureSums from "../data/fixtures/SHA256SUMS?raw";
import { highlights } from "../data/highlights.ts";
import {
  compareVersions,
  parseChangelog,
  plainText,
  renderInline,
  type Version,
} from "./changelog.ts";
import { readRelease, readStars, type Release } from "./github.ts";

export interface SiteData {
  versions: Version[];
  release: Release;
  latest: Version;
  stars: number | null;
}

let loading: Promise<SiteData> | null = null;

// once per build, so every page and the feed show one release
export function siteData(): Promise<SiteData> {
  loading ??= load();
  return loading;
}

async function load(): Promise<SiteData> {
  const env = process.env;
  const offline = env.PITBOARD_SITE_OFFLINE === "1";
  const token = env.GITHUB_TOKEN || undefined;
  const log = (line: string) => console.log(`[site] ${line}`);
  const all = parseChangelog(changelogText);
  // the release commit fails here, not the pull requests after the release is out
  if (all[0]) highlight(all[0].version);
  const release = await readRelease({
    fetch,
    token,
    ci: Boolean(env.CI || env.WORKERS_CI),
    fixture: offline ? { release: fixtureRelease, sums: fixtureSums } : undefined,
    log,
  });
  const latest = all.find((v) => v.version === release.version);
  if (!latest) throw new Error(`CHANGELOG.md has no section for ${release.version}`);
  // a version written up before its release is published stays off the site
  const versions = all.filter((v) => compareVersions(v.version, release.version) <= 0);
  const stars = await readStars({ fetch, token, offline, log });
  return { versions, release, latest, stars };
}

export function highlight(version: string): string {
  const text = highlights[version];
  if (!text) throw new Error(`website/src/data/highlights.ts has no line for ${version}`);
  const html = renderInline(text);
  const length = plainText(html).length;
  if (length > 60) throw new Error(`the highlight for ${version} is ${length} characters, over 60`);
  return html;
}
