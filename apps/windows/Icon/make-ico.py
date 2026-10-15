# Writes Pitboard.App/Pitboard.ico from the drawings beside it: python3 make-ico.py, with
# rsvg-convert and Pillow. Each size comes from the grid it is drawn on, or the nearest.
import io
import struct
import subprocess
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
DRAWN = {
    16: "icon-16.svg",
    20: "icon-16.svg",
    24: "icon-24.svg",
    32: "icon-16.svg",
    40: "icon-48.svg",
    48: "icon-48.svg",
    64: "icon-48.svg",
    256: "icon-48.svg",
}


def rendered(size, drawing):
    png = subprocess.run(
        ["rsvg-convert", "-w", str(size), "-h", str(size), str(HERE / drawing)],
        check=True,
        capture_output=True,
    ).stdout
    return Image.open(io.BytesIO(png)).convert("RGBA")


# Below 256 a frame is a DIB with its AND mask, which every icon loader reads; 256 is a PNG.
def frame(image):
    size = image.width
    if size == 256:
        out = io.BytesIO()
        image.save(out, "PNG")
        return out.getvalue()
    pixels = image.load()
    rows = range(size - 1, -1, -1)
    colour = b"".join(
        bytes((b, g, r, a)) for y in rows for (r, g, b, a) in (pixels[x, y] for x in range(size))
    )
    stride = (size + 31) // 32 * 4
    mask = b""
    for y in rows:
        bits = bytearray(stride)
        for x in range(size):
            if pixels[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bytes(bits)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(colour) + len(mask), 0, 0, 0, 0)
    return header + colour + mask


frames = [(size, frame(rendered(size, drawing))) for size, drawing in DRAWN.items()]
offset = 6 + 16 * len(frames)
directory = struct.pack("<HHH", 0, 1, len(frames))
for size, data in frames:
    directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
(HERE.parent / "Pitboard.App" / "Pitboard.ico").write_bytes(directory + b"".join(data for _, data in frames))
