using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace FFVMissableHelper;

/// <summary>
/// Shared drawing helpers. The panel texture is the same generated blue gradient
/// the Blue Mage Helper uses, so both mods look like they belong to the game.
/// </summary>
internal static class UiKit
{
    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1080;

    private static Font _font;
    private static Sprite _panelSprite;
    private static Sprite _fillSprite;
    private static Texture2D _panelTexture;
    private static readonly Dictionary<string, Sprite> Icons = new();

    public static readonly Color TextColor = new Color(0.96f, 0.96f, 1f, 1f);
    public static readonly Color Gold = new Color(0.95f, 0.80f, 0.40f, 1f);
    public static readonly Color Muted = new Color(0.72f, 0.78f, 0.92f, 1f);

    public static Font Font
    {
        get
        {
            if (_font != null)
                return _font;

            string[] candidates =
            {
                "Georgia", "Palatino Linotype", "Times New Roman", "Segoe UI", "Arial"
            };

            foreach (string name in candidates)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(name, 24);
                    if (font != null)
                        return _font = font;
                }
                catch
                {
                    // Try the next one.
                }
            }

            try
            {
                return _font = Font.GetDefault();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Switches to the game's own font once a menu text has been found.</summary>
    public static void UseFont(Font font)
    {
        if (font != null)
            _font = font;
    }

    /// <summary>
    /// The blue fill: a plain vertical gradient, light at the top and dark at the bottom,
    /// exactly like the game's own windows. It is one pixel wide because it only ever runs
    /// top to bottom - the shape around it comes from the window sprite, not from here.
    /// </summary>
    public static Sprite FillSprite
    {
        get
        {
            if (_fillSprite != null)
                return _fillSprite;

            const int height = 256;

            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var top = new Color(0.26f, 0.40f, 0.85f, 1f);
            var bottom = new Color(0.05f, 0.10f, 0.40f, 1f);

            for (int y = 0; y < height; y++)
            {
                // the top of a texture is the last row, so the light end is at y = height - 1
                float t = y / (float)(height - 1);
                texture.SetPixel(0, y, Color.Lerp(bottom, top, t));
            }

            texture.Apply();

            _fillSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, height),
                new Vector2(0.5f, 0.5f),
                1f);
            return _fillSprite;
        }
    }

    public static Sprite PanelSprite
    {
        get
        {
            if (_panelSprite != null)
                return _panelSprite;

            _panelTexture = CreateBoxTexture();
            _panelSprite = Sprite.Create(
                _panelTexture,
                new Rect(0f, 0f, _panelTexture.width, _panelTexture.height),
                new Vector2(0.5f, 0.5f),
                1f,                                        // one texture pixel = one UI unit
                0,
                SpriteMeshType.FullRect,                   // a Tight mesh drops the border
                new Vector4(FrameBorder, FrameBorder, FrameBorder, FrameBorder));
            return _panelSprite;
        }
    }

    /// <summary>Thickness of the drawn frame in UI units (the sprite runs at 1 unit per pixel).</summary>
    public const int FrameBorder = 5;

    /// <summary>How thick the drawn frame is, and so how far inside it a fill should start.</summary>
    public const float RingThickness = FrameBorder;

    /// <summary>
    /// Canvas units per sprite texture pixel, i.e. the divisor uGUI applies to a sprite's rate.
    /// </summary>
    private static float CanvasRate(Graphic graphic)
    {
        float rate = 0f;

        try
        {
            var canvas = graphic != null ? graphic.canvas : null;
            if (canvas != null)
                rate = canvas.referencePixelsPerUnit;
        }
        catch
        {
            // fall through to the parent lookup
        }

        if (rate <= 0.01f && graphic != null)
        {
            try
            {
                var canvas = graphic.GetComponentInParent<Canvas>();
                if (canvas != null)
                    rate = canvas.referencePixelsPerUnit;
            }
            catch
            {
                // fall through to the default
            }
        }

        return rate > 0.01f && !float.IsNaN(rate) ? rate : 100f;
    }

    /// <summary>The thickest side of a sprite's nine-slice border, in texture pixels.</summary>
    private static float BorderTexels(Sprite sprite)
    {
        var border = sprite.border;
        return Mathf.Max(Mathf.Max(border.x, border.y), Mathf.Max(border.z, border.w));
    }

    /// <summary>
    /// Forces a nine-sliced frame to draw its border at exactly <paramref name="units"/>.
    ///
    /// uGUI sizes a border as (sprite border) x (sprite rate / canvas rate) x multiplier, so a
    /// sprite made at one pixel per unit comes out a hundredth of its intended thickness on a
    /// canvas that references a hundred pixels per unit. When the border collapses, uGUI
    /// stretches the corner tiles across the whole panel instead: that is the stray circle
    /// that used to sit inside the window frame. Setting the multiplier here makes the frame
    /// the same thickness on any canvas, at any resolution.
    /// </summary>
    public static float SetBorderUnits(Image image, float units)
    {
        if (image == null || image.sprite == null)
            return 0f;

        float texels = BorderTexels(image.sprite);
        if (texels <= 0.01f)
        {
            // no border at all: a sliced image would stretch the artwork, so draw it plain
            if (image.type != Image.Type.Simple)
                image.type = Image.Type.Simple;
            return 0f;
        }

        float perTexel = image.sprite.pixelsPerUnit / CanvasRate(image);
        if (perTexel <= 0.0001f)
            return 0f;

        image.pixelsPerUnitMultiplier = units / (texels * perTexel);
        return units;
    }

    /// <summary>How thick a panel's frame is actually drawn, in UI units.</summary>
    public static float BorderUnits(Image image)
    {
        if (image == null || image.sprite == null)
            return 0f;

        float multiplier = image.pixelsPerUnitMultiplier > 0f ? image.pixelsPerUnitMultiplier : 1f;
        return BorderTexels(image.sprite) * (image.sprite.pixelsPerUnit / CanvasRate(image)) * multiplier;
    }

    public static GameObject CreateCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    public static Image CreatePanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.sprite = PanelSprite;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        image.raycastTarget = false;

        // draw the frame at its texture size, whatever rate this canvas runs at
        SetBorderUnits(image, FrameBorder);
        return image;
    }

    /// <summary>
    /// The background of a panel: one plain vertical gradient, stretched to the box and inset
    /// so it sits inside the frame. A single stretched texture has no corners, no slices and
    /// no shape of its own, so it cannot warp into anything but horizontal bands.
    /// </summary>
    public static Image CreateFill(Transform parent, string name, float inset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.sprite = FillSprite;
        image.type = Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = Color.white;
        image.raycastTarget = false;

        var rect = Rect(go);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        return image;
    }

    public static RectTransform Rect(GameObject go)
    {
        var rect = go.GetComponent<RectTransform>();
        if (rect == null)
        {
            // a plain GameObject has only a Transform; UI children need a RectTransform
            rect = go.AddComponent<RectTransform>();
        }

        return rect;
    }

    public static Text CreateText(Transform parent, string name, int size, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<Text>();
        text.font = Font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = TextColor;
        text.raycastTarget = false;
        text.supportRichText = true;
        text.lineSpacing = 1.05f;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.useGraphicAlpha = true;

        return text;
    }

    public static Image CreateIcon(Transform parent, string iconName, float size)
    {
        var go = new GameObject("Icon_" + iconName);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.sprite = GetIcon(iconName);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = image.sprite != null;

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(size, size);
        return image;
    }

    public static Sprite GetIcon(string iconName)
    {
        if (Icons.TryGetValue(iconName, out var cached))
            return cached;

        // Prefer the game's own artwork where an equivalent icon exists.
        Sprite sprite = GameIcons.Get(iconName);
        if (sprite == null)
            sprite = LoadSprite("FFVMissableHelper.assets.icon_" + iconName + ".png");

        Icons[iconName] = sprite;
        return sprite;
    }

    /// <summary>Forgets cached row icons (the icon atlas is unloaded with the field).</summary>
    public static void ResetIconCache()
    {
        var keys = new List<string>(Icons.Keys);
        foreach (string key in keys)
        {
            // keep the bestiary portraits; they are decoded from our own PNGs
            if (!key.StartsWith("mon:", StringComparison.Ordinal))
                Icons.Remove(key);
        }

        GameIcons.Reset();
    }

    /// <summary>Bestiary picture for one entry, or null when the guide has none.</summary>
    public static Sprite GetBestiaryImage(int number)
    {
        string key = "mon:" + number;
        if (Icons.TryGetValue(key, out var cached))
            return cached;

        var sprite = LoadSprite($"FFVMissableHelper.assets.bestiary.mon_{number:000}.png");
        Icons[key] = sprite;
        return sprite;
    }

    /// <summary>Loads an embedded PNG (used by the icon fallbacks as well).</summary>
    internal static Sprite LoadSprite(string resourceName)
    {
        Sprite sprite = null;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadImage(memory.ToArray());

                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not load sprite {resourceName}: {e.Message}");
        }

        return sprite;
    }

    /// <summary>The frame ring: a thin outline, a bright double line and a dark gap.</summary>
    private static Color RingColor(int edge) => edge switch
    {
        0 => new Color(0.04f, 0.06f, 0.18f, 1f),
        1 or 2 => new Color(0.93f, 0.95f, 1f, 1f),
        3 => new Color(0.07f, 0.11f, 0.32f, 1f),
        4 => new Color(0.62f, 0.70f, 0.95f, 1f),
        _ => new Color(0f, 0f, 0f, 0f)     // holes: the fill shows through
    };

    /// <summary>
    /// The frame of our own window: a dark outer line, a bright double line and a soft inner
    /// line, drawn as a nine-slice. Nothing fills the middle - the gradient laid behind it
    /// does - so the frame never has to be stretched to match a panel size.
    /// </summary>
    private static Texture2D CreateBoxTexture()
    {
        int size = FrameBorder * 4;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                texture.SetPixel(x, y, RingColor(edge));
            }
        }

        texture.Apply();
        return texture;
    }
}
