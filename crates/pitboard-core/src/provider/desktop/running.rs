use super::paths::REAL;
use crate::context::Context;
use crate::host::Listed;
use std::path::Path;

const NATIVE_HOST: &str = "Contents/Helpers/chrome-native-host";

// A build for tests counts its scratch app alone, never a developer's own Claude.
pub(super) fn is_the_app(ctx: &Context, root: &Path, one: &Listed, all: &[Listed]) -> bool {
    let scratch = ctx.scratch_claude();
    let in_app = |path: &Path| {
        if REAL {
            in_any_claude_app(path)
        } else {
            scratch.is_some_and(|scratch| path.starts_with(&scratch.app))
        }
    };
    counts(&in_app, root, one, all)
}

fn in_any_claude_app(path: &Path) -> bool {
    path.ancestors()
        .skip(1)
        .any(|dir| dir.file_name().is_some_and(|name| name == "Claude.app"))
}

// Chrome starts `chrome-native-host` for the browser extension: Claude's only where Claude did.
fn counts(in_app: &dyn Fn(&Path) -> bool, root: &Path, one: &Listed, all: &[Listed]) -> bool {
    if one.path.starts_with(root) {
        return true;
    }
    if !in_app(&one.path) {
        return false;
    }
    if one.path.ends_with(NATIVE_HOST) {
        return all
            .iter()
            .find(|parent| parent.pid == one.parent)
            .is_some_and(|parent| in_app(&parent.path));
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn listed(listing: &[(u32, u32, &str)]) -> Vec<Listed> {
        listing
            .iter()
            .map(|&(pid, parent, path)| Listed {
                pid,
                parent,
                path: PathBuf::from(path),
            })
            .collect()
    }

    // The contributor's `ps` sample of 3 October 2026: the native host, started both ways.
    #[test]
    fn a_helper_counts_and_chrome_native_host_does_not() {
        let all = listed(&[
            (501, 1, "/Applications/Claude.app/Contents/MacOS/Claude"),
            (
                502,
                501,
                "/Applications/Claude.app/Contents/Frameworks/Claude Helper (Renderer).app/Contents/MacOS/Claude Helper (Renderer)",
            ),
            (
                503,
                501,
                "/Applications/Claude.app/Contents/Frameworks/Electron Framework.framework/Helpers/chrome_crashpad_handler",
            ),
            (
                590,
                1,
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            ),
            (
                600,
                590,
                "/Applications/Claude.app/Contents/Helpers/chrome-native-host",
            ),
            (
                601,
                501,
                "/Applications/Claude.app/Contents/Helpers/chrome-native-host",
            ),
            (
                700,
                1,
                "/Users/a/Downloads/Claude.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt",
            ),
            (702, 1, "/usr/local/bin/claude"),
            (703, 1, "Claude"),
        ]);
        let root = Path::new("/Users/a/Library/Application Support/Claude");
        let found: Vec<u32> = all
            .iter()
            .filter(|one| counts(&in_any_claude_app, root, one, &all))
            .map(|one| one.pid)
            .collect();
        assert_eq!(found, [501, 502, 503, 601, 700]);
    }

    #[test]
    fn a_program_inside_the_data_folder_counts() {
        let root = Path::new("/Users/a/Library/Application Support/Claude");
        let all = listed(&[(
            900,
            1,
            "/Users/a/Library/Application Support/Claude/claude-code/2.1.290/claude",
        )]);
        assert!(counts(&in_any_claude_app, root, &all[0], &all));
    }

    #[test]
    fn a_build_for_tests_counts_its_scratch_app_alone() {
        let scratch = std::env::temp_dir().join(format!("pitboard-running-{}", std::process::id()));
        let ctx = Context::for_unit_test().with_scratch_claude(scratch.clone());
        let root = scratch.join("data");
        let own = scratch.join("Claude.app/Contents/MacOS/Claude");
        let inside = root.join("claude-code/x/claude");
        let (own, inside) = (own.to_string_lossy(), inside.to_string_lossy());
        let all = listed(&[
            (1, 0, "/Applications/Claude.app/Contents/MacOS/Claude"),
            (2, 0, own.as_ref()),
            (3, 0, inside.as_ref()),
        ]);
        let found: Vec<u32> = all
            .iter()
            .filter(|one| is_the_app(&ctx, &root, one, &all))
            .map(|one| one.pid)
            .collect();
        assert_eq!(found, [2, 3]);
        assert!(!is_the_app(&Context::for_unit_test(), &root, &all[0], &all));
    }
}
