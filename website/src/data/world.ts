// Each plan has the limits Pitboard measured it with (ARCHITECTURE.md, "Anthropic's usage answer").

export type Tool = "claude" | "codex";

export interface Limit {
  kind: "5h" | "week";
  used: number;
  even: number | null;
  resets: string;
  runsOut?: string;
}

export interface Account {
  tool: Tool;
  label: string;
  email: string;
  plan: string;
  limits: Limit[];
  parked?: { cli: string; app: string };
}

export const accounts: Account[] = [
  {
    tool: "claude",
    label: "personal",
    email: "me@example.com",
    plan: "Max 20x",
    limits: [
      { kind: "5h", used: 59, even: 77, resets: "1h 10m" },
      { kind: "week", used: 73, even: 18, resets: "5d 18h", runsOut: "11h 05m" },
    ],
    parked: { cli: "26d 4h", app: "26 more days" },
  },
  {
    tool: "claude",
    label: "work",
    email: "me@company.com",
    plan: "Max 20x",
    limits: [
      { kind: "5h", used: 12, even: 40, resets: "3h 00m" },
      { kind: "week", used: 40, even: 69, resets: "2d 4h" },
    ],
    parked: { cli: "26d 4h", app: "26 more days" },
  },
  {
    tool: "codex",
    label: "main",
    email: "me@example.com",
    plan: "Pro",
    limits: [
      { kind: "5h", used: 18, even: 34, resets: "3h 19m" },
      { kind: "week", used: 7, even: 18, resets: "5d 18h" },
    ],
  },
  {
    tool: "codex",
    label: "spare",
    email: "me@company.com",
    plan: "Plus",
    limits: [{ kind: "5h", used: 0, even: null, resets: "5h 00m" }],
  },
];

export type Moment = "before" | "after";
export const inUse: Record<Moment, Record<Tool, string>> = {
  before: { claude: "personal", codex: "main" },
  after: { claude: "work", codex: "main" },
};

// A five-hour period starts and resets on a ten-minute mark (concepts/usage#when-a-period-starts).
export const clock = {
  menuBar: "Tue 13 Oct 20:51",
  read: "20:50",
  switched: "20:49",
  warmed: "17:00",
  warmsAgain: "22:00",
  // when personal, left in use, runs out of its weekly limit
  runOut: "Wed 14 Oct 07:56",
};

export const account = (label: string, tool: Tool = "claude"): Account => {
  const found = accounts.find((a) => a.label === label && a.tool === tool);
  if (!found) throw new Error(`no example account ${tool}/${label}`);
  return found;
};
