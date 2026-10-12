use crate::context::Context;
use crate::error::Then;
use crate::host::{Cookie, JarError};
use crate::provider::folder::{SignedIn, Unread};
use serde_json::value::RawValue;
use std::path::Path;

// Read in 2.7032.0 @2929634.
pub(crate) const SESSION_NAMES: [&str; 5] = [
    "sessionKey",
    "sessionKeyV2",
    "sessionKeyV3",
    "sessionKeyLC",
    "sessionKeyV3LC",
];
pub(crate) const HOSTS: [&str; 2] = [".claude.ai", "claude.ai"];
const COMPATIBLE: i64 = 24;
// What a signed-out Claude leaves in each token cache (E1b).
const EMPTIED_LENGTH: usize = 28;
const SECONDS_FROM_1601_TO_1970: i64 = 11_644_473_600;

pub(super) fn session(ctx: &Context, jar: &Path) -> Result<Option<SignedIn>, Unread> {
    let read = match ctx.host().cookies(jar, &HOSTS, &SESSION_NAMES) {
        Ok(read) => read,
        Err(JarError::Absent) => return Ok(None),
        Err(JarError::Unsettled(detail) | JarError::Unreadable(detail)) => {
            return Err(Unread::Unreadable(detail));
        }
        Err(JarError::Unknown(detail)) => return Err(format_unread(&detail)),
        Err(JarError::Unsupported) => {
            return Err(Unread::Unreadable(
                "this system's cookie stores are not read".into(),
            ));
        }
    };
    if read.compatible > COMPATIBLE {
        return Err(format_unread(&format!(
            "a store version {} that a reader of version {} cannot read",
            read.version, COMPATIBLE
        )));
    }
    let sessions: Vec<&Cookie> = read
        .cookies
        .iter()
        .filter(|cookie| SESSION_NAMES.contains(&cookie.name.as_str()))
        .collect();
    if let Some(stray) = sessions
        .iter()
        .find(|cookie| !HOSTS.contains(&cookie.host.as_str()))
    {
        return Err(unknown(format!(
            "keeps a session cookie under {}, a host this Pitboard has not read",
            stray.host
        )));
    }
    for (at, cookie) in sessions.iter().enumerate() {
        if !cookie.stored.starts_with(b"v10") {
            return Err(format_unread(&format!(
                "`{}` is not stored as `v10`",
                cookie.name
            )));
        }
        if sessions[..at]
            .iter()
            .any(|earlier| earlier.name == cookie.name && earlier.host == cookie.host)
        {
            return Err(format_unread(&format!(
                "two `{}` cookies for {}",
                cookie.name, cookie.host
            )));
        }
    }
    // A `sessionKey` for each host, as Claude's own account switcher may leave. At version 24
    // each value carries a hash of its host, so one session there is not told from two.
    let mut keys = sessions
        .iter()
        .filter(|cookie| cookie.name == SESSION_NAMES[0]);
    if let (Some(one), Some(other)) = (keys.next(), keys.next()) {
        return Err(format_unread(&format!(
            "two `{}` cookies, for {} and {}",
            one.name, one.host, other.host
        )));
    }
    let Some(first) = sessions.first() else {
        return Ok(None);
    };
    let key = sessions
        .iter()
        .find(|c| c.name == SESSION_NAMES[0] && c.path == "/")
        .ok_or_else(|| format_unread(&format!("`{}` and no `sessionKey`", first.name)))?;
    Ok(Some(SignedIn {
        login: crate::store::fingerprint(&hex::encode(&key.stored)),
        expires_at: (key.expires_utc != 0)
            .then(|| key.expires_utc / 1_000_000 - SECONDS_FROM_1601_TO_1970),
    }))
}

fn unknown(what: String) -> Unread {
    Unread::Unknown {
        what,
        then: Then::UpdatePitboard,
    }
}

fn format_unread(detail: &str) -> Unread {
    unknown(format!(
        "keeps its cookies in a format this Pitboard has not read ({detail})"
    ))
}

pub(super) fn emptied(_key: &str, value: &RawValue) -> bool {
    serde_json::from_str::<String>(value.get())
        .is_ok_and(|cache| cache.chars().count() == EMPTIED_LENGTH)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::desktop_testing::write_stand_in_jar;
    use crate::host::memory::MemoryHost;

    struct Scratch(std::path::PathBuf);

    impl Drop for Scratch {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    fn scratch(name: &str) -> (Scratch, Context, std::sync::Arc<MemoryHost>) {
        let root = std::env::temp_dir().join(format!(
            "pitboard-desktop-jar-{name}-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).expect("a scratch folder");
        let host = MemoryHost::new();
        let ctx = Context::for_unit_test().with_memory_stores(host.clone());
        (Scratch(root), ctx, host)
    }

    fn cookie(host: &str, name: &str, stored: &[u8]) -> Cookie {
        Cookie {
            host: host.into(),
            name: name.into(),
            path: "/".into(),
            stored: stored.to_vec(),
            expires_utc: 13_420_000_000_000_000,
        }
    }

    fn read(scratch: &Scratch, ctx: &Context, rows: &[Cookie]) -> Result<Option<SignedIn>, Unread> {
        let jar = scratch.0.join("Cookies");
        write_stand_in_jar(&jar, 24, rows);
        session(ctx, &jar)
    }

    fn format_refusal(found: Result<Option<SignedIn>, Unread>) -> String {
        match found {
            Err(Unread::Unknown {
                what,
                then: Then::UpdatePitboard,
            }) => what,
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn the_session_is_sessionkey_for_claude_ai_fingerprinted_as_stored() {
        let (scratch, ctx, _) = scratch("one");
        let found = read(
            &scratch,
            &ctx,
            &[
                cookie(".claude.ai", "sessionKey", b"v10abc"),
                cookie(".claude.ai", "sessionKeyV3", b"v10abc"),
                cookie(".claude.ai", "lastActiveOrg", b"v10x"),
            ],
        )
        .expect("read")
        .expect("signed in");
        assert_eq!(
            found.login,
            crate::store::fingerprint(&hex::encode(b"v10abc"))
        );
        assert_eq!(
            found.expires_at,
            Some(13_420_000_000 - SECONDS_FROM_1601_TO_1970)
        );
        let mut never = cookie("claude.ai", "sessionKey", b"v10def");
        never.expires_utc = 0;
        let found = read(&scratch, &ctx, &[never]).unwrap().unwrap();
        assert_eq!(found.expires_at, None);
    }

    #[test]
    fn signed_out_needs_none_of_the_five_names() {
        let (scratch, ctx, _) = scratch("names");
        for name in SESSION_NAMES {
            if name == "sessionKey" {
                continue;
            }
            let what = format_refusal(read(&scratch, &ctx, &[cookie(".claude.ai", name, b"v10a")]));
            assert!(what.contains("no `sessionKey`"), "{name}: {what}");
        }
        assert!(
            read(
                &scratch,
                &ctx,
                &[cookie(".claude.ai", "lastActiveOrg", b"v10a")]
            )
            .unwrap()
            .is_none()
        );
    }

    #[test]
    fn a_session_cookie_under_another_host_is_never_signed_out() {
        let (scratch, ctx, _) = scratch("host");
        let what = format_refusal(read(
            &scratch,
            &ctx,
            &[cookie("claude.com", "sessionKeyV3", b"v10a")],
        ));
        assert!(what.contains("a host this Pitboard has not read"), "{what}");
    }

    #[test]
    fn a_value_not_v10_two_rows_or_a_newer_store_is_another_format() {
        let (scratch, ctx, _) = scratch("format");
        let what = format_refusal(read(
            &scratch,
            &ctx,
            &[cookie(".claude.ai", "sessionKey", b"v11a")],
        ));
        assert!(what.contains("v10"), "{what}");
        let twice = [
            cookie(".claude.ai", "sessionKey", b"v10a"),
            cookie(".claude.ai", "sessionKey", b"v10b"),
        ];
        assert!(format_refusal(read(&scratch, &ctx, &twice)).contains("two"));
        let jar = scratch.0.join("Cookies");
        write_stand_in_jar(&jar, 25, &[]);
        assert!(format_refusal(session(&ctx, &jar)).contains("cannot read"));
    }

    #[test]
    fn a_session_key_for_each_host_is_another_format() {
        let (scratch, ctx, _) = scratch("two-hosts");
        let two = [
            cookie(".claude.ai", "sessionKey", b"v10a"),
            cookie("claude.ai", "sessionKey", b"v10b"),
        ];
        let what = format_refusal(read(&scratch, &ctx, &two));
        assert!(
            what.contains("two `sessionKey` cookies, for .claude.ai and claude.ai"),
            "{what}"
        );
        // Below version 24 one session on both hosts is one value, and it refuses too.
        let jar = scratch.0.join("Cookies");
        let one = [
            cookie("claude.ai", "sessionKey", b"v10a"),
            cookie(".claude.ai", "sessionKey", b"v10a"),
        ];
        write_stand_in_jar(&jar, 23, &one);
        let what = format_refusal(session(&ctx, &jar));
        assert!(what.contains("two `sessionKey` cookies"), "{what}");
    }

    #[test]
    fn a_jar_that_cannot_be_read_is_never_signed_out() {
        let (scratch, ctx, host) = scratch("unread");
        let jar = scratch.0.join("Cookies");
        write_stand_in_jar(&jar, 24, &[]);
        host.jar_unsettled(&jar);
        assert!(matches!(session(&ctx, &jar), Err(Unread::Unreadable(_))));
        assert_eq!(host.jars_asked().as_slice(), std::slice::from_ref(&jar));
        assert!(session(&ctx, &scratch.0.join("Missing")).unwrap().is_none());
    }

    #[test]
    fn only_a_28_character_cache_is_emptied() {
        let raw = |s: &str| RawValue::from_string(s.into()).unwrap();
        assert!(emptied(
            "oauth:tokenCache",
            &raw(&format!("\"{}\"", "a".repeat(28)))
        ));
        assert!(!emptied(
            "oauth:tokenCache",
            &raw(&format!("\"{}\"", "a".repeat(29)))
        ));
        assert!(!emptied("oauth:tokenCache", &raw("28")));
    }
}
