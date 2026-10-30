"""Parse the saved IGN wiki pages into structured intermediate JSON.

The Location/Contains line is deliberately kept separate from the walk-through
text: the plugin shows "Chest 3 - Drakenvale - Cave" as the heading and the
prose underneath.
"""

import json
import os
import re

from bs4 import BeautifulSoup

WIKI = r"C:\My Web Sites\FFV\www.ign.com\wikis\final-fantasy-v"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "_master")

WORLD_PAGES = [
    (1, "World_1_Chests_and_Items.html"),
    (2, "World_2_Chests_and_Items.html"),
    (3, "World_3_Chests_and_Items.html"),
]


def clean(text):
    return re.sub(r"\s+", " ", text or "").strip()


def parse_chests_page(path, world):
    with open(path, encoding="utf-8", errors="replace") as f:
        soup = BeautifulSoup(f.read(), "lxml")

    sections = []
    current = None
    entry = None

    for node in soup.find_all(["h1", "h2", "ul", "ol", "p", "img"]):
        if node.name == "h1":
            title = clean(node.get_text(" ", strip=True))
            if title.lower().startswith("world "):
                continue
            current = {"name": title, "world": world, "entries": []}
            sections.append(current)
            entry = None
            continue
        if current is None:
            continue
        if node.name == "h2":
            title = clean(node.get_text(" ", strip=True))
            m = re.match(r"^(.*?)\s*-\s*(Chest|Item)\s*(\d+)$", title)
            entry = None
            if m:
                entry = {"label_area": clean(m.group(1)), "kind": m.group(2).lower(),
                         "index": int(m.group(3)), "raw_title": title,
                         "location": "", "contains": "", "guide": [], "images": []}
                current["entries"].append(entry)
            continue
        if entry is None:
            continue
        if node.name == "img":
            alt = clean(node.get("alt") or node.get("title") or "")
            if alt.lower().endswith(".png") and "logo" not in alt.lower():
                # the wiki stores the files with underscores: "Tule_-_Chest_1.png"
                entry["images"].append(alt.replace(" ", "_"))
            continue
        if node.name in ("ul", "ol"):
            for item in (clean(li.get_text(" ", strip=True)) for li in node.find_all("li")):
                low = item.lower()
                # only the first Location/Contains list belongs to the chest; later
                # lists inside the walk-through (left box / right box) must not win
                if low.startswith("location") and not entry["location"]:
                    entry["location"] = clean(re.sub(r"(?i)^location\s*:?\s*", "", item))
                elif low.startswith("contains") and not entry["contains"]:
                    entry["contains"] = clean(re.sub(r"(?i)^contains\s*:?\s*", "", item))
        elif node.name == "p":
            text = clean(node.get_text(" ", strip=True))
            if text and len(text) > 25:
                entry["guide"].append(text)

    return sections


def parse_bestiary(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        soup = BeautifulSoup(f.read(), "lxml")

    rows = []
    for table in soup.find_all("table"):
        headers = [clean(c.get_text(" ", strip=True)) for c in table.find_all("th")]
        if not headers or headers[0].lower() != "image":
            continue
        for tr in table.find_all("tr"):
            cells = tr.find_all("td")
            if len(cells) < 3:
                continue
            name = clean(cells[1].get_text(" ", strip=True))
            m = re.match(r"^(\d+)\s*-\s*(.+)$", name)
            if not m:
                continue
            rows.append({"number": int(m.group(1)), "name": clean(m.group(2)),
                         "locations_raw": clean(cells[2].get_text(" ", strip=True))})
    return rows


def parse_bestiary_headings():
    with open(os.path.join(WIKI, "Bestiary.html"), encoding="utf-8", errors="replace") as f:
        soup = BeautifulSoup(f.read(), "lxml")
    out = []
    for h in soup.find_all("h2"):
        title = clean(h.get_text(" ", strip=True))
        if title.upper() in ("WARNING", "SPOILER WARNING"):
            continue
        # Only the "World 1/2/3 Bestiary Entries" dividers are skipped; the
        # "World Map - ..." entries are real locations.
        if re.match(r"^World \d", title) or title == "Other Bestiary Entries":
            continue
        out.append(title)
    return out


def parse_pianos():
    with open(os.path.join(WIKI, "Side_Quests.html"), encoding="utf-8", errors="replace") as f:
        soup = BeautifulSoup(f.read(), "lxml")
    h3 = soup.find("h3", string=re.compile("Pianos"))
    text = []
    for sib in h3.next_siblings:
        if getattr(sib, "name", None) == "h3":
            break
        if getattr(sib, "name", None) is None:
            continue
        t = clean(sib.get_text(" ", strip=True))
        if t:
            text.append(t)
    blob = " ".join(text)
    pianos = []
    for m in re.finditer(r"Piano\s*#(\d+)\s*:\s*(.+?)(?=\s*Piano\s*#\d+\s*:|$)", blob):
        pianos.append({"number": int(m.group(1)), "where": clean(m.group(2))})
    return pianos


def main():
    sections = []
    for world, page in WORLD_PAGES:
        sections.extend(parse_chests_page(os.path.join(WIKI, page), world))

    bestiary = parse_bestiary(os.path.join(WIKI, "Bestiary.html"))
    headings = parse_bestiary_headings()
    pianos = parse_pianos()

    print("sections:", len(sections), "bestiary rows:", len(bestiary),
          "headings:", len(headings), "pianos:", len(pianos))
    for s in sections[:2]:
        for e in s["entries"][:2]:
            print("  sample:", e["raw_title"], "|", e["location"], "|", e["contains"],
                  "|", (e["guide"][0][:70] if e["guide"] else ""))

    with open(os.path.join(OUT, "wiki_chests.json"), "w", encoding="utf-8") as f:
        json.dump(sections, f, indent=1)
    with open(os.path.join(OUT, "wiki_bestiary.json"), "w", encoding="utf-8") as f:
        json.dump({"rows": bestiary, "headings": headings}, f, indent=1)
    with open(os.path.join(OUT, "wiki_pianos.json"), "w", encoding="utf-8") as f:
        json.dump(pianos, f, indent=1)


if __name__ == "__main__":
    main()
