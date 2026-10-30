using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace FFVMissableHelper;

/// <summary>Lazily creates and shows the helper window.</summary>
internal static class HelperWindow
{
    private static HelperWindowBehaviour _instance;

    /// <summary>
    /// Plain managed state, on purpose. This is read from inside Harmony prefixes on game
    /// methods - and the game calls OnCommandMenuKeyAction in the field as well as in the
    /// menu. Asking a MonoBehaviour wrapper anything from in there (an
    /// UnityEngine.Object null check, a property) goes back through il2cpp and re-enters the
    /// patched method until the stack overflows.
    /// </summary>
    private static volatile bool _open;

    public static bool IsOpen => _open;

    internal static void SetOpen(bool open) => _open = open;

    private static volatile bool _suspendRequested;

    /// <summary>Managed-only: safe to call from inside a patched game method.</summary>
    public static void RequestSuspend() => _suspendRequested = true;

    public static bool TakeSuspendRequest()
    {
        if (!_suspendRequested)
            return false;

        _suspendRequested = false;
        return true;
    }

    public static void EnsureTypeRegistered()
    {
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<HelperWindowBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<HelperWindowBehaviour>();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not register the helper window: {e}");
        }
    }

    public static void EnsureCreated()
    {
        if (_instance != null)
        {
            try
            {
                if (!_instance.gameObject.activeSelf)
                    _instance.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning($"Could not wake the helper window: {e.Message}");
            }

            return;
        }

        try
        {
            var go = new GameObject("FFV Helper Window");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<HelperWindowBehaviour>();
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogError($"Could not create the helper window: {e.Message}");
        }
    }

    /// <summary>
    /// Puts the window to sleep while the pause menu is closed. The behaviour polls the menu
    /// row and watches the menu state every frame, and none of that means anything outside
    /// the pause menu - so outside it the plugin should be doing nothing at all.
    /// </summary>
    public static void Suspend()
    {
        try
        {
            if (_instance == null)
                return;

            if (_instance.IsShown)
                _instance.Hide(notify: false);

            if (_instance.gameObject.activeSelf)
                _instance.gameObject.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.LogSource?.LogWarning($"Could not suspend the helper window: {e.Message}");
        }
    }

    public static void Open(Action onClosed, Transform host = null, Font font = null)
    {
        EnsureCreated();
        _instance?.Show(onClosed, host, font);
    }

    public static void Close()
    {
        _instance?.Hide();
    }
}
