"""Cleans up location entries the game cannot detect.

Two kinds turned up in play:
 1. duplicates - the wiki sometimes lists the same treasure twice (once under Items, once
    under Chests), so the second copy has no flag, can never be detected and permanently
    reads as "missing". Those are dropped.
 2. no flag at all - some entries (shop stock, one-off gifts) have no treasure flag in the
    game's own table. Where the treasure table has a matching item in the location's areas
    the flag is filled in; anything left is reported so it can be handled by hand.

Run: python tools/fix_flags.py            (dry run)
     python tools/fix_flags.py --write    (rewrite data/guide.json)
"""
import csv
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MASTER = os.path.join(HERE, "_master")
GUIDE = os.path.join(ROOT, "data", "guide.json")

ICON = re.compile(r"<[^>]*>")


def norm(text):
    text = ICON.sub(" ", text or "")
    text = text.replace("\u00a0", " ")
    text = text.replace("\u2019", "'").replace("\u2018", "'")
    text = text.replace('"', "'").replace("`", "'")
    text = text.replace("\ufffd", "'")
    text = re.sub(r"\s+", " ", text).strip().lower()
    return text


def content_names():
    names = {}
    for line in open(os.path.join(MASTER, "system_en.txt"), encoding="utf-8-sig"):
        parts = line.rstrip("\r\n").split(None, 1)
        if len(parts) == 2:
            names[parts[0]] = parts[1]

    labels = {}
    with open(os.path.join(MASTER, "content.txt"), encoding="utf-8-sig") as handle:
        for row in csv.DictReader(handle):
            key = row.get("mes_id_name") or ""
            if key in names:
                labels[int(row["id"])] = norm(names[key])

    return labels


def treasure_label(treasure, labels):
    if treasure["content_id"] == 0:
        return f"{treasure['content_num']} gil"

    return labels.get(treasure["content_id"], "")


def main(write):
    labels = content_names()
    treasures = json.load(open(os.path.join(MASTER, "treasures.json"), encoding="utf-8"))

    by_area = {}
    for treasure in treasures:
        by_area.setdefault(treasure["area_id"], []).append(treasure)

    guide = json.load(open(GUIDE, encoding="utf-8"))
    dropped = 0
    filled = 0
    left = []

    for location in guide["locations"]:
        entries = location.get("entries") or []
        if not entries:
            continue

        # --- 1. duplicates: a flagged entry already covers this item in this location
        flagged_items = {
            norm(entry.get("item"))
            for entry in entries
            if entry.get("flags") and entry.get("item")
        }

        kept = []
        for entry in entries:
            item = norm(entry.get("item"))
            if not entry.get("flags") and item and item in flagged_items:
                dropped += 1
                print(f"  drop duplicate {location['order']:02d} {location['name']}: "
                      f"{entry.get('label')} {entry.get('item')!r}")
                continue

            kept.append(entry)

        # --- 2. fill in a flag the guide did not know about
        used = {flag for entry in kept for flag in (entry.get("flags") or [])}

        pool = []
        for area in location.get("areaIds") or []:
            for treasure in by_area.get(area, []):
                if treasure["flag"] not in used:
                    pool.append((treasure["flag"], treasure_label(treasure, labels)))

        for entry in kept:
            if entry.get("flags") or not entry.get("item"):
                continue

            wanted = [norm(part) for part in re.split(r"\s+or\s+", entry["item"]) if part.strip()]
            matches = [(flag, label) for flag, label in pool if label and label in wanted]
            if len(matches) == 1:
                entry["flags"] = [matches[0][0]]
                used.add(matches[0][0])
                filled += 1
                print(f"  fill {location['order']:02d} {location['name']}: "
                      f"{entry.get('label')} {entry['item']!r} -> flag {matches[0][0]} ({matches[0][1]})")
            else:
                left.append((location["order"], location["name"], entry.get("label"), entry.get("item")))

        location["entries"] = kept

    print(f"dropped {dropped}, filled {filled}, still untrackable {len(left)}")
    for order, name, label, item in left:
        print(f"  untrackable {order:02d} {name}: {label} {item!r}")

    if write:
        with open(GUIDE, "w", encoding="utf-8") as handle:
            json.dump(guide, handle, ensure_ascii=False, indent=1)
        print("wrote", GUIDE)


if __name__ == "__main__":
    main("--write" in sys.argv)
