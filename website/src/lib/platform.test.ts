import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { platforms, shownOn, type Platform } from "./platform.ts";

const source = readFileSync(new URL("./platform.ts", import.meta.url), "utf8");

// The variants global.css defines, by the platform head.js chose; null is a page without JavaScript.
const matches: Record<string, (reader: Platform | null) => boolean> = {
  "platform-macos": (reader) => reader === "macos" || reader === null,
  "platform-windows": (reader) => reader === "windows",
  "platform-linux": (reader) => reader === "linux",
};

function shows(classes: string, reader: Platform | null): boolean {
  let display = "";
  for (const name of classes.split(" ")) {
    const [variant, utility] = name.includes(":") ? name.split(":") : [null, name];
    assert.ok(["block", "hidden"].includes(utility), `${name} sets display`);
    if (variant === null) display ||= utility;
    else {
      assert.ok(variant in matches, `${variant} is a platform variant`);
      if (matches[variant](reader)) display = utility;
    }
  }
  return display === "block";
}

// Every set of drawings a figure can have: macOS's, with Windows' and Linux's or without them.
const sets: Platform[][] = [
  ["macos"],
  ["macos", "windows"],
  ["macos", "linux"],
  ["macos", "windows", "linux"],
];

test("a reader sees their platform's drawing, or macOS's where it has none", () => {
  for (const drawn of sets) {
    for (const reader of platforms) {
      const seen = drawn.filter((p) => shows(shownOn(p, drawn), reader));
      assert.deepEqual(seen, [drawn.includes(reader) ? reader : "macos"], `${reader} of ${drawn}`);
    }
  }
});

test("a page without JavaScript shows macOS's drawing alone", () => {
  for (const drawn of sets) {
    assert.deepEqual(
      drawn.filter((p) => shows(shownOn(p, drawn), null)),
      ["macos"],
    );
  }
});

test("a figure has a macOS drawing, and shows no drawing it was not given", () => {
  assert.throws(() => shownOn("windows", ["windows"]));
  assert.throws(() => shownOn("linux", ["macos", "windows"]));
});

test("platform.ts writes each set of classes whole, where Tailwind finds it", () => {
  for (const drawn of sets) {
    for (const p of drawn) assert.ok(source.includes(`"${shownOn(p, drawn)}"`), shownOn(p, drawn));
  }
});
