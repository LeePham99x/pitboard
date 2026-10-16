// Each page's link preview image, drawn once per build and named by a hash of its bytes, so a
// changed image gets a new address that every platform fetches again.

import { createHash } from "node:crypto";
import interBold from "../assets/fonts/Inter-4.1-700.ttf?inline";
import inter from "../assets/fonts/Inter-4.1-400.ttf?inline";
import monoBold from "../assets/fonts/JetBrainsMono-2.304-700.ttf?inline";
import mono from "../assets/fonts/JetBrainsMono-2.304-500.ttf?inline";
import lockup from "../assets/logo/lockup-color.svg?raw";
import template from "../assets/logo/template.svg?raw";
import css from "../styles/global.css?raw";
import { describe, render, type Assets, type Card } from "./card.ts";

const cards = {
  home: { kind: "home" },
  changelog: {
    kind: "page",
    label: "Changelog",
    title: "What each release added, changed and fixed",
  },
  download: { kind: "page", label: "Download", title: "Pitboard for macOS and Linux" },
} satisfies Record<string, Card>;

export type CardName = keyof typeof cards;

export interface OgImage {
  name: CardName;
  slug: string;
  path: string;
  png: Buffer;
  alt: string;
}

const bytes = (dataUrl: string) => Buffer.from(dataUrl.slice(dataUrl.indexOf(",") + 1), "base64");

const assets: Assets = {
  css,
  lockup,
  template,
  fonts: [
    { name: "Inter", data: bytes(inter), weight: 400 },
    { name: "Inter", data: bytes(interBold), weight: 700 },
    { name: "JetBrains Mono", data: bytes(mono), weight: 500 },
    { name: "JetBrains Mono", data: bytes(monoBold), weight: 700 },
  ],
};

let drawing: Promise<OgImage[]> | null = null;

export function ogImages(): Promise<OgImage[]> {
  drawing ??= Promise.all(
    (Object.entries(cards) as [CardName, Card][]).map(async ([name, card]) => {
      const png = await render(card, assets);
      const slug = `${name}-${createHash("sha256").update(png).digest("hex").slice(0, 8)}`;
      return { name, slug, path: `/og/${slug}.png`, png, alt: describe(card) };
    }),
  );
  return drawing;
}

export async function ogImage(name: CardName): Promise<OgImage> {
  return (await ogImages()).find((image) => image.name === name)!;
}
