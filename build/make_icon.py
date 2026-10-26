"""Builds every ServNook logo asset from the master vector, assets/logo/servnook-icon.svg.

    python3 build/make_icon.py          (needs: pip install cairosvg fonttools pillow)

Writes (all committed, so the Windows build needs none of these tools):
  assets/servnook.ico                    multi-size Windows icon (16-256)
  assets/servnook-512.png                README / docs
  assets/logo/servnook-icon.png          1024 x 1024
  assets/logo/servnook-<n>.png           16..256, embedded in ServNook.exe for the in-app logo
  assets/logo/servnook-symbol.svg        flame only, transparent background (orange)
  assets/logo/servnook-symbol-white.svg  monochrome white
  assets/logo/servnook-symbol-dark.svg   monochrome dark (#151C26)
  assets/logo/servnook-logo-light.svg    icon + "ServNook" wordmark for light backgrounds
  assets/logo/servnook-logo-dark.svg     icon + "ServNook" wordmark for dark backgrounds
  build/installer-side.png, build/installer-small.png   Inno Setup wizard images
"""
import io
import os
import re

import cairosvg
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
A = lambda *p: os.path.join(ROOT, "assets", *p)
ORANGE, INK, PAPER = "#EA4B0C", "#151C26", "#F5F7FA"

master = open(A("logo", "servnook-icon.svg"), encoding="utf-8").read()
paths = re.findall(r"<path[^>]*/>", master)
flame, flick, hollow, chevron, underscore = paths  # order in the master file
d = lambda tag: re.search(r'\sd="([^"]+)"', tag).group(1)


def render(svg, size):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=size, output_height=size))).convert("RGBA")


def symbol(color):
    """The flame alone on a transparent background: the hollow is a real hole, the prompt sits inside it."""
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        '<defs><mask id="hole" maskUnits="userSpaceOnUse" x="0" y="0" width="512" height="512">'
        '<rect x="0" y="0" width="512" height="512" fill="#fff"/><path fill="#000" d="%s"/></mask></defs>'
        '<g mask="url(#hole)" fill="%s"><path d="%s"/><path d="%s"/></g>'
        '<g fill="none" stroke="%s" stroke-width="13" stroke-linecap="round" stroke-linejoin="round"><path d="%s"/><path d="%s"/></g>'
        "</svg>" % (d(hollow), color, d(flame), d(flick), color, d(chevron), d(underscore))
    )


def wordmark(color, x, baseline, height):
    font = TTFont(os.path.join(ROOT, "assets", "fonts", "Inter-Bold.ttf"))
    gs, cmap, hmtx = font.getGlyphSet(), font.getBestCmap(), font["hmtx"]
    scale = height / font["head"].unitsPerEm
    out, pen_x = [], 0
    for ch in "ServNook":
        name = cmap[ord(ch)]
        pen = SVGPathPen(gs)
        gs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, x + pen_x * scale, baseline)))
        out.append(pen.getCommands())
        pen_x += hmtx[name][0]
    return '<path fill="%s" d="%s"/>' % (color, " ".join(out)), pen_x * scale


def lockup(text_color):
    icon = re.sub(r"<title>.*?</title>|<!--.*?-->", "", master, flags=re.S)
    inner = icon[icon.index(">") + 1 : icon.rindex("</svg>")]
    word, width = wordmark(text_color, 152, 118, 96)
    w = int(152 + width + 8)
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d 160"><title>ServNook</title>'
            '<g transform="translate(8 16) scale(0.25)">%s</g>%s</svg>' % (w, inner, word))


def write(name, text):
    with open(A("logo", name), "w", encoding="utf-8") as f:
        f.write(text)


def installer_images():
    sym = render(symbol("#FFFFFF"), 150)
    side = Image.new("RGBA", (328, 628), ORANGE)
    side.alpha_composite(sym, (89, 150))
    dr = ImageDraw.Draw(side)
    fb = ImageFont.truetype(os.path.join(ROOT, "assets", "fonts", "Inter-Bold.ttf"), 44)
    fr = ImageFont.truetype(os.path.join(ROOT, "assets", "fonts", "Inter-Regular.ttf"), 19)
    def centre(text, y, font, fill):
        w = dr.textlength(text, font=font)
        dr.text(((328 - w) / 2, y), text, font=font, fill=fill)
    centre("ServNook", 340, fb, "#FFFFFF")
    centre("Apache · PHP · MariaDB", 408, fr, (255, 236, 226))
    centre("Node.js · phpMyAdmin", 436, fr, (255, 236, 226))
    side.convert("RGB").save(os.path.join(ROOT, "build", "installer-side.png"))
    render(master, 110).save(os.path.join(ROOT, "build", "installer-small.png"))


if __name__ == "__main__":
    os.makedirs(A("logo"), exist_ok=True)
    big = render(master, 1024)
    big.save(A("logo", "servnook-icon.png"))
    render(master, 512).save(A("servnook-512.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = {n: render(master, n) for n in sizes}
    # Classic bitmap frames (not PNG) so every Windows icon API reads every size.
    frames[256].save(A("servnook.ico"), format="ICO", sizes=[(n, n) for n in sizes], append_images=[frames[n] for n in sizes[:-1]], bitmap_format="bmp")
    for n in (16, 24, 32, 48, 64, 128, 256):
        frames[n].save(A("logo", "servnook-%d.png" % n))
    write("servnook-symbol.svg", symbol(ORANGE))
    write("servnook-symbol-white.svg", symbol("#FFFFFF"))
    write("servnook-symbol-dark.svg", symbol(INK))
    write("servnook-logo-light.svg", lockup(INK))
    write("servnook-logo-dark.svg", lockup(PAPER))
    installer_images()
    print("logo assets written")
