import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { sessions } from "./sessions.ts";
import { account } from "./world.ts";
import { menuLine, plain, statusLine, statusLines } from "../lib/words.ts";

const repo = (path: string) => readFileSync(new URL(`../../../${path}`, import.meta.url), "utf8");
const docs = (path: string) => repo(`docs/${path}`);

// The text block in `page` that starts with the line `first`, as many lines as `length`.
function block(page: string, first: string, length: number): string[] {
  const lines = page.split("\n");
  const start = lines.findIndex((l) => l.trim() === first);
  assert.ok(start >= 0, `the page prints ${first}`);
  const indent = lines[start].indexOf(first);
  return lines.slice(start, start + length).map((l) => l.slice(indent).trimEnd());
}

const text = (lines: string[]) => lines.map((l) => plain(l).trimEnd());

// A Rust string literal as it reads once its line continuations are joined.
const joined = (source: string) => source.replace(/\\\n\s*/g, "");

test("pitboard after the switch prints the Quickstart's accounts", () => {
  const ours = text(statusLines("claude", "after"));
  assert.deepEqual(ours, block(docs("quickstart.mdx"), ours[0], ours.length));
});

test("pitboard before the switch prints what Check what each account has left prints", () => {
  const ours = text(statusLines("claude", "before"));
  assert.deepEqual(ours, block(docs("guides/usage.mdx"), ours[0], ours.length));
});

test("the status line before the switch is the Status line guide's", () => {
  assert.ok(docs("guides/status-line.mdx").includes(`\n${plain(statusLine("before"))}\n`));
});

test("the menu says an account's limits as the usage guide does", () => {
  assert.ok(docs("guides/usage.mdx").includes(`**${menuLine(account("personal"), "after")}**`));
});

test("pitboard use work prints what the Quickstart prints", () => {
  const ours = text(sessions.use.output);
  assert.deepEqual(ours, block(docs("quickstart.mdx"), ours[0], ours.length));
});

test("pitboard schedule install prints what Daily renewal prints", () => {
  const ours = text(sessions.schedule.output);
  assert.deepEqual(ours, block(docs("guides/renewal.mdx"), ours[0], ours.length));
});

test("the JSON envelope is the one JSON output shows", () => {
  const page = docs("reference/json-output.mdx");
  const shown = page.slice(
    page.indexOf("```json") + 7,
    page.indexOf("```", page.indexOf("```json") + 7),
  );
  assert.deepEqual(JSON.parse(sessions.json.output[0]), JSON.parse(shown));
});

test("pitboard watch starts with the line the command line writes", () => {
  const source = joined(repo("crates/pitboard/src/watch.rs"));
  assert.ok(source.includes("limit of the one in use reaches {}%{}. Stop with Ctrl-C."));
  assert.ok(source.includes('", and warms up {} from {}"'));
  const [start, watching] = text(sessions.watch.output);
  assert.match(
    start,
    /^Watching Claude Code\. Pitboard switches to another of your accounts once a limit of the one in use reaches \d+%, and warms up \w+ from \d\d:\d\d to \d\d:\d\d\. Stop with Ctrl-C\.$/,
  );
  const shape =
    /^\d\d:\d\d:\d\d Watching \w+: \d+% of its (5-hour|weekly) limit, as of \d\d:\d\d\.$/;
  assert.match(watching, shape);
  assert.ok(
    docs("guides/automatic-switching.mdx")
      .split("\n")
      .some((l) => shape.test(l.trim())),
  );
});

test("pitboard warm lists accounts as the warm-up guide does", () => {
  const ours = text(sessions.warm.output);
  const guide = block(docs("guides/warm-up.mdx"), ours[0], 3);
  assert.equal(ours[0], guide[0]);
  assert.match(ours[1], /^  \w+\s+on   warms up again at \d\d:\d\d$/);
  assert.match(guide[1], /^  \w+\s+on   warms up again at \d\d:\d\d$/);
  assert.match(ours[2], /^  \w+\s+off$/);
  assert.match(guide[2], /^  \w+\s+off$/);
});

test("pitboard log writes lines in the shape log shows", () => {
  const shape = /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\+07:00  [\w-]+\s+\w+  ok$/;
  for (const line of text(sessions.log.output)) assert.match(line, shape);
  const page = docs("reference/maintenance-commands.mdx");
  assert.ok(page.split("\n").filter((l) => shape.test(l)).length >= 3);
});
