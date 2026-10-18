// CSS order already shows the reader's platform first; this moves it in the DOM so focus follows.
const platform = document.documentElement.getAttribute("data-platform");
const first =
  platform === "linux"
    ? document.getElementById("command-line")
    : platform === "windows"
      ? document.getElementById("windows")
      : null;
const before =
  platform === "linux"
    ? document.getElementById("app")
    : platform === "windows"
      ? document.getElementById("get")
      : null;
if (first && before) before.parentNode?.insertBefore(first, before);
