#!/usr/bin/env python3
"""Builds the launcher's icon files from one large square source image (needs Pillow: pip install pillow).

Usage: scripts/make-icon.py path/to/source.png

Makes the black background outside the rounded border transparent, then writes
src/DW2ModLauncher.Avalonia/Assets/icon.png (512px, the window icon) and icon.ico (16-256px, embedded in the exe).
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "DW2ModLauncher.Avalonia" / "Assets"
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
BACKGROUND_THRESHOLD = 28  # how far from pure black a pixel may be and still count as outside the icon


def transparent_outside(image: Image.Image) -> Image.Image:
    """Flood-fills from every corner so only the black area connected to the outside becomes transparent."""
    rgba = image.convert("RGBA")
    marker = (255, 0, 255, 0)
    for corner in ((0, 0), (rgba.width - 1, 0), (0, rgba.height - 1), (rgba.width - 1, rgba.height - 1)):
        ImageDraw.floodfill(rgba, corner, marker, thresh=BACKGROUND_THRESHOLD)
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            if pixels[x, y] == marker:
                pixels[x, y] = (0, 0, 0, 0)
    return rgba


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    source = transparent_outside(Image.open(sys.argv[1]))
    ASSETS.mkdir(parents=True, exist_ok=True)
    source.resize((512, 512), Image.LANCZOS).save(ASSETS / "icon.png")
    source.save(ASSETS / "icon.ico", sizes=SIZES)
    print(f"wrote {ASSETS / 'icon.png'} and {ASSETS / 'icon.ico'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
