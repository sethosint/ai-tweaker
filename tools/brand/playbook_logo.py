"""AI Tweaker mark as the AME Wizard playbook logo (256x256 PNG), drawn like brandMark() in wwwroot/js/icons.js."""
from pathlib import Path
from PIL import Image, ImageDraw

S, K = 256, 4                       # output size, supersampling
OUT = Path(__file__).resolve().parents[2] / "src/AITweaker.App/Assets/playbook.png"


def u(v):                           # 28-unit icon grid -> supersampled pixels
    return round(v * S * K / 28)


img = Image.new("RGBA", (S * K, S * K), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, S * K - 1, S * K - 1], radius=u(8), fill=(22, 22, 24, 255))
for y in (10.5, 17.5):              # slider tracks
    d.line([(u(7.5), u(y)), (u(20.5), u(y))], fill=(242, 242, 242, 115), width=u(2))
    for x in (7.5, 20.5):           # round caps
        d.ellipse([u(x - 1), u(y - 1), u(x + 1), u(y + 1)], fill=(242, 242, 242, 115))
for x, y in ((17, 10.5), (11, 17.5)):  # knobs
    d.ellipse([u(x - 2.6), u(y - 2.6), u(x + 2.6), u(y + 2.6)], fill=(242, 242, 242, 255))
img.resize((S, S), Image.LANCZOS).save(OUT, optimize=True)
print(OUT)
