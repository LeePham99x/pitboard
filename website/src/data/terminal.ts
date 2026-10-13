// Quoted from docs/quickstart.mdx; terminal.test.ts fails when the two part.

export const status: string[] = [
  `<span class="g">●</span> <b>work    </b>  <span class="dim">me@company.com</span>  <span class="dim">Team 5x</span>  <span class="g">signed in</span>`,
  `    5h    <span class="g">█</span><span class="dim">░░</span><span class="g">│</span><span class="dim">░░░░░░</span>  <span class="g"> 12%</span>  <span class="dim">resets in 3h 02m</span>  <span class="g">27% under pace</span>`,
  `    week  <span class="g">████</span><span class="dim">░░</span><span class="g">│</span><span class="dim">░░░</span>  <span class="g"> 40%</span>  <span class="dim">resets in 2d 4h </span>  <span class="g">29% under pace</span>`,
  ``,
  `<span class="dim">○</span> <b>personal</b>  <span class="dim">me@example.com</span>  <span class="dim">Max 20x</span>  <span class="g">ready</span> <span class="dim">· good for 26d 4h</span>`,
  `    5h    <span class="g">██████</span><span class="dim">░</span><span class="g">│</span><span class="dim">░░</span>  <span class="g"> 59%</span>  <span class="dim">resets in 1h 10m</span>  <span class="g">18% under pace</span>`,
  `    week  <span class="y">█</span><span class="r">│</span><span class="y">█████</span><span class="dim">░░░</span>  <span class="y"> 73%</span>  <span class="dim">resets in 5d 18h</span>  <span class="r">55% over pace</span>`,
];

export interface Step {
  command: string;
  // the line Pitboard prints after a pause, while the browser sign-in runs
  output: { html: string; wait?: boolean }[];
  fit?: boolean;
}

export const tour: Step[] = [
  {
    command: "pitboard enroll personal",
    output: [
      { html: "Enrolled personal (me@example.com), the account signed in now." },
      {
        html: `Add another without signing out of it: <span class="nw">pitboard enroll &lt;label&gt; --sign-in</span>`,
      },
    ],
  },
  {
    command: "pitboard enroll work --sign-in",
    output: [
      {
        html: "Opening Claude Code's sign-in. Sign in as the account to add; the account in use now stays signed in.",
      },
      {
        html: `Enrolled work (me@company.com). Switch to it with: <span class="nw">pitboard use work</span>`,
        wait: true,
      },
    ],
  },
  {
    command: "pitboard use work",
    output: [
      { html: "Switched to <b>work</b>; <b>personal</b> is parked." },
      { html: "Claude Code sessions already running follow within 33 seconds." },
    ],
  },
  { command: "pitboard", output: status.map((html) => ({ html: html || " " })), fit: true },
];

export const statusLine = `<b>personal</b> 59%<span class="g">▼</span>·73%<span class="r">▲</span>  work 12%·40%`;
