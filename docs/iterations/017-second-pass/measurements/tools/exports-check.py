# Independent check of DoodleNook's exports with Pillow (not DoodleNook's own decoders).
import json, os, sys
from PIL import Image, ImageChops
d = sys.argv[1]
refs, meta = {}, {}
for line in open(os.path.join(d, "manifest.txt")):
    p = line.split()
    if p[0] == "ref": refs[p[1]] = (int(p[2]), int(p[3]))
    else: meta[p[0]] = p[1]
def ref(name, mode="RGBA"):
    w, h = refs[name]
    return Image.frombytes("RGBA", (w, h), open(os.path.join(d, name + ".rgba"), "rb").read()).convert(mode)
def diff(a, b):
    a, b = a.tobytes(), b.tobytes()
    if len(a) != len(b): return None
    errs = [abs(x - y) for x, y in zip(a, b)]
    return sum(1 for e in errs if e) , max(errs), sum(errs) / len(errs)
problems = []
def report(what, ok, detail):
    print(f"{'OK  ' if ok else 'FAIL'} {what}: {detail}")
    if not ok: problems.append(what)

im = Image.open(os.path.join(d, "design.png")); report("PNG still", im.format == "PNG" and diff(im.convert("RGBA"), ref("design"))[0] == 0, f"{im.format} {im.mode} {im.size}, differing bytes {diff(im.convert('RGBA'), ref('design'))[0]}")
im = Image.open(os.path.join(d, "design.jpeg")); n, mx, mean = diff(im.convert("RGB"), ref("design-matte", "RGB"))
report("JPEG still", im.format == "JPEG" and im.mode == "RGB" and mean < 2.5, f"{im.format} {im.mode} {im.size}, mean error {mean:.2f}/255, largest {mx} (lossy, quality 92)")
im = Image.open(os.path.join(d, "design.webp")); n, mx, mean = diff(im.convert("RGBA"), ref("design"))
report("WebP still", im.format == "WEBP" and mean < 2.5, f"{im.format} {im.mode} {im.size}, mean error {mean:.2f}/255, largest {mx}")

seq = meta["sequence"].split(",")
bad = [f for i, f in enumerate(seq) if (lambda m: m.format != "PNG" or diff(m.convert("RGBA"), ref(f"slime{i}"))[0])(Image.open(os.path.join(d, f)))]
report("PNG sequence", not bad, f"{len(seq)} files, transparent, identical pixels in {len(seq) - len(bad)}/{len(seq)}")

js = json.load(open(os.path.join(d, "slime-sheet.json")))
sheet = Image.open(os.path.join(d, js["image"])).convert("RGBA")
durs = [int(x) for x in meta["durations"].split(",")]
ok = (sheet.size == (js["size"]["w"], js["size"]["h"]) and len(js["frames"]) == len(durs) and js["rows"] * js["columns"] >= len(durs))
boxes = []
for i, f in enumerate(js["frames"]):
    box = (f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])
    inside = box[0] >= 0 and box[1] >= 0 and box[2] <= sheet.width and box[3] <= sheet.height
    overlap = any(not (box[2] <= b[0] or b[2] <= box[0] or box[3] <= b[1] or b[3] <= box[1]) for b in boxes)
    boxes.append(box)
    same = inside and diff(sheet.crop(box), ref(f"slime{i}"))[0] == 0
    ok = ok and inside and not overlap and same and f["durationMs"] == durs[i] and f["index"] == i
report("Sprite sheet + JSON", ok, f"JSON parses; {js['columns']}×{js['rows']} grid in {sheet.size}; {len(js['frames'])} frame rectangles inside the image, not overlapping, each crop identical to its frame; durations {[f['durationMs'] for f in js['frames']]}")

def gif(name, refname, durations, loop, exact):
    g = Image.open(os.path.join(d, name))
    frames_ok, worst, got = 0, 0.0, []
    for i in range(g.n_frames):
        g.seek(i)
        got.append(g.info.get("duration"))
        n, mx, mean = diff(g.convert("RGBA"), ref(f"{refname}{i}"))
        worst = max(worst, mean)
        frames_ok += (n == 0) if exact else (mean < 6)
    # GIF counts in 10 ms; DoodleNook rounds each frame's END on the running timeline (no drift), so every frame
    # boundary must be within 5 ms of the asked one.
    import itertools
    drift = max(abs(a - b) for a, b in zip(itertools.accumulate(got), itertools.accumulate(durations)))
    ok = g.n_frames == len(durations) and frames_ok == len(durations) and drift <= 5 and (("loop" in g.info) == loop)
    report(f"GIF {name}", ok, f"{g.n_frames} frames, durations {got} (asked {durations}; GIF counts in 10 ms, frame boundaries within {drift} ms of the asked timeline), loop {'on' if 'loop' in g.info else 'off'}, "
           + (f"identical pixels in {frames_ok}/{g.n_frames} frames" if exact else f"{frames_ok}/{g.n_frames} frames close, worst mean error {worst:.2f}/255 (colour-reduced)"))
gif("slime.gif", "slime-paper", durs, True, True)
gif("ball.gif", "ball", [int(x) for x in meta["balldurations"].split(",")], False, False)
print("ALL EXPORTS READ CORRECTLY BY PILLOW" if not problems else f"PROBLEMS: {problems}")
