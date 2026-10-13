// Script and style text stays byte for byte, so its hash matches the browser's.

const VOID = new Set([
  "area",
  "base",
  "br",
  "col",
  "embed",
  "hr",
  "img",
  "input",
  "link",
  "meta",
  "source",
  "track",
  "wbr",
]);
const RAW = new Set(["script", "style"]);

const ENTITIES = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " " };

function decode(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, e) => {
    if (e[0] === "#") {
      const n = e[1] === "x" || e[1] === "X" ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
      return String.fromCodePoint(n);
    }
    return ENTITIES[e.toLowerCase()] ?? m;
  });
}

function element(tag, attrs, parent) {
  return { type: "el", tag, attrs, children: [], parent };
}

export function parse(html) {
  const root = { type: "root", tag: "#root", attrs: {}, children: [], parent: null };
  let cur = root;
  let i = 0;
  const text = (raw) => {
    if (raw) cur.children.push({ type: "text", raw, value: decode(raw), parent: cur });
  };
  while (i < html.length) {
    const lt = html.indexOf("<", i);
    if (lt < 0) {
      text(html.slice(i));
      break;
    }
    text(html.slice(i, lt));
    if (html.startsWith("<!--", lt)) {
      const end = html.indexOf("-->", lt + 4);
      i = end < 0 ? html.length : end + 3;
      continue;
    }
    if (html[lt + 1] === "!" || html[lt + 1] === "?") {
      i = html.indexOf(">", lt) + 1;
      continue;
    }
    if (html[lt + 1] === "/") {
      const end = html.indexOf(">", lt);
      const tag = html
        .slice(lt + 2, end)
        .trim()
        .toLowerCase();
      // close up to the matching open element; a stray end tag changes nothing
      let n = cur;
      while (n && n.tag !== tag) n = n.parent;
      if (n && n.parent) cur = n.parent;
      i = end + 1;
      continue;
    }
    const m = /^<([a-zA-Z][\w:-]*)/.exec(html.slice(lt, lt + 64));
    if (!m) {
      text("<");
      i = lt + 1;
      continue;
    }
    const tag = m[1].toLowerCase();
    let j = lt + m[0].length;
    const attrs = {};
    let selfClosing = false;
    while (j < html.length) {
      while (/\s/.test(html[j])) j++;
      if (html[j] === ">") {
        j++;
        break;
      }
      if (html[j] === "/" && html[j + 1] === ">") {
        selfClosing = true;
        j += 2;
        break;
      }
      const a = /^[^\s"'>/=]+/.exec(html.slice(j));
      if (!a) {
        j++;
        continue;
      }
      const name = a[0].toLowerCase();
      j += a[0].length;
      while (/\s/.test(html[j])) j++;
      let value = "";
      if (html[j] === "=") {
        j++;
        while (/\s/.test(html[j])) j++;
        const q = html[j];
        if (q === '"' || q === "'") {
          const end = html.indexOf(q, j + 1);
          value = html.slice(j + 1, end);
          j = end + 1;
        } else {
          const v = /^[^\s>]+/.exec(html.slice(j))[0];
          value = v;
          j += v.length;
        }
      }
      attrs[name] = decode(value);
    }
    const el = element(tag, attrs, cur);
    cur.children.push(el);
    if (RAW.has(tag) && !selfClosing) {
      const end = html.toLowerCase().indexOf(`</${tag}`, j);
      const raw = html.slice(j, end < 0 ? html.length : end);
      if (raw) el.children.push({ type: "text", raw, value: raw, parent: el });
      i = end < 0 ? html.length : html.indexOf(">", end) + 1;
      continue;
    }
    i = j;
    if (!VOID.has(tag) && !selfClosing) cur = el;
  }
  return root;
}

export function* walk(node) {
  yield node;
  for (const c of node.children ?? []) yield* walk(c);
}

export const elements = (root, tag) =>
  [...walk(root)].filter((n) => n.type === "el" && (!tag || n.tag === tag));

export function textOf(node) {
  if (node.type === "text") return node.value;
  return (node.children ?? []).map(textOf).join("");
}

export function closest(node, test) {
  for (let n = node; n; n = n.parent) if (n.type === "el" && test(n)) return n;
  return null;
}
