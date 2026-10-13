// The app's and the command line's words for world.ts, by crates/pitboard-core/src/words.rs and
// crates/pitboard/src/render/status.rs.

import {
  accounts,
  inUse,
  type Account,
  type Limit,
  type Moment,
  type Tool,
} from "../data/world.ts";

export type Standing = "over" | "under" | "even";

// More than 5 points either side of an even pace counts (concepts/usage#pace).
export function standing(limit: Limit): Standing | null {
  if (limit.even === null) return null;
  const delta = limit.used - limit.even;
  if (delta > 5) return "over";
  if (delta < -5) return "under";
  return "even";
}

export function paceText(limit: Limit): string {
  const s = standing(limit);
  if (s === null) return "";
  if (s === "even") return "on pace";
  return `${Math.round(Math.abs(limit.used - limit.even!))}% ${s} pace`;
}

// the app draws "y" in orange
export function level(used: number): "g" | "y" | "r" {
  return used >= 90 ? "r" : used >= 70 ? "y" : "g";
}

const sentenceName = { "5h": "5-hour", week: "weekly" } as const;

export const isInUse = (a: Account, moment: Moment) => inUse[moment][a.tool] === a.label;

export const toolAccounts = (tool: Tool) => accounts.filter((a) => a.tool === tool);

export const menuTitle = (a: Account) => `${a.label} · ${a.plan}`;

export function menuLine(a: Account, moment: Moment): string {
  const said = a.limits.map((l) => {
    const runs = isInUse(a, moment) && l.runsOut ? ` (runs out in ${l.runsOut})` : "";
    return `${sentenceName[l.kind]} ${l.used}%${runs}`;
  });
  const line = said.join(", ");
  return line[0].toUpperCase() + line.slice(1);
}

// The in-use account closest to running out, a tie going to Claude Code (reference/app#menu-bar-item).
export function menuBarTitle(moment: Moment): string {
  const fullest = (a: Account) => Math.max(...a.limits.map((l) => l.used));
  const shown = (["claude", "codex"] as Tool[])
    .map((tool) => accounts.find((a) => a.tool === tool && isInUse(a, moment))!)
    .reduce((best, a) => (fullest(a) > fullest(best) ? a : best));
  return `${shown.label} ${fullest(shown)}%`;
}

export function runsOutNote(a: Account, moment: Moment): string | null {
  const l = isInUse(a, moment) ? a.limits.find((l) => l.runsOut) : undefined;
  return l ? `${sentenceName[l.kind]} limit runs out in ${l.runsOut} at this pace` : null;
}

const span = (cls: string, text: string) => (text ? `<span class="${cls}">${text}</span>` : "");
const pad = (text: string, width: number) => text + " ".repeat(Math.max(0, width - text.length));

// crates/pitboard/src/ui.rs `bar`; a run of cells is one span, so figures.css can draw it seamless.
function bar(limit: Limit): string {
  const width = 10;
  const filled = Math.min(width, Math.max(0, Math.round((limit.used / 100) * width)));
  const cells = (from: number, to: number) => {
    const full = Math.min(Math.max(filled, from), to);
    return (
      span(`${level(limit.used)} bk`, "█".repeat(full - from)) +
      span("dim sh", "░".repeat(to - full))
    );
  };
  const s = standing(limit);
  if (s === null || s === "even") return cells(0, width);
  const at = Math.min(Math.floor((limit.even! / 100) * width), width - 1);
  return cells(0, at) + span(s === "over" ? "r" : "g", "│") + cells(at + 1, width);
}

// `pitboard` for one tool's accounts, the account in use first, as one HTML string a line.
export function statusLines(tool: Tool, moment: Moment): string[] {
  const rows = [...toolAccounts(tool)].sort(
    (a, b) => Number(isInUse(b, moment)) - Number(isInUse(a, moment)),
  );
  const labelWidth = Math.max(...accounts.map((a) => a.label.length));
  const emailWidth = Math.max(...accounts.map((a) => a.email.length));
  const planWidth = Math.max(...accounts.map((a) => a.plan.length));
  const resetsWidth = Math.max(
    ...accounts.flatMap((a) => a.limits.map((l) => `resets in ${l.resets}`.length)),
  );
  const lines: string[] = [];
  rows.forEach((a, i) => {
    if (i > 0) lines.push("");
    const on = isInUse(a, moment);
    const standingText = on
      ? span("g", "signed in")
      : a.parked
        ? `${span("g", "ready")} ${span("dim", `· good for ${a.parked.cli}`)}`
        : span("g", "ready");
    lines.push(
      `${on ? span("g", "●") : span("dim", "○")} <b>${pad(a.label, labelWidth)}</b>  ${span("dim", pad(a.email, emailWidth))}  ${span("dim", pad(a.plan, planWidth))}  ${standingText}`,
    );
    for (const l of a.limits) {
      const resets = `resets in ${l.resets}`;
      const pace = paceText(l);
      const after = pace
        ? `${span("dim", pad(resets, resetsWidth))}  ${span(standing(l) === "over" ? "r" : standing(l) === "under" ? "g" : "dim", pace)}`
        : span("dim", resets);
      lines.push(
        `    ${pad(l.kind, 4)}  ${bar(l)}  ${span(level(l.used), `${String(l.used).padStart(3)}%`)}  ${after}`,
      );
    }
    const note = runsOutNote(a, moment);
    if (note) lines.push(`    ${pad("", 4)}  ${span("r", note)}`);
  });
  return lines;
}

// reference/integration-commands#statusline
export function statusLine(moment: Moment): string {
  const share = (l: Limit | undefined, marks: boolean) => {
    if (!l) return span("dim", "?");
    const s = standing(l);
    const mark =
      marks && s === "over" ? span("r", "▲") : marks && s === "under" ? span("g", "▼") : "";
    return `${span(level(l.used), `${l.used}%`)}${mark}`;
  };
  const shares = (a: Account, marks: boolean) =>
    `${share(
      a.limits.find((l) => l.kind === "5h"),
      marks,
    )}${span("dim", "·")}${share(
      a.limits.find((l) => l.kind === "week"),
      marks,
    )}`;
  const claude = toolAccounts("claude");
  const current = claude.find((a) => isInUse(a, moment))!;
  const others = claude.filter((a) => a !== current);
  return [
    `<b>${current.label}</b> ${shares(current, true)}`,
    ...others.map((a) => `${span("dim", a.label)} ${shares(a, false)}`),
  ].join("  ");
}
