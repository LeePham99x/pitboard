// One line per release, betas included, for the home page's Recent releases, 60 characters at
// most. The build fails when the newest version in CHANGELOG.md, or one the table shows, has none.
export const highlights: Record<string, string> = {
  "1.0.0-beta.2": "The Windows app turns on daily renewal of parked logins.",
  "1.0.0-beta.1": "Pitboard runs on Windows 11 24H2 and later, in beta.",
  "0.15.0": "**Add Account** says what to install when a tool is missing.",
  "0.14.0": "Pitboard switches Claude Desktop accounts on macOS.",
  "0.13.0": "Warm-up starts a five-hour period once the last one resets.",
  "0.12.0": "An install script puts the command line on macOS and Linux.",
  "0.11.0": "`forget`, `stow` and `uninstall` need `--yes` when they cannot ask.",
  "0.10.0": "`pitboard stow` puts away a login file behind the keychain.",
};
