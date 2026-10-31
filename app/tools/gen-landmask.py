#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Rasterizes Natural Earth 1:110m land polygons (public domain) into the 1-degree land mask used by the globe.

Usage: python3 tools/gen-landmask.py ne_110m_land.geojson core/Edex.Core/Geo/land-1deg.bin
Source: https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_110m_land.geojson
Layout: 360x180 bits, row 0 = 89.5N, column 0 = 179.5W, bit i = byte i>>3, bit i&7 (LSB first).
"""
import json
import sys

src, dst = sys.argv[1], sys.argv[2]
rings = []
for feature in json.load(open(src))["features"]:
    geometry = feature["geometry"]
    polygons = [geometry["coordinates"]] if geometry["type"] == "Polygon" else geometry["coordinates"]
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
open(dst, "wb").write(bits)
