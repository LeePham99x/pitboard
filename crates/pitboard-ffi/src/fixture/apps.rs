//! What a fixture's model asks of the app's own system, played by the fixture: the other
//! apps on its machine, and notifications, which it posts none of.

use crate::model::{AppControl, Notifications, PlatformError, RunOutNotice};
use pitboard_core::testing::MemoryHost;
use pitboard_core::testing::desktop::FakeClaude;
use std::collections::BTreeSet;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

/// ChatGPT, as the core's holder detection names it: its bundle id.
pub(crate) const CHATGPT: &str = "com.openai.codex";

pub(crate) const CLAUDE: &str = "com.anthropic.claudefordesktop";

/// Where ChatGPT runs its own `codex`, as the process list of a Mac running ChatGPT
/// 26.928.31416 gave it, recorded in pitboard-core's `provider::codex::holders`: what tells
/// the core the app is open. Two of them, as there were.
const CHATGPT_CODEX: &str =
    "/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex";

// Long enough for a UI test to see the waiting page under CI's load.
pub(super) const SIGNS_IN_AFTER: Duration = Duration::from_secs(5);
pub(super) const QUITS_AFTER: Duration = Duration::from_secs(2);

/// The apps running on a fixture's machine. The core sees ChatGPT's `codex` in the machine's
/// process list while it runs, and the model quits and opens it through this, so the two
/// agree the way the process list and the system's list of apps agree on a real machine.
/// Nothing here touches an app of the machine running the fixture.
pub(crate) struct FixtureApps {
    host: Arc<MemoryHost>,
    // played over the fixture's machine, writing its data folder as 2.7032.0 does as it quits
    claude: Arc<FakeClaude>,
    held: Arc<Mutex<Held>>,
}

#[derive(Default)]
struct Held {
    running: BTreeSet<String>,
    /// Every app asked to quit and every app opened, in order: "quit com.openai.codex".
    asked: Vec<String>,
    // how many times Claude has opened or quit: a sign-in played in it ends as it quits
    claude_turns: u64,
}

fn held(held: &Mutex<Held>) -> MutexGuard<'_, Held> {
    // Every change here is whole whenever the lock is let go.
    held.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

impl FixtureApps {
    /// The apps of the machine `host` is, `running` among them from the start.
    pub(crate) fn new(
        host: Arc<MemoryHost>,
        claude: Arc<FakeClaude>,
        running: &[&str],
    ) -> Arc<FixtureApps> {
        let apps = Arc::new(FixtureApps {
            host,
            claude,
            held: Arc::default(),
        });
        for app in running {
            apps.started(app);
        }
        apps
    }

    /// Every app asked to quit and every app opened, in order.
    #[cfg(test)]
    pub(crate) fn asked(&self) -> Vec<String> {
        held(&self.held).asked.clone()
    }

    /// Whether `app` is running.
    pub(crate) fn is_running(&self, app: &str) -> bool {
        held(&self.held).running.contains(app)
    }

    /// Where an app of the fixture's is, which is nowhere on the machine running it.
    fn copy_of(app: &str) -> String {
        format!("/fixture/{app}.app")
    }

    // The turn Claude is on once this has run.
    fn started(&self, app: &str) -> u64 {
        let mut now = held(&self.held);
        now.running.insert(app.to_owned());
        if app == CLAUDE {
            now.claude_turns += 1;
        }
        let turn = now.claude_turns;
        drop(now);
        match app {
            CHATGPT => self.host.runs_at("codex", &[CHATGPT_CODEX, CHATGPT_CODEX]),
            CLAUDE => self.claude.opens(),
            _ => {}
        }
        turn
    }
}

impl AppControl for FixtureApps {
    fn running(&self, app: String) -> Result<Option<String>, PlatformError> {
        Ok(self.is_running(&app).then(|| FixtureApps::copy_of(&app)))
    }

    /// Quits at once, as an app with no work in progress does, and its processes leave the
    /// process list with it.
    fn request_quit(&self, app: String) -> Result<(), PlatformError> {
        let mut asked = held(&self.held);
        asked.asked.push(format!("quit {app}"));
        asked.running.remove(&app);
        if app == CLAUDE {
            asked.claude_turns += 1;
        }
        drop(asked);
        match app.as_str() {
            CHATGPT => self.host.runs_at("codex", &[]),
            CLAUDE => self.claude.quits(),
            _ => {}
        }
        Ok(())
    }

    // Opened in front on a folder signed out, Claude is a person signing in and quitting.
    fn reopen(&self, location: String, front: bool) -> Result<(), PlatformError> {
        let app = location
            .strip_prefix("/fixture/")
            .and_then(|rest| rest.strip_suffix(".app"))
            .unwrap_or(&location)
            .to_owned();
        let opened = if front { "open-front" } else { "open" };
        held(&self.held).asked.push(format!("{opened} {app}"));
        let signs_in = app == CLAUDE && front && !self.claude.holds_a_session();
        let turn = self.started(&app);
        if signs_in {
            let (claude, who, apps) = (
                Arc::clone(&self.claude),
                pitboard_core::testing::desktop::TRAVEL,
                Arc::clone(&self.held),
            );
            // Held throughout, so Pitboard's quit or open comes before or after each step.
            let still = move |now: &Held| now.claude_turns == turn && now.running.contains(CLAUDE);
            std::thread::spawn(move || {
                std::thread::sleep(SIGNS_IN_AFTER);
                let now = held(&apps);
                if !still(&now) {
                    return;
                }
                claude.signs_in(&who);
                claude.reports();
                drop(now);
                std::thread::sleep(QUITS_AFTER);
                let mut now = held(&apps);
                if still(&now) {
                    now.running.remove(CLAUDE);
                    now.claude_turns += 1;
                    claude.quits();
                }
            });
        }
        Ok(())
    }
}

/// Notifications, of which a fixture posts none: the person running the tests is asked for
/// nothing, a notification's permission included.
pub(crate) struct Unposted;

impl Notifications for Unposted {
    fn post(&self, _notice: RunOutNotice) -> Result<(), PlatformError> {
        Ok(())
    }
}
