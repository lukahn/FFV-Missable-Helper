using System;
using HarmonyLib;
using Last.Interpreter;
using Last.Management;

namespace FFVMissableHelper;

/// <summary>
/// Diagnostic: logs every write the game makes to its flag/variable storage, with the value
/// it had before, and the area the player was standing in.
///
/// Pianos are the one thing the helper has to tick by hand. The game does track them - playing
/// a new one shows "Piano skill leveled up!" and writes a scenario flag - but which flag is
/// which piano is not in any table we extracted. Before/after tells a one-shot flag (0 -> 1)
/// from a counter (2 -> 3); the area tells which piano it was, so one capture per piano maps
/// the whole set.
///
/// Off by default (Debug > LogFlagWrites), and capped so a session cannot flood the log.
/// </summary>
internal static class FlagWatch
{
    private const int Limit = 600;

    private static int _writes;
    private static int _beforeFlag = -1;
    private static int _beforeVariable = -1;
    private static bool _dumped;

    public static void Apply(Harmony harmony)
    {
        Patch(harmony, "Set",
            new[] { typeof(DataStorage.Category), typeof(int), typeof(int) },
            nameof(BeforeSetFlag), nameof(AfterSetFlag));

        Patch(harmony, "Set",
            new[] { typeof(string), typeof(int), typeof(int) },
            nameof(BeforeSetVariable), nameof(AfterSetVariable));

        Plugin.Trace("flagwatch: logging every flag and variable write");
    }

    // --------------------------------------------------------------- category set
    private static void BeforeSetFlag(DataStorage.Category c, int index)
    {
        _beforeFlag = Read(c, index);
    }

    private static void AfterSetFlag(DataStorage.Category c, int index, int value)
    {
        // the first write of a session happens while the save is being restored, which is
        // the moment the whole scenario flag space can be read
        if (!_dumped)
        {
            _dumped = true;
            DumpScenario();
        }

        Log($"flag cat={(int)c} idx={index} {_beforeFlag}->{value}");
    }

    /// <summary>Logs every scenario flag that is set, so the piano numbering can be read off.</summary>
    private static void DumpScenario()
    {
        var set = new System.Collections.Generic.List<string>();

        for (int segment = 0; segment < 4; segment++)
        {
            var category = (DataStorage.Category)((int)DataStorage.Category.kScenarioFlag1 + segment);

            for (int index = 0; index < 256; index++)
            {
                int value = Read(category, index);
                if (value != 0)
                    set.Add($"{segment * 256 + index}");
            }
        }

        Plugin.Trace($"flagwatch: scenario flags set ({set.Count}): " +
                     (set.Count == 0 ? "none" : string.Join(" ", set)) +
                     $" (area {Area()})");
    }

    // ----------------------------------------------------------------- named set
    private static void BeforeSetVariable(string c, int index)
    {
        _beforeVariable = ReadVariable(c, index);
    }

    private static void AfterSetVariable(string c, int index, int value)
    {
        Log($"var name={c} idx={index} {_beforeVariable}->{value}");
    }

    // ------------------------------------------------------------------ helpers
    private static int Read(DataStorage.Category c, int index)
    {
        try
        {
            var storage = DataStorage.Instance;
            return storage == null ? -1 : storage.Get(c, index);
        }
        catch
        {
            return -1;
        }
    }

    private static int ReadVariable(string name, int index)
    {
        try
        {
            var storage = DataStorage.Instance;
            return storage == null ? -1 : storage.Get(name, index);
        }
        catch
        {
            return -1;
        }
    }

    private static int Area()
    {
        try
        {
            var userData = UserDataManager.Instance();
            return userData == null ? -1 : userData.CurrentAreaId;
        }
        catch
        {
            return -1;
        }
    }

    private static void Log(string message)
    {
        if (_writes++ >= Limit)
            return;

        if (_writes == Limit)
            Plugin.Trace("flagwatch: limit reached, stopping");

        Plugin.Trace($"flagwatch: {message} (area {Area()})");
    }

    private static void Patch(Harmony harmony, string method, Type[] signature,
        string prefix, string postfix)
    {
        try
        {
            var original = AccessTools.Method(typeof(DataStorage), method, signature);
            if (original == null)
            {
                Plugin.LogSource.LogWarning($"[FlagWatch] DataStorage.{method} not found; skipped.");
                return;
            }

            harmony.Patch(original,
                prefix: new HarmonyMethod(typeof(FlagWatch), prefix),
                postfix: new HarmonyMethod(typeof(FlagWatch), postfix));

            Plugin.LogSource.LogInfo($"[FlagWatch] watching DataStorage.{method}({signature.Length} args)");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[FlagWatch] could not patch DataStorage.{method}: {e.Message}");
        }
    }
}
