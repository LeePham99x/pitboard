//! `pitboard warm`: which accounts Pitboard warms up, and when. What a warm-up is, and what it
//! never does, is [`pitboard_core::warm`]'s.

use crate::ui::{BOLD, DIM, columns, pad, paint};
use crate::{Report, changed};
use clap::Subcommand;
use pitboard_core::service::Pitboard;
use pitboard_core::switch::warm::WarmUp;
use pitboard_core::warm::{self, Hold, Overview, Range, Standing};
use pitboard_core::words;
use serde_json::{Value, json};

#[derive(Subcommand)]
pub enum WarmCommand {
    /// Start an account's five-hour period now, whatever the span, through one short request
    /// Claude Code makes with that account's login
    Now {
        /// The label the account was enrolled under, such as `work`
        label: String,
    },
    /// Warm up an account from now on, within the span `range` sets
    On {
        /// The label the account was enrolled under, such as `work`
        label: String,
    },
    /// Stop warming up an account
    Off {
        /// The label the account was enrolled under
        label: String,
    },
    /// Warm up accounts only between these times of each day, in this computer's time zone
    Range {
        /// When to start, such as 06:00
        #[arg(value_name = "FROM")]
        from: String,
        /// When to stop, such as 22:00; 24:00 is midnight, and an earlier time runs past it
        #[arg(value_name = "TO")]
        to: String,
    },
}

pub fn run(pitboard: &Pitboard, what: Option<&WarmCommand>) -> Report {
    match what {
        None => overview(pitboard),
        Some(WarmCommand::Now { label }) => now(pitboard, label),
        Some(WarmCommand::On { label }) => turn(pitboard, label, true),
        Some(WarmCommand::Off { label }) => turn(pitboard, label, false),
        Some(WarmCommand::Range { from, to }) => match Range::parse(from, to) {
            Ok(range) => range_set(pitboard, range),
            Err(why) => Report::failed(
                Some("warm"),
                pitboard_core::error::Error::Usage(format!("`{from} {to}`: {why}")),
            ),
        },
    }
}

fn range_json(range: Range) -> Value {
    json!({ "from": warm::clock(range.from()), "to": warm::clock(range.to()) })
}

fn overview(pitboard: &Pitboard) -> Report {
    let overview = match pitboard.warm_overview() {
        Ok(overview) => overview,
        Err(error) => return Report::failed(Some("warm"), error),
    };
    let data = json!({
        "range": range_json(overview.range),
        "accounts": overview.accounts.iter().map(|choice| {
            let mut account = json!({ "label": choice.account, "on": choice.on });
            if let Some(standing) = &choice.standing {
                merge(&mut account, standing_json(standing));
            }
            account
        }).collect::<Vec<_>>(),
    });
    Report::done("warm", data, said(&overview))
}

fn merge(into: &mut Value, from: Value) {
    if let (Some(into), Value::Object(from)) = (into.as_object_mut(), from) {
        into.extend(from);
    }
}

/// Where an account turned on stands, as `--json` gives it: `state`, with when its next
/// warm-up is due as `next_at` where that is known, and why it is held back as `reason`.
fn standing_json(standing: &Standing) -> Value {
    match standing {
        Standing::Due => json!({ "state": "due" }),
        Standing::Running { until, next } => {
            json!({ "state": "running", "until": until, "next_at": next })
        }
        Standing::Resting { at } => json!({ "state": "resting", "next_at": at }),
        Standing::Waiting { until } => json!({ "state": "waiting", "next_at": until }),
        Standing::Held(hold) => {
            let until = match hold {
                Hold::LimitUsedUp { limit } => limit.resets_at,
                Hold::GaveUp { until } => Some(*until),
                _ => None,
            };
            json!({ "state": "held", "reason": hold.code(), "until": until })
        }
    }
}

/// The span, and each Claude Code account with whether it is warmed up and where it stands,
/// as a person reads it.
fn said(overview: &Overview) -> String {
    if overview.accounts.is_empty() {
        return "No Claude Code account is enrolled, so there is nothing to warm up.\n".into();
    }
    let width = overview
        .accounts
        .iter()
        .map(|choice| columns(&choice.account))
        .max()
        .unwrap_or_default();
    let mut text = format!(
        "Pitboard warms up these accounts from {}, while the app or `pitboard watch` runs.\n",
        overview.range
    );
    let now = epoch();
    let moment = |at| pitboard_core::time::moment(at, now);
    for choice in &overview.accounts {
        let name = paint(BOLD, pad(&choice.account, width));
        match &choice.standing {
            Some(standing) => text.push_str(&format!(
                "  {name}  on   {}\n",
                words::warm_standing(standing, &moment)
            )),
            None if choice.on => text.push_str(&format!("  {name}  on\n")),
            None => text.push_str(&format!("  {name}  {}\n", paint(DIM, "off"))),
        }
    }
    if !overview.accounts.iter().any(|choice| choice.on) {
        text.push_str("Turn it on for an account with `pitboard warm on <account>`.\n");
    }
    text
}

/// Seconds since 1970, for writing each moment as a person reads it.
fn epoch() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |since| {
            i64::try_from(since.as_secs()).unwrap_or(i64::MAX)
        })
}

fn now(pitboard: &Pitboard, label: &str) -> Report {
    changed("warm", pitboard.warm_now(label), |warmed| {
        let moment = |at| pitboard_core::time::moment(at, epoch());
        match warmed {
            WarmUp::Started { account, until } => (
                json!({ "label": account, "outcome": "started", "until": until }),
                match until {
                    Some(until) => format!(
                        "Warmed up {}: its 5-hour period runs until {}.\n",
                        paint(BOLD, &account),
                        moment(until)
                    ),
                    None => format!(
                        "Warmed up {}. Anthropic has not said yet when its 5-hour period \
                         resets.\n",
                        paint(BOLD, &account)
                    ),
                },
            ),
            WarmUp::Running { account, until } => (
                json!({ "label": account, "outcome": "running", "until": until }),
                format!(
                    "{}'s 5-hour period runs already, until {}.\n",
                    paint(BOLD, &account),
                    moment(until)
                ),
            ),
            WarmUp::Nothing => (
                json!({ "label": label, "outcome": "nothing" }),
                "Nothing was warmed up.\n".to_owned(),
            ),
        }
    })
}

fn turn(pitboard: &Pitboard, label: &str, on: bool) -> Report {
    changed("warm", pitboard.warm_turn(label, on), |changed| {
        let human = match (on, changed) {
            (true, true) => format!(
                "Pitboard warms up {} from now on, within the span `pitboard warm` shows.\n",
                paint(BOLD, label)
            ),
            (true, false) => format!("{} was warmed up already.\n", paint(BOLD, label)),
            (false, true) => format!("Pitboard no longer warms up {}.\n", paint(BOLD, label)),
            (false, false) => format!("{} was not warmed up.\n", paint(BOLD, label)),
        };
        (
            json!({ "label": label, "on": on, "changed": changed }),
            human,
        )
    })
}

fn range_set(pitboard: &Pitboard, range: Range) -> Report {
    changed("warm", pitboard.warm_range(range), |range| {
        (
            json!({ "range": range_json(range) }),
            format!("Pitboard warms up accounts from {range}.\n"),
        )
    })
}
