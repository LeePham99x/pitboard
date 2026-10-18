import assert from "node:assert/strict";
import { test } from "node:test";
import type { Version } from "./changelog.ts";
import { ahead, recentReleases, shown } from "./shown.ts";

const at = (version: string): Version => ({
  version,
  prerelease: version.includes("-"),
  date: "2026-11-01",
  intro: null,
  sections: [],
  link: "",
});

// newest first, as CHANGELOG.md lists them; 0.16.0 is written up and not released yet
const all = ["1.0.0-beta.2", "1.0.0-beta.1", "0.16.0", "0.15.0", "0.14.0"].map(at);

test("asks GitHub only of the betas above the latest full release", () => {
  assert.deepEqual(ahead(all, "0.15.0"), ["1.0.0-beta.2", "1.0.0-beta.1"]);
  assert.deepEqual(ahead([at("1.0.0"), at("1.0.0-beta.1"), at("0.15.0")], "1.0.0"), []);
});

test("keeps a version off the site until its release is published", () => {
  assert.deepEqual(
    shown(all, "0.15.0", new Set(["1.0.0-beta.1"])).map((v) => v.version),
    ["1.0.0-beta.1", "0.15.0", "0.14.0"],
  );
  assert.deepEqual(
    shown(all, "0.15.0", new Set()).map((v) => v.version),
    ["0.15.0", "0.14.0"],
  );
  const later = [at("1.0.0"), at("1.0.0-beta.1"), at("0.15.0")];
  assert.deepEqual(
    shown(later, "1.0.0", new Set()).map((v) => v.version),
    ["1.0.0", "1.0.0-beta.1", "0.15.0"],
  );
});

test("Recent releases put the newest published beta above the three newest full releases", () => {
  const many = ["1.0.0-beta.2", "1.0.0-beta.1", "0.15.0", "0.14.0", "0.13.0", "0.12.0"].map(at);
  const recent = (latest: string, published: string[], versions = many) =>
    recentReleases(shown(versions, latest, new Set(published))).map((v) => v.version);
  assert.deepEqual(recent("0.15.0", ["1.0.0-beta.2", "1.0.0-beta.1"]), [
    "1.0.0-beta.2",
    "0.15.0",
    "0.14.0",
    "0.13.0",
  ]);
  assert.deepEqual(recent("0.15.0", []), ["0.15.0", "0.14.0", "0.13.0"]);
  // at 1.0.0 the beta falls below the latest release and leaves the table
  const later = ["1.1.0-beta.1", "1.0.0", "1.0.0-beta.1", "0.15.0", "0.14.0"].map(at);
  assert.deepEqual(recent("1.0.0", [], later), ["1.0.0", "0.15.0", "0.14.0"]);
  assert.deepEqual(recent("1.0.0", ["1.1.0-beta.1"], later), [
    "1.1.0-beta.1",
    "1.0.0",
    "0.15.0",
    "0.14.0",
  ]);
});
