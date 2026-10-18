// The systems the site draws, in the order it names them; head.js chooses one for each reader.
export type Platform = "macos" | "windows" | "linux";
export const platforms: Platform[] = ["macos", "windows", "linux"];

// The classes that show each of a figure's drawings: a platform's own to its readers, and macOS's
// to the readers of a platform it has none for. Written whole, so Tailwind finds every class.
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
