use super::super::harness::{HOME, Machine, NOW, WORK, desktop_machine, hold};
use super::super::{Outcome, enroll, forget, repair, settle, switch, update_config};
use crate::desktop_testing::{EMPTIED, JarAs, Person, plant, write_stand_in_jar};
use crate::error::Error;
use crate::host::Cookie;
use crate::provider::ProviderId;
use crate::service::{Permit, Warning};
use crate::state::{self, Key, Park, ParkKind, SetAside};
use crate::store::folder::Manifest;
use serde_json::{Map, Value, json};
use std::path::PathBuf;

const STRANGER: Person = Person {
    uuid: "0b7e2c44-2222-4d1f-8a35-91c0de77aa01",
    session: "stranger-session",
};

// `work`, in a session from before the one Claude holds.
const WORK_BEFORE: Person = Person {
    uuid: WORK.uuid,
    session: "work-session-before",
};

fn data(m: &Machine) -> PathBuf {
    m.ctx_home().join("claude/data")
}

fn settled(m: &Machine) -> super::super::Settled {
    settle(&m.ctx, Permit::for_a_test(), Some(ProviderId::Desktop))
        .expect("nothing to recover")
        .0
}

fn enrol(m: &Machine, label: &str) -> crate::error::Result<(super::Enrolled, Vec<Warning>)> {
    enroll(settled(m), &m.key(label), None)
}

fn use_(m: &Machine, label: &str) -> crate::error::Result<Outcome> {
    switch(settled(m), &m.key(label)).map(|(outcome, _)| outcome)
}

fn forget_(m: &Machine, label: &str) -> crate::error::Result<String> {
    forget(settled(m), &m.key(label)).map(|(email, _)| email)
}

fn confirm_(m: &Machine, label: &str) -> crate::error::Result<Outcome> {
    update_config(settled(m), &m.key(label)).map(|(outcome, _)| outcome)
}

fn code<T: std::fmt::Debug>(result: crate::error::Result<T>) -> &'static str {
    result.expect_err("refused").code()
}

// Claude's settings with `key` set to `value`, or taken out, in place.
fn set(m: &Machine, key: &str, value: Option<Value>) {
    let path = data(m).join("config.json");
    let mut settings: Map<String, Value> =
        serde_json::from_str(&std::fs::read_to_string(&path).expect("settings")).expect("JSON");
    match value {
        Some(value) => {
            settings.insert(key.into(), value);
        }
        None => {
            settings.shift_remove(key);
        }
    }
    std::fs::write(
        &path,
        serde_json::to_string_pretty(&settings).expect("JSON"),
    )
    .expect("set");
}

fn names(m: &Machine, who: &Person) {
    set(m, "lastKnownAccountUuid", Some(json!(who.uuid)));
}

fn session_of(who: Option<&Person>) -> Vec<Cookie> {
    who.map(|who| Cookie {
        host: ".claude.ai".into(),
        name: "sessionKey".into(),
        path: "/".into(),
        stored: [b"v10".as_slice(), who.session.as_bytes()].concat(),
        expires_utc: 0,
    })
    .into_iter()
    .collect()
}

// The jar holding `who`'s session, or none.
fn jar(m: &Machine, who: Option<&Person>) {
    write_stand_in_jar(&data(m).join("Cookies"), 24, &session_of(who));
}

// `work` and `home` enrolled, `work` in use: what two enrolments over one Claude leave.
fn two_accounts(name: &str) -> Machine {
    let m = desktop_machine(name);
    jar(&m, Some(&HOME));
    names(&m, &HOME);
    enrol(&m, "home").expect("`home` enrolled");
    jar(&m, Some(&WORK));
    names(&m, &WORK);
    enrol(&m, "work").expect("`work` in use again");
    m
}

// Every entry under `root`, by its path from there, with each file's bytes.
fn tree(root: &std::path::Path) -> Vec<(PathBuf, Option<Vec<u8>>)> {
    fn walk(
        root: &std::path::Path,
        dir: &std::path::Path,
        out: &mut Vec<(PathBuf, Option<Vec<u8>>)>,
    ) {
        let Ok(entries) = std::fs::read_dir(dir) else {
            return;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            let within = path
                .strip_prefix(root)
                .expect("under the root")
                .to_path_buf();
            if std::fs::symlink_metadata(&path).expect("metadata").is_dir() {
                out.push((within, None));
                walk(root, &path, out);
            } else {
                out.push((within, Some(std::fs::read(&path).expect("a file"))));
            }
        }
    }
    let mut found = Vec::new();
    walk(root, root, &mut found);
    found.sort();
    found
}

type Snapshot = (
    Vec<u8>,
    Vec<u8>,
    Vec<(PathBuf, Option<Vec<u8>>)>,
    Vec<String>,
);

// What a refusal must leave as it was: the index, `pending`, Claude's folder and `parks`.
fn snapshot(m: &Machine) -> Snapshot {
    let home = crate::home::dir(&m.ctx);
    (
        std::fs::read(home.join("state.json")).unwrap_or_default(),
        std::fs::read(home.join("pending")).unwrap_or_default(),
        tree(&data(m)),
        crate::store::folder::list(&m.ctx)
            .expect("parks")
            .unwrap_or_default(),
    )
}

fn items() -> &'static [&'static str] {
    match crate::provider::of(ProviderId::Desktop) {
        crate::provider::LoginKind::Folder(app) => app.items(),
        crate::provider::LoginKind::Document(_) => unreachable!("a folder tool"),
    }
}

// Claude's folder with no jar and none of an account's other items.
fn without_items(m: &Machine) {
    for item in items() {
        let path = data(m).join(item);
        match std::fs::symlink_metadata(&path) {
            Ok(found) if found.is_dir() => std::fs::remove_dir_all(&path).expect("an item"),
            Ok(_) => std::fs::remove_file(&path).expect("an item"),
            Err(_) => {}
        }
    }
}

fn in_use(m: &Machine) -> Option<String> {
    let state = state::load(&m.ctx).expect("state");
    state
        .account_in_use(ProviderId::Desktop)
        .map(|a| a.label.clone())
}

// No mover exists yet to make a folder park.
fn park_by_hand(m: &Machine, key: Option<&Key>, name: &str, who: &Person) -> Park {
    let folder = crate::store::folder::at(&m.ctx, name);
    std::fs::create_dir_all(folder.join("items")).expect("a park");
    write_stand_in_jar(&folder.join("items/Cookies"), 24, &session_of(Some(who)));
    let keys = format!(
        "{{\"oauth:tokenCache\": \"acct:{0}|t\", \"lastKnownAccountUuid\": \"{0}\"}}",
        who.uuid
    );
    std::fs::write(folder.join("keys.json"), &keys).expect("keys");
    let digests = crate::provider::folder::Keys::from_json(keys.as_bytes())
        .expect("keys")
        .digests();
    let manifest = Manifest {
        v: 1,
        tool: "desktop".into(),
        account: Some(format!("desktop_{}", who.uuid)),
        named: Some(who.uuid.into()),
        login: Some(who.login()),
        session_expires_at: None,
        parked_at: NOW,
        machine: m.ctx.this_machine(),
        items: vec!["Cookies".into()],
        keys: digests,
    };
    std::fs::write(
        folder.join("manifest.json"),
        serde_json::to_vec(&manifest).expect("JSON"),
    )
    .expect("manifest");
    let park = Park {
        service: name.into(),
        kind: ParkKind::Folder,
        parked_at: NOW,
        login: who.login(),
        access_expires_at: None,
        refresh_expires_at: None,
        session_expires_at: None,
    };
    if let Some(key) = key {
        let mut state = state::load(&m.ctx).expect("state");
        state.park(key, park.clone());
        state::save(&m.ctx, Permit::for_a_test(), &state).expect("saved");
    }
    park
}

fn park_name(who: &Person) -> String {
    crate::park::service_name(&format!("desktop_{}", who.uuid), NOW * 1000)
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn the_account_claude_is_signed_in_to_is_enrolled_and_nothing_moves() {
    let m = desktop_machine("enrolled");
    let twin = m.ctx_home().join("twin");
    plant(&twin, &WORK, JarAs::StandIn);
    assert_eq!(
        tree(&data(&m)),
        tree(&twin),
        "the enrolment moved nothing of Claude's"
    );
    let state = state::load(&m.ctx).expect("state");
    let work = state.get(&m.key("work")).expect("enrolled");
    assert_eq!(work.id, format!("desktop_{}", WORK.uuid));
    assert_eq!(work.email, "");
    assert_eq!(in_use(&m).as_deref(), Some("work"));
    let record = state.in_use(ProviderId::Desktop).expect("recorded");
    assert_eq!(record.login, WORK.login());
    assert_eq!(record.known_at, NOW);
    let before = snapshot(&m);
    assert!(matches!(
        enrol(&m, "work").expect("enrolled again"),
        (super::Enrolled::Current { .. }, _)
    ));
    assert_eq!(snapshot(&m).2, before.2, "nothing of Claude's moved");
    assert!(crate::store::folder::list(&m.ctx).expect("parks").is_none());
    hold(&m, "enrolled");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn use_and_forget_answer_as_this_commit_can() {
    let m = two_accounts("use");
    assert!(matches!(
        use_(&m, "work").expect("in use"),
        Outcome::AlreadyActive {
            config_updated: false,
            ..
        }
    ));
    assert!(matches!(
        confirm_(&m, "work").expect("in use"),
        Outcome::AlreadyActive { .. }
    ));
    let before = snapshot(&m);
    assert_eq!(code(confirm_(&m, "home")), "account_not_in_use");
    assert_eq!(code(use_(&m, "home")), "nothing_parked");
    assert_eq!(code(forget_(&m, "work")), "cannot_forget_active_account");
    assert_eq!(snapshot(&m), before);
    forget_(&m, "home").expect("forgotten");
    assert!(
        state::load(&m.ctx)
            .expect("state")
            .get(&m.key("home"))
            .is_none()
    );
    hold(&m, "forgotten");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_missing_jar_beside_an_item_or_a_key_is_never_signed_out() {
    let m = desktop_machine("no-jar");
    std::fs::remove_file(data(&m).join("Cookies")).expect("the jar taken away by hand");
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert!(
        refused
            .to_string()
            .contains("has no cookie store and still holds `Cookies-journal`"),
        "{refused}"
    );
    assert_eq!(code(use_(&m, "work")), "login_folder_unknown");
    assert_eq!(
        code(forget_(&m, "work")),
        "login_folder_unknown",
        "doubt rule"
    );
    assert_eq!(snapshot(&m), before);

    // No item at all, and the keys still in the settings.
    let m = desktop_machine("no-items");
    without_items(&m);
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert!(
        refused
            .to_string()
            .contains("has no cookie store and still holds `oauth:tokenCache`"),
        "{refused}"
    );
    assert_eq!(code(use_(&m, "work")), "login_folder_unknown");
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn no_data_folder_and_an_empty_one_hold_nobody() {
    // Row 5: no jar, none of the items, none of the three keys.
    let m = two_accounts("row-5");
    without_items(&m);
    for key in [
        "oauth:tokenCache",
        "oauth:tokenCacheV2",
        "lastKnownAccountUuid",
    ] {
        set(&m, key, None);
    }
    let before = snapshot(&m);
    assert_eq!(code(enrol(&m, "work")), "live_credential_absent");
    assert_eq!(code(use_(&m, "home")), "nothing_parked");
    assert_eq!(code(confirm_(&m, "work")), "account_not_in_use");
    assert_eq!(snapshot(&m), before);
    hold(&m, "row 5");

    // Row 3: no data folder. `forget` records nobody, and forgets any account.
    let m = two_accounts("row-3");
    std::fs::remove_dir_all(data(&m)).expect("the folder taken away by hand");
    let before = snapshot(&m);
    assert_eq!(code(enrol(&m, "work")), "live_credential_absent");
    let refused = use_(&m, "home").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert!(
        refused.to_string().contains("does not exist yet"),
        "{refused}"
    );
    assert_eq!(snapshot(&m), before);
    assert!(!data(&m).exists(), "nothing made it");
    forget_(&m, "work").expect("forgotten, though the record named it");
    let state = state::load(&m.ctx).expect("state");
    assert!(state.get(&m.key("work")).is_none());
    let record = state.in_use(ProviderId::Desktop).expect("recorded");
    assert!(record.owner.is_none() && record.login.is_empty());
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_data_folder_that_cannot_be_looked_at_is_never_no_data_folder() {
    use crate::host::fs::testing;
    let m = two_accounts("unseen-folder");
    let above = m.ctx_home().join("claude");
    let index = crate::home::dir(&m.ctx).join("state.json");
    let before = std::fs::read(&index).expect("state");
    testing::deny_reading(&above).expect("closed");
    // The system's administrator is denied nothing, so there is nothing to see there.
    if std::fs::symlink_metadata(data(&m)).is_ok() {
        testing::make_private(&above).expect("opened again");
        return;
    }
    let refused = enrol(&m, "work");
    let used = use_(&m, "home");
    let confirmed = confirm_(&m, "work");
    let forgotten = [forget_(&m, "work"), forget_(&m, "home")];
    testing::make_private(&above).expect("opened again");
    let refused = refused.expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert!(
        refused.to_string().contains("could not be looked at ("),
        "{refused}"
    );
    assert_eq!(code(used), "login_folder_unknown");
    assert_eq!(code(confirmed), "login_folder_unknown");
    for forgotten in forgotten {
        assert_eq!(code(forgotten), "login_folder_unknown", "doubt rule");
    }
    assert_eq!(std::fs::read(&index).expect("state"), before);
    hold(&m, "unseen folder");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_jar_that_does_not_read_this_time_refuses_every_change() {
    let m = two_accounts("unsettled");
    m.mem.jar_unsettled(&data(&m).join("Cookies"));
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unreadable");
    assert_eq!(refused.exit_code(), 1);
    assert!(
        refused.to_string().contains("(a test held the jar)"),
        "{refused}"
    );
    assert_eq!(code(use_(&m, "home")), "login_folder_unreadable");
    assert_eq!(code(confirm_(&m, "work")), "login_folder_unreadable");
    assert_eq!(
        code(forget_(&m, "work")),
        "login_folder_unreadable",
        "doubt rule"
    );
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn no_session_cookie_beside_live_tokens_is_never_signed_out() {
    let m = desktop_machine("live-tokens");
    jar(&m, None);
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert!(
        refused
            .to_string()
            .contains("holds no session and still holds a login's tokens"),
        "{refused}"
    );
    assert_eq!(snapshot(&m), before);
    // Both caches emptied, the name kept: what a Log out leaves, which is signed out.
    for key in ["oauth:tokenCache", "oauth:tokenCacheV2"] {
        set(&m, key, Some(serde_json::from_str(EMPTIED).expect("JSON")));
    }
    let before = snapshot(&m);
    assert_eq!(code(enrol(&m, "work")), "live_credential_absent");
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_session_with_no_name_is_a_sign_in_not_finished() {
    let m = desktop_machine("no-name");
    jar(&m, Some(&HOME));
    set(&m, "lastKnownAccountUuid", None);
    let before = snapshot(&m);
    for label in ["work", "home"] {
        assert_eq!(code(enrol(&m, label)), "sign_in_unfinished", "{label}");
    }
    assert_eq!(code(use_(&m, "work")), "sign_in_unfinished");
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_session_beside_emptied_caches_is_never_taken_as_named() {
    for (named, label) in [(&WORK, "work"), (&HOME, "home"), (&STRANGER, "stranger")] {
        let m = two_accounts(&format!("emptied-{label}"));
        jar(&m, Some(&STRANGER));
        names(&m, named);
        for key in ["oauth:tokenCache", "oauth:tokenCacheV2"] {
            set(&m, key, Some(serde_json::from_str(EMPTIED).expect("JSON")));
        }
        let before = snapshot(&m);
        for under in ["work", "home", "stranger"] {
            assert_eq!(
                code(enrol(&m, under)),
                "sign_in_unfinished",
                "named {label}, enrolled under {under}"
            );
        }
        assert_eq!(code(use_(&m, "home")), "sign_in_unfinished", "{label}");
        assert_eq!(snapshot(&m), before, "{label}");
    }
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn the_account_claude_names_is_not_filed_without_enroll() {
    // Row 14: the recorded session, named for another enrolled account.
    let m = two_accounts("row-14");
    names(&m, &HOME);
    let row_14 = snapshot(&m);
    for label in ["work", "home"] {
        assert_eq!(code(use_(&m, label)), "login_in_doubt", "14: use {label}");
    }
    assert_eq!(code(confirm_(&m, "work")), "account_not_in_use");
    assert_eq!(
        code(forget_(&m, "home")),
        "login_in_doubt",
        "14: doubt rule"
    );
    assert_eq!(snapshot(&m), row_14);

    // Row 15: the recorded session, named for an account nobody enrolled.
    let m = two_accounts("row-15");
    names(&m, &STRANGER);
    let before = snapshot(&m);
    let refused = use_(&m, "home").expect_err("refused");
    assert_eq!(refused.code(), "login_in_doubt");
    assert_eq!(refused.exit_code(), 3);
    assert!(
        refused
            .to_string()
            .contains("an account Pitboard has not enrolled"),
        "{refused}"
    );
    assert_eq!(snapshot(&m), before);

    // Row 17: a session not seen, named for the account in use, which holds a park.
    let m = two_accounts("row-17");
    park_by_hand(&m, Some(&m.key("work")), &park_name(&WORK), &WORK);
    jar(&m, Some(&STRANGER));
    let before = snapshot(&m);
    let refused = use_(&m, "home").expect_err("refused");
    assert_eq!(refused.code(), "login_in_doubt");
    assert_eq!(refused.exit_code(), 3);
    assert!(
        refused.to_string().contains("outside Pitboard"),
        "{refused}"
    );
    assert_eq!(code(forget_(&m, "work")), "login_in_doubt");
    assert_eq!(snapshot(&m), before);

    // Row 18: a session not seen, named for an enrolled account not in use.
    let m = two_accounts("row-18");
    jar(&m, Some(&STRANGER));
    names(&m, &HOME);
    let before = snapshot(&m);
    assert_eq!(code(use_(&m, "work")), "login_in_doubt");
    assert_eq!(snapshot(&m), before);

    // Row 19: a session not seen, named for an account nobody enrolled.
    let m = two_accounts("row-19");
    jar(&m, Some(&STRANGER));
    names(&m, &STRANGER);
    let before = snapshot(&m);
    assert_eq!(code(use_(&m, "home")), "live_account_not_enrolled");
    assert_eq!(snapshot(&m), before);

    // Row 20: a session a park of this home holds.
    let m = two_accounts("row-20");
    park_by_hand(&m, Some(&m.key("home")), &park_name(&HOME), &HOME);
    jar(&m, Some(&HOME));
    names(&m, &HOME);
    let before = snapshot(&m);
    assert_eq!(code(use_(&m, "home")), "login_in_doubt");
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn enroll_files_what_claude_names_and_sets_an_older_park_aside() {
    // Row 14: filed as the named account's, which is said.
    let m = two_accounts("file-14");
    names(&m, &HOME);
    let before = snapshot(&m);
    assert_eq!(code(enrol(&m, "work")), "label_taken");
    assert_eq!(code(enrol(&m, "other")), "already_enrolled");
    assert_eq!(snapshot(&m), before);
    let (_, warnings) = enrol(&m, "home").expect("filed as `home`'s");
    assert!(
        warnings.iter().any(|w| w.code() == "login_taken_as_named"),
        "{warnings:?}"
    );
    assert_eq!(in_use(&m).as_deref(), Some("home"));
    let state = state::load(&m.ctx).expect("state");
    assert_eq!(
        state.in_use(ProviderId::Desktop).expect("record").login,
        WORK.login()
    );
    assert!(
        state
            .get(&m.key("work"))
            .expect("kept")
            .replaced_at
            .is_some(),
        "`work`, with no park, is marked replaced"
    );
    hold(&m, "row 14");

    // Row 17: the account in use's older park set aside, never deleted.
    let m = two_accounts("file-17");
    let older = park_name(&WORK);
    park_by_hand(&m, Some(&m.key("work")), &older, &WORK);
    jar(&m, Some(&STRANGER));
    let before = snapshot(&m);
    assert_eq!(code(enrol(&m, "home")), "label_taken");
    assert_eq!(code(enrol(&m, "other")), "already_enrolled");
    assert_eq!(snapshot(&m), before);
    let (_, warnings) = enrol(&m, "work").expect("filed");
    let set_aside = warnings
        .iter()
        .find(|w| w.code() == "set_aside")
        .expect("said");
    assert!(
        set_aside.to_string().contains("deleted nothing"),
        "{set_aside}"
    );
    let state = state::load(&m.ctx).expect("state");
    assert!(state.get(&m.key("work")).expect("kept").parked.is_none());
    let aside = state
        .aside
        .iter()
        .find(|a| a.name == older)
        .expect("set aside");
    assert_eq!(aside.why, SetAside::Superseded);
    assert_eq!(
        aside.account.as_deref(),
        Some(format!("desktop_{}", WORK.uuid).as_str())
    );
    assert!(
        crate::store::folder::at(&m.ctx, &older)
            .join("items/Cookies")
            .exists()
    );
    assert_eq!(
        state.in_use(ProviderId::Desktop).expect("record").login,
        STRANGER.login()
    );
    assert!(!state.discarded.iter().any(|d| d.service == older));
    hold(&m, "row 17");

    // Row 18: the named account's park set aside, the session filed under it.
    let m = two_accounts("file-18");
    let homes = park_name(&HOME);
    park_by_hand(&m, Some(&m.key("home")), &homes, &HOME);
    jar(&m, Some(&STRANGER));
    names(&m, &HOME);
    let (_, warnings) = enrol(&m, "home").expect("filed");
    assert!(
        warnings.iter().any(|w| w.code() == "set_aside"),
        "{warnings:?}"
    );
    assert_eq!(in_use(&m).as_deref(), Some("home"));
    assert!(
        state::load(&m.ctx)
            .expect("state")
            .aside
            .iter()
            .any(|a| a.name == homes)
    );

    // Row 20: the park that holds the same session set aside, the session filed.
    let m = two_accounts("file-20");
    let homes = park_name(&HOME);
    park_by_hand(&m, Some(&m.key("home")), &homes, &HOME);
    jar(&m, Some(&HOME));
    names(&m, &HOME);
    enrol(&m, "home").expect("filed");
    let state = state::load(&m.ctx).expect("state");
    assert_eq!(in_use(&m).as_deref(), Some("home"));
    assert!(state.aside.iter().any(|a| a.name == homes));
    assert!(state.get(&m.key("home")).expect("kept").parked.is_none());
    hold(&m, "row 20");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn nothing_is_read_or_changed_while_claude_runs_or_cannot_be_told() {
    let m = two_accounts("open");
    let claude = m.claude();
    claude.opens();
    let before = snapshot(&m);
    let asked = m.mem.jars_asked().len();
    assert_eq!(code(enrol(&m, "work")), "app_open");
    assert_eq!(code(use_(&m, "home")), "app_open");
    assert_eq!(code(confirm_(&m, "work")), "app_open");
    assert_eq!(
        code(forget_(&m, "work")),
        "app_open",
        "the account in use, by the doubt rule"
    );
    assert_eq!(m.mem.jars_asked().len(), asked, "no jar was opened");
    assert_eq!(snapshot(&m), before);

    claude.crashes();
    m.mem.without_a_process_list();
    assert_eq!(code(enrol(&m, "work")), "app_state_unknown");
    assert_eq!(code(use_(&m, "home")), "app_state_unknown");
    assert_eq!(m.mem.jars_asked().len(), asked);
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_refusal_names_what_of_claude_still_runs() {
    let m = desktop_machine("still");
    let claude = m.claude();
    claude.opens();
    let refused = enrol(&m, "work").expect_err("refused");
    assert!(
        matches!(&refused, Error::AppOpen { still, .. } if still.is_empty()),
        "{refused:?}"
    );
    assert!(
        refused.to_string().starts_with("Claude is open"),
        "{refused}"
    );

    claude.crashes();
    let ship_it = m
        .ctx_home()
        .join("claude/Claude.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt");
    m.mem
        .runs_at("ShipIt", &[ship_it.to_string_lossy().as_ref()]);
    let refused = enrol(&m, "work").expect_err("refused");
    let Error::AppOpen { still, .. } = &refused else {
        panic!("{refused:?}");
    };
    assert_eq!(still.len(), 1);
    assert!(still[0].starts_with("ShipIt (pid "), "{still:?}");
    assert!(refused.to_string().contains("ShipIt (pid"), "{refused}");
    assert!(
        refused.to_string().contains("Activity Monitor"),
        "{refused}"
    );
    assert_eq!(refused.exit_code(), 1);

    // A Claude that is not the test's own never counts.
    m.mem.runs_at("ShipIt", &[]);
    m.mem.runs_at(
        "Claude",
        &["/Applications/Claude.app/Contents/MacOS/Claude"],
    );
    enrol(&m, "work").expect("the developer's own Claude is not the test's");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_process_list_that_does_not_read_is_refused_with_why() {
    let m = desktop_machine("listless");
    m.mem.without_a_process_list();
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "app_state_unknown");
    assert!(
        refused
            .to_string()
            .contains("(a test withheld the process list)"),
        "{refused}"
    );
    assert!(!refused.to_string().contains("doctor"), "{refused}");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_second_home_is_refused_while_another_holds_the_folder() {
    let m = two_accounts("busy");
    let held = crate::host::fs::lock_dir(&data(&m)).expect("another Pitboard holds it");
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_busy");
    assert_eq!(code(use_(&m, "home")), "login_folder_busy");
    assert_eq!(snapshot(&m), before);
    held.unlock().expect("let go");
    enrol(&m, "work").expect("once the other has finished");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_sidecar_pitboard_cannot_read_refuses_every_change() {
    let m = desktop_machine("sidecar");
    std::fs::write(data(&m).join("config.json.journal"), "not a header\n{}").expect("sidecar");
    let before = snapshot(&m);
    let refused = enrol(&m, "work").expect_err("refused");
    assert_eq!(refused.code(), "login_folder_unknown");
    assert_eq!(refused.exit_code(), 3);
    assert!(
        refused.to_string().contains("config.json.journal"),
        "{refused}"
    );
    assert_eq!(snapshot(&m), before);
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn repair_gives_a_folder_back_where_its_manifest_its_keys_and_its_jar_agree() {
    let given = |name: &str, spoil: &dyn Fn(&Machine, &str)| {
        let m = two_accounts(name);
        let homes = park_name(&HOME);
        park_by_hand(&m, None, &homes, &HOME);
        spoil(&m, &homes);
        let reclaimed = repair(settled(&m)).expect("repaired");
        let state = state::load(&m.ctx).expect("state");
        let parked = state
            .get(&m.key("home"))
            .and_then(|a| a.parked.clone())
            .map(|p| p.service);
        assert!(
            crate::store::folder::at(&m.ctx, &homes).exists(),
            "{name}: never deleted"
        );
        hold(&m, name);
        (parked == Some(homes.clone()), reclaimed)
    };
    let (back, reclaimed) = given("repair-agrees", &|_, _| {});
    assert!(back, "{reclaimed:?}");
    // A listed item that is not under `items/`, the jar still there.
    let (back, reclaimed) = given("repair-item-gone", &|m, name| {
        let path = crate::store::folder::at(&m.ctx, name).join("manifest.json");
        let mut manifest: Value =
            serde_json::from_str(&std::fs::read_to_string(&path).expect("manifest")).expect("JSON");
        manifest["items"] = json!(["Cookies", "Local Storage"]);
        std::fs::write(&path, manifest.to_string()).expect("rewritten");
    });
    assert!(!back && reclaimed.set_aside.len() == 1, "{reclaimed:?}");
    // Another token cache under the same name: the manifest's digests are what differs.
    let (back, reclaimed) = given("repair-keys-differ", &|m, name| {
        std::fs::write(
            crate::store::folder::keys_file(&m.ctx, name),
            format!(
                "{{\"oauth:tokenCache\": \"other\", \"lastKnownAccountUuid\": \"{}\"}}",
                HOME.uuid
            ),
        )
        .expect("keys");
    });
    assert!(!back && reclaimed.set_aside.len() == 1, "{reclaimed:?}");
    // Parked on another Mac, whose key Claude's jar was written under.
    let (back, reclaimed) = given("repair-another-mac", &|m, name| {
        let path = crate::store::folder::at(&m.ctx, name).join("manifest.json");
        let mut manifest: Value =
            serde_json::from_str(&std::fs::read_to_string(&path).expect("manifest")).expect("JSON");
        manifest["machine"] = json!("another Mac");
        std::fs::write(&path, manifest.to_string()).expect("rewritten");
    });
    assert!(!back && reclaimed.set_aside.len() == 1, "{reclaimed:?}");
    // The jar holds another session than the manifest says: never the wrong account's.
    let (back, reclaimed) = given("repair-jar-differs", &|m, name| {
        let jar = crate::store::folder::item(&m.ctx, name, "Cookies");
        write_stand_in_jar(&jar, 24, &session_of(Some(&STRANGER)));
    });
    assert!(!back && reclaimed.set_aside.len() == 1, "{reclaimed:?}");
    // No keys file is a folder nothing names, not one that could not be read.
    let (back, reclaimed) = given("repair-no-keys", &|m, name| {
        std::fs::remove_file(crate::store::folder::keys_file(&m.ctx, name)).expect("keys");
    });
    assert!(!back && reclaimed.set_aside.len() == 1, "{reclaimed:?}");
    assert!(reclaimed.unreadable.is_empty(), "{reclaimed:?}");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn repair_gives_nothing_back_to_the_account_in_use() {
    // Row 17, then `enroll`: the older login is set aside, and `repair` leaves it there.
    let m = two_accounts("repair-row-17");
    let older = park_name(&WORK);
    park_by_hand(&m, Some(&m.key("work")), &older, &WORK_BEFORE);
    jar(&m, Some(&STRANGER));
    enrol(&m, "work").expect("filed");
    let reclaimed = repair(settled(&m)).expect("repaired");
    assert!(reclaimed.is_empty(), "{reclaimed:?}");
    let state = state::load(&m.ctx).expect("state");
    assert!(state.get(&m.key("work")).expect("kept").parked.is_none());
    let aside = state.aside.iter().find(|a| a.name == older);
    assert_eq!(aside.map(|a| a.why), Some(SetAside::Superseded));
    assert_eq!(in_use(&m).as_deref(), Some("work"));
    hold(&m, "row 17, enrolled, repaired");

    // A folder nothing names, of the account in use: set aside, whatever session it holds.
    let m = two_accounts("repair-in-use");
    park_by_hand(&m, None, &older, &WORK_BEFORE);
    let reclaimed = repair(settled(&m)).expect("repaired");
    assert!(reclaimed.given_back.is_empty(), "{reclaimed:?}");
    assert_eq!(reclaimed.set_aside, [older.as_str()]);
    let state = state::load(&m.ctx).expect("state");
    assert!(state.get(&m.key("work")).expect("kept").parked.is_none());
    let aside = state.aside.iter().find(|a| a.name == older).expect("aside");
    assert_eq!(aside.why, SetAside::Orphan);
    assert_eq!(aside.login, Some(WORK_BEFORE.login()));
    hold(&m, "an older login of the account in use");
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn an_aside_set_aside_while_the_machine_could_not_be_read_is_given_back() {
    let m = two_accounts("unknown-machine");
    let homes = park_name(&HOME);
    let park = park_by_hand(&m, None, &homes, &HOME);
    let mut state = state::load(&m.ctx).expect("state");
    state.aside.push(crate::state::Aside {
        name: homes.clone(),
        tool: ProviderId::Desktop,
        at: NOW,
        why: SetAside::AnotherMachine,
        account: Some(format!("desktop_{}", HOME.uuid)),
        login: Some(park.login.clone()),
    });
    state::save(&m.ctx, Permit::for_a_test(), &state).expect("saved");

    let blind = m.ctx.clone().with_unknown_machine();
    let (unknown, _) = settle(&blind, Permit::for_a_test(), None).expect("settled");
    repair(unknown).expect("repaired");
    let state = state::load(&m.ctx).expect("state");
    assert!(state.get(&m.key("home")).expect("kept").parked.is_none());
    assert!(state.aside.iter().any(|a| a.name == homes), "kept aside");

    repair(settled(&m)).expect("repaired");
    let state = state::load(&m.ctx).expect("state");
    assert_eq!(
        state
            .get(&m.key("home"))
            .and_then(|a| a.parked.as_ref())
            .map(|p| p.service.as_str()),
        Some(homes.as_str())
    );
    assert!(!state.aside.iter().any(|a| a.name == homes));
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_name_that_is_no_account_id_is_never_enrolled() {
    for (at, name, refusal, exit) in [
        (0, "", "sign_in_unfinished", 1),
        (1, "../x", "login_folder_unknown", 3),
        (2, "a b", "login_folder_unknown", 3),
    ] {
        let m = two_accounts(&format!("odd-name-{at}"));
        jar(&m, Some(&STRANGER));
        set(&m, "lastKnownAccountUuid", Some(json!(name)));
        let before = snapshot(&m);
        let refused = enrol(&m, "other").expect_err("refused");
        assert_eq!(
            (refused.code(), refused.exit_code()),
            (refusal, exit),
            "{name:?}: {refused}"
        );
        if exit == 3 {
            assert!(
                refused
                    .to_string()
                    .contains("whose `lastKnownAccountUuid` is not an account id"),
                "{refused}"
            );
        }
        assert_eq!(code(use_(&m, "home")), refusal, "{name:?}");
        assert_eq!(snapshot(&m), before, "{name:?}");
    }
}

// Row 4.
#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn a_linked_data_folder_or_a_third_party_one_beside_it_refuses_every_change() {
    let refuses = |name: &str, spoil: &dyn Fn(&Machine), words: &str| {
        let m = two_accounts(&format!("row-4-{name}"));
        spoil(&m);
        let before = snapshot(&m);
        let refused = enrol(&m, "work").expect_err("refused");
        assert_eq!(
            (refused.code(), refused.exit_code()),
            ("login_folder_unknown", 3),
            "{name}: {refused}"
        );
        assert!(refused.to_string().contains(words), "{refused}");
        assert_eq!(code(use_(&m, "home")), "login_folder_unknown", "{name}");
        assert_eq!(code(confirm_(&m, "work")), "login_folder_unknown", "{name}");
        assert_eq!(
            code(forget_(&m, "work")),
            "login_folder_unknown",
            "{name}: doubt rule"
        );
        assert_eq!(snapshot(&m), before, "{name}");
    };
    refuses(
        "third-party",
        &|m| {
            std::fs::create_dir_all(m.ctx_home().join("claude/data-3p")).expect("a folder beside");
        },
        "has a `Claude-3p` folder beside it, which a third-party deployment of Claude uses",
    );
    refuses(
        "link",
        &|m| {
            let real = m.ctx_home().join("claude/real");
            std::fs::rename(data(m), &real).expect("moved");
            crate::host::fs::testing::link_dir(&real, &data(m)).expect("linked");
        },
        "data folder is a link",
    );
}

#[test]
#[cfg_attr(
    windows,
    ignore = "Claude Desktop is macOS only: Windows refuses its folder calls"
)]
fn forget_and_uninstall_delete_the_folders_they_are_asked_to() {
    use crate::host::fs::testing;
    let m = two_accounts("deletes");
    let homes_park = park_name(&HOME);
    let (homes_aside, works_aside, nobodys) =
        ("pitboard-aside-1", "pitboard-aside-2", "pitboard-aside-3");
    park_by_hand(&m, Some(&m.key("home")), &homes_park, &HOME);
    let mut state = state::load(&m.ctx).expect("state");
    for (name, filed_under) in [
        (homes_aside, Some(&HOME)),
        (works_aside, Some(&WORK)),
        (nobodys, None),
    ] {
        let kept = park_by_hand(&m, None, name, &STRANGER);
        state.aside.push(crate::state::Aside {
            name: name.into(),
            tool: ProviderId::Desktop,
            at: NOW,
            why: SetAside::Superseded,
            account: filed_under.map(|who| format!("desktop_{}", who.uuid)),
            login: Some(kept.login),
        });
    }
    state::save(&m.ctx, Permit::for_a_test(), &state).expect("saved");
    testing::make_private(&crate::home::dir(&m.ctx)).expect("private");
    testing::make_private(&crate::store::folder::parks_dir(&m.ctx)).expect("private");
    let claudes = tree(&data(&m));
    let jar_of = |name: &str| crate::store::folder::item(&m.ctx, name, "Cookies");

    forget_(&m, "home").expect("forgotten");
    assert!(!crate::store::folder::at(&m.ctx, &homes_park).exists());
    assert!(!crate::store::folder::at(&m.ctx, homes_aside).exists());
    assert!(jar_of(works_aside).exists(), "another account's stays");
    assert!(jar_of(nobodys).exists(), "and nobody's");
    let state = state::load(&m.ctx).expect("state");
    assert!(state.discarded.is_empty(), "{:?}", state.discarded);
    assert_eq!(state.aside.len(), 2);
    assert_eq!(tree(&data(&m)), claudes);
    hold(&m, "forgotten");

    let removed = super::super::uninstall(settled(&m)).expect("uninstalled");
    assert_eq!(
        (removed.set_aside, removed.parks, removed.pending),
        (2, 0, 0)
    );
    assert!(removed.home_removed);
    assert!(!crate::home::dir(&m.ctx).exists());
    assert_eq!(tree(&data(&m)), claudes, "Claude's folder is as it was");
}

#[test]
fn the_desktop_provider_reaches_no_keychain_and_no_service() {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("src");
    for dir in ["provider/desktop", "switch/folder"] {
        for entry in std::fs::read_dir(root.join(dir)).expect("sources") {
            let path = entry.expect("an entry").path();
            if path
                .file_name()
                .is_some_and(|name| name == "tests.rs" || name == "assumptions.rs")
            {
                continue;
            }
            let source = std::fs::read_to_string(&path).expect("a source");
            for line in source
                .lines()
                .filter(|line| !line.trim_start().starts_with("//"))
            {
                for needle in [
                    "security",
                    "keychain",
                    "vault_",
                    "ureq",
                    "crate::api",
                    "crate::proxy",
                    "names::",
                ] {
                    assert!(
                        !line.contains(needle),
                        "{}: `{needle}` in {line:?}",
                        path.display()
                    );
                }
                if line.contains("crate::store::") || line.contains("use crate::store") {
                    assert!(
                        line.contains("store::fingerprint") || line.contains("store::folder"),
                        "{}: {line:?}",
                        path.display()
                    );
                }
            }
        }
    }
}
