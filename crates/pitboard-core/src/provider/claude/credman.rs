// Whether Claude Code keeps its login in Credential Manager on Windows, as far as anything
// Pitboard can read says (`windows_backend_choice`), and a look there that never writes.

use super::{paths as claude, slot};
use crate::context::Context;
use crate::host::Administered;
use serde_json::Value;

pub(crate) const FORCE: &str = "CLAUDE_CODE_FORCE_WINDOWS_CREDMAN";
const FEATURE: &str = "tengu_windows_credman";

// `<service>/<user>`, as `Bun.secrets` names a generic item.
pub(crate) fn target(ctx: &Context, service: &str) -> String {
    format!("{service}/{}", slot::account_name(ctx))
}

// What a session's `process.env` would hold as a string: settings' values are coerced.
fn is_one(value: &Value) -> bool {
    match value {
        Value::String(text) => text == "1",
        Value::Number(number) => number.as_f64() == Some(1.0),
        _ => false,
    }
}

// The config file Claude Code reads its cached features and its legacy `env` from, as JSON.
fn config(ctx: &Context) -> Option<Value> {
    let text = std::fs::read_to_string(claude::config_file(ctx)).ok()?;
    serde_json::from_str(&text).ok()
}

// The variable's value a session would start with: the highest settings layer that sets it,
// over the config file's `env`, over the environment it was started with.
fn forced(ctx: &Context, config: Option<&Value>) -> Option<String> {
    let settings = crate::settings::env_settings(ctx, FORCE);
    let deciding = settings
        .iter()
        .find(|set| set.managed && is_one(&set.value))
        .or_else(|| settings.first());
    if let Some(set) = deciding {
        return is_one(&set.value).then(|| format!("`{FORCE}` is 1 in the env of {}", set.layer));
    }
    let in_config = config
        .and_then(|config| config.get("env")?.as_object())
        .and_then(|env| env.iter().find(|(key, _)| key.eq_ignore_ascii_case(FORCE)));
    if let Some((_, value)) = in_config {
        return is_one(value).then(|| {
            format!(
                "`{FORCE}` is 1 in the env of {}",
                claude::config_file(ctx).display()
            )
        });
    }
    if ctx.credential_manager_forced {
        return Some(format!("`{FORCE}` is 1 in Pitboard's environment"));
    }
    match ctx.host().user_environment(FORCE) {
        Administered::Set(value) if value == "1" => {
            Some(format!(r"`{FORCE}` is 1 in HKCU\Environment"))
        }
        _ => None,
    }
}

// Why Claude Code would keep its login in Credential Manager, in words that follow "while".
pub(crate) fn turned_on(ctx: &Context) -> Option<String> {
    let config = config(ctx);
    forced(ctx, config.as_ref()).or_else(|| {
        let cached = config
            .as_ref()
            .and_then(|config| config.get("cachedGrowthBookFeatures")?.get(FEATURE))
            == Some(&Value::Bool(true));
        cached.then(|| {
            format!(
                "{} caches `{FEATURE}` as on",
                claude::config_file(ctx).display()
            )
        })
    })
}

// Its item, or the manifest `#m` of one kept in pieces.
pub(crate) fn holds(ctx: &Context, service: &str) -> Result<bool, String> {
    let whole = target(ctx, service);
    for name in [whole.clone(), format!("{whole}#m")] {
        if ctx.host().credential_held(ctx, &name)? {
            return Ok(true);
        }
    }
    Ok(false)
}

// None where `.credentials.json` alone is Claude Code's store.
pub(crate) fn refusal(ctx: &Context) -> Option<String> {
    if let Some(said) = turned_on(ctx) {
        return Some(format!(
            "Pitboard cannot switch Claude Code yet while it keeps its login in Credential \
             Manager ({said})"
        ));
    }
    let service = claude::live_service(ctx);
    let target = target(ctx, &service);
    match holds(ctx, &service) {
        Ok(false) => None,
        Ok(true) => Some(format!(
            "Credential Manager holds a Claude Code login under `{target}`, and nothing Pitboard \
             reads says whether Claude Code uses that store or .credentials.json. If it uses \
             Credential Manager, set `{FORCE}` to `1` in the env of {}; if not, remove that \
             item in Credential Manager",
            claude::config_dir(ctx).join("settings.json").display()
        )),
        Err(why) => Some(format!(
            "Credential Manager could not say whether it holds a Claude Code login under \
             `{target}` ({why})"
        )),
    }
}

// A test's own slots: its `CLAUDE_CONFIG_DIR`'s and its private sign-in's, where neither is a
// real login's place.
#[cfg(any(test, feature = "test-support"))]
fn own_slots(ctx: &Context, real: &[std::path::PathBuf]) -> Vec<String> {
    let config = ctx
        .claude_config_dir
        .as_deref()
        .filter(|dir| !dir.is_empty())
        .map(std::path::PathBuf::from);
    config
        .into_iter()
        .chain([crate::switch::enroll::signin_dir(ctx)])
        .filter(|dir| dir.is_absolute())
        .filter(|dir| !real.iter().any(|place| crate::host::lies_in(dir, place)))
        .map(|dir| slot::service_for_dir(&dir.to_string_lossy()))
        .collect()
}

// Before every Credential Manager call a build for tests makes, which only Windows' host makes.
#[cfg(any(test, feature = "test-support"))]
#[cfg_attr(not(windows), allow(dead_code))]
pub(crate) fn refused_in_a_build_for_tests(ctx: &Context, target: &str) -> Option<String> {
    let own = match crate::host::real_login_places(None) {
        Ok(real) => own_slots(ctx, &real),
        Err(_) => Vec::new(),
    };
    let own: Vec<&str> = own.iter().map(String::as_str).collect();
    crate::provider::names::refusal(target, &own)
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a test sets up its scratch files as a person or a tool would"
)]
mod tests {
    use super::*;
    use crate::context::Environment;
    use crate::host::memory::MemoryHost;
    use crate::host::{Hive, Os};
    use serde_json::json;
    use std::path::{Path, PathBuf};
    use std::sync::Arc;

    struct Scratch {
        ctx: Context,
        memory: Arc<MemoryHost>,
        config: PathBuf,
        _home: tempfile::TempDir,
    }

    impl Scratch {
        fn write(&self, path: &Path, value: &Value) {
            std::fs::write(path, value.to_string()).expect("written");
        }

        fn claude_json(&self) -> PathBuf {
            self.config.join(".claude.json")
        }
    }

    fn windows(extra: &[(&str, &str)]) -> Scratch {
        let home = tempfile::tempdir().expect("a scratch home");
        let profile = home.path().to_string_lossy().into_owned();
        let config = home.path().join(".claude");
        std::fs::create_dir_all(&config).expect("a config folder");
        let config_dir = config.to_string_lossy().into_owned();
        let pitboard = home.path().join("pitboard").to_string_lossy().into_owned();
        let mut pairs = vec![
            ("USERPROFILE", profile.as_str()),
            ("CLAUDE_CONFIG_DIR", config_dir.as_str()),
            ("PITBOARD_HOME", pitboard.as_str()),
        ];
        pairs.extend_from_slice(extra);
        let memory = MemoryHost::new();
        let ctx = Context::for_command_line(&Environment::on(Os::Windows, pairs))
            .with_memory_stores(Arc::clone(&memory));
        Scratch {
            ctx,
            memory,
            config,
            _home: home,
        }
    }

    fn on(scratch: &Scratch) -> Option<String> {
        turned_on(&scratch.ctx)
    }

    #[test]
    fn the_variable_turns_the_store_on_only_as_1() {
        assert_eq!(
            on(&windows(&[(FORCE, "1")])).as_deref(),
            Some("`CLAUDE_CODE_FORCE_WINDOWS_CREDMAN` is 1 in Pitboard's environment")
        );
        for value in ["true", "0", "", " 1"] {
            assert_eq!(on(&windows(&[(FORCE, value)])), None, "{value:?}");
        }
        assert_eq!(on(&windows(&[])), None);
    }

    #[test]
    fn hkcu_environment_turns_it_on_for_a_session_started_from_now_on() {
        let scratch = windows(&[]);
        scratch.memory.sets_user_environment(FORCE, "1");
        assert_eq!(
            on(&scratch).as_deref(),
            Some(r"`CLAUDE_CODE_FORCE_WINDOWS_CREDMAN` is 1 in HKCU\Environment")
        );
        scratch.memory.sets_user_environment(FORCE, "0");
        assert_eq!(on(&scratch), None);
    }

    #[test]
    fn the_persons_managed_and_policy_settings_turn_it_on_and_a_projects_do_not() {
        let scratch = windows(&[]);
        let settings = scratch.config.join("settings.json");
        scratch.write(&settings, &json!({"env": {FORCE: "1"}}));
        assert_eq!(
            on(&scratch),
            Some(format!(
                "`{FORCE}` is 1 in the env of {}",
                settings.display()
            ))
        );
        scratch.write(
            &settings,
            &json!({"env": {"claude_code_force_windows_credman": 1}}),
        );
        assert!(on(&scratch).is_some(), "a number is a string to a session");

        let managed = PathBuf::from(r"C:\Program Files\ClaudeCode").join("managed-settings.json");
        scratch
            .memory
            .administers(&managed, &json!({"env": {FORCE: "0"}}).to_string());
        assert_eq!(on(&scratch), None, "managed settings are assigned last");
        scratch
            .memory
            .administers(&managed, &json!({"env": {FORCE: "1"}}).to_string());
        assert_eq!(
            on(&scratch),
            Some(format!(
                "`{FORCE}` is 1 in the env of {}",
                managed.display()
            ))
        );

        let scratch = windows(&[]);
        scratch.memory.sets_policy(
            Hive::Machine,
            r"SOFTWARE\Policies\ClaudeCode",
            "Settings",
            &json!({"env": {FORCE: "1"}}).to_string(),
        );
        assert_eq!(
            on(&scratch),
            Some(format!(
                r"`{FORCE}` is 1 in the env of HKLM\SOFTWARE\Policies\ClaudeCode\Settings"
            ))
        );

        let scratch = windows(&[]);
        let project = scratch.config.parent().expect("the home").join("project");
        std::fs::create_dir_all(project.join(".claude")).expect("a project");
        for file in [
            project.join(".claude").join("settings.json"),
            project.join(".claude").join("settings.local.json"),
            scratch.config.join("settings.local.json"),
        ] {
            scratch.write(&file, &json!({"env": {FORCE: "1"}}));
        }
        assert_eq!(on(&scratch), None);
    }

    #[test]
    fn the_config_files_env_counts_below_the_persons_settings() {
        let scratch = windows(&[]);
        scratch.write(&scratch.claude_json(), &json!({"env": {FORCE: "1"}}));
        assert_eq!(
            on(&scratch),
            Some(format!(
                "`{FORCE}` is 1 in the env of {}",
                scratch.claude_json().display()
            ))
        );
        scratch.write(
            &scratch.config.join("settings.json"),
            &json!({"env": {FORCE: "0"}}),
        );
        assert_eq!(on(&scratch), None);
        let scratch = windows(&[(FORCE, "1")]);
        scratch.write(&scratch.claude_json(), &json!({"env": {FORCE: "0"}}));
        assert_eq!(on(&scratch), None, "it is assigned over the environment");
    }

    #[test]
    fn the_cached_feature_turns_it_on_from_the_config_file_claude_code_reads() {
        let scratch = windows(&[]);
        let cached =
            |on: Value| json!({"cachedGrowthBookFeatures": {FEATURE: on}, "numStartups": 1});
        scratch.write(&scratch.claude_json(), &cached(json!(true)));
        assert_eq!(
            on(&scratch),
            Some(format!(
                "{} caches `{FEATURE}` as on",
                scratch.claude_json().display()
            ))
        );
        for off in [json!(false), json!("true"), json!(1)] {
            scratch.write(&scratch.claude_json(), &cached(off.clone()));
            assert_eq!(on(&scratch), None, "{off}");
        }
        scratch.write(&scratch.claude_json(), &json!({"numStartups": 1}));
        assert_eq!(on(&scratch), None, "missing");

        let legacy = scratch.config.join(".config.json");
        scratch.write(&scratch.claude_json(), &cached(json!(true)));
        scratch.write(&legacy, &cached(json!(false)));
        assert_eq!(
            on(&scratch),
            None,
            "a non-empty .config.json is the one read"
        );
        std::fs::write(&legacy, "").expect("emptied");
        assert!(on(&scratch).is_some(), "an empty one gives way");
        std::fs::remove_file(&legacy).expect("removed");
        std::fs::create_dir(&legacy).expect("a folder");
        assert!(on(&scratch).is_some(), "so does a folder");
        std::fs::remove_dir(&legacy).expect("removed");
        std::fs::write(&legacy, "{ broken").expect("broken");
        assert_eq!(on(&scratch), None, "a broken one reads as off");
    }

    #[test]
    fn credential_manager_holding_the_live_target_is_ambiguous_with_the_store_off() {
        assert_eq!(refusal(&windows(&[]).ctx), None);
        for piece in ["", "#m", "#0"] {
            let scratch = windows(&[]);
            let service = claude::live_service(&scratch.ctx);
            scratch
                .memory
                .keeps_credentials(&[&format!("{service}/CLAUDE-CODE-USER{piece}")]);
            let said = refusal(&scratch.ctx);
            if piece == "#0" {
                assert_eq!(said, None, "a piece alone is no login");
                continue;
            }
            let said = said.expect("refused");
            assert!(
                said.starts_with(&format!(
                    "Credential Manager holds a Claude Code login under \
                     `{service}/claude-code-user`"
                )),
                "{said}"
            );
        }
    }

    // The chain is built for this build's own system.
    #[cfg(windows)]
    #[test]
    fn the_chain_is_never_the_file_alone_while_credential_manager_holds_the_live_target() {
        let scratch = windows(&[]);
        let service = claude::live_service(&scratch.ctx);
        let chain = super::super::live::chain(&scratch.ctx);
        assert_eq!(
            crate::store::resolve(&chain, &service).expect("readable"),
            crate::store::Backend::Absent
        );
        crate::store::write_raw(&chain, crate::service::Permit::for_a_test(), &service, "{}")
            .expect("written");
        assert_eq!(
            crate::store::resolve(&chain, &service).expect("readable"),
            crate::store::Backend::File
        );
        scratch
            .memory
            .keeps_credentials(&[&target(&scratch.ctx, &service)]);
        let chain = super::super::live::chain(&scratch.ctx);
        assert!(matches!(
            crate::store::read_raw(&chain, &service),
            Err(crate::store::Error::Unreadable(_))
        ));
    }

    #[test]
    fn a_look_that_fails_refuses_and_the_store_on_refuses_before_any_look() {
        let scratch = windows(&[]);
        scratch.memory.credentials_unreadable("1312");
        let said = refusal(&scratch.ctx).expect("refused");
        assert!(
            said.starts_with("Credential Manager could not say whether it holds"),
            "{said}"
        );
        assert!(said.ends_with("(1312)"), "{said}");
        let scratch = windows(&[(FORCE, "1")]);
        scratch.memory.credentials_unreadable("1312");
        assert_eq!(
            refusal(&scratch.ctx).as_deref(),
            Some(
                "Pitboard cannot switch Claude Code yet while it keeps its login in Credential \
                 Manager (`CLAUDE_CODE_FORCE_WINDOWS_CREDMAN` is 1 in Pitboard's environment)"
            )
        );
    }

    #[test]
    fn a_build_for_tests_looks_only_at_a_tests_own_slots() {
        let scratch = windows(&[]);
        let config = scratch.config.to_string_lossy().into_owned();
        let signin = crate::switch::enroll::signin_dir(&scratch.ctx);
        let own = slot::service_for_dir(&config);
        let signing_in = slot::service_for_dir(&signin.to_string_lossy());
        assert_eq!(own_slots(&scratch.ctx, &[]), [own.clone(), signing_in]);
        assert_eq!(
            own_slots(&scratch.ctx, std::slice::from_ref(&scratch.config)).len(),
            1,
            "a folder that is a real login's place has no slot of a test's"
        );
        let refused = |ctx: &Context, name: &str| {
            let own = own_slots(ctx, &[]);
            let own: Vec<&str> = own.iter().map(String::as_str).collect();
            crate::provider::names::refusal(name, &own)
        };
        assert_eq!(
            refused(&scratch.ctx, &format!("{own}/claude-code-user")),
            None
        );
        assert_eq!(
            refused(&scratch.ctx, &format!("{own}/claude-code-user#m")),
            None
        );
        for name in [
            "Claude Code-credentials/claude-code-user".to_owned(),
            format!(
                "{}/claude-code-user",
                slot::service_for_dir(r"C:\Users\x\.claude")
            ),
        ] {
            assert!(refused(&scratch.ctx, &name).is_some(), "{name}");
        }
        let unset = Context::new(PathBuf::from(r"C:\nowhere"));
        assert!(
            refused(&unset, "Claude Code-credentials/claude-code-user").is_some(),
            "no CLAUDE_CONFIG_DIR is the default item"
        );
        assert_eq!(
            refused_in_a_build_for_tests(&scratch.ctx, &format!("{own}/claude-code-user")),
            None
        );
        assert!(
            refused_in_a_build_for_tests(&scratch.ctx, "Claude Code-credentials/claude-code-user")
                .is_some()
        );
    }
}
