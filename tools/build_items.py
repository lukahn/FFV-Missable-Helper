"""Builds a name -> content id map for the game's items, and reports how many of the
guide's chest/item names it covers. Run: python tools/build_items.py"""
import csv
import json
import os
import re
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MASTER = os.path.join(HERE, "_master")

ICON = re.compile(r"<[^>]*>")
PREFIXES = ("MSG_ITEM_NAME_", "MSG_WEAPON_NAME_", "MSG_ARMOR_NAME_", "MSG_KEY_NAME_")
FALLBACK_PREFIXES = ("MSG_MAGIC_NAME_",)
GIL = re.compile(r"^([\d,]+)\s+gil$")


def norm(text):
    text = ICON.sub(" ", text or "")
    text = text.replace("\u00a0", " ")
    # the guide mixes encodings: 'â€™' is a UTF-8 apostrophe read as latin-1
    text = text.replace("â€™", "'").replace("\u2019", "'").replace("\u2018", "'")
    text = text.replace("\ufffd", "'")
    text = text.replace('"', "'").replace("`", "'")
    text = re.sub(r"\s+", " ", text).strip().lower()
    return text


def read_text_table():
    """system_en.txt is 'KEY <IC_TAG>Name' per line."""
    table = {}
    path = os.path.join(MASTER, "system_en.txt")
    with open(path, encoding="utf-8-sig") as handle:
        for line in handle:
            line = line.rstrip("\r\n")
            if not line.strip():
                continue
            parts = line.split(None, 1)
            if len(parts) != 2:
                continue
            table[parts[0]] = parts[1]

    return table


def build():
    names = read_text_table()
    ids = {}
    clashes = Counter()

    with open(os.path.join(MASTER, "content.txt"), encoding="utf-8-sig") as handle:
        for row in csv.DictReader(handle):
            key = row.get("mes_id_name") or ""
            fallback = key.startswith(FALLBACK_PREFIXES)
            if not key.startswith(PREFIXES) and not fallback:
                continue

            label = names.get(key)
            if label is None:
                continue

            cleaned = norm(label)
            if not cleaned:
                continue

            content_id = int(row["id"])

            # a real item name wins over a spell that happens to share the name
            if cleaned in ids and (ids[cleaned] != content_id or fallback):
                clashes[cleaned] += 1
                continue

            ids[cleaned] = content_id

    return ids, clashes


def gold_for(name):
    """'5,000 gil' -> 5000, or 0 when the name is not a gil entry."""
    match = GIL.match(name)
    if not match:
        return 0

    return int(match.group(1).replace(",", ""))


def report(ids, clashes):
    guide_path = os.path.join(ROOT, "data", "guide.json")
    with open(guide_path, encoding="utf-8") as handle:
        guide = json.load(handle)

    wanted = {}
    for location in guide["locations"]:
        for entry in location.get("entries") or []:
            item = norm(entry.get("item"))
            if item:
                wanted.setdefault(item, 0)
                wanted[item] += 1

    missing = sorted(name for name in wanted if name not in ids and not gold_for(name))
    matched = len(wanted) - len(missing)

    print(f"content names: {len(ids)}   duplicate names dropped: {len(clashes)}")
    print(f"guide item names: {len(wanted)}   matched: {matched}   missing: {len(missing)}")
    for name in missing:
        print(f"  MISSING {name!r} ({wanted[name]}x)")

    return ids


if __name__ == "__main__":
    table, dupes = build()
    report(table, dupes)

    out = os.path.join(ROOT, "data", "items.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(table, handle, ensure_ascii=False, indent=0, sort_keys=True)

    print(f"wrote {out}")
