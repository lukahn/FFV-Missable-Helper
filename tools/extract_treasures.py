"""Scan the game's map bundles for treasure-box entities.

Produces tools/_master/treasures.json: a flat list of
{flag, content_id, content_num, map, asset, area_id}
Cached so build_guide.py can be re-run quickly.
"""

import csv
import json
import os
import sys

import UnityPy

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY V PR"
AA = os.path.join(GAME, "FINAL FANTASY V_Data", "StreamingAssets", "aa", "StandaloneWindows64")
HERE = os.path.dirname(os.path.abspath(__file__))
M = os.path.join(HERE, "_master")
CACHE = os.path.join(M, "treasures.json")


def read_table(name):
    with open(os.path.join(M, name + ".txt"), encoding="utf-8", errors="replace", newline="") as f:
        return list(csv.DictReader(f))


def main():
    maps = read_table("map")
    boxes = read_table("treasure_box")
    wanted_areas = {int(b["gps_area_id"]) for b in boxes}

    # asset_name -> area_id (all maps in an asset group share an area)
    asset_to_area = {}
    for m in maps:
        aid = int(m["area_id"])
        if aid in wanted_areas:
            asset_to_area.setdefault(m["asset_name"], aid)

    # Scan every map bundle, not just ones whose asset group already maps to a
    # treasure area: a few areas (for example the Sealed Room) keep their chests
    # in a map group that is filed under a different area id.
    bundles = sorted(
        b for b in os.listdir(AA)
        if b.startswith("map_")
    )
    print(f"{len(asset_to_area)} asset groups, {len(bundles)} bundles to scan")

    found = {}
    for i, bundle in enumerate(bundles, 1):
        asset = bundle.split("_assets_all_")[0]
        area_id = asset_to_area.get(asset, 0)
        try:
            env = UnityPy.load(os.path.join(AA, bundle))
        except Exception as e:
            print("  load failed", bundle, e)
            continue
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            try:
                data = obj.read()
            except Exception:
                continue
            if getattr(data, "m_Name", "") != "entity_default":
                continue
            raw = data.m_Script
            raw = raw.encode("utf-8", "replace") if isinstance(raw, str) else bytes(raw)
            if b"flag_id" not in raw:
                continue
            try:
                js = json.loads(raw.decode("utf-8", "replace"))
            except Exception:
                continue
            for layer in js.get("layers", []):
                for ob in layer.get("objects", []):
                    props = {p.get("name"): p.get("value") for p in ob.get("properties", [])}
                    if "flag_id" not in props:
                        continue
                    flag = int(props["flag_id"])
                    if str(props.get("object_type", "")).strip() not in ("6", "5", "7", "0", ""):
                        pass
                    found[flag] = {
                        "flag": flag,
                        "content_id": int(props.get("content_id") or 0),
                        "content_num": int(props.get("content_num") or 1),
                        "object_name": ob.get("name"),
                        "asset": asset,
                        "area_id": area_id,
                    }
        if i % 50 == 0:
            print(f"  scanned {i}/{len(bundles)}, flags so far {len(found)}")
            sys.stdout.flush()

    out = sorted(found.values(), key=lambda r: r["flag"])
    with open(CACHE, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=0)
    print(f"wrote {len(out)} treasure entities to {CACHE}")

    master_flags = {int(b["flag"]) for b in boxes if int(b["flag"]) > 0}
    got = {r["flag"] for r in out}
    print("master flags:", len(master_flags), "found:", len(got),
          "missing:", sorted(master_flags - got)[:20], "extra:", sorted(got - master_flags)[:20])
    by_flag = {r["flag"]: r["asset"] for r in out}
    for flag in sorted(master_flags - got):
        print("   missing flag", flag)
    asset_to_maps = {}
    for m in maps:
        asset_to_maps.setdefault(m["asset_name"], []).append((m["map_name"], m["area_id"]))
    for flag in sorted(got - master_flags):
        asset = by_flag[flag]
        print("   extra flag", flag, "asset", asset, "maps", asset_to_maps.get(asset, "?")[:3])


if __name__ == "__main__":
    main()
