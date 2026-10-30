"""Curated story order for the FFV Helper location list.

Each entry ties together:
  name        display name
  area_ids    game `area` ids that hold this location's treasure flags
  chests      IGN chest/item section titles to merge into this location
  bestiary    IGN bestiary heading names whose monsters belong here
  pianos      piano numbers from the Side Quests page
  missable    true when the area can be permanently left behind
  note        optional warning shown in the detail screen subtitle
  missables   optional list of plain-text warnings shown as a "Notes" category;
              they are informational and do not count towards completion
  bestiary_extra  monster names the wiki table never lists (they are resolved by name)

The order is the order a player reaches these places.
Keep this file in sync with the IGN pages listed in README.md.
"""

LOCATIONS = [
    # ---------------------------------------------------------------- World 1
    dict(key="tycoon_meteorite", name="Tycoon Meteorite", world=1, area_ids=[21],
         chests=["Tycoon Meteorite Chests"], bestiary=["Tycoon Meteorite"], missable=True),
    dict(key="cave", name="Cave", world=1, area_ids=[81],
         chests=["Cave Chests"], bestiary=[]),
    dict(key="pirates_hideout", name="Pirates' Hideout", world=1, area_ids=[6],
         chests=["Pirates' Hideout Chests"], bestiary=["Pirates' Hideout"], missable=True),
    dict(key="wm_tycoon", name="World Map - Tycoon", world=1, area_ids=[1],
         chests=[], bestiary=["World Map - Tycoon"]),
    dict(key="wind_shrine", name="Wind Shrine", world=1, area_ids=[8],
         chests=["Wind Shrine Chests"], bestiary=["Wind Shrine"], missable=True),
    dict(key="tule", name="Tule", world=1, area_ids=[9],
         chests=["Tule Items & Chests", "Tule Chests"], bestiary=[], pianos=[1]),
    dict(key="torna_canal", name="Torna Canal", world=1, area_ids=[11],
         chests=[], bestiary=[], missable=True,
         bestiary_extra=["Sucker", "Octokraken"],
         missables=["One-time area on the boat trip north: the two battles here are the "
                    "only chance to record Sucker and Octokraken."]),
    dict(key="ship_graveyard", name="Ship Graveyard", world=1, area_ids=[13],
         chests=["Ship Graveyard Chests"], bestiary=["Ship Graveyard"], missable=True,
         missables=["Leave the area and come back after clearing it, then win one more "
                    "battle: the achievement check is known to miss the first pass."]),
    dict(key="carwen", name="Carwen", world=1, area_ids=[14],
         chests=["Carwen Items"], bestiary=[], pianos=[2],
         missables=["Only three of the town's four items are reachable in World 1.",
                    "Later on, Mighty Guard is taught by the Stingray in the sea battle "
                    "off the coast, and Syldra joins straight after it."]),
    dict(key="wm_walse", name="World Map - Walse", world=1, area_ids=[1],
         chests=[], bestiary=["World Map - Walse", "World Map - Tower of Walse"]),
    dict(key="north_mountain", name="North Mountain", world=1, area_ids=[15],
         chests=["North Mountain Chests"], bestiary=["North Mountain"],
         missables=["In the Magissa fight, hold back until she summons Forza and beat it "
                    "before the battle ends."]),
    dict(key="castle_tycoon", name="Castle Tycoon", world=1, area_ids=[17],
         chests=["Castle Tycoon Items & Chests"], bestiary=[], missable=True),
    dict(key="castle_walse", name="Castle Walse", world=1, area_ids=[19],
         chests=["Castle Walse Items and Chests"],
         bestiary=["Castle Walse", "Castle Walse - Basement"],
         bestiary_extra=["Jackanapes"],
         missables=["Do not free Lone Wolf: he empties the castle chests.",
                    "Shiva waits in the Water Tower behind the waterfall."]),
    dict(key="tower_of_walse", name="Tower of Walse", world=1, area_ids=[71],
         chests=["Tower of Walse Chests"], bestiary=["Tower of Walse"], missable=True,
         bestiary_extra=["Ricard Mage", "Wyvern", "Pas de Seul"],
         missables=["One-time area: four chests, Pond's Chorus from the Elf Toad, and three "
                    "bestiary entries that appear nowhere else.",
                    "In the merged world the sunken tower holds the Mime job."]),
    dict(key="walse", name="Walse", world=1, area_ids=[16],
         chests=["Walse Chests"], bestiary=[],
         missables=["Buy the Iron Shield and the Kenpo Gi from the shop while you are here.",
                    "Chocobo, Sylph and Remora are all picked up around Walse."]),
    dict(key="fire_powered_ship", name="Fire-Powered Ship", world=1, area_ids=[26],
         chests=["Fire-Powered Ship Chests"], bestiary=["Fire-Powered Ship"], missable=True),
    dict(key="karnak_castle", name="Karnak Castle", world=1, area_ids=[24],
         chests=["Karnak Castle Chests"], bestiary=["Karnak Castle"], missable=True,
         missables=["Ten-minute timer and 15 chests: Aera is taught by Gigas, Death Claw by "
                    "the Iron Claw. Beat the Cur Nakks before the Sergeant transforms."]),
    dict(key="karnak", name="Karnak", world=1, area_ids=[25],
         chests=["Karnak Chests"], bestiary=[], pianos=[3]),
    dict(key="wm_karnak", name="World Map - Karnak", world=1, area_ids=[1],
         chests=[], bestiary=["World Map - Karnak", "World Map - Karnak West"],
         missables=["Titan is summoned from the Karnak meteorite: do it before the world "
                    "changes."]),
    dict(key="library_ancients", name="Library of the Ancients", world=1, area_ids=[28],
         chests=["Library of the Ancients Chests", "Library of the Ancients Items"],
         bestiary=["Library of the Ancients"], missable=True,
         missables=["Ifrit is in the basement; Byblos teaches Magic Hammer.",
                    "Mana's Paean sits on the top floor and must be taken before the pyramid "
                    "boss falls: the library is sealed afterwards."]),
    dict(key="jachol_cave", name="Jachol Cave", world=1, area_ids=[78],
         chests=["Jachol Cave Chests"], bestiary=[], pianos=[4]),
    dict(key="catapult", name="Catapult", world=1, area_ids=[33],
         chests=["Catapult Chests"], bestiary=[]),
    dict(key="ronka_ruins", name="Ronka Ruins", world=1, area_ids=[34],
         chests=["Ronka Ruins Chests"], bestiary=["Ronka Ruins"], missable=True,
         missables=["One-time area: twelve chests, and White Wind, 1000 Needles and "
                    "Level 4 Graviga are taught by its monsters."]),
    dict(key="wm_jachol", name="World Map - Jachol, Istory & Crescent Islands", world=1, area_ids=[1],
         chests=[], bestiary=["World Map - Jachol, Istory & Crescent Islands", "Desert Enemies"],
         missables=["Istory Village: the Toad spell, Romeo's Ballad and Ramuh.",
                    "Lix Village: the Alluring Air song."]),
    dict(key="crescent", name="Crescent", world=1, area_ids=[31],
         chests=["Crescent Items"], bestiary=[], pianos=[5]),

    # ---------------------------------------------------------------- World 2
    dict(key="castle_exdeath_1", name="Castle Exdeath (First Visit)", world=2, area_ids=[40],
         chests=["Castle Exdeath Chests"], bestiary=["Castle Exdeath (First Visit)"],
         missable=True, bestiary_extra=["Pao"],
         missables=["Pao and the Abductor only appear here.",
                    "The Spear is stolen from a Shell Bear."]),
    dict(key="big_bridge", name="Big Bridge", world=2, area_ids=[54],
         chests=[], bestiary=["Big Bridge"],
         missables=["Gilgamesh's first fight: four bestiary entries appear on the bridge "
                    "and nowhere else."]),
    dict(key="sealed_castle", name="Sealed Castle (Kuza)", world=2, area_ids=[53, 55],
         chests=[], bestiary=["Sealed Castle"],
         note="The Sealed Room's twelve chests open as you collect tablets.",
         missables=["Each tablet unlocks more of the Sealed Room: Holy and Flare, Meteor, "
                    "Leviathan and Bahamut all come from the tablet quests."]),
    dict(key="underground_waterway", name="Underground Waterway", world=2, area_ids=[43],
         chests=["Underground Waterway Chests"], bestiary=["Underground Waterway"]),
    dict(key="wm_gloceana", name="World Map - Gloceana", world=2, area_ids=[1],
         chests=[], bestiary=["World Map - Gloceana", "Gloceana Desert"]),
    dict(key="gil_cave", name="Gil Cave", world=2, area_ids=[61],
         chests=[], bestiary=["Gil Cave"]),
    dict(key="wm_bal", name="World Map - Bal", world=2, area_ids=[1],
         chests=[], bestiary=["World Map - Bal"]),
    dict(key="castle_bal", name="Castle of Bal", world=2, area_ids=[45],
         chests=["Castle of Bal Chests & Items"], bestiary=["Castle of Bal"],
         missables=["In the merged world, Odin waits in the basement reached from Jachol Cave."]),
    dict(key="quelb", name="Quelb", world=2, area_ids=[49],
         chests=["Quelb Items"], bestiary=[],
         missables=["Requiem is learned from the wolves around Quelb."]),
    dict(key="moogle_village", name="Moogle Village", world=2, area_ids=[44],
         chests=["Moogle Village Chests"], bestiary=[]),
    dict(key="drakenvale", name="Drakenvale", world=2, area_ids=[50],
         chests=["Drakenvale Chests and Items"], bestiary=[],
         missables=["Golem is found in Drakenvale: summon it before the world changes."]),
    dict(key="surgate_castle", name="Surgate Castle", world=2, area_ids=[52],
         chests=["Surgate Castle Items & Chests"], bestiary=[],
         missables=["The Swift Song is found here."]),
    dict(key="xezat_fleet", name="Xezat's Fleet", world=2, area_ids=[56],
         chests=[], bestiary=["Xezat's Fleet"],
         missables=["Let Gilgamesh summon Enkidu before you finish him.",
                    "Genji Gloves are stolen from Gilgamesh."]),
    dict(key="barrier_tower", name="Barrier Tower", world=2, area_ids=[57],
         chests=["Barrier Tower Chests"], bestiary=["Barrier Tower", "Barrier Tower (Chests)"],
         missable=True,
         missables=["One-time area: Level 2 Old from a Level Tricker, Level 3 Flare from the "
                    "Red Dragon, Time Slip from a Traveler, and the Gold Hairpin."]),
    dict(key="ghido_cave", name="Ghido's Cave", world=2, area_ids=[51],
         chests=[], bestiary=["Ghido's Cave"]),
    dict(key="wm_moore", name="World Map - Moore", world=2, area_ids=[1],
         chests=[], bestiary=["World Map - Moore"]),
    dict(key="moore", name="Moore", world=2, area_ids=[59],
         chests=["Moore Items"], bestiary=[], pianos=[7]),
    dict(key="forest_of_moore", name="Forest of Moore", world=2, area_ids=[79],
         chests=["Forest of Moore Chests"], bestiary=["Forest of Moore"], missable=True,
         missables=["Twelve chests split nine before the tree changes, one during it and two "
                    "after.",
                    "Lilliputian Lyric is taught by a Mini Magician."]),
    dict(key="castle_exdeath_2", name="Castle Exdeath (Second Visit)", world=2,
         area_ids=[40], chests=[], bestiary=[], missable=True,
         missables=["Point of no return: the castle is gone once you leave it.",
                    "Power Staff (Black Warlock drop), Genji Helm (steal), Excalipoor and "
                    "Carbuncle are all inside.",
                    "Bestiary 030, 071, 082, 121, 123-134, 158, 255 and 271-273 must already "
                    "be recorded."]),

    # ---------------------------------------------------------------- World 3
    dict(key="pyramid_of_moore", name="Pyramid of Moore", world=3, area_ids=[65],
         chests=["Pyramid of Moore Chests"], bestiary=["Pyramid of Moore"],
         missables=["Mana's Paean is on the Library of the Ancients' top floor: collect it "
                    "before the pyramid boss dies or it is lost."]),
    dict(key="island_shrine", name="Island Shrine", world=3, area_ids=[62],
         chests=["Island Shrine Chests"], bestiary=["Island Shrine"],
         missables=["The Damned teaches Doom here."]),
    dict(key="fork_tower", name="Fork Tower", world=3, area_ids=[66, 67, 68],
         chests=["Fork Tower Chests"], bestiary=["Fork Tower"], missable=True,
         missables=["Four chests, two on each side.",
                    "Kornago Gourd and the Blitz Whip are stolen from enemies inside; the "
                    "magic side holds bestiary 162-164 and the physical side 165-167."]),
    dict(key="phantom_village", name="Phantom Village", world=3, area_ids=[73],
         chests=["Phantom Village Items"], bestiary=[], pianos=[8],
         missables=["The piano is hidden behind a false wall, and the village keeps secret "
                    "merchants.",
                    "Playing all eight pianos finishes the song list: seven gives Sinewy Etude, "
                    "the eighth adds Hero's Rime."]),
    dict(key="regole", name="Regole", world=3, area_ids=[41],
         chests=["Regole Items"], bestiary=[], pianos=[6]),
    dict(key="great_sea_trench", name="Great Sea Trench", world=3, area_ids=[46],
         chests=["Great Sea Trench Chests"], bestiary=["Great Sea Trench"]),
    dict(key="istory_falls", name="Istory Falls", world=3, area_ids=[69],
         chests=["Istory Falls Chests"], bestiary=["Istory Falls", "Istory Cave"]),
    dict(key="wm_istory_regole", name="World Map - Istory & Regole", world=3, area_ids=[1],
         chests=[], bestiary=["World Map - Istory & Regole"]),
    dict(key="phoenix_tower", name="Phoenix Tower", world=3, area_ids=[72],
         chests=["Phoenix Tower Items"], bestiary=["Phoenix Tower"],
         missables=["On the way up, refuse to harm the dragon: Phoenix is the reward."]),
    dict(key="rift_1", name="Interdimensional Rift - Part 1", world=3, area_ids=[74],
         chests=["Interdimensional Rift Chests - Part 1"], bestiary=["Interdimensional Rift"],
         missables=["Tinklebell drops from Twintania; Titan's Gloves and Thief's Gloves are "
                    "stolen here, along with the Elven Mantle, Genji Shield and Genji Armor."]),
    dict(key="rift_2", name="The Rift", world=3, area_ids=[85],
         chests=["The Rift Chests"],
         missables=["Optional fights: Magic Pot (feed it an Elixir), Mover, Omega and "
                    "Shinryu."]),
    dict(key="rift_3", name="Interdimensional Rift - Part 2", world=3, area_ids=[74],
         chests=["Interdimensional Rift Chests - Part 2"]),
]


# Names used by the IGN pages that differ from the game's own area names.
AREA_ALIASES = {
    "cave": 81,
    "tower of walse": 71,
    "forest of moore": 79,
    "interdimensional rift": 74,
    "the rift": 85,
    "sealed castle": 53,
    "sealed room": 55,
}
