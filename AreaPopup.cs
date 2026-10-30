using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.Injection;
using Last.Management;
using UnityEngine;
using UnityEngine.UI;

namespace FFVMissableHelper;

/// <summary>Creates and keeps the single area popup behaviour.</summary>
internal static class AreaPopup
{
    private static AreaPopupBehaviour _instance;

    public static void EnsureTypeRegistered()
    {
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<AreaPopupBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<AreaPopupBehaviour>();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not register the area popup: {e}");
        }
    }

    public static void EnsureCreated()
    {
        if (_instance != null)
            return;

        if (!Plugin.AreaPopupEnabled.Value)
            return;

        try
        {
            var go = new GameObject("FFV Helper Area Popup");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<AreaPopupBehaviour>();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not create the area popup: {e}");
        }
    }

    /// <summary>
    /// Re-renders the popup text on the next frame.
    ///
    /// This is deliberately deferred: the game events that call it (setting the area or map
    /// id, adding an item, opening a chest) run inside the game's own save and map-transition
    /// code, and rebuilding the text there means reading half the save file synchronously in
    /// the middle of it. On Windows that was quick enough to get away with; on a Steam Deck
    /// it is exactly the sort of interruption that can leave a transition stuck on black.
    ///
    /// It is also where the behaviour is created. Creating it at plugin load put the GameObject
    /// in no scene at all, so it never ran (the popup has never actually appeared); by the time
    /// a game event fires there is a scene to belong to.
    /// </summary>
    public static void Refresh()
    {
        EnsureCreated();

        if (_instance != null)
            _instance.MarkStale();
    }
}

/// <summary>
/// The top-left summary shown the first time the player walks into an area.
/// Sub-areas (dungeon floors, shops) share the same area id, so this only fires
/// on a real area change.
/// </summary>
public sealed class AreaPopupBehaviour : MonoBehaviour
{
    private const float PollSeconds = 0.25f;
    private const float FadeIn = 0.18f;
    private const float FadeOut = 0.5f;

    private CanvasGroup _group;
    private Text _title;
    private Text _body;
    private RectTransform _box;
    private GuideLocation _location;

    private int _lastAreaId = -1;
    private float _nextPoll;
    private float _shownAt;
    private float _hideAt;
    private bool _visible;
    private bool _stale;

    public bool IsVisible => _visible;

    /// <summary>Re-render the text on the next Update, never inside the game's own call.</summary>
    public void MarkStale() => _stale = true;

    public AreaPopupBehaviour() : base(ClassInjector.DerivedConstructorPointer<AreaPopupBehaviour>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public AreaPopupBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Awake()
    {
        try
        {
            BuildUi();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not build the area popup: {e}");
        }
    }

    private void Update()
    {
        try
        {
            if (_stale)
            {
                _stale = false;
                if (_visible)
                    RefreshText();
            }

            if (UnityEngine.Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = UnityEngine.Time.unscaledTime + PollSeconds;
                PollArea();
            }

            UpdateVisibility();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Area popup update failed: {e.Message}");
        }
    }

    private void PollArea()
    {
        var userData = UserDataManager.Instance();

        if (userData == null)
            return;

        int areaId = userData.CurrentAreaId;
        if (areaId == _lastAreaId)
            return;

        _lastAreaId = areaId;

        if (areaId <= 0 || areaId == 1 || areaId == 101)
            return;                       // world map: no single "area" to announce

        if (!Plugin.AreaPopupEnabled.Value)
            return;

        if (HelperWindow.IsOpen)
            return;

        var location = Guide.FindByArea(areaId);
        if (location == null || !location.HasAnyContent)
            return;

        // nothing left to collect here, so there is nothing to announce
        Progress.Refresh(force: true);
        if (LocationStatus.Build(location).Complete)
        {
            if (Plugin.Verbose.Value)
                Plugin.LogSource.LogInfo($"[Popup] {location.Name} is complete; no popup.");
            return;
        }

        _location = location;
        RefreshText();

        _shownAt = UnityEngine.Time.unscaledTime;
        _hideAt = _shownAt + Mathf.Max(2f, Plugin.AreaPopupSeconds.Value);
        _visible = true;
        _group.alpha = 0f;
    }

    public void RefreshText()
    {
        if (_location == null || _title == null)
            return;

        Progress.Refresh(force: true);
        var status = LocationStatus.Build(_location);

        _title.text = $"<size=17><color=#9A9A9A>WORLD {_location.World}</color></size>   " +
                      _location.Name +
                      (_location.Missable ? "   <size=17><color=#F0C060>MISSABLE</color></size>" : "");

        var builder = new StringBuilder();
        if (status.HasChests)
            builder.AppendLine(Line("Chests", status.ChestsDone, status.ChestsTotal));
        if (status.HasItems)
            builder.AppendLine(Line("Items", status.ItemsDone, status.ItemsTotal));
        if (status.HasBestiary)
            builder.AppendLine(Line("Bestiary", status.BestiaryDone, status.BestiaryTotal));
        if (status.HasBlue)
            builder.AppendLine(Line("Blue magic", status.BlueDone, status.BlueTotal));
        if (status.HasPianos)
            builder.AppendLine(Line("Piano", status.PianoDone, status.PianoTotal));

        if (status.Complete)
            builder.AppendLine("<color=#F0C060>All done here</color>");

        // how the world you are standing in is doing, not the whole game
        builder.Append(WorldLine(_location.World));

        _body.text = builder.ToString().TrimEnd();

        float height = Mathf.Max(96f, _body.preferredHeight + _title.preferredHeight + 40f);
        _box.sizeDelta = new Vector2(440f, height);
    }

    /// <summary>Outstanding work in one world, as a single muted line.</summary>
    private static string WorldLine(int world)
    {
        int left = 0;
        try
        {
            foreach (var candidate in Guide.Locations)
            {
                if (candidate.World != world || !candidate.HasAnyContent)
                    continue;

                left += LocationStatus.Build(candidate).Remaining;
            }
        }
        catch
        {
            return "";
        }

        return left > 0
            ? $"<color=#9A9A9A>{left} left in World {world}</color>"
            : $"<color=#F0C060>World {world} is complete</color>";
    }

    private static string Line(string label, int done, int total)
    {
        string color = done >= total ? "#9BE39B" : "#FFFFFF";
        return $"{label}: <color={color}>{done}/{total}</color>";
    }

    private void UpdateVisibility()
    {
        if (!_visible || _group == null)
            return;

        float now = UnityEngine.Time.unscaledTime;

        if (now < _shownAt + FadeIn)
        {
            _group.alpha = Mathf.Clamp01((now - _shownAt) / FadeIn);
            return;
        }

        if (now < _hideAt)
        {
            _group.alpha = 1f;
            return;
        }

        float fade = Mathf.Clamp01((now - _hideAt) / FadeOut);
        _group.alpha = 1f - fade;
        if (fade >= 1f)
        {
            _visible = false;
            _group.alpha = 0f;
        }
    }

    private void BuildUi()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(UiKit.ReferenceWidth, UiKit.ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var box = UiKit.CreatePanel(transform, "Box");
        _box = UiKit.Rect(box.gameObject);
        _box.anchorMin = new Vector2(0f, 1f);
        _box.anchorMax = new Vector2(0f, 1f);
        _box.pivot = new Vector2(0f, 1f);
        _box.anchoredPosition = new Vector2(28f, -28f);
        _box.sizeDelta = new Vector2(440f, 120f);

        // Same arrangement as the main window: the box is only a frame, and the background is
        // a plain stretched gradient laid inside it. A gradient with no nine-slice of its own
        // cannot warp into a shape, so the panel resizing to fit the text is harmless.
        UiKit.CreateFill(box.transform, "Fill", UiKit.RingThickness);

        _title = UiKit.CreateText(box.transform, "Title", 24, TextAnchor.UpperLeft);
        var titleRect = UiKit.Rect(_title.gameObject);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(20f, 0f);
        titleRect.offsetMax = new Vector2(-20f, -14f);
        titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 30f);
        _title.color = UiKit.Gold;

        _body = UiKit.CreateText(box.transform, "Body", 20, TextAnchor.UpperLeft);
        _body.color = UiKit.TextColor;
        var bodyRect = UiKit.Rect(_body.gameObject);
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.offsetMin = new Vector2(20f, 0f);
        bodyRect.offsetMax = new Vector2(-20f, -46f);
        bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, 60f);
    }
}
