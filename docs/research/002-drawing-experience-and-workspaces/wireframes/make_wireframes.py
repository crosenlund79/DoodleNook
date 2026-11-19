"""Static, to-scale research wireframes of five DoodleNook workspace concepts.

Each image is 1920x1080 = the Kamvas 13's physical pixels. UI sizes are specified in
device-independent pixels (DIP) and multiplied by SCALE (Windows display scaling, 150%).
Research concepts only - not a design spec and not an interactive prototype.
"""
import html
import os

OUT = os.path.dirname(os.path.abspath(__file__))
W, H = 1920, 1080
SCALE = 1.5          # Windows display scale assumed for a 13.3" 1080p panel
PX_PER_MM = 1920 / 293.76   # Kamvas 13 active area width (mm) -> ~6.54 px/mm (~166 ppi)


def d(v):            # DIP -> physical px
    return round(v * SCALE)


def mm(px):
    return px / PX_PER_MM


CSS = """
* { box-sizing: border-box; margin: 0; padding: 0; }
body { width: 1920px; height: 1080px; position: relative; overflow: hidden;
       font-family: 'Segoe UI', 'DejaVu Sans', Arial, sans-serif; background: #f3f2ee; }
.ui   { position: absolute; background: #2f3237; color: #e9e9e9; border: 1px solid #1d1f22;
        display: flex; align-items: center; justify-content: center; font-size: 19px; }
.ui.lighter { background: #3b3f45; }
.ui.float { background: rgba(47,50,55,0.94); border-radius: 14px; box-shadow: 0 6px 22px rgba(0,0,0,.35); }
.btn  { position: absolute; background: #4a4f57; border-radius: 7px; color: #f4f4f4;
        display: flex; align-items: center; justify-content: center; font-size: 17px; }
.btn.on { background: #c9772f; }
.canvas { position: absolute; background:
        repeating-linear-gradient(45deg, #fbfaf7 0 22px, #f6f4ef 22px 44px);
        border: 2px dashed #b9b4a8; }
.canvas .lab { position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
        text-align: center; color: #6f6a5f; font-size: 30px; line-height: 1.35; }
.note { position: absolute; background: #fff4e3; border: 2px solid #c9772f; color: #3b2a14;
        border-radius: 10px; padding: 10px 14px; font-size: 19px; line-height: 1.32; }
.tag  { position: absolute; width: 34px; height: 34px; border-radius: 50%; background: #c9772f;
        color: #fff; font-weight: 700; font-size: 19px; display: flex; align-items: center;
        justify-content: center; border: 2px solid #fff; box-shadow: 0 1px 4px rgba(0,0,0,.3); }
.title { position: absolute; left: 0; right: 0; font-size: 16px; }
.swatch { position: absolute; border: 1px solid #222; }
.ring { position: absolute; border-radius: 50%; border: 3px solid rgba(255,255,255,.25); }
.pixgrid { position: absolute; background-image:
        linear-gradient(rgba(0,0,0,.12) 1px, transparent 1px),
        linear-gradient(90deg, rgba(0,0,0,.12) 1px, transparent 1px);
        background-size: 24px 24px; }
"""


class Page:
    def __init__(self, name, title):
        self.name, self.title, self.parts = name, title, []

    def add(self, s):
        self.parts.append(s)

    def ui(self, x, y, w, h, label="", cls="", style=""):
        self.add(f'<div class="ui {cls}" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px;{style}">'
                 f'{label}</div>')

    def btn(self, x, y, w, h, label="", on=False, style=""):
        self.add(f'<div class="btn{" on" if on else ""}" style="left:{x}px;top:{y}px;width:{w}px;'
                 f'height:{h}px;{style}">{label}</div>')

    def canvas(self, x, y, w, h, extra="", lab_top="50%", lab_left="50%"):
        pct = 100 * w * h / (W * H)
        self.add(f'<div class="canvas" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px">'
                 f'<div class="lab" style="top:{lab_top};left:{lab_left}"><b>CANVAS</b><br>'
                 f'{w} × {h} px<br>{pct:.0f}% of the screen{extra}</div></div>')
        return pct

    def note(self, x, y, w, text, n=None):
        self.add(f'<div class="note" style="left:{x}px;top:{y}px;width:{w}px">'
                 f'{"<b>"+str(n)+".</b> " if n else ""}{text}</div>')

    def tag(self, x, y, n):
        self.add(f'<div class="tag" style="left:{x}px;top:{y}px">{n}</div>')

    def html(self):
        return (f'<!doctype html><html><head><meta charset="utf-8"><title>{html.escape(self.title)}'
                f'</title><style>{CSS}</style></head><body>{"".join(self.parts)}</body></html>')


def toolstrip(p, x, y, tools, w=d(44), bh=d(36), gap=d(4), active=0):
    p.ui(x, y, w, H - y - (0), "", "lighter")
    by = y + gap
    for i, t in enumerate(tools):
        p.btn(x + (w - bh) // 2, by, bh, bh, t, on=(i == active), style="font-size:13px")
        by += bh + gap


TOOLS_FULL = ["Move", "Marq", "Lasso", "Wand", "Crop", "Pick", "Brush", "Pencil", "Erase", "Fill",
              "Blur", "Text", "Shape", "Hand", "Zoom"]
TOOLS_QUICK = ["Brush", "Pencil", "Erase", "Pick", "Lasso", "Move", "Fill", "Hand"]


# ---------------------------------------------------------------- A  Pure Sketch
def pure_sketch():
    p = Page("A_pure_sketch", "A Pure Sketch")
    p.canvas(0, 0, W, H, "<br><small>borderless full screen</small>", lab_top="86%", lab_left="62%")
    # transient status pill (fades out after ~1.5 s)
    p.ui(24, 24, d(250), d(28), "Brush 12 px · 80% · Layer 3 &nbsp;●", "float",
         "font-size:18px")
    # radial palette at the pen position
    cx, cy, r = 1180, 470, d(150)
    p.add(f'<div class="ui float" style="left:{cx-r}px;top:{cy-r}px;width:{2*r}px;height:{2*r}px;'
          f'border-radius:50%"></div>')
    p.add(f'<div class="ring" style="left:{cx-r+d(40)}px;top:{cy-r+d(40)}px;width:{2*r-d(80)}px;'
          f'height:{2*r-d(80)}px"></div>')
    import math
    labels = ["Pencil", "Ink", "Soft", "Marker", "Erase", "Undo", "Redo", "Flip", "Layer+", "Hide UI"]
    for i, lab in enumerate(labels):
        a = -math.pi / 2 + i * 2 * math.pi / len(labels)
        bx, by = cx + (r - d(22)) * math.cos(a), cy + (r - d(22)) * math.sin(a)
        p.btn(int(bx - d(19)), int(by - d(19)), d(38), d(38), lab,
              on=(lab == "Pencil"), style="border-radius:50%;font-size:13px")
    p.add(f'<div class="swatch" style="left:{cx-d(46)}px;top:{cy-d(46)}px;width:{d(92)}px;'
          f'height:{d(92)}px;border-radius:50%;background:conic-gradient(red,yellow,lime,cyan,blue,'
          f'magenta,red)"></div>')
    p.add(f'<div class="swatch" style="left:{cx-d(24)}px;top:{cy-d(24)}px;width:{d(48)}px;'
          f'height:{d(48)}px;border-radius:50%;background:#2b5f8f;border:3px solid #fff"></div>')
    p.tag(cx + r - 10, cy - r - 10, 1)
    p.tag(24 + d(250) + 8, 24 - 6, 2)
    p.note(60, 130, 640, "Nothing is permanently visible. The canvas fills the whole 1920×1080 "
           "panel (no title bar, no taskbar).", None)
    p.note(60, 260, 640, "<b>1.</b> Radial palette summoned at the pen tip (pen side button or "
           "express key): favourite brushes, colour, undo/redo, eraser, flip, new layer. Its "
           f"buttons are {d(38)} px ≈ {mm(d(38)):.1f} mm on the Kamvas 13.", None)
    p.note(60, 438, 640, "<b>2.</b> A small status pill appears briefly after any change "
           "(brush, size, layer) top-left, outside the area a right hand covers, then fades.",
           None)
    p.note(60, 576, 640, "Other access: single-key shortcuts / express keys, Tab to bring back "
           "the last full workspace, F1-style command search.", None)
    return p


# ---------------------------------------------------------------- B  Quick Sketch
def quick_sketch():
    p = Page("B_quick_sketch", "B Quick Sketch")
    top = d(36)
    p.ui(0, 0, W, top, "", "lighter")
    x = 8
    for lab, w in [("☰", 32), ("Pencil ▾", 92), ("Size", 34), ("━━━●━━━ 12", 150), ("Opacity", 56),
                   ("━━━━●━ 80%", 150), ("Smooth", 56), ("━●━━━━ 20%", 150), ("● ", 32),
                   ("Erase", 56), ("↶", 32), ("↷", 32), ("Layers ▾", 80)]:
        p.btn(x, d(3), d(w), d(30), lab, on=(lab == "● "),
              style="background:#2b5f8f" if lab == "● " else "")
        x += d(w) + d(5)
    p.btn(W - d(140) - 8, d(3), d(140), d(30), "Quick Sketch ▾")
    toolstrip(p, 0, top, TOOLS_QUICK, active=0)
    pct = p.canvas(d(44), top, W - d(44), H - top, lab_top="80%")
    # layers popover, shown open
    p.ui(1240, top + 4, d(250), d(220), "", "float")
    for i, lab in enumerate(["Sketch ✓", "Lines", "Background"]):
        p.btn(1240 + d(10), top + 4 + d(12) + i * d(44), d(230), d(38), lab, on=(i == 0),
              style="justify-content:flex-start;padding-left:14px")
    p.btn(1240 + d(10), top + 4 + d(12) + 3 * d(44) + d(8), d(110), d(30), "+ Layer")
    p.tag(1240 + d(250) - 18, top - 4, 2)
    p.tag(d(44) - 6, top + 10, 1)
    p.tag(560, d(36) - 4, 3)
    p.note(380, 260, 700, "<b>1.</b> One slim tool strip (8 tools) on the left, next to the "
           f"Kamvas express keys. Buttons {d(36)} px ≈ {mm(d(36)):.1f} mm; strip {d(44)} px wide.")
    p.note(380, 360, 700, "<b>2.</b> Layers live in a pop-over, not a dock: open it, pick, it "
           "closes. Enough for sketch / lines / background.")
    p.note(380, 460, 700, "<b>3.</b> One top bar merges menu (☰) and the current tool's options: "
           "size, opacity, smoothing, colour, eraser toggle, undo/redo as big pen targets.")
    p.note(380, 590, 700, "Everything else: ☰ menu, shortcuts, command search, or switch "
           "workspace (top right).")
    return p


# ---------------------------------------------------------------- C  Painting
def painting():
    p = Page("C_painting", "C Painting")
    top, opt = d(32), d(32)
    p.ui(0, 0, W, top, "File &nbsp; Edit &nbsp; Image &nbsp; Layer &nbsp; Select &nbsp; View &nbsp; "
         "Window &nbsp;&nbsp;&nbsp;&nbsp;&nbsp; Painting ▾", "lighter",
         "justify-content:flex-start;padding-left:16px")
    p.ui(0, top, W, opt, "Brush: Soft Round ▾ &nbsp;│ Size 40 &nbsp;│ Opacity 100% &nbsp;│ Flow 35% "
         "&nbsp;│ Smoothing 10% &nbsp;│ Pressure → size ✓ opacity ✓", "",
         "justify-content:flex-start;padding-left:16px")
    toolstrip(p, 0, top + opt, TOOLS_QUICK[:6] + ["Fill", "Grad", "Smudge", "Hand"], active=0)
    rw = d(220)
    rx = W - rw
    p.ui(rx, top + opt, rw, H - top - opt, "", "lighter")
    # colour
    cy0 = top + opt + 10
    p.add(f'<div class="swatch" style="left:{rx+20}px;top:{cy0}px;width:{rw-40}px;height:{rw-60}px;'
          f'background:linear-gradient(to top,#000,transparent),linear-gradient(to right,#fff,#d23)">'
          f'</div>')
    p.add(f'<div class="swatch" style="left:{rx+20}px;top:{cy0+rw-50}px;width:{rw-40}px;height:22px;'
          f'background:linear-gradient(to right,red,yellow,lime,cyan,blue,magenta,red)"></div>')
    for i, c in enumerate(["#2b5f8f", "#d9a441", "#7a3b2e", "#e8d7c1", "#3e7d4f", "#222", "#c45"]):
        p.add(f'<div class="swatch" style="left:{rx+20+i*d(28)}px;top:{cy0+rw-16}px;width:{d(24)}px;'
              f'height:{d(24)}px;background:{c}"></div>')
    # brush presets
    by0 = cy0 + rw + 40
    p.ui(rx, by0 - 6, rw, 30, "Brush presets", "", "font-size:16px;background:#26292d")
    for i in range(12):
        p.btn(rx + 14 + (i % 4) * d(50), by0 + 32 + (i // 4) * d(50), d(44), d(44), "~")
    # layers
    ly0 = by0 + 32 + 3 * d(50) + 18
    p.ui(rx, ly0 - 6, rw, 30, "Layers", "", "font-size:16px;background:#26292d")
    for i, lab in enumerate(["Highlights", "Colour", "Lines", "Sketch (hidden)", "Paper"]):
        p.btn(rx + 10, ly0 + 32 + i * d(30), rw - 20, d(26), lab, on=(i == 1),
              style="justify-content:flex-start;padding-left:12px")
    pct = p.canvas(d(44), top + opt, W - d(44) - rw, H - top - opt, lab_top="82%")
    collapsed = 100 * (W - d(44) - d(36)) * (H - top - opt) / (W * H)
    p.tag(rx - 16, cy0, 1)
    p.tag(rx - 16, by0, 2)
    p.tag(rx - 16, ly0, 3)
    p.note(260, 230, 760, f"Right column {rw} px ({d(220)//SCALE:.0f} DIP): <b>1.</b> colour field + "
           "hue strip + recent colours, <b>2.</b> brush preset thumbnails, <b>3.</b> compact layers. "
           "Each section collapses; the whole column collapses to a 54 px icon strip.")
    p.note(260, 400, 760, f"With the column collapsed the canvas grows to ≈{collapsed:.0f}% of the "
           "screen; sections then open as fly-outs and close after use.")
    p.note(260, 520, 760, "Options bar keeps Photoshop's place and order (size, opacity, flow, "
           "smoothing, pressure toggles).")
    return p


# ---------------------------------------------------------------- D  Editing
def editing():
    p = Page("D_editing", "D Editing")
    top, opt, status = d(32), d(32), d(24)
    p.ui(0, 0, W, top, "File &nbsp; Edit &nbsp; Image &nbsp; Layer &nbsp; Select &nbsp; Filter "
         "&nbsp; View &nbsp; Window &nbsp; Help &nbsp;&nbsp;&nbsp;&nbsp;&nbsp; Editing ▾", "lighter",
         "justify-content:flex-start;padding-left:16px")
    p.ui(0, top, W, opt, "Rectangular Marquee: &nbsp;□ ⊕ ⊖ ⊗ &nbsp;│ Feather 0 px &nbsp;│ "
         "Anti-alias ✓ &nbsp;│ Style: Normal &nbsp;│ Select and Mask…", "",
         "justify-content:flex-start;padding-left:16px")
    toolstrip(p, 0, top + opt, TOOLS_FULL, active=1)
    rw = d(260)
    rx = W - rw
    p.ui(rx, top + opt, rw, H - top - opt - status, "", "lighter")
    y = top + opt + 6
    for title, rows in [("Properties / Adjustments", 3), ("Layers", 8), ("History", 3)]:
        p.ui(rx, y, rw, 30, title, "", "font-size:16px;background:#26292d")
        y += 34
        for i in range(rows):
            p.btn(rx + 10, y + i * d(26), rw - 20, d(22), "", on=(title == "Layers" and i == 2))
        y += rows * d(26) + 14
    p.ui(0, H - status, W, status, "Doc 2048×2048 · 100% · 6 layers · 8-bit RGB", "",
         "justify-content:flex-start;padding-left:16px;font-size:16px")
    pct = p.canvas(d(44), top + opt, W - d(44) - rw, H - top - opt - status, lab_top="88%")
    # selection + contextual bar on canvas
    p.add('<div style="position:absolute;left:520px;top:330px;width:520px;height:340px;'
          'border:3px dashed #222"></div>')
    p.ui(560, 690, d(300), d(30), "Fill · Stroke · Transform · Deselect · Invert", "float",
         "font-size:17px")
    p.tag(560 + d(300) - 14, 676, 1)
    p.tag(d(44) - 6, top + opt + 10, 2)
    p.note(120, 140, 360, "<b>1.</b> A small contextual bar under the active selection offers the "
           "next likely actions (cf. Photoshop's Contextual Task Bar, Krita 5.3's Selection Action "
           "Bar, CSP's Selection Launcher). Can be turned off.")
    p.note(1140, 140, 360, "<b>2.</b> Full Photoshop-order toolbar with grouped tools; Photoshop "
           "single-key shortcuts.")
    p.note(1140, 360, 360, f"On a 27″ 2560×1440 monitor at 100% the same layout leaves ≈83% for "
           f"the canvas; on the Kamvas 13 at 150% it leaves ≈{pct:.0f}%.")
    return p


# ---------------------------------------------------------------- E  Pixel Art
def pixel_art():
    p = Page("E_pixel_art", "E Pixel Art")
    top, ctx, tl = d(32), d(32), d(26)
    p.ui(0, 0, W, top, "File &nbsp; Edit &nbsp; Sprite &nbsp; Layer &nbsp; Frame &nbsp; Select "
         "&nbsp; View &nbsp;&nbsp;&nbsp;&nbsp;&nbsp; Pixel Art ▾", "lighter",
         "justify-content:flex-start;padding-left:16px")
    p.ui(0, top, W, ctx, "Pencil: &nbsp;■ 1 px &nbsp;│ Pixel-perfect ✓ &nbsp;│ Ink: Simple ▾ "
         "&nbsp;│ Symmetry ⇆ &nbsp;│ Grid ✓ &nbsp;│ Tiled ✗ &nbsp;│ Zoom 800% (integer)", "",
         "justify-content:flex-start;padding-left:16px")
    toolstrip(p, 0, top + ctx, ["Pencil", "Erase", "Pick", "Fill", "Line", "Rect", "Marq", "Move",
                                "Hand"], active=0)
    pw = d(132)
    px = W - pw
    p.ui(px, top + ctx, pw, H - top - ctx - tl, "", "lighter")
    p.add(f'<div class="swatch" style="left:{px+14}px;top:{top+ctx+14}px;width:{d(36)}px;'
          f'height:{d(36)}px;background:#2b5f8f;border:3px solid #fff"></div>')
    p.add(f'<div class="swatch" style="left:{px+14+d(22)}px;top:{top+ctx+14+d(22)}px;width:{d(36)}px;'
          f'height:{d(36)}px;background:#f2d16b"></div>')
    pal = ["#1a1c2c", "#5d275d", "#b13e53", "#ef7d57", "#ffcd75", "#a7f070", "#38b764", "#257179",
           "#29366f", "#3b5dc9", "#41a6f6", "#73eff7", "#f4f4f4", "#94b0c2", "#566c86", "#333c57"]
    sw = d(26)
    for i, c in enumerate(pal):
        p.add(f'<div class="swatch" style="left:{px+12+(i%4)*(sw+6)}px;'
              f'top:{top+ctx+d(80)+(i//4)*(sw+6)}px;width:{sw}px;height:{sw}px;background:{c}"></div>')
    p.ui(0, H - tl, W, tl, "Timeline (collapsed) · Frame 1/4 · Onion skin off &nbsp;▲", "",
         "justify-content:flex-start;padding-left:16px;font-size:16px")
    cw, ch = W - d(44) - pw, H - top - ctx - tl
    pct = p.canvas(d(44), top + ctx, cw, ch, lab_top="88%", lab_left="80%")
    # sprite area with pixel grid at 800%
    p.add(f'<div class="pixgrid" style="left:{d(44)+cw//2-384}px;top:{top+ctx+ch//2-384}px;'
          f'width:768px;height:768px;background-color:rgba(255,255,255,.75)"></div>')
    p.ui(px - d(150) - 16, top + ctx + 16, d(150), d(150), "Preview<br>1:1", "float")
    p.tag(px - 16, top + ctx + d(80), 1)
    p.tag(px - d(150) - 30, top + ctx + 8, 2)
    p.tag(30, H - tl - 30, 3)
    p.note(120, 150, 420, "<b>1.</b> Palette column (16-colour example, "
           f"swatches {sw} px ≈ {mm(sw):.1f} mm). Left/right click = foreground/background, "
           "X swaps (Aseprite).")
    p.note(120, 420, 420, "<b>2.</b> Floating 1:1 preview (Aseprite F7). <b>3.</b> Timeline "
           "collapsed to a strip; expands only when the sprite has frames.")
    p.note(120, 690, 420, "Context bar shows only the active tool's options; zoom snaps to whole "
           "multiples (100, 200, 400, 800%) so pixels stay square.")
    return p


def main():
    pages = [pure_sketch(), quick_sketch(), painting(), editing(), pixel_art()]
    for p in pages:
        with open(os.path.join(OUT, p.name + ".html"), "w") as f:
            f.write(p.html())
    print("\n".join(p.name for p in pages))


if __name__ == "__main__":
    main()
