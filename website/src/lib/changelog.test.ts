import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import {
  compareBase,
  longDate,
  monthOf,
  parseChangelog,
  plainText,
  renderInline,
} from "./changelog.ts";

const LINKS = `
[Unreleased]: https://github.com/datlechin/pitboard/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/datlechin/pitboard/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/datlechin/pitboard/releases/tag/v0.1.0
`;

function doc(body: string, links = LINKS): string {
  return `# Changelog\n\nIntro with a [link](https://keepachangelog.com/).\n\n${body}\n${links}`;
}

test("reads every version of the repository's CHANGELOG.md", () => {
  const text = readFileSync(new URL("../../../CHANGELOG.md", import.meta.url), "utf8");
  const versions = parseChangelog(text);
  const headings = text.match(/^## \[\d+\.\d+\.\d+\]/gm) ?? [];
  assert.equal(versions.length, headings.length);
  assert.equal(versions.at(-1)?.version, "0.1.0");
  for (const v of versions) {
    assert.match(v.date, /^\d{4}-\d{2}-\d{2}$/);
    assert.ok(v.sections.length > 0 || v.intro);
  }
});

test("skips Unreleased and keeps each version's sections in Keep a Changelog's order", () => {
  const versions = parseChangelog(
    doc(`## [Unreleased]

### Added

- Not out yet.

## [0.2.0] - 2026-09-22

### Fixed

- A fix.

### Added

- A thing.

### Security

- A hole closed.

## [0.1.0] - 2026-09-21

### Added

- The first.
`),
  );
  assert.deepEqual(
    versions.map((v) => v.version),
    ["0.2.0", "0.1.0"],
  );
  assert.deepEqual(
    versions[0].sections.map((s) => s.kind),
    ["Added", "Fixed", "Security"],
  );
  assert.deepEqual(
    versions[0].sections.map((s) => s.id),
    ["v0.2.0-added", "v0.2.0-fixed", "v0.2.0-security"],
  );
  assert.equal(versions[0].sections[0].html, "<ul><li>A thing.</li></ul>");
  assert.equal(compareBase(versions[0].link), "0.1.0");
  assert.equal(compareBase(versions[1].link), null);
});

// release.yml's guard takes any line that starts "## [<version>]", so each one it would take
// either parses or stops the build.
test("agrees with release.yml on version headings", () => {
  const good = ["## [0.2.0] - 2026-09-22", "## [0.2.0] - 2026-09-22 "];
  for (const h of good) {
    const v = parseChangelog(
      doc(`${h}\n\n### Added\n\n- A.\n\n## [0.1.0] - 2026-09-21\n\n### Added\n\n- B.\n`),
    );
    assert.equal(v[0].version, "0.2.0");
    assert.equal(v[0].date, "2026-09-22");
  }
  const bad = [
    "## [0.2.0]",
    "## [0.2.0] 2026-09-22",
    "## [0.2.0] - 22 September 2026",
    "## [0.2.0] -- 2026-09-22",
    "## [0.2.0] - 2026-02-30",
    "## [0.2.0-rc1] - 2026-09-22",
    "## [0.2.0](https://example.com) - 2026-09-22",
    "## 0.2.0 - 2026-09-22",
  ];
  for (const h of bad) {
    assert.throws(
      () =>
        parseChangelog(
          doc(`${h}\n\n### Added\n\n- A.\n\n## [0.1.0] - 2026-09-21\n\n### Added\n\n- B.\n`),
        ),
      Error,
      h,
    );
  }
  // the awk that cuts release notes stops at the next "## [", so its notes and this page agree
  const text = doc(
    `## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n\n## [0.1.0] - 2026-09-21\n\n### Added\n\n- B.\n`,
  );
  const awk = text.split(/^## \[0\.2\.0\][^\n]*\n/m)[1].split(/^## \[/m)[0];
  assert.match(awk, /- A\./);
  assert.doesNotMatch(awk, /- B\./);
});

test("fails on what it cannot render faithfully", () => {
  const cases: [string, RegExp][] = [
    ["## [0.2.0] - 2026-09-22\n\n### Notes\n\n- A.\n", /section "Notes"/],
    ["## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n\n### Added\n\n- B.\n", /two Added/],
    [
      "## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n\n## [0.2.0] - 2026-09-22\n\n### Added\n\n- B.\n",
      /twice/,
    ],
    [
      "## [0.1.0] - 2026-09-21\n\n### Added\n\n- A.\n\n## [0.2.0] - 2026-09-22\n\n### Added\n\n- B.\n",
      /listed above/,
    ],
    ["## [0.2.0] - 2026-09-22\n\n### Added\n\n#### Deep\n", /heading it cannot hold/],
    ["## [0.2.0] - 2026-09-22\n\n### Added\n\n1. A numbered list.\n", /does not render/],
    [
      "## [0.2.0] - 2026-09-22\n\n### Added\n\n- A _single_ __underscore__ pair.\n",
      /does not render/,
    ],
    [
      "## [0.2.0] - 2026-09-22\n\n### Added\n\n- [A link](javascript:alert(1)).\n",
      /a link goes to/,
    ],
    ["## [0.2.0] - 2026-09-22\n\n```sh\nopen\n", /not closed/],
    ["## [0.2.0] - 2026-09-22\n\n### Added\n", /empty Added/],
    [
      "## [Unreleased]\n\n## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n\n## [Unreleased]\n",
      /after a released/,
    ],
  ];
  for (const [body, why] of cases) {
    assert.throws(
      () =>
        parseChangelog(
          doc(
            body,
            "[0.2.0]: https://github.com/datlechin/pitboard/compare/v0.1.0...v0.2.0\n[0.1.0]: https://github.com/datlechin/pitboard/releases/tag/v0.1.0\n",
          ),
        ),
      why,
    );
  }
  assert.throws(
    () => parseChangelog(doc("## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n", "")),
    /no link definition/,
  );
  assert.throws(
    () =>
      parseChangelog(
        doc("## [0.2.0] - 2026-09-22\n\n### Added\n\n- A.\n", "[0.2.0]: https://example.com/x\n"),
      ),
    /links somewhere else/,
  );
});

test("renders an intro with fenced commands, nested bullets and loose items", () => {
  const [v] = parseChangelog(
    doc(
      `## [0.2.0] - 2026-09-22

In the tap, \`pitboard\` is the command line's cask. Run:

\`\`\`sh
brew uninstall --formula pitboard
# a comment line, not a heading
brew install datlechin/tap/pitboard
\`\`\`

### Changed

- **Breaking:** the parent.
  - A child with \`code\`.
  - Another child.
- Two paragraphs.

  The second one.
`,
      "[0.2.0]: https://github.com/datlechin/pitboard/compare/v0.1.0...v0.2.0\n",
    ),
  );
  assert.equal(
    v.intro,
    "<p>In the tap, <code>pitboard</code> is the command line's cask. Run:</p><pre><code><span>brew uninstall --formula pitboard</span><span># a comment line, not a heading</span><span>brew install datlechin/tap/pitboard</span></code></pre>",
  );
  assert.equal(
    v.sections[0].html,
    "<ul><li><p><strong>Breaking:</strong> the parent.</p><ul><li>A child with <code>code</code>.</li><li>Another child.</li></ul></li><li><p>Two paragraphs.</p><p>The second one.</p></li></ul>",
  );
});

test("renders inline code, links and bold, and breaks long code", () => {
  assert.equal(
    renderInline(
      "Run `pitboard use work` or see [the docs](https://docs.usepitboard.com/install), **Settings** > **Limits**.",
    ),
    'Run <code>pitboard use work</code> or see <a href="https://docs.usepitboard.com/install">the docs</a>, <strong>Settings</strong> &gt; <strong>Limits</strong>.',
  );
  assert.equal(
    renderInline("`curl -fsSL https://usepitboard.com/install.sh | sh`"),
    '<code class="long">curl -fsSL https://<wbr>usepitboard<wbr>.com/<wbr>install<wbr>.sh | sh</code>',
  );
  assert.equal(renderInline("`a <b> & c`"), "<code>a &lt;b&gt; &amp; c</code>");
  assert.equal(renderInline("`` a`b ``"), "<code>a`b</code>");
  assert.equal(plainText(renderInline("`forget` and **x** &")), "forget and x &");
});

test("formats dates as the docs do", () => {
  assert.equal(longDate("2026-10-12"), "12 October 2026");
  assert.equal(longDate("2026-09-01"), "1 September 2026");
  assert.equal(monthOf("2026-10-12"), "October 2026");
});
