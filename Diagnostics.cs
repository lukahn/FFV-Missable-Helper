using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace FFVMissableHelper;

/// <summary>
/// Optional one-shot checks that run when verbose logging is on, so the icon
/// lookup and the guide data can be verified from the log alone.
/// </summary>
internal static class Diagnostics
{
    public static void LogIconLookup()
    {
        if (!Plugin.Verbose.Value)
            return;

        var builder = new StringBuilder("[Icons] self-test:");
        foreach (var pair in GameIcons.Mapping)
        {
            Sprite sprite;
            try
            {
                sprite = GameIcons.Get(pair.Key);
            }
            catch (Exception e)
            {
                builder.Append($" {pair.Key}=error({e.Message})");
                continue;
            }

            builder.Append($" {pair.Key}({pair.Value})={(sprite != null ? $"{sprite.name} {sprite.rect.width}x{sprite.rect.height}" : "none")}");
        }

        Plugin.LogSource.LogInfo(builder.ToString());
    }
}

[HarmonyPatch(typeof(Last.Scene.SceneTitleScreen), nameof(Last.Scene.SceneTitleScreen.CreateTitleWindow))]
internal static class TitleScreenDiagnosticsPatch
{
    private static void Postfix()
    {
        try
        {
            Diagnostics.LogIconLookup();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Icon self-test failed: {e.Message}");
        }
    }
}
