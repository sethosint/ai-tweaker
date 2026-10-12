#!/usr/bin/env python3
"""AI Tweaker's minimal dark wallpapers: no text, no logos, no graphics — just light and material.

Each image is a near-black field lit by one or two very soft light sources, with fine monochrome grain so the
gradients never band (on 8-bit panels and after JPEG). Rendered at 4K; Windows "Fill" crops them for any aspect.

    python3 tools/wallpapers/make.py   # writes src/AITweaker.App/Assets/Wallpapers/*.jpg and wwwroot/img/*.webp previews
"""
import os

import numpy as np
from PIL import Image

W, H = 3840, 2160
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_JPG = os.path.join(ROOT, "src", "AITweaker.App", "Assets", "Wallpapers")
OUT_WEBP = os.path.join(ROOT, "src", "AITweaker.App", "wwwroot", "img")

# coordinate grid in "height units": y in 0..1, x in 0..W/H
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
yy /= H
xx /= H


def glow(cx, cy, radius, power=1.0):
    """Soft light falloff (smooth, no hard edge), 1 at the centre."""
    d = np.sqrt((xx - cx * W / H) ** 2 + (yy - cy) ** 2) / radius
    return np.clip(1.0 - d, 0, 1) ** (2.2 * power)


def sheen(angle_deg, offset, width):
    """A very wide diagonal band of light, like light across dark glass."""
    a = np.deg2rad(angle_deg)
    t = (xx * np.cos(a) + yy * np.sin(a)) - offset
    return np.exp(-(t / width) ** 2)


def finish(rgb, seed, grain=0.9):
    """Adds fine monochrome grain (in 8-bit levels) and clips."""
    rng = np.random.default_rng(seed)
    noise = rng.normal(0, grain, (H, W, 1)).astype(np.float32)
    return Image.fromarray(np.clip(rgb + noise, 0, 255).astype(np.uint8), "RGB")


def tint(base, light, amount):
    base = np.array(base, np.float32)
    light = np.array(light, np.float32)
    return base + (light - base) * amount[..., None]


def graphite():
    # charcoal with a soft cool light from the upper left
    a = 0.55 * glow(0.18, 0.08, 1.35) + 0.18 * glow(0.95, 1.05, 1.1)
    return finish(tint((9, 9, 10), (44, 45, 49), a), 1)


def obsidian():
    # near-black, one faint diagonal sheen from the top right
    a = 0.30 * sheen(-32, 0.95, 0.55) + 0.10 * glow(0.85, 0.0, 1.0)
    return finish(tint((4, 4, 5), (40, 41, 45), a), 2, grain=0.8)


def lock():
    # darker edges, a little light behind the clock area
    a = 0.42 * glow(0.5, 0.18, 1.0) + 0.08 * glow(0.5, 1.2, 1.2)
    return finish(tint((6, 6, 7), (38, 39, 43), a), 3)


if __name__ == "__main__":
    os.makedirs(OUT_JPG, exist_ok=True)
    os.makedirs(OUT_WEBP, exist_ok=True)
    for name, make in [("graphite", graphite), ("obsidian", obsidian), ("lock", lock)]:
        im = make()
        im.save(os.path.join(OUT_JPG, f"aitweaker-{name}.jpg"), quality=92, optimize=True, progressive=True, subsampling=0)
        im.resize((960, 540), Image.LANCZOS).save(os.path.join(OUT_WEBP, f"aitweaker-{name}.webp"), quality=86)
        print("wrote", name)
