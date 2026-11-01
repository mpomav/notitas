"""Draws the app icon: a yellow note with a folded corner, at every size Windows asks for."""
import struct
import sys
from io import BytesIO

from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 48, 64, 256]
SCALE = 8


def draw(size):
    canvas = size * SCALE
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    pen = ImageDraw.Draw(image)
    margin = canvas * 0.08
    right = canvas - margin
    fold = canvas * 0.3
    radius = canvas * 0.12

    pen.rounded_rectangle((margin, margin, right, right), radius, fill=(255, 214, 79, 255))
    pen.polygon([(right - fold, right + 1), (right + 1, right - fold), (right + 1, right + 1)], fill=(0, 0, 0, 0))
    pen.polygon([(right - fold, right), (right, right - fold), (right - fold, right - fold)], fill=(226, 170, 40, 255))
    if size >= 24:
        for row, width in ((0.32, 0.5), (0.47, 0.5), (0.62, 0.26)):
            top = canvas * row
            pen.rounded_rectangle((canvas * 0.22, top, canvas * (0.22 + width), top + canvas * 0.06),
                                  canvas * 0.03, fill=(120, 88, 20, 255))
    return image.resize((size, size), Image.LANCZOS)


def write_ico(path):
    entries = []
    for size in SIZES:
        buffer = BytesIO()
        draw(size).save(buffer, "PNG")
        entries.append((size, buffer.getvalue()))

    offset = 6 + 16 * len(entries)
    with open(path, "wb") as out:
        out.write(struct.pack("<HHH", 0, 1, len(entries)))
        for size, data in entries:
            edge = 0 if size == 256 else size
            out.write(struct.pack("<BBBBHHII", edge, edge, 0, 0, 1, 32, len(data), offset))
            offset += len(data)
        for _, data in entries:
            out.write(data)


if __name__ == "__main__":
    write_ico(sys.argv[1])
