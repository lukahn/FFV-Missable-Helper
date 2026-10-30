# FFV Missable Helper

> **AI disclosure:** This mod — source code, data pipeline and documentation — was produced with the
> help of an AI assistant (GitHub Copilot in VS Code). The data is derived from the IGN *Final
> Fantasy V* wiki and from the game's own master data, but the in-game menu integration has not
> been verified on every game build. Back up your saves before relying on it.

A BepInEx plugin for **FINAL FANTASY V Pixel Remaster** that adds an **FFV Helper** row to the pause
menu, under *Load*. It lists every location in story order and shows how much of the permanently
missable content you have collected there:

* chests
* hidden items
* bestiary entries
* blue magic spells
* pianos

A location that is completely done gets a ★. Otherwise the row shows an icon for each thing that is
still missing (chest, potion, chocobo, wizard hat, piano). Opening a location shows the counts, and
opening a count lists the exact chests, items, monsters or spells that are left, with the floor and
the walk-through text. The bestiary list shows each missing monster's picture, plus a larger
portrait of the highlighted one.

A last **Notes** category lists the missables the game gives no flag for - steals, drops, songs,
summons, one-time events and point-of-no-return warnings. Notes are informational: they never count
towards a location's total, and a note icon next to a ★ just means "read the notes for this area".

It also shows a small blue box in the top-left corner the first time you walk into an area, so you
can see at a glance whether anything is still outstanding there. The popup can be toggled from the
top of the FFV Helper screen.

## Requirements

- FINAL FANTASY V Pixel Remaster (Steam)
- BepInEx 6 IL2CPP (`6.0.0-be.697` at the time of writing), set up per
  [the usual Pixel Remaster modding guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2788533275)
- The custom `UnityPlayer.dll` required by the Pixel Remaster modding scene
- The interop assemblies BepInEx generates on first launch (`BepInEx/interop`)

Steam Deck / Proton needs the same extra step as the other Pixel Remaster plugins: set
`WINEDLLOVERRIDES="winhttp=n,b" %command%` in the game's launch options, otherwise BepInEx never
starts. See `../FFV-Blue-Mage-Helper/README.md` for the full walk-through.

## Build and install

```powershell
dotnet build -c Release -p:DeployToPlugins=true
```

This copies `FFVMissableHelper.dll` into `BepInEx/plugins`. Use
`-p:GameDir="D:\Games\FINAL FANTASY V PR"` if the game is installed elsewhere.

### One folder to copy

To produce a self-contained install folder:

```powershell
dotnet build -c Release -p:Package=true
```

which creates `build\package\`:

```
build\package\
    FFVMissableHelper.dll
    HOW-TO-INSTALL.txt
    FFVMissableHelper\
        guide.json
        maps\        (301 pictures / stubs, taken from the repository's maps\ folder)
```

Copy the **contents** of `build\package\` into `BepInEx\plugins\` in one go. The folder is rebuilt
from scratch each time, so re-run it after adding or replacing map pictures.

## Using it

1. Open the field menu and move down to **FFV Helper**, directly under *Load*.
2. The first row, **Area popup on entering a location**, toggles the corner popup (the setting is
   saved to the BepInEx config file).
3. Pick a location to see `Chests x/y`, `Items x/y`, `Bestiary x/y`, `Blue magic x/y`,
   `Piano x/y` and, where the game keeps no flag for something, a **Notes** row. Rows with no
   content in that location are hidden. The screen opens inside the pause
   menu's sub-window area - the same slot the Config screen uses - rather than over a dimmed overlay, and
   it borrows the game's own font.
4. Pick a category to see exactly what is left. The walk-through for the highlighted entry is
   shown at the bottom of the screen. For chests and items the selected entry's map or screenshot
   is drawn on the right when a picture is installed for it.
5. Pianos are ticked by hand (Enter on the entry) because the game keeps no accessible "piano
   played" flag. Notes are read-only.

Keyboard: `Up`/`Down` to move, `Enter` to select, `Esc` to go back. Mouse: click a row. Both the
legacy Input Manager and the new Input System are handled.

`*` next to a location name means the area can be left permanently.

## Maps and location pictures

The helper looks for pictures in

```
BepInEx/plugins/FFVMissableHelper/maps/
```

Every file is named **exactly like the picture on the IGN page**, including its underscores
(the wiki stores `Tule_-_Chest_1.png`, not `Tule - Chest 1.png`):

```
Tule_-_Chest_1.png
Castle_Tycoon_-_Item_7.png
Istory_Falls_-_Chest_12.png
```

and there is also one map per area named after the location, for example `Tule.png` or
`Castle_Tycoon.png`. A **zero-byte stub for every one of these (301 files)** is kept in the
repository's `maps/` folder, and the same set is installed into the game folder, so the list is
complete and you only need to replace the files you want to use.

* Fill the stubs in `maps/` in the repository, then run
  `python tools/make_map_stubs.py --install` to copy every real picture into the game folder (and
  create any missing stubs there). A picture saved under an old space-separated name is renamed to
  the wiki name automatically, so nothing is lost when the guide data changes.
* Entry picture wins; if it is missing or still empty, the area map is used.
* Empty files are ignored silently - leaving a stub alone simply means "no picture".
* `.png`, `.jpg`, `.jpeg` and `.webp` are accepted. If you keep the `.png` name but drop a JPG in
  its place, rename it to match the content or save it as PNG.
* Pictures are cached when the helper opens, so changes show up the next time you open it.
* Nothing is downloaded automatically; the maps are entirely yours to supply.

To recreate the stubs after re-extracting the guide data:

```powershell
python tools/make_map_stubs.py             # repository maps/
python tools/make_map_stubs.py --install   # repository maps/ + game folder
```

## How progress is read

Nothing has to be ticked off by hand except pianos.

| Row | Source |
| --- | --- |
| Chests / Items | The game's treasure flags (`DataStorage`, `Category.kTreasureFlag1..4`). Each guide entry carries the flag ids of the box(es) that hold it. |
| Bestiary | `PictureBookSaveManager.SaveData.MonsterDefeats` — the same data the in-game bestiary uses. Monsters are matched to the guide by name. |
| Blue magic | The party's learned abilities, mapped back through the game's blue-spell alias table (the same approach as FFV Blue Mage Helper). |
| Piano | Manual ticks, stored in `BepInEx/config/FFVMissableHelper/ticks.json`. |

Chest and item counts come from the game's own treasure table, so a row's `y` matches what the
achievement counter sees. Guide entries the wiki does not list (the twelve tablet chests in the
Sealed Castle, a few cut-content boxes) still appear as `Chest +1`, `Item +3`, …, so the totals
never disagree with the save.

The state is re-read when something actually happens, not on a timer: when a battle ends, when the
game bumps its chest counter, when an item or ability is added, when the area or map id changes,
and when a save is loaded. While the helper is open it also reads before every repaint, so a chest
opened a moment ago is already ticked.

The row icons are the game's own item icons, resolved through `IconTextUtility.GetIcon`: the potion
(`<IC_DRAG>`), claw (`<IC_CLW>`, used for the bestiary), hat (`<IC_HEAD>`) and harp (`<IC_HRP>`,
used for the pianos). The treasure chest and the completion star have no in-game equivalent, so
those two are drawn by the plugin. Set `Display.UseGameIcons = false` to use the drawn icons for
all six.

Copies of the four game icons are also kept in `assets/game/` and embedded as a fallback, so the
markers still render if the icon atlas cannot be resolved at runtime.

## Configuration

`BepInEx/config/com.lukahn.ffv.missablehelper.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `Display.AreaPopup` | `true` | Show the top-left summary when entering an area. Also toggled from the helper screen. |
| `Display.AreaPopupSeconds` | `10` | How long the popup stays. |
| `Display.UseGameIcons` | `true` | Use the game's item icons for the potion/claw/hat/harp markers. |
| `Display.HostInPauseMenu` | `true` | Draw the helper inside the pause menu's sub-window slot with the game's font. Set to `false` to show it as a standalone overlay instead. |
| `Debug.VerboseLogging` | `false` | Logs the pause-menu hierarchy, the appended menu row, save-state reads and whether each game icon resolved. |

## Overriding the guide text

The guide is embedded in the DLL, but a copy placed at

```
BepInEx/plugins/FFVMissableHelper/guide.json
```

is used instead. It is plain JSON; edit the location names, walk-through text or add entries without
rebuilding. Blue magic and the treasure flags come from `data/blue_magic.json` and the game's master
data respectively.

## Regenerating the data

`tools/` rebuilds `data/guide.json` from the sources:

```powershell
python tools/extract_master.py      # game master CSVs + English messages (UnityPy)
python tools/extract_treasures.py   # every treasure flag -> map content, from the map bundles
python tools/parse_wiki.py          # the saved IGN pages
python tools/build_guide.py         # combines them into data/guide.json
python tools/extract_monster_images.py  # bestiary pictures -> assets/bestiary/
python tools/make_icons.py          # the seven row icons (chest + star + notes + fallbacks)
python tools/make_map_stubs.py --install   # map/screenshot stubs -> maps/ and the game folder
```

The wiki input is a local copy of the IGN guide:

```
C:\My Web Sites\FFV\www.ign.com\wikis\final-fantasy-v\
    World_1_Chests_and_Items.html
    World_2_Chests_and_Items.html
    World_3_Chests_and_Items.html
    Bestiary.html
    Side_Quests.html
```

`tools/locations.py` holds the curated story order — the list of locations, which IGN section feeds
each one, which bestiary headings belong to it and which pianos it contains. `build_guide.py` prints
a reconciliation report (unmatched flags, missing sections, blue spells without a location) every
time it runs.

Three fields there exist because the IGN pages are not complete on their own:

- `missables=[...]` — plain-text warnings for things the game keeps no flag for (steals and drops,
  songs, summons, shop stock, one-time events, point-of-no-return deadlines). They are cross-checked
  against the Steam guide *FFV - Missable & Optional Content* (`sharedfiles/filedetails?id=2651019340`)
  and shown as the **Notes** category. They do not count towards completion.
- `bestiary_extra=[...]` — monster names the IGN bestiary table never lists at all. The Tower of
  Walse trio (Ricard Mage, Wyvern, Pas de Seul), Jackanapes, Pao, Sucker and Octokraken only come
  from the game's own monster table, so they carry no wiki number and no picture.
- `missable=True` — marks areas that can be left for good (Torna Canal, Tower of Walse, Karnak
  Castle, Ronka Ruins, Barrier Tower, Forest of Moore, Castle Exdeath, Fork Tower, ...).

## Legal / data notes

- The **walk-through text** is condensed from the IGN wiki pages the user supplied, for personal
  use. It is not bundled with any IGN map images: those are third-party copyrighted artwork. The
  locations are given as exact floors (`Istory Falls - B2`) plus prose directions instead.
- The **monster pictures** in `assets/bestiary/` are the screenshots from the supplied bestiary
  page, downloaded by `tools/extract_monster_images.py` and scaled to 96 px. They are a local build
  input only (the folder is git-ignored); the plugin simply shows no picture when it is empty.
- The **map pictures** are supplied by the user; no map artwork is bundled. The plugin only ships
  empty placeholders named after the wiki files (`maps/`).
- The **row icons** come from the game itself at runtime (`IconTextUtility`). For personal use the
  four 12 px sprites are also kept in `assets/game/` as an embedded fallback; they are © Square Enix.
  The chest and star are drawn by `tools/make_icons.py`.
- The panel graphics are generated at runtime, the same way FFV Blue Mage Helper does it.

## Known limitations

- **Not yet verified end-to-end in game.** The menu row injection relies on the pause menu being
  data-driven (`CommandMenuData.Contents`) and on intercepting the two state-machine entry points
  that would otherwise act on an unknown command id. If the row does not appear, or the box does not
  grow, turn on `VerboseLogging` and check `BepInEx\LogOutput.log`: the `[Menu]`/`[Layout]` lines
  show which hook was skipped and dump the menu hierarchy.
- Areas whose guide section is missing (the twelve Sealed Castle chests) show unnamed entries.
- A handful of bestiary entries share a name with two game monsters; either one counts as seen.
- Chests/items are matched to the wiki by item name. Where the wiki and the game disagree (for
  example Castle Tycoon's first chest is "Diamond Bell" in the game but "Healing Staff" in the
  guide), the surplus box is shown as an extra entry rather than being guessed.
- The plugin only reads the save. It never changes flags, items or achievements.

## Uninstall

Delete `BepInEx/plugins/FFVMissableHelper.dll` (and optionally
`BepInEx/config/com.lukahn.ffv.missablehelper.cfg` and the `FFVMissableHelper` config folder).
