// Another process for a test: this test program run again as the one test
// `helper::tests::a_helper_serves`, told its job and its file through its environment. It
// says so on its standard output once its job is under way, and stops once its standard input
// closes.

use std::io::{BufRead, BufReader, Read};
use std::path::Path;
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};

const JOB: &str = "PITBOARD_TEST_HELPER_JOB";
const FILE: &str = "PITBOARD_TEST_HELPER_FILE";
const UNDER_WAY: &str = "pitboard-helper: under way";
const SAID: &str = "pitboard-helper: ";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Job {
    // An exclusive lock on the file, as another Pitboard run holds one of its own.
    Locks,
    // The file open to read, sharing its reading and writing but not its deletion, as some
    // editors and scanners open one.
    #[cfg(windows)]
    HoldsWithoutDelete,
    // The file read whole, again and again, opened as std opens one.
    Reads,
}

impl Job {
    fn code(self) -> &'static str {
        match self {
            Job::Locks => "locks",
            #[cfg(windows)]
            Job::HoldsWithoutDelete => "holds-without-delete",
            Job::Reads => "reads",
        }
    }

    fn named(code: &str) -> Option<Job> {
        [
            Job::Locks,
            #[cfg(windows)]
            Job::HoldsWithoutDelete,
            Job::Reads,
        ]
        .into_iter()
        .find(|job| job.code() == code)
    }
}

pub(crate) struct Helper {
    child: Child,
    stdin: Option<ChildStdin>,
    stdout: BufReader<ChildStdout>,
}

impl Helper {
    pub(crate) fn start(job: Job, file: &Path) -> Helper {
        let mut child = Command::new(std::env::current_exe().expect("this test's program"))
            .args([
                "helper::tests::a_helper_serves",
                "--exact",
                "--nocapture",
                "--test-threads=1",
            ])
            .env(JOB, job.code())
            .env(FILE, file)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .spawn()
            .expect("the helper starts");
        let stdin = child.stdin.take();
        let mut stdout = BufReader::new(child.stdout.take().expect("its output"));
        let mut line = String::new();
        // libtest names the test on the same line, before the helper speaks.
        while !line.contains(UNDER_WAY) {
            line.clear();
            let read = stdout.read_line(&mut line).expect("the helper's output");
            assert!(read > 0, "the helper ended before its job was under way");
        }
        Helper {
            child,
            stdin,
            stdout,
        }
    }

    // What the helper said once it stopped.
    pub(crate) fn stop(mut self) -> String {
        drop(self.stdin.take());
        let mut rest = String::new();
        self.stdout
            .read_to_string(&mut rest)
            .expect("the helper's output");
        let ended = self.child.wait().expect("the helper ends");
        assert!(ended.success(), "the helper failed: {rest}");
        rest.lines()
            .filter_map(|line| line.split_once(SAID).map(|(_, said)| said.to_owned()))
            .collect::<Vec<_>>()
            .join("\n")
    }
}

impl Drop for Helper {
    fn drop(&mut self) {
        drop(self.stdin.take());
        let _ = self.child.wait();
    }
}

// A folder on an exFAT volume, which keeps no POSIX rename and no access list. On CI,
// test-as-standard-user.ps1's -ExFat mounts one and names it in PITBOARD_TEST_VOLUME.
#[cfg(windows)]
#[allow(
    clippy::disallowed_methods,
    reason = "CI names the volume in the test's environment"
)]
pub(crate) fn exfat(name: &str) -> std::path::PathBuf {
    let volume = std::env::var_os("PITBOARD_TEST_VOLUME")
        .expect("PITBOARD_TEST_VOLUME names a folder on an exFAT volume");
    let folder = Path::new(&volume).join(format!("{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&folder);
    folder
}

// What a reading helper counted: its reads, and of them the whole, torn, missing and failed.
#[cfg(windows)]
pub(crate) fn counts(said: &str) -> [u64; 5] {
    let first = said.lines().next().unwrap_or_default();
    let numbers: Vec<u64> = first
        .split_whitespace()
        .filter_map(|word| word.parse().ok())
        .collect();
    numbers
        .try_into()
        .unwrap_or_else(|_| panic!("a reading helper's counts: {said}"))
}

// A whole record of `round`, as long as a login: the round eight bytes at a time.
pub(crate) fn record(round: u64) -> Vec<u8> {
    round.to_le_bytes().repeat(512)
}

fn whole(read: &[u8]) -> bool {
    read.len() == 4096 && read.chunks(8).all(|chunk| chunk == &read[..8])
}

#[cfg(test)]
#[allow(
    clippy::disallowed_methods,
    reason = "a helper is told its job through its environment, and holds a file as another program does"
)]
mod tests {
    use super::*;
    use std::sync::Arc;
    use std::sync::atomic::{AtomicBool, Ordering};

    #[test]
    fn a_helper_serves() {
        let (Some(job), Some(file)) = (std::env::var_os(JOB), std::env::var_os(FILE)) else {
            return;
        };
        let job = Job::named(&job.to_string_lossy()).expect("a job this helper knows");
        let file = std::path::PathBuf::from(file);
        let closed = Arc::new(AtomicBool::new(false));
        let watched = Arc::clone(&closed);
        let watching = std::thread::spawn(move || {
            let _ = std::io::stdin().read_to_end(&mut Vec::new());
            watched.store(true, Ordering::Release);
        });
        let said = match job {
            Job::Locks => {
                let held = std::fs::OpenOptions::new()
                    .read(true)
                    .write(true)
                    .create(true)
                    .truncate(false)
                    .open(&file)
                    .expect("the file to lock");
                held.lock().expect("locked");
                println!("{UNDER_WAY}");
                watching.join().expect("watched");
                String::new()
            }
            #[cfg(windows)]
            Job::HoldsWithoutDelete => {
                use std::os::windows::fs::OpenOptionsExt;
                use windows_sys::Win32::Storage::FileSystem::{FILE_SHARE_READ, FILE_SHARE_WRITE};
                let _held = std::fs::OpenOptions::new()
                    .read(true)
                    .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE)
                    .open(&file)
                    .expect("the file to hold");
                println!("{UNDER_WAY}");
                watching.join().expect("watched");
                String::new()
            }
            Job::Reads => {
                println!("{UNDER_WAY}");
                let (mut reads, mut whole_reads, mut torn, mut missing) = (0u64, 0u64, 0u64, 0u64);
                let mut failed = Vec::new();
                while !closed.load(Ordering::Acquire) {
                    reads += 1;
                    match std::fs::read(&file) {
                        Ok(read) if whole(&read) => whole_reads += 1,
                        Ok(_) => torn += 1,
                        Err(e) if e.kind() == std::io::ErrorKind::NotFound => missing += 1,
                        Err(e) => failed.push(e.to_string()),
                    }
                }
                let mut said = format!(
                    "reads {reads} whole {whole_reads} torn {torn} missing {missing} failed {}",
                    failed.len()
                );
                failed.sort();
                failed.dedup();
                for each in failed {
                    said.push_str(&format!("\n{SAID}{each}"));
                }
                said
            }
        };
        println!("{SAID}{said}");
    }

    #[test]
    fn a_record_is_whole_only_as_written() {
        let one = record(7);
        assert!(whole(&one));
        assert!(!whole(&one[..100]));
        assert!(!whole(&[]));
        let mut mixed = record(7);
        mixed[2048..].copy_from_slice(&record(8)[2048..]);
        assert!(!whole(&mixed));
    }

    #[test]
    fn a_held_lock_is_held_until_the_helper_stops() {
        let dir = tempfile::tempdir().expect("a scratch folder");
        let file = dir.path().join("state.lock");
        let helper = Helper::start(Job::Locks, &file);
        let mine = std::fs::File::open(&file).expect("opened");
        assert!(matches!(
            mine.try_lock_shared(),
            Err(std::fs::TryLockError::WouldBlock)
        ));
        assert_eq!(helper.stop(), "");
        mine.try_lock_shared()
            .expect("free once the helper stopped");
    }
}
