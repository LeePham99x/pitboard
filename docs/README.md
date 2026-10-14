# Pitboard documentation

This folder is the source of [docs.usepitboard.com](https://docs.usepitboard.com), built
with Mintlify. Pages are `.mdx` files, and `docs.json` holds the sidebar and site settings.
The writing rules are in [AGENTS.md](AGENTS.md).

Merging a change to `main` publishes the site: Mintlify builds it from this folder.

## Preview

Install the Mintlify CLI once, then run it in this folder:

```sh
npm i -g mint
mint dev
```

The site opens at `http://localhost:3000`. The first run downloads Mintlify's preview
client, about 360 MB. Restart `mint dev` after changing `docs.json`, because it misses
some changes to that file.

## Check

No CI job runs these checks, so run them in this folder before each commit. All three must
pass:

```sh
mint validate
mint broken-links --check-anchors --check-redirects
mint a11y
```

## Where things go

- Each page goes in the folder its sidebar group uses, such as `guides/` for
  **Use Pitboard**, and in that group in `docs.json`. `index`, `quickstart`, `install`,
  `troubleshooting` and `security` sit at the top.
- Images go in `images/` and are referenced as `/images/name.png`.
- `.mintignore` lists the files Mintlify must not publish, such as `AGENTS.md`.

Pitboard's mark is the board the app's icon and `MenuBarMark.swift` draw. Nothing regenerates
its copies, so change them together:

- `favicon.svg`, `favicon-dark.svg` and `logo/` in this folder: the site icons and header logos.
- `website/public/`: `favicon.svg`, `favicon.ico`, `apple-touch-icon.png`, the `icon-*.png`
  files and `og.png`.
- `website/src/assets/logo/`: the lockup in the site's header and the mark in its drawn menu bar.

A page that moves needs a redirect in `docs.json`, so links from outside the site keep
working. Markdown files in the repository, such as `README.md`, `SECURITY.md`, `CHANGELOG.md`
and `packaging/tap-README.md`, and the pages in `website/` link pages by path. The README's
copies on crates.io and in release tarballs keep their links.

A redirect maps only a path, so a heading those files link, such as
`security#what-leaves-your-machine`, keeps its text. The app's **Pitboard Help** item and
the Linux renewal unit link only the site's root, so no move breaks them.
