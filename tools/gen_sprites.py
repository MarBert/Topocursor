"""
Generates the default pixel-art mouse sprite sheet + a preview GIF.

  python gen_sprites.py [output_folder]

Sheet layout: one row per animation, 48x32 px frames, mouse facing right.
Rows: 0 run (4), 1 idle (6), 2 sleep (4), 3 bite (7), 4 drag (4), 5 hold (4).
Hotspot (tip of the nose) = (37, 18): the point that "clicks".
"""
import math
import os
import sys

from PIL import Image, ImageDraw

FW, FH = 48, 32
HOTSPOT = (37, 18)

# ---- palette ---------------------------------------------------------------
OUT = (58, 42, 46, 255)
FUR = (158, 158, 172, 255)
FUR_LIGHT = (196, 196, 208, 255)
FUR_DARK = (122, 122, 138, 255)
BELLY = (232, 230, 236, 255)
PINK = (242, 160, 176, 255)
PINK_DARK = (214, 120, 140, 255)
NOSE = (232, 96, 130, 255)
EYE = (24, 20, 26, 255)
WHITE = (255, 255, 255, 255)
WHISKER = (90, 80, 90, 170)
TAIL = (214, 132, 148, 255)
CHEESE = (248, 204, 72, 255)
CHEESE_LIGHT = (255, 232, 140, 255)
CHEESE_DARK = (214, 160, 36, 255)
CHEESE_HOLE = (190, 130, 24, 255)
CHEESE_OUT = (120, 78, 20, 255)
Z_COL = (120, 150, 230, 255)


def ell(d, box, col):
    x0, y0, x1, y1 = box
    if x1 > x0 and y1 > y0:
        d.ellipse((x0, y0, x1, y1), fill=col)


def inset(box, n=1):
    x0, y0, x1, y1 = box
    return (x0 + n, y0 + n, x1 - n, y1 - n)


def tail(d, x, y, phase, length=11, curl=0.55, amp=1.6, lie=False):
    pts = []
    for i in range(length):
        px = x - i
        if lie:
            py = y + math.sin(phase + i * 0.5) * 0.6
        else:
            py = y - i * curl + math.sin(phase + i * 0.75) * amp * (i / length)
        pts.append((px, round(py)))
    d.line(pts, fill=TAIL, width=1)


def leg(d, x, top, bottom, near=True):
    col = PINK if near else PINK_DARK
    d.rectangle((x - 1, top, x + 2, bottom), fill=OUT)
    d.rectangle((x, top, x + 1, bottom - 1), fill=col)


def mouse(img, *, ox=0, oy=0, b=0, breathe=0, legs=None, tail_phase=0.0,
          eye="open", head=(0, 0), whisk=0, sleep=False, cheeks=False):
    """Draws the mouse. ox/oy global offset, b body bob, head head offset."""
    d = ImageDraw.Draw(img)
    hx, hy = head
    gy = 27 + oy  # ground row

    # --- tail (behind everything)
    if sleep:
        tail(d, 9 + ox, gy - 1, tail_phase, length=9, lie=True)
    else:
        tail(d, 9 + ox, 22 + oy + b, tail_phase)

    # --- far-side legs
    if legs and not sleep:
        (ff, fb), (nf, nb) = legs
        leg(d, 24 + ox + ff[0], 22 + oy, gy + ff[1], near=False)
        leg(d, 13 + ox + fb[0], 22 + oy, gy + fb[1], near=False)

    # --- ear (behind the head)
    ear = (22 + ox + hx, 7 + oy + b + hy, 29 + ox + hx, 14 + oy + b + hy)
    if sleep:
        ear = (21 + ox, 11 + oy, 28 + ox, 18 + oy)
    ell(d, ear, OUT)
    ell(d, inset(ear), FUR)
    ell(d, inset(ear, 2), PINK)

    # --- body + head as a single outlined shape
    if sleep:
        body = (7 + ox, 16 + oy - breathe, 27 + ox, 27 + oy)
        headb = (21 + ox, 15 + oy, 32 + ox, 26 + oy)
        snout = (28 + ox, 18 + oy, 36 + ox, 25 + oy)
    else:
        body = (8 + ox, 15 + oy + b - breathe, 26 + ox, 26 + oy + b)
        headb = (21 + ox + hx, 12 + oy + b + hy, 32 + ox + hx, 22 + oy + b + hy)
        sy = 1 if cheeks else 0
        snout = (28 + ox + hx, 15 + oy + b + hy, 36 + ox + hx, 21 + oy + b + hy + sy)
    for s in (body, headb, snout):
        ell(d, s, OUT)
    for s in (body, headb, snout):
        ell(d, inset(s), FUR)
    # belly and highlight on the back
    bx0, by0, bx1, by1 = body
    ell(d, (bx0 + 4, by1 - 6, bx1 - 3, by1 - 1), BELLY)
    ell(d, (bx0 + 4, by0 + 2, bx0 + 11, by0 + 4), FUR_LIGHT)
    if cheeks:
        sx0, sy0, sx1, sy1 = snout
        ell(d, (sx0, sy0 + 2, sx0 + 5, sy1 - 1), FUR_LIGHT)

    # --- near-side legs (in front of the body, just the feet)
    if legs and not sleep:
        (ff, fb), (nf, nb) = legs
        for lx, (dx, dy) in ((22 + ox, nf), (11 + ox, nb)):
            d.rectangle((lx + dx - 1, gy - 1 + dy, lx + dx + 2, gy + dy), fill=OUT)
            d.rectangle((lx + dx, gy - 1 + dy, lx + dx + 1, gy - 1 + dy), fill=PINK)
    if sleep:
        # paws tucked under the snout
        d.rectangle((26 + ox, gy - 1, 29 + ox, gy), fill=OUT)
        d.rectangle((27 + ox, gy - 1, 28 + ox, gy - 1), fill=PINK)

    # --- eye
    if sleep:
        ex, ey = 28 + ox, 20 + oy
    else:
        ex, ey = 29 + ox + hx, 15 + oy + b + hy
    if eye == "open":
        d.rectangle((ex, ey, ex + 1, ey + 1), fill=EYE)
        d.point((ex, ey), fill=WHITE)
    elif eye == "closed":
        d.line((ex - 1, ey + 1, ex + 1, ey + 1), fill=EYE)
    elif eye == "happy":  # ^
        d.point((ex - 1, ey + 1), fill=EYE)
        d.point((ex, ey), fill=EYE)
        d.point((ex + 1, ey + 1), fill=EYE)
    elif eye == "wide":
        d.rectangle((ex, ey - 1, ex + 1, ey + 1), fill=EYE)
        d.point((ex, ey - 1), fill=WHITE)

    # --- nose (its tip is the hotspot while the mouse is idle/running)
    sx1 = snout[2]
    ny = (snout[1] + snout[3]) // 2 - 1
    d.rectangle((sx1 - 1, ny, sx1, ny + 1), fill=NOSE)

    # --- whiskers
    if not sleep:
        wx = sx1 - 4
        d.line((wx, ny + 2, wx + 7, ny + 1 - whisk), fill=WHISKER)
        d.line((wx, ny + 3, wx + 7, ny + 4 + whisk), fill=WHISKER)
    return d


# ---- cheese --------------------------------------------------------------
def cheese(img, ox=0, oy=0, bites=0, crumbs=()):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # wedge seen from the side: tall side on the right, sloping to the left
    poly = [(36 + ox, 27 + oy), (47 + ox, 27 + oy), (47 + ox, 15 + oy), (36 + ox, 22 + oy)]
    d.polygon(poly, fill=CHEESE, outline=CHEESE_OUT)
    # lighter top face
    d.line((37 + ox, 22 + oy, 46 + ox, 16 + oy), fill=CHEESE_LIGHT)
    d.line((46 + ox, 17 + oy, 46 + ox, 26 + oy), fill=CHEESE_DARK)
    for hx, hy, r in ((40, 24, 1), (43, 20, 1), (44, 25, 0)):
        d.rectangle((hx + ox, hy + oy, hx + ox + r, hy + oy + r), fill=CHEESE_HOLE)
    # bites: circles "carved out" along the top-left edge
    px = layer.load()
    for i in range(bites):
        cx, cy, r = 37.5 + i * 2.6 + ox, 21.0 - i * 1.6 + oy, 2.3
        for y in range(FH):
            for x in range(FW):
                dd = math.hypot(x - cx, y - cy)
                if dd <= r:
                    px[x, y] = (0, 0, 0, 0)
                elif dd <= r + 1.0 and px[x, y][3] and px[x, y] != CHEESE_OUT:
                    px[x, y] = CHEESE_DARK
    for (cx, cy) in crumbs:
        d.point((cx + ox, cy + oy), fill=CHEESE)
    img.alpha_composite(layer)


def zzz(img, items):
    d = ImageDraw.Draw(img)
    for (x, y, s) in items:
        d.line((x, y, x + s, y), fill=Z_COL)
        d.line((x + s, y, x, y + s), fill=Z_COL)
        d.line((x, y + s, x + s, y + s), fill=Z_COL)


def frame():
    return Image.new("RGBA", (FW, FH), (0, 0, 0, 0))


# ---- animations -----------------------------------------------------------------
def run_frames():
    # legs: ((far_front, far_back), (near_front, near_back)), each (dx, dy)
    cfg = [
        dict(b=0, legs=(((2, 0), (-2, 0)), ((-1, 0), (1, 0))), ph=0.0),
        dict(b=-1, legs=(((0, -1), (0, 0)), ((0, 0), (0, -1))), ph=1.6),
        dict(b=0, legs=(((-1, 0), (1, 0)), ((2, 0), (-2, 0))), ph=3.2),
        dict(b=-1, legs=(((0, 0), (0, -1)), ((0, -1), (0, 0))), ph=4.8),
    ]
    out = []
    for c in cfg:
        f = frame()
        mouse(f, b=c["b"], legs=c["legs"], tail_phase=c["ph"], whisk=1 if c["b"] else 0)
        out.append(f)
    return out


STAND = (((0, 0), (0, 0)), ((0, 0), (0, 0)))


def idle_frames():
    seq = [
        dict(breathe=0, eye="open", whisk=0),
        dict(breathe=1, eye="open", whisk=0),
        dict(breathe=0, eye="open", whisk=1),
        dict(breathe=1, eye="open", whisk=0),
        dict(breathe=0, eye="open", whisk=0),
        dict(breathe=0, eye="closed", whisk=0),
    ]
    out = []
    for i, s in enumerate(seq):
        f = frame()
        mouse(f, legs=STAND, tail_phase=i * 0.5, breathe=s["breathe"], eye=s["eye"], whisk=s["whisk"])
        out.append(f)
    return out


def sleep_frames():
    zs = [
        [(33, 11, 2)],
        [(35, 7, 2), (33, 11, 2)],
        [(37, 2, 3), (35, 7, 2)],
        [(37, 2, 3)],
    ]
    out = []
    for i, z in enumerate(zs):
        f = frame()
        mouse(f, sleep=True, breathe=i % 2, eye="closed", tail_phase=i * 0.8)
        zzz(f, z)
        out.append(f)
    return out


def bite_frames():
    seq = [
        # (head, eye, bites, crumbs, cheeks)
        dict(head=(0, 0), eye="open", bites=0, crumbs=(), cheeks=False),
        dict(head=(-1, -2), eye="wide", bites=0, crumbs=(), cheeks=False),
        dict(head=(2, 1), eye="happy", bites=0, crumbs=(), cheeks=False),
        dict(head=(1, 0), eye="happy", bites=1, crumbs=((39, 17), (41, 15), (37, 16)), cheeks=True),
        dict(head=(0, 0), eye="happy", bites=1, crumbs=((40, 19), (42, 17), (36, 19)), cheeks=False),
        dict(head=(0, -1), eye="happy", bites=1, crumbs=((40, 23), (43, 21), (35, 24)), cheeks=True),
        dict(head=(0, 0), eye="open", bites=1, crumbs=((40, 26), (43, 26), (35, 26)), cheeks=False),
    ]
    out = []
    for s in seq:
        f = frame()
        cheese(f, bites=s["bites"])
        m = frame()
        mouse(m, legs=STAND, head=s["head"], eye=s["eye"], cheeks=s["cheeks"], tail_phase=1.0)
        f.alpha_composite(m)
        if s["crumbs"]:
            d = ImageDraw.Draw(f)
            for c in s["crumbs"]:
                d.point(c, fill=CHEESE_DARK)
        out.append(f)
    return out


def paw(d, x, y):
    """Front paw gripping something (3x2 with outline)."""
    d.rectangle((x - 1, y - 1, x + 2, y + 1), fill=OUT)
    d.rectangle((x, y, x + 1, y), fill=PINK)


def heart(d, x, y):
    red, dark = (240, 80, 110, 255), (170, 40, 70, 255)
    for px, py in ((1, 0), (3, 0), (0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (1, 2), (2, 2), (3, 2), (2, 3)):
        d.point((x + px, y + py), fill=red)
    d.point((x + 1, y), fill=(255, 170, 190, 255))
    d.point((x + 2, y + 3), fill=dark)


def speed_lines(d, phase):
    col = (150, 150, 170, 150)
    for i, (y, ln) in enumerate(((9, 5), (14, 7), (19, 4))):
        x = 1 + ((phase + i * 2) % 4)
        d.line((x, y, x + ln, y), fill=col)


def drag_frames():
    # Dragged: the mouse holds on to the cheese, lifted off the ground,
    # hind legs dangling, tail in the wind, pendulum swing.
    cfg = [
        dict(oy=-2, legs=(((1, 1), (-1, 2)), ((0, 2), (1, 1))), ph=0.0, eye="happy"),
        dict(oy=-3, legs=(((0, 2), (0, 1)), ((1, 1), (-1, 2))), ph=1.6, eye="closed"),
        dict(oy=-2, legs=(((-1, 2), (1, 1)), ((-1, 1), (0, 2))), ph=3.2, eye="happy"),
        dict(oy=-3, legs=(((0, 1), (0, 2)), ((0, 2), (1, 1))), ph=4.8, eye="closed"),
    ]
    out = []
    for i, c in enumerate(cfg):
        f = frame()
        speed_lines(ImageDraw.Draw(f), i)
        cheese(f, oy=-6)
        m = frame()
        mouse(m, ox=-2, oy=c["oy"], legs=c["legs"], tail_phase=c["ph"], eye=c["eye"], head=(1, 1), whisk=i % 2)
        f.alpha_composite(m)
        d = ImageDraw.Draw(f)
        # front paws gripping the side of the cheese
        paw(d, 35, 17 + c["oy"] + 3)
        paw(d, 35, 20 + c["oy"] + 3)
        out.append(f)
    return out


def hold_frames():
    # Still with the button held: hugs the cheese, happily.
    seq = [
        dict(breathe=0, eye="happy", heart=None),
        dict(breathe=1, eye="happy", heart=(31, 6)),
        dict(breathe=0, eye="closed", heart=(32, 4)),
        dict(breathe=1, eye="happy", heart=None),
    ]
    out = []
    for i, s in enumerate(seq):
        f = frame()
        cheese(f)
        m = frame()
        mouse(m, ox=-2, legs=STAND, breathe=s["breathe"], eye=s["eye"], head=(1, 1), tail_phase=i * 0.6)
        f.alpha_composite(m)
        d = ImageDraw.Draw(f)
        paw(d, 35, 22 + (i % 2))
        paw(d, 35, 25)
        if s["heart"]:
            heart(d, *s["heart"])
        out.append(f)
    return out


ANIMS = [("run", run_frames, 12), ("idle", idle_frames, 4), ("sleep", sleep_frames, 2), ("bite", bite_frames, 14),
         ("drag", drag_frames, 12), ("hold", hold_frames, 4)]


def main():
    outdir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(outdir, exist_ok=True)
    rows = [(name, fn(), fps) for name, fn, fps in ANIMS]
    cols = max(len(fr) for _, fr, _ in rows)
    sheet = Image.new("RGBA", (cols * FW, len(rows) * FH), (0, 0, 0, 0))
    for r, (_, frames, _) in enumerate(rows):
        for c, f in enumerate(frames):
            sheet.alpha_composite(f, (c * FW, r * FH))
    sheet.save(os.path.join(outdir, "mouse.png"))

    # preview: enlarged sheet + animated GIF on a light background
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save(os.path.join(outdir, "mouse_preview_sheet.png"))
    gif = []
    for name, frames, fps in rows:
        reps = max(1, round(fps * 1.6 / len(frames)))
        for _ in range(reps):
            for f in frames:
                bg = Image.new("RGBA", (FW, FH), (250, 248, 240, 255))
                bg.alpha_composite(f)
                gif.append((bg.resize((FW * 6, FH * 6), Image.NEAREST).convert("P", palette=Image.ADAPTIVE), int(1000 / fps)))
    gif[0][0].save(os.path.join(outdir, "mouse_preview.gif"), save_all=True,
                   append_images=[g for g, _ in gif[1:]], duration=[t for _, t in gif], loop=0)
    print("ok", sheet.size)


if __name__ == "__main__":
    main()
