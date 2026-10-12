// The Claude Desktop the tests play, on a scratch folder. Nothing here touches the real one.

use crate::host::Cookie;
use crate::host::memory::{MemoryHost, StandInCookie, StandInJar};
use std::path::{Path, PathBuf};
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Person {
    pub uuid: &'static str,
    pub session: &'static str,
}

impl Person {
    // The fingerprint Pitboard keeps of this person's session cookie as stored.
    pub fn login(&self) -> String {
        crate::store::fingerprint(&hex::encode(self.stored()))
    }

    fn stored(&self) -> Vec<u8> {
        [b"v10".as_slice(), self.session.as_bytes()].concat()
    }

    fn tokens(&self) -> String {
        format!("\"acct:{}|{}\"", self.uuid, self.session)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JarAs {
    // a real SQLite jar, for the real hosts
    Sqlite,
    // the JSON `MemoryHost` reads in its place
    StandIn,
}

// About four weeks from the tests' clocks, in Chromium's microseconds since 1601.
const EXPIRES_UTC: i64 = (1_762_500_000 + 11_644_473_600) * 1_000_000;
// What Claude leaves in each token cache after a Log out (E1b).
pub const EMPTIED: &str = "\"0123456789abcdefghijklmnopqr\"";

pub struct FakeClaude {
    scratch: PathBuf,
    host: Arc<MemoryHost>,
    jar: JarAs,
    mind: std::cell::RefCell<Mind>,
}

// What a running Claude holds in memory and writes as it quits.
#[derive(Default)]
struct Mind {
    // None: whoever the folder held when it opened. Some(None): logged out.
    session: Option<Option<Person>>,
    reported: bool,
}

const SETTINGS: &str = "config.json";
const SIDECAR: &str = "config.json.journal";
// A key Claude writes while it runs and takes out, through a sidecar, as it quits.
const RUNNING: &str = "updaterRunningInstance";
pub const STORES: [(&str, &str); 4] = [
    ("Local Storage/leveldb", "000003.log"),
    ("Session Storage", "000003.log"),
    (
        "IndexedDB/https_claude.ai_0.indexeddb.leveldb",
        "000003.log",
    ),
    ("WebStorage/QuotaManager", "db"),
];

impl FakeClaude {
    // `scratch` is the folder `Context::with_scratch_claude` was given.
    pub fn on(host: Arc<MemoryHost>, scratch: &Path, jar: JarAs) -> FakeClaude {
        FakeClaude {
            scratch: scratch.to_path_buf(),
            host,
            jar,
            mind: std::cell::RefCell::default(),
        }
    }

    pub fn data(&self) -> PathBuf {
        self.scratch.join("data")
    }

    pub fn program(&self) -> PathBuf {
        self.scratch.join("Claude.app/Contents/MacOS/Claude")
    }

    // Listed, its own program and a helper it started. It makes the stores that are missing
    // and writes a key of its own into its settings.
    pub fn opens(&self) {
        *self.mind.borrow_mut() = Mind::default();
        let data = self.data();
        std::fs::create_dir_all(&data).expect("a data folder");
        for (store, file) in STORES {
            if !data.join(store).exists() {
                std::fs::create_dir_all(data.join(store)).expect("a store");
                std::fs::write(data.join(store).join(file), "made by Claude").expect("a file");
            }
        }
        let mut pairs = self.settings();
        if !pairs.iter().any(|(key, _)| key == RUNNING) {
            pairs.push((RUNNING.into(), "true".into()));
            self.write_settings(&pairs);
        }
        self.lists();
    }

    fn lists(&self) {
        let helper = self.scratch.join(
            "Claude.app/Contents/Frameworks/Claude Helper (Renderer).app/Contents/MacOS/\
             Claude Helper (Renderer)",
        );
        let (program, helper) = (self.program(), helper);
        let program = program.to_string_lossy();
        let helper = helper.to_string_lossy();
        self.host.runs_at("Claude", &[program.as_ref()]);
        self.host
            .runs_at("Claude Helper (Renderer)", &[helper.as_ref()]);
        self.host.child_of(&helper, &program);
    }

    // Unlisted, writing nothing.
    pub fn crashes(&self) {
        self.host.runs_at("Claude", &[]);
        self.host.runs_at("Claude Helper (Renderer)", &[]);
    }

    // The session in memory, nothing on disk yet.
    pub fn signs_in(&self, who: &Person) {
        *self.mind.borrow_mut() = Mind {
            session: Some(Some(*who)),
            reported: false,
        };
    }

    // The account's name is written as Claude quits (E4).
    pub fn reports(&self) {
        self.mind.borrow_mut().reported = true;
    }

    // Another session value under the same name, as on 5 October: the account stays signed in.
    pub fn renews(&self, who: &Person, session: &'static str) {
        *self.mind.borrow_mut() = Mind {
            session: Some(Some(Person {
                uuid: who.uuid,
                session,
            })),
            reported: true,
        };
    }

    // The session gone, both caches the emptied value, the name kept (E1b).
    pub fn logs_out(&self) {
        *self.mind.borrow_mut() = Mind {
            session: Some(None),
            reported: false,
        };
    }

    // Writes the jar and `config.json` whole, then leaves its own key's removal in a valid
    // sidecar, as 2.7032.0 does. Unlisted (E8).
    pub fn quits(&self) {
        let data = self.data();
        let jar = data.join("Cookies");
        let mind = std::mem::take(&mut *self.mind.borrow_mut());
        let held = match mind.session {
            Some(session) => {
                write_jar(
                    &jar,
                    self.jar,
                    &session.map_or_else(Vec::new, |who| cookies(&who)),
                );
                std::fs::write(data.join("Cookies-journal"), b"").expect("a journal");
                Some(session)
            }
            None if !jar.exists() => {
                write_jar(&jar, self.jar, &[]);
                std::fs::write(data.join("Cookies-journal"), b"").expect("a journal");
                Some(None)
            }
            None => None,
        };
        let mut pairs = self.settings();
        match held {
            Some(Some(who)) => {
                set(&mut pairs, "oauth:tokenCache", who.tokens());
                set(&mut pairs, "oauth:tokenCacheV2", who.tokens());
                if mind.reported {
                    set(
                        &mut pairs,
                        "lastKnownAccountUuid",
                        format!("\"{}\"", who.uuid),
                    );
                }
            }
            Some(None) => {
                set(&mut pairs, "oauth:tokenCache", EMPTIED.into());
                set(&mut pairs, "oauth:tokenCacheV2", EMPTIED.into());
            }
            None => {}
        }
        self.write_settings(&pairs);
        let on_disk = std::fs::read(data.join(SETTINGS)).expect("settings");
        pairs.retain(|(key, _)| key != RUNNING);
        let body = body_of(&pairs);
        std::fs::write(
            data.join(SIDECAR),
            format!("{} {} .\n{body}", sha(body.as_bytes()), sha(&on_disk)),
        )
        .expect("a sidecar");
        self.crashes();
    }

    pub fn quits_before_reporting(&self) {
        self.mind.borrow_mut().reported = false;
        self.quits();
    }

    // A sidecar that names a `config.json` since written over: Claude drops it when it opens.
    pub fn leaves_a_stale_sidecar(&self) {
        let body = "{\n\t\"locale\": \"fr-FR\"\n}";
        std::fs::write(
            self.data().join(SIDECAR),
            format!("{} {} .\n{body}", sha(body.as_bytes()), sha(b"gone")),
        )
        .expect("a sidecar");
    }

    // The settings as Claude holds them: a valid sidecar's body, else the file's.
    fn settings(&self) -> Vec<(String, String)> {
        let data = self.data();
        let on_disk = std::fs::read(data.join(SETTINGS)).ok();
        let pending = std::fs::read_to_string(data.join(SIDECAR))
            .ok()
            .and_then(|written| {
                let (header, body) = written.split_once('\n')?;
                let fields: Vec<&str> = header.split(' ').collect();
                let file = on_disk.as_deref().map_or_else(|| "-".to_string(), sha);
                (fields[0] == sha(body.as_bytes()) && fields[1] == file).then(|| body.to_string())
            });
        let _ = std::fs::remove_file(data.join(SIDECAR));
        let raw = pending
            .or_else(|| on_disk.map(|bytes| String::from_utf8(bytes).expect("UTF-8")))
            .unwrap_or_else(|| "{}".into());
        let parsed: serde_json::Map<String, serde_json::Value> =
            serde_json::from_str(&raw).expect("settings are JSON");
        parsed
            .into_iter()
            .map(|(key, value)| (key, value.to_string()))
            .collect()
    }

    fn write_settings(&self, pairs: &[(String, String)]) {
        let path = self.data().join(SETTINGS);
        let fresh = !path.exists();
        std::fs::write(&path, body_of(pairs)).expect("settings");
        if fresh {
            let _ = crate::host::fs::testing::make_private(&path);
        }
    }

    // A whole signed-in data folder, as Claude leaves one when it quits.
    pub fn signed_in_as(&self, who: &Person) {
        plant(&self.data(), who, self.jar);
    }
}

fn sha(bytes: &[u8]) -> String {
    use sha2::{Digest, Sha256};
    hex::encode(Sha256::digest(bytes))
}

fn set(pairs: &mut Vec<(String, String)>, key: &str, value: String) {
    match pairs.iter_mut().find(|(held, _)| held == key) {
        Some((_, held)) => *held = value,
        None => pairs.push((key.into(), value)),
    }
}

fn body_of(pairs: &[(String, String)]) -> String {
    let lines: Vec<String> = pairs
        .iter()
        .map(|(key, value)| format!("\t{}: {value}", serde_json::Value::from(key.as_str())))
        .collect();
    format!("{{\n{}\n}}", lines.join(",\n"))
}

fn cookies(who: &Person) -> Vec<Cookie> {
    vec![
        Cookie {
            host: ".claude.ai".into(),
            name: "sessionKey".into(),
            path: "/".into(),
            stored: who.stored(),
            expires_utc: EXPIRES_UTC,
        },
        Cookie {
            host: ".claude.ai".into(),
            name: "lastActiveOrg".into(),
            path: "/".into(),
            stored: b"v10org".to_vec(),
            expires_utc: EXPIRES_UTC,
        },
    ]
}

pub fn plant(folder: &Path, who: &Person, jar: JarAs) {
    std::fs::create_dir_all(folder).expect("a scratch data folder");
    write_jar(&folder.join("Cookies"), jar, &cookies(who));
    std::fs::write(folder.join("Cookies-journal"), b"").expect("a journal");
    for (store, file) in STORES.into_iter().chain([("Cache/Cache_Data", "index")]) {
        std::fs::create_dir_all(folder.join(store)).expect("a store");
        std::fs::write(folder.join(store).join(file), who.session).expect("a store's file");
    }
    std::fs::write(folder.join("Preferences"), b"{}").expect("preferences");
    let settings = folder.join("config.json");
    std::fs::write(
        &settings,
        format!(
            "{{\n\t\"locale\": \"en-US\",\n\t\"oauth:tokenCache\": {tokens},\n\t\
             \"userThemeMode\": \"dark\",\n\t\"oauth:tokenCacheV2\": {tokens},\n\t\
             \"lastKnownAccountUuid\": \"{uuid}\"\n}}",
            tokens = who.tokens(),
            uuid = who.uuid
        ),
    )
    .expect("settings");
    // Not the mode a new file gets, so a write that loses it shows. Windows sets none yet.
    let _ = crate::host::fs::testing::make_private(&settings);
}

pub fn write_jar(path: &Path, jar: JarAs, cookies: &[Cookie]) {
    match jar {
        JarAs::Sqlite => crate::host::write_jar(path, cookies).expect("a jar"),
        JarAs::StandIn => write_stand_in_jar(path, 24, cookies),
    }
}

pub fn write_stand_in_jar(path: &Path, compatible: i64, cookies: &[Cookie]) {
    let jar = StandInJar {
        version: compatible,
        compatible,
        cookies: cookies
            .iter()
            .map(|cookie| StandInCookie {
                host: cookie.host.clone(),
                name: cookie.name.clone(),
                path: cookie.path.clone(),
                stored: hex::encode(&cookie.stored),
                expires_utc: cookie.expires_utc,
            })
            .collect(),
    };
    std::fs::write(path, serde_json::to_vec(&jar).expect("JSON")).expect("a stand-in jar");
}
