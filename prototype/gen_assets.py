"""Generate WordGuard app icon (.ico, multi-resolution) and a default alert sound (.wav)."""
import math
import struct
import wave

from PIL import Image, ImageDraw

BRAND = (37, 99, 235)        # #2563EB primary blue
BRAND_DARK = (29, 78, 216)   # #1D4ED8
BRAND_LIGHT = (96, 165, 250) # #60A5FA for sheen
WHITE = (255, 255, 255)


def _shield_polygon(s):
    """Classic shield: flat top, straight sides to a deep pointed bottom. Reads at 16px."""
    m = s * 0.11
    return [
        (m, m),
        (s - m, m),
        (s - m, s * 0.46),
        (s * 0.5, s - m * 0.4),
        (m, s * 0.46),
    ]


def _checkmark(s):
    """Bold check, centered in the shield's upper-mid area, sized so it survives small downscales."""
    lw = max(2, int(s * 0.105))
    p1 = (0.28, 0.50)
    p2 = (0.44, 0.67)
    p3 = (0.76, 0.31)
    return (
        (p1[0] * s, p1[1] * s, p2[0] * s, p2[1] * s, lw),
        (p2[0] * s, p2[1] * s, p3[0] * s, p3[1] * s, lw),
    )


def render_icon(size):
    """Flat, crisp shield with white check. Reads cleanly down to 16px."""
    SS = 4
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    poly = [(x * SS, y * SS) for (x, y) in _shield_polygon(big)]

    # soft drop shadow (slight offset, low alpha)
    shadow = [(x, y + int(2.5 * SS)) for (x, y) in poly]
    d.polygon(shadow, fill=(15, 23, 42, 55))

    # shield body — solid brand blue
    d.polygon(poly, fill=BRAND)

    # crisp inner stroke along the shield edge for definition at small sizes
    d.line(poly + [poly[0]], fill=BRAND_DARK, width=max(1, int(1.5 * SS)), joint="curve")

    # white checkmark, centered, generous stroke
    for (x1, y1, x2, y2, lw) in _checkmark(big):
        d.line([(x1, y1), (x2, y2)], fill=WHITE, width=lw, joint="curve")

    img = img.resize((size, size), Image.LANCZOS)
    return img


def make_ico(path, sizes=(16, 20, 24, 32, 40, 48, 64, 128, 256)):
    """Write a multi-size ICO container with PNG-compressed frames (PIL can't append ICO frames)."""
    import io

    frames = []
    for s in sizes:
        buf = io.BytesIO()
        render_icon(s).save(buf, format="PNG")
        frames.append((s, buf.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries = []
    blobs = []
    for (s, png) in frames:
        w = 0 if s >= 256 else s
        h = 0 if s >= 256 else s
        entries.append(struct.pack(
            "<BBBBHHII", w, h, 0, 0, 1, 32, len(png), offset))
        blobs.append(png)
        offset += len(png)

    with open(path, "wb") as f:
        f.write(header + b"".join(entries) + b"".join(blobs))
    print("wrote", path, "entries=", len(frames))


def make_wav(path, sr=44100):
    def tone(freq, start, dur, amp=0.45):
        n0 = int(start * sr)
        n1 = int((start + dur) * sr)
        out = []
        for i in range(n0, n1):
            t = i / sr
            local = (i - n0) / (n1 - n0)
            env = min(1.0, local / 0.01) * math.exp(-3.0 * local)
            v = amp * env * math.sin(2 * math.pi * freq * t)
            out.append(v)
        return out

    samples = []
    samples += tone(880.0, 0.0, 0.14, 0.45)
    samples += tone(1174.7, 0.12, 0.26, 0.45)
    samples += [0.0] * int(0.08 * sr)

    frames = bytearray()
    for v in samples:
        iv = max(-1.0, min(1.0, v))
        frames += struct.pack("<h", int(iv * 32767))

    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(bytes(frames))
    print("wrote", path, "samples=", len(samples))


if __name__ == "__main__":
    make_ico(r"F:\code\System\desk\src\WordGuard.Client.App\WordGuard.ico")
    make_ico(r"F:\code\System\desk\src\WordGuard.Studio.App\WordGuard.ico")
    make_wav(r"F:\code\System\desk\src\WordGuard.Client.App\alert.wav")
