const d = document.documentElement;

function put(k: string, v: string) {
  try {
    localStorage.setItem(k, v);
  } catch {}
}

function all<T extends Element = HTMLElement>(sel: string, root: ParentNode = document): T[] {
  return Array.from(root.querySelectorAll<T>(sel));
}

function head() {
  const set = () => d.classList.toggle("scrolled", scrollY > 4);
  addEventListener("scroll", set, { passive: true });
  set();
}

// a link to a <details>, or into one, opens it
function openTarget() {
  const id = decodeURIComponent(location.hash.slice(1));
  const box = id ? document.getElementById(id)?.closest("details") : null;
  if (box) box.open = true;
}

function theming() {
  const names: Record<string, string> = { system: "System", light: "Light", dark: "Dark" };
  const btn = document.getElementById("theme-btn");
  const menu = document.getElementById("theme-menu");
  const items = menu ? all<HTMLButtonElement>("[role=menuitemradio]", menu) : [];
  const segs = all<HTMLButtonElement>("#theme-seg button");
  const metas = all<HTMLMetaElement>('meta[name="theme-color"]');
  const current = () => d.getAttribute("data-theme") || "system";
  function sync() {
    const v = current();
    btn?.setAttribute("aria-label", `Theme: ${names[v]}`);
    for (const i of items) i.setAttribute("aria-checked", String(i.dataset.v === v));
    for (const b of segs) b.setAttribute("aria-pressed", String(b.dataset.v === v));
    for (const m of metas) {
      const own = m.dataset.scheme === "dark" ? "#090b0b" : "#ffffff";
      m.content = v === "system" ? own : v === "dark" ? "#090b0b" : "#ffffff";
    }
  }
  function set(v: string) {
    if (v === "system") d.removeAttribute("data-theme");
    else d.setAttribute("data-theme", v);
    put("theme", v);
    sync();
  }
  for (const b of segs) b.addEventListener("click", () => set(b.dataset.v!));
  sync();
  if (!btn || !menu) return;
  const open = (i?: number) => {
    menu.hidden = false;
    btn.setAttribute("aria-expanded", "true");
    const n =
      i ??
      Math.max(
        0,
        items.findIndex((x) => x.dataset.v === current()),
      );
    items[n].focus();
  };
  const close = (refocus: boolean) => {
    menu.hidden = true;
    btn.setAttribute("aria-expanded", "false");
    if (refocus) btn.focus();
  };
  btn.addEventListener("click", () => (menu.hidden ? open() : close(false)));
  btn.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      open(0);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      open(items.length - 1);
    }
  });
  items.forEach((it, i) => {
    it.addEventListener("click", () => {
      set(it.dataset.v!);
      close(true);
    });
    it.addEventListener("keydown", (e) => {
      let n: number | null = null;
      if (e.key === "ArrowDown") n = (i + 1) % items.length;
      else if (e.key === "ArrowUp") n = (i - 1 + items.length) % items.length;
      else if (e.key === "Home") n = 0;
      else if (e.key === "End") n = items.length - 1;
      else if (e.key === "Escape") {
        e.preventDefault();
        close(true);
        return;
      } else if (e.key === "Tab") {
        close(false);
        return;
      }
      if (n != null) {
        e.preventDefault();
        items[n].focus();
      }
    });
  });
  document.addEventListener("click", (e) => {
    if (!menu.hidden && !(e.target as Element).closest("#theme")) close(false);
  });
}

function phoneMenu() {
  for (const m of all<HTMLDetailsElement>("#mnav")) {
    m.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && m.open) {
        e.preventDefault();
        m.open = false;
        m.querySelector("summary")?.focus();
      }
    });
  }
}

// a box that scrolls sideways becomes a focusable region, named by the caption in data-scroll
function scrollers() {
  const boxes = all("[data-scroll]");
  if (!boxes.length) return;
  const check = () => {
    for (const b of boxes) {
      if (b.offsetParent === null) continue;
      if (b.scrollWidth > b.clientWidth + 1) {
        b.tabIndex = 0;
        b.setAttribute("role", "region");
        b.setAttribute("aria-labelledby", b.dataset.scroll!);
      } else {
        b.removeAttribute("tabindex");
        b.removeAttribute("role");
        b.removeAttribute("aria-labelledby");
      }
    }
  };
  check();
  addEventListener("resize", check);
  document.addEventListener("surface", check);
  document.fonts?.ready.then(check);
}

// The install tabs and the App and Command line buttons over each figure share one choice.
function surfaces() {
  const tabs = all<HTMLButtonElement>("[role=tab][data-surface]");
  const picks = all<HTMLButtonElement>("button[data-surface]:not([role=tab])");
  if (!tabs.length && !picks.length) return;
  const mark = (name: string) => {
    for (const t of tabs) {
      const on = t.dataset.surface === name;
      t.setAttribute("aria-selected", String(on));
      t.tabIndex = on ? 0 : -1;
    }
    for (const b of picks) b.setAttribute("aria-pressed", String(b.dataset.surface === name));
  };
  const select = (name: string, focus: HTMLElement | null) => {
    mark(name);
    d.setAttribute("data-surface", name);
    focus?.focus();
    put("tab", name);
    document.dispatchEvent(new CustomEvent("surface", { detail: name }));
  };
  for (const b of picks) b.addEventListener("click", () => select(b.dataset.surface!, null));
  for (const tl of all("[role=tablist]")) {
    const own = all<HTMLButtonElement>("[role=tab]", tl);
    own.forEach((t, i) => {
      t.addEventListener("click", () => select(t.dataset.surface!, null));
      t.addEventListener("keydown", (e) => {
        let n: HTMLButtonElement | null = null;
        if (e.key === "ArrowRight") n = own[(i + 1) % own.length];
        else if (e.key === "ArrowLeft") n = own[(i - 1 + own.length) % own.length];
        else if (e.key === "Home") n = own[0];
        else if (e.key === "End") n = own[own.length - 1];
        if (n) {
          e.preventDefault();
          select(n.dataset.surface!, n);
        }
      });
    });
  }
  mark(d.getAttribute("data-surface") || "app");
}

function copying() {
  const buttons = all<HTMLButtonElement>("[data-copy]");
  if (!buttons.length) return;
  const live = document.createElement("p");
  live.className = "sr-only";
  live.setAttribute("role", "status");
  document.body.appendChild(live);
  for (const b of buttons) {
    const label = b.textContent;
    const name = b.getAttribute("aria-label");
    let timer = 0;
    let shown: Element | null = null;
    const restore = () => {
      b.textContent = label;
      if (name) b.setAttribute("aria-label", name);
      live.textContent = "";
      if (shown) {
        shown.classList.add("sr-only");
        shown.parentElement?.removeAttribute("data-full");
        shown = null;
      }
    };
    const done = (ok: boolean) => {
      if (ok) {
        b.textContent = "Copied";
        // the visible label stays part of the accessible name (WCAG 2.5.3)
        if (name) b.setAttribute("aria-label", "Copied");
        live.textContent = "Copied";
      } else {
        const code = b.parentElement?.querySelector("code");
        if (code) {
          // a digest shown in part shows whole while it is selected
          const hid = code.querySelector(".sr-only");
          if (hid) {
            hid.classList.remove("sr-only");
            code.setAttribute("data-full", "");
            shown = hid;
          }
          const r = document.createRange();
          r.selectNodeContents(code);
          const s = getSelection();
          s?.removeAllRanges();
          s?.addRange(r);
        } else {
          // a link to a part of this page: the address bar holds it instead
          const text = b.dataset.copy!;
          const at = text.indexOf("#");
          const part = at > -1 ? document.getElementById(text.slice(at + 1)) : null;
          if (part) history.replaceState(null, "", `#${part.id}`);
        }
      }
      clearTimeout(timer);
      timer = window.setTimeout(restore, 2000);
    };
    b.addEventListener("click", () => {
      if (navigator.clipboard?.writeText) {
        navigator.clipboard.writeText(b.dataset.copy!).then(
          () => done(true),
          () => done(false),
        );
      } else done(false);
    });
  }
}

function afterDownload() {
  for (const a of all("[data-download]")) {
    a.addEventListener("click", () => {
      for (const p of all("[data-after]")) p.setAttribute("data-shown", "");
    });
  }
}

function demo() {
  const fig = document.getElementById("demo");
  if (!fig) {
    d.removeAttribute("data-demo");
    return;
  }
  if (d.getAttribute("data-demo") !== "start") return;
  let ran = false;
  const run = () => {
    if (ran) return;
    ran = true;
    const steps: [number, string | null][] = [
      [500, "move"],
      [1400, "hl"],
      [1900, "pick"],
      [2100, "sw"],
      [3100, null],
    ];
    for (const [at, state] of steps) {
      setTimeout(() => {
        if (state) d.setAttribute("data-demo", state);
        else d.removeAttribute("data-demo");
      }, at);
    }
  };
  const watch = () => {
    if (!("IntersectionObserver" in window)) {
      run();
      return;
    }
    const io = new IntersectionObserver(
      (es) => {
        if (es.some((e) => e.isIntersecting)) {
          io.disconnect();
          run();
        }
      },
      { threshold: 0.6 },
    );
    io.observe(fig);
  };
  // a figure hidden by the Command line tab waits until the App tab shows it
  if (d.getAttribute("data-surface") === "app") watch();
  else {
    const on = (e: Event) => {
      if ((e as CustomEvent).detail === "app") {
        document.removeEventListener("surface", on);
        watch();
      }
    };
    document.addEventListener("surface", on);
  }
}

head();
openTarget();
addEventListener("hashchange", openTarget);
theming();
phoneMenu();
scrollers();
surfaces();
copying();
afterDownload();
demo();
