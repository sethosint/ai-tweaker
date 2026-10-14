#!/usr/bin/env python3
"""Cuts the guide screenshots into wwwroot/guides: crops each to the settings it shows,
blurs personal details (user names in paths, server and game names, avatars, location)
and writes WebP. Sources are the original screenshots, kept out of the repo.

Usage: python3 tools/guide-shots/build.py <folder with the source screenshots>
Each source is matched by its file-name prefix below (e.g. "005" or "IMG_5330_0").
"""
import glob
import json
import os
import sys

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "src", "AITweaker.App", "wwwroot", "guides")
MAX_W = 1100

# guide id -> section title (en) -> [(output name, source prefix, crop box or None, [blur boxes])]
SPEC = {
    "obs": {
        "General": [("obs-general", "IMG_5317_0", (185, 30, 935, 690), [])],
        "Output — streaming": [("obs-stream", "003", None, []), ("obs-stream-encoder", "004", None, [])],
        "Output — recording": [("obs-record", "005", None, [(193, 173, 657, 203)]), ("obs-record-encoder", "006", None, [])],
        "Output — audio and replay": [("obs-audio-bitrate", "007", None, []), ("obs-replay", "008", None, [])],
        "Audio": [("obs-audio", "009", None, []), ("obs-audio-advanced", "010", None, [])],
        "Video": [("obs-video", "011", None, [])],
        "Advanced": [("obs-advanced", "012", None, []), ("obs-advanced-network", "013", None, [])],
    },
    "spotify": {
        "Quality and library": [("spotify-quality", "014", None, [])],
        "Display and video": [("spotify-display", "015", None, [])],
        "Playback": [("spotify-playback", "017", None, [])],
        "Startup, storage and compatibility": [("spotify-startup", "018", None, [(8, 394, 310, 413)])],
        "Privacy": [("spotify-privacy", "016", None, [])],
    },
    "discord": {
        "Game Overlay": [("discord-overlay", "028", None, [])],
        "System": [("discord-system", "026", None, [])],
        "Animations (Accessibility → Reduced Motion)": [("discord-motion", "IMG_5311_2", (312, 175, 815, 420), [])],
        "Chat box & search": [("discord-chatbox", "IMG_5312_0", (315, 120, 815, 530), [])],
        "Voice — advanced": [("discord-voice", "IMG_5312_1", (315, 40, 815, 540), [])],
        "Streaming & sounds": [("discord-streaming", "IMG_5312_2", (315, 120, 815, 525), [])],
        "Clips": [("discord-clips", "025", None, [])],
        "Diagnostics (Voice → Advanced)": [("discord-diagnostics", "IMG_5313_0", (315, 45, 820, 530), [])],
        "Notifications": [("discord-notifications", "021", None, []), ("discord-sounds", "022", None, [])],
        "Email": [("discord-email", "023", None, [])],
        "Notifications — advanced": [("discord-notifications-advanced", "024", None, [])],
        "Data & privacy": [("discord-data", "019", (0, 0, 740, 712), [])],
        "Profile and voice encryption": [("discord-profile", "020", None, [(70, 494, 400, 514), (612, 455, 682, 512)])],
        "Activity": [("discord-activity", "027", (0, 0, 743, 318), [])],
        "Advanced": [("discord-advanced", "029", None, [])],
    },
    "epic": {
        "Launcher settings": [("epic-preferences", "030", None, [])],
        "Desktop notifications": [("epic-notifications", "031", None, [])],
    },
    "steam": {
        "Interface": [("steam-interface", "035", None, []), ("steam-interface-advanced", "036", None, [])],
        "Library": [("steam-library", "037", None, [])],
        "Downloads": [("steam-downloads", "038", None, []), ("steam-updates", "039", None, []), ("steam-downloads-more", "040", None, [])],
        "Shader Pre-Caching": [("steam-shaders", "041", None, [])],
        "In Game (overlay)": [("steam-ingame", "043", None, [])],
        "Game Recording": [("steam-recording", "044", None, [])],
        "Cloud": [("steam-cloud", "042", None, [])],
        "Remote Play & Broadcast": [("steam-remoteplay", "045", None, []), ("steam-broadcast", "IMG_5323_0", (215, 20, 935, 440), [])],
        "Friends & Chat": [("steam-friends", "032", None, []), ("steam-chat", "033", None, [])],
        "Notifications": [("steam-notifications", "034", None, [])],
    },
    "nvidia-app": {
        "Graphics — global settings": [("nvidia-global", "048", (0, 0, 1080, 849), []), ("nvidia-global-legacy", "049", (0, 0, 1060, 734), [])],
        "Features": [("nvidia-features", "046", None, [])],
        "Display & G-SYNC": [("nvidia-gsync", "050", None, []), ("nvidia-display", "051", None, [])],
        "Scaling": [("nvidia-scaling", "052", None, [])],
        "Color": [("nvidia-color", "053", None, [])],
        "Video": [("nvidia-video", "054", None, [])],
        "Advanced": [("nvidia-advanced", "055", None, [])],
        "Statistics": [("nvidia-statistics", "IMG_5319_0", (0, 0, 738, 520), [])],
        "HUD configuration": [("nvidia-hud", "IMG_5320_0", None, [])],
        "Highlights": [("nvidia-highlights", "IMG_5319_1", None, [])],
        "Video capture": [("nvidia-capture", "IMG_5320_1", None, []), ("nvidia-capture-bitrate", "IMG_5321_0", (0, 0, 560, 210), [])],
        "Notifications & privacy": [("nvidia-notifications", "047", None, []), ("nvidia-privacy", "IMG_5321_1", None, [])],
    },
    "amd-software": {
        "Graphics (global)": [("amd-graphics", "IMG_5330_0", None, [])],
        "Graphics — advanced": [("amd-graphics-advanced", "IMG_5330_1", (35, 10, 465, 485), [])],
        "System": [("amd-system", "IMG_5329_0", (35, 10, 910, 410), [])],
        "Hotkeys": [("amd-hotkeys", "IMG_5329_1", (35, 10, 910, 350), [])],
        "Preferences": [("amd-preferences", "IMG_5329_2", (35, 10, 470, 445), [])],
        "Performance metrics": [("amd-metrics", "IMG_5330_2", (250, 0, 900, 330), [])],
    },
}


def amd_graphics(im):
    """The source shows Radeon Boost on; the guide says off: copy Chill's 'Disabled' cell onto Boost and drop Boost's
    Performance Mode row, which only exists while Boost is on. Then crop to the settings column."""
    im = im.copy()
    im.paste(im.crop((250, 186, 457, 212)), (250, 121))
    top, rest = im.crop((0, 0, im.width, 149)), im.crop((0, 178, im.width, im.height))
    out = Image.new("RGB", (im.width, top.height + rest.height))
    out.paste(top, (0, 0))
    out.paste(rest, (0, top.height))
    return out.crop((35, 10, 465, out.height))


PATCH = {"amd-graphics": amd_graphics}


def blur(im, box):
    region = im.crop(box)
    for _ in range(3):
        region = region.filter(ImageFilter.GaussianBlur(radius=9))
    im.paste(region, box)


def build(src_dir):
    os.makedirs(OUT, exist_ok=True)
    images = {}
    for guide, sections in SPEC.items():
        for title, shots in sections.items():
            for name, prefix, crop, blurs in shots:
                matches = sorted(glob.glob(os.path.join(src_dir, prefix + "*")))
                if not matches:
                    sys.exit(f"missing source for {name}: {prefix}*")
                im = Image.open(matches[0]).convert("RGB")
                for box in blurs:
                    blur(im, box)
                if name in PATCH:
                    im = PATCH[name](im)
                elif crop:
                    im = im.crop(crop)
                if im.width > MAX_W:
                    im = im.resize((MAX_W, round(im.height * MAX_W / im.width)), Image.LANCZOS)
                path = os.path.join(OUT, name + ".webp")
                im.save(path, "WEBP", quality=88, method=6)
                images.setdefault(guide, {}).setdefault(title, []).append(name + ".webp")
                print(f"{name:34} {im.size[0]:4}x{im.size[1]:<4} {os.path.getsize(path) // 1024:4} KB")
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "images.json"), "w", encoding="utf-8") as f:
        json.dump(images, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    build(sys.argv[1] if len(sys.argv) > 1 else ".")
