#!/usr/bin/env python3
"""生成带"雷犀"文字的 WordGuard 应用图标（盾牌渐变蓝 + 白色中文），多尺寸 ICO。
手动组装 ICO（每尺寸独立渲染为 PNG 后拼入），保证各尺寸清晰。"""
import io
import struct

from PIL import Image, ImageDraw, ImageFont

FONT_PATH = r"C:\Windows\Fonts\msyhbd.ttc"   # 微软雅黑 粗体
OUT_CLIENT = r"E:\System\desk\src\WordGuard.Client.App\WordGuard.ico"
OUT_STUDIO = r"E:\System\desk\src\WordGuard.Studio.App\WordGuard.ico"
TOP_COLOR = (37, 99, 235, 255)    # #2563EB
BOT_COLOR = (30, 64, 175, 255)    # #1E40AF
TEXT_COLOR = (255, 255, 255, 255)
SIZES = [256, 128, 64, 48, 32, 24, 16]


def shield_mask(size):
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    s = size
    pts = [
        (0.50 * s, 0.97 * s), (0.93 * s, 0.55 * s),
        (0.90 * s, 0.13 * s), (0.10 * s, 0.13 * s), (0.07 * s, 0.55 * s),
    ]
    d.polygon(pts, fill=255)
    return mask


def gradient(size):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    for y in range(size):
        t = y / max(1, size - 1)
        r = int(TOP_COLOR[0] + (BOT_COLOR[0] - TOP_COLOR[0]) * t)
        g = int(TOP_COLOR[1] + (BOT_COLOR[1] - TOP_COLOR[1]) * t)
        b = int(TOP_COLOR[2] + (BOT_COLOR[2] - TOP_COLOR[2]) * t)
        for x in range(size):
            px[x, y] = (r, g, b, 255)
    return img


def draw_icon(size):
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    mask = shield_mask(size)
    base.paste(gradient(size), (0, 0), mask)

    if size >= 32:
        d = ImageDraw.Draw(base)
        s = size
        pts = [
            (0.50 * s, 0.97 * s), (0.93 * s, 0.55 * s),
            (0.90 * s, 0.13 * s), (0.10 * s, 0.13 * s), (0.07 * s, 0.55 * s),
        ]
        d.line(pts + [pts[0]], fill=(255, 255, 255, 90),
               width=max(1, size // 64), joint="curve")

    try:
        font = ImageFont.truetype(FONT_PATH, int(size * 0.34))
    except Exception:
        font = ImageFont.load_default()
    text = "雷犀"
    d = ImageDraw.Draw(base)
    l, t, r, b = d.textbbox((0, 0), text, font=font)
    tw, th = r - l, b - t
    cx, cy = size * 0.5, size * 0.45
    d.text((cx - tw / 2 - l, cy - th / 2 - t), text, font=font, fill=TEXT_COLOR)
    return base


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def assemble_ico(frames):
    """frames: list of (size, png_bytes)。手动写入 ICONDIR + 条目 + 数据。"""
    entries = b""
    data = b""
    offset = 6 + 16 * len(frames)
    for size, png in frames:
        w = size if size < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(png), offset)
        data += png
        offset += len(png)
    header = struct.pack("<HHH", 0, 1, len(frames))
    return header + entries + data


def main():
    frames = [(s, png_bytes(draw_icon(s))) for s in SIZES]
    ico = assemble_ico(frames)
    for out in (OUT_CLIENT, OUT_STUDIO):
        with open(out, "wb") as f:
            f.write(ico)
        print("written:", out, len(ico), "bytes,", len(frames), "frames")


if __name__ == "__main__":
    main()
