using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;
using KeyInputView = Last.UI.KeyInput.MainMenuView;
using TouchView = Last.UI.Touch.MainMenuView;

namespace FFVMissableHelper;

/// <summary>
/// Makes room for the extra pause-menu row: the Time/Gil panel is moved down by
/// exactly one command row.
///
/// The row height is only known once the game has laid the rows out, which happens
/// after <c>CreateCommandMenu</c> has returned, so the work is spread over a few
/// frames by <see cref="MenuLayoutWatcher"/>. Everything here is measured, never
/// assumed: growing the box by a guessed height pushed the counters down far more
/// than one row.
/// </summary>
internal static class MenuLayout
{
    private static readonly HashSet<int> AlreadyMoved = new();
    private static readonly HashSet<string> LoggedOnce = new();

    public static void EnsureTypeRegistered()
    {
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<MenuLayoutWatcher>())
                ClassInjector.RegisterTypeInIl2Cpp<MenuLayoutWatcher>();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not register the menu layout watcher: {e}");
        }
    }

    /// <summary>Starts watching a freshly created pause menu.</summary>
    public static void Watch(bool touch)
    {
        try
        {
            var go = new GameObject("FFV Helper Layout");
            UnityEngine.Object.DontDestroyOnLoad(go);

            var watcher = go.AddComponent<MenuLayoutWatcher>();
            watcher.Touch = touch;
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Layout] could not watch the pause menu: {e.Message}");
        }
    }

    /// <summary>True once the layout is done (or cannot be done) so the watcher can stop.</summary>
    public static bool TryApply(bool touch)
    {
        try
        {
            if (touch)
            {
                var view = UnityEngine.Object.FindObjectOfType<TouchView>();
                if (view == null)
                    return false;

                bool left = ApplyTo(view.CommandMenuLeftParent, view.PlayerInfoLeftParent);
                bool right = ApplyTo(view.CommandMenuRightParent, view.PlayerInfoRightParent);
                return left && right;
            }

            var keyView = UnityEngine.Object.FindObjectOfType<KeyInputView>();
            if (keyView == null)
                return false;

            return ApplyTo(keyView.CommandMenuParent, keyView.PlayerInfoParent);
        }
        catch (Exception e)
        {
            LogOnce("apply", $"[Layout] could not move the counters: {e.Message}");
            return true;
        }
    }

    private static bool ApplyTo(GameObject box, GameObject info)
    {
        if (box == null || info == null)
            return false;

        var boxRect = box.GetComponent<RectTransform>();
        var infoRect = info.GetComponent<RectTransform>();
        if (boxRect == null || infoRect == null)
            return false;

        if (boxRect.childCount == 0)
            return false;

        // the real row views live under CommandMenuView.contentsParent
        var menuView = UnityEngine.Object.FindObjectOfType<Last.UI.CommandMenuView>();
        var contents = menuView != null && menuView.contentsParent != null
            ? menuView.contentsParent.GetComponent<RectTransform>()
            : null;

        float row = RowHeight(contents);
        if (row <= 1f)
            row = RowHeight(boxRect);

        if (Plugin.Verbose.Value && contents != null)
        {
            Plugin.LogSource.LogInfo(
                $"[Layout] command rows={contents.childCount}, contents={contents.rect.size}, " +
                $"box={boxRect.rect.size}, row={row:0.#}");
        }

        if (row <= 1f)
            return false;

        if (AlreadyMoved.Add(info.GetInstanceID()))
        {
            var position = infoRect.anchoredPosition;
            position.y -= row;
            infoRect.anchoredPosition = position;

            // the window is sized for the old row count; without this the extra row
            // is drawn past the edge and never seen
            var panel = boxRect.GetChild(0).GetComponent<RectTransform>();
            if (panel != null)
            {
                var size = panel.sizeDelta;
                size.y += row;
                panel.sizeDelta = size;

                if (Plugin.Verbose.Value)
                    Plugin.LogSource.LogInfo(
                        $"[Layout] grew {panel.name} to {size.x:0}x{size.y:0} (+{row:0.#})");
            }

            if (Plugin.Verbose.Value)
                Plugin.LogSource.LogInfo(
                    $"[Layout] moved {info.name} down one row ({row:0.#} px) to y={position.y:0.#}");
        }

        return true;
    }

    /// <summary>
    /// The real row height: the closest spacing between two sibling entries that
    /// carry a label. Looking at one container level only used to pick up the
    /// cursor or the frame and produced a nonsense 649 px.
    /// </summary>
    private static float RowHeight(RectTransform container)
    {
        if (container == null || container.childCount < 2)
            return 0f;

        // the rows are evenly spaced, and the container is exactly tall enough for them,
        // so the layout height divided by the row count is the most reliable measure
        float height = container.rect.height;
        if (height <= 1f)
            height = container.sizeDelta.y;

        if (height > 1f)
        {
            float even = height / container.childCount;
            if (even is >= 16f and <= 140f)
                return even;
        }

        var siblings = new Dictionary<int, List<RectTransform>>();
        var parents = new Dictionary<int, Transform>();

        CollectLabelled(container, siblings, parents, 0);

        float best = 0f;
        foreach (var group in siblings.Values)
        {
            if (group.Count < 2)
                continue;

            group.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            for (int i = 1; i < group.Count; i++)
            {
                float gap = Mathf.Abs(group[i - 1].anchoredPosition.y - group[i].anchoredPosition.y);
                if (gap > 1f && (best <= 1f || gap < best))
                    best = gap;
            }
        }

        // a menu row is never 600 px tall; ignore anything outside a sane range
        return best is >= 16f and <= 140f ? best : 0f;
    }

    private static void CollectLabelled(RectTransform parent, Dictionary<int, List<RectTransform>> siblings,
        Dictionary<int, Transform> parents, int depth)
    {
        if (depth > 3)
            return;

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i).GetComponent<RectTransform>();
            if (child == null)
                continue;

            if (child.GetComponent<Text>() != null)
            {
                int key = child.parent.GetInstanceID();
                if (!siblings.TryGetValue(key, out var list))
                {
                    list = new List<RectTransform>();
                    siblings[key] = list;
                    parents[key] = child.parent;
                }

                list.Add(child);
            }

            CollectLabelled(child, siblings, parents, depth + 1);
        }
    }

    /// <summary>One line per ancestor, for the verbose log.</summary>
    public static string Describe(Transform start, int maxDepth)
    {
        var builder = new StringBuilder();
        for (var transform = start; transform != null && maxDepth-- > 0; transform = transform.parent)
        {
            // a Transform wrapper is never a RectTransform in IL2CPP, so ask for the component
            var rect = transform.GetComponent<RectTransform>();
            builder.Append("\n  ").Append(transform.name);

            if (rect != null)
            {
                builder.Append(" rect=").Append(rect.rect.size)
                       .Append(" scale=").Append(rect.lossyScale);
            }

            if (transform.GetComponent<LayoutGroup>() != null)
                builder.Append(" [layout]");
            if (transform.GetComponent<ContentSizeFitter>() != null)
                builder.Append(" [fitter]");
            if (!transform.gameObject.activeSelf)
                builder.Append(" (inactive)");
        }

        return builder.ToString();
    }

    private static void LogOnce(string key, string message)
    {
        if (LoggedOnce.Add(key))
            Plugin.LogSource?.LogWarning(message);
    }
}

/// <summary>Runs until the pause menu has laid its rows out, then stops itself.</summary>
public sealed class MenuLayoutWatcher : MonoBehaviour
{
    public bool Touch { get; set; }

    private int _frames;

    public MenuLayoutWatcher() : base(ClassInjector.DerivedConstructorPointer<MenuLayoutWatcher>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public MenuLayoutWatcher(IntPtr pointer) : base(pointer)
    {
    }

    private void LateUpdate()
    {
        if (++_frames > 120)
        {
            Destroy(this);
            return;
        }

        if (MenuLayout.TryApply(Touch))
            Destroy(this);
    }
}
