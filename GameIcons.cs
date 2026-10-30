using System;
using System.Collections.Generic;
using Last.UI;
using UnityEngine;

namespace FFVMissableHelper;

/// <summary>
/// The game's own item icons, resolved through <see cref="IconTextUtility"/>,
/// which is the same lookup the inventory uses for the &lt;IC_...&gt; tags in
/// item names. Two entries have no in-game equivalent (the treasure chest and
/// the completion star), so those keep the drawn icons.
/// </summary>
internal static class GameIcons
{
    // row icon name -> the game's icon tag
    private static readonly Dictionary<string, string> Tags = new()
    {
        ["potion"] = "<IC_DRAG>",    // Potion / Hi-Potion / Ether / Elixir
        ["chocobo"] = "<IC_CLW>",    // claw: the closest thing to a creature marker
        ["hat"] = "<IC_HEAD>",       // Leather Cap, Plumed Hat, ...
        ["piano"] = "<IC_HRP>",      // Silver Harp, Dream Harp, ...
    };

    private static readonly Dictionary<string, Sprite> Cache = new();
    private static bool _unavailable;

    public static readonly IReadOnlyDictionary<string, string> Mapping = Tags;

    public static Sprite Get(string iconName)
    {
        if (!Plugin.UseGameIcons.Value || _unavailable)
            return null;

        if (Cache.TryGetValue(iconName, out var cached))
            return cached;

        if (!Tags.TryGetValue(iconName, out string tag))
            return null;

        Sprite sprite = null;
        try
        {
            sprite = IconTextUtility.GetIcon(tag);
            if (sprite == null)
                sprite = IconTextUtility.GetIcon(tag.Trim('<', '>'));

            // If the atlas is not loaded (or a future build moves the lookup),
            // fall back to the copy embedded from the same atlas.
            if (sprite == null)
                sprite = UiKit.LoadSprite($"FFVMissableHelper.assets.game.{iconName}.png");

            if (Plugin.Verbose.Value)
            {
                Plugin.LogSource.LogInfo(
                    $"[Icons] {iconName} -> {tag}: {(sprite != null ? "sprite" : "none (using drawn icon)")}");
            }
        }
        catch (Exception e)
        {
            _unavailable = true;
            Plugin.LogSource.LogWarning(
                $"[Icons] the game's icon lookup failed ({e.Message}); using the drawn icons.");
        }

        Cache[iconName] = sprite;
        return sprite;
    }

    /// <summary>Drops cached sprites, e.g. after a scene change unloads the atlas.</summary>
    public static void Reset()
    {
        Cache.Clear();
        _unavailable = false;
    }
}
