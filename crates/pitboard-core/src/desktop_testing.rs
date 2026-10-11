// The Claude Desktop the tests play, on a scratch folder. Nothing here touches the real one.

use crate::host::Cookie;
use crate::host::memory::{MemoryHost, StandInCookie, StandInJar};
use std::path::{Path, PathBuf};
use std::sync::Arc;

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
}

impl FakeClaude {
    // `scratch` is the folder `Context::with_scratch_claude` was given.
    pub fn on(host: Arc<MemoryHost>, scratch: &Path, jar: JarAs) -> FakeClaude {
        FakeClaude {
            scratch: scratch.to_path_buf(),
            host,
            jar,
        }
    }

    pub fn data(&self) -> PathBuf {
        self.scratch.join("data")
    }

    pub fn program(&self) -> PathBuf {
        self.scratch.join("Claude.app/Contents/MacOS/Claude")
    }

    // Listed, its own program and a helper it started.
    pub fn opens(&self) {
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

    // A whole signed-in data folder, as Claude leaves one when it quits.
    pub fn signed_in_as(&self, who: &Person) {
        plant(&self.data(), who, self.jar);
    }
}

pub fn plant(folder: &Path, who: &Person, jar: JarAs) {
    std::fs::create_dir_all(folder).expect("a scratch data folder");
    write_jar(
        &folder.join("Cookies"),
        jar,
        &[
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
        ],
    );
    std::fs::write(folder.join("Cookies-journal"), b"").expect("a journal");
    for (store, file) in [
        ("Local Storage/leveldb", "000003.log"),
        ("Session Storage", "000003.log"),
        (
            "IndexedDB/https_claude.ai_0.indexeddb.leveldb",
            "000003.log",
        ),
        ("WebStorage/QuotaManager", "db"),
        ("Cache/Cache_Data", "index"),
    ] {
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
