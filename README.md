# Pitboard

Switch Claude Code and Codex between your own accounts without signing out, and see what each
one has left.

[![CI](https://github.com/datlechin/pitboard/actions/workflows/ci.yml/badge.svg)](https://github.com/datlechin/pitboard/actions/workflows/ci.yml)
[![crates.io](https://img.shields.io/crates/v/pitboard.svg)](https://crates.io/crates/pitboard)
[![Licence](https://img.shields.io/badge/licence-Apache--2.0-blue.svg)](https://github.com/datlechin/pitboard/blob/main/LICENSE)

<img src="https://raw.githubusercontent.com/datlechin/pitboard/main/.github/media/menu.png"
  alt="The Pitboard menu, listing Claude Code and Codex accounts, with a check mark on each
  one in use and, under each account's name, its usage or that it needs signing in again"
  width="300">

Pitboard keeps the login of each account you are not using, called a parked login. A switch
puts the chosen account's parked login where Claude Code or OpenAI's Codex CLI reads it. Your
history, settings and projects stay where they are.

[Open Claude Code sessions](https://docs.usepitboard.com/concepts/switching#open-sessions)
follow a switch with no restart. A running `codex` keeps the old account until it starts
again.

The menu bar app also gives each account its own
[window on claude.ai or chatgpt.com](https://docs.usepitboard.com/guides/account-windows).

Pitboard has no server of its own and no telemetry. It sends each login only to the service
that issued it.
[What leaves your machine](https://docs.usepitboard.com/security#what-leaves-your-machine)
lists every request.

The command line runs on macOS and Linux, and the app on macOS 14 or later. Pitboard is
tested on macOS. On Linux it builds and passes its tests, but has not been run with real
logins.

## Install

```sh
curl -fsSL https://usepitboard.com/install.sh | sh
```

With Homebrew:

```sh
brew install datlechin/tap/pitboard              # the command line
brew install --cask datlechin/tap/pitboard-app   # the app
```

The app includes the command line, so install one or the other.
[Install Pitboard](https://docs.usepitboard.com/install) covers Cargo, release files and
updates. [Verify a download](https://docs.usepitboard.com/install/verify) checks the script
before you run it.

Removing Pitboard without `pitboard uninstall` leaves its parked logins behind. Follow
[Remove Pitboard](https://docs.usepitboard.com/install/remove).

## Quickstart

```sh
pitboard enroll personal        # the account Claude Code is signed in to
pitboard enroll work --sign-in  # another one, through Claude Code's own sign-in
pitboard                        # what each account has left
pitboard use work               # switch Claude Code to work
```

In the app, click Pitboard's item in the menu bar and choose the account. The
[Quickstart](https://docs.usepitboard.com/quickstart) shows each step and what it prints. For
Codex, put `codex/` before each label, as
[Use Pitboard with Codex](https://docs.usepitboard.com/guides/codex) explains.

Add accounts only this way, never with Claude Code's `/login` or with `codex login`. Each
replaces the login in use without Pitboard parking it. The account that was in use then needs
a browser sign-in.

## Documentation

The documentation is at [docs.usepitboard.com](https://docs.usepitboard.com). Start with:

- [Commands](https://docs.usepitboard.com/reference/commands)
- [JSON output](https://docs.usepitboard.com/reference/json-output)
- [Security and privacy](https://docs.usepitboard.com/security)
- [Troubleshooting](https://docs.usepitboard.com/troubleshooting)

## Contributing

[CONTRIBUTING.md](https://github.com/datlechin/pitboard/blob/main/CONTRIBUTING.md) says how to
build Pitboard and submit a change. Report a bug with the
[bug report form](https://github.com/datlechin/pitboard/issues/new?template=bug.yml), and a
vulnerability privately, as
[SECURITY.md](https://github.com/datlechin/pitboard/blob/main/SECURITY.md) says. Everyone
taking part follows the
[code of conduct](https://github.com/datlechin/pitboard/blob/main/CODE_OF_CONDUCT.md).

## Licence

Pitboard is licensed under
[Apache-2.0](https://github.com/datlechin/pitboard/blob/main/LICENSE). It is not affiliated
with Anthropic or OpenAI, as [NOTICE](https://github.com/datlechin/pitboard/blob/main/NOTICE)
states.
