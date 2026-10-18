import { compareVersions, type Version } from "./changelog.ts";

// A beta below the latest full release came out before it, so only those above are asked.
export function ahead(all: Version[], latest: string): string[] {
  return all
    .filter((v) => v.prerelease && compareVersions(v.version, latest) > 0)
    .map((v) => v.version);
}

export function shown(all: Version[], latest: string, published: ReadonlySet<string>): Version[] {
  return all.filter(
    (v) => compareVersions(v.version, latest) <= 0 || (v.prerelease && published.has(v.version)),
  );
}

// A beta at the head of shown()'s list is published above the latest release.
export function recentReleases(versions: Version[]): Version[] {
  const full = versions.filter((v) => !v.prerelease).slice(0, 3);
  const [newest] = versions;
  return newest?.prerelease ? [newest, ...full] : full;
}
