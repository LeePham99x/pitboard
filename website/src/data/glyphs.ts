// The figures' symbols: Icons.astro draws them on the page, lib/card.ts in the link preview images.

export const line: Record<string, string> = {
  "sf-gear":
    "M10 6.6a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8ZM8.6 2.5h2.8l.4 2 1.6.9 1.9-.7 1.4 2.4-1.5 1.3v1.9l1.5 1.3-1.4 2.4-1.9-.7-1.6.9-.4 2H8.6l-.4-2-1.6-.9-1.9.7-1.4-2.4 1.5-1.3V8.4L3.3 7.1l1.4-2.4 1.9.7 1.6-.9Z",
  "sf-gauge": "M3 14a7 7 0 1 1 14 0M10 14l-3-4.5M5.4 9.6l1 .6M10 6.2v1.2M14.6 9.6l-1 .6",
  "sf-cal": "M4 4.5h12v11H4ZM4 8h12M7 3v3M13 3v3M13.5 13.5a2.5 2.5 0 1 0 0-.01M13.5 12v1.5h1.2",
  "sf-term": "M3.5 4.5h13v11h-13ZM6.5 8.5l2.2 1.8-2.2 1.8M10 12.5h3.5",
  "sf-down": "M10 3a7 7 0 1 1 0 14 7 7 0 0 1 0-14ZM10 6.5v6.5M7.2 10.3 10 13l2.8-2.7",
  "sf-people":
    "M7.5 9.5a2.6 2.6 0 1 0 0-5.2 2.6 2.6 0 0 0 0 5.2ZM2.5 16c.6-2.6 2.6-4 5-4s4.4 1.4 5 4M13.5 9.2a2.2 2.2 0 1 0-.6-4.3M14.5 12.3c1.6.4 2.7 1.6 3 3.7",
  "sf-clock": "M10 6.5V10l2.4 1.6M4.2 7.4A6.3 6.3 0 1 1 3.7 10M2.6 5.6l1.6 1.8 1.9-1.4",
  "sf-steth":
    "M5 3v4.5a3 3 0 0 0 6 0V3M8 10.5v1.8a3.8 3.8 0 0 0 7.6 0v-1M15.6 9.9a1.5 1.5 0 1 0 0-.01",
  "sf-refresh": "M15.6 10A5.6 5.6 0 1 1 13.9 6M14.4 2.8v3.6h-3.6",
  "sf-plus": "M10 4v12M4 10h12",
  "sf-check": "M4.5 10.4 8.2 14l7.3-8.4",
  "sf-chev": "M7.5 4.5 13 10l-5.5 5.5",
  "sf-xmark": "M5 5l10 10M15 5 5 15",
  "sf-person":
    "M10 3a7 7 0 1 1 0 14 7 7 0 0 1 0-14ZM10 6.3a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5ZM5.3 15.2c1-1.6 2.7-2.5 4.7-2.5s3.7.9 4.7 2.5",
};

export const bar: Record<"ic-batt" | "ic-wifi" | "ic-cc", { viewBox: string; body: string }> = {
  "ic-batt": {
    viewBox: "0 0 25 12",
    body: '<rect x=".5" y=".5" width="21.5" height="11" rx="3.2" fill="none" stroke="currentColor" stroke-opacity=".45"/><rect x="22.9" y="4" width="1.6" height="4" rx=".8" fill="currentColor" fill-opacity=".45"/><rect x="2" y="2" width="15" height="8" rx="1.8" fill="currentColor"/>',
  },
  "ic-wifi": {
    viewBox: "0 0 16 12",
    body: '<path d="M1.3 4.3a9.5 9.5 0 0 1 13.4 0M3.8 6.8a6 6 0 0 1 8.4 0" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/><circle cx="8" cy="9.9" r="1.5" fill="currentColor"/>',
  },
  "ic-cc": {
    viewBox: "0 0 16 14",
    body: '<rect x=".75" y="1" width="14.5" height="5" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.2"/><circle cx="3.6" cy="3.5" r="1.5" fill="currentColor"/><rect x=".75" y="8" width="14.5" height="5" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.2"/><circle cx="12.4" cy="10.5" r="1.5" fill="currentColor"/>',
  },
};

// GNOME's symbolic icons in a header bar, 16 pixels square, drawn after Adwaita's.
export const symbolic: Record<
  "adw-tab-new" | "adw-pan-down" | "adw-grid" | "adw-menu" | "adw-close",
  string
> = {
  "adw-tab-new":
    '<rect x="1" y="1" width="14" height="14" rx="2.5" fill="none" stroke="currentColor" stroke-width="2"/><path d="M8 4.5v7M4.5 8h7" stroke="currentColor" stroke-width="2"/>',
  "adw-pan-down":
    '<path d="m4 6.5 4 4 4-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>',
  "adw-grid":
    '<path d="M2 1h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1Zm8 0h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1ZM2 9h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1Zm8 0h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1Z" fill="currentColor"/>',
  "adw-menu": '<path d="M1 3h14M1 8h14M1 13h14" stroke="currentColor" stroke-width="2"/>',
  "adw-close":
    '<path d="m4.75 4.75 6.5 6.5m0-6.5-6.5 6.5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>',
};
