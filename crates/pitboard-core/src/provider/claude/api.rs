//! What Pitboard asks of Anthropic: who a login belongs to and how much it has left, both
//! read with an access token, and fresh tokens for a parked login.
//!
//! Renewing is only ever done for a parked login, which Pitboard alone holds. The login
//! signed in is Claude Code's to renew: two holders renewing one refresh chain would break
//! it for both.

use crate::api::{agent, retry_after, server_time, test_base};
use crate::context::Context;
use crate::provider::Owner;
use crate::service::Permit;
use crate::usage::{self, Snapshot};
use serde_json::Value;

pub(crate) const BASE: &str = "https://api.anthropic.com";
pub(crate) const AUTH_BASE: &str = "https://platform.claude.com";

/// Claude Code's own OAuth client, which every login it stores was issued to.
const CLIENT_ID: &str = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

fn base(ctx: &Context) -> String {
    test_base(ctx).unwrap_or_else(|| BASE.to_string())
}

fn auth_base(ctx: &Context) -> String {
    test_base(ctx).unwrap_or_else(|| AUTH_BASE.to_string())
}

#[derive(Debug, thiserror::Error)]
pub(crate) enum ApiError {
    /// The access token has expired or been revoked.
    #[error("the session has expired")]
    Unauthorized,
    /// `retry_after` is what Anthropic said to wait, in seconds, where it said anything.
    #[error("Anthropic is rate limiting this request")]
    RateLimited { retry_after: Option<i64> },
    #[error("could not reach Anthropic: {0}")]
    Network(String),
    #[error("Anthropic answered {status}")]
    Unexpected { status: u16 },
    #[error("Anthropic's answer was not understood: {0}")]
    Malformed(String),
    /// The refresh token was refused for good: revoked, or already used elsewhere.
    #[error("Anthropic no longer accepts this login")]
    InvalidGrant,
}

/// Fresh tokens for a parked login, as the token endpoint returns them.
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
#[derive(Debug, Clone, PartialEq)]
pub struct Renewed {
    pub access_token: String,
    /// `None` when the server keeps the refresh token it was given.
    pub refresh_token: Option<String>,
    pub expires_in: i64,
    pub refresh_token_expires_in: Option<i64>,
    pub scopes: Option<Vec<String>>,
    /// When the server said it was, from the response's `Date` header, in epoch seconds.
    ///
    /// The lifetimes above are relative, so whatever they are added to decides when the
    /// login expires. Adding them to this machine's clock makes a wrong clock a wrong
    /// expiry: running ahead, a freshly renewed park reads as lapsed and every status
    /// renews it again, rotating the refresh chain on a loop; running behind, a lapsed
    /// park looks restorable and a switch installs a login that cannot work. Anchored to
    /// the answer's own clock, neither happens.
    ///
    /// `None` where the server sent no `Date`, and then the local clock is all there is.
    pub at: Option<i64>,
}

/// What Pitboard asks Anthropic, as a seam.
///
/// The loopback server the integration tests run answers requests, which proves the
/// parsing and the wiring. What it cannot produce on demand is the half that decides
/// behaviour: a request that times out, a 429, a refresh token Anthropic has stopped
/// accepting. Those are the answers the engine has to be right about.
pub(crate) trait Api: Send + Sync + std::fmt::Debug {
    fn owner(&self, ctx: &Context, access_token: &str) -> Result<Owner, ApiError>;
    fn usage(&self, ctx: &Context, access_token: &str) -> Result<Snapshot, ApiError>;
    /// The token exchange, which spends `refresh_token` as it answers, so it takes the
    /// [`Permit`] only the one gate every change passes makes.
    fn renew(
        &self,
        ctx: &Context,
        permit: Permit,
        refresh_token: &str,
        scopes: &[String],
        client_id: Option<&str>,
    ) -> Result<Renewed, ApiError>;
}

/// Anthropic, over the network. What every real context uses.
#[derive(Debug, Clone, Copy)]
pub(crate) struct Anthropic;

impl Api for Anthropic {
    fn owner(&self, ctx: &Context, access_token: &str) -> Result<Owner, ApiError> {
        ask_owner(ctx, access_token)
    }

    fn usage(&self, ctx: &Context, access_token: &str) -> Result<Snapshot, ApiError> {
        ask_usage(ctx, access_token)
    }

    fn renew(
        &self,
        ctx: &Context,
        permit: Permit,
        refresh_token: &str,
        scopes: &[String],
        client_id: Option<&str>,
    ) -> Result<Renewed, ApiError> {
        ask_renew(ctx, permit, refresh_token, scopes, client_id)
    }
}

/// Ask Anthropic through this context.
pub(crate) fn owner(ctx: &Context, access_token: &str) -> Result<Owner, ApiError> {
    ctx.api().owner(ctx, access_token)
}

pub(crate) fn usage(ctx: &Context, access_token: &str) -> Result<Snapshot, ApiError> {
    ctx.api().usage(ctx, access_token)
}

pub(crate) fn renew(
    ctx: &Context,
    permit: Permit,
    refresh_token: &str,
    scopes: &[String],
    client_id: Option<&str>,
) -> Result<Renewed, ApiError> {
    ctx.api()
        .renew(ctx, permit, refresh_token, scopes, client_id)
}

fn get(ctx: &Context, path: &str, access_token: &str) -> Result<Value, ApiError> {
    let mut response = agent(ctx)
        .get(format!("{}{path}", base(ctx)))
        .header("Authorization", format!("Bearer {access_token}"))
        .header("anthropic-beta", "oauth-2025-04-20")
        .call()
        .map_err(|e| ApiError::Network(e.to_string()))?;
    match response.status().as_u16() {
        200 => {
            let body = response
                .body_mut()
                .read_to_string()
                .map_err(|e| ApiError::Network(e.to_string()))?;
            serde_json::from_str(&body).map_err(|e| ApiError::Malformed(e.to_string()))
        }
        401 | 403 => Err(ApiError::Unauthorized),
        429 => Err(ApiError::RateLimited {
            retry_after: retry_after(response.headers()),
        }),
        status => Err(ApiError::Unexpected { status }),
    }
}

/// The request Claude Code makes to renew its own login, for a parked one. The scopes asked
/// for are the ones the login already has, so the answer can never be `invalid_scope`.
///
/// A login issued to another client carries its own `clientId`, and Claude Code renews it
/// as that client. Renewing it as the first-party one would be a different login.
fn ask_renew(
    ctx: &Context,
    _: Permit,
    refresh_token: &str,
    scopes: &[String],
    client_id: Option<&str>,
) -> Result<Renewed, ApiError> {
    let body = serde_json::json!({
        "grant_type": "refresh_token",
        "refresh_token": refresh_token,
        "client_id": client_id.unwrap_or(CLIENT_ID),
        "scope": scopes.join(" "),
    });
    let mut response = agent(ctx)
        .post(format!("{}/v1/oauth/token", auth_base(ctx)))
        .header("Content-Type", "application/json")
        .send(body.to_string())
        .map_err(|e| ApiError::Network(e.to_string()))?;
    let status = response.status().as_u16();
    let at = server_time(response.headers());
    let rate_limit_wait = retry_after(response.headers());
    let text = response
        .body_mut()
        .read_to_string()
        .map_err(|e| ApiError::Network(e.to_string()))?;
    match status {
        200 => {
            let body: Value =
                serde_json::from_str(&text).map_err(|e| ApiError::Malformed(e.to_string()))?;
            parse_renewed(&body, at)
        }
        429 => Err(ApiError::RateLimited {
            retry_after: rate_limit_wait,
        }),
        400..=499 if text.contains("invalid_grant") => Err(ApiError::InvalidGrant),
        status => Err(ApiError::Unexpected { status }),
    }
}

fn parse_renewed(body: &Value, at: Option<i64>) -> Result<Renewed, ApiError> {
    let text = |key: &str| body.get(key).and_then(Value::as_str).map(str::to_owned);
    Ok(Renewed {
        access_token: text("access_token")
            .ok_or_else(|| ApiError::Malformed("no access_token".into()))?,
        refresh_token: text("refresh_token"),
        expires_in: body
            .get("expires_in")
            .and_then(Value::as_i64)
            .ok_or_else(|| ApiError::Malformed("no expires_in".into()))?,
        refresh_token_expires_in: body.get("refresh_token_expires_in").and_then(Value::as_i64),
        scopes: text("scope").map(|s| s.split_whitespace().map(str::to_owned).collect()),
        at,
    })
}

fn ask_owner(ctx: &Context, access_token: &str) -> Result<Owner, ApiError> {
    let body = get(ctx, "/api/oauth/profile", access_token)?;
    parse_owner(&body)
}

fn ask_usage(ctx: &Context, access_token: &str) -> Result<Snapshot, ApiError> {
    let body = get(ctx, "/api/oauth/usage", access_token)?;
    Ok(usage::from_usage_object(&body, ctx.now()))
}

fn parse_owner(body: &Value) -> Result<Owner, ApiError> {
    let text = |path: &[&str]| -> Result<String, ApiError> {
        path.iter()
            .try_fold(body, |v, key| v.get(key))
            .and_then(Value::as_str)
            .map(str::to_owned)
            .ok_or_else(|| ApiError::Malformed(format!("no {}", path.join("."))))
    };
    Ok(Owner {
        account_uuid: text(&["account", "uuid"])?,
        email: text(&["account", "email"])?,
        organization_uuid: text(&["organization", "uuid"])?,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn a_renewal_answer_is_read_as_claude_code_reads_it() {
        let full = json!({
            "access_token": "a2", "refresh_token": "r2", "expires_in": 28_800,
            "refresh_token_expires_in": 2_592_000, "scope": "user:inference user:profile",
            "token_type": "Bearer"
        });
        assert_eq!(
            parse_renewed(&full, Some(1_790_000_000)).unwrap(),
            Renewed {
                access_token: "a2".into(),
                refresh_token: Some("r2".into()),
                expires_in: 28_800,
                refresh_token_expires_in: Some(2_592_000),
                scopes: Some(vec!["user:inference".into(), "user:profile".into()]),
                at: Some(1_790_000_000),
            }
        );
        let kept = parse_renewed(&json!({"access_token": "a2", "expires_in": 60}), None).unwrap();
        assert_eq!(
            kept.refresh_token, None,
            "no refresh_token means the old one stays"
        );
        assert_eq!(kept.at, None, "no Date header leaves the local clock to it");
        assert!(parse_renewed(&json!({"expires_in": 60}), None).is_err());
    }

    /// Field names copied from a live response on 2026-09-21.
    #[test]
    fn reads_the_owner_from_the_shape_the_profile_endpoint_returns() {
        let body = json!({
            "account": {
                "uuid": "acc", "email": "a@b.c", "display_name": "A", "full_name": "A",
                "created_at": "2025-10-15T02:36:35Z", "has_claude_max": true, "has_claude_pro": false
            },
            "organization": {
                "uuid": "org", "name": "Org", "organization_type": "claude_max",
                "rate_limit_tier": "default_claude_max_20x", "billing_type": "stripe_subscription"
            },
            "application": {}, "enabled_plugins": []
        });
        assert_eq!(
            parse_owner(&body).unwrap(),
            Owner {
                account_uuid: "acc".into(),
                email: "a@b.c".into(),
                organization_uuid: "org".into(),
            }
        );
    }

    #[test]
    fn a_profile_missing_the_account_is_malformed_rather_than_guessed() {
        assert!(matches!(
            parse_owner(&json!({"organization": {"uuid": "org"}})),
            Err(ApiError::Malformed(_))
        ));
    }
}
