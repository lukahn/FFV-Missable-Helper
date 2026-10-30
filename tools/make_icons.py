"""Generate the small pixel icons the helper draws next to each row.

These are drawn from scratch (no game assets), at 32x32, with a dark outline so
they read on the blue message box.
"""

import os

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets")
S = 32

OUTLINE = (18, 16, 28, 255)
GOLD = (247, 205, 96, 255)
GOLD_DARK = (196, 142, 38, 255)
WHITE = (246, 246, 250, 255)
BROWN = (124, 78, 45, 255)
BROWN_DARK = (86, 51, 29, 255)
WOOD = (168, 112, 62, 255)
BLUE = (86, 152, 232, 255)
BLUE_DARK = (40, 92, 170, 255)
YELLOW = (250, 214, 80, 255)
ORANGE = (228, 152, 44, 255)
GREY = (198, 202, 214, 255)
BLACK = (34, 32, 46, 255)
GREEN = (110, 196, 116, 255)
RED = (214, 84, 84, 255)


def canvas():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def star_points(cx, cy, outer, inner, points=5):
    import math
    pts = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        a = -math.pi / 2 + i * math.pi / points
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def icon_star():
    img, d = canvas()
    d.polygon(star_points(16, 16.5, 14, 6), fill=OUTLINE)
    d.polygon(star_points(16, 16.5, 12, 5), fill=GOLD)
    d.polygon(star_points(13.5, 14, 6, 2.6), fill=(255, 240, 180, 255))
    return img


def icon_chest():
    img, d = canvas()
    d.rounded_rectangle([4, 8, 28, 27], radius=3, fill=OUTLINE)
    d.rounded_rectangle([6, 10, 26, 25], radius=2, fill=WOOD)
    d.rectangle([6, 10, 26, 15], fill=BROWN)
    d.rectangle([6, 20, 26, 25], fill=BROWN_DARK)
    d.rectangle([14, 12, 18, 25], fill=GOLD)
    d.rectangle([13, 17, 19, 20], fill=GOLD_DARK)
    d.rectangle([15, 18, 17, 20], fill=OUTLINE)
    d.line([7, 11, 25, 11], fill=(206, 154, 96, 255))
    return img


def icon_potion():
    img, d = canvas()
    d.rectangle([13, 4, 19, 11], fill=OUTLINE)
    d.rectangle([14, 5, 18, 10], fill=GREY)
    d.ellipse([7, 9, 25, 29], fill=OUTLINE)
    d.ellipse([9, 11, 23, 27], fill=(226, 234, 246, 255))
    d.ellipse([10, 16, 22, 26], fill=BLUE)
    d.ellipse([10, 18, 22, 26], fill=BLUE_DARK)
    d.rectangle([11, 17, 21, 20], fill=BLUE, width=0)
    d.ellipse([12, 12, 15, 15], fill=WHITE)
    d.rectangle([12, 20, 13, 24], fill=(140, 190, 250, 255))
    return img


def icon_chocobo():
    img, d = canvas()
    # head
    d.ellipse([8, 8, 24, 26], fill=OUTLINE)
    d.ellipse([10, 10, 22, 24], fill=YELLOW)
    # crest
    d.polygon([(15, 9), (19, 2), (21, 10)], fill=OUTLINE)
    d.polygon([(16, 9), (19, 4), (20, 10)], fill=ORANGE)
    # beak
    d.polygon([(22, 15), (30, 18), (22, 20)], fill=OUTLINE)
    d.polygon([(23, 16), (29, 18), (23, 19)], fill=ORANGE)
    # eye
    d.ellipse([13, 14, 17, 18], fill=WHITE)
    d.ellipse([14, 15, 16, 17], fill=BLACK)
    # wattle
    d.ellipse([19, 21, 23, 26], fill=RED)
    return img


def icon_hat():
    img, d = canvas()
    # brim
    d.ellipse([3, 20, 29, 28], fill=OUTLINE)
    d.ellipse([5, 21, 27, 27], fill=BLUE_DARK)
    # cone
    d.polygon([(16, 2), (26, 23), (6, 23)], fill=OUTLINE)
    d.polygon([(16, 5), (24, 22), (8, 22)], fill=BLUE)
    d.polygon([(16, 6), (20, 14), (12, 18)], fill=(140, 190, 250, 255))
    # star
    d.polygon(star_points(17, 17, 4, 1.8), fill=GOLD)
    return img


def icon_piano():
    img, d = canvas()
    d.rounded_rectangle([2, 9, 30, 25], radius=2, fill=OUTLINE)
    d.rectangle([4, 11, 28, 23], fill=WHITE)
    for i in range(1, 6):
        x = 4 + i * 4
        d.line([x, 11, x, 23], fill=(150, 150, 160, 255))
    for x in (7, 11, 15, 19, 23):
        d.rectangle([x, 11, x + 2, 18], fill=BLACK)
    d.rectangle([4, 21, 28, 23], fill=(224, 224, 232, 255))
    return img


def icon_notes():
    img, d = canvas()
    # parchment scroll
    d.rounded_rectangle([6, 3, 26, 29], radius=3, fill=OUTLINE)
    d.rounded_rectangle([8, 5, 24, 27], radius=2, fill=(238, 228, 198, 255))
    d.rectangle([8, 5, 24, 7], fill=(214, 200, 162, 255))
    # curled bottom
    d.ellipse([7, 24, 25, 30], fill=OUTLINE)
    d.ellipse([9, 25, 23, 29], fill=(214, 200, 162, 255))
    # exclamation mark
    d.rectangle([15, 9, 17, 18], fill=(176, 96, 40, 255))
    d.rectangle([15, 20, 17, 22], fill=(176, 96, 40, 255))
    return img


def icon_cursor():
    """A white glove pointing right, used when the menu's own hand cannot be found."""
    img, d = canvas()
    # outline pass
    d.rounded_rectangle([1, 13, 10, 26], radius=2, fill=OUTLINE)
    d.rounded_rectangle([7, 9, 21, 26], radius=5, fill=OUTLINE)
    d.rounded_rectangle([17, 10, 31, 16], radius=2, fill=OUTLINE)
    d.rounded_rectangle([10, 5, 18, 13], radius=2, fill=OUTLINE)
    # fill pass
    d.rounded_rectangle([2, 14, 9, 25], radius=1, fill=(206, 212, 230, 255))
    d.rounded_rectangle([8, 10, 20, 25], radius=4, fill=WHITE)
    d.rounded_rectangle([18, 11, 30, 15], radius=1, fill=WHITE)
    d.rounded_rectangle([11, 6, 17, 12], radius=1, fill=WHITE)
    # knuckles
    for y in (14, 17, 20, 23):
        d.line([10, y, 19, y], fill=(188, 194, 214, 255))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in (("star", icon_star), ("chest", icon_chest), ("potion", icon_potion),
                     ("chocobo", icon_chocobo), ("hat", icon_hat), ("piano", icon_piano),
                     ("notes", icon_notes), ("cursor", icon_cursor)):
        img = fn().resize((S * 4, S * 4), Image.NEAREST)
        path = os.path.join(OUT, "icon_%s.png" % name)
        img.save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
