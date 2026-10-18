// What the Windows app shows in the site's figures, in its own words: windows.test.ts finds each
// in the file that says it.
import { clock, type Limit, type Moment, type Tool } from "./world.ts";
import {
  activity,
  isInUse,
  level,
  listed,
  menuLine,
  menuTitle,
  paceText,
  runsOutNote,
  standing,
  toolAccounts,
  toolsOn,
} from "../lib/words.ts";

export const app = "Pitboard";

// MainWindow.xaml's panes, and NavigationView's own Settings at the foot
export const panes = [
  ["accounts", "Accounts", "fl-people"],
  ["activity", "Activity", "fl-history"],
  ["machine", "This PC", "fl-desktop"],
] as const;
export type Pane = (typeof panes)[number][0] | "settings";

export type MenuEntry =
  | { heading: string }
  | { account: string; label: string; checked: boolean; tip: string }
  | { command: string; bold?: boolean; site?: string; submenu?: string[] };

// the site each tool's accounts have windows on, in pitboard_sites::ALL's order
const sites: [site: string, tool: Tool][] = [
  ["claude.ai", "claude"],
  ["chatgpt.com", "codex"],
];

// IconMenu.cs `Entries`, a group between each two separators. A beta's menu has no Check for updates.
// An account's tooltip is the line the menu bar app shows under it.
export function iconMenu(moment: Moment, updates: boolean): MenuEntry[][] {
  return [
    ...toolsOn("windows").map(([tool, name]) => [
      { heading: name },
      ...listed(tool, moment).map((a) => ({
        account: menuTitle(a),
        label: a.label,
        checked: isInUse(a, moment),
        tip: menuLine(a, moment),
      })),
    ]),
    [{ command: "Add account…" }, { command: "Refresh" }],
    sites.map(([site, tool]) => ({
      command: `Open ${site}`,
      site,
      submenu: toolAccounts(tool).map((a) => a.label),
    })),
    [
      { command: "Open Pitboard", bold: true },
      { command: "Settings" },
      ...(updates ? [{ command: "Check for updates" }] : []),
    ],
    [{ command: "Pitboard help" }, { command: "Exit" }],
  ];
}

// The tooltip of an account's row in the icon's menu.
export function accountTip(label: string, moment: Moment): string {
  for (const e of iconMenu(moment, false).flat()) {
    if ("account" in e && e.label === label) return e.tip;
  }
  throw new Error(`the icon's menu lists no ${label}`);
}

const capitalised = (s: string) => s[0].toUpperCase() + s.slice(1);

// AccountRow.xaml: its notes are the problem, the pace, the parked login's life and the warm-up,
// and Windows warms up no account.
export function accountsPage(moment: Moment) {
  return {
    title: "Accounts",
    updated: `Updated ${clock.read}`,
    commands: ["Refresh", "Add account"],
    notice:
      moment === "after"
        ? {
            title: "Switched Claude Code to work",
            countdown: "Claude Code sessions already open follow in 0:21",
          }
        : null,
    sections: toolsOn("windows").map(([tool, name]) => ({
      name,
      rows: listed(tool, moment).map((a) => {
        const inUse = isInUse(a, moment);
        const runsOut = runsOutNote(a, moment);
        return {
          label: a.label,
          plan: a.plan,
          email: a.email,
          inUse,
          action: inUse ? null : "Use",
          limits: a.limits.map((l: Limit) => ({
            ...l,
            level: level(l.used),
            standing: standing(l),
            pace: paceText(l),
          })),
          notes: [
            runsOut && capitalised(runsOut),
            !inUse && a.parked && `Parked login good for ${a.parked.app}`,
          ].filter((n): n is string => Boolean(n)),
        };
      }),
    })),
  };
}

const threshold = 95;

// SettingsPage.xaml's Limits, with machine.rs's words under Switch Claude Code automatically
export const settingsPage = {
  title: "Settings",
  section: "Limits",
  autoSwitch: {
    header: "Switch Claude Code automatically",
    on: true,
    standing: `Watching work: 40% of its weekly limit, as of ${clock.read}.`,
    note: `Pitboard switches Claude Code to another of your accounts with room below ${threshold}% in every limit, once a limit of the one in use reaches ${threshold}%, while the app is open. Sessions already running follow within about 33 seconds. It never switches back by itself, and never switches Codex: a running codex keeps its account until restarted.`,
  },
  share: { header: "Switch when a limit reaches", range: "From 50% to 99%", value: threshold },
};

export const activityPage = {
  title: "Activity",
  command: "Refresh",
  rows: activity("windows"),
};

// The run-out notification: title, tool and body, and its one button (WindowsNotifications.cs)
export const toast = {
  app,
  title: "personal has no weekly limit left",
  tool: "Claude Code",
  body: "work has 60% of its own left.",
  button: "Switch",
};
