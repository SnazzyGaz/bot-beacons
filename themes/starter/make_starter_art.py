#!/usr/bin/env python3
"""Draws the Bot Beacons starter pack: one shared base plus accent layers for an orb, a robot and a cat.

Every layer is a 150x190 transparent PNG on the same canvas, so they stack with no offsets.
Run:  python3 make_starter_art.py [out_dir]     (needs Pillow; default out_dir = ../../art)
"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

W, H, S = 150, 190, 4                      # canvas and supersampling factor
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "art")
CX, CY, R = 75, 108, 50                    # body centre and radius
EYE = (75, 101)                            # glow anchor 1: the face (both eyes)
LAMP = (75, 141)                           # glow anchor 2: the chest light
ANTENNA = (75, 27)                         # robot antenna tip (anim "antenna" point)

def canvas(): return Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
def s(*v): return [x * S for x in v]
def done(im, name, blur=0):
    im = im.resize((W, H), Image.LANCZOS)
    if blur: im = im.filter(ImageFilter.GaussianBlur(blur))
    im.save(os.path.join(OUT, name)); return im

def radial(size, inner, outer, centre=(0.5, 0.5), power=1.0):
    """RGBA radial gradient image (size x size) from inner colour at centre to outer colour at the rim"""
    im = Image.new("RGBA", (size, size)); px = im.load()
    cx, cy = centre[0] * size, centre[1] * size
    for y in range(size):
        for x in range(size):
            t = min(1.0, math.hypot(x - cx, y - cy) / (size * 0.75)) ** power
            px[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(4))
    return im

def base():
    im = canvas(); d = ImageDraw.Draw(im)
    # pearl body: radial shading, light from the top left, thin cool rim
    size = 2 * R * S
    body = radial(size // 2, (255, 255, 255, 255), (196, 204, 218, 255), centre=(0.36, 0.30), power=1.25).resize((size, size), Image.BICUBIC)
    mask = Image.new("L", (size, size), 0); ImageDraw.Draw(mask).ellipse([0, 0, size - 1, size - 1], fill=255)
    im.paste(body, ((CX - R) * S, (CY - R) * S), mask)
    d.ellipse(s(CX - R, CY - R, CX + R, CY + R), outline=(150, 160, 178, 255), width=2 * S)
    # glossy highlight
    hl = Image.new("RGBA", im.size, (0, 0, 0, 0)); ImageDraw.Draw(hl).ellipse(s(CX - 34, CY - 44, CX - 6, CY - 28), fill=(255, 255, 255, 150))
    im.alpha_composite(hl.filter(ImageFilter.GaussianBlur(3 * S)))
    # dark face visor
    d.rounded_rectangle(s(EYE[0] - 34, EYE[1] - 21, EYE[0] + 34, EYE[1] + 21), radius=19 * S, fill=(26, 29, 36, 255), outline=(70, 78, 92, 255), width=S)
    d.rounded_rectangle(s(EYE[0] - 28, EYE[1] - 18, EYE[0] + 10, EYE[1] - 14), radius=2 * S, fill=(60, 66, 80, 255))   # visor sheen
    # two neutral (white) eyes and a small smile: these pixels take the beacon's tint colour
    for ex in (EYE[0] - 13, EYE[0] + 13):
        d.rounded_rectangle(s(ex - 5, EYE[1] - 8, ex + 5, EYE[1] + 7), radius=5 * S, fill=(240, 243, 247, 255))
    d.arc(s(EYE[0] - 5, EYE[1] + 4, EYE[0] + 5, EYE[1] + 12), 20, 160, fill=(235, 238, 242, 255), width=2 * S)
    # chest light in a dark bezel
    d.ellipse(s(LAMP[0] - 8, LAMP[1] - 8, LAMP[0] + 8, LAMP[1] + 8), fill=(40, 44, 54, 255))
    d.ellipse(s(LAMP[0] - 5, LAMP[1] - 5, LAMP[0] + 5, LAMP[1] + 5), fill=(238, 241, 246, 255))
    return done(im, "beacon-base.png")

# ---------------------------------------------------------------- orb accents
def orb():
    # soft white aura around the body (outside it only, so the body is not washed out)
    im = canvas(); d = ImageDraw.Draw(im)
    for i in range(14):
        a = int(70 * (1 - i / 14) ** 1.6)
        d.ellipse(s(CX - R - i, CY - R - i, CX + R + i, CY + R + i), outline=(225, 245, 255, a), width=S)
    done(im, "orb-aura.png", blur=1.2)
    # two small sparkles that orbit
    im = canvas(); d = ImageDraw.Draw(im)
    for (x, y, r) in ((122, 58, 8), (30, 72, 5)):
        w = r * 0.28
        d.polygon([(x * S, (y - r) * S), ((x + w) * S, (y - w) * S), ((x + r) * S, y * S), ((x + w) * S, (y + w) * S),
                   (x * S, (y + r) * S), ((x - w) * S, (y + w) * S), ((x - r) * S, y * S), ((x - w) * S, (y - w) * S)], fill=(255, 255, 255, 240))
    done(im, "orb-sparkles.png")
    # a tiny floating crest: three dots above the head
    im = canvas(); d = ImageDraw.Draw(im)
    for i, (x, y, r) in enumerate(((63, 50, 3), (75, 46, 4), (87, 50, 3))):
        d.ellipse(s(x - r, y - r, x + r, y + r), fill=(235, 248, 255, 255), outline=(170, 190, 210, 255), width=S)
    done(im, "orb-crest.png")

# ---------------------------------------------------------------- robot accents
def robot():
    im = canvas(); d = ImageDraw.Draw(im)
    d.line(s(75, 60, 75, 33), fill=(120, 130, 148, 255), width=3 * S)
    d.ellipse(s(68, 52, 82, 62), fill=(150, 160, 178, 255), outline=(95, 104, 120, 255), width=S)     # collar
    d.ellipse(s(ANTENNA[0] - 6, ANTENNA[1] - 6, ANTENNA[0] + 6, ANTENNA[1] + 6), fill=(255, 196, 92, 255), outline=(170, 110, 30, 255), width=S)
    done(im, "robot-antenna.png")
    im = canvas(); d = ImageDraw.Draw(im)
    for x in (CX - R - 4, CX + R + 4):
        d.rounded_rectangle(s(x - 7, 96, x + 7, 124), radius=4 * S, fill=(128, 138, 156, 255), outline=(84, 92, 108, 255), width=S)
        d.ellipse(s(x - 3, 106, x + 3, 112), fill=(220, 226, 236, 255))
    done(im, "robot-ears.png")
    im = canvas(); d = ImageDraw.Draw(im)
    for x in (52, 98):                        # rivets and a panel seam
        d.ellipse(s(x - 2, 128, x + 2, 132), fill=(140, 150, 166, 255))
    d.arc(s(CX - 44, CY - 46, CX + 44, CY + 42), 200, 340, fill=(170, 178, 194, 255), width=S)
    done(im, "robot-panel.png")

# ---------------------------------------------------------------- cat accents
def cat():
    im = canvas(); d = ImageDraw.Draw(im)
    for side in (-1, 1):
        bx = CX + side * 30
        outer = [(bx - 17 * side, 72), (bx + 12 * side, 62), (bx + 6 * side, 36)]
        inner = [(bx - 9 * side, 68), (bx + 7 * side, 63), (bx + 4 * side, 46)]
        d.polygon([(x * S, y * S) for x, y in outer], fill=(246, 247, 250, 255), outline=(150, 160, 178, 255))
        d.polygon([(x * S, y * S) for x, y in inner], fill=(255, 176, 196, 255))
    done(im, "cat-ears.png")
    im = canvas(); d = ImageDraw.Draw(im)
    for side in (-1, 1):
        for k, dy in enumerate((-4, 1, 6)):
            x0 = CX + side * 37; d.line(s(x0, EYE[1] + 8 + dy, x0 + side * 18, EYE[1] + 4 + dy * 1.7), fill=(120, 128, 144, 255), width=S + S // 2)
        d.ellipse(s(CX + side * 24 - 5, EYE[1] + 22, CX + side * 24 + 5, EYE[1] + 28), fill=(255, 190, 205, 160))   # cheeks
    done(im, "cat-whiskers.png")
    im = canvas(); d = ImageDraw.Draw(im)
    pts = [(118, 150), (132, 146), (140, 134), (138, 120), (130, 116)]
    d.line([(x * S, y * S) for x, y in pts], fill=(150, 160, 178, 255), width=11 * S, joint="curve")
    d.line([(x * S, y * S) for x, y in pts], fill=(246, 247, 250, 255), width=8 * S, joint="curve")
    d.ellipse(s(124, 111, 136, 123), fill=(255, 176, 196, 255))
    done(im, "cat-tail.png")

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    b = base(); orb(); robot(); cat()
    # flattened orb: built-in fallback picture (beacon.png) and the exe icon
    flat = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for n in ("orb-aura.png", "beacon-base.png", "orb-crest.png", "orb-sparkles.png"):
        flat.alpha_composite(Image.open(os.path.join(OUT, n)).convert("RGBA"))
    flat.save(os.path.join(OUT, "beacon.png"))
    ic = flat.crop((CX - R - 14, CY - R - 14 - 10, CX + R + 14, CY + R + 14 - 10))
    ic.save(os.path.join(OUT, "icon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128)])
    print("Wrote the starter art to", os.path.abspath(OUT))
