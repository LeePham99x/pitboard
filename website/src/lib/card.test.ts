import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import sharp from "sharp";
import { HEIGHT, WIDTH, darkColors, describe, render, type Assets, type Card } from "./card.ts";

const file = (path: string) => readFileSync(new URL(`../${path}`, import.meta.url));
const font = (name: string) => file(`assets/fonts/${name}.ttf`);

const assets: Assets = {
  css: file("styles/global.css").toString(),
  lockup: file("assets/logo/lockup-color.svg").toString(),
  template: file("assets/logo/template.svg").toString(),
  fonts: [
    { name: "Inter", data: font("Inter-4.1-400"), weight: 400 },
    { name: "Inter", data: font("Inter-4.1-700"), weight: 700 },
    { name: "JetBrains Mono", data: font("JetBrainsMono-2.304-500"), weight: 500 },
    { name: "JetBrains Mono", data: font("JetBrainsMono-2.304-700"), weight: 700 },
  ],
};

test("takes the site's dark colours, whatever commas their light half holds", () => {
  const c = darkColors(assets.css);
  assert.equal(c["page"], "#090b0b");
  assert.equal(c["mn-bg"], "rgb(36 36 36 / 0.78)");
  assert.equal(c["white"], "#ffffff");
  assert.deepEqual(darkColors("--color-x: light-dark(rgb(0 0 0 / 0.1), #fff);"), { x: "#fff" });
});

test("draws each card as an opaque 1200 by 630 PNG under 300 KB", async () => {
  const cards: Card[] = [
    { kind: "home" },
    { kind: "page", label: "Download", title: "Pitboard for macOS and Linux" },
  ];
  for (const card of cards) {
    const png = await render(card, assets);
    const meta = await sharp(png).metadata();
    assert.deepEqual([meta.format, meta.width, meta.height], ["png", WIDTH, HEIGHT]);
    assert.equal(meta.hasAlpha, false);
    assert.ok(png.length < 300_000, `${card.kind} is ${png.length} bytes`);
  }
});

test("describes the accounts the home card's menu shows", () => {
  const alt = describe({ kind: "home" });
  assert.match(alt, /menu bar item, work 40%\./);
  assert.match(
    alt,
    /Claude Code has work, in use, and personal; Codex has main, in use, and spare;/,
  );
  assert.match(alt, /Switch AI coding accounts without signing out\./);
  assert.ok(alt.length <= 420, `${alt.length} characters`);
  assert.equal(
    describe({ kind: "page", label: "Download", title: "Pitboard for macOS and Linux" }),
    "Pitboard's logo above the label Download and the line: Pitboard for macOS and Linux.",
  );
});
