//! What Pitboard asks of Anthropic: who a login belongs to and how much it has left, both
//! read with an access token, and fresh tokens for a parked login.
//!
//! Renewing is only ever done for a parked login, which Pitboard alone holds. The login
//! signed in is Claude Code's to renew: two holders renewing one refresh chain would break
//! it for both.

use crate::api::{Refusal, agent, retry_after, server_time, test_base};
use crate::context::Context;
use crate::provider::{Owner, ProviderError, ProviderId};
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

const SERVICE: &str = ProviderId::Claude.service();

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
    fn owner(&self, ctx: &Context, access_token: &str) -> Result<Owner, ProviderError>;
    fn usage(&self, ctx: &Context, access_token: &str) -> Result<Snapshot, ProviderError>;
    /// The token exchange, which spends `refresh_token` as it answers, so it takes the
    /// [`Permit`] only the one gate every change passes makes.
    fn renew(
        &self,
        ctx: &Context,
        permit: Permit,
        refresh_token: &str,
        scopes: &[String],
        client_id: Option<&str>,
    ) -> Result<Renewed, ProviderError>;
}

/// Anthropic, over the network. What every real context uses.
#[derive(Debug, Clone, Copy)]
pub(crate) struct Anthropic;

impl Api for Anthropic {
    fn owner(&self, ctx: &Context, access_token: &str) -> Result<Owner, ProviderError> {
        ask_owner(ctx, access_token)
    }

    fn usage(&self, ctx: &Context, access_token: &str) -> Result<Snapshot, ProviderError> {
        ask_usage(ctx, access_token)
    }

    fn renew(
        &self,
        ctx: &Context,
        permit: Permit,
        refresh_token: &str,
        scopes: &[String],
        client_id: Option<&str>,
    ) -> Result<Renewed, ProviderError> {
        ask_renew(ctx, permit, refresh_token, scopes, client_id)
    }
}

/// Ask Anthropic through this context.
pub(crate) fn owner(ctx: &Context, access_token: &str) -> Result<Owner, ProviderError> {
    ctx.api().owner(ctx, access_token)
}

pub(crate) fn usage(ctx: &Context, access_token: &str) -> Result<Snapshot, ProviderError> {
    ctx.api().usage(ctx, access_token)
}

pub(crate) fn renew(
    ctx: &Context,
    permit: Permit,
    refresh_token: &str,
    scopes: &[String],
    client_id: Option<&str>,
) -> Result<Renewed, ProviderError> {
    ctx.api()
        .renew(ctx, permit, refresh_token, scopes, client_id)
}

fn get(ctx: &Context, path: &str, access_token: &str) -> Result<Value, ProviderError> {
    let mut response = agent(ctx)
        .get(format!("{}{path}", base(ctx)))
        .header("Authorization", format!("Bearer {access_token}"))
        .header("anthropic-beta", "oauth-2025-04-20")
        .call()
        .map_err(network)?;
    match response.status().as_u16() {
        200 => {
            let body = response.body_mut().read_to_string().map_err(network)?;
            serde_json::from_str(&body).map_err(malformed)
        }
        429 => Err(ProviderError::RateLimited {
            service: SERVICE,
            retry_after: retry_after(response.headers()),
        }),
        401 | 403 => {
            let refusal = Refusal::of(&mut response);
            if refuses_the_account(&refusal) {
                Err(ProviderError::Refused {
                    service: SERVICE,
                    refusal: Box::new(refusal),
                })
            } else {
                Err(ProviderError::Unauthorized)
            }
        }
        _ => Err(ProviderError::answered(SERVICE, Refusal::of(&mut response))),
    }
}

// A 401 or 403 that no renewal mends, as Claude Code 2.1.301 tells one apart (the register's
// `account_refusals`): the account must be verified, or its organisation allows no OAuth.
fn refuses_the_account(refusal: &Refusal) -> bool {
    let verification = refusal.status == 403
        && refusal.kind.as_deref() == Some("permission_error")
        && refusal.code.as_deref() == Some("verification_required");
    let organisation = refusal.message.as_deref().is_some_and(|message| {
        message.contains("OAuth authentication is currently not allowed for this organization")
    });
    verification || organisation
}

// As Claude Code 2.1.301 reads the token endpoint (the register's `token_refusals`): an
// account on hold answers `invalid_grant` or `access_denied` with `account_on_hold`, and keeps
// its login; only `invalid_grant` otherwise is a refresh token spent for good.
fn token_refused(refusal: Refusal) -> ProviderError {
    let error = refusal.oauth_error();
    if matches!(refusal.status, 400 | 401 | 403)
        && matches!(error, Some("invalid_grant" | "access_denied"))
        && refusal.message.as_deref() == Some("account_on_hold")
    {
        return ProviderError::OnHold {
            service: SERVICE,
            link: refusal.link.as_deref().and_then(anthropic_page),
        };
    }
    if matches!(refusal.status, 400 | 401) && error == Some("invalid_grant") {
        return ProviderError::InvalidGrant { service: SERVICE };
    }
    ProviderError::answered(SERVICE, refusal)
}

// Only a page of Anthropic's own, as Claude Code 2.1.301 opens one.
fn anthropic_page(link: &str) -> Option<String> {
    let uri = link.parse::<ureq::http::Uri>().ok()?;
    let authority = uri.authority()?;
    let plain = !authority.as_str().contains('@') && authority.port().is_none();
    (uri.scheme_str() == Some("https")
        && plain
        && matches!(
            authority.host(),
            "portal.anthropic.com" | "claude.ai" | "platform.claude.com"
        ))
    .then(|| link.to_owned())
}

fn network(error: impl std::fmt::Display) -> ProviderError {
    ProviderError::Network {
        service: SERVICE,
        detail: error.to_string(),
    }
}

fn malformed(error: impl std::fmt::Display) -> ProviderError {
    ProviderError::Malformed {
        service: SERVICE,
        detail: error.to_string(),
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
) -> Result<Renewed, ProviderError> {
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
        .map_err(network)?;
    let status = response.status().as_u16();
    let at = server_time(response.headers());
    let rate_limit_wait = retry_after(response.headers());
    let text = response.body_mut().read_to_string().map_err(network)?;
    match status {
        200 => {
            let body: Value = serde_json::from_str(&text).map_err(malformed)?;
            parse_renewed(&body, at)
        }
        429 => Err(ProviderError::RateLimited {
            service: SERVICE,
            retry_after: rate_limit_wait,
        }),
        status => Err(token_refused(Refusal::answered(
            status,
            response.headers(),
            &text,
        ))),
    }
}

fn parse_renewed(body: &Value, at: Option<i64>) -> Result<Renewed, ProviderError> {
    let text = |key: &str| body.get(key).and_then(Value::as_str).map(str::to_owned);
    Ok(Renewed {
        access_token: text("access_token").ok_or_else(|| malformed("no access_token"))?,
        refresh_token: text("refresh_token"),
        expires_in: body
            .get("expires_in")
            .and_then(Value::as_i64)
            .ok_or_else(|| malformed("no expires_in"))?,
        refresh_token_expires_in: body.get("refresh_token_expires_in").and_then(Value::as_i64),
        scopes: text("scope").map(|s| s.split_whitespace().map(str::to_owned).collect()),
        at,
    })
}

fn ask_owner(ctx: &Context, access_token: &str) -> Result<Owner, ProviderError> {
    let body = get(ctx, "/api/oauth/profile", access_token)?;
    parse_owner(&body)
}

fn ask_usage(ctx: &Context, access_token: &str) -> Result<Snapshot, ProviderError> {
    let body = get(ctx, "/api/oauth/usage", access_token)?;
    Ok(usage::from_usage_object(&body, ctx.now()))
}

fn parse_owner(body: &Value) -> Result<Owner, ProviderError> {
    let text = |path: &[&str]| -> Result<String, ProviderError> {
        path.iter()
            .try_fold(body, |v, key| v.get(key))
            .and_then(Value::as_str)
            .map(str::to_owned)
            .ok_or_else(|| malformed(format!("no {}", path.join("."))))
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

    fn refused(status: u16, body: &str) -> ProviderError {
        token_refused(Refusal::read(status, body))
    }

    // Shapes from Claude Code 2.1.301's readers of the token endpoint's errors.
    #[test]
    fn an_account_on_hold_keeps_its_login() {
        let on_hold = |error: &str, link: &str| {
            refused(
                400,
                &json!({"error": error, "error_description": "account_on_hold", "error_uri": link})
                    .to_string(),
            )
        };
        assert!(matches!(
            on_hold("invalid_grant", "https://claude.ai/settings/hold"),
            ProviderError::OnHold { link: Some(link), .. } if link == "https://claude.ai/settings/hold"
        ));
        assert!(matches!(
            on_hold("access_denied", "https://evil.example/claude.ai"),
            ProviderError::OnHold { link: None, .. }
        ));
        for elsewhere in [
            "http://claude.ai/x",
            "https://claude.ai:8443/x",
            "https://someone@claude.ai/x",
            "https://claude.ai.example.com/x",
        ] {
            assert!(
                matches!(
                    on_hold("invalid_grant", elsewhere),
                    ProviderError::OnHold { link: None, .. }
                ),
                "{elsewhere}"
            );
        }
    }

    #[test]
    fn only_invalid_grant_ends_a_refresh_token() {
        assert!(matches!(
            refused(
                400,
                r#"{"error":"invalid_grant","error_description":"Refresh token not found or invalid"}"#
            ),
            ProviderError::InvalidGrant { .. }
        ));
        assert!(matches!(
            refused(401, r#"{"error":{"type":"invalid_grant"}}"#),
            ProviderError::InvalidGrant { .. }
        ));
        let kept = refused(403, r#"{"error":"invalid_grant"}"#);
        assert!(matches!(kept, ProviderError::Refused { .. }), "{kept:?}");
        let no_organisation = refused(
            400,
            r#"{"error":"invalid_request","error_description":"No organization associated with this token"}"#,
        );
        assert_eq!(
            no_organisation.to_string(),
            "Anthropic answered 400: No organization associated with this token"
        );
        assert!(matches!(no_organisation, ProviderError::Refused { .. }));
        assert!(matches!(
            refused(503, ""),
            ProviderError::Unavailable { .. }
        ));
    }

    // Claude Code 2.1.301's `Wqn` and `Xqt`.
    #[test]
    fn a_refusal_of_the_account_is_not_an_expired_token() {
        let verification = Refusal::read(
            403,
            r#"{"type":"error","error":{"type":"permission_error","message":"Verify your account","details":{"error_code":"verification_required"}}}"#,
        );
        assert!(refuses_the_account(&verification));
        let organisation = Refusal::read(
            401,
            r#"{"type":"error","error":{"type":"authentication_error","message":"OAuth authentication is currently not allowed for this organization."}}"#,
        );
        assert!(refuses_the_account(&organisation));
        let expired = Refusal::read(
            401,
            r#"{"type":"error","error":{"type":"authentication_error","message":"OAuth access token is invalid."}}"#,
        );
        assert!(!refuses_the_account(&expired));
        let scope = Refusal::read(
            403,
            r#"{"type":"error","error":{"type":"permission_error","message":"OAuth token does not meet scope requirement user:profile"}}"#,
        );
        assert!(!refuses_the_account(&scope));
    }

    #[test]
    fn a_profile_missing_the_account_is_malformed_rather_than_guessed() {
        assert!(matches!(
            parse_owner(&json!({"organization": {"uuid": "org"}})),
            Err(ProviderError::Malformed { .. })
        ));
    }
}
