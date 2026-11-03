using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace FFVMissableHelper;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.lukahn.ffv.missablehelper";
    public const string PluginName = "FFV Missable Helper";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> AreaPopupEnabled;
    internal static ConfigEntry<float> AreaPopupSeconds;
    internal static ConfigEntry<bool> UseGameIcons;
    internal static ConfigEntry<bool> HostInPauseMenu;
    internal static ConfigEntry<bool> ShowCompleted;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> MenuRowEnabled;
    internal static ConfigEntry<bool> GameEventsEnabled;
    internal static ConfigEntry<bool> ProgressRefreshEnabled;
    internal static ConfigEntry<bool> LogFlagWrites;

    private static readonly object TraceLock = new();

    /// <summary>
    /// Appends to our own log file. BepInEx truncates LogOutput.log on every start, which
    /// made it impossible to see what happened in the previous run, so menu diagnostics go
    /// here as well.
    /// </summary>
    internal static void Trace(string message)
    {
        try
        {
            var folder = Path.Combine(Paths.ConfigPath, "FFVMissableHelper");
            Directory.CreateDirectory(folder);

            lock (TraceLock)
                File.AppendAllText(Path.Combine(folder, "trace.log"),
                    $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // tracing must never break the game
        }
    }

    public override void Load()
    {
        LogSource = Log;

        AreaPopupEnabled = Config.Bind(
            "Display",
            "AreaPopup",
            true,
            "Show a summary box in the top-left corner when entering a new area. " +
            "Can also be toggled from the FFV Helper menu.");

        AreaPopupSeconds = Config.Bind(
            "Display",
            "AreaPopupSeconds",
            10f,
            "How long the area popup stays on screen.");

        UseGameIcons = Config.Bind(
            "Display",
            "UseGameIcons",
            true,
            "Use the game's own item icons for the potion, monster, hat and harp markers. " +
            "Turn this off to use the plugin's drawn icons instead.");

        HostInPauseMenu = Config.Bind(
            "Display",
            "HostInPauseMenu",
            true,
            "Open the helper inside the pause menu's sub-window area (like Config) instead of " +
            "as an overlay. Turn this off if the layout does not line up with your game build.");

        ShowCompleted = Config.Bind(
            "Display",
            "ShowCompleted",
            false,
            "List chests, items, monsters and blue magic that are already done instead of " +
            "hiding them. Finished entries are drawn dimmed. Can also be toggled from the " +
            "helper's location list.");

        Verbose = Config.Bind(
            "Debug",
            "VerboseLogging",
            false,
            "Log menu layout details and save-state reads. Useful when the helper " +
            "row or window does not line up with the game's own menu.");

        MenuRowEnabled = Config.Bind(
            "Debug",
            "MenuRow",
            true,
            "Add the FFV Helper row to the pause menu. Turn this off to test whether the " +
            "row is behind a problem in game.");

        GameEventsEnabled = Config.Bind(
            "Debug",
            "GameEvents",
            true,
            "Refresh the helper's counts from the game's own events (battle end, chest, item, " +
            "area change, save load). Turn this off to test whether those hooks are involved.");

        ProgressRefreshEnabled = Config.Bind(
            "Debug",
            "ProgressRefresh",
            true,
            "Read the save state in the background. Turn this off to test whether those reads " +
            "are involved; the helper then only reads when it is opened.");

        LogFlagWrites = Config.Bind(
            "Debug",
            "LogFlagWrites",
            false,
            "Diagnostic: record every write the game makes to its flag/variable storage in " +
            "trace.log. Used to find out how the game records pianos. Only useful while " +
            "deliberately reproducing something, so it is off by default.");

        Guide.Load();
        Progress.LoadManual();

        HelperWindow.EnsureTypeRegistered();
        AreaPopup.EnsureTypeRegistered();
        MenuLayout.EnsureTypeRegistered();

        Progress.Refresh(force: true);

        // The area popup creates itself on the first game event (see AreaPopup.Refresh):
        // a GameObject made during plugin load belongs to no scene and never ticks.
        // The helper window is created when the pause menu first opens.

        var harmony = new Harmony(PluginGuid);
        MenuIntegration.Apply(harmony);
        ProgressEvents.Apply(harmony);

        if (LogFlagWrites.Value)
            FlagWatch.Apply(harmony);

        harmony.PatchAll(typeof(TitleScreenDiagnosticsPatch).Assembly);

        LogSource.LogInfo(
            $"{PluginName} {PluginVersion} loaded (area popup={AreaPopupEnabled.Value}, " +
            $"menu row={MenuRowEnabled.Value}, game events={GameEventsEnabled.Value}, " +
            $"progress refresh={ProgressRefreshEnabled.Value}, verbose={Verbose.Value}, " +
            $"guide={Guide.SourcePath ?? "unavailable"}).");

        LogSource.LogInfo($"[Input] {InputCompat.Describe()}");

        // a marker per session, so an empty trace file proves something on its own
        Trace($"--- {PluginName} {PluginVersion} loaded ---");

        if (Guide.LoadError != null)
            LogSource.LogError($"Guide data problem: {Guide.LoadError}");
    }
}
