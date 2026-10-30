"""Download the bestiary monster pictures used by the helper.

The saved IGN page is lazy-loading, but it also carries the server-rendered copy
of each table (with the real URLs) inside an escaped JSON blob. That is the copy
parsed here.

Images are written to assets/bestiary/mon_<bestiary number>.png, padded to a
square with transparency and scaled down. They are only a build input: if the
folder is empty the plugin simply draws no picture.

Run:  python tools/extract_monster_images.py
"""

import io
import os
import re
import sys
import time
import urllib.request

from PIL import Image

PAGE = r"C:\My Web Sites\FFV\www.ign.com\wikis\final-fantasy-v\Bestiary.html"
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "assets", "bestiary")
SIZE = 128

PATTERN = re.compile(
    r'alt=\\?"(?P<num>\d{3}) - (?P<name>[^"\\]+?)\.png\\?"\s+src=\\?"(?P<url>https://oyster\.ignimgs\.com/[^"\\]+)\\?"'
)


def collect():
    with open(PAGE, encoding="utf-8", errors="replace") as f:
        text = f.read()

    found = {}
    for match in PATTERN.finditer(text):
        number = int(match.group("num"))
        url = match.group("url")
        # ask for a smaller version; IGN honours the width parameter
        url = re.sub(r"\?width=\d+", "", url) + "?width=160"
        found.setdefault(number, url)
    return found


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def square(image):
    image = image.convert("RGBA")
    width, height = image.size
    scale = SIZE / max(width, height)
    image = image.resize((max(1, round(width * scale)), max(1, round(height * scale))),
                         Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((SIZE - image.width) // 2, (SIZE - image.height) // 2))
    return canvas


def main():
    os.makedirs(OUT, exist_ok=True)
    found = collect()
    print("image urls found:", len(found))

    missing = []
    for number in range(1, 303):
        if number not in found:
            missing.append(number)
    if missing:
        print("numbers with no url:", missing)

    written = skipped = failed = 0
    for number, url in sorted(found.items()):
        path = os.path.join(OUT, "mon_%03d.png" % number)
        if os.path.exists(path):
            skipped += 1
            continue

        try:
            raw = fetch(url)
            image = square(Image.open(io.BytesIO(raw)))
            image.save(path, optimize=True)
            written += 1
        except Exception as exc:  # noqa: BLE001 - report and continue
            failed += 1
            print("  failed", number, exc)
        time.sleep(0.05)

    total = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT))
    print(f"written={written} skipped={skipped} failed={failed} "
          f"files={len(os.listdir(OUT))} total={total / 1024:.0f} KB")


if __name__ == "__main__":
    main()
