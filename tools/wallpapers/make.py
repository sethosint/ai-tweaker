#!/usr/bin/env python3
"""AI Tweaker's own wallpapers, drawn from scratch as SVG (no third-party artwork).

Story: a frame-time trace that is jagged on the left (stock Windows: stutter) and becomes a calm flat line
after the AI Tweaker marker. Instrument-panel grid and ticks match the OPS terminal look of the app.

    python3 tools/wallpapers/make.py            # writes SVGs next to this script
    NODE_PATH=$(npm root -g) node tools/wallpapers/render.js   # rasterizes them to JPG/WebP
"""
import math
import os
import random

W, H = 3840, 2160
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "svg")

THEMES = {
    "dark": dict(bg0="#181b20", bg1="#0a0b0d", grid="236,230,211", minor=0.030, major=0.055, ink="#ece6d3", muted="#8a8577",
                 stutter="#ff5a4e", stutter_op=0.42, tuned="#f2a93b", glow=0.55, fill=0.10, hazard_b="#14160f"),
    "light": dict(bg0="#f4f1e8", bg1="#dcd6c7", grid="29,29,27", minor=0.045, major=0.080, ink="#1d1d1b", muted="#77715f",
                  stutter="#c62828", stutter_op=0.38, tuned="#e8590c", glow=0.25, fill=0.10, hazard_b="#1d1d1b"),
}


def trace(seed, x0, x1, marker, base):
    """Frame-time polyline: noisy with spikes before the marker, settling to a flat line after it."""
    rnd = random.Random(seed)
    pts, x = [], x0
    while x <= x1:
        if x < marker:
            amp = 46 + 30 * math.sin(x / 310.0)
            y = base - abs(rnd.gauss(0, 1)) * amp
            if rnd.random() < 0.08:
                y -= rnd.uniform(120, 330)  # hitch
        else:
            d = x - marker
            amp = 60 * math.exp(-d / 70.0) + 3.0
            y = base - 6 - abs(math.sin(x / 23.0)) * amp
        pts.append((x, y))
        x += 12 if x < marker else 16
    return pts


def path(pts, step=True):
    out = [f"M{pts[0][0]:.0f},{pts[0][1]:.1f}"]
    for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
        out.append(f"H{xb:.0f}V{yb:.1f}" if step else f"L{xb:.0f},{yb:.1f}")
    return "".join(out)


def svg(theme, lock=False):
    c = THEMES[theme]
    marker = 1720
    base = 1560 if not lock else 1640
    pts = trace(7, -20, W + 40, marker, base)
    before = [p for p in pts if p[0] <= marker]
    after = [p for p in pts if p[0] >= marker - 12]
    tuned_d = path(after, step=False)
    fill_d = tuned_d + f"L{W + 40},{H}L{after[0][0]:.0f},{H}Z"
    grid = c["grid"]

    ticks = []
    for x in range(0, W + 1, 48):
        major = x % 240 == 0
        ticks.append(f'<line x1="{x}" y1="0" x2="{x}" y2="{26 if major else 12}"/>')
        if major and not lock and 0 < x < W:
            ticks.append(f'<text x="{x + 6}" y="44" class="tk">{x // 48:03d}</text>')
    for y in range(0, H + 1, 48):
        major = y % 240 == 0
        ticks.append(f'<line x1="{W}" y1="{y}" x2="{W - (26 if major else 12)}" y2="{y}"/>')

    top = 980 if lock else 520
    hazard = "".join(f'<polygon points="{x},0 {x + 18},0 {x + 6},18 {x - 12},18"/>' for x in range(0, 380, 36))
    sub = "WINDOWS 11 · LOW-LATENCY BUILD"
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">
<defs>
  <radialGradient id="bg" cx="47%" cy="58%" r="78%"><stop offset="0" stop-color="{c["bg0"]}"/><stop offset="1" stop-color="{c["bg1"]}"/></radialGradient>
  <radialGradient id="fade" cx="45%" cy="55%" r="70%"><stop offset="0" stop-color="#fff" stop-opacity="1"/><stop offset=".75" stop-color="#fff" stop-opacity=".55"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
  <mask id="m"><rect width="{W}" height="{H}" fill="url(#fade)"/></mask>
  <pattern id="minor" width="48" height="48" patternUnits="userSpaceOnUse"><path d="M48 0H0V48" fill="none" stroke="rgba({grid},{c["minor"]})" stroke-width="1.5"/></pattern>
  <pattern id="major" width="240" height="240" patternUnits="userSpaceOnUse"><path d="M240 0H0V240" fill="none" stroke="rgba({grid},{c["major"]})" stroke-width="2"/></pattern>
  <linearGradient id="under" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{c["tuned"]}" stop-opacity="{c["fill"]}"/><stop offset=".55" stop-color="{c["tuned"]}" stop-opacity="{c["fill"] * .25}"/><stop offset="1" stop-color="{c["tuned"]}" stop-opacity="0"/></linearGradient>
  <linearGradient id="fadeIn" x1="{marker}" y1="0" x2="{W}" y2="0" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset=".18" stop-color="#fff" stop-opacity="1"/><stop offset=".85" stop-color="#fff" stop-opacity="1"/><stop offset="1" stop-color="#fff" stop-opacity=".4"/></linearGradient>
  <mask id="underMask"><rect width="{W}" height="{H}" fill="url(#fadeIn)"/></mask>
  <linearGradient id="stut" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="{c["stutter"]}" stop-opacity="{c["stutter_op"] * .35}"/><stop offset="1" stop-color="{c["stutter"]}" stop-opacity="{c["stutter_op"]}"/></linearGradient>
  <filter id="glow" x="-5%" y="-50%" width="110%" height="200%"><feGaussianBlur stdDeviation="14"/></filter>
</defs>
<rect width="{W}" height="{H}" fill="url(#bg)"/>
<g mask="url(#m)"><rect width="{W}" height="{H}" fill="url(#minor)"/><rect width="{W}" height="{H}" fill="url(#major)"/></g>
<g stroke="rgba({grid},.22)" stroke-width="2">{"".join(ticks)}</g>
<path d="{fill_d}" fill="url(#under)" mask="url(#underMask)"/>
<path d="{path(before)}" fill="none" stroke="url(#stut)" stroke-width="4" stroke-linejoin="miter"/>
<path d="{tuned_d}" fill="none" stroke="{c["tuned"]}" stroke-width="10" opacity="{c["glow"]}" filter="url(#glow)"/>
<path d="{tuned_d}" fill="none" stroke="{c["tuned"]}" stroke-width="5" stroke-linecap="round"/>
<line x1="{marker}" y1="{top}" x2="{marker}" y2="{H - 260}" stroke="{c["tuned"]}" stroke-width="3" stroke-dasharray="4 14" opacity=".75"/>
<g transform="translate({marker},{top - 96})">
  <circle r="58" fill="none" stroke="{c["ink"]}" stroke-width="5"/>
  <path d="M0 0L0 -48A48 48 0 0 1 36.8 -30.9Z" fill="{c["tuned"]}"/>
  <path d="M0 0L0 -48A48 48 0 0 0 -24 -41.6Z" fill="{c["tuned"]}" opacity=".38"/>
  <path d="M0 0L-24 -41.6A48 48 0 0 0 -41.6 -24Z" fill="{c["tuned"]}" opacity=".16"/>
  <circle r="7" fill="{c["ink"]}"/>
</g>
<text x="{marker + 28}" y="{top + 40}" class="lbl" fill="{c["tuned"]}">AI TWEAKER</text>
<text x="{marker - 28}" y="{top + 40}" class="lbl" fill="{c["muted"]}" text-anchor="end">STOCK</text>
<g transform="translate({W - 140},{H - 300})" text-anchor="end">
  <text class="word" fill="{c["ink"]}">AI TWEAKER</text>
  <text y="56" class="sub" fill="{c["muted"]}">{sub}</text>
  <g transform="translate(-380,92)" fill="{c["tuned"]}"><rect width="380" height="18" fill="{c["hazard_b"]}"/>{hazard}</g>
</g>
<style>
  .tk {{ font: 500 18px "DejaVu Sans Mono", monospace; fill: rgba({grid},.35); }}
  .lbl {{ font: 600 30px "DejaVu Sans Mono", monospace; letter-spacing: 6px; }}
  .word {{ font: 800 88px "Inter Display", Inter, sans-serif; letter-spacing: 14px; }}
  .sub {{ font: 500 26px "DejaVu Sans Mono", monospace; letter-spacing: 5px; }}
</style>
</svg>'''


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name, theme, lock in [("aitweaker-dark", "dark", False), ("aitweaker-light", "light", False), ("aitweaker-lock", "dark", True)]:
        with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as f:
            f.write(svg(theme, lock))
        print("wrote", name)
