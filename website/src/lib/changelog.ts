import { REPO } from "../data/site.ts";

const KINDS = ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"] as const;
type Kind = (typeof KINDS)[number];

interface Section {
  kind: Kind;
  id: string;
  html: string;
}

export interface Version {
  version: string;
  prerelease: boolean;
  date: string;
  intro: string | null;
  sections: Section[];
  link: string;
}

const MONTHS = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
];

// release.yml's guard accepts any line starting "## [<version>] - ", so every such line either
// parses here or fails the build; none is dropped quietly.
const VERSION_HEADING =
  /^## \[(\d+\.\d+\.\d+(?:-beta\.(?:[1-9]|[1-8]\d|9[0-8]))?)\] - (\d{4}-\d{2}-\d{2})\s*$/;
const LINK_DEFINITION = /^\[([^\]]+)\]:\s*(\S+)\s*$/;

export function parseChangelog(text: string): Version[] {
  const lines = text.replace(/\r\n/g, "\n").split("\n");
  const links = new Map<string, string>();
  for (const line of lines) {
    const m = LINK_DEFINITION.exec(line);
    if (m) links.set(m[1], m[2]);
  }

  const raw: { version: string; date: string; intro: string[]; sections: Map<Kind, string[]> }[] =
    [];
  let current: (typeof raw)[number] | null = null;
  let section: string[] | null = null;
  let seenVersion = false;
  let fenced = false;

  for (const line of lines) {
    if (current && fenced) {
      if (/^```\s*$/.test(line)) fenced = false;
      (section ?? current.intro).push(line);
      continue;
    }
    if (line.startsWith("## ")) {
      section = null;
      if (/^## \[Unreleased\]\s*$/.test(line)) {
        if (seenVersion) throw new Error("## [Unreleased] comes after a released version");
        current = null;
        continue;
      }
      const m = VERSION_HEADING.exec(line);
      if (!m) {
        throw new Error(
          `a version heading needs "## [x.y.z] - YYYY-MM-DD" or "## [x.y.z-beta.n] - YYYY-MM-DD": ${line}`,
        );
      }
      const [, version, date] = m;
      if (version.startsWith("0.") && version.includes("-"))
        throw new Error(`${version} is a beta of a 0.x release, which release.yml refuses`);
      if (!validDate(date)) throw new Error(`${version} has no real date: ${date}`);
      if (raw.some((r) => r.version === version)) throw new Error(`${version} is listed twice`);
      current = { version, date, intro: [], sections: new Map() };
      raw.push(current);
      seenVersion = true;
      continue;
    }
    if (LINK_DEFINITION.test(line)) {
      current = null;
      section = null;
      continue;
    }
    if (!current) continue;
    if (line.startsWith("### ")) {
      const kind = line.slice(4).trim();
      if (!isKind(kind)) throw new Error(`${current.version} has a section "${kind}"`);
      if (current.sections.has(kind)) throw new Error(`${current.version} has two ${kind}`);
      section = [];
      current.sections.set(kind, section);
      continue;
    }
    if (/^#/.test(line))
      throw new Error(`${current.version} has a heading it cannot hold: ${line}`);
    if (/^```/.test(line)) fenced = true;
    (section ?? current.intro).push(line);
  }

  for (let i = 1; i < raw.length; i++) {
    if (compareVersions(raw[i - 1].version, raw[i].version) <= 0) {
      throw new Error(`${raw[i - 1].version} is listed above ${raw[i].version}`);
    }
  }

  return raw.map((r) => {
    const link = links.get(r.version);
    if (!link) throw new Error(`${r.version} has no link definition`);
    if (![`${REPO}/compare/`, `${REPO}/releases/tag/`].some((p) => link.startsWith(p))) {
      throw new Error(`${r.version} links somewhere else: ${link}`);
    }
    const intro = renderBlocks(blocks(r.intro));
    const sections = KINDS.filter((k) => r.sections.has(k)).map((kind) => {
      const html = renderBlocks(blocks(r.sections.get(kind) ?? []));
      if (!html) throw new Error(`${r.version} has an empty ${kind}`);
      return { kind, id: `v${r.version}-${kind.toLowerCase()}`, html };
    });
    if (!sections.length && !intro) throw new Error(`${r.version} says nothing`);
    return {
      version: r.version,
      prerelease: r.version.includes("-"),
      date: r.date,
      intro: intro || null,
      sections,
      link,
    };
  });
}

function isKind(s: string): s is Kind {
  return (KINDS as readonly string[]).includes(s);
}

function validDate(s: string): boolean {
  const [y, m, d] = s.split("-").map(Number);
  const t = new Date(Date.UTC(y, m - 1, d));
  return t.getUTCFullYear() === y && t.getUTCMonth() === m - 1 && t.getUTCDate() === d;
}

// SemVer's order for x.y.z and x.y.z-beta.n: a beta sits below its release.
export function compareVersions(a: string, b: string): number {
  const parts = (v: string) => {
    const [release, beta] = v.split("-beta.");
    return [...release.split(".").map(Number), beta === undefined ? Infinity : Number(beta)];
  };
  const x = parts(a);
  const y = parts(b);
  for (let i = 0; i < 4; i++) if (x[i] !== y[i]) return x[i] < y[i] ? -1 : 1;
  return 0;
}

export function longDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return `${d} ${MONTHS[m - 1]} ${y}`;
}

export function monthOf(iso: string): string {
  const [y, m] = iso.split("-").map(Number);
  return `${MONTHS[m - 1]} ${y}`;
}

export function compareBase(link: string): string | null {
  const m = /\/compare\/v(\d+\.\d+\.\d+(?:-beta\.\d+)?)\.\.\.v/.exec(link);
  return m ? m[1] : null;
}

type Block = { t: "p"; text: string } | { t: "pre"; text: string } | ListBlock;
interface ListBlock {
  t: "ul";
  items: Block[][];
  loose: boolean;
}

const indent = (l: string) => l.length - l.trimStart().length;

function blocks(lines: string[]): Block[] {
  const out: Block[] = [];
  let i = 0;
  while (i < lines.length) {
    if (!lines[i].trim()) {
      i++;
      continue;
    }
    if (/^```/.test(lines[i])) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !/^```\s*$/.test(lines[i])) code.push(lines[i++]);
      if (i >= lines.length) throw new Error("a code fence is not closed");
      i++;
      out.push({ t: "pre", text: code.join("\n") });
      continue;
    }
    if (/^- /.test(lines[i])) {
      const items: Block[][] = [];
      let loose = false;
      while (i < lines.length && /^- /.test(lines[i])) {
        const body = [lines[i].slice(2)];
        i++;
        while (i < lines.length && (!lines[i].trim() || indent(lines[i]) >= 2)) {
          if (!lines[i].trim()) {
            // a blank line belongs to the item only when indented text follows
            let j = i;
            while (j < lines.length && !lines[j].trim()) j++;
            if (j < lines.length && indent(lines[j]) >= 2) {
              body.push("");
              i++;
              continue;
            }
            break;
          }
          body.push(lines[i].slice(2));
          i++;
        }
        const inner = blocks(body);
        if (inner.filter((b) => b.t === "p").length > 1) loose = true;
        items.push(inner);
      }
      out.push({ t: "ul", items, loose });
      continue;
    }
    if (/^\s*(\d+\.|[*+>|#])\s/.test(lines[i])) {
      throw new Error(`Markdown the changelog page does not render: ${lines[i]}`);
    }
    const para: string[] = [];
    while (i < lines.length && lines[i].trim() && !/^- |^```/.test(lines[i])) {
      para.push(lines[i].trim());
      i++;
    }
    out.push({ t: "p", text: para.join(" ") });
  }
  return out;
}

export function escapeHtml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

const attr = (s: string) => escapeHtml(s).replace(/"/g, "&quot;");

// break chances in a long code span: after a slash, before a dot or ?, after an underscore
const breaks = (s: string) =>
  escapeHtml(
    s
      .replace(/(?<=\S)\/(?=[^\s/])/g, "/\u0001")
      .replace(/(?<=[^\s.\u0001])([.?])(?=[^\s.])/g, "\u0001$1")
      .replace(/(?<=[^\s_\u0001])_(?=[^\s_])/g, "_\u0001"),
  ).replace(/\u0001/g, "<wbr>");

export function renderInline(s: string): string {
  const codes: string[] = [];
  let out = "";
  let i = 0;
  while (i < s.length) {
    if (s[i] === "`") {
      const run = /^`+/.exec(s.slice(i))![0];
      let j = i + run.length;
      let end = -1;
      while (j < s.length) {
        const k = s.indexOf(run, j);
        if (k < 0) break;
        if (s[k + run.length] !== "`" && s[k - 1] !== "`") {
          end = k;
          break;
        }
        j = k + /^`+/.exec(s.slice(k))![0].length;
      }
      if (end > -1) {
        let c = s.slice(i + run.length, end).replace(/\n/g, " ");
        if (c.length > 2 && c.startsWith(" ") && c.endsWith(" ") && c.trim()) c = c.slice(1, -1);
        // short spans stay on one line; long ones wrap at spaces and break chances, then anywhere
        codes.push(
          c.length > 26
            ? `<code class="long">${breaks(c)}</code>`
            : `<code>${escapeHtml(c)}</code>`,
        );
        out += `\u0000${codes.length - 1}\u0000`;
        i = end + run.length;
        continue;
      }
      out += run;
      i += run.length;
      continue;
    }
    out += s[i];
    i++;
  }
  out = escapeHtml(out)
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, text: string, url: string) => {
      if (!/^(https:\/\/|\/|#)/.test(url)) throw new Error(`a link goes to ${url}`);
      return `<a href="${attr(url)}">${text}</a>`;
    })
    .replace(/\*\*(.+?)\*\*/gs, "<strong>$1</strong>");
  if (/[*_]{2}|\[[^\]]*\]\(/.test(out.replace(/\u0000\d+\u0000/g, ""))) {
    throw new Error(`Markdown the changelog page does not render: ${s.slice(0, 80)}`);
  }
  return out.replace(/\u0000(\d+)\u0000/g, (_, n: string) => codes[Number(n)]);
}

function renderBlocks(bs: Block[]): string {
  return bs
    .map((b) => {
      if (b.t === "p") return `<p>${renderInline(b.text)}</p>`;
      // one block per line, so a line that wraps hangs under its own start
      if (b.t === "pre") {
        const spans = b.text
          .split("\n")
          .map((l) => `<span>${escapeHtml(l)}</span>`)
          .join("");
        return `<pre><code>${spans}</code></pre>`;
      }
      const lis = b.items.map((it) => {
        const first = it[0];
        if (it.length === 1 && first.t === "p" && !b.loose)
          return `<li>${renderInline(first.text)}</li>`;
        const lead = !b.loose && first.t === "p" && it.filter((y) => y.t === "p").length === 1;
        const rest = renderBlocks(lead ? it.slice(1) : it);
        return `<li>${lead && first.t === "p" ? renderInline(first.text) : ""}${rest}</li>`;
      });
      return `<ul>${lis.join("")}</ul>`;
    })
    .join("");
}

export function plainText(html: string): string {
  return html
    .replace(/<[^>]+>/g, "")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&amp;/g, "&");
}
