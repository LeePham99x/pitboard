// The systems the site draws, in the order it names them; head.js chooses one for each reader.
export type Platform = "macos" | "windows" | "linux";
export const platforms: Platform[] = ["macos", "windows", "linux"];
export const platformNames: Record<Platform, string> = {
  macos: "macOS",
  windows: "Windows",
  linux: "Linux",
};

// The classes that show each of a figure's drawings, or each panel of the install block: a
// platform's own to its readers, and macOS's to the readers of a platform it has none for, and
// without JavaScript. Written whole, so Tailwind finds every class.
const shown = {
  macos: { macos: "block", windows: "hidden", linux: "hidden" },
  windows: {
    macos: "block platform-windows:hidden",
    windows: "hidden platform-windows:block",
    linux: "hidden",
  },
  linux: {
    macos: "block platform-linux:hidden",
    windows: "hidden",
    linux: "hidden platform-linux:block",
  },
  all: {
    macos: "block platform-windows:hidden platform-linux:hidden",
    windows: "hidden platform-windows:block",
    linux: "hidden platform-linux:block",
  },
} as const;

export function shownOn(platform: Platform, drawn: readonly Platform[]): string {
  if (!drawn.includes("macos")) throw new Error("a figure has no macOS drawing");
  if (!drawn.includes(platform)) throw new Error(`a figure has no ${platform} drawing to show`);
  const windows = drawn.includes("windows");
  const linux = drawn.includes("linux");
  const set = windows && linux ? "all" : windows ? "windows" : linux ? "linux" : "macos";
  return shown[set][platform];
}

// A part of a page shows to its platforms' readers, and every part shows without JavaScript.
const parts = {
  macos: "block platform-windows:hidden platform-linux:hidden",
  windows: "hidden platform-windows:block no-js:block",
  linux: "hidden platform-linux:block no-js:block",
  "macos linux": "block platform-windows:hidden",
} as const;

export function partOf(on: readonly Platform[]): string {
  const set = platforms.filter((p) => on.includes(p)).join(" ");
  if (!(set in parts)) throw new Error(`no part of a page is for "${set}"`);
  return parts[set as keyof typeof parts];
}
