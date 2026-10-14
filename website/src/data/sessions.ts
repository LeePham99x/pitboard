import { clock, tools, type Moment } from "./world.ts";
import { statusHeading, statusLines } from "../lib/words.ts";

export interface Session {
  title: string;
  command: string;
  output: string[];
}

function status(moment: Moment): Session {
  return {
    title: "pitboard",
    command: "pitboard",
    output: tools.flatMap(([tool], i) => [
      ...(i > 0 ? [""] : []),
      statusHeading(tool),
      ...statusLines(tool, moment),
    ]),
  };
}

export const sessions = {
  status: status("after"),
  statusBefore: status("before"),
  use: {
    title: "pitboard use work",
    command: "pitboard use work",
    output: [
      "Switched to <b>work</b>; <b>personal</b> is parked.",
      "Claude Code sessions already running follow within 33 seconds.",
    ],
  },
  useDesktop: {
    title: "pitboard use desktop/home",
    command: "pitboard use desktop/home",
    output: [
      "Switched to <b>desktop/home</b>; <b>desktop/office</b> is parked.",
      "Claude opens signed in to it.",
    ],
  },
  watch: {
    title: "pitboard watch",
    command: "pitboard watch",
    output: [
      "Watching Claude Code. Pitboard switches to another of your accounts once a limit of the one in use reaches 95%, and warms up <b>personal</b> from 07:00 to 23:00. Stop with Ctrl-C.",
      `<span class="dim">${clock.read}:14</span> Watching <b>work</b>: 40% of its weekly limit, as of ${clock.read}.`,
    ],
  },
  warm: {
    title: "pitboard warm",
    command: "pitboard warm",
    output: [
      "Pitboard warms up these accounts from 07:00 to 23:00, while the app or `pitboard watch` runs.",
      `  <b>personal</b>  on   warms up again at ${clock.warmsAgain}`,
      `  <b>work    </b>  <span class="dim">off</span>`,
    ],
  },
  log: {
    title: "pitboard log",
    command: "pitboard log -n 4",
    output: [
      ["2026-10-10T09:16:52+07:00", "enroll ", "work"],
      [`2026-10-13T${clock.warmed}:04+07:00`, "warm-up", "personal"],
      [`2026-10-13T${clock.desktopSwitched}:12+07:00`, "use    ", "desktop/home"],
      [`2026-10-13T${clock.switched}:31+07:00`, "use    ", "work"],
    ].map(
      ([at, verb, subject]) =>
        `<span class="dim">${at}</span>  ${verb}  ${subject}  <span class="dim">ok</span>`,
    ),
  },
} satisfies Record<string, Session>;
