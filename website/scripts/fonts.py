"""Make the site's two font files, the list of code points each one draws, and the single
weights the link preview images are drawn with.

Local only; the outputs are committed. Needs fontTools and brotli.

    python3 scripts/fonts.py subset Inter[opsz,wght].ttf src/assets/fonts/Inter-4.1-opsz-wght.woff2
    python3 scripts/fonts.py subset JetBrainsMono[wght].ttf src/assets/fonts/JetBrainsMono-2.304-wght.woff2
    python3 scripts/fonts.py codepoints src/assets/fonts/*.woff2
    python3 scripts/fonts.py instance src/assets/fonts/Inter-4.1-opsz-wght.woff2 400 src/assets/fonts/Inter-4.1-400.ttf

Inputs are the upstream variable fonts: Inter 4.1 and JetBrains Mono 2.304, both OFL. The
faces carry no unicode-range, so the terminal's block glyphs and the menu's key symbols are
drawn by these files and never by a system font; guard.mjs checks every character the pages
show against the lists written here.

Satori, which draws the link preview images, reads no WOFF2 and no variable axes. `instance`
pins a committed WOFF2 at one weight, and Inter at its text optical size, as a TTF with the
same glyphs.
"""

import io
import json
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

LATIN = (
    "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,"
    "U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD"
)
TERM = [0x2502, 0x2588, 0x2591, 0x25B2, 0x25BC, 0x25CB, 0x25CF, 0x2713, 0x2717, 0x00B7]
MENU = [0x2318, 0x21E7, 0x2325, 0x2303, 0x21A9, 0x232B]
FEATURES = ["calt", "case", "ccmp", "cv11", "dnom", "frac", "locl", "numr", "ss01", "tnum", "zero"]


def parse(ranges):
    out = []
    for part in ranges.split(","):
        part = part.strip().replace("U+", "")
        if "-" in part:
            a, b = part.split("-")
            out.extend(range(int(a, 16), int(b, 16) + 1))
        else:
            out.append(int(part, 16))
    return out


def write_codepoints(woff2):
    cmap = TTFont(woff2).getBestCmap()
    out = os.path.splitext(woff2)[0] + ".codepoints.json"
    with open(out, "w") as f:
        json.dump(sorted(cmap), f)
        f.write("\n")
    print(f"{out}: {len(cmap)} code points")


def make(src, dst):
    font = TTFont(src)
    axes = {a.axisTag for a in font["fvar"].axes}
    limits = {"wght": (400, 700)}
    if "opsz" in axes:
        limits["opsz"] = (14, 32)
    font = instancer.instantiateVariableFont(font, limits, updateFontNames=False)
    # the subsetter needs the instanced font read back in
    buf = io.BytesIO()
    font.save(buf)
    buf.seek(0)
    font = TTFont(buf)
    opts = subset.Options()
    opts.layout_features = FEATURES
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.hinting = False
    opts.desubroutinize = True
    opts.flavor = "woff2"
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=parse(LATIN) + TERM + MENU)
    sub.subset(font)
    font.flavor = "woff2"
    font.save(dst)
    print(f"{dst}: {os.path.getsize(dst)} bytes")
    write_codepoints(dst)


def instance(src, weight, dst):
    # the source's timestamp, so the same WOFF2 gives the same bytes
    font = TTFont(src, recalcTimestamp=False)
    axes = {a.axisTag: a.minValue for a in font["fvar"].axes}
    axes["wght"] = float(weight)
    font = instancer.instantiateVariableFont(font, axes, updateFontNames=False)
    font.flavor = None
    font.save(dst)
    print(f"{dst}: {os.path.getsize(dst)} bytes")


def main(argv):
    if len(argv) == 3 and argv[0] == "subset":
        make(argv[1], argv[2])
    elif len(argv) == 4 and argv[0] == "instance":
        instance(argv[1], argv[2], argv[3])
    elif len(argv) >= 2 and argv[0] == "codepoints":
        for path in argv[1:]:
            write_codepoints(path)
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
