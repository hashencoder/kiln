"""Draws the Kiln icon (flame in a rounded square) as a multi-size .ico."""
import sys
from PIL import Image, ImageDraw

ACCENT = (194, 65, 12, 255)


def bezier(p0, p1, p2, p3, steps=24):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        mt = 1 - t
        x = mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0]
        y = mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1]
        pts.append((x, y))
    return pts


def draw(size):
    scale = 8  # supersample for smooth edges
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=ACCENT)
    u = s / 32
    segs = [
        ((16, 5), (22, 11), (25, 16), (24, 21)),
        ((24, 21), (23, 25.5), (19.5, 28), (16, 28)),
        ((16, 28), (12.5, 28), (9, 25.5), (8, 21)),
        ((8, 21), (7.5, 17), (10, 14), (12, 12.5)),
        ((12, 12.5), (12, 15), (13, 17), (14.5, 17.5)),
        ((14.5, 17.5), (14, 13), (15, 9), (16, 5)),
    ]
    pts = []
    for seg in segs:
        pts += bezier(*[(x * u, y * u) for x, y in seg])
    d.polygon(pts, fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    out = sys.argv[1]
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    big = draw(256)
    big.save(out, format="ICO", sizes=[(n, n) for n in sizes])
    if len(sys.argv) > 2:
        draw(512).save(sys.argv[2])
