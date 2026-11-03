using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;

namespace FFVMissableHelper;

internal sealed class GuideEntry
{
    public string Kind { get; set; }          // "chest" or "item"
    public string Label { get; set; }
    public string Item { get; set; }
    public string Where { get; set; }
    public string Guide { get; set; }
    public string Image { get; set; }        // wiki picture name, resolved from the maps folder
    public List<int> Flags { get; set; } = new();
    public bool Late { get; set; }
}

internal sealed class BestiaryEntry
{
    public int N { get; set; }
    public string Name { get; set; }
    public List<int> Monsters { get; set; } = new();
}

internal sealed class BlueEntry
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<string> Enemies { get; set; } = new();
}

internal sealed class PianoEntry
{
    public int Slot { get; set; }
    public string Name { get; set; }
}

internal sealed class GuideLocation
{
    public string Key { get; set; }
    public string Name { get; set; }
    public int World { get; set; }
    public int Order { get; set; }
    public List<int> AreaIds { get; set; } = new();
    public bool Missable { get; set; }
    public string Note { get; set; }
    public List<GuideEntry> Entries { get; set; } = new();
    public List<BestiaryEntry> Bestiary { get; set; } = new();
    public List<BlueEntry> Blue { get; set; } = new();
    public List<PianoEntry> Pianos { get; set; } = new();

    /// <summary>Plain-text warnings for things the helper cannot track (steals,
    /// songs, summons, one-time events). Informational only.</summary>
    public List<string> Missables { get; set; } = new();

    [JsonIgnore] public string Title => $"{World}-{Order:00} {Name}";
    [JsonIgnore] public bool HasAnyContent =>
        Entries.Count > 0 || Bestiary.Count > 0 || Blue.Count > 0 || Pianos.Count > 0 ||
        Missables.Count > 0;
}

internal sealed class GuideFile
{
    public int Format { get; set; }
    public string Source { get; set; }
    public List<GuideLocation> Locations { get; set; } = new();
}

internal sealed class BlueAlias
{
    public int AbilityId { get; set; }
    public int ContentId { get; set; }
    public int SpellId { get; set; }
}

internal sealed class BlueSpellInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int[] Enemies { get; set; }
}

internal sealed class BlueMonsterInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
}

internal sealed class BlueMagicFile
{
    public List<BlueSpellInfo> Spells { get; set; } = new();
    public List<BlueMonsterInfo> Monsters { get; set; } = new();
    public List<BlueAlias> Aliases { get; set; } = new();
}

/// <summary>
/// The guide data. The copy embedded in the DLL is used unless a
/// <c>guide.json</c> override exists next to the plugin.
/// </summary>
internal static class Guide
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static GuideFile Data { get; private set; }
    public static BlueMagicFile BlueMagic { get; private set; }
    public static string SourcePath { get; private set; }
    public static string LoadError { get; private set; }

    /// <summary>monster id -> blue spell ids it can teach.</summary>
    public static Dictionary<int, List<int>> BlueByMonster { get; } = new();

    /// <summary>
    /// Ability id (383..412) -> blue spell id. Only ever consulted for ids that came out of
    /// an <em>ability</em> list.
    /// </summary>
    public static Dictionary<int, int> BlueByAbilityAlias { get; } = new();

    /// <summary>
    /// Content id (649..678) -> blue spell id. Only ever consulted for ids that came out of
    /// the game's owned-<em>magic</em> list.
    ///
    /// The two tables have to stay apart. They overlap numerically with each other and with
    /// ordinary magic: content id 403 is the black magic Bio while ability id 403 is the blue
    /// magic Transfusion, so a single shared table reported Bio, Speed (410) and Slow (411) as
    /// blue spells the player already knew.
    /// </summary>
    public static Dictionary<int, int> BlueByContentAlias { get; } = new();

    /// <summary>The blue spell ids themselves, for a save that stores them raw.</summary>
    public static HashSet<int> BlueSpellIds { get; } = new();

    /// <summary>blue spell id -> the ability id the game files it under.</summary>
    public static Dictionary<int, int> BlueAbilityIdBySpell { get; } = new();

    /// <summary>blue spell id -> the content id the game files it under.</summary>
    public static Dictionary<int, int> BlueContentIdBySpell { get; } = new();

    private static readonly Dictionary<int, string> BlueNames = new();

    /// <summary>The spell's name, or null when the id is not a blue spell we know.</summary>
    public static string BlueName(int spellId) =>
        BlueNames.TryGetValue(spellId, out string name) ? name : null;

    public static IReadOnlyList<GuideLocation> Locations =>
        Data?.Locations ?? (IReadOnlyList<GuideLocation>)Array.Empty<GuideLocation>();

    public static void Load()
    {
        Data = null;
        BlueMagic = null;
        LoadError = null;
        SourcePath = null;
        BlueByMonster.Clear();
        BlueByAbilityAlias.Clear();
        BlueByContentAlias.Clear();
        BlueSpellIds.Clear();
        BlueAbilityIdBySpell.Clear();
        BlueContentIdBySpell.Clear();
        BlueNames.Clear();

        try
        {
            string overridePath = Path.Combine(Paths.PluginPath, "FFVMissableHelper", "guide.json");
            if (File.Exists(overridePath))
            {
                Data = JsonSerializer.Deserialize<GuideFile>(File.ReadAllText(overridePath), Options);
                SourcePath = overridePath;
            }

            if (Data == null)
            {
                Data = ReadEmbedded<GuideFile>("FFVMissableHelper.guide.json");
                SourcePath = "embedded";
            }

            BlueMagic = ReadEmbedded<BlueMagicFile>("FFVMissableHelper.blue_magic.json");

            if (Data == null)
                throw new InvalidOperationException("guide.json could not be loaded.");

            if (BlueMagic != null)
            {
                foreach (var spell in BlueMagic.Spells)
                {
                    BlueSpellIds.Add(spell.Id);
                    BlueNames[spell.Id] = spell.Name;

                    foreach (int monsterId in spell.Enemies ?? Array.Empty<int>())
                    {
                        if (!BlueByMonster.TryGetValue(monsterId, out var list))
                            BlueByMonster[monsterId] = list = new List<int>();
                        if (!list.Contains(spell.Id))
                            list.Add(spell.Id);
                    }
                }

                int abilityOffset = PlayerOffset(BlueMagic.Aliases, alias => alias.AbilityId);
                int contentOffset = PlayerOffset(BlueMagic.Aliases, alias => alias.ContentId);

                foreach (var alias in BlueMagic.Aliases)
                {
                    // The same table also lists the enemy attacks that teach each spell, at
                    // unrelated id offsets. Those ids must never be looked up in a player
                    // list: job abilities share their numbering, so monster attack 210 would
                    // "prove" that Level 2 Old had been learned.
                    if (alias.AbilityId > 0 &&
                        alias.AbilityId - alias.SpellId == abilityOffset &&
                        BlueSpellIds.Contains(alias.SpellId))
                    {
                        BlueByAbilityAlias[alias.AbilityId] = alias.SpellId;
                        BlueAbilityIdBySpell[alias.SpellId] = alias.AbilityId;
                    }

                    if (alias.ContentId > 0 &&
                        alias.ContentId - alias.SpellId == contentOffset &&
                        BlueSpellIds.Contains(alias.SpellId))
                    {
                        BlueByContentAlias[alias.ContentId] = alias.SpellId;
                        BlueContentIdBySpell[alias.SpellId] = alias.ContentId;
                    }
                }
            }

            Plugin.LogSource?.LogInfo(
                $"Guide loaded from {SourcePath}: {Data.Locations.Count} locations, " +
                $"{BlueMagic?.Spells.Count ?? 0} blue spells, " +
                $"{BlueByAbilityAlias.Count} ability and {BlueByContentAlias.Count} content aliases.");
        }
        catch (Exception e)
        {
            LoadError = e.Message;
            Plugin.LogSource?.LogError($"Could not load guide data: {e}");
        }
    }

    /// <summary>
    /// The offset the game uses for the player's own copy of a blue spell in one of the two id
    /// spaces (383 = 205 + 178 for abilities, 649 = 205 + 444 for content).
    ///
    /// It is found by coverage rather than by looking for the smallest difference: the same
    /// table also lists the enemy attacks that teach each spell, and one of those (ability 212,
    /// the enemy version of Level 4 Graviga) sits only three ids above the spell. The player's
    /// block is the one that accounts for every spell, which enemy ids never do.
    /// </summary>
    private static int PlayerOffset(List<BlueAlias> aliases, Func<BlueAlias, int> id)
    {
        var coverage = new Dictionary<int, HashSet<int>>();

        foreach (var alias in aliases)
        {
            int candidate = id(alias);
            if (candidate <= 0 || !BlueSpellIds.Contains(alias.SpellId))
                continue;

            int offset = candidate - alias.SpellId;
            if (offset <= 0)
                continue;

            if (!coverage.TryGetValue(offset, out var spells))
                coverage[offset] = spells = new HashSet<int>();

            spells.Add(alias.SpellId);
        }

        int best = 0;
        int bestCount = 0;

        foreach (var pair in coverage)
        {
            int count = pair.Value.Count;
            if (count > bestCount || (count == bestCount && best != 0 && pair.Key < best))
            {
                best = pair.Key;
                bestCount = count;
            }
        }

        return best;
    }

    public static GuideLocation FindByArea(int areaId)
    {
        foreach (var location in Locations)
        {
            if (location.AreaIds.Contains(areaId))
                return location;
        }

        return null;
    }

    private static T ReadEmbedded<T>(string name) where T : class
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource {name} was not found.");

        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Options);
    }
}
