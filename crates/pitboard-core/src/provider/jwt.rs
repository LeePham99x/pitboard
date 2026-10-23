// The signature is never checked, so nothing here may decide whether a token is genuine: the
// claims only label a login the person's own tool wrote to their own disk.

use base64::prelude::{BASE64_URL_SAFE_NO_PAD, Engine as _};
use serde_json::Value;

// None unless `token` is three unpadded base64url parts with a JSON object in the middle.
pub(crate) fn claims(token: &str) -> Option<Value> {
    let mut parts = token.split('.');
    let (_header, payload, _signature) = (parts.next()?, parts.next()?, parts.next()?);
    if parts.next().is_some() {
        return None;
    }
    let raw = BASE64_URL_SAFE_NO_PAD.decode(payload).ok()?;
    serde_json::from_slice(&raw).ok().filter(Value::is_object)
}

pub(crate) fn expires_at(token: &str) -> Option<i64> {
    claims(token)?.get("exp")?.as_i64()
}

pub(crate) fn claim<'a>(claims: &'a Value, path: &[&str]) -> Option<&'a str> {
    let mut at = claims;
    for step in path {
        at = at.get(step)?;
    }
    at.as_str().filter(|found| !found.is_empty())
}

// A token in a real one's shape, with a signature nothing checks.
#[cfg(any(test, feature = "test-support"))]
#[cfg_attr(not(feature = "test-support"), allow(unreachable_pub))]
pub fn unsigned(payload: &Value) -> String {
    let payload = payload.to_string();
    [
        br#"{"alg":"RS256"}"#.as_slice(),
        payload.as_bytes(),
        b"not a real signature",
    ]
    .map(|part| BASE64_URL_SAFE_NO_PAD.encode(part))
    .join(".")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_claims_come_back_whole() {
        let made = serde_json::json!({"email": "a@b.c", "exp": 1_789_935_600});
        assert_eq!(claims(&unsigned(&made)).unwrap(), made);
        assert_eq!(expires_at(&unsigned(&made)), Some(1_789_935_600));
    }

    #[test]
    fn a_nested_claim_is_reachable_by_path() {
        let made = serde_json::json!({
            "email": "a@b.c",
            "https://api.openai.com/auth": {"chatgpt_plan_type": "pro", "empty": ""}
        });
        let found = claims(&unsigned(&made)).unwrap();
        assert_eq!(claim(&found, &["email"]), Some("a@b.c"));
        assert_eq!(
            claim(
                &found,
                &["https://api.openai.com/auth", "chatgpt_plan_type"]
            ),
            Some("pro")
        );
        assert_eq!(claim(&found, &["nothing", "here"]), None);
        assert_eq!(
            claim(&found, &["https://api.openai.com/auth", "empty"]),
            None,
            "an empty claim is not an answer"
        );
    }

    #[test]
    fn anything_that_is_not_a_token_is_not_read() {
        let bad_parts = unsigned(&serde_json::json!("a string, not an object"));
        let padded = format!("aaa.{}==.ccc", BASE64_URL_SAFE_NO_PAD.encode(br#"{"a":1}"#));
        for bad in [
            "",
            "one.part",
            "four.parts.are.wrong",
            "aaa.not+base64.ccc",
            "aaa.not/base64.ccc",
            bad_parts.as_str(),
            padded.as_str(),
        ] {
            assert!(claims(bad).is_none(), "{bad} was read as claims");
            assert_eq!(expires_at(bad), None, "{bad}");
        }
    }
}
