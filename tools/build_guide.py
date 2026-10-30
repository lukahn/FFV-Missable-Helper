"""Build data/guide.json for the FFV Helper plugin.

Inputs (all produced by the scripts in this folder):
  _master/*.txt           game master-data CSVs        (extract_master.py)
  _master/treasures.json  flag -> map content          (extract_treasures.py)
  _master/wiki_*.json     parsed IGN pages             (parse_wiki.py)
  ../FFV-Blue-Mage-Helper/data/blue_magic.json  blue magic spell/enemy table
  locations.py            curated story order

Output: data/guide.json, embedded into the plugin.

Pairing happens in two passes: every wiki entry is matched to a treasure flag
first, then whatever flags are left over become extra entries on the location
that owns the area. Doing it the other way round lets an early location swallow
the flags a later section in the same area needs (the Rift does this).
"""

import csv
import json
import os
import re

from locations import LOCATIONS

HERE = os.path.dirname(os.path.abspath(__file__))
M = os.path.join(HERE, "_master")
PROJECT = os.path.dirname(HERE)
OUT = os.path.join(PROJECT, "data", "guide.json")
BLUE = os.path.join(os.path.dirname(PROJECT), "FFV-Blue-Mage-Helper", "data", "blue_magic.json")


# --------------------------------------------------------------------------- io
def table(name):
    with open(os.path.join(M, name + ".txt"), encoding="utf-8", errors="replace", newline="") as f:
        return list(csv.DictReader(f))


def load_json(*parts):
    with open(os.path.join(*parts), encoding="utf-8") as f:
        return json.load(f)


def read_messages():
    msg = {}
    with open(os.path.join(M, "system_en.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\r\n").split("\t")
            if len(parts) >= 2:
                msg[parts[0]] = parts[1]
    return msg


# ----------------------------------------------------------------------- names
ICON_TAG = re.compile(r"^<IC_[A-Z0-9_]+>")


def strip_icon(name):
    return ICON_TAG.sub("", (name or "").strip())


def norm(name):
    return re.sub(r"[^a-z0-9]", "", (name or "").lower())


def gil_amount(text):
    m = re.search(r"([\d][\d,]*)", text or "")
    return m.group(1).replace(",", "") if m else None


# Names the IGN pages spell differently from the game's own text.
ITEM_ALIASES = {
    "airknight": "airknife",        # IGN typo, Istory Falls
    "flametounge": "flametongue",   # IGN typo, Forest of Moore
    "mythrilglove": "mythrilgloves",
    "bravebladechickenknife": "b",  # the two mutually exclusive Tule boxes
}


# ------------------------------------------------------------------------ main
def main():
    msg = read_messages()
    content = {int(r["id"]): r for r in table("content")}
    boxes = table("treasure_box")
    monster = {int(r["id"]): strip_icon(msg.get(r["mes_id_name"], r["mes_id_name"]))
               for r in table("monster")}
    pb_rows = table("picture_book")
    treasures = {t["flag"]: t for t in load_json(M, "treasures.json")}
    wiki = load_json(M, "wiki_chests.json")
    wiki_best = load_json(M, "wiki_bestiary.json")
    pianos = load_json(M, "wiki_pianos.json")
    blue_data = load_json(BLUE)

    print(f"boxes={len(boxes)} monsters={len(monster)} picture_book={len(pb_rows)} "
          f"wiki rows={len(wiki_best['rows'])} blue spells={len(blue_data['spells'])}")

    # ---------------------------------------------------------- treasure boxes
    def content_name(cid):
        return strip_icon(msg.get(content.get(cid, {}).get("mes_id_name", ""), ""))

    # Some flags appear twice in the master table (the same dungeon is filed under
    # a world-2 and a world-3 area id). Keep one record per flag, preferring an
    # area that the location list actually uses.
    wanted_areas = {a for loc in LOCATIONS for a in loc["area_ids"]}
    records = []
    seen_flags = {}
    for b in boxes:
        flag = int(b["flag"])
        if flag in seen_flags:
            prev = seen_flags[flag]
            if prev["area"] not in wanted_areas and int(b["gps_area_id"]) in wanted_areas:
                prev["area"] = int(b["gps_area_id"])
            continue
        t = treasures.get(flag, {})
        cid = int(t.get("content_id") or 0)
        name = content_name(cid)
        obj = t.get("object_name") or ""
        if cid == 1:
            amount = str(t.get("content_num") or gil_amount(obj) or "")
            name = f"{int(amount):,} gil" if amount else "Gil"
        rec = {"flag": flag, "area": int(b["gps_area_id"]), "type": int(b["treasure_type"]),
               "content": cid, "name": name, "map": t.get("asset") or "", "used": False}
        records.append(rec)
        seen_flags[flag] = rec

    # ------------------------------------------------------------- bestiary map
    monster_ids_by_name = {}
    for mid, name in monster.items():
        monster_ids_by_name.setdefault(norm(name), []).append(mid)

    # vocabulary = every location name and bestiary heading, longest match first
    vocab = []
    for loc in LOCATIONS:
        vocab.append((norm(loc["name"]), loc["key"]))
        # "Castle Exdeath (First Visit)" is written as "Castle Exdeath" in the rows
        if " (" in loc["name"]:
            vocab.append((norm(loc["name"].split(" (")[0]), loc["key"]))
        for h in loc.get("bestiary", []):
            vocab.append((norm(h), loc["key"]))
    vocab = sorted({(n, k) for n, k in vocab if n}, key=lambda x: (-len(x[0]), x[0], x[1]))

    loc_by_key = {loc["key"]: loc for loc in LOCATIONS}
    monsters_by_loc = {loc["key"]: [] for loc in LOCATIONS}
    unmatched_fragments = set()

    for row in wiki_best["rows"]:
        raw = row["locations_raw"]
        seen = []
        i = 0
        while i < len(raw):
            while i < len(raw) and not raw[i].isalnum():
                i += 1
            hit = None
            for vn, key in vocab:
                if norm(raw[i:i + len(vn) + 16])[:len(vn)] == vn:
                    hit = (vn, key)
                    break
            if hit:
                if hit[1] not in seen:
                    seen.append(hit[1])
                i += len(hit[0])
            else:
                m = re.match(r"[A-Za-z0-9'&.\-]+", raw[i:])
                if not m:
                    break
                unmatched_fragments.add(m.group(0))
                i += len(m.group(0))
        for key in seen:
            monsters_by_loc[key].append(row)

    # The wiki uses numbered / suffixed placeholder names for some rows
    # ("Gilgamesh 1", "Abductor 2", "Launcher A", "Unknown (Slime)", "??? (Fire)").
    # The game keys its picture book by monster id, so strip the suffix and match.
    SPELLING = {"archetoad": "archeotoad", "castrophe": "catastrophe", "lyacon": "lycaon"}

    def resolve_monsters(name):
        ids = monster_ids_by_name.get(norm(name), [])
        if ids:
            return ids

        base = re.sub(r"\s*\(.*?\)$", "", name)
        base = re.sub(r"\s+[0-9]+$", "", base)
        base = re.sub(r"\s+[AB]$", "", base).strip()
        if base and norm(base) != norm(name):
            ids = monster_ids_by_name.get(norm(base), [])
        if not ids:
            fixed = SPELLING.get(norm(name))
            if fixed:
                ids = monster_ids_by_name.get(fixed, [])
        return ids

    def bestiary_entry(row):
        return {"n": row["number"], "name": row["name"],
                "monsters": resolve_monsters(row["name"])}

    unmapped_bestiary = sorted({row["name"] for row in wiki_best["rows"]
                                if not resolve_monsters(row["name"])})

    # ------------------------------------------------- build entries, pass one
    out_locations = []
    used_section_names = set()
    missing_sections = set()

    for order, loc in enumerate(LOCATIONS, 1):
        entries = []
        for section_name in loc.get("chests", []):
            wanted = norm(section_name)
            section = next((s for s in wiki if norm(s["name"]) == wanted), None)
            if section is None:
                missing_sections.add(section_name)
                continue
            used_section_names.add(section["name"])
            for e in section["entries"]:
                images = e.get("images") or []
                entries.append({
                    "kind": e["kind"],
                    "label": f"{e['kind'].capitalize()} {e['index']}",
                    "item": e["contains"],
                    "where": e["location"],
                    "guide": " ".join(e["guide"]),
                    # first picture the wiki shows for this entry is its map/screenshot
                    "image": images[0] if images else None,
                    "flag": None,
                    "late": section["world"] == 3 and loc["world"] < 3,
                })
        out_locations.append({
            "key": loc["key"], "name": loc["name"], "world": loc["world"], "order": order,
            "areaIds": loc["area_ids"], "missable": bool(loc.get("missable")),
            "note": loc.get("note", ""), "missables": list(loc.get("missables", [])),
            "entries": entries, "bestiary": [], "blue": [], "pianos": [],
        })
    # keep a direct handle on each entry's flag list
    for out in out_locations:
        for entry in out["entries"]:
            entry["flags"] = [entry.pop("flag")] if entry["flag"] is not None else []

    pools = {}
    for r in records:
        pools.setdefault(r["area"], []).append(r)

    def take(area_ids, predicate):
        for area in area_ids:
            for r in pools.get(area, []):
                if not r["used"] and predicate(r):
                    r["used"] = True
                    return r
        return None

    # pass one: pair every wiki entry with a flag
    for out in out_locations:
        for entry in out["entries"]:
            target = norm(entry["item"])
            target = ITEM_ALIASES.get(target, target)
            amount = gil_amount(entry["item"]) if "gil" in entry["item"].lower() else None

            if target == "b":
                # the two mutually exclusive weapon boxes count as one obtainable
                group = [r for a in out["areaIds"] for r in pools.get(a, [])
                         if not r["used"] and r["content"] == 0]
                for r in group:
                    r["used"] = True
                    entry["flags"].append(r["flag"])
                if group:
                    entry["item"] = "Brave Blade or Chicken Knife"
                continue
            if not target and not amount:
                match = take(out["areaIds"], lambda r: True)
            elif amount:
                match = take(out["areaIds"],
                             lambda r, a=amount: r["name"].lower().endswith("gil")
                             and gil_amount(r["name"]) == a)
                if match is None:
                    # amount not carried by the object name: take any unclaimed gil box
                    match = take(out["areaIds"], lambda r: r["name"].lower().endswith("gil"))
                if match is None:
                    match = take(out["areaIds"], lambda r, t=target: norm(r["name"]) == t)
            else:
                match = take(out["areaIds"], lambda r, t=target: norm(r["name"]) == t)
                if match is None:
                    # duplicate wiki names are common; allow a contains match
                    match = take(out["areaIds"], lambda r, t=target: t and t in norm(r["name"]))
            if match:
                entry["flags"].append(match["flag"])

    # pass two: unclaimed flags become extra entries on the first location owning them
    unpaired = []
    for r in records:
        if r["used"]:
            continue
        owner = next((o for o in out_locations if r["area"] in o["areaIds"]), None)
        if owner is None:
            unpaired.append((r["flag"], r["name"], "no location"))
            continue
        kind = "chest" if r["type"] == 1 else "item"
        n = 1 + sum(1 for e in owner["entries"] if e["label"].startswith(f"{kind.capitalize()} +"))
        label = f"{kind.capitalize()} +{n}"
        owner["entries"].append({"kind": kind, "label": label,
                                 "item": r["name"] or "Unlisted", "where": "",
                                 "guide": "", "image": None, "flags": [r["flag"]], "late": False})
        r["used"] = True
        unpaired.append((owner["name"], r["flag"], r["name"], r["type"]))

    # ------------------------------------------------------------ bestiary etc.
    extra_unresolved = []
    for out, loc in zip(out_locations, LOCATIONS):
        best = []
        for row in monsters_by_loc[loc["key"]]:
            item = bestiary_entry(row)
            if item not in best:
                best.append(item)
        # monsters the wiki table never lists, curated by hand in locations.py
        for name in loc.get("bestiary_extra", []):
            ids = resolve_monsters(name)
            if not ids:
                extra_unresolved.append(name)
            item = {"n": 0, "name": name, "monsters": ids}
            if not any(b["name"] == name for b in best):
                best.append(item)
        best.sort(key=lambda x: (x["n"] == 0, x["n"], x["name"]))
        out["bestiary"] = best
        out["pianos"] = [{"slot": p["number"], "name": p["where"]}
                         for p in pianos if p["number"] in loc.get("pianos", [])]

    first_loc_by_name = {}
    for out in out_locations:
        for b in out["bestiary"]:
            first_loc_by_name.setdefault(norm(b["name"]), out)

    spells_unplaced = []
    for spell in blue_data["spells"]:
        placed = None
        enemies = []
        for mid in spell.get("enemies", []):
            name = monster.get(int(mid), "")
            if name:
                enemies.append(name)
            loc = first_loc_by_name.get(norm(name))
            if loc and (placed is None or loc["order"] < placed["order"]):
                placed = loc
        if placed is None:
            spells_unplaced.append((spell["name"], enemies))
            continue
        placed["blue"].append({"id": spell["id"], "name": spell["name"],
                               "enemies": sorted(set(enemies))})
    for out in out_locations:
        out["blue"].sort(key=lambda x: x["id"])

    data = {"format": 1,
            "source": "IGN Final Fantasy V wiki (local copy) + FINAL FANTASY V PR master data",
            "locations": out_locations}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)

    # ------------------------------------------------------------------ report
    used = sum(1 for r in records if r["used"])
    print(f"wrote {OUT}")
    print(f"  locations={len(out_locations)} entries={sum(len(o['entries']) for o in out_locations)} "
          f"flags={len(records)} paired={used}")
    print(f"  bestiary entries={sum(len(o['bestiary']) for o in out_locations)} "
          f"blue placed={sum(len(o['blue']) for o in out_locations)}")
    print(f"  missable notes={sum(len(o['missables']) for o in out_locations)} "
          f"on {sum(1 for o in out_locations if o['missables'])} locations")
    if extra_unresolved:
        print("  BESTIARY EXTRAS WITH NO MONSTER:", sorted(set(extra_unresolved)))
    if missing_sections:
        print("  MISSING SECTIONS:", sorted(missing_sections))
    unused = sorted(set(s["name"] for s in wiki) - used_section_names)
    if unused:
        print("  UNUSED SECTIONS:", unused)
    print(f"  FLAGS WITHOUT A GUIDE ENTRY: {len(unpaired)}")
    for item in unpaired:
        print("     ", item)
    if spells_unplaced:
        print("  BLUE SPELLS WITH NO LOCATION:", spells_unplaced)
    print("  UNMATCHED BESTIARY TEXT:", sorted(unmatched_fragments)[:40])
    print("  NO-BESTIARY LOCATIONS:", [o["name"] for o in out_locations if not o["bestiary"]])
    print(f"  BESTIARY NAMES WITH NO MONSTER ID ({len(unmapped_bestiary)}):", unmapped_bestiary)


if __name__ == "__main__":
    main()
