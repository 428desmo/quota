"""Crop trade-good photos to 288px icons.

The source JPEGs are objects on a white field. Background is the near-white
region connected to the image edge. The largest remaining object is scaled
so its longer side is 288px and centered on a transparent 288×288 PNG.
"""

from __future__ import annotations

import json
import struct
import subprocess
import uuid
import zlib
from array import array
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VISUAL = ROOT / "visual"
CATALOG = ROOT / "quota_goods_v1.0.json"
DESTINATIONS = (ROOT / "web" / "goods", ROOT / "unity" / "Assets" / "StreamingAssets" / "goods")
SIZE = 288
LIGHT = 250


def main() -> None:
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    items = list(catalog["goods"]) + [catalog["wild"]]
    for dest in DESTINATIONS:
        dest.mkdir(parents=True, exist_ok=True)
    streaming = ROOT / "unity" / "Assets" / "StreamingAssets"
    write_meta(streaming / "goods.meta", folder=True)
    goods_json = streaming / "quota_goods_v1.0.json"
    goods_json.write_text(CATALOG.read_text(encoding="utf-8"), encoding="utf-8")
    write_meta(streaming / "quota_goods_v1.0.json.meta", folder=False)
    for item in items:
        source = VISUAL / f"{item['file']}.jpg"
        rgb, width, height = load_jpeg(source)
        box = subject_box(rgb, width, height)
        x0, y0, x1, y1 = box
        if x1 - x0 > width * 0.9 or y1 - y0 > height * 0.9:
            raise SystemExit(f"background was not removed from {source.name}")
        png = render(rgb, width, height, box)
        for dest in DESTINATIONS:
            path = dest / f"{item['file']}.png"
            path.write_bytes(png)
        write_meta(DESTINATIONS[1] / f"{item['file']}.png.meta", folder=False)
        print(f"{item['file']}: subject {x1 - x0}x{y1 - y0} at ({x0},{y0})")


def load_jpeg(path: Path) -> tuple[bytes, int, int]:
    raw = subprocess.check_output(["djpeg", str(path)])
    if not raw.startswith(b"P6"):
        raise SystemExit(f"{path} did not decode as PPM")
    index = 2
    tokens: list[bytes] = []
    while len(tokens) < 3:
        while raw[index : index + 1] in b" \t\r\n":
            index += 1
        if raw[index : index + 1] == b"#":
            while raw[index : index + 1] != b"\n":
                index += 1
            continue
        start = index
        while raw[index : index + 1] not in b" \t\r\n":
            index += 1
        tokens.append(raw[start:index])
    width, height, maxval = (int(token) for token in tokens)
    if maxval != 255 or raw[index : index + 1] not in b" \t\r\n":
        raise SystemExit(f"unexpected PPM header in {path}")
    index += 1
    rgb = raw[index:]
    if len(rgb) != width * height * 3:
        raise SystemExit(f"PPM size mismatch in {path}")
    return rgb, width, height


def subject_box(rgb: bytes, width: int, height: int) -> tuple[int, int, int, int]:
    count = width * height
    background = bytearray(count)
    stack: list[int] = []

    def light(pixel: int) -> bool:
        offset = pixel * 3
        return rgb[offset] >= LIGHT and rgb[offset + 1] >= LIGHT and rgb[offset + 2] >= LIGHT

    for x in range(width):
        for y in (0, height - 1):
            pixel = y * width + x
            if light(pixel):
                background[pixel] = 1
                stack.append(pixel)
    for y in range(height):
        for x in (0, width - 1):
            pixel = y * width + x
            if not background[pixel] and light(pixel):
                background[pixel] = 1
                stack.append(pixel)
    while stack:
        pixel = stack.pop()
        y, x = divmod(pixel, width)
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if nx < 0 or ny < 0 or nx >= width or ny >= height:
                continue
            neighbor = ny * width + nx
            if not background[neighbor] and light(neighbor):
                background[neighbor] = 1
                stack.append(neighbor)

    component = array("I", [0]) * count
    best_id = 0
    best_count = 0
    next_id = 0
    for start in range(count):
        if background[start] or component[start]:
            continue
        next_id += 1
        component[start] = next_id
        stack = [start]
        found = 0
        while stack:
            pixel = stack.pop()
            found += 1
            y, x = divmod(pixel, width)
            for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if nx < 0 or ny < 0 or nx >= width or ny >= height:
                    continue
                neighbor = ny * width + nx
                if background[neighbor] or component[neighbor]:
                    continue
                component[neighbor] = next_id
                stack.append(neighbor)
        if found > best_count:
            best_count = found
            best_id = next_id
    if best_count < width * height // 200:
        raise SystemExit("subject is missing")

    min_x, min_y, max_x, max_y = width, height, 0, 0
    for pixel in range(count):
        if component[pixel] != best_id:
            background[pixel] = 1
            continue
        y, x = divmod(pixel, width)
        if x < min_x:
            min_x = x
        if y < min_y:
            min_y = y
        if x > max_x:
            max_x = x
        if y > max_y:
            max_y = y
    # Keep the foreground mask in the unused high bit of background: 0 means subject.
    subject_box.mask = background  # type: ignore[attr-defined]
    return min_x, min_y, max_x + 1, max_y + 1


def render(rgb: bytes, width: int, height: int, box: tuple[int, int, int, int]) -> bytes:
    background: bytearray = subject_box.mask  # type: ignore[attr-defined]
    x0, y0, x1, y1 = box
    bw = x1 - x0
    bh = y1 - y0
    if bw >= bh:
        dw = SIZE
        dh = max(1, min(SIZE, round(bh * SIZE / bw)))
    else:
        dh = SIZE
        dw = max(1, min(SIZE, round(bw * SIZE / bh)))
    origin_x = (SIZE - dw) // 2
    origin_y = (SIZE - dh) // 2
    pixels = bytearray(SIZE * SIZE * 4)
    for y in range(dh):
        sy0 = y0 + y * bh / dh
        sy1 = y0 + (y + 1) * bh / dh
        for x in range(dw):
            sx0 = x0 + x * bw / dw
            sx1 = x0 + (x + 1) * bw / dw
            red = green = blue = covered = total = 0
            iy0 = int(sy0)
            iy1 = min(height, int(sy1) + 1)
            ix0 = int(sx0)
            ix1 = min(width, int(sx1) + 1)
            for iy in range(iy0, iy1):
                row = iy * width
                for ix in range(ix0, ix1):
                    total += 1
                    pixel = row + ix
                    if background[pixel]:
                        continue
                    offset = pixel * 3
                    red += rgb[offset]
                    green += rgb[offset + 1]
                    blue += rgb[offset + 2]
                    covered += 1
            if not covered or not total:
                continue
            alpha = min(255, round(255 * covered / total))
            out = ((origin_y + y) * SIZE + (origin_x + x)) * 4
            pixels[out] = red // covered
            pixels[out + 1] = green // covered
            pixels[out + 2] = blue // covered
            pixels[out + 3] = alpha
    return png(SIZE, SIZE, pixels)


def png(width: int, height: int, rgba: bytes) -> bytes:
    raw = b"".join(b"\x00" + rgba[y * width * 4 : (y + 1) * width * 4] for y in range(height))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return b"".join(
        (
            b"\x89PNG\r\n\x1a\n",
            chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)),
            chunk(b"IDAT", zlib.compress(raw, 9)),
            chunk(b"IEND", b""),
        )
    )


def write_meta(path: Path, folder: bool) -> None:
    if path.exists():
        return
    guid = uuid.uuid4().hex
    kind = "folderAsset: yes\n" if folder else ""
    path.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        f"{kind}"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
