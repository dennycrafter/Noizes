#!/usr/bin/env python3
"""Generate assets/app.ico - indigo rounded square with white sound bars.
PNG-in-ICO format (Vista+), sizes 16/32/48/64/256. Pure stdlib."""
import struct, zlib, os

OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "app.ico")

def png_chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

def make_png(size, px):
    raw = b"".join(b"\x00" + b"".join(struct.pack("BBBB", *p) for p in row) for row in px)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", ihdr)
            + png_chunk(b"IDAT", zlib.compress(raw, 9)) + png_chunk(b"IEND", b""))

def draw(size):
    px = [[(0, 0, 0, 0)] * size for _ in range(size)]
    indigo = (79, 70, 229, 255)
    white = (255, 255, 255, 255)
    r = max(2, size // 5)
    for y in range(size):
        for x in range(size):
            tl = x < r and y < r and (r - x) ** 2 + (r - y) ** 2 > r * r
            tr = x >= size - r and y < r and (x - (size - r - 1)) ** 2 + (r - y - 1) ** 2 > r * r
            bl = x < r and y >= size - r and (r - x - 1) ** 2 + (y - (size - r - 1)) ** 2 > r * r
            br = x >= size - r and y >= size - r and (x - (size - r - 1)) ** 2 + (y - (size - r - 1)) ** 2 > r * r
            if tl or tr or bl or br:
                continue
            px[y][x] = indigo
    # three sound bars
    bar_w = max(1, size // 10)
    heights = [0.4, 0.8, 0.55]
    xs = [int(size * 0.22), int(size * 0.5) - bar_w // 2, int(size * 0.78) - bar_w]
    for hfrac, x0 in zip(heights, xs):
        h = int(size * hfrac)
        y0 = (size - h) // 2
        for y in range(max(1, y0), min(size - 1, y0 + h)):
            for x in range(max(1, x0), min(size - 1, x0 + bar_w)):
                px[y][x] = white
    return px

images = []
for size in (16, 32, 48, 64, 256):
    png = make_png(size, draw(size))
    images.append((size, png))

with open(OUT, "wb") as f:
    f.write(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for size, png in images:
        b = size % 256
        f.write(struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(png), offset))
        offset += len(png)
    for size, png in images:
        f.write(png)

print("wrote", os.path.abspath(OUT), os.path.getsize(OUT), "bytes")
