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

    /// <summary>ability/content alias ids that mean "this blue spell is learned".</summary>
    public static Dictionary<int, int> BlueByAlias { get; } = new();

    public static IReadOnlyList<GuideLocation> Locations =>
        Data?.Locations ?? (IReadOnlyList<GuideLocation>)Array.Empty<GuideLocation>();

    public static void Load()
    {
        Data = null;
        BlueMagic = null;
        LoadError = null;
        SourcePath = null;
        BlueByMonster.Clear();
        BlueByAlias.Clear();

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
                    foreach (int monsterId in spell.Enemies ?? Array.Empty<int>())
                    {
                        if (!BlueByMonster.TryGetValue(monsterId, out var list))
                            BlueByMonster[monsterId] = list = new List<int>();
                        if (!list.Contains(spell.Id))
                            list.Add(spell.Id);
                    }
                }

                foreach (var alias in BlueMagic.Aliases)
                {
                    if (alias.AbilityId > 0)
                        BlueByAlias[alias.AbilityId] = alias.SpellId;
                    if (alias.ContentId > 0)
                        BlueByAlias[alias.ContentId] = alias.SpellId;
                }
            }

            Plugin.LogSource?.LogInfo(
                $"Guide loaded from {SourcePath}: {Data.Locations.Count} locations, " +
                $"{BlueMagic?.Spells.Count ?? 0} blue spells.");
        }
        catch (Exception e)
        {
            LoadError = e.Message;
            Plugin.LogSource?.LogError($"Could not load guide data: {e}");
        }
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
