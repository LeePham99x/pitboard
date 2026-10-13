// Inlined and hashed into the CSP, so theme, system and tab are set before the first paint.
(function () {
  var d = document.documentElement;
  function get(k) {
    try {
      return localStorage.getItem(k);
    } catch (e) {
      return null;
    }
  }
  var theme = get("theme");
  if (theme === "dark" || theme === "light") {
    d.setAttribute("data-theme", theme);
    var bg = theme === "dark" ? "#090b0b" : "#ffffff";
    [].forEach.call(document.querySelectorAll('meta[name="theme-color"]'), function (m) {
      m.setAttribute("content", bg);
    });
  }
  var ua = navigator.userAgent || "";
  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || "";
  var os = "";
  if (/iPhone|iPad|iPod|Android/i.test(ua) || /Android/i.test(platform)) os = "mobile";
  else if (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1) os = "mobile";
  else if (/Windows/i.test(platform || ua)) os = "windows";
  else if (/Linux|X11|CrOS|Chrome OS/i.test(platform || ua)) os = "linux";
  if (os) d.setAttribute("data-os", os);
  var tab = get("tab");
  if (!tab && os === "linux") tab = "cli";
  d.setAttribute("data-surface", tab === "cli" ? "cli" : "app");
  // the menu demo starts rewound, so its first frame never flashes the end state
  if (!matchMedia("(prefers-reduced-motion: reduce)").matches) d.setAttribute("data-demo", "start");
  d.classList.add("js");
})();
