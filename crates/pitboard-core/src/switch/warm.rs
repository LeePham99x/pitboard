//! Changes to which accounts Pitboard warms up, and when, under the lock every change takes,
//! so two front ends turning accounts on at once never write over each other.

use super::{Result, Settled};
use crate::error::Error;
use crate::provider::ProviderId;
use crate::state::Key;
use crate::warm::{self, Range};

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
}
