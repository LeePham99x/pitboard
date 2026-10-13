// CSS order already shows the reader's system first; this moves it in the DOM so focus follows.
const os = document.documentElement.getAttribute("data-os");
const first =
  os === "linux"
    ? document.getElementById("command-line")
    : os === "windows"
      ? document.getElementById("windows")
      : null;
const before =
  os === "linux"
    ? document.getElementById("app")
    : os === "windows"
      ? document.querySelector(".dl-get")
      : null;
if (first && before) before.parentNode?.insertBefore(first, before);
