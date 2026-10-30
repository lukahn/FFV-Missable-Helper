using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;

namespace FFVMissableHelper;

/// <summary>
/// The FFV Helper screen: locations -> category rows -> the things still missing.
/// </summary>
public sealed class HelperWindowBehaviour : MonoBehaviour
{
    private const int VisibleRows = 12;
    private const float RowHeight = 50f;

    /// <summary>Left edge of the row text: clear of the selector hand and the portrait.</summary>
    private const float TextInset = 50f;
    private const float MenuInset = 24f;

    private enum Level
    {
        Locations,
        Categories,
        Payload
    }

    private sealed class Row
    {
        public GameObject Root;
        public Image Back;
        public Text Main;
        public Text Detail;
        public Image Portrait;
        public Image Cursor;
        public readonly List<Image> Icons = new();
        public Button Button;
        public RectTransform MainRect;
    }

    private CanvasGroup _panelGroup;
    private GameObject _panel;
    private Image _fillImage;
    private Text _title;
    private Text _subtitle;
    private Text _footer;
    private Text _help;
    private Transform _rowParent;
    private RectTransform _rowParentRect;
    private Image _preview;
    private Image _dim;

    private readonly List<Row> _rows = new();
    private readonly List<LocationStatus> _statuses = new();

    /// <summary>
    /// The location list as it is drawn: a world header, then that world's locations, then
    /// the next header. The toggle row is index 0 and is not in here, so display index
    /// i + 1 maps to <c>_locationRows[i]</c>. Headers are skipped by the cursor.
    /// </summary>
    private readonly List<(bool Header, int World, int Status)> _locationRows = new();

    private Level _level = Level.Locations;
    private bool _ready;
    private RectTransform _frame;
    private bool _mirrored;
    private int _mirrorRetries = 30;
    private Image _panelImage;
    private GuideLocation _location;
    private int _categoryIndex;
    private int _selected;
    private int _scroll;
    private int _locationCursor;
    private int _visibleRows = VisibleRows;
    private Action _onClosed;
    private bool _visible;
    private int _openedFrame = -1;
    private bool _tickFailed;
    private bool _openedBefore;

    public bool IsShown => _visible;

    // category rows for the current location
    private readonly List<(string Label, string Key, string Icon, bool Complete)> _categories = new();

    public HelperWindowBehaviour() : base(ClassInjector.DerivedConstructorPointer<HelperWindowBehaviour>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public HelperWindowBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Awake()
    {
        try
        {
            BuildUi();
            _ready = _panel != null && _panelGroup != null && _title != null &&
                     _subtitle != null && _footer != null && _help != null &&
                     _rowParentRect != null && _rows.Count > 0;

            if (!_ready)
                Plugin.LogSource?.LogError("The helper window built incompletely; it will not be shown.");
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not build the helper window: {e}");
        }
    }

    // ------------------------------------------------------------------ public
    public void Show(Action onClosed, Transform host = null, Font font = null)
    {
        // the game destroys its menu hierarchy, and our panel with it
        if (_panel == null)
            Rebuild();

        if (!_ready)
        {
            Plugin.LogSource?.LogError("The helper window is not usable in this game build.");
            onClosed?.Invoke();
            return;
        }

        if (_panelGroup == null || _panel == null)
        {
            Plugin.LogSource?.LogError("The helper window did not finish building.");
            onClosed?.Invoke();
            return;
        }

        _onClosed = onClosed;
        _visible = true;
        HelperWindow.SetOpen(true);
        _openedFrame = Time.frameCount;
        UiKit.ResetIconCache();
        MapImages.Reset();

        // reopening lands where you left off; the first open starts at the top
        bool resume = _openedBefore;
        _openedBefore = true;

        if (!resume)
        {
            _level = Level.Locations;
            _location = null;
            _selected = 0;
            _scroll = 0;
        }

        SetHost(host, font);

        _panelGroup.alpha = 1f;
        _panelGroup.blocksRaycasts = true;
        _panelGroup.interactable = true;
        _panel.SetActive(true);

        Progress.Refresh(force: true);
        RebuildLocations(keepPosition: resume);

        if (Plugin.Verbose.Value)
        {
            var first = _rows.Count > 0 ? _rows[0] : null;
            Plugin.LogSource.LogInfo(
                $"[Window] ready: locations={_statuses.Count}, rows={_rows.Count}, " +
                $"visibleRows={_visibleRows}, selected={_selected}, " +
                $"row0='{(first != null && first.Main != null ? first.Main.text : "?")}'");
        }
    }

    public void Hide() => Hide(notify: true);

    /// <summary>
    /// <paramref name="notify"/> is false when the game is closing the pause menu out from
    /// under us: the menu content must not be switched back on at that point.
    /// </summary>
    public void Hide(bool notify)
    {
        if (!_visible)
            return;

        _visible = false;
        HelperWindow.SetOpen(false);

        try
        {
            if (_panelGroup != null)
            {
                _panelGroup.alpha = 0f;
                _panelGroup.blocksRaycasts = false;
                _panelGroup.interactable = false;
            }

            if (_panel != null)
                _panel.SetActive(false);

            // detach before the game can destroy the menu it lives in
            SetHost(null, null);
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Window] could not hide cleanly: {e.Message}");
        }

        var callback = _onClosed;
        _onClosed = null;

        if (!notify)
            return;

        callback?.Invoke();
    }

    /// <summary>Rebuilds the UI after the game destroyed the objects it was parented to.</summary>
    private void Rebuild()
    {
        try
        {
            _rows.Clear();
            _statuses.Clear();
            _categories.Clear();
            _title = null;
            _subtitle = null;
            _footer = null;
            _help = null;
            _preview = null;
            _rowParent = null;
            _rowParentRect = null;
            _panel = null;
            _panelGroup = null;
            _panelImage = null;
            _dim = null;
            _frame = null;
            _ready = false;

            BuildUi();
            _ready = _panel != null && _panelGroup != null && _title != null &&
                     _subtitle != null && _footer != null && _help != null &&
                     _rowParentRect != null && _rows.Count > 0;

            Plugin.LogSource?.LogInfo($"[Window] rebuilt: {( _ready ? "ok" : "incomplete")}");
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not rebuild the helper window: {e}");
        }
    }

    /// <summary>
    /// Draws the helper inside the game's own menu window. Parenting into that hierarchy
    /// (rather than working out screen coordinates ourselves) means the game's own
    /// scaling and letterboxing apply, so the window is always where the menu is, at any
    /// resolution - including tall remote displays.
    /// </summary>
    private void SetHost(Transform host, Font font)
    {
        _frame = host != null ? ResolveFrame(host) : null;

        if (_dim != null)
            _dim.gameObject.SetActive(false);

        var rect = UiKit.Rect(_panel);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        if (_frame != null)
        {
            // read the menu's own look before our panel joins the hierarchy, or our texts
            // and sprites would be counted as part of the menu
            GameSkin.Harvest(_frame);

            _panel.transform.SetParent(_frame, false);
            rect.localPosition = Vector3.zero;

            // fill the whole menu window
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _mirrored = true;
            _mirrorRetries = 0;

            float height = _frame.rect.height;

            _visibleRows = Mathf.Clamp(Mathf.FloorToInt((height - 274f) / RowHeight), 4, VisibleRows);

            ApplySkin(font);

            if (Plugin.Verbose.Value)
            {
                Plugin.LogSource.LogInfo(
                    $"[Window] inside {_frame.name}: panel={rect.rect.size}, frame={_frame.rect.size}, " +
                    $"rows={_visibleRows}, font={(UiKit.Font != null ? UiKit.Font.name : "?")}");
            }

            return;
        }

        _mirrored = false;
        if (_panel.transform.parent != transform)
            _panel.transform.SetParent(transform, false);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var (viewportSize, viewportCentre) = GameViewportInCanvasUnits();
        rect.sizeDelta = viewportSize * 0.92f;
        rect.anchoredPosition = viewportCentre;

        // rows are 50 px, with 274 px of header/footer around them
        float rowsHeight = rect.rect.height;
        if (rowsHeight < 400f)
            rowsHeight = 820f;

        _visibleRows = Mathf.Clamp(Mathf.FloorToInt((rowsHeight - 274f) / RowHeight), 4, VisibleRows);

        ApplySkin(font);

        if (Plugin.Verbose.Value)
        {
            Plugin.LogSource.LogInfo(
                $"[Window] centred: panel={rect.rect.size}, rows={_visibleRows}" +
                $"\n  host chain:{MenuLayout.Describe(host, 8)}");
        }
    }

    /// <summary>
    /// Dresses the panel in whatever the game's menu could tell us: its window sprite, its
    /// font, and its selector hand. Each one falls back to our own drawing on its own, so a
    /// missing piece only costs that piece.
    /// </summary>
    private void ApplySkin(Font fallbackFont)
    {
        bool fromGame = GameSkin.Frame != null;

        if (_panelImage != null)
        {
            _panelImage.sprite = fromGame ? GameSkin.Frame : UiKit.PanelSprite;
            _panelImage.type = fromGame ? GameSkin.FrameType : Image.Type.Sliced;
            _panelImage.pixelsPerUnitMultiplier = fromGame ? GameSkin.FrameMultiplier : 1f;
            _panelImage.color = Color.white;
        }

        if (_fillImage != null)
        {
            // without the game's sprite the drawn panel already carries its own gradient
            _fillImage.gameObject.SetActive(fromGame);
            _fillImage.rectTransform.offsetMin = new Vector2(PanelInset, PanelInset);
            _fillImage.rectTransform.offsetMax = new Vector2(-PanelInset, -PanelInset);
        }

        if (GameSkin.Font == null && fallbackFont != null)
            UiKit.UseFont(fallbackFont);

        foreach (var text in _panel.GetComponentsInChildren<Text>(true))
            text.font = UiKit.Font;

        var cursor = GameSkin.Cursor != null ? GameSkin.Cursor : UiKit.GetIcon("cursor");
        foreach (var row in _rows)
        {
            if (row.Cursor == null)
                continue;

            row.Cursor.sprite = cursor;
            row.Cursor.preserveAspect = true;
            row.Cursor.enabled = false;
        }
    }

    /// <summary>The whole screen measured in our own canvas' units, whatever the display is.</summary>
    private Vector2 ScreenSizeInCanvasUnits() => GameViewportInCanvasUnits().Size;

    /// <summary>
    /// The game's own visible area in our canvas' units. The game letterboxes its 16:9
    /// view inside the window (very visible on a tall remote display), so the camera's
    /// pixel rect is the right thing to fill - not the whole window.
    /// </summary>
    private (Vector2 Size, Vector2 Centre) GameViewportInCanvasUnits()
    {
        var canvasRect = UiKit.Rect(gameObject);
        var fallback = (new Vector2(1480f, 820f), Vector2.zero);
        if (canvasRect == null)
            return fallback;

        try
        {
            var view = new Rect(0f, 0f, Screen.width, Screen.height);
            var camera = Camera.main;
            if (camera != null && camera.pixelRect.width > 1f && camera.pixelRect.height > 1f)
                view = camera.pixelRect;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, view.min, null, out var min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, view.max, null, out var max))
            {
                var size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
                if (size.x > 100f && size.y > 100f)
                    return (size, (min + max) * 0.5f);
            }
        }
        catch
        {
            // fall through
        }

        return fallback;
    }

    /// <summary>
    /// The pause menu's outer frame. The sub-window container itself is only a
    /// 100x100 anchor box, so walk up to the first ancestor that has a real size.
    /// </summary>
    private static RectTransform ResolveFrame(Transform host)
    {
        for (var transform = host; transform != null; transform = transform.parent)
        {
            var rect = transform.GetComponent<RectTransform>();
            if (rect != null && rect.rect.width >= 300f && rect.rect.height >= 300f)
                return rect;
        }

        return host != null ? host.GetComponent<RectTransform>() : null;
    }

    /// <summary>Copies the on-screen rectangle of the game's menu frame onto our panel.</summary>
    private bool MirrorFrame(RectTransform frame, RectTransform panel)
    {
        var canvasRect = UiKit.Rect(gameObject);
        if (canvasRect == null)
            return false;

        try
        {
            var corners = new Vector3[4];
            frame.GetWorldCorners(corners);

            // The game's menu canvas is not a screen-space overlay one, so its world
            // corners are not pixels: go through its own camera first. Assuming they
            // were pixels put the window far outside a non-1080p screen.
            var gameCanvas = frame.GetComponentInParent<Canvas>();
            var camera = gameCanvas != null && gameCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? gameCanvas.worldCamera
                : null;

            var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bottomLeft, null, out var min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, topRight, null, out var max))
            {
                var size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
                var limit = ScreenSizeInCanvasUnits();

                // never bigger than the screen, and never absurdly small
                size = Vector2.Min(size, limit);
                if (size.x >= 200f && size.y >= 150f)
                {
                    // stay inside the game's frame so it still reads as the menu window
                    size -= new Vector2(MenuInset * 2f, MenuInset * 2f);

                    panel.anchorMin = new Vector2(0.5f, 0.5f);
                    panel.anchorMax = new Vector2(0.5f, 0.5f);
                    panel.pivot = new Vector2(0.5f, 0.5f);
                    panel.sizeDelta = size;
                    panel.anchoredPosition = (min + max) * 0.5f;
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Window] could not read the menu frame: {e.Message}");
        }

        // The menu animates open, so its world size can still be zero. Its layout
        // size is always right, and the frame sits centred in the menu.
        var frameSize = frame.rect.size;
        var screen = ScreenSizeInCanvasUnits();
        if (frameSize.x >= 200f && frameSize.y >= 150f)
        {
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = Vector2.Min(frameSize - new Vector2(MenuInset * 2f, MenuInset * 2f), screen);
            panel.anchoredPosition = Vector2.zero;
            return true;
        }

        return false;
    }

    /// <summary>The local point inside <paramref name="parent"/> that sits over the screen centre.</summary>
    private static Vector2 ScreenCentreIn(RectTransform parent)
    {
        if (parent == null)
            return Vector2.zero;

        try
        {
            var camera = parent.GetComponentInParent<Canvas>()?.rootCanvas?.worldCamera;
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, centre, camera, out var local))
                return local;
        }
        catch
        {
            // fall back to the container's own centre
        }

        return Vector2.zero;
    }

    // ------------------------------------------------------------------ update
    private void Update()
    {
        if (HelperWindow.TakeSuspendRequest())
        {
            HelperWindow.Suspend();
            return;
        }

        try
        {
            Progress.Tick();
        }
        catch (Exception e)
        {
            if (!_tickFailed)
            {
                _tickFailed = true;
                Plugin.LogSource?.LogWarning($"Progress refresh failed: {e.Message}");
            }
        }

        MenuIntegration.PollMenuSelection();

        if (!_visible)
        {
            // the only way in is the pause menu row
            MenuIntegration.PollMenuSelection();
            return;
        }

        // the pause menu (and with it our host frame) can be closed from under us
        if (_mirrored && _frame == null)
        {
            Hide();
            return;
        }

        // the game re-shows its own panels while the menu runs
        if (_mirrored)
            MenuIntegration.KeepMenuContentHidden();

        // the menu animates open, so the frame may have no size on the first frames
        if (!_mirrored && _frame != null && _mirrorRetries-- > 0)
        {
            if (MirrorFrame(_frame, UiKit.Rect(_panel)))
            {
                _mirrored = true;
                var mirroredRect = UiKit.Rect(_panel);
                _visibleRows = Mathf.Clamp(
                    Mathf.FloorToInt((mirroredRect.rect.height - 274f) / RowHeight), 4, VisibleRows);
                PaintRows();
            }
        }

        try
        {
            // the key press that opened the window must not also confirm a row
            if (Time.frameCount == _openedFrame)
                return;

            if (InputCompat.Down(GameKey.Down)) MoveSelection(1);
            else if (InputCompat.Down(GameKey.Up)) MoveSelection(-1);
            else if (InputCompat.Down(GameKey.Right)) MovePage(1);
            else if (InputCompat.Down(GameKey.Left)) MovePage(-1);
            else if (InputCompat.Down(GameKey.Confirm)) Confirm();
            else if (InputCompat.Down(GameKey.Cancel)) Back();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Helper input failed: {e.Message}");
        }
    }

    private void MoveSelection(int delta)
    {
        int count = CurrentCount();
        if (count == 0)
            return;

        int index = _selected;
        for (int guard = 0; guard < count; guard++)
        {
            index = (index + delta + count) % count;
            if (!IsHeader(index))
                break;
        }

        _selected = index;
        EnsureVisible();
        PaintRows();
    }

    /// <summary>The display switches at the top of the location list.</summary>
    private const int ToggleRows = 2;

    /// <summary>Left and Right jump a whole screen of rows, like a page of a menu.</summary>
    private void MovePage(int direction)
    {
        int count = CurrentCount();
        if (count == 0)
            return;

        int steps = Mathf.Max(1, _visibleRows);
        int index = _selected;
        int taken = 0;

        // walk the steps one row at a time so world headings are stepped over, not stopped on
        while (taken < steps && index + direction >= 0 && index + direction < count)
        {
            index += direction;
            if (!IsHeader(index))
                taken++;
        }

        if (index == _selected)
            return;

        _selected = index;
        EnsureVisible();
        PaintRows();
    }

    /// <summary>True for the world headings, which are drawn but cannot be selected.</summary>
    private bool IsHeader(int displayIndex) =>
        _level == Level.Locations &&
        displayIndex >= ToggleRows &&
        displayIndex - ToggleRows < _locationRows.Count &&
        _locationRows[displayIndex - ToggleRows].Header;

    /// <summary>The status behind a display index, or -1 for the switches and headings.</summary>
    private int StatusIndexAt(int displayIndex)
    {
        int index = displayIndex - ToggleRows;
        if (index < 0 || index >= _locationRows.Count)
            return -1;

        return _locationRows[index].Header ? -1 : _locationRows[index].Status;
    }

    private void Confirm()
    {
        switch (_level)
        {
            case Level.Locations:
                if (_selected == 0)
                {
                    // the toggle row
                    Plugin.AreaPopupEnabled.Value = !Plugin.AreaPopupEnabled.Value;

                    // turning it back on has to create the popup: it is only built on demand
                    if (Plugin.AreaPopupEnabled.Value)
                        AreaPopup.Refresh();

                    PaintRows();
                    return;
                }

                if (_selected == 1)
                {
                    Plugin.ShowCompleted.Value = !Plugin.ShowCompleted.Value;
                    PaintRows();
                    return;
                }

                int statusIndex = StatusIndexAt(_selected);
                if (statusIndex < 0)
                    return;

                var status = _statuses[statusIndex];
                _location = status.Location;
                _locationCursor = _selected;
                _level = Level.Categories;
                _categoryIndex = 0;
                _selected = 0;
                _scroll = 0;
                BuildCategories();
                break;

            case Level.Categories:
                if (_categories.Count == 0)
                {
                    Back();
                    return;
                }

                _categoryIndex = _selected;
                _level = Level.Payload;
                _selected = 0;
                _scroll = 0;
                break;

            case Level.Payload:
                TogglePayload();
                break;
        }

        EnsureVisible();
        PaintRows();
    }

    private void Back()
    {
        switch (_level)
        {
            case Level.Locations:
                Hide();
                return;

            case Level.Categories:
                _level = Level.Locations;
                _location = null;

                // come back to the location that was open, not to the top of the list
                _selected = Mathf.Clamp(_locationCursor, 0, Mathf.Max(0, CurrentCount() - 1));
                _scroll = 0;
                break;

            case Level.Payload:
                _level = Level.Categories;
                _selected = _categoryIndex;
                _scroll = 0;
                break;
        }

        EnsureVisible();
        PaintRows();
    }

    /// <summary>
    /// Enter on a payload row: writes the entry into the game itself, so a chest that can no
    /// longer be reached can still be completed (and its item really gained).
    /// </summary>
    private void TogglePayload()
    {
        if (_location == null || _categories.Count == 0)
            return;

        string key = _categories[Mathf.Clamp(_categoryIndex, 0, _categories.Count - 1)].Key;
        if (key == "piano")
        {
            ToggleSelectedPayload();
            return;
        }

        if (key == "notes")
            return;

        var rows = PayloadRows();
        if (_selected < 0 || _selected >= rows.Count)
            return;

        var line = rows[_selected];
        bool changed;

        if (line.Entry != null)
        {
            var entry = line.Entry;
            bool opened = Progress.IsEntryOpened(entry);

            if (entry.Flags == null || entry.Flags.Count == 0)
            {
                // no treasure flag exists for this one: keep it in our own file instead
                Progress.ToggleManual(Progress.ManualKey(entry));
                Progress.SaveManual();
                changed = true;
            }
            else
            {
                changed = Unlock.SetOpened(entry, !opened);

                // best effort: the flag is what the game uses, the item is a bonus
                Unlock.GrantItem(entry, !opened);
            }
        }
        else if (line.Monster != null)
        {
            changed = Unlock.SetMonsterSeen(line.Monster, !Progress.IsMonsterSeen(line.Monster));
        }
        else
        {
            return;
        }

        if (!changed)
            return;

        Progress.Invalidate("helper unlock");
        Progress.Refresh(force: true);
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, PayloadCount() - 1));
        EnsureVisible();
        PaintRows();
    }

    private void ToggleSelectedPayload()
    {
        var pianos = _location?.Pianos;
        if (pianos == null || _categoryIndex != CategoryIndex("piano"))
            return;

        if (_selected < 0 || _selected >= PayloadRows().Count)
            return;

        // the visible rows and the piano list differ when completed ones are hidden, so map
        // through the row itself rather than the cursor
        var row = PayloadRows()[_selected];
        if (row.Piano == null)
            return;

        bool played = Progress.IsPianoPlayed(_location, row.Piano);
        if (!Unlock.SetPianoPlayed(_location, row.Piano, !played))
        {
            // no storage yet: fall back to the hand tick
            Progress.ToggleManual(Progress.ManualKey(_location, row.Piano));
            Progress.SaveManual();
        }

        Progress.Invalidate("helper piano");
        Progress.Refresh(force: true);
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, PayloadCount() - 1));
        EnsureVisible();
        PaintRows();
    }

    // ------------------------------------------------------------------ layout
    private void BuildUi()
    {
        if (GetComponent<Canvas>() == null)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiKit.ReferenceWidth, UiKit.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
        }

        var dimObject = new GameObject("Dim");
        dimObject.transform.SetParent(transform, false);
        _dim = dimObject.AddComponent<Image>();
        _dim.color = new Color(0f, 0f, 0f, 0.55f);
        _dim.raycastTarget = true;
        var dimRect = dimObject.GetComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;

        // off until an actual overlay is wanted: left on, it darkens the whole game
        dimObject.SetActive(false);

        var panel = UiKit.CreatePanel(transform, "Panel");
        _panel = panel.gameObject;
        _panelImage = panel;
        _panelGroup = _panel.AddComponent<CanvasGroup>();
        _panelGroup.alpha = 0f;
        _panelGroup.blocksRaycasts = false;
        _panelGroup.interactable = false;

        var panelRect = UiKit.Rect(_panel);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1480f, 820f);
        panelRect.anchoredPosition = Vector2.zero;

        // The panel wears the game's own window sprite, which is a frame with a soft glow
        // through its middle. The gradient is a child drawn over that glow, inset far enough
        // to stay inside the frame's border.
        _fillImage = NewFillImage(_panel.transform, "Fill");

        _title = UiKit.CreateText(_panel.transform, "Title", 46, TextAnchor.UpperLeft);
        var titleRect = UiKit.Rect(_title.gameObject);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(42f, 0f);
        titleRect.offsetMax = new Vector2(-42f, -26f);
        titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 54f);

        _subtitle = UiKit.CreateText(_panel.transform, "Subtitle", 26, TextAnchor.UpperLeft);
        _subtitle.color = UiKit.Muted;
        var subRect = UiKit.Rect(_subtitle.gameObject);
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.offsetMin = new Vector2(44f, 0f);
        subRect.offsetMax = new Vector2(-44f, -84f);
        subRect.sizeDelta = new Vector2(subRect.sizeDelta.x, 30f);

        // A RectTransform has to exist from the start: bolting one onto a plain
        // GameObject is silently refused, and every child of it collapses to nothing.
        // Creating it through a UI component is the reliable way to get one.
        var rowParent = UiKit.CreatePanel(_panel.transform, "Rows");
        rowParent.sprite = null;
        rowParent.color = new Color(0f, 0f, 0f, 0f);
        rowParent.raycastTarget = false;
        _rowParent = rowParent.transform;
        _rowParentRect = UiKit.Rect(rowParent.gameObject);
        _rowParentRect.anchorMin = new Vector2(0f, 0f);
        _rowParentRect.anchorMax = new Vector2(1f, 1f);
        _rowParentRect.offsetMin = new Vector2(42f, 150f);
        _rowParentRect.offsetMax = new Vector2(-42f, -124f);

        _preview = UiKit.CreateIcon(_panel.transform, "star", 140f);
        var previewRect = UiKit.Rect(_preview.gameObject);
        previewRect.anchorMin = new Vector2(1f, 1f);
        previewRect.anchorMax = new Vector2(1f, 1f);
        previewRect.pivot = new Vector2(1f, 1f);
        previewRect.anchoredPosition = new Vector2(-40f, -118f);
        _preview.gameObject.SetActive(false);

        for (int i = 0; i < VisibleRows; i++)
            _rows.Add(CreateRow(i));

        _footer = UiKit.CreateText(_panel.transform, "Footer", 24, TextAnchor.UpperLeft);
        _footer.color = UiKit.Muted;
        var footerRect = UiKit.Rect(_footer.gameObject);
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.offsetMin = new Vector2(44f, 78f);
        footerRect.offsetMax = new Vector2(-44f, 0f);
        footerRect.sizeDelta = new Vector2(footerRect.sizeDelta.x, 62f);

        _help = UiKit.CreateText(_panel.transform, "Help", 22, TextAnchor.LowerLeft);
        _help.color = UiKit.Muted;
        var helpRect = UiKit.Rect(_help.gameObject);
        helpRect.anchorMin = new Vector2(0f, 0f);
        helpRect.anchorMax = new Vector2(1f, 0f);
        helpRect.pivot = new Vector2(0.5f, 0f);
        helpRect.offsetMin = new Vector2(44f, 22f);
        helpRect.offsetMax = new Vector2(-44f, 0f);
        helpRect.sizeDelta = new Vector2(helpRect.sizeDelta.x, 34f);
        _help.text = "Up/Down  Move      Enter  Select      Esc  Back";

        _panel.SetActive(false);
    }

    /// <summary>How far inside the window frame the gradient starts, in UI units.</summary>
    private const float PanelInset = 14f;

    /// <summary>
    /// The blue fill: a single stretched gradient, tucked just inside the window frame so its
    /// square corners sit under the frame's rounded ones. No shape of its own - the frame
    /// draws the shape.
    /// </summary>
    private static Image NewFillImage(Transform parent, string name) =>
        UiKit.CreateFill(parent, name, PanelInset);

    private Row CreateRow(int index)
    {
        var root = new GameObject("Row" + index);
        root.transform.SetParent(_rowParent, false);

        var back = root.AddComponent<Image>();
        back.color = new Color(1f, 1f, 1f, 0f);
        back.raycastTarget = true;

        var rect = UiKit.Rect(root);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(0f, RowHeight);
        rect.anchoredPosition = new Vector2(0f, -index * RowHeight);

        var button = root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        int captured = index;
        button.onClick.AddListener(new Action(() => OnRowClicked(captured)));

        var main = UiKit.CreateText(root.transform, "Main", 30, TextAnchor.MiddleLeft);
        var mainRect = UiKit.Rect(main.gameObject);
        mainRect.anchorMin = new Vector2(0f, 0f);
        mainRect.anchorMax = new Vector2(1f, 1f);
        mainRect.offsetMin = new Vector2(TextInset, 0f);
        mainRect.offsetMax = new Vector2(-260f, 0f);

        var detail = UiKit.CreateText(root.transform, "Detail", 24, TextAnchor.MiddleRight);
        detail.color = UiKit.Gold;
        var detailRect = UiKit.Rect(detail.gameObject);
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 1f);
        detailRect.offsetMin = new Vector2(0f, 0f);
        detailRect.offsetMax = new Vector2(-150f, 0f);

        var portrait = UiKit.CreateIcon(root.transform, "star", 44f);
        var portraitRect = UiKit.Rect(portrait.gameObject);
        portraitRect.anchorMin = new Vector2(0f, 0.5f);
        portraitRect.anchorMax = new Vector2(0f, 0.5f);
        portraitRect.pivot = new Vector2(0f, 0.5f);
        portraitRect.anchoredPosition = new Vector2(TextInset, 0f);
        portrait.gameObject.SetActive(false);

        // the game's own selector: a pointing hand at the left of the highlighted row
        var cursor = UiKit.CreateIcon(root.transform, "cursor", 30f);
        var cursorRect = UiKit.Rect(cursor.gameObject);
        cursorRect.anchorMin = new Vector2(0f, 0.5f);
        cursorRect.anchorMax = new Vector2(0f, 0.5f);
        cursorRect.pivot = new Vector2(0f, 0.5f);
        cursorRect.anchoredPosition = new Vector2(10f, 0f);
        cursor.enabled = false;

        var row = new Row
        {
            Root = root,
            Back = back,
            Main = main,
            Detail = detail,
            Portrait = portrait,
            Cursor = cursor,
            Button = button,
            MainRect = mainRect
        };

        for (int i = 0; i < 5; i++)
        {
            var icon = UiKit.CreateIcon(root.transform, "star", 30f);
            var iconRect = UiKit.Rect(icon.gameObject);
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-16f - i * 38f, 0f);
            icon.gameObject.SetActive(false);
            row.Icons.Add(icon);
        }

        return row;
    }

    private void OnRowClicked(int index)
    {
        int absolute = _scroll + index;
        if (absolute >= CurrentCount())
            return;

        _selected = absolute;
        Confirm();
    }

    // ---------------------------------------------------------------- content
    private void RebuildLocations(bool keepPosition = false)
    {
        var previousLevel = _level;
        var previousLocation = _location;
        int previousCategory = _categoryIndex;
        int previousSelected = _selected;

        _statuses.Clear();
        _locationRows.Clear();
        Progress.Refresh(force: true);

        int world = int.MinValue;
        foreach (var location in Guide.Locations)
        {
            if (!location.HasAnyContent)
                continue;

            if (location.World != world)
            {
                world = location.World;
                _locationRows.Add((true, world, -1));
            }

            _locationRows.Add((false, world, _statuses.Count));
            _statuses.Add(LocationStatus.Build(location));
        }

        _level = Level.Locations;
        _location = null;

        if (keepPosition && previousLevel != Level.Locations && previousLocation != null)
        {
            _location = previousLocation;
            BuildCategories();

            if (_categories.Count > 0)
            {
                _level = previousLevel;
                _categoryIndex = Mathf.Clamp(previousCategory, 0, _categories.Count - 1);
            }
            else
            {
                _location = null;
            }
        }

        _selected = Mathf.Clamp(keepPosition ? previousSelected : _selected, 0,
                                Mathf.Max(0, CurrentCount() - 1));
        EnsureVisible();
        PaintRows();
    }

    /// <summary>How much is still outstanding in one world, for its heading.</summary>
    private string WorldSummary(int world)
    {
        int left = 0;
        foreach (var entry in _locationRows)
        {
            if (entry.Header || entry.World != world)
                continue;

            left += _statuses[entry.Status].Remaining;
        }

        return left > 0 ? $"{left} left" : "all done";
    }

    private void BuildCategories()
    {
        _categories.Clear();

        var status = LocationStatus.Build(_location);
        if (status.HasChests)
            _categories.Add(($"{status.ChestsDone} / {status.ChestsTotal}", "chests", "chest",
                AllDone(status.ChestsDone, status.ChestsTotal)));
        if (status.HasItems)
            _categories.Add(($"{status.ItemsDone} / {status.ItemsTotal}", "items", "potion",
                AllDone(status.ItemsDone, status.ItemsTotal)));
        if (status.HasBestiary)
            _categories.Add(($"{status.BestiaryDone} / {status.BestiaryTotal}", "bestiary", "chocobo",
                AllDone(status.BestiaryDone, status.BestiaryTotal)));
        if (status.HasBlue)
            _categories.Add(($"{status.BlueDone} / {status.BlueTotal}", "blue", "hat",
                AllDone(status.BlueDone, status.BlueTotal)));
        if (status.HasPianos)
            _categories.Add(($"{status.PianoDone} / {status.PianoTotal}", "piano", "piano",
                AllDone(status.PianoDone, status.PianoTotal)));
        if (_location.Missables.Count > 0)
            _categories.Add(($"{_location.Missables.Count} notes", "notes", "notes", false));
    }

    /// <summary>A category is only "complete" when it actually has something to collect.</summary>
    private static bool AllDone(int done, int total) => total > 0 && done >= total;

    private int CategoryIndex(string key)
    {
        for (int i = 0; i < _categories.Count; i++)
        {
            if (_categories[i].Key == key)
                return i;
        }

        return -1;
    }

    private int CurrentCount()
    {
        switch (_level)
        {
            case Level.Locations:
                return _locationRows.Count + ToggleRows; // the display switches live at the top
            case Level.Categories:
                return _categories.Count;
            case Level.Payload:
                return PayloadCount();
            default:
                return 0;
        }
    }

    private int PayloadCount()
    {
        if (_location == null || _categories.Count == 0)
            return 0;

        var status = LocationStatus.Build(_location);
        bool showDone = Plugin.ShowCompleted.Value;

        return _categories[_categoryIndex].Key switch
        {
            "chests" => showDone ? status.AllChests.Count : status.MissingChests.Count,
            "items" => showDone ? status.AllItems.Count : status.MissingItems.Count,
            "bestiary" => showDone ? status.AllBestiary.Count : status.MissingBestiary.Count,
            "blue" => showDone ? status.AllBlue.Count : status.MissingBlue.Count,
            "piano" => showDone ? status.AllPianos.Count : status.MissingPianos.Count,
            "notes" => _location.Missables.Count,
            _ => 0
        };
    }

    private void EnsureVisible()
    {
        if (_selected < _scroll)
            _scroll = _selected;
        else if (_selected >= _scroll + _visibleRows)
            _scroll = _selected - _visibleRows + 1;

        int count = CurrentCount();
        _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, count - _visibleRows));
    }

    // ------------------------------------------------------------------ paint
    private void PaintRows()
    {
        var status = _location != null ? LocationStatus.Build(_location) : null;

        switch (_level)
        {
            case Level.Locations:
                PaintLocations();
                break;
            case Level.Categories:
                PaintCategories(status);
                break;
            case Level.Payload:
                PaintPayload(status);
                break;
        }

        PaintCursor();
        PaintFrame(status);
    }

    /// <summary>The pointing hand sits on the last painted row that the cursor is on.</summary>
    private void PaintCursor()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Cursor == null)
                continue;

            bool show = row.Root != null && row.Root.activeSelf &&
                        _scroll + i == _selected && row.Cursor.sprite != null;
            row.Cursor.enabled = show;
        }
    }

    private void PaintFrame(LocationStatus status)
    {
        if (_title == null || _subtitle == null || _footer == null || _help == null)
            return;

        switch (_level)
        {
            case Level.Locations:
                _title.text = "FFV Helper";
                _subtitle.text = BuildTotalsLine();
                _footer.text =
                    "Turn the area popup on or off, then pick a location.   " +
                    "★ means everything here is done.\n" +
                    "<color=#F0C060>*</color> means the location has missable content " +
                    "- the Notes category lists what it is.";
                break;

            case Level.Categories:
                _title.text = $"{_location.Order:00}   {_location.Name}";
                _subtitle.text =
                    $"{(_location.Missable ? "<color=#F0C060>MISSABLE</color>   " : "")}" +
                    $"World {_location.World}   {status.Remaining} left" +
                    (string.IsNullOrEmpty(_location.Note) ? "" : $"   {_location.Note}");
                _footer.text = "Pick a category to see exactly what is left.";
                break;

            case Level.Payload:
                _title.text = $"{_location.Order:00}   {_location.Name} - " + CategoryTitle();
                _subtitle.text = CategoryProgress(status);
                _footer.text = _selected < FooterLines().Count ? FooterLines()[_selected] : "";
                break;
        }

        _help.text = _level == Level.Locations
            ? "Up/Down  Move      Left/Right  Page      Enter  Select      Esc  Close"
            : "Up/Down  Move      Left/Right  Page      Enter  Select      Esc  Back";

        UpdatePreview();
    }

    /// <summary>
    /// Shows the picture of the highlighted entry beside the list: a monster
    /// portrait for the bestiary, a map or screenshot for chests and items.
    /// </summary>
    private void UpdatePreview()
    {
        if (_preview == null || _rowParentRect == null)
            return;

        bool bestiary = false;
        bool mapCategory = false;

        if (_level == Level.Payload && _categories.Count > 0)
        {
            string key = _categories[Mathf.Clamp(_categoryIndex, 0, _categories.Count - 1)].Key;
            bestiary = key == "bestiary";
            mapCategory = key == "chests" || key == "items";
        }

        Sprite sprite = null;
        var rows = bestiary || mapCategory ? PayloadRows() : null;

        if (bestiary && rows != null && _selected >= 0 && _selected < rows.Count &&
            rows[_selected].Picture > 0)
        {
            sprite = UiKit.GetBestiaryImage(rows[_selected].Picture);
        }
        else if (mapCategory && rows != null && _selected >= 0 && _selected < rows.Count)
        {
            // prefer the entry's own picture, then a map for the whole area
            sprite = MapImages.Get(rows[_selected].MapImage);
            if (sprite == null && _location != null)
            {
                sprite = MapImages.Get(_location.Name + ".png");
                if (sprite == null)
                    sprite = MapImages.Get(_location.Name.Replace(' ', '_') + ".png");
            }
        }

        _preview.sprite = sprite;
        _preview.enabled = sprite != null;
        _preview.gameObject.SetActive(sprite != null);

        bool large = mapCategory && sprite != null;
        var rect = _preview.rectTransform;
        rect.sizeDelta = large ? new Vector2(400f, 320f) : new Vector2(140f, 140f);
        rect.anchoredPosition = large ? new Vector2(-40f, -110f) : new Vector2(-40f, -118f);

        // leave room for the picture while it is showing
        _rowParentRect.offsetMax = new Vector2(sprite != null ? (large ? -460f : -200f) : -42f, -124f);
    }

    private void PaintLocations()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            int absolute = _scroll + i;
            var row = _rows[i];

            if (i >= _visibleRows || absolute >= _locationRows.Count + ToggleRows)
            {
                row.Root.SetActive(false);
                continue;
            }

            row.Root.SetActive(true);
            row.Main.color = absolute == _selected ? UiKit.Gold : UiKit.TextColor;
            row.Back.color = absolute == _selected
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(1f, 1f, 1f, 0f);

            ClearIcons(row);
            SetPortrait(row, 0);
            row.MainRect.offsetMin = new Vector2(TextInset, 0f);

            if (absolute == 0)
            {
                row.Main.text = "Area popup on entering a location";
                row.Detail.text = Plugin.AreaPopupEnabled.Value ? "On" : "Off";
                row.Detail.color = Plugin.AreaPopupEnabled.Value ? UiKit.Gold : UiKit.Muted;
                continue;
            }

            if (absolute == 1)
            {
                row.Main.text = "Show finished entries in the lists";
                row.Detail.text = Plugin.ShowCompleted.Value ? "On" : "Off";
                row.Detail.color = Plugin.ShowCompleted.Value ? UiKit.Gold : UiKit.Muted;
                continue;
            }

            var entry = _locationRows[absolute - ToggleRows];
            if (entry.Header)
            {
                // a heading, not a selection: the later worlds are only there for reference
                row.Main.text = $"WORLD {entry.World}";
                row.Main.color = UiKit.Muted;
                row.Detail.text = WorldSummary(entry.World);
                row.Detail.color = UiKit.Muted;
                continue;
            }

            var status = _statuses[entry.Status];
            var location = status.Location;

            string name = location.Name;
            if (location.Missable)
                name = "<color=#F0C060>*</color> " + name;

            // the guide's own number, so "where I got up to" is one glance away
            row.Main.text = $"<color=#8E8E8E>{location.Order:00}</color>   {name}";
            row.Detail.text = status.Complete ? "" : $"{status.Remaining} left";
            row.Detail.color = UiKit.Muted;

            // a finished location reads gold, the same colour the game uses for what is done
            if (status.Complete)
                row.Main.color = UiKit.Gold;

            if (status.Complete)
            {
                SetIcon(row, 0, "star");
                if (location.Missables.Count > 0)
                    SetIcon(row, 1, "notes");
                continue;
            }

            int slot = 0;
            foreach (string icon in status.MissingIcons())
                SetIcon(row, slot++, icon);
        }
    }

    private void PaintCategories(LocationStatus status)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= _visibleRows || i >= _categories.Count)
            {
                row.Root.SetActive(false);
                continue;
            }

            row.Root.SetActive(true);
            var category = _categories[i];
            row.Main.text = CategoryTitle(category.Key);
            row.Detail.text = category.Label;
            row.Detail.color = UiKit.TextColor;
            row.Main.color = i == _selected ? UiKit.Gold : UiKit.TextColor;
            row.Back.color = i == _selected
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(1f, 1f, 1f, 0f);

            ClearIcons(row);
            SetPortrait(row, 0);
            row.MainRect.offsetMin = new Vector2(TextInset, 0f);
            SetIcon(row, 0, category.Icon);

            // a finished part of the location gets a star, like the location row does
            if (category.Complete)
            {
                SetIcon(row, 1, "star");
                row.Detail.color = UiKit.Gold;
            }
        }
    }

    private void PaintPayload(LocationStatus status)
    {
        var rows = PayloadRows();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int absolute = _scroll + i;

            if (i >= _visibleRows || absolute >= rows.Count)
            {
                row.Root.SetActive(false);
                continue;
            }

            row.Root.SetActive(true);
            var item = rows[absolute];
            row.Main.text = item.Main;
            row.Detail.text = item.Detail;
            row.Detail.color = item.DetailColor;
            row.Main.color = absolute == _selected
                ? UiKit.Gold
                : item.Done ? UiKit.Muted : UiKit.TextColor;
            row.Back.color = absolute == _selected
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(1f, 1f, 1f, 0f);

            ClearIcons(row);
            if (!string.IsNullOrEmpty(item.Icon))
                SetIcon(row, 0, item.Icon);

            SetPortrait(row, item.Picture);
            row.MainRect.offsetMin = new Vector2(item.Picture > 0 ? TextInset + 54f : TextInset, 0f);
        }
    }

    private static void SetPortrait(Row row, int picture)
    {
        var sprite = picture > 0 ? UiKit.GetBestiaryImage(picture) : null;
        row.Portrait.sprite = sprite;
        row.Portrait.enabled = sprite != null;
        row.Portrait.gameObject.SetActive(sprite != null);
    }

    private sealed class PayloadLine
    {
        public string Main;
        public string Detail;
        public Color DetailColor = UiKit.Muted;
        public string Icon;
        public string Guide;
        public int Picture;                 // bestiary number, 0 when there is no picture
        public string MapImage;             // map / screenshot file name for chests and items
        public bool Done;                   // only set when finished entries are being shown
        public GuideEntry Entry;            // chest / item entry behind this row, if any
        public BestiaryEntry Monster;       // bestiary entry behind this row, if any
        public PianoEntry Piano;            // piano behind this row, if any
    }

    private List<PayloadLine> PayloadRows()
    {
        var lines = new List<PayloadLine>();
        if (_location == null || _categories.Count == 0)
            return lines;

        var status = LocationStatus.Build(_location);
        bool showDone = Plugin.ShowCompleted.Value;
        switch (_categories[_categoryIndex].Key)
        {
            case "chests":
                if (showDone)
                {
                    foreach (var (entry, done) in status.AllChests)
                        lines.Add(MarkDone(EntryLine(entry), done));
                }
                else
                {
                    foreach (var entry in status.MissingChests)
                        lines.Add(EntryLine(entry));
                }
                break;
            case "items":
                if (showDone)
                {
                    foreach (var (entry, done) in status.AllItems)
                        lines.Add(MarkDone(EntryLine(entry), done));
                }
                else
                {
                    foreach (var entry in status.MissingItems)
                        lines.Add(EntryLine(entry));
                }
                break;
            case "bestiary":
                foreach (var (monster, done) in showDone
                             ? status.AllBestiary
                             : status.MissingBestiary.Select(m => (m, false)).ToList())
                {
                    lines.Add(MarkDone(new PayloadLine
                    {
                        Main = monster.N > 0
                            ? $"{monster.N:000}  {monster.Name}"
                            : monster.Name,
                        Detail = _location.Name,
                        Icon = "chocobo",
                        Picture = monster.N,
                        Guide = monster.Monsters.Count == 0
                            ? "No monster id is known for this entry."
                            : "Not recorded in the picture book yet.",
                        Monster = monster
                    }, done));
                }
                break;
            case "blue":
                foreach (var (spell, done) in showDone
                             ? status.AllBlue
                             : status.MissingBlue.Select(s => (s, false)).ToList())
                {
                    lines.Add(MarkDone(new PayloadLine
                    {
                        Main = spell.Name,
                        Detail = string.Join(", ", spell.Enemies),
                        DetailColor = UiKit.Muted,
                        Icon = "hat",
                        Guide = "Learn it from " + string.Join(", ", spell.Enemies) + "."
                    }, done));
                }
                break;
            case "piano":
                foreach (var (piano, done) in showDone
                             ? status.AllPianos
                             : status.MissingPianos.Select(p => (p, false)).ToList())
                {
                    lines.Add(MarkDone(new PayloadLine
                    {
                        Main = piano.Name,
                        Detail = done ? "done" : "Press Enter once you have played it",
                        Icon = "piano",
                        Guide = "Play the piano here. The game records each one, so this ticks itself.",
                        Piano = piano
                    }, done));
                }
                break;
            case "notes":
                foreach (string note in _location.Missables)
                {
                    var parts = WrapNote(note, 74);
                    for (int i = 0; i < parts.Count; i++)
                    {
                        lines.Add(new PayloadLine
                        {
                            Main = parts[i],
                            Detail = "",
                            Icon = i == 0 ? "notes" : null,
                            Guide = ""
                        });
                    }
                }
                break;
        }

        return lines;
    }

    private PayloadLine EntryLine(GuideEntry entry)
    {
        string detail = entry.Where;
        if (string.IsNullOrEmpty(detail))
            detail = entry.Late ? "World 3 revisit" : "No guide entry";

        // nothing in the game's save records these, so they can only be ticked by hand
        if (entry.Flags == null || entry.Flags.Count == 0)
            detail = "no flag - Enter to tick";

        return new PayloadLine
        {
            Main = string.IsNullOrEmpty(entry.Label) ? entry.Item : $"{entry.Label}   {entry.Item}",
            Detail = detail,
            Icon = entry.Kind == "chest" ? "chest" : "potion",
            Guide = string.IsNullOrEmpty(entry.Guide) ? detail : entry.Guide,
            MapImage = entry.Image,
            Entry = entry
        };
    }

    /// <summary>Dims a row and marks it, when the finished entries are being shown.</summary>
    private static PayloadLine MarkDone(PayloadLine line, bool done)
    {
        if (!done || line == null)
            return line;

        line.Done = true;
        line.Main = "\u2713   " + line.Main;
        line.Detail = "done";
        line.DetailColor = UiKit.Muted;
        return line;
    }

    /// <summary>Splits a note into rows that fit the list width.</summary>
    private static List<string> WrapNote(string text, int width)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(text))
            return parts;

        string rest = text.Trim();
        while (rest.Length > width)
        {
            int cut = rest.LastIndexOf(' ', width - 1);
            if (cut <= 0)
                cut = width;

            parts.Add(rest.Substring(0, cut).TrimEnd());
            rest = rest.Substring(cut).TrimStart();
        }

        if (rest.Length > 0)
            parts.Add(rest);

        return parts;
    }

    private List<string> FooterLines()
    {
        var lines = new List<string>();
        foreach (var line in PayloadRows())
            lines.Add(line.Guide ?? "");
        return lines;
    }

    private string CategoryTitle(string key = null)
    {
        key ??= _categories.Count > 0 ? _categories[_categoryIndex].Key : "";
        return key switch
        {
            "chests" => "Chests",
            "items" => "Items",
            "bestiary" => "Bestiary",
            "blue" => "Blue magic",
            "piano" => "Piano",
            "notes" => "Notes",
            _ => ""
        };
    }

    private string CategoryProgress(LocationStatus status)
    {
        if (status == null || _categories.Count == 0)
            return "";

        _categoryIndex = Mathf.Clamp(_categoryIndex, 0, _categories.Count - 1);
        var key = _categories[_categoryIndex].Key;
        return key switch
        {
            "chests" => $"{status.ChestsDone} / {status.ChestsTotal}",
            "items" => $"{status.ItemsDone} / {status.ItemsTotal}",
            "bestiary" => $"{status.BestiaryDone} / {status.BestiaryTotal}",
            "blue" => $"{status.BlueDone} / {status.BlueTotal}",
            "piano" => $"{status.PianoDone} / {status.PianoTotal}",
            "notes" => "Things the helper cannot track",
            _ => ""
        };
    }

    private string BuildTotalsLine()
    {
        int chests = 0, chestsDone = 0, items = 0, itemsDone = 0;
        int bestiary = 0, bestiaryDone = 0, blue = 0, blueDone = 0, pianos = 0, pianosDone = 0;

        foreach (var status in _statuses)
        {
            chests += status.ChestsTotal;
            chestsDone += status.ChestsDone;
            items += status.ItemsTotal;
            itemsDone += status.ItemsDone;
            bestiary += status.BestiaryTotal;
            bestiaryDone += status.BestiaryDone;
            blue += status.BlueTotal;
            blueDone += status.BlueDone;
            pianos += status.PianoTotal;
            pianosDone += status.PianoDone;
        }

        return $"Chests {chestsDone}/{chests}   Items {itemsDone}/{items}   " +
               $"Bestiary {bestiaryDone}/{bestiary}   Blue magic {blueDone}/{blue}   " +
               $"Pianos {pianosDone}/{pianos}";
    }

    // ------------------------------------------------------------------ icons
    private static void ClearIcons(Row row)
    {
        for (int i = 0; i < row.Icons.Count; i++)
        {
            var icon = row.Icons[i];
            icon.gameObject.SetActive(false);
            icon.rectTransform.anchoredPosition = new Vector2(-16f - i * 38f, 0f);
        }
    }

    private static void SetIcon(Row row, int slot, string name)
    {
        if (slot < 0 || slot >= row.Icons.Count)
            return;

        var icon = row.Icons[slot];
        icon.sprite = UiKit.GetIcon(name);
        icon.enabled = icon.sprite != null;
        icon.gameObject.SetActive(icon.sprite != null);
    }
}
