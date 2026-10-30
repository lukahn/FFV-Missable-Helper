using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FFVMissableHelper;

/// <summary>
/// Takes the look of the game's own menus - the window frame, the font and the pointing-hand
/// selector - from the live pause menu, so the helper reads as another screen of the game
/// rather than an overlay.
///
/// Every piece is best effort and independently optional, so a game update that renames
/// things cannot break the helper.
/// </summary>
internal static class GameSkin
{
    public static Font Font { get; private set; }
    public static Sprite Frame { get; private set; }
    public static Sprite Cursor { get; private set; }

    public static Image.Type FrameType { get; private set; } = Image.Type.Sliced;
    public static float FrameMultiplier { get; private set; } = 1f;

    private static readonly HashSet<string> LoggedSprites = new();

    /// <summary>
    /// Reads the skins out of the menu. Must be called on the menu root *before* the helper
    /// panel is parented into it, or the helper's own texts and sprites get counted too.
    /// </summary>
    public static void Harvest(RectTransform root)
    {
        if (root == null)
            return;

        Font font = null;
        Sprite frame = null;
        Sprite cursor = null;

        try
        {
            font = MostUsedFont(root);
        }
        catch (Exception e)
        {
            Warn("font", e);
        }

        try
        {
            frame = LargestSlicedImage(root, out var type, out float multiplier);
            if (frame != null)
            {
                Frame = frame;
                FrameType = type;
                FrameMultiplier = multiplier;
            }
        }
        catch (Exception e)
        {
            Warn("frame", e);
        }

        try
        {
            cursor = FindCursor(root);
        }
        catch (Exception e)
        {
            Warn("cursor", e);
        }

        if (font != null)
            Font = font;

        if (cursor != null)
            Cursor = cursor;

        UiKit.UseFont(Font);

        Plugin.Trace(
            $"skin: font={(Font != null ? Font.name : "none")}, " +
            $"frame={(Frame != null ? Frame.name : "drawn")}, " +
            $"cursor={(Cursor != null ? Cursor.name : "drawn")}");
    }

    /// <summary>
    /// The menu's own text font. Picked by how many labels use it, because the pause menu
    /// also carries one or two texts in a plainer font (the location line, for instance) and
    /// the body of the menu is what the helper should look like.
    /// </summary>
    private static Font MostUsedFont(RectTransform root)
    {
        var counts = new Dictionary<int, (Font Font, int Count)>();

        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            var font = text != null ? text.font : null;
            if (font == null)
                continue;

            int id = font.GetInstanceID();
            counts[id] = counts.TryGetValue(id, out var seen)
                ? (seen.Font, seen.Count + 1)
                : (font, 1);
        }

        Font best = null;
        int bestCount = 0;
        foreach (var pair in counts)
        {
            if (pair.Value.Count > bestCount)
            {
                best = pair.Value.Font;
                bestCount = pair.Value.Count;
            }
        }

        return best;
    }

    /// <summary>
    /// The window frame is the largest nine-sliced image on the menu: corners drawn at native
    /// size, middle stretched, which is the only way a single sprite gives a fixed-width
    /// border at any panel size.
    /// </summary>
    private static Sprite LargestSlicedImage(
        RectTransform root, out Image.Type type, out float multiplier)
    {
        type = Image.Type.Sliced;
        multiplier = 1f;

        Sprite best = null;
        float bestArea = 0f;

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null)
                continue;

            var border = image.sprite.border;
            if (border.sqrMagnitude < 1f)
                continue;

            var rect = image.rectTransform.rect;
            float area = rect.width * rect.height;
            if (area <= bestArea || rect.width < 400f || rect.height < 300f)
                continue;

            best = image.sprite;
            bestArea = area;
            type = image.type;
            multiplier = image.pixelsPerUnitMultiplier;
        }

        return best;
    }

    /// <summary>The selector hand, found by name anywhere in the menu.</summary>
    private static Sprite FindCursor(RectTransform root)
    {
        string[] wanted = { "cursor", "hand", "fing", "pointer", "select" };

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null)
                continue;

            string spriteName = image.sprite != null ? image.sprite.name : "";
            string haystack = (image.name + " " + spriteName).ToLowerInvariant();

            if (Plugin.Verbose.Value && spriteName.Length > 0 && LoggedSprites.Add(spriteName))
                Plugin.Trace($"skin: image '{image.name}' sprite '{spriteName}' " +
                             $"{image.rectTransform.rect.width:0}x{image.rectTransform.rect.height:0}");

            if (image.sprite == null)
                continue;

            foreach (string needle in wanted)
            {
                if (haystack.Contains(needle))
                    return image.sprite;
            }
        }

        return null;
    }

    private static void Warn(string part, Exception e) =>
        Plugin.LogSource?.LogWarning($"[Skin] could not read the menu {part}: {e.Message}");
}
