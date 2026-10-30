using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FFVMissableHelper;

/// <summary>
/// Map / location pictures for chests and items. Files live next to the plugin:
///
///   BepInEx/plugins/FFVMissableHelper/maps/&lt;wiki image name&gt;.png
///
/// The plugin ships zero-byte stubs named exactly like the IGN pictures; drop a
/// PNG or JPG over a stub (same name) and it is used the next time the helper
/// opens. Empty files are treated as "no picture", so the stubs are harmless.
/// </summary>
internal static class MapImages
{
    private static readonly Dictionary<string, Sprite> Cache = new();
    private static readonly List<string> Order = new();
    private static readonly HashSet<string> Empty = new();
    private static readonly HashSet<string> Logged = new();

    // full-resolution screenshots are large; keep only the most recent few
    private const int CacheLimit = 12;

    public static string Folder => Path.Combine(Paths.PluginPath, "FFVMissableHelper", "maps");

    public static Sprite Get(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (Cache.TryGetValue(fileName, out var cached))
            return cached;

        if (Empty.Contains(fileName))
            return null;

        try
        {
            string path = FindFile(fileName);
            if (path == null)
            {
                Empty.Add(fileName);
                return null;
            }

            // a stub is a real file with no content: not an error, just no picture
            if (new FileInfo(path).Length == 0)
            {
                Empty.Add(fileName);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(bytes) || texture.width <= 1 || texture.height <= 1)
            {
                Log(fileName, $"'{fileName}' is not a readable image; ignoring it.");
                Empty.Add(fileName);
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);

            Cache[fileName] = sprite;
            Order.Add(fileName);
            TrimCache();

            if (Plugin.Verbose.Value)
                Plugin.LogSource.LogInfo($"[Maps] loaded {fileName} ({texture.width}x{texture.height})");

            return sprite;
        }
        catch (Exception e)
        {
            Log(fileName, $"could not load '{fileName}': {e.Message}");
            Empty.Add(fileName);
            return null;
        }
    }

    private static string FindFile(string fileName)
    {
        string exact = Path.Combine(Folder, fileName);
        if (File.Exists(exact))
            return exact;

        // tolerate a missing extension or a different one (.jpg next to the .png stub)
        string bare = Path.Combine(Folder, Path.GetFileNameWithoutExtension(fileName));
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            if (File.Exists(bare + extension))
                return bare + extension;
        }

        return null;
    }

    public static void Reset()
    {
        foreach (var sprite in Cache.Values)
            Destroy(sprite);

        Cache.Clear();
        Order.Clear();
        Empty.Clear();
    }

    private static void TrimCache()
    {
        while (Order.Count > CacheLimit)
        {
            string oldest = Order[0];
            Order.RemoveAt(0);

            if (!Cache.TryGetValue(oldest, out var sprite))
                continue;

            Cache.Remove(oldest);
            Destroy(sprite);
        }
    }

    private static void Destroy(Sprite sprite)
    {
        try
        {
            if (sprite == null)
                return;

            var texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
        catch
        {
            // the object may already have been unloaded with the scene
        }
    }

    private static void Log(string key, string message)
    {
        if (Logged.Add(key))
            Plugin.LogSource?.LogWarning($"[Maps] {message}");
    }
}
