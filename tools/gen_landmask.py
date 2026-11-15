#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""
Rasterize Natural Earth 1:110m land polygons into the globe land mask.

Usage: python3 tools/gen_landmask.py ne_110m_land.geojson land-1deg.bin
Source: Natural Earth (public domain), geojson/ne_110m_land.geojson in
https://github.com/nvkelso/natural-earth-vector
Layout: 360x180 bits, row 0 = 89.5N, column 0 = 179.5W,
bit i = byte i>>3, bit i&7 (LSB first).
Both paths must stay inside the current working directory.
"""
import json
import sys
from pathlib import Path

src, dst = (Path(a).resolve().relative_to(Path.cwd().resolve())
            for a in sys.argv[1:3])
rings = []
for feature in json.loads(src.read_text(encoding="utf-8"))["features"]:
    geometry = feature["geometry"]
    polygons = ([geometry["coordinates"]] if geometry["type"] == "Polygon"
                else geometry["coordinates"])
    for polygon in polygons:
        rings.extend(polygon)

W, H = 360, 180
bits = bytearray(W * H // 8)
for row in range(H):
    lat = 90 - (row + 0.5)
    crossings = sorted(
        x1 + (lat - y1) * (x2 - x1) / (y2 - y1)
        for ring in rings
        for (x1, y1), (x2, y2) in zip(ring, ring[1:])
        if (y1 > lat) != (y2 > lat)
    )
    for col in range(W):
        lon = -180 + (col + 0.5)
        if sum(1 for x in crossings if x < lon) % 2 == 1:
            i = row * W + col
            bits[i >> 3] |= 1 << (i & 7)
dst.write_bytes(bits)
