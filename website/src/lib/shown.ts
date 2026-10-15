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

export function newestFull(all: Version[]): Version | undefined {
  return all.find((v) => !v.prerelease);
}

export function recentReleases(versions: Version[]): Version[] {
  return versions.filter((v) => !v.prerelease).slice(0, 3);
}
