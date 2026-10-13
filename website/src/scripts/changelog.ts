// The rail marks the version being read: the last entry whose top has passed under the header.
const entries = Array.from(document.querySelectorAll<HTMLElement>("article"));
const box = document.getElementById("rail");
const links = new Map<string, HTMLElement>();
for (const a of document.querySelectorAll<HTMLElement>("#rail a[data-v]"))
  links.set(a.dataset.v!, a);

// a rail taller than the window scrolls its marked version into view, without moving the page
function reveal(a: HTMLElement) {
  if (!box) return;
  const r = a.getBoundingClientRect();
  const b = box.getBoundingClientRect();
  if (r.top < b.top) box.scrollTop -= b.top - r.top;
  else if (r.bottom > b.bottom) box.scrollTop += r.bottom - b.bottom;
  if (r.left < b.left) box.scrollLeft -= b.left - r.left;
  else if (r.right > b.right - 48) box.scrollLeft += r.right - b.right + 48;
}

let on: HTMLElement | null = null;
let queued = false;
function mark() {
  queued = false;
  let id = entries[0].id;
  for (const e of entries) if (e.getBoundingClientRect().top <= 140) id = e.id;
  const next = links.get(id) ?? null;
  if (next === on) return;
  on?.removeAttribute("aria-current");
  on = next;
  if (on) {
    on.setAttribute("aria-current", "location");
    reveal(on);
  }
}

if (entries.length) {
  addEventListener(
    "scroll",
    () => {
      if (!queued) {
        queued = true;
        requestAnimationFrame(mark);
      }
    },
    { passive: true },
  );
  mark();
}
