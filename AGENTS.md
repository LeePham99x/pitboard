# AGENTS.md

Pitboard is a Rust command line and a native macOS menu bar app that switch Claude Code and
Codex between a person's own accounts. It moves real OAuth logins, so a mistake here can sign
someone out of a paid account. These rules apply to people too.
[CONTRIBUTING.md](CONTRIBUTING.md) says how to set up, check and submit a change.

## Never

- Never write to, overwrite or delete a keychain item that holds a real login, such as
  `Claude Code-credentials`. A test names its items after itself and calls
  `common::guard_not_live` before its first write.
- Never run `claude`, `claude auth login`, `codex login` or `codex logout` against a real
  home. They replace or revoke the login stored there.
- Never run a `pitboard` built from a branch against your real home. Build first, then point
  `HOME`, `PITBOARD_HOME`, `CLAUDE_CONFIG_DIR` and `CODEX_HOME` at a fresh directory, since
  Cargo and rustup read `HOME` too.
- Never start an app you built outside a fixture. Run a debug build's executable with
  `PITBOARD_FIXTURE` and those four homes set, as
  [Run the debug build](CONTRIBUTING.md#run-the-debug-build) shows. A release build reads no
  fixture, so never start one you built.
- Never start a built app with `open`, Finder, a link, the Share menu or `launchctl setenv`.
  Launch Services passes on none of the variables, so the app starts on the real home.
- From a test or a script, never write to `~/.claude`, `~/.claude.json`, `~/.codex` or
  `~/.pitboard`. A test that writes gives every home a scratch directory, `CODEX_HOME`
  included.
- Never print a token. `pitboard doctor --json` hides them; raw files do not.
- Never run the tests as root or under `sudo` on a machine that holds logins.
- Tests never touch launchd, systemd, `/usr/local/bin`, `/Applications` or
  `~/Library/LaunchAgents`.
- Never load claude.ai or chatgpt.com, or sign in to either, from a test, a script or a
  branch build.
- Never read, copy or delete anything under `~/Library/WebKit/com.usepitboard.Pitboard`,
  where the account windows keep real sign-ins.
- Never open a `pitboard://` or `pitboard-debug://` link against a real home. It starts
  whichever copy claims the scheme, with the real home.
- Never leave a local build of the app registered with macOS: a link or a share can then
  start it on the real home. Unregister each copy you built, as
  [Unregister a build](CONTRIBUTING.md#unregister-a-build) shows, and leave the one in
  `/Applications` alone.
- Never put an `install.sh`, a rule naming it or a catch-all fallback in `website/`. A zone
  rule sends `/install.sh` to the newest release; a site file there would change what people
  pipe to `sh`.
- Never push a tag.
- Never let `.github/workflows/rotation.yml` name a repository secret. Its rehearsal ships
  bundles that update themselves, and must never reach the real key.
- Never keep the update key and the Developer ID certificate in one place. With both lost,
  no installed copy accepts another update.

### On Windows

[Windows](CONTRIBUTING.md#windows) says how to build and test Pitboard there.

- Never write to, overwrite or delete a Credential Manager item that holds a real login:
  `Claude Code-credentials`, every `Claude Code-credentials-<hash>` and each piece of them,
  Codex's `cli|*` and `secrets|*` targets, and anything under `Codex MCP Credentials`.
- Never use `cmdkey /delete`, `vaultcmd` or PowerShell's credential modules on those names.
  Tests write only `pitboard-citest-*` items or the slot hashed from their own folder.
- Never read, copy, rewrite or delete `%USERPROFILE%\.claude`, `%USERPROFILE%\.claude.json`,
  `%USERPROFILE%\.codex`, `%ProgramData%\OpenAI\Codex`, `C:\Program Files\ClaudeCode`, or
  the `%LOCALAPPDATA%\Pitboard` of an account that holds real logins.
- Never run a `pitboard.exe` built from a branch on a Windows account that holds real
  logins. Never put one on a `PATH`, in `%LOCALAPPDATA%\Programs`, in WinGet's Links or
  behind a Scoop shim.
- Never run a test or a script there before pointing `USERPROFILE`, `HOME`, `APPDATA`,
  `LOCALAPPDATA`, an existing `CODEX_HOME`, `CLAUDE_CONFIG_DIR` and `PITBOARD_HOME` at
  fresh folders. Set them after building: Cargo and rustup find their homes through
  `USERPROFILE`.
- Never weaken or get around the release gate in a build anybody installs. No release,
  script or package passes `--cfg pitboard_unreleased_windows`, `test-support` or
  `fixture`.
- Tests never touch Task Scheduler, the registry, the user `PATH` or DPAPI keys, and never
  run elevated on a machine that holds logins.
- Never run `.github/scripts/test-as-standard-user.ps1` or `runner-facts.ps1` on a machine of
  your own. They make a Windows account and change who holds which rights.
- Never run `claude.exe` or `codex.exe` to read a fact. Read the build as bytes on a Mac or
  Linux, as `pitboard-conformance` does.
- Never measure a running tool on a Windows machine that holds logins. Use one kept for it,
  with throwaway accounts and scratch homes.
- Never register the probe's sparse package outside a throwaway account in the VM.
  Unregister it and remove its certificate before reverting the snapshot.

## Check a change

Before you push, run what [Check a change](CONTRIBUTING.md#check-a-change) lists, and the
checks of each other section of CONTRIBUTING.md the change reaches.

## Rules

- A behaviour change starts with a test that fails against the current code.
- A claim about Claude Code or Codex needs a reading of a named build or an experiment,
  recorded in the tool's register, a test or the commit message.
  [Tool registers](CONTRIBUTING.md#tool-registers) says how.
- A change people would notice gets an entry under `## [Unreleased]` in
  [CHANGELOG.md](CHANGELOG.md), as [Changelog](CONTRIBUTING.md#changelog) says.
- A change that breaks `pitboard-core`'s public API raises the minor version in the same
  pull request. When `Cargo.toml` already names a minor version past the last release, it
  stays.
- A commit subject is one present-tense sentence saying what Pitboard does after the change,
  with no prefix. The body says why, and what was measured.

## Where things are

- [ARCHITECTURE.md](ARCHITECTURE.md): the code map and the rules the code keeps.
- [RELEASING.md](RELEASING.md): how a release is made.
- `docs/`: the documentation site; [docs/AGENTS.md](docs/AGENTS.md) has its writing rules.
