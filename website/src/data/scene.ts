// The desktop the menu drops onto: a Claude Code session in a terminal behind it, with
// example accounts. Each line is one terminal row, drawn with white-space: pre.

export const session: string[] = [
  `<span class="d">&gt; the parser test fails about one run in ten, find out why</span>`,
  ` `,
  `<span class="g">●</span> Search(pattern: "flush", path: "src")`,
  `  <span class="tw-el"></span> Found 4 files`,
  ` `,
  `<span class="g">●</span> Read(src/parser_test.rs)`,
  `  <span class="tw-el"></span> Read 142 lines`,
  ` `,
  `<span class="g">●</span> Read(src/writer.rs)`,
  `  <span class="tw-el"></span> Read 88 lines`,
  ` `,
  `● The test opens the log while the writer still holds`,
  `  its buffer, so the parser can read half a line. I will`,
  `  make the test wait until the writer has flushed.`,
  ` `,
  `<span class="g">●</span> Update(src/parser_test.rs)`,
  `  <span class="tw-el"></span> Updated src/parser_test.rs with 2 additions`,
  `      <span class="d">41</span>     let log = writer.start();`,
  `      <span class="d">42</span> <span class="g">+</span>   writer.finish();`,
  `      <span class="d">43</span> <span class="g">+</span>   log.wait_flushed();`,
  `      <span class="d">44</span>     let lines = parse(&amp;log.path);`,
  ` `,
  `<span class="g">●</span> Bash(cargo test parser)`,
  `  <span class="tw-el"></span> test result: ok. 48 passed; 0 failed`,
  ` `,
  `<span class="g">●</span> Bash(for i in $(seq 50); do cargo test -q parser; done)`,
  `  <span class="tw-el"></span> 50 of 50 runs passed`,
  ` `,
  `● The test now waits for the writer to flush before it`,
  `  parses the log. It passed 50 runs in a row.`,
  ` `,
];

// the session's status line once work is in use, and before, with personal in use
export const statusNow = `  <b>work</b> <span class="g">12%▼</span><span class="d">·</span><span class="g">40%▼</span>  <span class="d">personal</span> <span class="g">59%</span><span class="d">·</span><span class="y">73%</span>`;
export const statusWas = `  <b>personal</b> <span class="g">59%▼</span><span class="d">·</span><span class="y">73%</span><span class="r">▲</span>  <span class="d">work</span> <span class="g">12%</span><span class="d">·</span><span class="g">40%</span>`;
