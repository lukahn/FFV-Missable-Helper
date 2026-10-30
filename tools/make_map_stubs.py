"""Create the map/screenshot stubs the helper looks for.

Files are zero bytes and named exactly like the picture on the IGN page. Drop a
PNG or JPG with the same name over a stub and the helper shows it; empty files
are ignored, so leaving a stub alone is safe.

By default the stubs are written to the repository (`maps/`) so the whole set
lives with the project. `--install` then copies whatever pictures are real
(non-empty) into the game's plugin folder and creates any missing stubs there.

    python tools/make_map_stubs.py                 # repo maps/ only
    python tools/make_map_stubs.py --install       # repo + game folder
    python tools/make_map_stubs.py --dir D:\\maps   # somewhere else
"""

import argparse
import json
import os
import re
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
GUIDE = os.path.join(PROJECT, "data", "guide.json")
REPO_MAPS = os.path.join(PROJECT, "maps")
DEFAULT_GAME = r"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY V PR"
GAME_MAPS = os.path.join(DEFAULT_GAME, "BepInEx", "plugins", "FFVMissableHelper", "maps")

BAD = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def wanted_names():
    with open(GUIDE, encoding="utf-8") as f:
        guide = json.load(f)

    wanted = []
    for location in guide["locations"]:
        if any(e["kind"] in ("chest", "item") for e in location["entries"]):
            # the wiki names files with underscores
            wanted.append(location["name"].replace(" ", "_") + ".png")
        for entry in location["entries"]:
            if entry.get("image"):
                wanted.append(entry["image"])
    return sorted(set(wanted))


def cleanup(folder, names):
    """Removes empty stubs whose names are no longer used. Never touches real pictures."""
    keep = set(names)
    removed = 0
    for name in os.listdir(folder):
        if name in keep:
            continue
        path = os.path.join(folder, name)
        if not os.path.isfile(path) or not name.lower().endswith((".png", ".jpg", ".jpeg", ".webp")):
            continue
        if os.path.getsize(path) == 0:
            os.remove(path)
            removed += 1
    return removed


def migrate_renames(folder, names):
    """Renames real pictures saved under the old space-style names to the wiki names."""
    keep = set(names)
    renamed = []
    for name in os.listdir(folder):
        path = os.path.join(folder, name)
        if not os.path.isfile(path) or os.path.getsize(path) == 0 or name in keep:
            continue

        candidate = name.replace(" ", "_")
        if candidate not in keep:
            continue

        target = os.path.join(folder, candidate)
        if os.path.exists(target) and os.path.getsize(target) > 0:
            continue

        os.replace(path, target)
        renamed.append((name, candidate))
    return renamed


def ensure_stub(folder, name):
    path = os.path.join(folder, name)
    if os.path.exists(path):
        return "kept"
    with open(path, "wb"):
        pass
    return "created"


def install_to_game(folder, names):
    os.makedirs(GAME_MAPS, exist_ok=True)
    migrate_renames(GAME_MAPS, names)
    copied = stubs = 0
    for name in names:
        source = os.path.join(folder, name)
        target = os.path.join(GAME_MAPS, name)

        if os.path.exists(source) and os.path.getsize(source) > 0:
            shutil.copy2(source, target)
            copied += 1
            continue

        if not os.path.exists(target):
            with open(target, "wb"):
                pass
            stubs += 1

    removed = cleanup(GAME_MAPS, names)
    return copied, stubs, removed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dir", help="where to write the stubs (default: the repo maps/)")
    parser.add_argument("--install", action="store_true",
                        help="also copy real pictures / missing stubs into the game folder")
    args = parser.parse_args()

    folder = args.dir or REPO_MAPS
    os.makedirs(folder, exist_ok=True)

    names = wanted_names()
    valid = [n for n in names if not BAD.search(n)]
    invalid = [n for n in names if BAD.search(n)]

    created = kept = 0
    for name in valid:
        if ensure_stub(folder, name) == "created":
            created += 1
        else:
            kept += 1

    obsolete = cleanup(folder, valid)
    renamed = migrate_renames(folder, valid)

    print(f"folder: {folder}")
    print(f"stubs created={created} existing={kept} obsolete removed={obsolete} total={len(valid)}")
    for old, new in renamed:
        print(f"renamed real picture: {old} -> {new}")
    if invalid:
        print("names that are not valid file names:", invalid)

    if args.install:
        real = sum(
            1 for n in valid
            if os.path.exists(os.path.join(folder, n)) and os.path.getsize(os.path.join(folder, n)) > 0
        )
        copied, stubs, removed = install_to_game(folder, valid)
        print(f"installed into {GAME_MAPS}: {copied} picture(s) copied, {stubs} stub(s) added, "
              f"{removed} obsolete removed")
        print(f"repo currently holds {real} real picture(s) of {len(valid)} names")


if __name__ == "__main__":
    main()
