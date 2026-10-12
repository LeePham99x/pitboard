//! `pitboard warm`, run as a person runs it, with a stand-in for Claude Code's headless mode
//! started for real: what it is started as, what it is handed, and what Pitboard records.

mod common;

use common::{Env, os, put_stand_in, two_accounts};
use pitboard_core::stand_in::{Script, Step};

const ANSWERED: &str = r#"{"type":"result","subtype":"success","is_error":false,"result":"OK","api_error_status":null}"#;

/// A stand-in for `claude` that records how it was started and what it was handed, then
/// answers as print mode does.
fn install_fake_claude_print(env: &Env) -> (std::path::PathBuf, std::path::PathBuf) {
    let bin = env.root.join("bin");
    std::fs::create_dir_all(&bin).unwrap();
    let started = env.root.join("claude-started.json");
    let handed = env.root.join("claude-handed.json");
    put_stand_in(
        &bin.join(os::program("claude")),
        &Script::Plays {
            args: None,
            steps: vec![
                Step::Records {
                    at: started.clone(),
                    variables: vec![
                        "CLAUDE_CONFIG_DIR".into(),
                        "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR".into(),
                        "ANTHROPIC_API_KEY".into(),
                        "PITBOARD_HOME".into(),
                    ],
                },
                Step::ReadsDescriptor { at: handed.clone() },
                Step::Says(ANSWERED.into()),
                Step::Exits(0),
            ],
        },
    );
    (started, handed)
}

fn json(text: &str) -> serde_json::Value {
    serde_json::from_str(text).unwrap_or_else(|e| panic!("{e}: {text}"))
}

/// Asked to warm up a parked account now, Pitboard starts Claude Code's print mode with that
/// account's access token on descriptor 3 alone, in a home of its own, and records what the
/// next reading says: its five-hour period running.
#[test]
#[cfg_attr(windows, ignore = "a warm-up runs on macOS and Linux")]
fn warming_up_now_runs_claude_code_with_the_access_token_on_descriptor_three() {
    let mut env = two_accounts("warm-now");
    let (started, handed) = install_fake_claude_print(&env);
    env.claude_usage(None);
    let (_, err, code) = env.run(&["status"]);
    assert_eq!(code, 0, "status: {err}");
    env.claude_usage(Some(5 * 3600));

    let (out, err, code) = env.run_with(
        &["warm", "now", "beta", "--json"],
        &[("ANTHROPIC_API_KEY", "sk-not-this")],
    );
    assert_eq!(code, 0, "warm now: {out}{err}");
    let envelope = json(&out);
    assert_eq!(envelope["data"]["outcome"], "started", "{out}");
    assert_eq!(
        envelope["warnings"],
        serde_json::json!([]),
        "a key in this shell reaches no run, so nothing warns of it"
    );
    assert!(envelope["data"]["until"].as_i64().is_some(), "{out}");

    let started = json(std::fs::read_to_string(&started).unwrap().trim());
    assert_eq!(
        started["args"],
        serde_json::json!([
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
        ])
    );
    let variables = &started["variables"];
    assert_eq!(variables["CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR"], "3");
    assert_eq!(variables["ANTHROPIC_API_KEY"], serde_json::Value::Null);
    assert_eq!(variables["PITBOARD_HOME"], serde_json::Value::Null);
    let config = variables["CLAUDE_CONFIG_DIR"]
        .as_str()
        .expect("a config dir");
    assert!(
        std::path::Path::new(config).ends_with("pitboard/warm/claude/config"),
        "{config}"
    );

    let handed = json(&std::fs::read_to_string(&handed).unwrap());
    assert_eq!(handed["descriptor"], "access-refresh-b");
    assert_eq!(handed["input"], "", "nothing on standard input");

    let (log, _, _) = env.run(&["log", "--json"]);
    let entries = json(&log);
    assert!(
        entries["data"]["entries"]
            .as_array()
            .expect("entries")
            .iter()
            .any(|e| e["verb"] == "warm-up" && e["subject"] == "beta" && e["outcome"] == "ok"),
        "{log}"
    );
}

/// An account whose period runs already is not warmed up again; it says until when.
#[test]
#[cfg_attr(windows, ignore = "a warm-up runs on macOS and Linux")]
fn warming_up_now_an_account_whose_period_runs_starts_nothing() {
    let mut env = two_accounts("warm-running");
    let (started, _) = install_fake_claude_print(&env);
    env.claude_usage(Some(3600));
    let (_, err, code) = env.run(&["status"]);
    assert_eq!(code, 0, "status: {err}");
    let (out, err, code) = env.run(&["warm", "now", "beta"]);
    assert_eq!(code, 0, "{out}{err}");
    assert!(out.contains("runs already"), "{out}");
    assert!(!started.exists(), "Claude Code was not started");
}
