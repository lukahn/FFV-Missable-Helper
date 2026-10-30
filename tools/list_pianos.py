import json
import os

path = os.path.join(os.path.dirname(__file__), "..", "data", "guide.json")
data = json.load(open(path, encoding="utf-8"))

n = 0
for loc in data["locations"]:
    for entry in loc.get("entries", []):
        if entry.get("category") == "piano":
            print(f"slot {n}  order {loc['order']:>2}  {loc['name']:<22} {entry['name']}")
            n += 1
