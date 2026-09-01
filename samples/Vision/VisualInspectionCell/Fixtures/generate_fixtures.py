"""Generates the three inspection fixtures for the VisualInspectionCell sample.

The sample's deterministic analyser measures these images by counting pixels
and converting to millimetres through a known calibration scale, so the
geometry written here IS the ground truth the verdict is judged against.
Regenerate with:  python tools/generate_fixtures.py <output-dir>

At 10 px/mm a feature edge can only fall on a pixel boundary, so a measurement
carries a quantisation uncertainty of one pixel, 0.10 mm. That is not an
invented number to make the demonstration work - it is the camera's pixel
pitch, and it is what makes the Uncertainty field of
VisionCharacteristicDataType mean something physical.

One recipe, three parts:

  bore diameter   nominal 12.00 mm, tolerance +/- 0.20
  slot width      nominal  8.00 mm, tolerance +/- 0.15
  edge offset     nominal 20.00 mm, tolerance +/- 0.25

  bracket-ok         bore 12.00, slot 8.00. Every interval falls wholly
                     inside its tolerance band, so the verdict is Ok.
  bracket-not-ok     bore 12.60. The interval [12.50, 12.70] lies wholly
                     outside the [11.80, 12.20] band, so the verdict is NotOk.
  bracket-ambiguous  slot draws as 8.10 - 8.15 mm is 81.5 px and cannot be
                     drawn. Its interval [8.00, 8.20] straddles the 8.15
                     tolerance limit, so no verdict is possible and the
                     characteristic is NotDecidable. This is precisely the
                     case the specification defines that value for, and the
                     case that escalates to a human.
"""

import struct
import sys
import zlib
from pathlib import Path

WIDTH = 800
HEIGHT = 600
SCALE_PX_PER_MM = 10.0

BACKGROUND = (28, 30, 34)
BRACKET = (176, 180, 188)
CUT = (18, 19, 22)


def new_canvas():
    return [[BACKGROUND for _ in range(WIDTH)] for _ in range(HEIGHT)]


def fill_rect(px, x0, y0, x1, y1, colour):
    for y in range(max(0, int(y0)), min(HEIGHT, int(y1))):
        for x in range(max(0, int(x0)), min(WIDTH, int(x1))):
            px[y][x] = colour


def fill_circle(px, cx, cy, radius, colour):
    r2 = radius * radius
    for y in range(max(0, int(cy - radius) - 1), min(HEIGHT, int(cy + radius) + 2)):
        for x in range(max(0, int(cx - radius) - 1), min(WIDTH, int(cx + radius) + 2)):
            dx = x + 0.5 - cx
            dy = y + 0.5 - cy
            if dx * dx + dy * dy <= r2:
                px[y][x] = colour


def render(bore_mm, slot_mm, edge_mm):
    """Draws a bracket whose features measure exactly the given millimetres."""
    px = new_canvas()

    body_x0, body_y0 = 150, 110
    body_x1, body_y1 = 650, 490
    fill_rect(px, body_x0, body_y0, body_x1, body_y1, BRACKET)

    # The bore: a through hole, measured across its diameter.
    bore_r = (bore_mm * SCALE_PX_PER_MM) / 2.0
    fill_circle(px, 290.0, 300.0, bore_r, CUT)

    # The slot: measured across its width. Its left edge sits edge_mm from the
    # bracket's right-hand edge, which is the third characteristic.
    slot_w = slot_mm * SCALE_PX_PER_MM
    slot_left = body_x1 - (edge_mm * SCALE_PX_PER_MM)
    fill_rect(px, slot_left, 200.0, slot_left + slot_w, 400.0, CUT)

    return px


def write_png(path, px):
    raw = bytearray()
    for row in px:
        raw.append(0)
        for r, g, b in row:
            raw += bytes((r, g, b))

    def chunk(tag, data):
        out = struct.pack(">I", len(data)) + tag + data
        return out + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 2, 0, 0, 0)
    blob = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )
    Path(path).write_bytes(blob)
    return len(blob)


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    out.mkdir(parents=True, exist_ok=True)

    parts = {
        "bracket-ok.png": (12.00, 8.00, 20.00),
        "bracket-not-ok.png": (12.60, 8.00, 20.00),
        "bracket-ambiguous.png": (12.00, 8.15, 20.00),
    }

    for name, (bore, slot, edge) in parts.items():
        size = write_png(out / name, render(bore, slot, edge))
        print(f"{name:26} bore={bore:5.2f} slot={slot:5.2f} edge={edge:5.2f}  {size:6d} bytes")


if __name__ == "__main__":
    main()
