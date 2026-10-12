//! A machine in memory, where a test can make anything fail.
//!
//! It fakes what a real host offers and nothing above it, so the code under test is the
//! real one. Before this it faked `read_signin` with a map keyed by directory, which meant
//! no test ever ran Claude Code's own slot hashing on the sign-in path: the double answered
//! the question the code was supposed to answer.

use super::{
    Administered, Cookie, Elevation, Floor, Host, Jar, JarError, Listed, Process, Scheduler,
};
use crate::context::Context;
use crate::service::Permit;
use crate::store::memory::{Fault, MemoryStore, Touch};
use crate::store::{Backend, Cost, Error, RawStore};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};

/// The vault or the keychain as one context opens it. A real keychain takes
/// `PITBOARD_NO_ARGV` from the context it is opened with, so above its ceiling it writes on
/// the argument line or refuses; this one does the same.
struct Opened {
    store: Arc<MemoryStore>,
    argument_line: bool,
}

impl RawStore for Opened {
    fn kind(&self) -> Backend {
        RawStore::kind(&self.store)
    }

    fn contains(&self, service: &str) -> Result<bool, Error> {
        RawStore::contains(&self.store, service)
    }

    fn read(&self, service: &str) -> Result<Option<String>, Error> {
        RawStore::read(&self.store, service)
    }

    fn write(&self, permit: Permit, service: &str, contents: &str) -> Result<(), Error> {
        if let Some(cost) = self.cost(service, contents)
            && cost.refused()
        {
            return Err(Error::Write(format!(
                "this credential is {} bytes, past the {}-byte command limit",
                cost.needs, cost.limit
            )));
        }
        RawStore::write(&self.store, permit, service, contents)
    }

    fn delete(&self, permit: Permit, service: &str) -> Result<(), Error> {
        RawStore::delete(&self.store, permit, service)
    }

    fn list(&self) -> Result<Option<Vec<String>>, Error> {
        RawStore::list(&self.store)
    }

    fn cost(&self, service: &str, contents: &str) -> Option<Cost> {
        RawStore::cost(&self.store, service, contents).map(|cost| Cost {
            second_route: self.argument_line,
            ..cost
        })
    }
}

/// A machine whose keychain and filesystem are both in memory, and whose scheduler writes
/// its files in the test's own home and asks no service manager to start them.
#[derive(Debug)]
pub struct MemoryHost {
    keychain: Arc<MemoryStore>,
    vault: Arc<MemoryStore>,
    files: Mutex<HashMap<PathBuf, Arc<MemoryStore>>>,
    trail: Arc<Mutex<Vec<Touch>>>,
    running: Mutex<HashMap<String, Vec<Process>>>,
    /// Whether the process list cannot be read here, which is not the same as nothing
    /// running.
    listless: AtomicBool,
    // the process that started each, by path; 1 where nothing is said
    parents: Mutex<HashMap<PathBuf, PathBuf>>,
    // each path listed from the `listing` call of that number on
    later: Mutex<Vec<(usize, PathBuf)>>,
    listings: AtomicUsize,
    jars_asked: Mutex<Vec<PathBuf>>,
    jar_faults: Mutex<HashMap<PathBuf, JarFault>>,
    versions: Mutex<HashMap<PathBuf, String>>,
    /// Whether every home parks in `vault`, the way every home on macOS parks in the login
    /// keychain. So by default, because that is where the rules about another Pitboard's
    /// parks are needed.
    shared_vault: AtomicBool,
    /// This system's scheduler, writing in the test's own home, where Pitboard writes for one
    /// on this system: none on Windows until W25.
    scheduler: Option<Box<dyn Scheduler>>,
    /// Whether the scheduler refuses the next schedule it is asked to start.
    refuse_start: Arc<AtomicBool>,
    /// Whether this machine has no scheduler at all.
    unscheduled: AtomicBool,
    /// Whether this process runs as the person, as this machine says it does.
    elevation: Mutex<Elevation>,
    floor: Mutex<Floor>,
    /// The files only an administrator writes, by path, as a test said. Unset otherwise.
    administered: Mutex<HashMap<PathBuf, Administered>>,
    /// The managed preferences a profile forces, by domain and key, as a test said.
    forced: Mutex<HashMap<(String, String), Administered>>,
}

impl Default for MemoryHost {
    fn default() -> MemoryHost {
        let refuse_start = Arc::new(AtomicBool::new(false));
        let trail = Arc::default();
        MemoryHost {
            // Keychain, because that is the chain the interesting rules are written for.
            keychain: MemoryStore::on_trail(Backend::Keychain, "live".into(), Arc::clone(&trail)),
            vault: MemoryStore::on_trail(Backend::Keychain, "vault".into(), Arc::clone(&trail)),
            files: Mutex::new(HashMap::new()),
            trail,
            running: Mutex::new(HashMap::new()),
            listless: AtomicBool::new(false),
            parents: Mutex::new(HashMap::new()),
            later: Mutex::new(Vec::new()),
            listings: AtomicUsize::new(0),
            jars_asked: Mutex::new(Vec::new()),
            jar_faults: Mutex::new(HashMap::new()),
            versions: Mutex::new(HashMap::new()),
            shared_vault: AtomicBool::new(true),
            scheduler: super::os::pretend_scheduler(Arc::clone(&refuse_start)),
            refuse_start,
            unscheduled: AtomicBool::new(false),
            elevation: Mutex::new(Elevation::Normal),
            floor: Mutex::new(Floor::Met),
            administered: Mutex::new(HashMap::new()),
            forced: Mutex::new(HashMap::new()),
        }
    }
}

impl MemoryHost {
    pub fn new() -> Arc<MemoryHost> {
        Arc::new(MemoryHost::default())
    }

    /// The keychain, where a tool that uses one keeps its live credential.
    pub fn live(&self) -> &Arc<MemoryStore> {
        &self.keychain
    }

    /// Where Pitboard's parked logins are.
    pub fn vault(&self) -> &Arc<MemoryStore> {
        &self.vault
    }

    /// The file at `path`, where a tool that keeps its login in a file keeps it.
    pub fn file_at(&self, path: PathBuf) -> Arc<MemoryStore> {
        let name = format!("file {}", path.display());
        Arc::clone(
            self.files
                .lock()
                .expect("a poisoned test host is a failed test")
                .entry(path)
                .or_insert_with(|| {
                    MemoryStore::on_trail(Backend::File, name, Arc::clone(&self.trail))
                }),
        )
    }

    pub fn trail(&self) -> Vec<Touch> {
        self.trail
            .lock()
            .expect("a poisoned test host is a failed test")
            .clone()
    }

    /// Locks the keychain from now on, as a screen lock does on macOS, where Claude Code's
    /// login and Pitboard's vault are items in the one login keychain.
    pub fn lock_keychain(&self) {
        self.keychain.fault_all(Fault::Locked);
        self.vault.fault_all(Fault::Locked);
    }

    /// Unlocks what [`MemoryHost::lock_keychain`] locked.
    pub fn unlock_keychain(&self) {
        self.keychain.heal_all();
        self.vault.heal_all();
    }

    /// From now on the vault belongs to one home alone, the way Pitboard's vault of files
    /// does off macOS.
    pub fn vault_of_its_own(&self) {
        self.shared_vault.store(false, Ordering::SeqCst);
    }

    /// Say that `count` processes are running `program`, each started by its bare name, the
    /// way a shell starts one from `PATH`.
    pub fn runs(&self, program: &str, count: usize) {
        self.runs_at(program, &vec![program; count]);
    }

    /// Say that a process is running `program` from each of `paths`.
    pub fn runs_at(&self, program: &str, paths: &[&str]) {
        let processes = (1..)
            .zip(paths)
            .map(|(pid, path)| Process {
                pid,
                path: PathBuf::from(path),
            })
            .collect();
        self.running
            .lock()
            .expect("a poisoned test host is a failed test")
            .insert(program.to_string(), processes);
    }

    /// From now on the process list cannot be read here, as when the system refuses to say
    /// what is running: nobody can tell whether anything runs a program, or what.
    pub fn without_a_process_list(&self) {
        self.listless.store(true, Ordering::SeqCst);
    }

    // The process running `child` was started by the one running `parent`, by path.
    pub fn child_of(&self, child: &str, parent: &str) {
        locked(&self.parents).insert(child.into(), parent.into());
    }

    // `path` is listed from the `n`th `listing` after this: an app opened partway through.
    pub fn listed_from(&self, n: usize, path: &str) {
        let from = self.listings() + n;
        locked(&self.later).push((from, path.into()));
    }

    pub fn listings(&self) -> usize {
        self.listings.load(Ordering::SeqCst)
    }

    pub fn jars_asked(&self) -> Vec<PathBuf> {
        locked(&self.jars_asked).clone()
    }

    pub fn jar_unsettled(&self, path: &Path) {
        locked(&self.jar_faults).insert(path.into(), JarFault::Unsettled);
    }

    pub fn jar_unknown(&self, path: &Path, why: &str) {
        locked(&self.jar_faults).insert(path.into(), JarFault::Unknown(why.into()));
    }

    pub fn app_at(&self, bundle: &Path, version: &str) {
        locked(&self.versions).insert(bundle.into(), version.into());
    }

    /// The next time a schedule is to be started, the system will not start it.
    pub fn refuse_next_start(&self) {
        self.refuse_start.store(true, Ordering::SeqCst);
    }

    /// From now on there is no scheduler here.
    pub fn without_a_scheduler(&self) {
        self.unscheduled.store(true, Ordering::SeqCst);
    }

    /// From now on this machine says this process runs with `elevation`: as root, under
    /// sudo, or with rights it cannot tell, on whatever system the tests run on. It runs as
    /// the person until a test says otherwise.
    pub fn runs_with(&self, elevation: Elevation) {
        *self
            .elevation
            .lock()
            .expect("a poisoned test host is a failed test") = elevation;
    }

    pub fn runs_on(&self, floor: Floor) {
        *self
            .floor
            .lock()
            .expect("a poisoned test host is a failed test") = floor;
    }

    /// Say an administrator wrote `contents` to the file at `path`, outside every home, such
    /// as Codex's `/etc/codex/requirements.toml`. Nothing is written anywhere.
    pub fn administers(&self, path: impl Into<PathBuf>, contents: &str) {
        self.administer(path.into(), Administered::Set(contents.into()));
    }

    /// Say the file at `path` an administrator writes is there and cannot be read, `why`.
    pub fn administers_unreadably(&self, path: impl Into<PathBuf>, why: &str) {
        self.administer(path.into(), Administered::Unreadable(why.into()));
    }

    fn administer(&self, path: PathBuf, holds: Administered) {
        self.administered
            .lock()
            .expect("a poisoned test host is a failed test")
            .insert(path, holds);
    }

    /// Say an administrator's configuration profile forces `key` of `domain` to `value`, a
    /// managed preference, on whatever system the tests run on.
    pub fn forces(&self, domain: &str, key: &str, value: &str) {
        self.forced
            .lock()
            .expect("a poisoned test host is a failed test")
            .insert(
                (domain.to_string(), key.to_string()),
                Administered::Set(value.into()),
            );
    }
}

impl Host for MemoryHost {
    fn foreign_secrets(&self, ctx: &Context, _account: &str) -> Option<Box<dyn RawStore>> {
        Some(Box::new(Opened {
            store: Arc::clone(&self.keychain),
            argument_line: ctx.argv_fallback(),
        }))
    }

    fn file(&self, path: PathBuf) -> Box<dyn RawStore> {
        Box::new(self.file_at(path))
    }

    fn vault(&self, ctx: &Context) -> Box<dyn RawStore> {
        Box::new(Opened {
            store: Arc::clone(&self.vault),
            argument_line: ctx.argv_fallback(),
        })
    }

    fn vault_is_shared(&self) -> bool {
        self.shared_vault.load(Ordering::SeqCst)
    }

    // What `runs`, `runs_at` and `listed_from` said, each with a pid of its own.
    fn listing(&self) -> Result<Vec<Listed>, String> {
        if self.listless.load(Ordering::SeqCst) {
            self.listings.fetch_add(1, Ordering::SeqCst);
            return Err("a test withheld the process list".into());
        }
        // Counted under the lock `runs_at` takes: a listing a test sees counted read what ran.
        let (call, mut paths) = {
            let running = locked(&self.running);
            let mut programs: Vec<&String> = running.keys().collect();
            programs.sort();
            let paths: Vec<PathBuf> = programs
                .into_iter()
                .flat_map(|program| running[program].iter().map(|p| p.path.clone()))
                .collect();
            (self.listings.fetch_add(1, Ordering::SeqCst) + 1, paths)
        };
        paths.extend(
            locked(&self.later)
                .iter()
                .filter(|(from, _)| call >= *from)
                .map(|(_, path)| path.clone()),
        );
        let pid = |index: usize| 1000 + u32::try_from(index).unwrap_or(u32::MAX - 1000);
        let parents = locked(&self.parents);
        Ok(paths
            .iter()
            .enumerate()
            .map(|(index, path)| Listed {
                pid: pid(index),
                parent: parents
                    .get(path)
                    .and_then(|parent| paths.iter().position(|p| p == parent))
                    .map_or(1, pid),
                path: path.clone(),
            })
            .collect())
    }

    // Read as JSON, a real file on the scratch disk, so a rename carries it.
    fn cookies(&self, jar: &Path, hosts: &[&str], names: &[&str]) -> Result<Jar, JarError> {
        locked(&self.jars_asked).push(jar.to_path_buf());
        match locked(&self.jar_faults).get(jar) {
            Some(JarFault::Unsettled) => {
                return Err(JarError::Unsettled("a test held the jar".into()));
            }
            Some(JarFault::Unknown(why)) => return Err(JarError::Unknown(why.clone())),
            None => {}
        }
        let raw = match std::fs::read(jar) {
            Ok(raw) => raw,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Err(JarError::Absent),
            Err(e) => return Err(JarError::Unreadable(e.to_string())),
        };
        let stand_in: StandInJar =
            serde_json::from_slice(&raw).map_err(|e| JarError::Unknown(e.to_string()))?;
        Ok(Jar {
            version: stand_in.version,
            compatible: stand_in.compatible,
            cookies: stand_in
                .cookies
                .into_iter()
                .filter(|c| hosts.contains(&c.host.as_str()) || names.contains(&c.name.as_str()))
                .map(|c| {
                    Ok(Cookie {
                        stored: hex::decode(&c.stored)
                            .map_err(|e| JarError::Unknown(e.to_string()))?,
                        host: c.host,
                        name: c.name,
                        path: c.path,
                        expires_utc: c.expires_utc,
                    })
                })
                .collect::<Result<_, _>>()?,
        })
    }

    fn app_version(&self, bundle: &Path) -> Option<String> {
        locked(&self.versions).get(bundle).cloned()
    }

    /// What a test said is running, and nothing on the machine running the tests. `None`
    /// once a test has said the list cannot be read.
    // Not over `listing`: `doctor` asks this on every machine, and it must not count as one.
    fn processes(&self, program: &str) -> Option<Vec<Process>> {
        if self.listless.load(Ordering::SeqCst) {
            return None;
        }
        Some(
            self.running
                .lock()
                .expect("a poisoned test host is a failed test")
                .get(program)
                .cloned()
                .unwrap_or_default(),
        )
    }

    fn scheduler(&self) -> Option<&dyn Scheduler> {
        self.scheduler
            .as_deref()
            .filter(|_| !self.unscheduled.load(Ordering::SeqCst))
    }

    /// What a test said, and nothing about the process running the tests.
    fn elevation(&self, _ctx: &Context) -> Elevation {
        *self
            .elevation
            .lock()
            .expect("a poisoned test host is a failed test")
    }

    fn floor(&self) -> Floor {
        *self
            .floor
            .lock()
            .expect("a poisoned test host is a failed test")
    }

    /// What a test said an administrator wrote, and nothing on the machine running the tests.
    fn administered_file(&self, path: &Path) -> Administered {
        self.administered
            .lock()
            .expect("a poisoned test host is a failed test")
            .get(path)
            .cloned()
            .unwrap_or(Administered::Unset)
    }

    /// What a test said a profile forces, on whatever system the tests run on.
    fn managed_preference(&self, domain: &str, key: &str) -> Administered {
        self.forced
            .lock()
            .expect("a poisoned test host is a failed test")
            .get(&(domain.to_string(), key.to_string()))
            .cloned()
            .unwrap_or(Administered::Unset)
    }
}

#[derive(Debug)]
enum JarFault {
    Unsettled,
    Unknown(String),
}

// What `MemoryHost::cookies` reads: `desktop_testing` writes it with `JarAs::StandIn`.
#[derive(serde::Serialize, serde::Deserialize)]
pub(crate) struct StandInJar {
    pub version: i64,
    pub compatible: i64,
    pub cookies: Vec<StandInCookie>,
}

#[derive(serde::Serialize, serde::Deserialize)]
pub(crate) struct StandInCookie {
    pub host: String,
    pub name: String,
    pub path: String,
    // hex
    pub stored: String,
    pub expires_utc: i64,
}

fn locked<T>(mutex: &Mutex<T>) -> std::sync::MutexGuard<'_, T> {
    mutex.lock().expect("a poisoned test host is a failed test")
}
