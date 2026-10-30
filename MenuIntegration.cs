using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Data;
using Last.Defaine;
using UnityEngine;
using KeyInputMenu = Last.UI.KeyInput.MainMenuController;
using KeyInputView = Last.UI.KeyInput.MainMenuView;
using TouchView = Last.UI.Touch.MainMenuView;

namespace FFVMissableHelper;

/// <summary>
/// Adds the "FFV Helper" row under Load and opens the helper over the pause menu.
///
/// Hard-won rules, in order of how much they cost:
///
/// 1. Only UpdateNone is hooked. OnCommandMenuKeyAction and SetNextState are called from the
///    field as well as the menu, and patching them makes Harmony's detour for an IL2CPP method
///    call itself through il2cpp_runtime_invoke until the stack overflows (the crash log shows
///    the recursion with no managed frame of ours in it at all). InitWords and UpdateWords are
///    not hooked either, because the game is never steered into that state.
/// 2. Nothing inside a hook may touch an IL2CPP object unless it is the menu path itself.
///    HelperWindow.IsOpen is a plain managed bool for exactly that reason.
/// 3. The pause menu keeps running while our window is open - it reads the same keys and
///    would act on them - so UpdateNone is skipped for as long as the helper is showing.
/// 4. The game answers a confirm on an unknown command with its error sound, so the press is
///    taken in the prefix, before the menu body ever sees it.
/// </summary>
internal static class MenuIntegration
{
    public const string HelperLabel = "FFV Helper";

    /// <summary>An unused pause-menu command id to hang our own row on. Never entered.</summary>
    private const MenuCommandId HelperState = MenuCommandId.Words;

    private static bool _rowAdded;
    private static int _rowCheckedFor = -1;
    private static KeyInputMenu _menu;
    private static Last.UI.CommandMenuController _controller;

    public static void Apply(Harmony harmony)
    {
        if (!Plugin.MenuRowEnabled.Value)
        {
            Plugin.LogSource.LogInfo("[Menu] disabled in the config (Debug > MenuRow); no hooks applied.");
            return;
        }

        SafePatch(harmony, typeof(KeyInputMenu), "UpdateNone",
            prefix: nameof(UpdateNonePrefix), postfix: nameof(UpdateNonePostfix));

        // the game closes the menu itself when the player walks away; tidy up after it
        SafePatch(harmony, typeof(KeyInputMenu), "Close", postfix: nameof(MenuClosedPostfix));
    }

    // --------------------------------------------------------------------- row
    /// <summary>
    /// Runs before the game's own command-list update. Two jobs: take a confirm that is aimed
    /// at our row, and skip the update entirely while the helper is showing.
    /// </summary>
    private static bool UpdateNonePrefix()
    {
        if (!HelperWindow.IsOpen && _rowAdded && CursorOnHelperRow())
        {
            if (InputCompat.Down(GameKey.Confirm))
            {
                Plugin.Trace("confirm on helper row");
                OpenFromMenu();
            }
        }

        if (!HelperWindow.IsOpen)
            return true;

        return false;
    }

    private static void UpdateNonePostfix(KeyInputMenu __instance)
    {
        try
        {
            HelperWindow.EnsureCreated();

            if (HelperWindow.IsOpen)
                return;

            // the menu was dismissed while the helper was up, so our hiding of its own content
            // was never undone - do it here, now that the menu is properly open again
            if (_contentHidden)
            {
                Plugin.Trace("restoring the menu content after a silent close");
                SetMenuContentVisible(true);
            }

            _menu = __instance;

            int id = __instance.GetInstanceID();
            if (_rowCheckedFor == id && _rowAdded && RowPresent())
                return;

            _rowCheckedFor = id;
            _rowAdded = false;
            AddRowToMenu();
        }
        catch (Exception e)
        {
            LogOnce("update-none", $"[Menu] could not add the helper row: {e.Message}");
        }
    }

    /// <summary>The pause menu has gone: forget the row and put the window to sleep.</summary>
    private static void MenuClosedPostfix()
    {
        // managed state only - the game calls this while the player is walking around
        HelperWindow.RequestSuspend();

        _rowAdded = false;
        _rowCheckedFor = -1;
        _controller = null;
        _view = null;
    }

    private static bool CursorOnHelperRow()
    {
        try
        {
            var views = _controller != null ? _controller.contents : null;
            var cursor = _controller != null ? _controller.selectCursor : null;
            if (views == null || cursor == null || views.Count == 0)
                return false;

            // our row is always the last one
            if (cursor.Index != views.Count - 1)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RowPresent()
    {
        try
        {
            var views = _controller != null ? _controller.contents : null;
            if (views == null)
                return false;

            foreach (var view in views)
            {
                if (view != null && view.Data != null && view.Data.Id == HelperState)
                    return true;
            }
        }
        catch
        {
            // treat as missing so the row is rebuilt
        }

        return false;
    }

    private static bool AddRowToMenu()
    {
        var controller = UnityEngine.Object.FindObjectOfType<Last.UI.CommandMenuController>();
        var view = UnityEngine.Object.FindObjectOfType<Last.UI.CommandMenuView>();
        if (controller == null || view == null || view.contentsParent == null)
            return false;

        var views = controller.contents;
        if (views == null)
            return false;

        _controller = controller;

        if (RowPresent())
        {
            _rowAdded = true;
            return false;
        }

        var created = controller.CreateCommandMenuContent(
            view.contentsParent, new CommandMenuContentData(HelperState, HelperLabel));

        if (created == null)
        {
            Plugin.Trace("row: the game refused to create it");
            return false;
        }

        views.Add(created);
        controller.ResetCursor();
        _rowAdded = true;

        Plugin.LogSource.LogInfo(
            $"[Menu] added the {HelperLabel} row ({views.Count} rows, " +
            $"{view.contentsParent.transform.childCount} views).");
        Plugin.Trace($"row added ({views.Count} rows)");

        return true;
    }

    // --------------------------------------------------------------- selection
    /// <summary>
    /// Fallback for the case where the prefix never sees the key: watch the highlighted row
    /// and open the helper on confirm while it is the last one.
    /// </summary>
    public static void PollMenuSelection()
    {
        if (HelperWindow.IsOpen)
            return;

        try
        {
            if (_controller == null || !RowPresent())
            {
                // a scene-wide search is not free, so do not run one every frame
                if (Time.frameCount % 10 != 0)
                    return;

                _controller = UnityEngine.Object.FindObjectOfType<Last.UI.CommandMenuController>();
                if (!RowPresent())
                    return;
            }

            var views = _controller.contents;
            var cursor = _controller.selectCursor;
            if (views == null || cursor == null || views.Count == 0)
                return;

            if (cursor.Index != views.Count - 1)
                return;

            if (!InputCompat.Down(GameKey.Confirm))
                return;

            Plugin.Trace($"confirm on helper row ({cursor.Index} of {views.Count})");
            OpenFromMenu();
        }
        catch (Exception e)
        {
            LogOnce("poll", $"[Menu] could not watch the helper row: {e.Message}");
        }
    }

    // -------------------------------------------------------------------- open
    private static void OpenFromMenu()
    {
        try
        {
            if (HelperWindow.IsOpen)
                return;

            Plugin.Trace("opening helper");

            var host = (Transform)null;
            var font = (Font)null;

            if (Plugin.HostInPauseMenu.Value)
                FindHost(out host, out font);

            if (host != null)
                SetMenuContentVisible(false);

            HelperWindow.Open(ClosedFromMenu, host, font);
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogError($"[Menu] could not open the helper: {e}");
            HelperWindow.Close();
        }
    }

    private static void ClosedFromMenu()
    {
        try
        {
            SetMenuContentVisible(true);
            Plugin.Trace("helper closed -> command list resumes");
        }
        catch (Exception e)
        {
            LogOnce("close-state", $"[Menu] could not restore the menu state: {e.Message}");
        }
    }

    private static void FindHost(out Transform host, out Font font)
    {
        host = null;
        font = null;

        try
        {
            var view = UnityEngine.Object.FindObjectOfType<KeyInputView>();
            if (view != null)
            {
                host = view.SubMenuParent != null ? view.SubMenuParent.transform : null;
                font = view.locationText != null ? view.locationText.font : null;
            }

            if (host == null)
            {
                var touch = UnityEngine.Object.FindObjectOfType<TouchView>();
                if (touch != null && touch.SubMenuRightParent != null)
                    host = touch.SubMenuRightParent.transform;
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"[Menu] could not find the sub-window area: {e.Message}");
            host = null;
        }
    }

    // ----------------------------------------------------------------- content
    private static GameObject _commandParent;
    private static GameObject _playerInfoParent;
    private static GameObject _keyHelpParent;
    private static KeyInputView _view;

    /// <summary>
    /// True while the pause menu's own content is hidden because of us. The menu can be
    /// dismissed with the helper still open, and closing that way is deliberately silent, so
    /// the content would otherwise stay hidden for good.
    /// </summary>
    private static bool _contentHidden;

    private static void SetMenuContentVisible(bool visible)
    {
        try
        {
            if (_view == null)
                _view = UnityEngine.Object.FindObjectOfType<KeyInputView>();

            if (_view == null)
                return;

            if (!visible)
            {
                _commandParent = _view.CommandMenuParent;
                _playerInfoParent = _view.PlayerInfoParent;
                _keyHelpParent = _view.KeyHelpParent;
            }

            Toggle(_commandParent, visible);
            Toggle(_playerInfoParent, visible);
            Toggle(_keyHelpParent, visible);

            _contentHidden = !visible;
        }
        catch (Exception e)
        {
            LogOnce("content", $"[Menu] could not show or hide the menu content: {e.Message}");
        }
    }

    public static void KeepMenuContentHidden() => SetMenuContentVisible(false);

    private static void Toggle(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible)
            target.SetActive(visible);
    }

    // ----------------------------------------------------------------- helpers

    private static readonly HashSet<string> LoggedOnce = new();

    private static void LogOnce(string key, string message)
    {
        if (LoggedOnce.Add(key))
            Plugin.LogSource?.LogWarning(message);
    }

    private static void SafePatch(Harmony harmony, Type type, string method,
        string prefix = null, string postfix = null)
    {
        try
        {
            var original = AccessTools.Method(type, method);
            if (original == null)
            {
                Plugin.LogSource.LogWarning($"[Menu] {type.Name}.{method} not found; that hook is skipped.");
                return;
            }

            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(MenuIntegration), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(MenuIntegration), postfix));

            Plugin.LogSource.LogInfo($"[Menu] patched {type.Name}.{method}");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[Menu] could not patch {type.Name}.{method}: {e.Message}");
        }
    }
}
