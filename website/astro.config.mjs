import sitemap from "@astrojs/sitemap";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "astro/config";

export default defineConfig({
  site: "https://usepitboard.com",
  trailingSlash: "never",
  build: { format: "file", inlineStylesheets: "never" },
  // Whitespace stays as written: the menu, the terminal and the commands depend on it, and
  // "jsx" drops the space between a word and a link that Prettier wraps onto the next line.
  compressHTML: false,
  devToolbar: { enabled: false },
  integrations: [sitemap({ filter: (page) => !page.endsWith("/404") })],
  vite: {
    plugins: [tailwindcss()],
    // Scripts and fonts stay files, so the CSP needs no hash for them.
    build: { assetsInlineLimit: 0 },
    // CHANGELOG.md sits beside website/, one folder up.
    server: { fs: { allow: [".."] } },
  },
});
