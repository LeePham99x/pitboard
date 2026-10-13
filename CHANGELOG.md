# Changelog

All notable changes to Pitboard are recorded here. The format follows [Keep a
Changelog](https://keepachangelog.com/en/1.1.0/), and Pitboard adheres to [Semantic
Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Copy Link** in the sign-in dialog copies the sign-in page's address, to paste into
  another browser or browser profile.

### Changed

- **Open Sign-In Page** in the sign-in dialog is renamed **Open**.
- The `pitboard` cask's caveats link
  [Upgrade from 0.3.0 or earlier](https://docs.usepitboard.com/install/upgrade-from-0-3-0) in
  place of the commands that remove 0.3.0's formula.

## [0.13.0] - 2026-10-13

### Added

- Pitboard can [warm up](https://docs.usepitboard.com/guides/warm-up) a Claude Code account:
  once its five-hour limit resets, Claude Code sends one short message with that account's
  login, so the next window starts at once. Turn it on with `pitboard warm on <account>` or
  in **Settings** > **Limits**.

### Changed

- The app's icon and its mark in the menu bar are a pit board held up, its top row lit, in
  place of three bars and a speedometer.
- **Settings** has **Limits** and **Renewal** panes. The automatic switch and warm-ups moved
  from **General** to **Limits**, and daily renewal and **Renew Now** to **Renewal**.
- The automatic switch, and the app's offer once an account runs out, prefer an account
  without a weekly limit for all models, such as a Team seat.
- In `pitboard-core`, `usage::roomiest` is now `usage::preferred` and `usage::Room` gains
  `weekly`; `autoswitch::retry_after` and its constants moved to `duty::cadence`, and
  `autoswitch::Look` is now `duty::Look<Auto>`.

### Fixed

- **Settings** fits on smaller screens, such as 1024 x 768: it opens at the top of the
  screen, and a pane longer than the window scrolls.

## [0.12.0] - 2026-10-12

### Added

- Each account shows its plan, such as `Max 20x`, `Team 5x` or `Pro`, in the app, the menu
  and `pitboard status`. `--json` gives it as `plan`.
- An [install script](https://docs.usepitboard.com/install) puts the command line on macOS
  and Linux without Homebrew or Rust: `curl -fsSL https://usepitboard.com/install.sh | sh`.
  It installs `pitboard` in `~/.local/bin`, checks the download's SHA-256 and edits no shell
  profile.

## [0.11.0] - 2026-10-10

### Changed

- **Breaking:** `pitboard forget`, `stow` and `uninstall` change nothing where they cannot
  ask, such as from a script, unless you pass `--yes`. They stop with `confirmation_needed`,
  and `--json` no longer implies `--yes`.
- The account index is schema 7, which earlier versions cannot read. Update the command line
  and the app together.
- When Anthropic no longer accepts the Claude Code login in use, Pitboard says to run
  `claude` and sign in again, and marks the account `login_refused`.

### Fixed

- A switch, `pitboard stow` and the automatic switch no longer stop when the stored Claude
  Code login's access token has expired: Pitboard renews it, as Claude Code would.
- An interrupted `pitboard stow` no longer loses a login it renewed. Pitboard keeps the
  renewed login until it is saved where Claude Code keeps it.

## [0.10.0] - 2026-10-10

### Added

- `pitboard stow`, and **Put Away** in the app, put away a Claude Code login left in
  `~/.claude/.credentials.json` behind the keychain on macOS, so running sessions follow a
  switch again. It asks first, and parks the login if Pitboard has none for that account.
- The warnings `config_names_another`, where Claude Code's config names another account than
  the one whose login is stored, and `in_use_unconfirmed`. `pitboard doctor` has an `in use`
  check.
- **Update Claude Code's Config** in the app's notice, and `pitboard use` on the account
  already in use, write that account into Claude Code's config without moving a login.
- `pitboard log` records why the automatic switch did not switch, as `auto-stay`, shown in
  the app's **Activity** pane as **Automatic switch not made**.
- `pitboard watch --json` has the event `waiting`, and more fields on `idle`, `no_room` and
  `skipped`.

### Changed

- The account index is schema 6, which earlier versions cannot read. Update the command line
  and the app together.
- While `~/.claude/.credentials.json` is behind the keychain on macOS, every read warns with
  `fallback_login`, and a switch says running sessions keep their account until they renew
  their login or restart.
- **Breaking:** `pitboard watch --json` reports `switch_interrupted` and
  `custom_oauth_endpoint` with the new event `not_watching`, not with `skipped`. The app and
  `pitboard watch` say once why Pitboard is not switching.
- The app decides whether to switch every 30 seconds, as `pitboard watch` does, and **Switch
  when a limit reaches** shows which account it watches. After a failed switch, both wait a
  minute, doubling up to 15 minutes, before they try again.
- An answer from Anthropic replaces a Claude Code account's reading whole, so numbers filed
  under the wrong account go at its next answer.
- The status line files a session's usage under the account the numbers belong to, and
  labels it `work?` or `?` while it cannot be sure.
- The app no longer offers an account whose reading may leave out a limit the account in use
  has.
- In `pitboard-core`, `State::active` gives way to `State::in_use`, `autoswitch` gains
  `Blind` and `Look`, and many items are added, renamed or removed.

### Removed

- **Breaking:** `"source": "claude_code_cache"` in `pitboard status --json`, and `pitboard
  doctor`'s `usage_cache` check. Pitboard no longer reads Claude Code's usage cache.
- The error code `switch_overtaken`, which was never printed.

### Fixed

- A sign-in outside Pitboard that replaces the login of the account in use shows as `login
  replaced outside Pitboard` until that account signs in again. It showed as `nothing
  parked`.
- An account just switched to no longer shows the previous account's usage.
- The automatic switch and the app's offer can choose an account with fewer limits, such as
  a Team seat with no weekly limit for all models.
- The automatic switch switches at the share the menu shows, so 95% shown counts as 95%.
  Every front end rounds a half up and changes colour at 70% and 90%.
- Pitboard takes the account in use from what Anthropic last said of the stored login, not
  from Claude Code's config, which another Claude Code process can rewrite.
- The automatic switch identifies the stored login before it decides, so after a sign-in
  outside Pitboard it watches the right account. When it cannot tell, it decides nothing and
  says so.
- The automatic switch judges every limit of the account in use, and says and records each
  reason it does not switch once.
- A Claude Code switch holds the lock a session takes to renew its login, so a renewal at
  the same moment no longer leaves the account switched from with a refused login.
- The app and `pitboard watch` see a change made in the same second as the last one, such as
  a `pitboard use` typed in a terminal.
- The app and `pitboard watch` no longer report a switch that another `pitboard` is making
  as interrupted.
- A login too large to pass without the argument line, under `PITBOARD_NO_ARGV`, counts as
  one of the automatic switch's three attempts.

## [0.9.0] - 2026-10-08

### Added

- On macOS, `pitboard doctor` and each change to a Claude Code account warn with
  `fallback_login` when `~/.claude/.credentials.json` holds a login besides the keychain's,
  which [Claude Code over
  SSH](https://docs.usepitboard.com/troubleshooting#claude-code-over-ssh-uses-another-account)
  uses. The app says **Another login is left in a file**.
- Each limit says how its use compares with an even
  [pace](https://docs.usepitboard.com/concepts/usage#pace) to its reset, such as `27% over
  pace`, in `pitboard status`, the app and, as `▲` or `▼`, the status line. `--json` gives
  it as `pace`.

### Changed

- **Breaking:** A keychain locked where it cannot ask for its password stops a command with
  `credential_store_locked`, not `credential_store_unreadable`. A script that checks for the
  old code should check for both.
- How long the account in use lasts follows its limits' paces, as in `weekly limit runs out
  in 11h 05m at this pace`. Other accounts no longer say it.
- `pitboard status --json` gives each account `organization_uuid`, the Claude organisation
  its login belongs to.
- The account index is schema 5, which earlier versions cannot read. Update the command line
  and the app together.

### Removed

- Pitboard no longer keeps `readings/` in its directory, and deletes the files it wrote
  there.

### Fixed

- `pitboard doctor` and **This Mac** report a locked keychain once, as a failed `credential`
  check, and each parked login behind it as `not read: the keychain is locked`, not as
  needing a new sign-in.
- While the login in use cannot be read, the account Pitboard last switched to shows `login
  could not be read` (`login_unreadable`), not **Needs signing in again**.
- One person's Claude accounts in two organisations, such as a claude.ai plan and a Team
  plan, are two accounts, each with its own label and limits. The second was refused as
  already enrolled.

## [0.8.0] - 2026-10-08

### Added

- Pitboard can [switch Claude Code
  automatically](https://docs.usepitboard.com/guides/automatic-switching) before the account
  in use runs out, so running sessions move to the account with the most room. It switches
  once any limit reaches a share you choose, never switches Codex and logs `auto-switch`.
- `pitboard watch` switches Claude Code that way while it runs in a terminal, whatever the
  app's setting. `--at` sets the share, `--once` decides once and exits, and `--json` prints
  one event per line.
- The app's **Settings** > **General** has **Switch Claude Code automatically**, off by
  default, and the share it switches at. The app switches only while open, and posts a
  notification for each switch and for what stops one.
- `pitboard doctor` and **This Mac** have a `network` check that names the proxy Pitboard's
  requests go through, hiding any password, and warns about a SOCKS proxy or a proxy
  variable it cannot read.
- The daily renewal schedule goes through the proxy of the Pitboard that installed it; run
  `pitboard schedule install` again after your proxy changes. `pitboard doctor`'s
  `schedule_proxy` check warns when the two differ.

### Changed

- A Windows build of Pitboard compiles, and answers only `--version`, `--help`,
  `completions` and `manpage`. Anything else says Pitboard for Windows is not released yet
  (`windows_not_released`) and changes nothing.
- The product is called Pitboard, with a capital P, in the app, the command line's messages
  and the docs. The command stays `pitboard`, and no key it reads or prints changes.
- `pitboard status` names a limit that is not a whole number of hours by its minutes or
  seconds, such as `90m`, as the app does. It showed its kind, such as `90_minute`.
- The app writes spans as `pitboard status` does, such as **resets in 2h 05m**, and says
  **resetting now** once a reset is due, where it said nothing.
- Under a minute, `pitboard status` says `about to run out` or `resets any moment`, as the
  app does.
- `pitboard doctor` and **This Mac**, and `pitboard renew` and **Renew Now**, sum up in the
  same words, such as `1 broken: do not switch accounts until fixed.`
- The command line runs the `claude` or `codex` that `PITBOARD_CLAUDE` or `PITBOARD_CODEX`
  names, as the app does, and `pitboard doctor` reports its build. In both, an empty value
  names nothing.
- Pitboard reads a few rare links shared with it, or carried in a `pitboard://` link,
  differently, such as `ｃ%EF%BD%8Caude.ai`, which it now takes for claude.ai.
- The app keeps its own preferences, such as the tools you said **Not Now** to, in
  `app.json` in Pitboard's directory, so they follow `PITBOARD_HOME`.
- The app posts that the account in use has run out once per reset, even across relaunches,
  and keeps what it posted in `told.json`.
- The app updates text that depends only on the time, such as **used up until 14:05**, once
  a minute, and what its settings can offer each time they are shown.
- The **Activity** list names a switch **Switch**, where it said **Use**.
- The app keeps the account windows' records in `windows.json` in `~/Library/Application
  Support/com.usepitboard.Pitboard`, not in its macOS preferences. If it cannot read that
  file, it deletes no window's data and never writes over it.
- `--json` output is ASCII only: other characters, as in a label, are `\u` escapes, which a
  JSON parser reads as before. `pitboard doctor --json` details and `pitboard statusline
  --json`'s `line` carry `\u00b7`.
- **Breaking:** On macOS and Linux, Pitboard [changes nothing as
  root](https://docs.usepitboard.com/troubleshooting#pitboard-changes-nothing-as-root) or
  with `sudo`, and stops with `elevated`; run it as yourself. `pitboard status` still
  answers as with `--offline`, warning `read_only`.
- `pitboard schedule install`, and **Renew parked logins daily**, refuse with
  `schedule_not_default_home` while `PITBOARD_HOME` names a directory other than
  `~/.pitboard`, since the schedule renews only `~/.pitboard`. Unset `PITBOARD_HOME` to turn
  it on.
- Pitboard refuses SOCKS proxies: while `ALL_PROXY`, `HTTPS_PROXY` or `HTTP_PROXY` names
  one, each request fails as `unreachable`, naming the variable, unless `NO_PROXY` names its
  host. Requests no longer go out directly, around a SOCKS proxy you set.
- Pitboard writes the daily renewal schedule's files so only you can read them, mode 600,
  since they can hold a proxy's password.
- Pitboard's own Codex sign-ins tell Codex to keep the new login in `auth.json`, where
  Pitboard reads it back, even when `/etc/codex/config.toml` or a project's
  `.codex/config.toml` chooses the keychain.
- `pitboard doctor`'s `Codex login store` line, and **This Mac**, say which setting chose
  Codex's store, such as `file (Codex's default)`. `--json` gives `unknown` as `backend`
  where nobody can tell.
- In `pitboard-core`, `assumptions::Platform::ALL` is gone, `holder::Remedy::ReopenApp`
  takes an `AppId` in place of `bundle_id`, `Pitboard::renew` returns a `Result`, writes
  take a `service::Permit`, `Context::new` names no proxy, and many items are added.

### Fixed

- When the account in use runs out, the app offers only an account with room in every limit.
  It offered one with room in the limit that ran out, even if another was spent.
- Where Pitboard cannot list running processes, a Codex switch, or a sign-in that puts a new
  login in use, warns with `sessions_unknown` that earlier Codex sessions may still be
  running. It said nothing.
- Pitboard reads Codex's login store from every config layer Codex reads, such as
  `/etc/codex` and managed preferences, not only your `config.toml`. Where one chooses a
  store other than the file, Codex changes stop with `live_store_unsupported`.
- Pitboard also stops with `live_store_unsupported` where a Codex config layer cannot be
  read, names a store Codex does not have, or has a `profile` line no profile matches.
- Pitboard no longer refuses a Codex account where your `config.toml` chooses another store
  but a managed setting or a requirement chooses the file, which Codex then uses.
- An account's window no longer opens again on a sign-in page of its site, such as
  chatgpt.com's `/api/auth`, but on the page before or the site's home.
- After Claude Code refuses a pasted code, the sign-in sheet asks for the code again, and
  the same sign-in takes the next one. Only **Cancel** could end it before.
- **Sign In** pressed right after **Cancel** no longer says another `pitboard enroll
  --sign-in` is already waiting.
- After you add, rename or forget an account, give up on a switch or press **Renew Now**,
  the app shows what it reads right after. An account just added could show no numbers.
- A sign-in that fails, or a name that cannot be saved, after its sheet has closed is said
  in the window. It was said nowhere.
- Quitting Pitboard stops a sign-in under way and the tool's own sign-in with it, where it
  left `claude` or `codex` running.
- The window shows two warnings with the same words as two notices, and **This Mac** gives
  each account's parked login check its own row. Both could draw wrongly.
- **This Mac** says it is checking until the last check is in, rather than showing **Checked
  at** beside checks still running.
- After an interrupted switch Pitboard cannot finish, the app shows **An interrupted switch
  is waiting**, with **Give Up**, and `pitboard status` warns with `recovery_undetermined`.
  The notice never appeared, and a failed switch offered only **OK**.
- `pitboard abandon` and **Give Up** give up on an interrupted Codex switch while
  `CLAUDE_CODE_CUSTOM_OAUTH_URL` is set, as other Codex changes do. Both refused.
- `pitboard doctor` and the app's checks no longer warn that a parked login could not be
  read after a switch, an enrolment or a renewal. Its advice about a login others can read
  is one sentence again.
- The note under the sign-in sheet's **Code** field says Claude Code takes the code shown
  after you sign in, whether or not your browser came back to it.
- The sign-in sheet links the right address when Claude Code prints it as a terminal
  hyperlink, as with `FORCE_HYPERLINK` set. It picked up a stray control character.
- With `HOME` unset, the command line uses your account's home directory, as the app does,
  not the folder it was run from.
- With `HOME`, `PITBOARD_HOME` or `CLAUDE_CONFIG_DIR` empty or relative, or `CODEX_HOME`
  relative, Pitboard refuses every command but `completions`, `manpage` and `doctor` with
  `home_not_absolute`. Set a full path or unset the variable.
- The daily renewal schedule renews `~/.pitboard` even when `PITBOARD_HOME` reaches it
  through `launchctl setenv` or systemd's user environment. It renewed that directory and
  left the parked logins in `~/.pitboard` to run out.
- On Linux, the daily renewal timer runs `pitboard renew --scheduled` once `pitboard
  schedule install` rewrites it. After going back to an earlier Pitboard, which does not
  take `--scheduled`, the timer fails every day until that Pitboard's `pitboard schedule
  install` rewrites it.
- The command line and the app take a `claude` or `codex` they find only when it is a
  regular file you may run, and otherwise look further on `PATH`.
- The app reads the environment it was started with as the command line does, so it honours
  variables such as `CLAUDE_CODE_CUSTOM_OAUTH_URL` and `ANTHROPIC_API_KEY`. Opened from
  Finder, it sees them only when set with `launchctl setenv`.
- The **Open Link** window and the Share sheet name claude.ai and chatgpt.com in English. On
  a Mac in another language they joined the two names in it.

### Security

- Pitboard refuses more synced folders as its directory, with `state_on_synced_drive`, such
  as any folder in `~/Library/CloudStorage` or `~/Library/Mobile Documents`.
- A sync client's folder name, such as `Dropbox`, `Google Drive`, `OneDrive` or `Sync`,
  counts anywhere in the path of `~/.pitboard`, in any case. So a home such as `/home/sync`
  is refused too, and `PITBOARD_HOME` can name a folder outside it.
- Pitboard refuses a site's sign-in link, or a link with a `.` or `..` path segment, however
  the rest of its path is written. Some, such as `claude.ai/magic-link/%FF`, opened in an
  account's window, where another person's link signs it in as them.
- A link that names a user or a port, and a `pitboard://` link with a user, port or path, is
  refused whatever they hold. Some were opened as if they had none.

## [0.7.0] - 2026-10-04

### Added

- The app gives each enrolled account a [window on its tool's
  site](https://docs.usepitboard.com/guides/account-windows), claude.ai or chatgpt.com, with
  its own website data, so every account stays signed in side by side.
- An account's window has Back, Forward, Reload, zoom and **Find** with Safari's shortcuts,
  and opens at the last page it showed.
- When an account's window first opens, a note says how to sign in, since Google's sign-in
  does not work inside apps. Other web pages open in your default browser.
- Downloads in an account's window save to your Downloads folder without replacing a file,
  and ask first when an artifact or another site starts one. The **Downloads** button lists
  them.
- **File** > **Remove Website Data** signs an account's window out on this Mac only.
- Choose **Pitboard** in a browser's Share menu to open a claude.ai or chatgpt.com page as
  the account you pick.
- While any of its windows is open, Pitboard has a Dock icon, is in Command-Tab and shows
  its menus in the menu bar.

### Changed

- **View** > **Refresh** (Command-R) works in every window, and reloads the page in an
  account's window.
- Forgetting an account also deletes everything its window keeps on this Mac, its sign-in
  included.
- `brew uninstall --zap --cask pitboard-app` also removes the account windows' data.

## [0.6.0] - 2026-10-01

### Added

- Switching Codex in the app while the ChatGPT app is open offers **Quit ChatGPT and
  Switch**, since ChatGPT otherwise keeps the old login. It quits ChatGPT, switches and
  opens it again; the command line never quits an app.

### Changed

- After a Codex switch, Pitboard names each program still running the old login and how to
  make it take the new one. `pitboard doctor`'s `codex_running` check lists them too.
- A switch counts only your own `codex` processes, not every user's.
- In `pitboard-core`, `SessionsStillRunning` and `SessionsKeepTheOldLogin` carry `holding`
  in place of `program` and `count`, and the `holder` module is new.

## [0.5.2] - 2026-09-29

### Fixed

- After a banked reset on claude.ai, Pitboard shows the account's lower weekly share the
  next time it asks Anthropic, not 100% until the old reset. A plan upgraded mid-window is
  followed too.

## [0.5.1] - 2026-09-29

### Added

- In `pitboard-core`, `assumptions::read_on` and `assumptions::Platform` say which systems'
  builds a fact is read from.

### Changed

- The README introduces Pitboard and links to
  [docs.usepitboard.com](https://docs.usepitboard.com) for the guides and reference.
- `SECURITY.md` holds only the security policy. Where parked logins are kept and what leaves
  your machine moved to [Security and privacy](https://docs.usepitboard.com/security).
- Release steps moved to `RELEASING.md`, and the code map to `ARCHITECTURE.md`.

### Fixed

- `pitboard status --fresh` and the app's **Refresh** no longer ask Anthropic or OpenAI
  again during a wait the service asked for (a 429 with `Retry-After`). They still ask again
  after a service could not be reached.
- An account whose service never answered and then could not be reached no longer shows as
  rate limited.
- With `PITBOARD_NO_ARGV=1`, renewing a parked login too large for `security`'s standard
  input no longer loses it.
- A switch whose login could not be read back names the `pitboard use` that finishes or
  undoes it.
- With Codex's `auth.json` missing, `pitboard doctor` says to run `codex login`.
- The man page covers every command on its one page, instead of naming pages that were never
  installed.
- The app says Claude Code is not installed when it finds neither `claude` nor `codex`, and
  no login or enrolled account.

### Security

- The app, and a renewal schedule installed while `PITBOARD_NO_ARGV` is set, honour it, so a
  large login no longer reaches `security` as an argument another process running as you
  could read.

## [0.5.0] - 2026-09-28

### Added

- The window's account list can rename an account, use one with a double-click or Return and
  forget one with Delete, and each account's menu has **Copy Email Address** and **Sign In
  Again**.
- The menu has **About Pitboard**.

### Changed

- The menu bar item opens a menu, not a panel, with each account under its tool. Choosing
  one switches to it.
- Adding an account, signing in again and renaming are sheets over Pitboard's window, and a
  sheet stays open while your browser is in front.
- A failed switch, rename or forget says why in an alert, and a failed sign-in says so in
  its sheet.
- **Settings** has a **Command Line** tab, and **Menu bar shows** picks the account and
  usage, the usage alone or the icon alone. **Open Pitboard at login** says when macOS waits
  for your approval in Login Items.

### Fixed

- The menu shows when it last read, not "updated just now", and the window's reset times
  stay current while it is open.
- Giving up on an interrupted switch shows as a note, not as a failure.
- A failure to change daily renewal or **Open Pitboard at login** shows in Settings.
- Switching one tool's account no longer hides advice about another tool's account that ran
  out.
- Advice to switch no longer offers an account that was forgotten, expired or ran out since.
- A read in flight when you switch no longer lands after the switch and shows the account
  before it.
- The window says why any account's numbers are not new, the one in use included.
- The menu and the window describe each account the same way, including how long a parked
  login lasts and how long the account in use lasts at its rate.

## [0.4.1] - 2026-09-26

### Changed

- Pitboard keeps one usage reading per account that only moves forward, so a session's older
  numbers no longer replace newer ones.
- The app's **Documentation** item opens docs.usepitboard.com.

### Fixed

- Every session on one account, `pitboard status` and the menu bar show the same, newest
  numbers. Add `"refreshInterval": 10` to the status line settings so idle sessions follow.
- A session still holding the previous account's numbers after a switch or a `/login` no
  longer records them as the new account's.
- The advice to switch stays for as long as the account in use is out. It went away at the
  app's next read.

## [0.4.0] - 2026-09-25

### Added

- **Settings** > **Advanced** shows which `pitboard` a terminal runs, and **Install command
  line tool…** links `/usr/local/bin/pitboard` to the app's own. It never replaces a
  `pitboard` someone else installed.
- An account whose parked login has expired has a **Sign in again** button in its row.

### Changed

- **Breaking:** In the tap, `pitboard` is the command line's cask, and the app's cask is
  `pitboard-app`.
  [Upgrade from 0.3.0 or earlier](https://docs.usepitboard.com/install/upgrade-from-0-3-0)
  has the commands to move.
- The app carries the command line inside it, so an update moves both. A copy of the app
  from a release updates itself as before.
- `brew install datlechin/tap/pitboard` installs the release's own command line as a cask,
  on macOS and Linux, without Rust or compiling.
- From the old formula, `brew update` warns that it did not install the cask, and Pitboard
  stays at 0.3.0. The two commands it prints leave the formula in front of the cask, so
  uninstall the formula first.
- From the old app cask, `brew update` replaces the app with the command line, once. Your
  settings and `~/.pitboard` stay.
- Leave `--zap` out when you remove the old app cask. Its zap moves `~/.pitboard` to the
  Trash, and Homebrew runs the zap of the cask as it was installed, whatever the tap says.
- Releases no longer publish a source tarball. The source is on crates.io and in the tag.
- In `pitboard-core`, `uninstall`'s report says whether the renewal schedule was removed,
  and it and `doctor::Facts` are `#[non_exhaustive]`.

### Fixed

- Daily renewal turned on from the app renews parked logins through the command line inside
  it, instead of starting a second copy of the app every day. Opening the app points a
  0.3.0 schedule at that command line.
- The app no longer turns daily renewal on from the temporary copy macOS runs before the app
  is in Applications.
- `pitboard doctor` fails, and **Settings** > **Advanced** > **Check this machine** says so,
  when the renewal schedule runs a `pitboard` that is gone or is an app, such as the
  formula's copy on Linux. Turn renewal off and on again in **Settings**, or run
  `pitboard schedule uninstall` and then `pitboard schedule install`.
- On Linux, a renewal schedule turned on from the command line keeps working after `brew
  upgrade`.
- `pitboard uninstall` removes the renewal schedule first, as `schedule_removed` in
  `--json`.
- Both casks remove the renewal schedule on `brew uninstall --zap` only. Neither touches
  `~/.pitboard`, which indexes the parked logins, so run `pitboard uninstall` before
  removing Pitboard.
- Advice about upgrading or removing Pitboard no longer assumes Homebrew.
- On Homebrew 6 and later, the app's cask installs on its own, since `pitboard-app` depends
  on nothing. It failed with `build.rb ... exited with 1` unless the formula was installed
  first.
- `cargo binstall pitboard` no longer falls back to a third party's build when it cannot
  fetch the release's.

## [0.3.0] - 2026-09-24

### Added

- Pitboard switches Codex accounts too: `pitboard enroll codex/work` records the Codex
  account signed in, `--sign-in` runs `codex login` in a private `CODEX_HOME`, `pitboard use
  codex/work` switches, and `pitboard` shows each account's five-hour and weekly limits.
- Pitboard handles Codex's default store, the `auth.json` file, and refuses its keychain
  stores with a reason. Until a Codex account is enrolled, Pitboard reads nothing of Codex's
  and asks OpenAI nothing.
- A Codex switch says that running `codex` sessions keep the old account until restarted,
  how many it found, and that signing out in one would revoke the login just parked. The app
  keeps saying so until that tool switches again.
- A Codex switch writes nothing over a login that a running `codex` renewed meanwhile, and
  refuses to park one that ends up naming two accounts.
- The app handles both tools. The panel groups accounts by tool, the menu bar follows the
  signed-in account closest to running out, and only an account of the same tool is offered
  when one runs out.
- **Add another account** asks which tool when both are installed, a Codex sign-in shows the
  address `codex login` printed, and cancelling a sign-in no longer holds the app.
- Labels belong to a tool: `work` can name a Claude Code and a Codex account, and
  `codex/work` says which. A bare name works as before, means Claude Code for a new account,
  and is refused as `label_ambiguous` where it matches both.
- A run killed while parking a login no longer leaves a live login that nothing names. The
  next command gives it back to its account or deletes it, and `pitboard doctor` reports any
  left.
- `pitboard adopt` takes over a `~/.pitboard` written on another computer, such as one moved
  by Migration Assistant, which other commands refuse. It keeps each account's label, email
  and usage and drops every parked login.
- `pitboard repair` finds every parked login in the keychain or vault and gives it back to
  its account, deletes it, or reports it. It deletes only logins this Pitboard recorded,
  since another `PITBOARD_HOME` may own the rest.
- On macOS, a login `repair` gave back that this Pitboard never recorded is deleted only
  once Pitboard has used it. `uninstall` says how many such logins it left, as `parks_left`
  in `--json`.
- A failure from asking Anthropic carries a cause beside its code, in `--json` and the app,
  such as `unreachable`, `rate_limited` or `login_refused`, and says whether asking again
  can help. `status` tells them apart too.
- The app follows a switch made elsewhere on the machine, such as in a terminal, within
  seconds. It showed the old account for up to five minutes.
- The app has a window with each account's usage history, the full change log and what
  `doctor` found, and a **Settings** window, opened with Command-comma, that can turn on
  scheduled renewal.
- On a first run, the app says what state the machine is in, such as no Claude Code, nobody
  signed in or one account only, and offers the next step itself. The first launch opens the
  window.
- `pitboard doctor` fails when anyone but the owner can read a stored login, lists what
  fills a login too large to store, largest first, and says when an account has gone unused
  longer than its refresh token lasts.
- The app shows the last measured numbers when Anthropic cannot be reached, not an empty
  panel. It can give up on an interrupted switch that recovery cannot finish, and shows
  every warning, not only the first.
- Pitboard keeps two weeks of each account's usage and says how long the account will last
  at its recent rate, or until its limit resets if that comes first. It says nothing until
  it has enough readings.
- `pitboard status` names the credential slot it reports on, which `CLAUDE_CONFIG_DIR`
  selects: always in `--json`, and in the report when it is not the default.
- `pitboard renew` renews every parked login that is due, and `pitboard schedule install`
  runs it on a schedule, through a LaunchAgent on macOS or a systemd user timer on Linux. It
  is opt-in and never switches.
- Pitboard asks Anthropic about an account again only once its tightest limit could have
  moved by a percentage point, and every front end waits out a refusal as long as
  `Retry-After` says. `pitboard doctor` says what is held back.
- `pitboard status --fresh` and the app's **Refresh** ask at once, but never through a wait
  the service asked for, a 429 with `Retry-After`.
- A parked Claude Code login keeps the account's other credential keys, such as its device
  token, and a switch back restores them. Before, an account came back to Claude Code with
  less than it left.
- Pitboard, the app included, warns when Claude Code's managed or user settings set an `env`
  block, an `apiKeyHelper` or a third-party provider that makes sessions ignore the login it
  switches. A custom OAuth endpoint set there refuses a switch.
- `pitboard doctor` shows the installed Claude Code version, read from disk without running
  it, beside the version Pitboard's facts about it were checked against.
- An interrupted switch is recovered without a network in most cases. It used to stop every
  command that changes anything until Anthropic could be asked.
- Each release artefact comes with a CycloneDX bill of materials, and the bills,
  `SHA256SUMS` and `appcast.xml` are attested like the artefacts.

### Changed

- The account index is schema 4, which earlier versions cannot read; they say which of the
  command line and the app to update. A schema 3 index is brought forward on first read.
- Messages name the tool, service and sign-in command of the account they are about, and
  qualify a name to type, such as `claude/work`, where a bare one would be ambiguous. Error
  codes are unchanged.
- **Breaking:** A Claude Code credential with no account in it, as `/logout` leaves, counts
  as nobody signed in. `use` and `enroll` exit 1 with `live_credential_absent`, not 3 with
  `live_credential_shape_unexpected`, and `doctor` warns instead of failing.
- In `--json`, `use`, enrolments and renewals gain `provider`, and `use` gains `adoption`,
  saying whether running sessions follow on their own or need restarting.
- In `pitboard status --json`, each account gains `provider` and `qualified`, such as
  `codex/work`. A login Pitboard cannot read shows as `login_unreadable`, and one it cannot
  switch, such as an API key, as `login_unusable`.
- `pitboard doctor` gains `environment.codex` and Codex checks whose codes start `codex_`. A
  Codex keychain store or API key login fails or warns only where it stands in the way of
  enrolled Codex accounts.
- `CLAUDE_CODE_CUSTOM_OAUTH_URL` refuses changes to Claude Code accounts only, and no longer
  stops a change to a Codex one.
- **Breaking:** `pitboard-core` is reorganised around a provider boundary. Build on
  `service::Pitboard` and `context::Context`; the code enums are `#[non_exhaustive]`,
  `state::save` is gone, and much else moved.
- `pitboard doctor` reports Claude Code's supervisor daemon, which also writes the login,
  and warns about the storage v5 flag only when it is on and the login is in the fallback
  store.
- README and SECURITY.md list every attested release file and how to check one with `gh
  attestation verify`.
- A failed read in the app shows its own warnings, not those of the last read that worked.
- Text in the app grows with the system text size without clipping. VoiceOver reads each
  account row as one item, says whether each check passed, and no longer calls the status
  item "speedometer".
- A login too large for `security`'s standard input is written as an argument, as Claude
  Code does, and the switch says so. `PITBOARD_NO_ARGV=1` refuses instead.
- Homebrew serves each release as it is published, not up to a day later, and the formula
  builds from a source tarball the release publishes and attests.

### Fixed

- The app finds `claude` and `codex` installed through a Node version manager such as nvm or
  volta, or a custom npm prefix, by asking the login shell for its `PATH`, and can start
  their sign-in.
- `pitboard enroll <label> --sign-in` for the account in use puts the new login in use, and
  `--json` says `in_use`. It parked the new login, and the next switch away deleted it.
- When Pitboard cannot confirm a new sign-in is in use, it parks the login rather than lose
  it and says why. Running `codex` sessions that keep the old login are counted and warned
  about.
- A renewal interrupted after writing the fresh login no longer loses that login on the next
  run.
- Recovering an interrupted switch from another `CODEX_HOME` or `CLAUDE_CONFIG_DIR` stops
  with `recovery_elsewhere` instead of comparing the switch with an unrelated login.
- A command refused over a mistyped name still finishes or undoes an interrupted switch,
  records it in `pitboard log` and reports it. It used to leave the switch for a later run
  without a word.
- Labels written by 0.1.x that contain a slash can be switched to, forgotten, renamed and
  signed in to again. They read as a tool prefix and were refused.
- Pitboard no longer asks about an account as often as once a minute because it did not know
  the limit kinds Anthropic sends, such as `session` and `weekly_all`. In `--json`, a window
  gains `length_seconds`.
- `pitboard doctor --json` no longer prints the signed-in email, organisation uuid, login
  name, a value derived from the refresh token or home paths with the username. Plain
  `pitboard doctor` is unchanged.
- Changing Claude Code's config no longer loses what Claude Code wrote to it meanwhile, such
  as project history. What Pitboard removes from it goes into `pitboard log`.
- A renewed login's expiry follows Anthropic's clock, not this machine's. A clock running
  ahead made Pitboard renew a parked login on every run, and one running behind installed a
  lapsed login.
- When Claude Code's config names a signed-in account but no store Pitboard reads holds its
  login, Pitboard refuses with `live_credential_elsewhere` instead of saying nothing is
  signed in.
- A switch checks the incoming login with Anthropic first: a refused login is dropped and
  the switch stops, one filed under the wrong account is refused, and an expired access
  token is renewed.
- A switch undone by a Claude Code `/logout` at the same moment no longer reports success.
  Both logins stay parked and it fails with `switch_did_not_hold`.
- A switch notices when the machine slept long enough for Claude Code to take back the
  credential lock, stops, and leaves that lock alone.
- A locked keychain no longer reads as a lost login that needs a new sign-in. The switch
  keeps every copy and fails with `switch_unverified`, and a later run finishes or undoes
  it.

### Security

- The crates.io release uses a token issued to the publish job and revoked when it ends, not
  a stored `CARGO_REGISTRY_TOKEN`, and publishing waits for a reviewer's approval.
- A release checks the update feed against the public key in the published app, not the
  private key that signed it, and refuses a new update key unless told to expect one.

## [0.2.0] - 2026-09-22

### Added

- `pitboard log` shows what Pitboard has changed and when, and which front end asked.
- `pitboard uninstall` deletes every parked login, then Pitboard's own files.
- `pitboard abandon` gives up on an interrupted switch that cannot be finished, keeping
  every login, for when recovery cannot reach Anthropic.
- `pitboard status --offline` answers from the last measured numbers without asking
  Anthropic or touching a login, and works with no network.
- `forget` asks before deleting a parked login, unless given `--yes` or `--json`.
- Each usage row in `--json` carries `severity`, Anthropic's own grade for that limit.
- The app shows each account's email, when each limit resets, when a parked login stops
  working, `doctor`'s checks on demand, its own version and a mark in the menu bar, with
  VoiceOver labels.
- The app can record the account in use, drop an account, and run Claude Code's sign-in for
  a new one, showing what that sign-in says.
- `cargo binstall pitboard` fetches the built binary instead of compiling.
- When the command line or the app meets a state file a newer version wrote, it says which
  of the two to update instead of offering to delete the accounts.
- The macOS command line binaries are signed and notarised like the app, and each release
  carries build provenance.

### Changed

- The minimum supported Rust version is 1.91.

### Fixed

- `pitboard statusline` typed at a prompt no longer waits for input. It reads stdin only
  when something is piped in.
- Offline, the account in use no longer shows as one with nothing parked, advising a sign-in
  it did not need.
- A switch that failed before installing anything no longer makes the next command announce
  a recovery.
- `enroll --sign-in` checks what could refuse the enrolment before the browser sign-in, not
  after.
- The status line shows numbers for every account, keeping the ones Claude Code hands it. It
  showed question marks for all but the account in use until someone ran `pitboard`.
- `doctor` and `forget` go by who is signed in, not by Pitboard's record of its last switch,
  so a sign-in with Claude Code's `/login` no longer misleads them.
- Three ways a parked login could be left in the keychain with nothing naming it are closed.
- A renewal that could not be written no longer leaves the account with a login already
  spent.
- A login past the keychain's size limit, as one with MCP server tokens can be, gets its own
  error giving the size, the limit and what to do.
- `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` raise a warning
  on every change, since Claude Code signs in with those instead of the login Pitboard
  moved.
- Columns line up in a terminal when a label is in Chinese or Japanese.
- One state file serves every credential slot, and an account switched to in one slot is no
  longer claimed in another.
- A limit scoped to one model no longer makes the menu bar read as though everything had
  stopped.
- Building on Windows stops with one sentence instead of a screen of type errors.

## [0.1.4] - 2026-09-22

### Fixed

- A switch no longer leaves the outgoing account's `trustedDeviceToken`, `organizationUuid`,
  `enterpriseGateway` or `designOauth` for the incoming account to present as its own.
  Pitboard removes them with the login, as a Claude Code logout does.
- Renewing a parked login whose answer has no refresh-token lifetime keeps the deadline it
  had, as Claude Code does. A parked login about to lapse looked as though it never expired.
- Pitboard refuses to act when `CLAUDE_CODE_CUSTOM_OAUTH_URL` is set, since Claude Code then
  keeps its login under another name and a switch would restore into an item nobody reads.

## [0.1.3] - 2026-09-22

### Added

- The menu bar panel says when an account has run out and which has the most left, whether
  or not notifications are allowed, and asks for permission only when it has something to
  say.
- After a switch, the panel counts down until sessions already open follow.
- A Homebrew tap: `brew install datlechin/tap/pitboard`, and `--cask` for the app.
- A release fails if its update feed is missing or unsigned, and a later job reads the feed
  back as an installed copy would.

### Fixed

- Pitboard run without `USER` set, as from launchd, cron or an app opened from Finder, reads
  the same keychain item as Claude Code instead of reporting no login.
- A parked login issued to another client is renewed as that client, not as the first-party
  one.
- The app's build number counts up with its version. 0.1.2 shipped the template's build
  number, which Sparkle would have read as newer than the next release.

## [0.1.2] - 2026-09-22

### Added

- A macOS menu bar app: the account in use and its tightest limit in the menu bar, every
  account's limits in the panel, one-click switching, a notification when an account runs
  out, and launch at login.
- `status` renews a parked login whose access token has expired, so every account's usage is
  live and parked logins no longer lapse. A login Anthropic refuses is dropped, with the
  command that signs in to it again.

## [0.1.1] - 2026-09-21

### Fixed

- `enroll --sign-in` no longer holds Pitboard's lock while the browser sign-in waits, so
  `use`, `forget` and `rename` work meanwhile; a second sign-in is refused.
- `enroll --sign-in --json` prints exactly one JSON line, with Claude Code's sign-in output
  on stderr.

## [0.1.0] - 2026-09-21

### Added

- `enroll`, `use`, `forget`, `status`, `doctor` and `statusline`.
- Live usage for every enrolled account in `status`, with which accounts can be switched to
  and until when.
- `enroll <label> --sign-in` on an enrolled label renews its parked login.
- `rename` changes an account's label.
- A switch refuses a parked login that has expired.
- `doctor` checks every parked login and reports an interrupted switch.
- `--json` on every command, with a versioned envelope and stable error codes, even for a
  mistyped command line.
- Shell completions and a man page.
- Linux support, with parked logins in a file vault, not yet tried against a signed-in
  Claude Code there.
- An audit log of every change Pitboard makes.
- Schema 3: one parked login per account, with its expiry.

[Unreleased]: https://github.com/datlechin/pitboard/compare/v0.13.0...HEAD
[0.13.0]: https://github.com/datlechin/pitboard/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/datlechin/pitboard/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/datlechin/pitboard/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/datlechin/pitboard/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/datlechin/pitboard/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/datlechin/pitboard/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/datlechin/pitboard/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/datlechin/pitboard/compare/v0.5.2...v0.6.0
[0.5.2]: https://github.com/datlechin/pitboard/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/datlechin/pitboard/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/datlechin/pitboard/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/datlechin/pitboard/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/datlechin/pitboard/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/datlechin/pitboard/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/datlechin/pitboard/compare/v0.1.4...v0.2.0
[0.1.4]: https://github.com/datlechin/pitboard/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/datlechin/pitboard/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/datlechin/pitboard/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/datlechin/pitboard/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/datlechin/pitboard/releases/tag/v0.1.0
