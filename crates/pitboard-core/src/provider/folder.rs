use crate::context::Context;
use crate::host::Listed;
use crate::provider::{ProviderError, ProviderId};
use serde_json::value::RawValue;
use std::collections::BTreeMap;
use std::ffi::OsStr;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Class {
    // moves with the account
    Account,
    Stays,
    // a temporary that never moves and never stops a move
    Passing,
    // not read: stops the start of a move
    Unread,
}

pub(crate) struct SignedIn {
    // the fingerprint of the session as stored
    pub login: String,
    pub expires_at: Option<i64>,
}

impl std::fmt::Debug for SignedIn {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SignedIn")
            .field("login", &format_args!("{} characters", self.login.len()))
            .field("expires_at", &self.expires_at)
            .finish()
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Sidecar {
    None,
    // a settings write the app left, which it writes in at its next start
    Valid,
    // one the app drops at its next start
    Stale,
}

pub(crate) struct AppSettings {
    pub(crate) body: Vec<(String, Box<RawValue>)>,
    // SHA-256 of the settings file on disk; None: no file
    pub file: Option<String>,
    pub sidecar: Sidecar,
}

impl AppSettings {
    pub(crate) fn account_keys(&self, app: &dyn Folder) -> Keys {
        Keys(
            self.body
                .iter()
                .filter(|(key, _)| app.keys().contains(&key.as_str()))
                .map(|(key, value)| (key.clone(), value.clone()))
                .collect(),
        )
    }

    // An empty name is no name; the settings read refuses what is not an account id.
    pub(crate) fn named(&self, app: &dyn Folder) -> Option<String> {
        self.body
            .iter()
            .find(|(key, _)| key == app.naming_key())
            .and_then(|(_, value)| serde_json::from_str::<String>(value.get()).ok())
            .filter(|name| !name.is_empty())
    }
}

impl std::fmt::Debug for AppSettings {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AppSettings")
            .field(
                "keys",
                &self.body.iter().map(|(key, _)| key).collect::<Vec<_>>(),
            )
            .field("file", &self.file.is_some())
            .field("sidecar", &self.sidecar)
            .finish()
    }
}

// An account's keys of the settings, values as the file held them, byte for byte.
pub(crate) struct Keys(pub(crate) BTreeMap<String, Box<RawValue>>);

impl Keys {
    pub(crate) fn get(&self, key: &str) -> Option<&RawValue> {
        self.0.get(key).map(AsRef::as_ref)
    }

    // SHA-256, hex, of each value as it is held: what a manifest records.
    pub(crate) fn digests(&self) -> BTreeMap<String, String> {
        use sha2::{Digest, Sha256};
        self.0
            .iter()
            .map(|(key, value)| (key.clone(), hex::encode(Sha256::digest(value.get()))))
            .collect()
    }

    pub(crate) fn from_json(raw: &[u8]) -> Result<Keys, String> {
        serde_json::from_slice(raw)
            .map(Keys)
            .map_err(|e| format!("is not a JSON object of keys: {e}"))
    }
}

impl std::fmt::Debug for Keys {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_map()
            .entries(
                self.0
                    .iter()
                    .map(|(key, value)| (key, format!("{} bytes", value.get().len()))),
            )
            .finish()
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Unread {
    // read, and not a shape the register names
    Unknown {
        what: String,
        then: crate::error::Then,
    },
    // could not be read this time
    Unreadable(String),
}

// Why a data folder is not one Pitboard moves a login in, said as the words after its path.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Unfit {
    Link,
    // the folder a third-party deployment of the app keeps beside it, by name
    ThirdParty(&'static str),
}

impl std::fmt::Display for Unfit {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Unfit::Link => f.write_str("is a link"),
            Unfit::ThirdParty(name) => write!(
                f,
                "has a `{name}` folder beside it, which a third-party deployment of Claude uses"
            ),
        }
    }
}

pub(crate) trait Folder: Send + Sync + std::fmt::Debug {
    fn id(&self) -> ProviderId;
    fn root(&self, ctx: &Context) -> Result<PathBuf, ProviderError>;
    fn refuses(&self, root: &Path) -> Option<Unfit>;
    // the account's items, in the order a login leaves by; the first holds the session
    fn items(&self) -> &'static [&'static str];
    fn keys(&self) -> &'static [&'static str];
    fn naming_key(&self) -> &'static str;
    fn classify(&self, name: &OsStr) -> Class;
    fn settings(&self, root: &Path) -> Result<AppSettings, Unread>;
    fn session(&self, ctx: &Context, jar: &Path) -> Result<Option<SignedIn>, Unread>;
    // whether a token cache's value is the one a signed-out app leaves
    fn emptied(&self, key: &str, value: &RawValue) -> bool;
    fn is_the_app(&self, ctx: &Context, root: &Path, one: &Listed, all: &[Listed]) -> bool;
    fn holder(&self) -> &'static crate::holder::Holder;
    fn app_name(&self) -> &'static str;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn printing_the_keys_never_shows_a_value() {
        let keys = Keys::from_json(br#"{"oauth:tokenCache": "sk-ant-secret"}"#).unwrap();
        let printed = format!("{keys:?}");
        assert!(!printed.contains("secret"), "{printed}");
        assert!(printed.contains("oauth:tokenCache"), "{printed}");
        let digests = keys.digests();
        assert_eq!(digests["oauth:tokenCache"].len(), 64);
        let session = SignedIn {
            login: "0a1b2c3d4e5f6071".into(),
            expires_at: Some(1),
        };
        assert!(!format!("{session:?}").contains("0a1b"));
    }
}
