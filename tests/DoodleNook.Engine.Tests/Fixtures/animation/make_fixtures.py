"""Mission 012 test fixtures, made with Pillow (an independent encoder): animated GIFs and PNGs with partial frames,
offsets, every disposal mode, transparency, interlacing and several PNG colour types. The expected frames come from
Chromium (make_expected_chromium.js), i.e. what a browser shows. Pillow's own reader is not used as the reference: it
doesn't restore "dispose to previous" regions, and it keeps a first frame's "previous" where the APNG specification
says to clear.
Run: python make_fixtures.py (needs Pillow). The generated files are committed; this script documents how."""
from PIL import Image, ImageDraw
import os

here = os.path.dirname(os.path.abspath(__file__))

def walker(n, w=48, h=32, bg=(0, 0, 0, 0)):
    frames = []
    for i in range(n):
        im = Image.new("RGBA", (w, h), bg)
        d = ImageDraw.Draw(im)
        x = 4 + i * 6
        d.rectangle([x, 8, x + 10, 24], fill=(200, 60 + i * 20, 40, 255))   # a body moving right
        d.ellipse([x + 2, 2, x + 8, 8], fill=(240, 200, 150, 255))         # a head
        if i % 2:
            d.line([x, 24, x - 3, 30], fill=(40, 40, 160, 255), width=2)   # legs alternate
        else:
            d.line([x + 10, 24, x + 13, 30], fill=(40, 40, 160, 255), width=2)
        d.point((1, 1), fill=(255, 255, 255, 255))                           # a still pixel in the corner
        frames.append(im)
    return frames

def gif(name, frames, **kw):
    path = os.path.join(here, name + ".gif")
    frames[0].save(path, save_all=True, append_images=frames[1:], loop=0, **kw)

def apng(name, frames, **kw):
    path = os.path.join(here, name + ".png")
    frames[0].save(path, save_all=True, append_images=frames[1:], loop=0, **kw)

w = walker(6)
gif("walk-dispose2", w, duration=[80, 80, 120, 80, 200, 80], disposal=2, optimize=False)
gif("walk-dispose1", w, duration=80, disposal=1, optimize=False)
gif("walk-dispose3", w, duration=100, disposal=3, optimize=False, interlace=False)
gif("walk-opaque-interlaced", walker(5, bg=(30, 120, 90, 255)), duration=[0, 10, 50, 70, 70], interlace=True)
apng("walk-apng-rgba", w, duration=[80, 80, 120, 80, 200, 80], disposal=1, blend=0)
apng("walk-apng-over", w, duration=100, disposal=0, blend=1)
apng("walk-apng-previous", w, duration=100, disposal=2, blend=1)
apng("walk-apng-palette", [f.convert("RGB").quantize(16) for f in walker(4, bg=(250, 250, 250, 255))], duration=90)
apng("walk-apng-grey", [f.convert("LA") for f in walker(4)], duration=60)
print("done")
