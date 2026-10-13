//! What every request Pitboard makes shares, to Anthropic or to OpenAI: the agent each
//! context's requests go out on, the loopback base a test redirects them to, and the headers
//! read off every answer.

#[cfg(any(test, feature = "test-support"))]
pub mod scripted;

use crate::context::Context;
use std::sync::{Arc, OnceLock};
use std::time::Duration;
use ureq::Agent;
use ureq::tls::{RootCerts, TlsConfig, TlsProvider};

/// Where requests go instead, for tests. Nothing else may redirect them, because an address
/// that answers "this token belongs to account X" decides which account a credential is filed
/// under. Only loopback is accepted, so a token or an answer never leaves this machine.
pub(crate) fn test_base(ctx: &Context) -> Option<String> {
    ctx.api_base.clone().filter(|url| is_loopback(url))
}

/// Judged on the parsed host, never on a prefix: `http://127.0.0.1:@elsewhere/` begins like
/// loopback and is not. A name is refused too, since the hosts file decides where it points.
fn is_loopback(url: &str) -> bool {
    let Ok(uri) = url.parse::<ureq::http::Uri>() else {
        return false;
    };
    uri.scheme_str() == Some("http")
        && uri.host().is_some_and(|host| {
            host.trim_start_matches('[')
                .trim_end_matches(']')
                .parse::<std::net::IpAddr>()
                .is_ok_and(|ip| ip.is_loopback())
        })
}
const TIMEOUT: Duration = Duration::from_secs(5);

/// The agent a context's requests go out on, made the first time one of them needs it and
/// shared by every copy of the context, so they share its connections too.
#[derive(Clone, Default)]
pub(crate) struct Agents(Arc<OnceLock<Agent>>);

/// Whether it has been made, and nothing of how: ureq's own form shows the first letter of a
/// proxy's user name.
impl std::fmt::Debug for Agents {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Agents")
            .field("made", &self.0.get().is_some())
            .finish()
    }
}

/// The agent `ctx`'s requests go out on: through the HTTP proxy its environment names, and
/// through no other ([`crate::proxy`]). ureq finds one of its own in this process's
/// environment as the configuration is made; giving it the context's, or none, replaces that
/// before the agent exists, so what ureq found never reaches a request. Where the environment
/// names a SOCKS proxy, which Pitboard does not use yet, ureq is given none, and the agent
/// refuses each request that proxy would have carried before anything is sent
/// ([`crate::proxy::Refusal`]).
pub(crate) fn agent(ctx: &Context) -> &Agent {
    ctx.agent
        .0
        .get_or_init(|| made(ctx, ureq::unversioned::resolver::DefaultResolver::default()))
}

/// [`agent`]'s agent, looking hosts up with `resolver`, which is ureq's own but in a test,
/// and connecting through ureq's own default chain of connectors.
///
/// A SOCKS proxy's refusal is the agent's middleware, not a check made by each function that
/// sends a request: every request on the agent passes it, whichever code sends it, and
/// `clippy.toml` refuses every other way to make an agent, so no request can go out directly
/// past the proxy the person named. ureq follows a redirect inside the request the
/// middleware handed on, so with a SOCKS proxy named the agent follows none: one from a host
/// `NO_PROXY` names could take the request to a host it does not name, directly.
///
/// ureq 3.4.2 also lets a single request change the agent's configuration, with
/// `RequestBuilder::config`, `Agent::configure_request`, `WithAgent::configure`, or
/// `RequestExt::middleware_config` inside a middleware. A request given its own
/// `max_redirects` follows that redirect after all, and one given its own proxy goes another
/// way, so `clippy.toml` refuses each of those too: every request goes out with the
/// configuration made here.
#[allow(
    clippy::disallowed_methods,
    reason = "the one agent configuration Pitboard makes, which is given the context's proxy, \
              and the one agent, given the resolver a test can replace"
)]
pub(crate) fn made(ctx: &Context, resolver: impl ureq::unversioned::resolver::Resolver) -> Agent {
    // rustls's documented way to choose a crypto provider. Returns Err only when one is
    // already installed, which is exactly the state wanted.
    let _ = rustls::crypto::ring::default_provider().install_default();
    let mut config = Agent::config_builder()
        .proxy(ctx.proxy.for_ureq())
        .tls_config(
            TlsConfig::builder()
                .provider(TlsProvider::Rustls)
                .root_certs(RootCerts::PlatformVerifier)
                .build(),
        )
        .timeout_global(Some(TIMEOUT))
        .http_status_as_error(false)
        .user_agent(concat!("pitboard/", env!("CARGO_PKG_VERSION")));
    if let Some(refusal) = ctx.proxy.refusal() {
        config = config.middleware(refusal).max_redirects(0);
    }
    Agent::with_parts(
        config.build(),
        ureq::unversioned::transport::DefaultConnector::default(),
        resolver,
    )
}

/// How long a service asked us to wait, from `Retry-After`. Only the seconds form is read:
/// the date form is allowed by the standard and has not been seen from this endpoint, and
/// misreading one would be worse than not reading it.
pub(crate) fn retry_after(headers: &ureq::http::HeaderMap) -> Option<i64> {
    headers
        .get("retry-after")?
        .to_str()
        .ok()?
        .trim()
        .parse::<i64>()
        .ok()
        .filter(|seconds| *seconds > 0)
}

/// The instant a response says it was sent, from its `Date` header.
///
/// Measured against api.anthropic.com over eight requests: this machine sat within 0.75
/// seconds of the server, and `Date` has a granularity of one second, so the whole spread
/// was inside the noise. That is why there is no skew estimate here and no median of
/// several observations: on a machine whose clock works there is nothing to correct, and
/// on a machine whose clock does not, reading the time off the answer is the correction.
pub(crate) fn server_time(headers: &ureq::http::HeaderMap) -> Option<i64> {
    let raw = headers.get("date")?.to_str().ok()?;
    // RFC 9110's preferred form, which is what every answer measured used:
    // `Mon, 22 Sep 2026 12:34:56 GMT`.
    jiff::civil::DateTime::strptime("%a, %d %b %Y %H:%M:%S GMT", raw)
        .ok()?
        .to_zoned(jiff::tz::TimeZone::UTC)
        .ok()
        .map(|z| z.timestamp().as_second())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The header every answer measured carried, in RFC 9110's preferred form.
    #[test]
    fn the_instant_an_answer_says_it_was_sent_is_read_off_it() {
        let mut headers = ureq::http::HeaderMap::new();
        headers.insert(
            "date",
            "Tue, 22 Sep 2026 12:34:56 GMT".parse().expect("valid"),
        );
        assert_eq!(server_time(&headers), Some(1_790_080_496));

        headers.insert("date", "whenever".parse().expect("valid"));
        assert_eq!(
            server_time(&headers),
            None,
            "an answer Pitboard cannot read the time off is not a reason to guess one"
        );
        assert_eq!(server_time(&ureq::http::HeaderMap::new()), None);
    }

    #[test]
    fn only_a_loopback_address_can_redirect_requests() {
        for allowed in ["http://127.0.0.1:8080", "http://[::1]:9"] {
            assert!(is_loopback(allowed), "{allowed}");
        }
        for refused in [
            "https://evil.example.com",
            "http://127.0.0.1.evil.example.com:80",
            "http://localhost.evil.example.com:80",
            "http://127.0.0.1:@evil.example.com/",
            "http://127.0.0.1:8080@evil.example.com/",
            "http://localhost:1",
            "https://127.0.0.1:443",
            "http://10.0.0.1:80",
            "",
        ] {
            assert!(
                !is_loopback(refused),
                "{refused} must not be able to answer identity"
            );
        }
    }
}
