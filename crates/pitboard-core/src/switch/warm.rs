//! Warming up an account, and changes to which accounts Pitboard warms up and when, under
//! the lock every change takes, so two front ends never warm up one account twice for one
//! period, nor write over each other's setting.
//!
//! A warm-up is decided again under the lock, from the files as they are then, and its attempt
//! recorded before anything runs, so however it ends, killed included, it counts against the
//! day's attempts. The lock is let go while the tool runs, which takes seconds, so no switch
//! waits on it, and taken again to record what it came to. A login is never handed on whole:
//! the tool gets its access token alone, renewed first where it would lapse during the run.

use super::{Result, Settled, identify, renew};
use crate::audit;
use crate::context::Context;
use crate::duty::ledger::Tries;
use crate::error::Error;
use crate::provider::{self, ProviderId};
use crate::service::Permit;
use crate::state::Key;
use crate::warm::decide::{self, Asked, Ledger, Login, Standing};
use crate::warm::run::{self, Verdict};
use crate::warm::{self, Failure, Range};
use crate::{park, readings, status};
use serde_json::Value;

/// How long ahead of its expiry an access token is renewed before it is handed on: well past
/// what a run takes.
const AHEAD_SECONDS: i64 = 300;

/// What a warm-up came to.
#[derive(Debug, Clone, PartialEq)]
pub enum WarmUp {
    /// `account`'s five-hour period started, as it is typed, and resets at `until` where a
    /// reading taken after the run said when.
    Started { account: String, until: Option<i64> },
    /// `account`'s period was running already, until `until`, so there was nothing to start.
    Running { account: String, until: i64 },
    /// No account turned on was due by the time the lock was held.
    Nothing,
    /// Nothing is due, as a look at the files alone says, with where each account stands.
    Stands(Box<warm::Overview>),
}

/// Which account a warm-up is of.
pub(crate) enum Target {
    /// The first one turned on that is due, as Pitboard does by itself.
    Due,
    /// This one, which somebody asked for now.
    Account(Key),
}

/// Warms up `target`, as [`crate::warm`] says when, and records what it came to in the
/// activity log as `warm-up` of the account, with `ok` or why it started nothing.
pub(crate) fn warm_up(settled: Settled, target: Target) -> Result<WarmUp> {
    let Settled {
        _exclusive,
        mut state,
        ctx,
        permit,
    } = settled;
    let ctx = &ctx;
    let now = ctx.now();
    let zone = ctx.zone();
    let setting = warm::load(ctx)?;
    let mut ledger = Ledger::load(ctx);
    let readings = readings::load(ctx);
    let in_use = warm::in_use(ctx, &state);
    let (key, asked) = match target {
        Target::Account(key) => (key, Asked::Now),
        Target::Due => {
            let due = decide::standings(
                &state,
                &setting,
                in_use.as_ref(),
                &readings,
                &ledger,
                now,
                &zone,
            )
            .into_iter()
            .find(|(_, standing)| *standing == Standing::Due)
            .map(|(account, _)| account.key());
            match due {
                Some(key) => (key, Asked::ByItself),
                None => return Ok(WarmUp::Nothing),
            }
        }
    };
    let label = state.typed(&key);
    let account = state
        .get(&key)
        .cloned()
        .ok_or_else(|| Error::AccountUnknown {
            label: label.clone(),
            enrolled: state.labels(key.provider),
        })?;
    if key.provider != ProviderId::Claude {
        return Err(Error::WarmUpUnsupported {
            tool: key.provider,
            label,
        });
    }
    let standing = decide::decide(
        Login::of(&account, in_use.as_ref(), now),
        readings.get(&account.id),
        ledger.get(&account.id),
        setting.range,
        asked,
        now,
        &zone,
    );
    match (standing, asked) {
        (Standing::Due, _) => {}
        (Standing::Running { until, .. }, Asked::Now) => {
            return Ok(WarmUp::Running {
                account: label,
                until,
            });
        }
        (other, Asked::Now) => return Err(not_due(label, &other, now)),
        (_, Asked::ByItself) => return Ok(WarmUp::Nothing),
    }
    let tool = provider::of(ProviderId::Claude);
    let overridden = warm::overridden(ctx);
    if !overridden.is_empty() {
        return Err(Error::WarmUpOverridden { names: overridden });
    }
    if tool.program(ctx).is_none() {
        return Err(Error::ProgramNotFound {
            tool: ProviderId::Claude,
        });
    }
    let (login, signed_in) = match account.parked.clone() {
        Some(mut held) => {
            if held
                .access_expires_at
                .is_some_and(|at| at <= now + AHEAD_SECONDS)
            {
                held =
                    renew::renew_one(ctx, permit, &mut state, &key, &held)?.ok_or_else(|| {
                        Error::WarmUpFailed {
                            label: label.clone(),
                            why: Failure::Unreachable,
                        }
                    })?;
            }
            (park::load(ctx, &key, &held)?, false)
        }
        None => {
            let (live, found) =
                identify::fresh(ctx, permit, &mut state, ProviderId::Claude, AHEAD_SECONDS)?;
            identify::keep(ctx, permit, &mut state, ProviderId::Claude, found)?;
            match live {
                identify::Live::Login(login) if account.owned_by(&login.owner) => {
                    (login.document, true)
                }
                _ => {
                    return Err(not_due(
                        label,
                        &Standing::Held(warm::Hold::NothingParked),
                        now,
                    ));
                }
            }
        }
    };
    let running = Running::claim(ctx, permit)?;
    let home = run::home(ctx, ProviderId::Claude);
    run::prepare(permit, &home).map_err(|source| Error::HomeUnwritable {
        path: home.clone(),
        source,
    })?;
    let headless =
        tool.warm_up(ctx, permit, &home, &login)
            .map_err(|error| Error::WarmUpFailed {
                label: label.clone(),
                why: Failure::Failed(error.to_string()),
            })?;
    ledger.of(&account.id, now, &zone).tries.attempt(now);
    ledger.save(ctx, permit)?;
    // The tool runs for seconds; nothing else waits on Pitboard's lock meanwhile.
    drop(_exclusive);

    let came = match run::run(ctx, headless) {
        Ok(ran) if ran.timed_out => Err(Failure::TimedOut),
        Ok(ran) => match tool.warm_verdict(&ran) {
            Verdict::Answered => started(ctx, permit, &account.id, &login, signed_in),
            Verdict::LimitReached => {
                // So what holds it back from now on is the reading's, which says until when.
                let _ = status::ask_one(
                    ctx,
                    permit,
                    ProviderId::Claude,
                    &account.id,
                    &login,
                    signed_in,
                );
                Err(Failure::LimitReached)
            }
            Verdict::LoginRefused => Err(Failure::LoginRefused),
            Verdict::Unreachable => Err(Failure::Unreachable),
            Verdict::Failed(said) => Err(Failure::Failed(said)),
        },
        Err(error) => Err(Failure::Failed(error.to_string())),
    };
    drop(running);
    record(ctx, permit, &account.id, &label, now, &came)?;
    match came {
        Ok(until) => Ok(WarmUp::Started {
            account: label,
            until,
        }),
        Err(why) => Err(Error::WarmUpFailed { label, why }),
    }
}

/// Whether the run started the period: the reset of the account's five-hour limit, as a reading
/// asked for after it says, where one could be asked for. A reading that still has no period
/// running says it started nothing.
fn started(
    ctx: &Context,
    permit: Permit,
    id: &str,
    login: &Value,
    signed_in: bool,
) -> std::result::Result<Option<i64>, Failure> {
    let Some(reading) = status::ask_one(ctx, permit, ProviderId::Claude, id, login, signed_in)
    else {
        return Ok(None);
    };
    decide::startable(&reading)
        .and_then(|session| session.resets_at)
        .filter(|&at| at > ctx.now())
        .map(Some)
        .ok_or(Failure::NotStarted)
}

/// What a warm-up of the account with `id`, begun at `began`, came to, kept under the lock
/// again, and written in the activity log.
fn record(
    ctx: &Context,
    permit: Permit,
    id: &str,
    label: &str,
    began: i64,
    came: &std::result::Result<Option<i64>, Failure>,
) -> Result<()> {
    let _exclusive = super::exclusive(ctx, permit)?;
    let mut ledger = Ledger::load(ctx);
    let warmed = ledger.of(id, ctx.now(), &ctx.zone());
    match came {
        Ok(_) => {
            warmed.tries = Tries::default();
            warmed.started = Some(began);
        }
        Err(why) if why.mended_by_waiting() => warmed.tries.waited(),
        Err(_) => {}
    }
    let saved = ledger.save(ctx, permit);
    let code = match came {
        Ok(_) => "ok",
        Err(why) => why.code(),
    };
    audit::record(ctx, permit, "warm-up", label, code);
    saved
}

/// Why `label` is not warmed up when somebody asked for it now.
fn not_due(label: String, standing: &Standing, now: i64) -> Error {
    let code = match standing {
        Standing::Held(hold) => hold.code(),
        _ => "warm_up_not_due",
    };
    Error::WarmUpNotDue {
        label,
        why: crate::words::warm_standing(standing, &|at| crate::time::moment(at, now)),
        code,
    }
}

/// One warm-up's run at a time on this machine, held for as long as it runs, by whichever
/// front end started it: two runs would share the tool's private home.
struct Running {
    _lock: std::fs::File,
}

impl Running {
    fn claim(ctx: &Context, permit: Permit) -> Result<Running> {
        let path = crate::home::dir(ctx).join("warm.lock");
        let file = crate::host::fs::open_private_lock(permit, &path)
            .map_err(|source| Error::HomeUnwritable { path, source })?;
        file.try_lock().map_err(|_| Error::WarmUpInProgress)?;
        Ok(Running { _lock: file })
    }
}

/// Warms up `key`'s account, or stops. Whether that changed anything.
pub fn turn(settled: Settled, key: &Key, on: bool) -> Result<bool> {
    let Settled {
        _exclusive,
        state,
        ctx,
        permit,
    } = settled;
    let account = state.get(key).ok_or_else(|| Error::AccountUnknown {
        label: key.typed(),
        enrolled: state.labels(key.provider),
    })?;
    if on && key.provider != ProviderId::Claude {
        return Err(Error::WarmUpUnsupported {
            tool: key.provider,
            label: key.typed(),
        });
    }
    let mut setting = warm::load(&ctx)?;
    let changed = setting.turn(&account.id, on);
    if changed {
        warm::save(&ctx, permit, &setting)?;
    }
    Ok(changed)
}

/// Warms up within `range` of each day.
pub fn range(settled: Settled, range: Range) -> Result<Range> {
    let Settled {
        _exclusive,
        ctx,
        permit,
        ..
    } = settled;
    let mut setting = warm::load(&ctx)?;
    if setting.range != range {
        setting.range = range;
        warm::save(&ctx, permit, &setting)?;
    }
    Ok(range)
}

#[cfg(test)]
mod tests {
    use super::super::harness::{audit_lines, codex_account, machine};
    use crate::error::Error;
    use crate::service::{Permit, Pitboard};
    use crate::state;
    use crate::warm::{self, Range};

    #[test]
    fn a_claude_code_account_is_turned_on_and_off_and_each_is_recorded() {
        let m = machine("warm-turn");
        let pitboard = Pitboard::new(m.ctx.clone());
        let done = pitboard.warm_turn("there", true).expect("turned on");
        assert!(done.value, "it changed");
        let again = pitboard.warm_turn("there", true).expect("turned on");
        assert!(!again.value, "already on");

        let setting = warm::load(&m.ctx).expect("readable");
        assert!(setting.is_on("there"));
        assert_eq!(setting.range, Range::DEFAULT);
        let overview = pitboard.warm_overview().expect("an overview");
        let on: Vec<(&str, bool)> = overview
            .accounts
            .iter()
            .map(|choice| (choice.account.as_str(), choice.on))
            .collect();
        assert_eq!(on, [("here", false), ("there", true)]);

        pitboard.warm_turn("there", false).expect("turned off");
        assert!(!warm::load(&m.ctx).expect("readable").any());
        assert_eq!(
            audit_lines(&m, "warm-on"),
            [
                ("there".to_owned(), "ok".to_owned()),
                ("there".to_owned(), "ok".to_owned())
            ]
        );
        assert_eq!(
            audit_lines(&m, "warm-off"),
            [("there".to_owned(), "ok".to_owned())]
        );
    }

    #[test]
    fn a_codex_account_is_not_warmed_up() {
        let m = machine("warm-codex");
        let mut state = state::load(&m.ctx).expect("state");
        state.accounts.push(codex_account("main", "main", None));
        state::save(&m.ctx, Permit::for_a_test(), &state).expect("saved");
        let pitboard = Pitboard::new(m.ctx.clone());
        let refused = pitboard.warm_turn("codex/main", true).expect_err("refused");
        assert_eq!(refused.error.code(), "warm_up_unsupported");
        assert!(!warm::path(&m.ctx).exists(), "nothing written");
        pitboard
            .warm_turn("codex/main", false)
            .expect("turning one off is always fine");
    }

    #[test]
    fn the_span_is_kept_and_recorded() {
        let m = machine("warm-range");
        let pitboard = Pitboard::new(m.ctx.clone());
        let range = Range::parse("07:30", "23:00").expect("a span");
        pitboard.warm_range(range).expect("kept");
        assert_eq!(warm::load(&m.ctx).expect("readable").range, range);
        assert_eq!(
            audit_lines(&m, "warm-range"),
            [("07:30-23:00".to_owned(), "ok".to_owned())]
        );
        let text = std::fs::read_to_string(warm::path(&m.ctx)).expect("written");
        assert_eq!(
            text,
            r#"{"version":1,"range":{"from":"07:30","to":"23:00"},"accounts":[]}"#
        );
    }

    #[test]
    fn a_forgotten_account_is_warmed_up_no_more() {
        let m = machine("warm-forget");
        let pitboard = Pitboard::new(m.ctx.clone());
        pitboard.warm_turn("there", true).expect("turned on");
        pitboard.forget("there").expect("forgotten");
        assert!(!warm::load(&m.ctx).expect("readable").any());
    }

    #[test]
    fn a_setting_a_newer_pitboard_wrote_is_neither_read_nor_written_over() {
        let m = machine("warm-newer");
        let path = warm::path(&m.ctx);
        std::fs::write(&path, r#"{"version":2,"days":["mon"]}"#).expect("written");
        let refused = warm::load(&m.ctx).expect_err("refused");
        assert!(matches!(
            refused,
            Error::StateFromNewerVersion {
                found: 2,
                expected: 1,
                ..
            }
        ));
        let pitboard = Pitboard::new(m.ctx.clone());
        assert!(pitboard.warm_turn("there", true).is_err());
        let kept = std::fs::read_to_string(&path).expect("still there");
        assert!(kept.contains("days"), "{kept}");

        std::fs::write(&path, "{not json").expect("written");
        assert_eq!(
            warm::load(&m.ctx).expect_err("refused").code(),
            "warm_setting_corrupt"
        );
    }

    mod running {
        use super::super::super::harness::{
            Machine, NOW, audit_lines, lapsed, machine, owner, renews,
        };
        use super::super::WarmUp;
        use crate::duty::Look;
        use crate::service::{Permit, Pitboard};
        use crate::time::{Clock, FixedClock};
        use crate::usage::{Snapshot, Source, Window};
        use crate::warm::run::{Ran, Started, WarmScript};
        use crate::warm::{Range, Standing};
        use std::sync::{Arc, Mutex};

        const HOUR: i64 = 3_600;
        const ANSWERED: &str = r#"{"type":"result","subtype":"success","is_error":false,"result":"OK","api_error_status":null}"#;
        const LIMIT_REACHED: &str = r#"{"type":"result","subtype":"success","is_error":true,"api_error_status":429,"api_error":"usage_limit_reached","result":"You've hit your weekly limit"}"#;

        /// Claude Code's headless run, as a test plays it: what it prints, and every run it
        /// was started as.
        #[derive(Debug)]
        struct Script {
            exit: i32,
            prints: &'static str,
            runs: Mutex<Vec<Started>>,
        }

        impl WarmScript for Script {
            fn run(&self, started: &Started) -> Ran {
                self.runs.lock().expect("runs").push(started.clone());
                Ran {
                    exit: Some(self.exit),
                    stdout: self.prints.to_owned(),
                    timed_out: false,
                }
            }
        }

        impl Script {
            fn runs(&self) -> Vec<Started> {
                self.runs.lock().expect("runs").clone()
            }
        }

        fn window(kind: &str, percent: f64, resets_at: Option<i64>) -> Window {
            Window {
                kind: kind.into(),
                scope: None,
                percent,
                resets_at,
                is_active: true,
                severity: None,
                length_seconds: None,
            }
        }

        fn reading(session: Option<i64>, weekly: f64) -> Snapshot {
            Snapshot {
                windows: vec![
                    window("session", 0.0, session),
                    window("weekly_all", weekly, Some(NOW + 40 * HOUR)),
                ],
                observed_at: Some(NOW - 60),
                answered_at: Some(NOW - 60),
                lists_every_limit: true,
                source: Source::Live,
            }
        }

        /// Pitboard's reading of `id`: its five-hour period not running, with `weekly` used.
        fn idle(m: &Machine, id: &str, weekly: f64) {
            crate::readings::answered(
                &m.ctx,
                Permit::for_a_test(),
                &[(id.to_owned(), reading(None, weekly))],
            );
        }

        /// [`machine`], with `there` turned on for warm-ups, at 08:53 UTC, a Claude Code program
        /// Pitboard finds, and its run played by a script that exits `exit` printing `prints`.
        fn warming(name: &str, exit: i32, prints: &'static str) -> (Machine, Arc<Script>) {
            let mut m = machine(name);
            let program = m.ctx_home().join("bin").join("claude");
            std::fs::create_dir_all(program.parent().expect("a folder")).expect("bin");
            std::fs::write(&program, "#!/bin/sh\nexit 99\n").expect("a program");
            crate::host::fs::testing::make_runnable(&program).expect("runnable");
            let script = Arc::new(Script {
                exit,
                prints,
                runs: Mutex::new(Vec::new()),
            });
            let clock = FixedClock::at(NOW).in_zone(jiff::tz::TimeZone::UTC);
            m.ctx = m
                .ctx
                .clone()
                .with_claude_program(program)
                .with_clock(Arc::new(clock) as Arc<dyn Clock>)
                .with_warm_script(Arc::clone(&script) as Arc<dyn WarmScript>);
            Pitboard::new(m.ctx.clone())
                .warm_turn("there", true)
                .expect("turned on");
            (m, script)
        }

        fn warmed(m: &Machine) -> crate::service::Changing<WarmUp> {
            Pitboard::new(m.ctx.clone()).warm_up()
        }

        #[test]
        fn a_parked_account_due_is_warmed_up_with_its_access_token_alone() {
            let (m, script) = warming("warm-parked", 0, ANSWERED);
            idle(&m, "there", 20.0);
            m.api
                .using("access-there-refresh", reading(Some(NOW + 5 * HOUR), 20.0));

            let done = warmed(&m).expect("warmed up").value;
            assert_eq!(
                done,
                WarmUp::Started {
                    account: "there".into(),
                    until: Some(NOW + 5 * HOUR)
                }
            );
            let runs = script.runs();
            assert_eq!(runs.len(), 1);
            let run = &runs[0];
            assert_eq!(
                run.args,
                [
                    "-p",
                    "Reply with the single word OK.",
                    "--model",
                    "haiku",
                    "--tools",
                    "",
                    "--no-session-persistence",
                    "--strict-mcp-config",
                    "--disable-slash-commands",
                    "--output-format",
                    "json"
                ]
            );
            assert_eq!(run.token.as_deref(), Some("access-there-refresh"));
            let home = crate::home::dir(&m.ctx).join("warm").join("claude");
            let env = |name: &str| {
                run.env
                    .iter()
                    .find(|(key, _)| key == name)
                    .map(|(_, value)| value.clone())
            };
            assert_eq!(
                env("CLAUDE_CONFIG_DIR").map(std::path::PathBuf::from),
                Some(home.join("config"))
            );
            assert_eq!(
                env("HOME").map(std::path::PathBuf::from),
                Some(home.join("home"))
            );
            assert_eq!(
                env("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR").as_deref(),
                Some("3")
            );
            assert_eq!(run.dir.as_deref(), Some(home.join("work").as_path()));
            let allowed = [
                "PATH",
                "HOME",
                "TMPDIR",
                "LANG",
                "TERM",
                "NO_COLOR",
                "USER",
                "CLAUDE_CONFIG_DIR",
                "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR",
                "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC",
                "DISABLE_AUTOUPDATER",
            ];
            for (name, _) in &run.env {
                assert!(allowed.contains(&name.as_str()), "{name} reached the run");
            }
            assert!(
                !run.env
                    .iter()
                    .any(|(_, value)| value.contains("there-refresh")),
                "no refresh token is handed on"
            );
            assert_eq!(
                audit_lines(&m, "warm-up"),
                [("there".to_owned(), "ok".to_owned())]
            );

            let again = warmed(&m).expect("looked again").value;
            assert!(
                matches!(&again, WarmUp::Stands(overview) if overview.accounts.iter().any(|c| {
                    c.account == "there" && matches!(c.standing, Some(Standing::Running { .. }))
                })),
                "its period runs now: {again:?}"
            );
            assert_eq!(script.runs().len(), 1);
        }

        /// The account in use is warmed up with the login Claude Code has stored, renewed first
        /// under Claude Code's own locks where it would lapse during the run, and saved where
        /// Claude Code keeps it.
        #[test]
        fn the_account_in_use_is_warmed_up_with_its_login_renewed_first_where_it_lapses() {
            let (m, script) = warming("warm-in-use", 0, ANSWERED);
            Pitboard::new(m.ctx.clone())
                .warm_turn("here", true)
                .expect("turned on");
            Pitboard::new(m.ctx.clone())
                .warm_turn("there", false)
                .expect("turned off");
            m.sign_in(&lapsed("here-refresh"));
            renews(&m, "here-refresh", "here-renewed");
            m.api.owned_by("access-here-renewed", owner("here"));
            idle(&m, "here", 20.0);
            m.api
                .using("access-here-renewed", reading(Some(NOW + 5 * HOUR), 20.0));

            let done = warmed(&m).expect("warmed up").value;
            assert!(matches!(done, WarmUp::Started { ref account, .. } if account == "here"));
            assert_eq!(
                script.runs()[0].token.as_deref(),
                Some("access-here-renewed")
            );
            let live = m.live().expect("a login stored");
            assert_eq!(
                live.pointer("/claudeAiOauth/refreshToken"),
                Some(&serde_json::json!("here-renewed")),
                "the renewal is Claude Code's login from now on"
            );
        }

        #[test]
        fn a_limit_used_up_is_waited_on_and_not_counted() {
            let (m, _) = warming("warm-limit", 1, LIMIT_REACHED);
            idle(&m, "there", 20.0);
            m.api.using("access-there-refresh", reading(None, 100.0));
            let refused = warmed(&m).expect_err("refused");
            assert_eq!(refused.error.code(), "warm_up_limit_reached");
            let ledger = crate::warm::decide::Ledger::load(&m.ctx);
            let tries = &ledger.get("there").expect("kept").tries;
            assert_eq!((tries.attempts, tries.waits), (0, 1));
            assert_eq!(
                audit_lines(&m, "warm-up"),
                [("there".to_owned(), "warm_up_limit_reached".to_owned())]
            );
            let overview = Pitboard::new(m.ctx.clone())
                .warm_overview()
                .expect("an overview");
            let there = overview
                .accounts
                .iter()
                .find(|choice| choice.account == "there")
                .expect("there");
            assert!(
                matches!(
                    there.standing,
                    Some(Standing::Held(crate::warm::Hold::LimitUsedUp { .. }))
                ),
                "the reading after it says so: {:?}",
                there.standing
            );
        }

        #[test]
        fn a_request_answered_that_starts_nothing_counts_against_the_day() {
            let (m, _) = warming("warm-not-started", 0, ANSWERED);
            idle(&m, "there", 20.0);
            m.api.using("access-there-refresh", reading(None, 20.0));
            let refused = warmed(&m).expect_err("refused");
            assert_eq!(refused.error.code(), "warm_up_not_started");
            let ledger = crate::warm::decide::Ledger::load(&m.ctx);
            assert_eq!(ledger.get("there").expect("kept").tries.attempts, 1);
        }

        #[test]
        fn asked_for_now_a_running_period_is_said_and_a_held_one_refused() {
            let (m, script) = warming("warm-now", 0, ANSWERED);
            let pitboard = Pitboard::new(m.ctx.clone());
            crate::readings::answered(
                &m.ctx,
                Permit::for_a_test(),
                &[("there".to_owned(), reading(Some(NOW + HOUR), 20.0))],
            );
            assert_eq!(
                pitboard.warm_now("there").expect("said").value,
                WarmUp::Running {
                    account: "there".into(),
                    until: NOW + HOUR
                }
            );
            idle(&m, "there", 100.0);
            let held = pitboard.warm_now("there").expect_err("held");
            assert_eq!(held.error.code(), "limit_used_up");
            assert!(script.runs().is_empty());
        }

        #[test]
        fn nothing_is_warmed_up_outside_the_span() {
            let (m, script) = warming("warm-span", 0, ANSWERED);
            idle(&m, "there", 20.0);
            let pitboard = Pitboard::new(m.ctx.clone());
            pitboard
                .warm_range(Range::parse("20:00", "23:00").expect("a span"))
                .expect("kept");
            assert!(matches!(pitboard.warm_look(), Ok(Look::Stands(_))));
            assert!(matches!(
                pitboard.warm_up().expect("looked").value,
                WarmUp::Stands(_)
            ));
            assert!(script.runs().is_empty());
            pitboard
                .warm_range(Range::parse("06:00", "22:00").expect("a span"))
                .expect("kept");
            assert!(matches!(pitboard.warm_look(), Ok(Look::Act)));
        }
    }
}
