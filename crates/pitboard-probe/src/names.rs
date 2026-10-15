use crate::PROBE_PREFIX;
pub use pitboard_core::provider::names::Family;

pub const CITEST_PREFIX: &str = "pitboard-citest-";

// Bun.secrets names its target `<service>/<name>`; Claude Code's pieces add `#<n>`, `#m`, `#p`.
pub const CITEST_BUN_SERVICE: &str = "pitboard-citest-bun";
pub const CITEST_BUN_NAME: &str = "claude-code-user";

// One CredEnumerateW each, so no other application's item is ever loaded into the probe.
pub const ENUMERATION_FILTERS: [&str; 5] = [
    "Claude Code*",
    "cli|*",
    "secrets|*",
    "Codex MCP Credentials*",
    "pitboard-*",
];

// Credential Manager filters by prefix alone, so no enumeration can reach these.
pub const UNLISTABLE_FORMS: [&str; 1] = ["<key>.Codex MCP Credentials"];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Kind {
    Live(Family),
    CiTest,
    Probe,
    Other,
}

impl Kind {
    pub fn is_leak(self) -> bool {
        matches!(self, Kind::Live(_) | Kind::CiTest)
    }

    pub fn is_reportable(self) -> bool {
        !matches!(self, Kind::Other)
    }
}

pub fn classify(target: &str) -> Kind {
    let lower = target.to_lowercase();
    let lower = lower
        .strip_prefix("legacygeneric:target=")
        .unwrap_or(&lower);
    // Before the families, so a name with one of these prefixes is never taken for a live login.
    if lower.starts_with(PROBE_PREFIX) {
        return Kind::Probe;
    }
    if lower.starts_with(CITEST_PREFIX) {
        return Kind::CiTest;
    }
    pitboard_core::provider::names::family(target).map_or(Kind::Other, Kind::Live)
}

pub fn is_probe_item_name(name: &str) -> bool {
    name.len() > PROBE_PREFIX.len()
        && name.len() <= 256
        && name.starts_with(PROBE_PREFIX)
        && name.bytes().all(|b| b.is_ascii_graphic())
        && classify(name) == Kind::Probe
}

pub fn is_citest_bun_item_name(target: &str) -> bool {
    let Some(rest) = target
        .strip_prefix(CITEST_BUN_SERVICE)
        .and_then(|r| r.strip_prefix('/'))
        .and_then(|r| r.strip_prefix(CITEST_BUN_NAME))
    else {
        return false;
    };
    match rest.strip_prefix('#') {
        None => rest.is_empty(),
        Some(piece) => {
            matches!(piece, "m" | "p")
                || (!piece.is_empty()
                    && piece.len() <= 3
                    && piece.bytes().all(|b| b.is_ascii_digit()))
        }
    }
}

pub fn blob_encoding(blob: &[u8]) -> &'static str {
    if blob.is_empty() {
        "empty"
    } else if blob.len().is_multiple_of(2)
        && blob
            .chunks(2)
            .all(|c| c[1] == 0 && c[0].is_ascii() && c[0] != 0)
    {
        "utf16le_ascii"
    } else if blob.iter().all(|b| b.is_ascii() && *b != 0) {
        "ascii"
    } else if std::str::from_utf8(blob).is_ok() {
        "utf8"
    } else {
        "binary"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_match_folds_case_the_way_credential_manager_does() {
        assert_eq!(
            classify("claude code-credentials"),
            Kind::Live(Family::ClaudeCode)
        );
        assert_eq!(classify("CLI|X"), Kind::Live(Family::CodexCli));
        assert_eq!(classify("PITBOARD-PROBE-x"), Kind::Probe);
    }

    #[test]
    fn the_probe_and_the_tests_name_their_own() {
        assert_eq!(classify("pitboard-probe-vault-1"), Kind::Probe);
        assert_eq!(classify("pitboard-citest-abcd"), Kind::CiTest);
    }

    #[test]
    fn everyone_elses_items_are_other_and_say_nothing() {
        for name in [
            "GitHub - https://github.com",
            "MicrosoftAccount:user=someone@example.com",
            "LegacyGeneric:target=some-app",
            "Claude something else",
            "my cli| thing",
        ] {
            let k = classify(name);
            assert_eq!(k, Kind::Other, "{name}");
            assert!(!k.is_reportable());
            assert!(!k.is_leak());
        }
    }

    #[test]
    fn a_leak_is_a_live_family_or_a_citest_name() {
        assert!(classify("Claude Code-credentials").is_leak());
        assert!(classify("pitboard-citest-x").is_leak());
        assert!(!classify("pitboard-probe-x").is_leak());
        assert!(!classify("GitHub").is_leak());
    }

    #[test]
    fn a_probe_prefix_wins_over_a_family_lookalike() {
        assert_eq!(classify("pitboard-probe-cli|x"), Kind::Probe);
    }

    #[test]
    fn every_enumeration_filter_is_a_prefix_of_a_reportable_name() {
        for filter in ENUMERATION_FILTERS {
            let stem = filter.trim_end_matches('*');
            assert!(filter.ends_with('*') && !stem.contains('*'), "{filter}");
            // A name the filter returns is reportable once it is a family's full form.
        }
        assert!(classify("Claude Code-credentials").is_reportable());
        assert!(classify("cli|x").is_reportable());
        assert!(classify("secrets|x").is_reportable());
        assert!(classify("Codex MCP Credentials").is_reportable());
        assert!(classify("pitboard-probe-x").is_reportable());
    }

    #[test]
    fn the_probe_writes_only_its_own_names() {
        assert!(is_probe_item_name("pitboard-probe-m1"));
        for bad in [
            "pitboard-probe-",
            "Claude Code-credentials",
            "pitboard-citest-x",
            "cli|x",
            "pitboard-probe-a b",
            "x-pitboard-probe-y",
        ] {
            assert!(!is_probe_item_name(bad), "{bad}");
        }
    }

    #[test]
    fn only_the_bun_rows_items_and_their_pieces_are_citest_bun_names() {
        for ok in [
            "pitboard-citest-bun/claude-code-user",
            "pitboard-citest-bun/claude-code-user#0",
            "pitboard-citest-bun/claude-code-user#255",
            "pitboard-citest-bun/claude-code-user#m",
            "pitboard-citest-bun/claude-code-user#p",
        ] {
            assert!(is_citest_bun_item_name(ok), "{ok}");
            assert_eq!(classify(ok), Kind::CiTest);
        }
        for bad in [
            "Claude Code-credentials/claude-code-user",
            "pitboard-citest-bun/other",
            "pitboard-citest-bun/claude-code-user#",
            "pitboard-citest-bun/claude-code-user#1000",
            "pitboard-citest-bun/claude-code-user#x",
            "pitboard-citest-bunx/claude-code-user",
            "pitboard-citest-bun\\claude-code-user",
        ] {
            assert!(!is_citest_bun_item_name(bad), "{bad}");
        }
    }

    #[test]
    fn a_blob_is_described_by_its_encoding_alone() {
        assert_eq!(blob_encoding(b""), "empty");
        assert_eq!(blob_encoding(b"abc1"), "ascii");
        assert_eq!(blob_encoding(&[b'a', 0, b'b', 0]), "utf16le_ascii");
        assert_eq!(blob_encoding("Đạt".as_bytes()), "utf8");
        assert_eq!(blob_encoding(&[0xff, 0xfe, 0x00]), "binary");
    }
}
