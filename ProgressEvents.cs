using System;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Last.Management;

namespace FFVMissableHelper;

/// <summary>
/// Re-reads the save state whenever something that can change it happens, so the
/// helper never shows counts from before the event.
///
/// The game already raises all of these as normal method calls: a battle ends,
/// a chest counter is bumped, an item is added, an area id changes. Hooking them
/// is cheaper and more accurate than polling.
/// </summary>
internal static class ProgressEvents
{
    public static void Apply(Harmony harmony)
    {
        if (!Plugin.GameEventsEnabled.Value)
        {
            Plugin.LogSource.LogInfo("[Events] disabled in the config (Debug > GameEvents); no hooks applied.");
            return;
        }

        // battle end: monster defeats (bestiary) and learned blue magic
        Patch(harmony, typeof(BattleController), "Exit", nameof(AfterBattle));

        // treasure chest opened / hidden item picked up
        Patch(harmony, typeof(UserDataManager), "UpdateOpenChestCount", nameof(AfterChest));

        // item, ability or magic stone obtained (includes chest contents)
        Patch(harmony, typeof(OwnedItemClient), "AddOwnedItem", nameof(AfterItem));
        Patch(harmony, typeof(OwnedItemClient), "AddAbility", nameof(AfterItem));
        Patch(harmony, typeof(OwnedItemClient), "AddMagicStone", nameof(AfterItem));

        // a spell learned or forgotten (blue magic included): hands us the client that can
        // write magic back, and moves the helper's counts
        Patch(harmony, typeof(UserDataClient), "AddOwnedMagic", nameof(AfterMagic));
        Patch(harmony, typeof(UserDataClient), "RemoveOwnedMagic", nameof(AfterMagic));

        // walking into another area
        Patch(harmony, typeof(UserDataManager), "set_CurrentAreaId", nameof(AfterArea));
        Patch(harmony, typeof(UserDataManager), "set_CurrentMapId", nameof(AfterArea));

        // loading a save / starting a new game
        Patch(harmony, typeof(UserDataManager), "Initialize", nameof(AfterLoad));
    }

    private static void AfterBattle()
    {
        Invalidate("battle end");
    }

    private static void AfterChest()
    {
        Invalidate("chest opened");
    }

    private static void AfterItem(OwnedItemClient __instance)
    {
        // the game's own client: the one object that can add or remove items safely
        Unlock.Items = __instance;
        Invalidate("item obtained");
    }

    private static void AfterMagic(UserDataClient __instance)
    {
        // same idea for magic: this instance is what a blue spell is written through
        Unlock.UserData = __instance;
        Invalidate("magic changed");
    }

    private static void AfterArea()
    {
        Invalidate("area changed");
    }

    private static void AfterLoad()
    {
        Invalidate("save loaded");
    }

    private static void Invalidate(string reason)
    {
        try
        {
            Progress.Invalidate(reason);
            AreaPopup.Refresh();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Events] refresh after {reason} failed: {e.Message}");
        }
    }

    /// <summary>
    /// Patches every overload of a method. AddOwnedItem has several signatures,
    /// so looking the method up by name alone is ambiguous.
    /// </summary>
    private static void Patch(Harmony harmony, Type type, string method, string postfix)
    {
        try
        {
            var overloads = AccessTools.GetDeclaredMethods(type)
                .Where(candidate => candidate.Name == method)
                .ToList();

            if (overloads.Count == 0)
            {
                Plugin.LogSource?.LogWarning($"[Events] {type.Name}.{method} not found; that refresh is skipped.");
                return;
            }

            int patched = 0;
            foreach (var overload in overloads)
            {
                try
                {
                    harmony.Patch(overload,
                        postfix: new HarmonyMethod(typeof(ProgressEvents), postfix));
                    patched++;
                }
                catch (Exception e)
                {
                    Plugin.LogSource?.LogWarning(
                        $"[Events] could not patch {type.Name}.{method}({overload.GetParameters().Length} args): {e.Message}");
                }
            }

            if (Plugin.Verbose.Value && patched > 0)
                Plugin.LogSource.LogInfo($"[Events] refreshing after {type.Name}.{method} ({patched} overload(s))");
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Events] could not patch {type.Name}.{method}: {e.Message}");
        }
    }
}
