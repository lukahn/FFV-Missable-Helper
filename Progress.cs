using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using Last.Interpreter;
using Last.Management;

namespace FFVMissableHelper;

/// <summary>
/// Reads the live save state and keeps the handful of things the game does not
/// expose as a small JSON file (hand ticks for entries with no flag).
/// </summary>
internal static class Progress
{
    private const int ScenarioCategoryBase = 0;   // Category.kScenarioFlag1
    private const int TreasureCategoryBase = 4;   // Category.kTreasureFlag1

    private static readonly object FileLock = new();
    private static Dictionary<string, int> _manual = new();

    private static HashSet<int> _seenMonsters = new();
    private static HashSet<int> _learnedBlue = new();
    private static bool _dirty = true;
    private static float _nextRefresh;
    private static float _nextManualSave;
    private static string _lastReason;

    public static string ManualPath =>
        Path.Combine(Paths.ConfigPath, "FFVMissableHelper", "ticks.json");

    // ------------------------------------------------------------------ load
    public static void LoadManual()
    {
        try
        {
            lock (FileLock)
            {
                if (File.Exists(ManualPath))
                {
                    _manual = JsonSerializer.Deserialize<Dictionary<string, int>>(
                        File.ReadAllText(ManualPath)) ?? new Dictionary<string, int>();
                }
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not read {ManualPath}: {e.Message}");
            _manual = new Dictionary<string, int>();
        }
    }

    public static void SaveManual()
    {
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManualPath) ?? Paths.ConfigPath);
                File.WriteAllText(ManualPath, JsonSerializer.Serialize(_manual,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not write {ManualPath}: {e.Message}");
        }
    }

    // --------------------------------------------------------------- refresh
    /// <summary>
    /// Marks the cached state stale. Called from the game events that can change
    /// it (battle end, chest opened, item obtained, area change, save load).
    /// </summary>
    public static void Invalidate(string reason = null)
    {
        _dirty = true;
        _lastReason = reason;

        if (Plugin.Verbose.Value && reason != null)
            Plugin.LogSource?.LogInfo($"[Progress] stale after {reason}");
    }

    /// <summary>
    /// Re-reads the save state. Cheap when nothing has happened: the cached copy
    /// is only rebuilt after <see cref="Invalidate"/> or a forced read.
    /// </summary>
    public static void Refresh(bool force = false)
    {
        // implicit refreshes can be switched off; an explicit read (opening the window)
        // always happens, otherwise the helper would show nothing at all
        if (!force && !Plugin.ProgressRefreshEnabled.Value)
            return;

        if (force)
            _dirty = true;

        if (!_dirty)
            return;

        float now = UnityEngine.Time.unscaledTime;
        if (!force && now < _nextRefresh)
            return;                       // picked up on the next call

        _dirty = false;
        _nextRefresh = now + 0.2f;

        RefreshBestiary();
        RefreshBlueMagic();

        if (Plugin.Verbose.Value)
            Plugin.LogSource?.LogInfo($"[Progress] re-read ({_lastReason ?? "requested"})");
    }

    private static void RefreshBestiary()
    {
        var seen = new HashSet<int>();
        try
        {
            var save = PictureBookSaveManager.Instance()?.SaveData;
            var defeats = save?.MonsterDefeats;
            if (defeats != null)
            {
                foreach (var pair in defeats)
                {
                    if (pair.Value > 0)
                        seen.Add(pair.Key);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not read the picture book: {e.Message}");
        }

        _seenMonsters = seen;
    }

    private static void RefreshBlueMagic()
    {
        // Two id namespaces come out of the save, and they overlap numerically. Ability ids
        // (383..412 hold the blue spells) and content ids (649..678) must never be resolved
        // through each other's table: content 403 is the black magic Bio, ability 403 is the
        // blue magic Transfusion, so one shared table claimed Bio, Speed and Slow as blue
        // magic the player already had.
        var abilityIds = new HashSet<int>();
        var contentIds = new HashSet<int>();

        try
        {
            var userData = UserDataManager.Instance();
            if (userData != null)
            {
                AddRange(abilityIds, userData.LearnedAbilityList);

                // the owned *magic* list is keyed by content id
                AddRange(contentIds, userData.OwnedMagicList);

                var characters = userData.OwnedCharacterList;
                if (characters != null)
                {
                    foreach (var character in characters)
                    {
                        if (character == null)
                            continue;

                        // the "learning" list is a job-progress list keyed by content id (the
                        // learning table is), so it is read in the content namespace only:
                        // blue magic is never learned through job progress
                        AddRange(contentIds, character.LearningAbilitys);

                        var abilities = character.OwnedAbilityList;
                        if (abilities == null)
                            continue;

                        foreach (var owned in abilities)
                        {
                            try
                            {
                                int id = owned?.saveData?.abilityId ?? 0;
                                if (id > 0)
                                    abilityIds.Add(id);

                                int contentId = owned?.saveData?.contentId ?? 0;
                                if (contentId > 0)
                                    contentIds.Add(contentId);
                            }
                            catch
                            {
                                // Ignore entries that have not been resolved yet.
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not read learned abilities: {e.Message}");
        }

        var learned = new HashSet<int>();

        foreach (int id in abilityIds)
        {
            if (Guide.BlueSpellIds.Contains(id))
                learned.Add(id);
            else if (Guide.BlueByAbilityAlias.TryGetValue(id, out int spellId))
                learned.Add(spellId);
        }

        foreach (int id in contentIds)
        {
            if (Guide.BlueByContentAlias.TryGetValue(id, out int spellId))
                learned.Add(spellId);
            else if (Guide.BlueSpellIds.Contains(id))
                learned.Add(id);
        }

        _learnedBlue = learned;

        if (Plugin.Verbose.Value)
        {
            var names = new List<string>();
            foreach (int id in learned)
                names.Add(Guide.BlueName(id) ?? id.ToString());
            names.Sort(StringComparer.OrdinalIgnoreCase);

            Plugin.Trace($"blue: learned ({names.Count}) [{string.Join(", ", names)}] " +
                         $"from {abilityIds.Count} ability ids and {contentIds.Count} content ids");
        }
    }

    private static void AddRange(HashSet<int> target, Il2CppSystem.Collections.Generic.HashSet<int> source)
    {
        if (source == null)
            return;

        try
        {
            foreach (int value in source)
                target.Add(value);
        }
        catch
        {
            // Ignore a half-loaded save.
        }
    }

    private static void AddRange(HashSet<int> target, Il2CppSystem.Collections.Generic.List<int> source)
    {
        if (source == null)
            return;

        try
        {
            for (int i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }
        catch
        {
            // Ignore a half-loaded save.
        }
    }

    // ----------------------------------------------------------------- flags
    /// <summary>
    /// Reads one of the game's treasure flags. The flag bitmap is split into
    /// four 256-bit segments (Category.kTreasureFlag1..4).
    /// </summary>
    public static bool IsTreasureFlagSet(int flag)
    {
        try
        {
            int segment = flag / 256;
            int index = flag % 256;
            if (segment < 0 || segment > 3)
                return false;

            var storage = DataStorage.Instance;
            if (storage == null)
                return false;

            return storage.Get((DataStorage.Category)(TreasureCategoryBase + segment), index) != 0;
        }
        catch (Exception e)
        {
            LogOnce("treasure", $"Could not read treasure flag {flag}: {e.Message}");
            return false;
        }
    }

    // ----------------------------------------------------------------- pianos
    /// <summary>
    /// Pianos live in the scenario flag space, one flag per pub: guide slot n is flag
    /// 211 + n, so Tule (slot 1) is 212 and Phantom Village (slot 8) is 219.
    ///
    /// Measured, not guessed: playing Carwen (slot 2) flipped 213 and 221, and a save with
    /// three pianos played had 212, 213, 214 set. 220..227 move in lockstep with them and
    /// look like a derived piano-skill series, so they are not read.
    /// </summary>
    private const int PianoFlagBase = 211;

    public static int PianoFlag(PianoEntry piano) => PianoFlagBase + piano.Slot;

    /// <summary>Reads one of the game's scenario flags (Category.kScenarioFlag1..4).</summary>
    public static bool IsScenarioFlagSet(int flag)
    {
        try
        {
            int segment = flag / 256;
            int index = flag % 256;
            if (segment < 0 || segment > 3)
                return false;

            var storage = DataStorage.Instance;
            if (storage == null)
                return false;

            return storage.Get((DataStorage.Category)(ScenarioCategoryBase + segment), index) != 0;
        }
        catch (Exception e)
        {
            LogOnce("scenario", $"Could not read scenario flag {flag}: {e.Message}");
            return false;
        }
    }

    public static bool IsMonsterSeen(BestiaryEntry entry)
    {
        foreach (int id in entry.Monsters)
        {
            if (_seenMonsters.Contains(id))
                return true;
        }

        return false;
    }

    public static bool IsBlueLearned(BlueEntry entry) => entry != null && IsBlueLearned(entry.Id);

    /// <summary>
    /// Blue magic as the helper reports it: normally whatever the save says, but a hand tick
    /// wins, so a spell the helper read wrong can be unticked (and ticked again) from the list.
    /// </summary>
    public static bool IsBlueLearned(int spellId)
    {
        switch (ManualValue(BlueKey(spellId)))
        {
            case 1: return true;
            case 2: return false;
            default: return _learnedBlue.Contains(spellId);
        }
    }

    /// <summary>What the save itself says, ignoring any hand tick.</summary>
    public static bool IsBlueLearnedInSave(int spellId) => _learnedBlue.Contains(spellId);

    public static bool IsEntryOpened(GuideEntry entry)
    {
        if (entry.Flags != null)
        {
            foreach (int flag in entry.Flags)
            {
                if (IsTreasureFlagSet(flag))
                    return true;
            }
        }

        return IsManual(ManualKey(entry));
    }

    // ---------------------------------------------------------------- manual
    public static string ManualKey(GuideEntry entry, GuideLocation location = null)
    {
        if (location == null)
            return $"{entry.Kind}|{entry.Label}|{entry.Item}";

        return $"{location.Key}|{entry.Kind}|{entry.Label}|{entry.Item}";
    }

    public static string ManualKey(GuideLocation location, PianoEntry piano) =>
        $"{location.Key}|piano|{piano.Slot}";

    /// <summary>The hand tick that overrides a blue spell's learned state.</summary>
    public static string BlueKey(int spellId) => $"blue|{spellId}";

    /// <summary>0 = follow the save, 1 = forced learned, 2 = forced not learned.</summary>
    public static int ManualValue(string key)
    {
        lock (FileLock)
            return _manual.TryGetValue(key, out int value) ? value : 0;
    }

    public static void SetManual(string key, int value)
    {
        lock (FileLock)
            _manual[key] = value;

        _nextManualSave = UnityEngine.Time.unscaledTime + 0.5f;
    }

    public static bool IsManual(string key)
    {
        lock (FileLock)
            return _manual.TryGetValue(key, out int value) && value != 0;
    }

    public static void ToggleManual(string key)
    {
        lock (FileLock)
        {
            bool on = _manual.TryGetValue(key, out int value) && value != 0;
            _manual[key] = on ? 0 : 1;
        }

        _nextManualSave = UnityEngine.Time.unscaledTime + 0.5f;
    }

    public static void ClearManual(string key)
    {
        lock (FileLock)
        {
            if (!_manual.TryGetValue(key, out int value) || value == 0)
                return;

            _manual[key] = 0;
        }

        _nextManualSave = UnityEngine.Time.unscaledTime + 0.5f;
    }

    public static void Tick()
    {
        if (_nextManualSave > 0f && UnityEngine.Time.unscaledTime >= _nextManualSave)
        {
            _nextManualSave = 0f;
            SaveManual();
        }
    }

    /// <summary>
    /// A piano counts as played when the game's own flag is set. The hand tick stays as a
    /// fallback for saves loaded before this was wired up, and for a storage that is not
    /// readable yet.
    /// </summary>
    public static bool IsPianoPlayed(GuideLocation location, PianoEntry piano) =>
        IsScenarioFlagSet(PianoFlag(piano)) || IsManual(ManualKey(location, piano));

    // --------------------------------------------------------------- logging
    private static readonly HashSet<string> Logged = new();

    private static void LogOnce(string key, string message)
    {
        if (!Logged.Add(key))
            return;

        Plugin.LogSource?.LogWarning(message);
    }
}

/// <summary>Progress summary for one location / the whole guide.</summary>
internal sealed class LocationStatus
{
    public GuideLocation Location;
    public int ChestsTotal, ChestsDone;
    public int ItemsTotal, ItemsDone;
    public int BestiaryTotal, BestiaryDone;
    public int BlueTotal, BlueDone;
    public int PianoTotal, PianoDone;

    public readonly List<GuideEntry> MissingChests = new();
    public readonly List<GuideEntry> MissingItems = new();
    public readonly List<BestiaryEntry> MissingBestiary = new();
    public readonly List<BlueEntry> MissingBlue = new();
    public readonly List<PianoEntry> MissingPianos = new();

    // Everything, with its done state - the "show completed" view needs the finished ones
    // too, which the Missing lists deliberately drop.
    public readonly List<(GuideEntry Entry, bool Done)> AllChests = new();
    public readonly List<(GuideEntry Entry, bool Done)> AllItems = new();
    public readonly List<(BestiaryEntry Monster, bool Done)> AllBestiary = new();
    public readonly List<(BlueEntry Spell, bool Done)> AllBlue = new();
    public readonly List<(PianoEntry Piano, bool Done)> AllPianos = new();

    public bool HasChests => ChestsTotal > 0;
    public bool HasItems => ItemsTotal > 0;
    public bool HasBestiary => BestiaryTotal > 0;
    public bool HasBlue => BlueTotal > 0;
    public bool HasPianos => PianoTotal > 0;

    public int Remaining =>
        (ChestsTotal - ChestsDone) + (ItemsTotal - ItemsDone) + (BestiaryTotal - BestiaryDone) +
        (BlueTotal - BlueDone) + (PianoTotal - PianoDone);

    public bool Complete => Remaining == 0;

    /// <summary>Icon names for whatever is still missing (empty when complete).</summary>
    public IEnumerable<string> MissingIcons()
    {
        if (ChestsDone < ChestsTotal) yield return "chest";
        if (ItemsDone < ItemsTotal) yield return "potion";
        if (BestiaryDone < BestiaryTotal) yield return "chocobo";
        if (BlueDone < BlueTotal) yield return "hat";
        if (PianoDone < PianoTotal) yield return "piano";
    }

    public static LocationStatus Build(GuideLocation location)
    {
        // pick up anything the game changed since the last paint
        Progress.Refresh();

        var status = new LocationStatus { Location = location };

        foreach (var entry in location.Entries)
        {
            bool done = Progress.IsEntryOpened(entry);
            if (entry.Kind == "chest")
            {
                status.ChestsTotal++;
                status.AllChests.Add((entry, done));
                if (done) status.ChestsDone++;
                else status.MissingChests.Add(entry);
            }
            else
            {
                status.ItemsTotal++;
                status.AllItems.Add((entry, done));
                if (done) status.ItemsDone++;
                else status.MissingItems.Add(entry);
            }
        }

        foreach (var monster in location.Bestiary)
        {
            status.BestiaryTotal++;
            bool done = Progress.IsMonsterSeen(monster);
            status.AllBestiary.Add((monster, done));
            if (done) status.BestiaryDone++;
            else status.MissingBestiary.Add(monster);
        }

        foreach (var spell in location.Blue)
        {
            status.BlueTotal++;
            bool done = Progress.IsBlueLearned(spell);
            status.AllBlue.Add((spell, done));
            if (done) status.BlueDone++;
            else status.MissingBlue.Add(spell);
        }

        foreach (var piano in location.Pianos)
        {
            status.PianoTotal++;
            bool done = Progress.IsPianoPlayed(location, piano);
            status.AllPianos.Add((piano, done));
            if (done) status.PianoDone++;
            else status.MissingPianos.Add(piano);
        }

        return status;
    }
}
