import { SLUG } from "./site.ts";

// Each command as copied, and as shown: <wbr> marks where a phone may break it, .nw where it may not.
export interface Command {
  copy: string;
  html: string;
}

const installSh = `curl -fsSL <wbr>https://<wbr>usepitboard.com/<wbr><span class="nw">install.sh | sh</span>`;

export const commands = {
  brewApp: {
    copy: "brew install --cask datlechin/tap/pitboard-app",
    html: `brew install --cask <wbr><span class="nw">datlechin/<wbr>tap/<wbr><span class="nw">pitboard-app</span></span>`,
  },
  brewCli: {
    copy: "brew install datlechin/tap/pitboard",
    html: `brew install <wbr><span class="nw">datlechin/<wbr>tap/<wbr><span class="nw">pitboard</span></span>`,
  },
  installScript: { copy: "curl -fsSL https://usepitboard.com/install.sh | sh", html: installSh },
  binstall: { copy: "cargo binstall pitboard", html: "cargo binstall pitboard" },
  cargoInstall: {
    copy: "cargo install --locked pitboard",
    html: "cargo install --locked pitboard",
  },
  wget: {
    copy: "wget -qO- https://usepitboard.com/install.sh | sh",
    html: `wget -qO- <wbr>https://<wbr>usepitboard.com/<wbr><span class="nw">install.sh | sh</span>`,
  },
  installDir: {
    copy: 'curl -fsSL https://usepitboard.com/install.sh | PITBOARD_INSTALL_DIR="$HOME/tools/bin" sh',
    html: `curl -fsSL <wbr>https://<wbr>usepitboard.com/<wbr><span class="nw">install.sh |</span> <wbr><span class="nw">PITBOARD_INSTALL_DIR=<wbr>&quot;$HOME/<wbr>tools/<wbr>bin&quot; sh</span>`,
  },
  shasum: {
    copy: "shasum -a 256 -c SHA256SUMS --ignore-missing",
    html: `shasum -a 256 -c SHA256SUMS <wbr><span class="nw"><span class="nw">--ignore-missing</span></span>`,
  },
  sha256sum: {
    copy: "sha256sum -c SHA256SUMS --ignore-missing",
    html: `sha256sum -c SHA256SUMS <wbr><span class="nw"><span class="nw">--ignore-missing</span></span>`,
  },
} satisfies Record<string, Command>;

export function attestCommand(file: string): Command {
  const dot = file.lastIndexOf(".");
  const [stem, ext] = [file.slice(0, dot), file.slice(dot)];
  return {
    copy: `gh attestation verify ${file} --repo ${SLUG}`,
    html: `gh attestation verify <wbr><span class="nw"><span class="nw">${stem}</span><wbr><span class="nw">${ext}</span></span> <wbr><span class="nw"><span class="nw">--repo</span> <span class="nw">${SLUG}</span></span>`,
  };
}
