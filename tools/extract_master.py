"""Dump the game's master-data CSVs and English message table into tools/_master.

Run:  python tools/extract_master.py
The extracted files are intermediate build inputs for tools/build_guide.py and are
not shipped with the plugin.
"""

import os
import re
import UnityPy

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY V PR"
AA = os.path.join(GAME, "FINAL FANTASY V_Data", "StreamingAssets", "aa", "StandaloneWindows64")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_master")

MASTER_BUNDLE = "master_assets_all_2c3f5c5e11072535b41fcc3a35ad8aad.bundle"
MESSAGE_BUNDLE = "message_assets_all_19c0f9906e0e77523b9e0869fef6af3c.bundle"


def dump_bundle(bundle_name, predicate, out_dir):
    path = os.path.join(AA, bundle_name)
    if not os.path.exists(path):
        raise SystemExit("missing bundle: " + path)
    env = UnityPy.load(path)
    written = 0
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        data = obj.read()
        name = getattr(data, "m_Name", "?")
        if not predicate(name):
            continue
        script = getattr(data, "m_Script", b"")
        raw = script.encode("utf-8", "replace") if isinstance(script, str) else bytes(script)
        with open(os.path.join(out_dir, name + ".txt"), "wb") as f:
            f.write(raw)
        written += 1
    return written


def main():
    os.makedirs(OUT, exist_ok=True)
    n = dump_bundle(MASTER_BUNDLE, lambda name: True, OUT)
    print("master text assets:", n)

    def is_english(name):
        return bool(re.search(r"_(en|us)$", name)) or name in ("etc_text_en",)

    n = dump_bundle(MESSAGE_BUNDLE, is_english, OUT)
    print("english message assets:", n)


if __name__ == "__main__":
    main()
