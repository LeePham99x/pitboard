import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { status, statusLine, tour } from "./terminal.ts";

const docs = (path: string) =>
  readFileSync(new URL(`../../../docs/${path}`, import.meta.url), "utf8");

const text = (html: string) =>
  html
    .replace(/<[^>]+>/g, "")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&amp;/g, "&");

test("the terminal tour prints what the Quickstart prints", () => {
  const quickstart = docs("quickstart.mdx");
  const lines = quickstart.split("\n").map((l) => l.trim());
  for (const step of tour) {
    assert.ok(lines.includes(step.command), `the Quickstart runs ${step.command}`);
    for (const out of step.output) {
      const line = text(out.html).trimEnd();
      if (line.trim()) assert.ok(quickstart.includes(line), `the Quickstart prints: ${line}`);
    }
  }
});

test("the status block keeps the Quickstart's columns", () => {
  const quickstart = docs("quickstart.mdx").split("\n");
  const start = quickstart.findIndex((l) => l.trim().startsWith("● work"));
  assert.ok(start > 0);
  const indent = quickstart[start].indexOf("●");
  const block = quickstart
    .slice(start, start + status.length)
    .map((l) => l.slice(indent).trimEnd());
  assert.deepEqual(
    status.map((l) => text(l).trimEnd()),
    block,
  );
});

test("the status line is the one the status line guide shows", () => {
  assert.ok(docs("guides/status-line.mdx").includes(text(statusLine)));
});
