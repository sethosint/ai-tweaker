"""AI Tweaker mark (corner brackets around a gauge needle turned to the top), drawn like brandMark() in
wwwroot/js/icons.js: the AME playbook logo (playbook.png), the app icon (app.png, app.ico)."""
from pathlib import Path
from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parents[2] / "src/AITweaker.App/Assets"
INK, BG, SIGNAL = (236, 233, 225, 255), (17, 19, 22, 255), (245, 165, 36, 255)


def mark(size: int, k: int = 4) -> Image.Image:
    """The mark on a dark rounded tile, supersampled k times."""
    S = size * k
    u = lambda v: v * S / 32  # 32-unit grid of the SVG
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=u(7), fill=BG)
    # brackets live on a 24-unit square inset 4 units, so they breathe inside the tile
    inset, span, arm, w = 5.2, 21.6, 5.6, 2.3
    lo, hi = inset, inset + span
    for (x, y, dx, dy) in [(lo, lo, 1, 1), (hi, lo, -1, 1), (hi, hi, -1, -1), (lo, hi, 1, -1)]:
        d.line([(u(x), u(y + dy * arm)), (u(x), u(y)), (u(x + dx * arm), u(y))], fill=INK, width=round(u(w)), joint="curve")
    # needle from lower-left to the signal dot at upper-right
    x0, y0, x1, y1 = 11.6, 20.4, 19.2, 12.8
    d.line([(u(x0), u(y0)), (u(x1), u(y1))], fill=INK, width=round(u(2.6)))
    r = 1.3
    d.ellipse([u(x0 - r), u(y0 - r), u(x0 + r), u(y0 + r)], fill=INK)
    R = 2.9
    d.ellipse([u(20.6 - R), u(11.4 - R), u(20.6 + R), u(11.4 + R)], fill=SIGNAL)
    return img.resize((size, size), Image.LANCZOS)


mark(256).save(ASSETS / "playbook.png", optimize=True)
mark(256).save(ASSETS / "app.png", optimize=True)
big = mark(256)
big.save(ASSETS / "app.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("written to", ASSETS)
