using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;
using Last.Interpreter;
using Last.Management;

namespace FFVMissableHelper;

/// <summary>
/// Writes progress into the game itself, for the entries the player cannot go back for.
///
/// Every write uses the same storage the game uses, so the save, the menus and the picture
/// book all agree afterwards:
///   chest / item  -> the treasure flag bitmap (DataStorage, Category.kTreasureFlag1..4)
///   item contents -> OwnedItemClient.AddOwnedItem/RemoveOwnedItem, or gil for coin chests
///   bestiary      -> PictureBookSaveManager.Registration(monsterId, count)
///
/// The item client is not reachable from a static accessor, so it is captured from the
/// game's own calls (see ProgressEvents): the first item the save loads hands us the live
/// instance, which is exactly the object the game mutates.
/// </summary>
internal static class Unlock
{
    private const int ScenarioCategoryBase = 0;   // DataStorage.Category.kScenarioFlag1
    private const int TreasureCategoryBase = 4;   // DataStorage.Category.kTreasureFlag1

    private static Dictionary<string, int> _itemIds;
    private static bool _itemIdsFailed;

    /// <summary>The live OwnedItemClient, captured from the game's own item calls.</summary>
    internal static OwnedItemClient Items { get; set; }

    /// <summary>
    /// The live user-data client, captured from the game's own magic calls. Blue magic can
    /// only be added or removed through it, and there is no static accessor for it.
    /// </summary>
    internal static UserDataClient UserData { get; set; }

    // ------------------------------------------------------------------- chests
    /// <summary>Sets or clears the game's treasure flag, i.e. marks the chest as opened.</summary>
    public static bool SetOpened(GuideEntry entry, bool opened)
    {
        if (entry?.Flags == null || entry.Flags.Count == 0)
            return false;

        try
        {
            var storage = DataStorage.Instance;
            if (storage == null)
                return false;

            foreach (int flag in entry.Flags)
            {
                int segment = flag / 256;
                int index = flag % 256;
                if (segment < 0 || segment > 3)
                    continue;

                storage.Set((DataStorage.Category)(TreasureCategoryBase + segment), index, opened ? 1 : 0);
            }

            Plugin.Trace($"unlock: {(opened ? "opened" : "reopened")} {entry.Item} (flags {string.Join(",", entry.Flags)})");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not set the treasure flag: {e.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------- blue magic
    /// <summary>
    /// Teaches or forgets a blue spell in the save, so the game's own list and the helper
    /// agree.
    ///
    /// Blue magic is stored twice: as owned magic under a content id (649..678) and as an
    /// ability (383..412) for the Blue command. Both are written, because the menus read
    /// different ones. Nothing happens when the game never handed us its client, which is the
    /// case until the player learns their first spell in this session.
    /// </summary>
    public static bool SetBlueLearned(BlueEntry entry, bool learned)
    {
        if (entry == null)
            return false;

        var client = UserData;
        if (client == null)
            return false;

        try
        {
            var userData = UserDataManager.Instance();
            if (userData == null)
                return false;

            int contentId = 0;
            if (Guide.BlueContentIdBySpell.TryGetValue(entry.Id, out int content))
                contentId = content;

            int abilityId = 0;
            if (Guide.BlueAbilityIdBySpell.TryGetValue(entry.Id, out int ability))
                abilityId = ability;

            if (contentId > 0)
            {
                if (learned)
                    client.AddOwnedMagic(contentId);
                else
                    client.RemoveOwnedMagic(contentId);
            }

            // keep the ability list in step: the Blue command reads that one
            var abilities = userData.LearnedAbilityList;
            if (abilities != null && abilityId > 0)
            {
                if (learned)
                    abilities.Add(abilityId);
                else
                    abilities.Remove(abilityId);
            }

            Plugin.Trace(
                $"unlock: blue {entry.Name} -> {(learned ? "learned" : "forgotten")} " +
                $"(content {contentId}, ability {abilityId})");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not set blue magic {entry.Name}: {e.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ pianos
    /// <summary>Sets or clears the game's own "this piano has been played" flag.</summary>
    public static bool SetPianoPlayed(GuideLocation location, PianoEntry piano, bool played)
    {
        if (piano == null)
            return false;

        try
        {
            int flag = Progress.PianoFlag(piano);
            int segment = flag / 256;
            int index = flag % 256;
            if (segment < 0 || segment > 3)
                return false;

            var storage = DataStorage.Instance;
            if (storage == null)
                return false;

            storage.Set((DataStorage.Category)(ScenarioCategoryBase + segment), index, played ? 1 : 0);

            // drop a hand tick for the same piano, or the row would stay ticked
            if (!played && location != null)
                Progress.ClearManual(Progress.ManualKey(location, piano));

            Plugin.Trace($"unlock: piano {piano.Name} (slot {piano.Slot}, flag {flag}) -> {(played ? 1 : 0)}");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not set piano flag: {e.Message}");
            return false;
        }
    }

    // -------------------------------------------------------------------- items
    /// <summary>Adds or removes the contents of a chest / item entry.</summary>
    public static bool GrantItem(GuideEntry entry, bool grant)
    {
        if (entry == null)
            return false;

        int gil = GoldFor(entry.Item);
        if (gil > 0)
            return SetGil(gil, grant);

        int contentId = ContentId(entry.Item);
        if (contentId <= 0)
        {
            Plugin.Trace($"unlock: no content id for '{entry.Item}', flag only");
            return false;
        }

        var client = Items;
        if (client == null)
        {
            Plugin.LogSource?.LogWarning("[Unlock] the item client is not known yet; open a save first.");
            return false;
        }

        try
        {
            if (grant)
                client.AddOwnedItem(contentId, 1);
            else
                client.RemoveOwnedItem(contentId, 1);

            Plugin.Trace($"unlock: {(grant ? "added" : "removed")} item {contentId} ({entry.Item})");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not change item {contentId}: {e.Message}");
            return false;
        }
    }

    private static bool SetGil(int amount, bool grant)
    {
        var client = Items;
        if (client == null)
            return false;

        try
        {
            if (grant)
                client.AddGil(amount);
            else
                client.RemoveGil(amount);

            Plugin.Trace($"unlock: {(grant ? "added" : "removed")} {amount} gil");
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not change gil: {e.Message}");
            return false;
        }
    }

    /// <summary>'5,000 gil' -> 5000; 0 when the name is not gil.</summary>
    private static int GoldFor(string item)
    {
        if (string.IsNullOrEmpty(item))
            return 0;

        string text = item.Trim();
        if (!text.EndsWith("gil", StringComparison.OrdinalIgnoreCase))
            return 0;

        text = text.Substring(0, text.Length - 3).Replace(",", "").Trim();
        return int.TryParse(text, out int value) ? value : 0;
    }

    /// <summary>The game's content id for a guide item name, or 0 when unknown.</summary>
    public static int ContentId(string item)
    {
        var ids = ItemIds();
        if (ids == null || string.IsNullOrEmpty(item))
            return 0;

        return ids.TryGetValue(Normalise(item), out int id) ? id : 0;
    }

    private static string Normalise(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        bool space = false;

        foreach (char c in text)
        {
            if (c == '<')
            {
                space = true;      // icon tags are not part of the name
                continue;
            }

            if (c == '>')
                continue;

            if (char.IsWhiteSpace(c) || space)
            {
                if (builder.Length > 0)
                    builder.Append(' ');

                space = false;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c == '\u2019' ? '\'' : c));
        }

        return builder.ToString().Trim();
    }

    private static Dictionary<string, int> ItemIds()
    {
        if (_itemIds != null || _itemIdsFailed)
            return _itemIds;

        try
        {
            string overridePath = Path.Combine(
                Paths.PluginPath, "FFVMissableHelper", "items.json");

            string json = File.Exists(overridePath)
                ? File.ReadAllText(overridePath)
                : ReadEmbedded("FFVMissableHelper.items.json");

            if (json == null)
            {
                _itemIdsFailed = true;
                return null;
            }

            _itemIds = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return _itemIds;
        }
        catch (Exception e)
        {
            _itemIdsFailed = true;
            Plugin.LogSource?.LogWarning($"[Unlock] could not read items.json: {e.Message}");
            return null;
        }
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(Unlock).Assembly.GetManifestResourceStream(name);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ----------------------------------------------------------------- bestiary
    /// <summary>
    /// Records (or un-records) a monster in the picture book.
    ///
    /// Registration() only ever adds, so un-ticking cannot subtract: the entry has to be
    /// dropped from the defeat table outright, and the picture book re-saved. A zero count
    /// is not enough - the key still counts as registered.
    ///
    /// Numbers the player earned themselves are stored in bestiary_backup.tsv, so a count
    /// that the tool created can always be told apart from one that was really there.
    /// </summary>
    public static bool SetMonsterSeen(BestiaryEntry entry, bool seen)
    {
        if (entry?.Monsters == null || entry.Monsters.Count == 0)
        {
            Plugin.Trace("unlock: bestiary entry has no monster ids");
            return false;
        }

        try
        {
            var manager = PictureBookSaveManager.Instance();
            if (manager == null)
            {
                Plugin.Trace("unlock: no picture book manager");
                return false;
            }

            var save = manager.SaveData;
            if (save == null)
            {
                Plugin.Trace("unlock: picture book has no save data");
                return false;
            }

            var defeats = save.MonsterDefeats;
            if (defeats == null)
            {
                Plugin.Trace("unlock: picture book has no defeat table");
                return false;
            }

            var backup = Backup();
            bool removed = false;

            foreach (int monsterId in entry.Monsters)
            {
                int before = defeats.ContainsKey(monsterId) ? defeats[monsterId] : 0;

                if (seen)
                {
                    backup[monsterId] = before;
                    manager.Registration(monsterId, 1);

                    if (!defeats.ContainsKey(monsterId))
                        defeats[monsterId] = 1;
                }
                else
                {
                    backup.Remove(monsterId);

                    // an entry the picture book still holds keeps the monster registered even
                    // at zero, so the key itself has to go
                    if (defeats.ContainsKey(monsterId))
                        defeats.Remove(monsterId);

                    removed = true;
                }

                int after = defeats.ContainsKey(monsterId) ? defeats[monsterId] : 0;
                Plugin.Trace($"unlock: bestiary {entry.Name} id {monsterId} " +
                             $"{(seen ? "recorded" : "cleared")} ({before} -> {after})");
            }

            if (removed)
            {
                // UpdateDefeatCount() enumerates the table it is handed and writes into the
                // same live dictionary, so calling it with SaveData.MonsterDefeats throws
                // "Collection was modified". The table is already live; just persist it.
                manager.Save();

                foreach (int monsterId in entry.Monsters)
                {
                    if (defeats.ContainsKey(monsterId))
                        Plugin.Trace($"unlock: id {monsterId} came back with {defeats[monsterId]}");
                }
            }

            SaveBackup(backup);
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not change the picture book: {e.Message}");
            Plugin.Trace($"unlock: picture book threw {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    // ------------------------------------------------------ bestiary backup file
    private static Dictionary<int, int> _backup;

    private static string BackupPath =>
        Path.Combine(Paths.ConfigPath, "FFVMissableHelper", "bestiary_backup.tsv");

    private static Dictionary<int, int> Backup()
    {
        if (_backup != null)
            return _backup;

        _backup = new Dictionary<int, int>();

        try
        {
            if (!File.Exists(BackupPath))
                return _backup;

            foreach (string line in File.ReadAllLines(BackupPath))
            {
                string[] parts = line.Split('\t');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int id) &&
                    int.TryParse(parts[1], out int count))
                {
                    _backup[id] = count;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not read the bestiary backup: {e.Message}");
        }

        return _backup;
    }

    private static void SaveBackup(Dictionary<int, int> backup)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));

            var lines = new List<string>(backup.Count);
            foreach (var pair in backup)
                lines.Add($"{pair.Key}\t{pair.Value}");

            File.WriteAllLines(BackupPath, lines);
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Unlock] could not write the bestiary backup: {e.Message}");
        }
    }
}
