# Checks the winget manifests komac wrote for a release: each installer's InstallerSha256 equals
# the SHA256SUMS line for the file its InstallerUrl names. komac hashes its own download, so two
# independent hashes of each file agree before anything is submitted.
import re
import sys
from pathlib import Path


class Refused(Exception):
    pass


# The installers of a komac-written manifest: `Installers:` at the top level, each a `- ` item at
# one indent, with InstallerUrl and InstallerSha256 among its keys.
def installers(text):
    lines = text.splitlines()
    try:
        start = lines.index("Installers:")
    except ValueError:
        raise Refused("the installer manifest has no Installers") from None
    found = []
    indent = None
    for line in lines[start + 1 :]:
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if not line.startswith((" ", "-")):
            break
        content = line.lstrip(" ")
        depth = len(line) - len(content)
        if content.startswith("- ") and indent in (None, depth):
            indent = depth
            found.append({})
            content = content[2:]
        match = re.fullmatch(r"(InstallerUrl|InstallerSha256):\s*(.*?)\s*", content)
        if match and found:
            key, value = match.groups()
            if key in found[-1]:
                raise Refused(f"installer {len(found)} names {key} twice")
            found[-1][key] = value.strip("'\"")
    if not found:
        raise Refused("the installer manifest lists no installer")
    return found


def release_lines(path):
    lines = {}
    for line in path.read_text().splitlines():
        match = re.fullmatch(r"([0-9a-f]{64}) [ *](.+)", line)
        if not match:
            raise Refused(f"{path} has a line that is not a SHA-256 and a name: {line}")
        digest, name = match.groups()
        if name in lines:
            raise Refused(f"{path} has two lines for {name}")
        lines[name] = digest
    return lines


def check(folder, sums):
    manifests = sorted(folder.rglob("*.installer.yaml"))
    if len(manifests) != 1:
        raise Refused(f"{folder} holds {len(manifests)} installer manifests, not 1")
    released = release_lines(sums)
    for number, installer in enumerate(installers(manifests[0].read_text()), 1):
        for key in ("InstallerUrl", "InstallerSha256"):
            if not installer.get(key):
                raise Refused(f"installer {number} has no {key}")
        url = installer["InstallerUrl"]
        name = url.rsplit("/", 1)[-1]
        if name not in released:
            raise Refused(f"SHA256SUMS has no line for {name}, which {url} names")
        said = installer["InstallerSha256"]
        if said.lower() != released[name]:
            raise Refused(f"{name}: the manifest says {said}, and SHA256SUMS {released[name]}")
        print(f"ok: {name} is {released[name]} in the manifest and in SHA256SUMS")


def main(argv):
    if len(argv) != 3:
        print("usage: check-winget-hashes.py <manifests folder> <SHA256SUMS>", file=sys.stderr)
        return 2
    try:
        check(Path(argv[1]), Path(argv[2]))
    except Refused as refused:
        print(refused, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
