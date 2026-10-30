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
        var learned = new HashSet<int>();
        try
        {
            var userData = UserDataManager.Instance();
            if (userData != null)
            {
                AddRange(learned, userData.LearnedAbilityList);
                AddRange(learned, userData.OwnedMagicList);

                var characters = userData.OwnedCharacterList;
                if (characters != null)
                {
                    foreach (var character in characters)
                    {
                        if (character == null)
                            continue;

                        AddRange(learned, character.LearningAbilitys);

                        var abilities = character.OwnedAbilityList;
                        if (abilities == null)
                            continue;

                        foreach (var owned in abilities)
                        {
                            try
                            {
                                int id = owned?.saveData?.abilityId ?? 0;
                                if (id > 0)
                                    learned.Add(id);

                                int contentId = owned?.saveData?.contentId ?? 0;
                                if (contentId > 0)
                                    learned.Add(contentId);
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

        // blue magic is stored under enemy-ability alias ids
        var resolved = new HashSet<int>(learned);
        foreach (int id in learned)
        {
            if (Guide.BlueByAlias.TryGetValue(id, out int spellId))
                resolved.Add(spellId);
        }

        _learnedBlue = resolved;
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

    public static bool IsBlueLearned(BlueEntry entry) => _learnedBlue.Contains(entry.Id);

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
