//! Claude Code's own headless run that starts an account's five-hour period, and what its
//! result says.

use super::engine::access_token;
use crate::context::Context;
use crate::provider::{ProviderError, ProviderId};
use crate::service::Permit;
use crate::warm::Failure;
use crate::warm::run::{self, Headless, Ran};
use serde_json::Value;
use std::path::Path;

/// Print mode on the smallest model, with no tools, no MCP servers, no slash commands and no
/// session kept, answering in JSON (the register's `headless_print_mode`). The access token
/// goes on descriptor 3, which `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR` names, and is used with
/// no refresh token and no expiry and stored nowhere (`headless_token_descriptor`). Never on
/// standard input: print mode adds what it reads there to the prompt. `CLAUDE_CONFIG_DIR` is
/// the private home's, so its config, its keychain slot and what it fetches are its own. It
/// sends a request on the account's plan, so it is made only with the [`Permit`].
pub(crate) fn headless(
    ctx: &Context,
    permit: Permit,
    home: &Path,
    login: &Value,
) -> Result<Headless, ProviderError> {
    let token = access_token(login).ok_or_else(|| ProviderError::ShapeUnexpected {
        provider: ProviderId::Claude,
        detail: "it has no claudeAiOauth.accessToken".into(),
    })?;
    let mut command = run::command(ctx, permit, ProviderId::Claude, home);
    command
        .args([
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
            "json",
        ])
        .env("CLAUDE_CONFIG_DIR", home.join("config"))
        .env("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "3")
        .env("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC", "1")
        .env("DISABLE_AUTOUPDATER", "1");
    Ok(Headless {
        command,
        token: Some(token.to_owned()),
    })
}

/// Print mode's JSON result (`headless_result`): `is_error`, with `api_error_status` and
/// `api_error` where the service refused the request, and `result`, the answer or what went
/// wrong in Claude Code's words. `Ok` where the request was answered.
pub(crate) fn verdict(ran: &Ran) -> Result<(), Failure> {
    let Some(result) = ran
        .stdout
        .lines()
        .rev()
        .find_map(|line| serde_json::from_str::<Value>(line.trim()).ok())
        .filter(Value::is_object)
    else {
        return Err(Failure::Failed("Claude Code printed no result".into()));
    };
    let failed = result.get("is_error").and_then(Value::as_bool) != Some(false);
    if !failed && ran.exit == Some(0) {
        return Ok(());
    }
    let said = result
        .get("result")
        .and_then(Value::as_str)
        .unwrap_or("Claude Code failed")
        .to_owned();
    match (
        result.get("api_error_status").and_then(Value::as_u64),
        result.get("api_error").and_then(Value::as_str),
    ) {
        (Some(429), Some("usage_limit_reached")) => Err(Failure::LimitReached),
        (Some(401 | 403), _) => Err(Failure::LoginRefused),
        (Some(429 | 500..=599), _) => Err(Failure::Unreachable),
        _ => Err(Failure::Failed(said)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ran(exit: i32, stdout: &str) -> Ran {
        Ran {
            exit: Some(exit),
            stdout: stdout.into(),
            timed_out: false,
        }
    }

    #[test]
    fn each_result_print_mode_gives_is_read_as_what_it_came_to() {
        let cases = [
            (0, r#"{"is_error":false,"result":"OK"}"#, Ok(())),
            (
                1,
                r#"{"is_error":true,"api_error_status":429,"api_error":"usage_limit_reached","result":"limit"}"#,
                Err(Failure::LimitReached),
            ),
            (
                1,
                r#"{"is_error":true,"api_error_status":429,"api_error":"rate_limit_error","result":"slow down"}"#,
                Err(Failure::Unreachable),
            ),
            (
                1,
                r#"{"is_error":true,"api_error_status":401,"result":"Invalid API key"}"#,
                Err(Failure::LoginRefused),
            ),
            (
                1,
                r#"{"is_error":true,"api_error_status":403,"result":"forbidden"}"#,
                Err(Failure::LoginRefused),
            ),
            (
                1,
                r#"{"is_error":true,"api_error_status":529,"result":"Overloaded"}"#,
                Err(Failure::Unreachable),
            ),
            (
                1,
                r#"{"is_error":true,"result":"something else"}"#,
                Err(Failure::Failed("something else".into())),
            ),
            (
                1,
                r#"{"is_error":false,"result":"OK"}"#,
                Err(Failure::Failed("OK".into())),
            ),
            (
                0,
                "a warning first\n{\"is_error\":false,\"result\":\"OK\"}\n",
                Ok(()),
            ),
            (
                0,
                "",
                Err(Failure::Failed("Claude Code printed no result".into())),
            ),
            (
                0,
                "[1,2]",
                Err(Failure::Failed("Claude Code printed no result".into())),
            ),
        ];
        for (exit, stdout, expected) in cases {
            assert_eq!(verdict(&ran(exit, stdout)), expected, "{stdout}");
        }
    }
}
